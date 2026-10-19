using Sora.Core;

internal static class FanFirmwareChecks
{
    public static void Run()
    {
        static void Assert(bool test, string message) { if (!test) throw new Exception(message); }
        var original = new FanStopConfig(false, 2, 0, 0);
        var target = new FanStopConfig(true, 0, 30, 35);
        for (int h = 0; h < 6; h++)
        {
            var fake = new FakeFirmware(original);
            FanFirmwareTransactions.Apply(fake, h, target);
            Assert(fake.Read(h) == target, "Firmware settings must read back");
            Assert(Enumerable.Range(0, 6).Where(i => i != h).All(i => fake.Read(i) == original), "Other headers changed");
            FanFirmwareTransactions.Restore(fake, h, original);
            Assert(fake.Read(h) == original, "Disabled zero-valued firmware defaults must restore");
            for (int failure = 1; failure <= 4; failure++)
            {
                var faulty = new FakeFirmware(original) { FailAt = failure };
                try { FanFirmwareTransactions.Apply(faulty, h, target); throw new Exception("Injected failure was ignored"); } catch (IOException) { }
                Assert(faulty.Read(h) == original, "Partial firmware update was not rolled back");
            }
            var mismatch = new FakeFirmware(original) { Mismatch = true };
            try { FanFirmwareTransactions.Apply(mismatch, h, target); throw new Exception("Mismatch was ignored"); } catch (IOException) { }
            Assert(mismatch.Read(h) == original, "Firmware mismatch rollback failed");
        }
        foreach (var config in new[] { new FanStopConfig(true, 6, 30, 35), new(true, 0, 30, 29), new(true, 0, 30, 50), new(true, 0, 0, 5) })
        {
            try { config.Validate(); throw new Exception("Invalid firmware stop accepted"); } catch (ArgumentException) { }
        }
        try { new FanProfile { Mode = FanCurveMode.Fixed, FirmwareStop = target }.Validate(); throw new Exception("Fixed duty accepted firmware stop"); } catch (ArgumentException) { }
    }
    private sealed class FakeFirmware(FanStopConfig initial) : IFanFirmwareAccess
    {
        private readonly FanStopConfig[] states = Enumerable.Repeat(initial, 6).ToArray();
        public int FailAt { get; init; }
        public bool Mismatch { get; set; }
        private int writes;
        public FanStopConfig Read(int header) => states[header];
        public void Write(int header, FanStopConfig config)
        {
            void Step() { if (++writes == FailAt) throw new IOException("Injected firmware write error"); }
            Step(); states[header] = states[header] with { Enabled = false };
            Step(); states[header] = states[header] with { TemperatureSource = config.TemperatureSource };
            Step(); states[header] = states[header] with { OffC = config.OffC, OnC = config.OnC };
            Step(); states[header] = states[header] with { Enabled = Mismatch ? !config.Enabled : config.Enabled }; Mismatch = false;
        }
        public void Dispose() { }
    }
}
