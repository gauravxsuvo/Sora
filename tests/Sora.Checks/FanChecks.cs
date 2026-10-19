using System.Text.Json;
using Sora.Core;

internal static class FanChecks
{
    public static void Run()
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException) { return; } throw new Exception(message); }
        var fixture = JsonSerializer.Deserialize<FanFixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "gigabyte-z890-fans.json")))!;
        Assert(fixture.Length == 6, "Six hardware fixture headers");
        var all = fixture.SelectMany(h => h.Registers).GroupBy(r => r.Register).ToDictionary(g => g.Key, g => g.First().Value);
        foreach (var h in fixture)
        {
            Assert(h.Registers.Select(r => r.Register).OrderBy(r => r.Bank).ThenBy(r => r.Address).SequenceEqual(FanProtocol.Registers(h.Header)), "Complete register fixture " + h.Header);
            var map = h.Registers.ToDictionary(r => r.Register, r => r.Value);
            var decoded = FanProtocol.Decode(h.Header, map);
            Assert(decoded.TemperatureSource == new[] { 2, 0, 0, 0, 2, 2 }[h.Header], "Captured GCC/BIOS temperature-source mapping");
            Assert(decoded.Points.Length == 7 && decoded.Points[^1].DutyPercent == 100, "Recovered BIOS curve " + h.Header);
            Assert(decoded.TemperatureSource == ((map[FanProtocol.ControlRegister(h.Header)] >> 3) & 7), "Temperature source is bits 5:3");
            var other = all.Where(kv => !FanProtocol.CanWrite(h.Header, kv.Key)).ToDictionary();
            foreach (var mode in Enum.GetValues<FanCurveMode>())
            {
                var points = Enumerable.Range(0, 11).Select(i => new FanPoint(20 + i * 7, 30 + i * 7)).ToArray();
                var profile = new FanProfile { Header = h.Header, Mode = mode, TemperatureSource = 4, Points = points, FixedDutyPercent = 72 };
                var fake = new FanMemory(all);
                FanTransactions.Apply(fake, profile);
                foreach (var (r, value) in other) Assert(fake.Read(r) == value, "Another header changed");
                var result = FanProtocol.Decode(h.Header, FanTransactions.Capture(fake, h.Header).ToDictionary(r => r.Register, r => r.Value));
                Assert(result.Mode == mode && result.TemperatureSource == 4, "Mode/source round trip");
                Assert((fake.Read(FanProtocol.ControlRegister(h.Header)) & 0x47) == (map[FanProtocol.ControlRegister(h.Header)] & 0x47), "PWM/DC and tachometer bits preserved");
                FanTransactions.Restore(fake, h.Header, h.Registers);
                Assert(all.All(kv => fake.Read(kv.Key) == kv.Value), "Exact original restored");
                // Every individual write failure must roll back every register, even after vector enables changed.
                var countFake = new FanMemory(all); FanTransactions.Apply(countFake, profile);
                for (int fail = 1; fail <= countFake.Writes; fail++)
                {
                    var faulty = new FanMemory(all) { FailWrite = fail };
                    try { FanTransactions.Apply(faulty, profile); throw new Exception("Write error was ignored"); } catch (IOException) { }
                    Assert(all.All(kv => faulty.Read(kv.Key) == kv.Value), "Partial-write rollback failed");
                }
            }
            // Readback can fail independently of the write transport; restore must still run.
            var mismatch = new FanMemory(all) { CorruptOnce = true };
            try { FanTransactions.Apply(mismatch, FanProfile.Preset(h.Header, "Full speed")); throw new Exception("Readback mismatch was ignored"); } catch (IOException) { }
            Assert(all.All(kv => mismatch.Read(kv.Key) == kv.Value), "Mismatch rollback");
            var stale = new FanMemory(all);
            FanTransactions.Apply(stale, new FanProfile { Header = h.Header, Points = [new(20, 30), new(85, 100)] });
            foreach (var v in FanProtocol.Vectors(h.Header)) Assert((stale.Read(new(v.Bank, v.Enable)) & v.EnableMask) == 0, "Trailing vector left enabled");
            Reject(() => FanTransactions.Restore(stale, h.Header, h.Registers[..^1]), "Incomplete backup accepted");
        }
        var known = FanProtocol.Compile(new FanProfile { Header = 0, TemperatureSource = 2, Points = [new(20, 20), new(40, 40), new(80, 100)] });
        Assert(known.Last().Value == 0x90 && known.Last().Mask == 0xB8, "CPU thermistor control byte 0x90 with mask B8");
        Assert(known.Single(w => w.Register == new FanRegister(0, 0x64)).Value == 2, "Main slope 51/20 -> integer 2");
        Assert(known.Single(w => w.Register == new FanRegister(2, 0x1C)).Value == 0x40, "Main slope fractional 4/8");
        foreach (var bad in new[]
        {
            new FanProfile { Header = -1 }, new FanProfile { TemperatureSource = 6 }, new FanProfile { Mode = (FanCurveMode)9 },
            new FanProfile { FixedDutyPercent = double.NaN }, new FanProfile { StopBelowC = 30 },
            new FanProfile { Points = [new(20, 60), new(40, 40), new(80, 100)] },
            new FanProfile { Points = [new(20, 30), new(20, 100)] }, new FanProfile { Points = [new(20, 30), new(80, 90)] },
            new FanProfile { Points = [new(20, double.PositiveInfinity), new(80, 100)] }
        }) Reject(() => FanProtocol.Compile(bad), "Invalid profile accepted");
        for (int h = 0; h < 6; h++)
            foreach (var r in new[] { new FanRegister(0, 0), new(0, 6), new(0, 0x58), new(1, 0x60), new(3, 0xFF) })
                Assert(!FanProtocol.CanWrite(h, r), "Non-fan register writable");
    }
    private sealed record FanFixture(int Header, string Name, FanRegisterValue[] Registers);
    private sealed class FanMemory(Dictionary<FanRegister, byte> source) : IFanRegisterAccess
    {
        private readonly Dictionary<FanRegister, byte> values = new(source);
        public int Writes { get; private set; }
        public int FailWrite { get; init; }
        public bool CorruptOnce { get; set; }
        public byte Read(FanRegister r) => values[r];
        public void Write(FanRegister r, byte value)
        {
            if (++Writes == FailWrite) throw new IOException("Injected write failure");
            values[r] = CorruptOnce ? (byte)(value ^ 1) : value;
            CorruptOnce = false;
        }
        public void Dispose() { }
    }
}
