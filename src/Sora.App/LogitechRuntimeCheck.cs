using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

internal static class LogitechRuntimeCheck
{
    internal static async Task<bool> RunAsync(string output)
    {
        var data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "logitech-runtime-check-data");
        var host = new SoraHost(data); LogitechSnapshot? before = null, after = null, applied = null; byte[]? beforeMap = null, afterMap = null;
        string? error = null; var settingsVerified = false; var screenVerified = false; var audioVerified = false; string? audioUnavailable = null;
        try
        {
            var info = LogitechDevice.EnumerateG502().Single();
            await using (var device = new LogitechDevice(info)) { before = await device.ReadAsync(); beforeMap = await device.ReadButtonMapAsync(before); }
            var profile = LogitechControlProfile.Create(1600, 500); await host.Logitech.ApplyAsync(profile);
            await using (var device = new LogitechDevice(info)) { applied = await device.ReadAsync(); }
            await Task.Delay(200);
            await using (var device = new LogitechDevice(info))
            {
                var settings = await device.ReadSettingsAsync(applied!);
                try
                {
                    await device.WriteSettingsAsync(applied!, settings, !settings.DpiAlwaysOn, !settings.StartupEffect);
                    var changed = await device.ReadSettingsAsync(applied!); settingsVerified = changed.DpiAlwaysOn != settings.DpiAlwaysOn && changed.StartupEffect != settings.StartupEffect;
                }
                finally
                {
                    var current = await device.ReadSettingsAsync(applied!); await device.WriteSettingsAsync(applied!, current, settings.DpiAlwaysOn, settings.StartupEffect);
                    var restored = await device.ReadSettingsAsync(applied!); settingsVerified &= restored.DpiAlwaysOn == settings.DpiAlwaysOn && restored.StartupEffect == settings.StartupEffect;
                }
            }
            var baselineLights = applied!.Lighting!;
            try
            {
                await host.LogitechLighting.SetAsync(new(1, "Screen sampler", "53D0EF", 50, "Whole screen"));
                await Task.Delay(150); screenVerified = host.LogitechLighting.Running;
            }
            finally { await host.LogitechLighting.ApplyFirmwareAsync(1, baselineLights.Single(z => z.Index == 1).State); }
            try
            {
                await host.LogitechLighting.SetAsync(new(0, "Audio visualizer", "53D0EF", 50, "Whole screen"));
                await Task.Delay(150); audioVerified = host.LogitechLighting.Running;
            }
            catch (System.Runtime.InteropServices.COMException e) { audioUnavailable = e.Message; }
            finally { await host.LogitechLighting.ApplyFirmwareAsync(0, baselineLights.Single(z => z.Index == 0).State); }
        }
        catch (Exception e) { error = e.ToString(); }
        finally
        {
            await host.LogitechLighting.StopAsync(); await host.Logitech.DisableAsync();
            try { await using var device = new LogitechDevice(LogitechDevice.EnumerateG502().Single()); after = await device.ReadAsync(); afterMap = await device.ReadButtonMapAsync(after); }
            catch (Exception e) { error = (error ?? "") + "\nRestoration audit: " + e; }
        }
        var passed = error == null && settingsVerified && screenVerified && (audioVerified || audioUnavailable != null) && before != null && after != null && applied?.OnboardMode == 2 && applied.Sensors[0].Dpi == 1600 && applied.ReportRate == 500 && before.OnboardMode == after.OnboardMode && before.Sensors[0].Dpi == after.Sensors[0].Dpi && before.ReportRate == after.ReportRate && beforeMap!.SequenceEqual(afterMap!) && before.Lighting!.All(z => after.Lighting!.Single(a => a.Index == z.Index).State.SequenceEqual(z.State));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed, error, settingsVerified, screenVerified, audioVerified, audioUnavailable, before, applied, after, beforeMap = beforeMap == null ? null : Convert.ToHexString(beforeMap), afterMap = afterMap == null ? null : Convert.ToHexString(afterMap), physicalHoldReleaseVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }
}
