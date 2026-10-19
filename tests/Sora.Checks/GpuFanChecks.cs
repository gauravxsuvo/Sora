using Sora.Core;

internal static class GpuFanChecks
{
    public static void Run()
    {
        GpuNativeFanChecks.Run();
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action, string message) { try { action(); } catch (ArgumentException) { return; } throw new Exception(message); }
        var profile = new GpuFanProfile { Mode = GpuFanMode.Curve };
        Assert(profile.DutyAt(40) == 35 && profile.DutyAt(70) == 70 && profile.DutyAt(90) == 100, "GPU curve interpolation and thermal ceiling");
        Assert((profile with { Mode = GpuFanMode.Fixed, FixedDutyPercent = 40 }).DutyAt(85) == 100, "Fixed mode cannot bypass thermal ceiling");
        Assert(profile.DutyAt(double.NaN) == 100, "Unknown temperature must never request low speed");
        Reject(() => (profile with { FixedDutyPercent = double.NaN }).Validate(), "Non-finite fixed duty accepted");
        Reject(() => (profile with { Points = [new(30, 50), new(25, 100)] }).Validate(), "Decreasing GPU temperatures accepted");
        Reject(() => (profile with { Points = [new(30, 70), new(50, 60), new(85, 100)] }).Validate(), "Decreasing GPU duty accepted");
        Reject(() => (profile with { Points = [new(30, 30), new(90, 100)] }).Validate(), "Unsafe curve ceiling accepted");
        var bytes = new byte[NvidiaGpuFans.ControlSize];
        static void Put(byte[] data, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);
        Put(bytes, 0, 0x10000 | NvidiaGpuFans.ControlSize); Put(bytes, 8, 3);
        for (int i = 0; i < 3; i++) { Put(bytes, 44 + i * 44, (uint)(i + 1)); Put(bytes, 44 + i * 44 + 4, 0); Put(bytes, 44 + i * 44 + 8, 0); Put(bytes, 44 + i * 44 + 12, 0xDEADCAFE); }
        var original = new GpuFanBackup("nvidia:bus2:5070ti", bytes);
        var snapshot = new GpuFanSnapshot(original.Identity, "RTX 5070 Ti", 2, 0, 0, 46, [new(1, 0, 0, 30, 100, 0), new(2, 0, 0, 30, 100, 0), new(3, 0, 0, 30, 100, 0)], true, "", DateTimeOffset.Now);
        var manual = NvidiaGpuFans.WithDuty(original, snapshot, 50);
        Assert(original.Control.SequenceEqual(bytes) && !NvidiaGpuFans.Equivalent(original, manual), "GPU target must not mutate original");
        for (int i = 0; i < 3; i++)
        {
            Assert(BitConverter.ToUInt32(manual.Control, 44 + i * 44 + 4) == 50 && BitConverter.ToUInt32(manual.Control, 44 + i * 44 + 8) == 1, "Each GPU fan channel must be programmed");
            Assert(BitConverter.ToUInt32(manual.Control, 44 + i * 44 + 12) == 0xDEADCAFE, "GPU reserved bytes must survive editing");
        }
        Reject(() => NvidiaGpuFans.WithDuty(original, snapshot, 0), "Unsupported firmware zero-RPM accepted");
        Reject(() => NvidiaGpuFans.WithDuty(original, snapshot, double.NaN), "Non-finite GPU control accepted");
        Reject(() => NvidiaGpuFans.WithDuty(original, snapshot with { Identity = "another-gpu" }, 50), "Foreign GPU backup accepted");
        var duplicate = (byte[])bytes.Clone(); Put(duplicate, 88, 1); Reject(() => NvidiaGpuFans.ValidateControl(duplicate), "Duplicate GPU fan IDs accepted");
        var unknown = (byte[])bytes.Clone(); Put(unknown, 52, 2); Reject(() => NvidiaGpuFans.ValidateControl(unknown), "Unknown GPU fan mode accepted");
        var automatic = NvidiaGpuFans.WithDuty(manual, snapshot, null);
        Assert(NvidiaGpuFans.Equivalent(original, automatic), "Driver automatic control restored");
        var changingAuto = (byte[])bytes.Clone(); Put(changingAuto, 48, 32);
        Assert(NvidiaGpuFans.Equivalent(original, original with { Control = changingAuto }), "Driver-managed auto duty changes must not break readback");
        foreach (int failure in new[] { 0, 1, 2, 4 })
        {
            using var fake = new Memory(original) { Failure = failure };
            try { GpuFanTransactions.Apply(fake, manual); Assert(failure == 0, "GPU injected failure ignored"); }
            catch (IOException) { Assert(failure != 0, "Unexpected GPU transaction failure"); }
            Assert(NvidiaGpuFans.Equivalent(failure == 0 ? manual : original, fake.Current), "GPU transaction did not roll back exactly");
            if (failure == 4) Assert(fake.Writes == 1, "Permission-denied no-change failure must not issue another setter");
        }
        using var noChange = new Memory(original) { Failure = 4 }; GpuFanTransactions.Apply(noChange, original); Assert(noChange.Writes == 0, "Restoring an already identical native state must not require a setter");
        using var broken = new Memory(original) { Failure = 3 };
        try { GpuFanTransactions.Apply(broken, manual); throw new Exception("GPU double failure swallowed"); }
        catch (AggregateException e) { Assert(e.InnerExceptions.Count == 2, "GPU rollback error lost"); }
    }
    private sealed class Memory(GpuFanBackup original) : IGpuFanAccess
    {
        public GpuFanBackup Current { get; private set; } = original;
        public int Failure { get; init; }
        private int writes;
        public int Writes => writes;
        public GpuFanSnapshot[] Read() => [];
        public GpuFanBackup Capture(string identity)
        {
            if (Failure is 2 or 3 && writes == 1) throw new IOException("Injected readback failure");
            return Current with { Control = (byte[])Current.Control.Clone() };
        }
        public void Write(GpuFanBackup control)
        {
            writes++; if (Failure == 4 && writes == 1) throw new IOException("Injected permission denial without changing hardware"); Current = control;
            if (Failure == 1 && writes == 1 || Failure == 3 && writes == 2) throw new IOException("Injected partial write failure");
        }
        public void Dispose() { }
    }
}
