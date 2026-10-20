using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Sora.Core;

namespace Sora.App;

// Explicit focused diagnostic: production input runtime, independent live DPI reads, original-state audit.
internal static class LogitechPhysicalCheck
{
    internal static async Task<bool> RunAsync(string output)
    {
        output = Path.GetFullPath(output);
        var data = output + ".data";
        if (File.Exists(output) || Directory.Exists(data)) throw new IOException("Choose a new evidence path.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var host = new SoraHost(data);
        LogitechSnapshot? before = null, after = null;
        byte[]? beforeMap = null, afterMap = null;
        LogitechHardwareSettings? beforeSettings = null, afterSettings = null;
        LogitechOnboardSlot[]? beforeSlots = null, afterSlots = null;
        var samples = new List<object>();
        var cycles = 0; var heldSamples = 0; var verifiedHold = false; var restored = false;
        string? error = null, restoreError = null;
        Window? window = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            if (LogitechDevice.IsGHubRunning()) throw new IOException("Exit G HUB before the physical test.");
            var info = LogitechDevice.EnumerateG502().Single();
            await using (var device = new LogitechDevice(info))
            {
                before = await device.ReadAsync(deadline.Token);
                beforeMap = await device.ReadButtonMapAsync(before, deadline.Token);
                beforeSettings = await device.ReadSettingsAsync(before);
                beforeSlots = await device.ReadOnboardAsync(before, deadline.Token);
                File.WriteAllText(output + ".backup.json", JsonSerializer.Serialize(new { before, beforeMap, beforeSettings, beforeSlots }, new JsonSerializerOptions { WriteIndented = true }));
            }
            var profile = LogitechControlProfile.Create(800, 1000) with { Name = "Physical recheck", ShiftDpi = 400 };
            await host.Logitech.ApplyAsync(profile);
            var status = new TextBlock { Text = "Hold G6 for two seconds, then release for two seconds. Repeat three times.", TextWrapping = TextWrapping.Wrap, FontSize = 19 };
            var detail = new TextBlock { Margin = new Thickness(0, 18, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 16 };
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(status); panel.Children.Add(detail);
            panel.Children.Add(new TextBlock { Text = "800 DPI normally; 400 DPI while G6 is held. Original mouse settings restore on completion, close or timeout. Keep G HUB closed.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0) });
            window = new Window { Title = "Sora · G502 DPI Shift physical check", Content = panel, Width = 600, Height = 310, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            window.Closed += (_, _) => deadline.Cancel();
            window.Show(); window.Activate();
            while (cycles < 3)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (!host.Logitech.Running || LogitechDevice.IsGHubRunning()) throw new IOException("Mouse runtime stopped or G HUB took control.");
                var pressed = host.Logitech.PressedButtons;
                var held = host.Logitech.DpiShiftHeld;
                var actualDpi = await host.Logitech.UseDeviceAsync(device => device.ReadLiveDpiAsync(host.Logitech.Snapshot!, deadline.Token));
                var stable = pressed == host.Logitech.PressedButtons && held == host.Logitech.DpiShiftHeld;
                samples.Add(new { time = DateTimeOffset.UtcNow, pressed, held, stable, actualDpi, runtimeDpi = host.Logitech.CurrentDpi });
                if (stable)
                {
                    var expected = held ? 400 : 800;
                    if (actualDpi != expected) throw new IOException($"Physical Shift DPI mismatch: expected {expected}, mouse returned {actualDpi}.");
                    if (held)
                    {
                        if ((pressed & (1 << 5)) == 0) throw new IOException("Shift state has no physical G6 press.");
                        heldSamples++;
                        if (heldSamples >= 2) verifiedHold = true;
                    }
                    else if (verifiedHold)
                    {
                        cycles++; heldSamples = 0; verifiedHold = false;
                    }
                    else { heldSamples = 0; }
                    detail.Text = $"Mouse readback: {actualDpi} DPI · G6 {(held ? "HELD" : "released")}\nVerified hold/release cycles: {cycles} / 3";
                }
                File.WriteAllText(output + ".samples.json", JsonSerializer.Serialize(samples));
                await Task.Delay(150, deadline.Token);
            }
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally
        {
            window?.Close();
            try
            {
                await host.Logitech.DisableAsync();
                if (before != null)
                {
                    await using var device = new LogitechDevice(LogitechDevice.EnumerateG502().Single());
                    after = await device.ReadAsync();
                    afterMap = await device.ReadButtonMapAsync(after);
                    afterSettings = await device.ReadSettingsAsync(after);
                    afterSlots = await device.ReadOnboardAsync(after);
                    restored = before.Identity == after.Identity && before.OnboardMode == after.OnboardMode && before.ReportRate == after.ReportRate &&
                        before.Sensors[0].Dpi == after.Sensors[0].Dpi && beforeMap!.SequenceEqual(afterMap) &&
                        before.Lighting!.All(z => after.Lighting!.Single(a => a.Index == z.Index).State.SequenceEqual(z.State)) &&
                        beforeSettings!.IndicatorNv.SequenceEqual(afterSettings.IndicatorNv) && beforeSettings.StartupNv.SequenceEqual(afterSettings.StartupNv) &&
                        beforeSlots!.Length == afterSlots.Length && beforeSlots.All(s => s.Raw.SequenceEqual(afterSlots.Single(a => a.Slot == s.Slot).Raw)) &&
                        !File.Exists(Path.Combine(data, "logitech", "control-ownership.json"));
                    if (!restored) throw new IOException("Original mouse mode/sensitivity/map/lights/preferences/onboard slots or ownership cleanup did not match.");
                }
            }
            catch (Exception exception) { restoreError = exception.ToString(); }
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = error == null && restoreError == null && cycles == 3 && restored,
                physicalHoldReleaseVerified = cycles == 3 && error == null, cycles, restored, error, restoreError,
                before, after, beforeMap, afterMap, beforeSettings, afterSettings, beforeSlots, afterSlots, samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return error == null && restoreError == null && cycles == 3 && restored;
    }
}
