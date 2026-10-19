namespace Sora.Core;

public sealed record LogitechDpiIndicatorState(byte Index, bool SoftwareControl, byte Mode, byte Level, bool AlwaysOn);

/// <summary>G502's 1300 stage display is separate from its 8070 RGB zone colour.</summary>
public static class LogitechDpiIndicator
{
    public static byte[] Encode(byte index, byte mode, int level)
    {
        if (index > 1 || mode is not (1 or 2) || level is < 0 or > 5) throw new InvalidDataException("Invalid G502 DPI indicator state.");
        // Native Feature1300LEDControl::SetLEDState: index + four big-endian words.
        return [index, 0, mode, 0, (byte)level, 0, 0, 0, 0];
    }

    public static (byte Mode, byte Level) Decode(byte index, ReadOnlySpan<byte> status)
    {
        // G HUB's getter reads mode from byte 1 and an optional display value from byte 3.
        // Firmware 174.0.5 returns zero for the display value even after a successful stage write.
        // Its status layout differs from the setter; replaying the getter bytes is invalid.
        if (status.Length < 4 || status[0] != index || status[1] is not (1 or 2) || status[3] > 5)
            throw new InvalidDataException("Unknown G502 DPI indicator status.");
        return (status[1], status[3]);
    }

    public static int Level(LogitechControlProfile profile, int dpi) =>
        Math.Clamp(Array.IndexOf(profile.Stages.Append(profile.ShiftDpi).Distinct().Order().ToArray(), dpi) + 1, 1, 5);

    public static async Task<LogitechDpiIndicatorState> ReadDpiIndicatorAsync(this LogitechDevice device, LogitechSnapshot snapshot, CancellationToken token = default)
    {
        var feature = snapshot.Features.Single(f => f.Id == 0x1300).Index;
        var count = await device.RequestAsync(feature, 0, token: token);
        if (count[0] is < 1 or > 2) throw new InvalidDataException("Unexpected G502 logical LED count.");
        for (byte index = 0; index < count[0]; index++)
        {
            var info = await device.RequestAsync(feature, 1, [index], token);
            if (info.Length < 6 || info[0] != index) throw new InvalidDataException("Incomplete G502 LED capabilities.");
            if (info[1] != 2) continue; // DPI display, not the separate profile indicator.
            if ((info[4] & 3) != 3) throw new InvalidDataException("G502 DPI display does not advertise On/Off.");
            var control = await device.RequestAsync(feature, 2, token: token);
            var status = Decode(index, await device.RequestAsync(feature, 4, [index], token));
            var nv = await device.RequestAsync(feature, 6, [index], token);
            return new(index, control[0] != 0, status.Mode, status.Level, (nv[1] & 6) == 2);
        }
        throw new NotSupportedException("No G502 DPI indicator was advertised.");
    }

    public static async Task SetDpiIndicatorControlAsync(this LogitechDevice device, LogitechSnapshot snapshot, bool enabled, CancellationToken token = default)
    {
        var feature = snapshot.Features.Single(f => f.Id == 0x1300).Index;
        await device.RequestAsync(feature, 3, [enabled ? (byte)1 : (byte)0], token);
        if ((await device.RequestAsync(feature, 2, token: token))[0] != (enabled ? 1 : 0)) throw new IOException("DPI indicator ownership readback differs.");
    }

    public static async Task SetDpiIndicatorAsync(this LogitechDevice device, LogitechSnapshot snapshot, byte index, int level, bool enabled, CancellationToken token = default)
    {
        var feature = snapshot.Features.Single(f => f.Id == 0x1300).Index;
        var mode = enabled ? (byte)2 : (byte)1;
        await device.RequestAsync(feature, 5, Encode(index, mode, level), token);
        var raw = await device.RequestAsync(feature, 4, [index], token);
        var actual = Decode(index, raw);
        if (actual.Mode != mode) throw new IOException($"DPI indicator readback differs: requested mode {mode}; received {Convert.ToHexString(raw)}.");
    }
}
