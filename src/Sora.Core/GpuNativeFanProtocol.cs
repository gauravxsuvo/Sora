namespace Sora.Core;

/// <summary>GCC GvDisplayA CNvDisplayAm's native driver protocol, gated to the observed GV-N507TWF3-16GD PCI identity.</summary>
public static class GpuNativeFanProtocol
{
    private static uint U32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
    private static void Put(byte[] data, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);
    public static void Validate(byte[] curve, byte[] stop)
    {
        if (curve == null || curve.Length != 220 || U32(curve, 0) != 0x200DC || U32(curve, 4) != 3 || stop == null || stop.Length != 292 || U32(stop, 0) != 0x10124 || U32(stop, 4) != 3) throw new ArgumentException("Invalid native GPU fan backup size, version or channel count.");
        for (int fan = 0; fan < 3; fan++)
        {
            if (U32(curve, 20 + fan * 52) != fan || U32(stop, 36 + fan * 8) != fan || (U32(stop, 40 + fan * 8) & ~1u) != 0) throw new ArgumentException("Unknown native GPU fan mapping or policy bits.");
            double previousC = -1; uint previousRpm = 0;
            for (int point = 0; point < 3; point++)
            {
                int offset = 24 + fan * 52 + point * 12; double temperature = U32(curve, offset) / 256d; uint duty = U32(curve, offset + 4), rpm = U32(curve, offset + 8);
                if (temperature < previousC || temperature > 125 || duty > 65536 || rpm > 10000 || rpm < previousRpm) throw new ArgumentException("Invalid native GPU RPM curve.");
                previousC = temperature; previousRpm = rpm;
            }
        }
    }
    public static GpuHardwareFanState Decode(byte[] curve, byte[] stop)
    {
        Validate(curve, stop);
        var fans = Enumerable.Range(0, 3).Select(fan => new GpuHardwareFan((uint)fan, Enumerable.Range(0, 3).Select(point =>
        {
            int offset = 24 + fan * 52 + point * 12;
            return new GpuRpmPoint(U32(curve, offset) / 256d, U32(curve, offset + 8));
        }).ToArray(), (U32(stop, 40 + fan * 8) & 1) != 0)).ToArray();
        return new(fans, 3000, true, "Three-point native RPM curves. Auto fan stop uses the GPU's existing firmware temperature thresholds.");
    }
    public static GpuFanBackup Compile(GpuFanBackup original, GpuFanSnapshot snapshot, GpuFanProfile profile)
    {
        profile.Validate();
        if (profile.Mode is not (GpuFanMode.HardwareCurve or GpuFanMode.HardwareFixed) || snapshot.Hardware?.CanProgram != true || original.FirmwareCurve == null || original.FirmwareStop == null) throw new ArgumentException("Native GPU curve control is unavailable.");
        Validate(original.FirmwareCurve, original.FirmwareStop);
        var automatic = NvidiaGpuFans.WithDuty(original, snapshot, null, profile.FanIndices.Select(i => i + 1).ToHashSet());
        var curve = (byte[])original.FirmwareCurve.Clone(); var stop = (byte[])original.FirmwareStop.Clone();
        // Preserve the full native header, reserved fields and unused fourth controller. Only three observed channels are edited.
        for (int fan = 0; fan < 3; fan++)
        {
            if (!profile.FanIndices.Contains((uint)fan)) continue;
            for (int point = 0; point < 3; point++)
            {
                int offset = 24 + fan * 52 + point * 12;
                double temperature = profile.Mode == GpuFanMode.HardwareFixed ? new[] { 30d, 84d, 85d }[point] : profile.RpmPoints[point].TemperatureC;
                uint rpm = profile.Mode == GpuFanMode.HardwareFixed && point < 2 ? profile.FixedRpm : profile.Mode == GpuFanMode.HardwareFixed ? 3000 : profile.RpmPoints[point].Rpm;
                Put(curve, offset, (uint)Math.Round(temperature * 256));
                // GCC rounds the UI percentage to an integer before encoding Q16, while retaining the explicit RPM target.
                Put(curve, offset + 4, (uint)Math.Round(rpm / 30d) * 65536 / 100); Put(curve, offset + 8, rpm);
            }
            Put(stop, 40 + fan * 8, profile.Mode == GpuFanMode.HardwareCurve && profile.AutoFanStop ? 1u : 0u);
        }
        // Current values rather than factory-default selector (last DWORD bit 0).
        Put(curve, 216, U32(curve, 216) & ~1u);
        Validate(curve, stop); return automatic with { FirmwareCurve = curve, FirmwareStop = stop };
    }
}
