using System.Text.Json;
using Sora.Core;

internal static class GpuNativeFanChecks
{
    public static void Run()
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action, string message) { try { action(); } catch (ArgumentException) { return; } throw new Exception(message); }
        var original = JsonSerializer.Deserialize<GpuFanBackup>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "gigabyte-5070ti-fans.json")))!;
        NvidiaGpuFans.ValidateBackup(original);
        var hardware = GpuNativeFanProtocol.Decode(original.FirmwareCurve!, original.FirmwareStop!);
        Assert(hardware.Fans.Length == 3 && hardware.Fans.All(f => f.ZeroRpmEnabled), "Three independently reported firmware stop policies");
        Assert(hardware.Fans.All(f => f.Points.SequenceEqual(new GpuRpmPoint[] { new(55, 998), new(76, 2400), new(84, 3000) })), "Actual WINDFORCE firmware curve decoding");
        var snapshot = new GpuFanSnapshot(original.Identity, "RTX 5070 Ti", 2, 0x2C0510DE, 0x41A81458, 46, [new(1, 0, 0, 30, 100, 0), new(2, 0, 0, 30, 100, 0), new(3, 0, 0, 30, 100, 0)], true, "", DateTimeOffset.Now) { Hardware = hardware };
        var profile = new GpuFanProfile { Mode = GpuFanMode.HardwareCurve, AutoFanStop = false, RpmPoints = [new(50, 1200), new(70, 2100), new(84, 3000)] };
        var target = GpuNativeFanProtocol.Compile(original, snapshot, profile);
        var decoded = GpuNativeFanProtocol.Decode(target.FirmwareCurve!, target.FirmwareStop!);
        Assert(decoded.Fans.All(f => !f.ZeroRpmEnabled && f.Points.SequenceEqual(profile.RpmPoints)), "Native three-point RPM curve and auto-stop compilation");
        Assert(NvidiaGpuFans.Equivalent(original, original with { Control = (byte[])original.Control.Clone(), FirmwareCurve = (byte[])original.FirmwareCurve!.Clone(), FirmwareStop = (byte[])original.FirmwareStop!.Clone() }), "Native backup comparison");
        Assert(!NvidiaGpuFans.Equivalent(original, target), "Native curve readback differences cannot be ignored");
        var manual = NvidiaGpuFans.WithDuty(original, snapshot, 50);
        var partial = GpuNativeFanProtocol.Compile(manual, snapshot, profile with { FanIndices = [1] });
        for (int fan = 0; fan < 3; fan++)
        {
            if (fan != 1)
            {
                Assert(original.FirmwareCurve!.AsSpan(8 + fan * 52, 52).SequenceEqual(partial.FirmwareCurve!.AsSpan(8 + fan * 52, 52)), "Partial GPU apply modified another firmware curve");
                Assert(manual.Control.AsSpan(44 + fan * 44, 44).SequenceEqual(partial.Control.AsSpan(44 + fan * 44, 44)), "Partial native apply modified another manual fan controller");
            }
        }
        var fixedTarget = GpuNativeFanProtocol.Compile(original, snapshot, profile with { Mode = GpuFanMode.HardwareFixed, FixedRpm = 1500 });
        Assert(GpuNativeFanProtocol.Decode(fixedTarget.FirmwareCurve!, fixedTarget.FirmwareStop!).Fans.All(f => f.Points.SequenceEqual(new GpuRpmPoint[] { new(30, 1500), new(84, 1500), new(85, 3000) }) && !f.ZeroRpmEnabled), "Native fixed RPM retains a hardware thermal ceiling and disables stop");
        var corrupt = (byte[])original.FirmwareCurve!.Clone(); BitConverter.GetBytes(259u).CopyTo(corrupt, 4);
        Reject(() => GpuNativeFanProtocol.Validate(corrupt, original.FirmwareStop!), "Full native count DWORD was not validated");
        Reject(() => NvidiaGpuFans.ValidateBackup(original with { FirmwareStop = null }), "Incomplete native backup accepted");
        for (int failure = 1; failure <= 3; failure++)
        {
            using var fake = new NativeMemory(original, failure);
            try { GpuFanTransactions.Apply(fake, target); throw new Exception("Partial native write failure ignored"); } catch (IOException) { }
            Assert(NvidiaGpuFans.Equivalent(original, fake.Current), "Partial native curve/stop/control write did not restore original");
        }
        string gcc = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "gigabyte-5070ti-gcc-profile.json"));
        var imported = GpuGccFanProfiles.Import(gcc, "GCC import");
        Assert(imported.Mode == GpuFanMode.HardwareCurve && imported.FanIndices.SequenceEqual(new uint[] { 0, 1, 2 }) && imported.RpmPoints.SequenceEqual(hardware.Fans[0].Points), "Installed GCC JSON profile imports by channel ID");
        Assert(GpuGccFanProfiles.Import(gcc, "Single fan", 1).FanIndices.SequenceEqual(new uint[] { 1 }), "Individual GCC fan profile import");
        Reject(() => GpuGccFanProfiles.Import("[]", "Invalid"), "Empty GCC profile accepted");
        Reject(() => GpuGccFanProfiles.Import(gcc.Replace("\"FanIndex\":2", "\"FanIndex\":5", StringComparison.Ordinal), "Invalid"), "Foreign GCC fan index accepted");
    }
    private sealed class NativeMemory(GpuFanBackup original, int failure) : IGpuFanAccess
    {
        public GpuFanBackup Current { get; private set; } = original;
        private int calls;
        public GpuFanSnapshot[] Read() => [];
        public GpuFanBackup Capture(string identity) => Current;
        public void Write(GpuFanBackup target)
        {
            calls++; Current = Current with { FirmwareCurve = target.FirmwareCurve }; if (calls == 1 && failure == 1) throw new IOException("Native curve write failed after changing hardware");
            Current = Current with { FirmwareStop = target.FirmwareStop }; if (calls == 1 && failure == 2) throw new IOException("Native fan stop write failed after changing hardware");
            Current = target; if (calls == 1 && failure == 3) throw new IOException("Client control write failed after changing hardware");
        }
        public void Dispose() { }
    }
}
