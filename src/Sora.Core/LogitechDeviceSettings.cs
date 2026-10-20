using System.Text;

namespace Sora.Core;

public sealed record LogitechHardwareSettings(bool DpiAlwaysOn, bool StartupEffect, string Firmware, byte[] IndicatorNv, byte[] IndicatorState, byte[] StartupNv);

public static class LogitechDeviceSettings
{
    public static async Task<LogitechHardwareSettings> ReadSettingsAsync(this LogitechDevice device, LogitechSnapshot snapshot)
    {
        if (!LogitechModels.HasG502Preferences(snapshot)) throw new NotSupportedException("Device preferences are unavailable for this model.");
        var indicator = snapshot.Features.Single(f => f.Id == 0x1300).Index; var light = snapshot.Features.Single(f => f.Id == 0x8070).Index;
        var nv = await device.RequestAsync(indicator, 6, [0]); var state = await device.RequestAsync(indicator, 4, [0]);
        var boot = await device.RequestAsync(light, 4, [0, 1]);
        if (nv.Length < 3 || state.Length < 9 || boot.Length != 16 || nv[0] != 0 || boot[0] != 0 || boot[1] != 1 || (nv[1] & 6) is not (2 or 4)) throw new InvalidDataException("The mouse returned an unknown indicator/startup setting.");
        var firmwareBytes = await device.RequestAsync(snapshot.Features.Single(f => f.Id == 3).Index, 1, [0]);
        if (firmwareBytes.Length < 8) throw new InvalidDataException("Incomplete firmware information.");
        var prefix = Encoding.ASCII.GetString(firmwareBytes, 1, 3).TrimEnd('\0');
        var firmware = prefix == "U1" ? $"1{firmwareBytes[4]:X2}.{firmwareBytes[5]:X}.{(firmwareBytes[6] << 8 | firmwareBytes[7])}" : $"{prefix} {firmwareBytes[4]:X2}.{firmwareBytes[5]:X2} · build {(firmwareBytes[6] << 8 | firmwareBytes[7])}";
        return new((nv[1] & 6) == 2, boot[2] != 2, firmware, nv, state, boot);
    }
    public static async Task WriteSettingsAsync(this LogitechDevice device, LogitechSnapshot snapshot, LogitechHardwareSettings expected, bool alwaysOn, bool startup, int? indicatorLevel = null)
    {
        if (LogitechDevice.IsGHubRunning()) throw new InvalidOperationException("Exit G HUB before applying device settings.");
        var before = await device.ReadSettingsAsync(snapshot);
        if (before.DpiAlwaysOn != expected.DpiAlwaysOn || before.StartupEffect != expected.StartupEffect) throw new InvalidOperationException("Device settings changed outside Sora. Refresh before applying.");
        var indicator = snapshot.Features.Single(f => f.Id == 0x1300).Index; var light = snapshot.Features.Single(f => f.Id == 0x8070).Index;
        var oldState = LogitechDpiIndicator.Decode(0, before.IndicatorState);
        var level = indicatorLevel ?? Math.Max(1, (int)oldState.Level);
        if (level is < 1 or > 5) throw new InvalidDataException("Invalid G502 DPI display level.");
        async Task Indicator(byte[] nv, bool restore = false)
        {
            var ownership = await device.ReadDpiIndicatorAsync(snapshot);
            try
            {
                await device.SetDpiIndicatorControlAsync(snapshot, true);
                await device.RequestAsync(indicator, 7, nv[..3]);
                await device.SetDpiIndicatorAsync(snapshot, 0, level, restore ? oldState.Mode == 2 : true);
            }
            finally
            {
                if (!ownership.SoftwareControl) await device.SetDpiIndicatorControlAsync(snapshot, false);
            }
        }
        async Task Startup(byte[] nv) { await device.RequestAsync(light, 5, nv[..16]); }
        try
        {
            if (alwaysOn != before.DpiAlwaysOn) { var nv = before.IndicatorNv.ToArray(); nv[1] = (byte)((nv[1] & ~6) | (alwaysOn ? 2 : 4)); await Indicator(nv); }
            if (startup != before.StartupEffect) { var nv = before.StartupNv.ToArray(); nv[2] = startup ? (byte)1 : (byte)2; await Startup(nv); }
            var after = await device.ReadSettingsAsync(snapshot);
            if (after.DpiAlwaysOn != alwaysOn || after.StartupEffect != startup) throw new IOException("Device settings readback failed.");
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            try { await Indicator(before.IndicatorNv, true); } catch (Exception restore) { failures.Add(restore); }
            try { await Startup(before.StartupNv); } catch (Exception restore) { failures.Add(restore); }
            try
            {
                var restored = await device.ReadSettingsAsync(snapshot);
                if (restored.DpiAlwaysOn != before.DpiAlwaysOn || restored.StartupEffect != before.StartupEffect) failures.Add(new IOException("Original preferences did not read back."));
            }
            catch (Exception restore) { failures.Add(restore); }
            if (failures.Count > 0) throw new IOException("Device settings failed and restoration needs attention: " + string.Join("; ", failures.Select(e => e.Message)), error);
            throw;
        }
    }
}
