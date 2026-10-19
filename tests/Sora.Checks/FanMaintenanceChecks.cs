using System.Text.Json;
using Sora.Core;

internal static class FanMaintenanceChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or ArgumentException or IOException) { return; }
        throw new Exception("Invalid maintenance state was accepted.");
    }
    public static void Run()
    {
        var fixture = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "gigabyte-z890-fans.json")))!;
        var baseline = new FanSnapshot(GigabyteFans.SupportedBoard, FanMaintenance.Controller, DateTimeOffset.UtcNow,
            [35, 40, 45, 42, 46, 35], fixture.Select(h => new FanHeaderState(h.Header, h.Name, h.Header is 0 or 5 ? 2000 : 0,
                (h.Registers.Single(r => r.Register == FanProtocol.ControlRegister(h.Header)).Value & 0x80) != 0,
                FanProtocol.Decode(h.Header, h.Registers.ToDictionary(r => r.Register, r => r.Value)), h.Registers)
            { FirmwareStop = new(false, 2, 0, 0) }).ToArray()) { FirmwareInterface = FanMaintenance.FirmwareInterface };
        FanMaintenance.RequireCooling(baseline);
        var registers = baseline.Headers.SelectMany(h => h.Registers).ToDictionary(r => r.Register, r => r.Value);
        var reordered = baseline with { Headers = baseline.Headers.Reverse().Select(h => h with { Registers = h.Registers.Reverse().ToArray() }).ToArray() };
        Require(FanMaintenance.Matches(baseline, reordered), "Comparison must bind by header/register, not array order.");
        foreach (var invalid in new[]
        {
            baseline with { Board = "Other motherboard" }, baseline with { Controller = "Other controller" },
            baseline with { FirmwareInterface = "GSA1_ACPIMethod" }, baseline with { Headers = baseline.Headers[..5] },
            baseline with { Headers = [baseline.Headers[0], .. baseline.Headers[..5]] },
            baseline with { Headers = baseline.Headers.Select(h => h.Header == 5 ? h with { Registers = h.Registers[..^1] } : h).ToArray() },
            baseline with { Headers = baseline.Headers.Select(h => h.Header == 5 ? h with { FirmwareStop = null } : h).ToArray() },
            baseline with { Headers = baseline.Headers.Select(h => h.Header == 5 ? h with { Automatic = !h.Automatic } : h).ToArray() },
            baseline with { Headers = baseline.Headers.Select(h => h.Header == 5 ? h with { Registers = [new(0, 0, 42), .. h.Registers[1..]] } : h).ToArray() }
        })
        {
            var native = new Native(registers); var firmware = new Firmware();
            Reject(() => FanMaintenance.Restore(native, firmware, invalid));
            Require(native.Writes == 0 && firmware.Writes == 0, "Malformed late header allowed an earlier hardware write.");
        }
        foreach (var invalid in new[]
        {
            baseline with { TemperaturesC = [35, 40, null, 42, 46, 35] },
            baseline with { TemperaturesC = [35, 40, 75, 42, 46, 35] },
            baseline with { TemperaturesC = [35, 40, double.NaN, 42, 46, 35] },
            baseline with { Headers = baseline.Headers.Select(h => h.Header == 0 ? h with { Rpm = 0 } : h).ToArray() },
            baseline with { Headers = baseline.Headers.Select(h => h.Header == 0 ? h with { Rpm = double.PositiveInfinity } : h).ToArray() },
            baseline with { Headers = baseline.Headers.Select(h => h.Header == 5 ? h with { FirmwareStop = new(true, 2, 30, 35) } : h).ToArray() }
        }) Reject(() => FanMaintenance.RequireCooling(invalid));

        var unchanged = new Native(registers); var unchangedFirmware = new Firmware();
        FanMaintenance.Restore(unchanged, unchangedFirmware, baseline);
        Require(unchanged.Writes == 0 && unchangedFirmware.Writes == 0, "Unchanged configuration must not be rewritten.");

        var drift = registers.ToDictionary(p => p.Key, p => (byte)(p.Value ^ 1));
        var successful = new Native(drift); var successfulFirmware = new Firmware(drift: true);
        FanMaintenance.Restore(successful, successfulFirmware, reordered);
        Require(registers.All(p => successful.Read(p.Key) == p.Value), "Complete native restoration failed.");
        Require(successfulFirmware.AllOriginal, "Complete firmware restoration failed.");
        foreach (var header in Enumerable.Range(0, 6))
            Require(successful.Written.Where(r => FanProtocol.CanWrite(header, r)).Last() == FanProtocol.ControlRegister(header), "Control/source must restore after coefficients.");

        // Inject a transient failure at EVERY native write, not just the two observed StartPWM bytes.
        for (var failure = 1; failure <= successful.Writes; failure++)
        {
            var native = new Native(drift) { FailAt = failure }; var firmware = new Firmware(drift: true);
            try { FanMaintenance.Restore(native, firmware, baseline); throw new Exception("Native failure was hidden."); }
            catch (AggregateException) { }
            Require(firmware.AllOriginal, "Native failure skipped independent firmware recovery.");
            var badHeader = baseline.Headers.Single(h => FanProtocol.CanWrite(h.Header, successful.Written[failure - 1])).Header;
            Require(baseline.Headers.Where(h => h.Header != badHeader).SelectMany(h => h.Registers).All(r => native.Read(r.Register) == r.Value), "Native failure skipped other headers.");
            FanMaintenance.Restore(native, firmware, baseline);
            Require(registers.All(p => native.Read(p.Key) == p.Value), "Retry did not recover the preserved original state.");
        }
        for (var failure = 1; failure <= 6; failure++)
        {
            var native = new Native(drift); var firmware = new Firmware(drift: true) { FailAt = failure };
            try { FanMaintenance.Restore(native, firmware, baseline); throw new Exception("Firmware failure was hidden."); }
            catch (AggregateException) { }
            Require(registers.All(p => native.Read(p.Key) == p.Value), "Firmware failure skipped native headers.");
            FanMaintenance.Restore(native, firmware, baseline);
            Require(firmware.AllOriginal, "Firmware retry did not recover original settings.");
        }
        var mismatch = new Native(drift) { CorruptAt = 1 };
        try { FanMaintenance.Restore(mismatch, new Firmware(), baseline); throw new Exception("Readback mismatch was hidden."); }
        catch (AggregateException) { }
        FanMaintenance.Restore(mismatch, new Firmware(), baseline);
        Require(registers.All(p => mismatch.Read(p.Key) == p.Value), "Mismatch recovery failed.");
    }
    private sealed record Fixture(int Header, string Name, FanRegisterValue[] Registers);
    private sealed class Native(Dictionary<FanRegister, byte> initial) : IFanRegisterAccess
    {
        private readonly Dictionary<FanRegister, byte> values = new(initial);
        public int Writes { get; private set; }
        public int FailAt { get; init; }
        public int CorruptAt { get; init; }
        public List<FanRegister> Written { get; } = [];
        public byte Read(FanRegister r) => values[r];
        public void Write(FanRegister r, byte value)
        {
            Written.Add(r);
            if (++Writes == FailAt) throw new IOException("Injected native write failure.");
            values[r] = Writes == CorruptAt ? (byte)(value ^ 1) : value;
        }
        public void Dispose() { }
    }
    private sealed class Firmware(bool drift = false) : IFanFirmwareAccess
    {
        private readonly FanStopConfig[] values = Enumerable.Repeat(drift ? new(false, 1, 1, 2) : new FanStopConfig(false, 2, 0, 0), 6).ToArray();
        public int FailAt { get; init; }
        public int Writes { get; private set; }
        public bool AllOriginal => values.All(v => v == new FanStopConfig(false, 2, 0, 0));
        public FanStopConfig Read(int h) => values[h];
        public void Write(int h, FanStopConfig v)
        {
            if (values[h] == v) return;
            if (++Writes == FailAt) { values[h] = values[h] with { TemperatureSource = v.TemperatureSource }; throw new IOException("Injected partial firmware failure."); }
            values[h] = v;
        }
        public void Dispose() { }
    }
}
