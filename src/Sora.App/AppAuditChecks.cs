using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sora.Core;

namespace Sora.App;

// Isolated regression checks: no host.Start, HID writes, driver controls or startup-task changes.
internal static class AppAuditChecks
{
    internal static async Task<bool> RunAsync(string output)
    {
        var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "regression-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.Delete(output + ".errors.txt");
        SoraData.UseIsolatedPreviewDirectory(root, false);
        var results = new Dictionary<string, bool>();
        async Task Check(string name, Func<Task> action)
        {
            try { await action(); results[name] = true; }
            catch (Exception error) { results[name] = false; File.AppendAllText(output + ".errors.txt", name + ": " + error + Environment.NewLine); }
        }
        static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Regression assertion failed."); }
        await Check("Legacy preferences retain values and default to dark mode", () =>
        {
            var legacy = JsonSerializer.Deserialize<DisplaySettings>("{\"Fahrenheit\":true,\"LeadingZeros\":true,\"MinimizeToTray\":false,\"AutoStartDisplay\":true}")!;
            Assert(legacy == new DisplaySettings(true, true, false, true, true)); return Task.CompletedTask;
        });
        await Check("Appearance and cooler/tray preferences survive a new host", async () =>
        {
            var directory = Path.Combine(root, "preferences"); var host = new SoraHost(directory);
            var saved = new DisplaySettings(true, true, false, true, false);
            await host.SaveSettingsAsync(saved);
            Assert(new SoraHost(directory).Settings == saved);
            await host.SaveSettingsAsync(saved with { DarkMode = true });
            Assert(new SoraHost(directory).Settings == saved with { DarkMode = true });
        });
        await Check("Failed saves preserve both previous memory and disk settings", async () =>
        {
            var directory = Path.Combine(root, "save-failure"); var host = new SoraHost(directory);
            await host.SaveSettingsAsync(new()); var before = File.ReadAllBytes(Path.Combine(directory, "settings.json"));
            Directory.CreateDirectory(Path.Combine(directory, "settings.json.tmp"));
            var failed = false;
            try { await host.SaveSettingsAsync(new(true, true, false, true, false)); } catch (IOException) { failed = true; }
            Assert(failed && host.Settings == new DisplaySettings() && before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "settings.json"))));
        });
        await Check("Fan modes, curves, names, device types and startup/exit choices reload", () =>
        {
            var directory = Path.Combine(root, "fans"); Directory.CreateDirectory(directory);
            var preferences = new FanPreferences { ApplyAtStartup = false, RestoreOnExit = false,
                Profiles = new() { [0] = FanProfile.Preset(0, "Quiet"), [1] = new FanProfile { Header = 1, Mode = FanCurveMode.Fixed, FixedDutyPercent = 62 } },
                HeaderNames = new() { [0] = "Radiator" }, HeaderDevices = new() { [0] = FanDeviceKind.Fan } };
            File.WriteAllText(Path.Combine(directory, "fans.json"), JsonSerializer.Serialize(preferences));
            var actual = new SoraHost(directory).Fans.Preferences;
            Assert(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(preferences)); return Task.CompletedTask;
        });
        await Check("GPU named profiles reload and an invalid library is preserved for recovery", () =>
        {
            var directory = Path.Combine(root, "gpu-library"); var library = new GpuFanProfileLibrary(directory);
            var profile = new GpuFanProfile { Name = "Quiet saved", Mode = GpuFanMode.Fixed, FixedDutyPercent = 47 };
            library.Save("gpu", profile);
            Assert(new GpuFanProfileLibrary(directory).Read("gpu").Single().FixedDutyPercent == 47);
            var path = Path.Combine(directory, "gpu-fans-profiles.json"); const string invalid = "{\"Version\":1,\"Adapters\":null}"; File.WriteAllText(path, invalid);
            var rejected = false; try { library.Save("gpu", profile); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && File.ReadAllText(path) == invalid); return Task.CompletedTask;
        });
        await Check("Null GPU backup entries block controls without crashing startup", () =>
        {
            var directory = Path.Combine(root, "gpu-corrupt"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "gpu-fans-original.json"), "[null]");
            var service = new SoraHost(directory).GpuFans; service.Load();
            Assert(service.Message.Contains("blocked", StringComparison.Ordinal)); return Task.CompletedTask;
        });
        await Check("Invalid fan preferences fall back without preventing startup", () =>
        {
            var directory = Path.Combine(root, "fans-corrupt"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "fans.json"), "{\"Profiles\":null}");
            var fans = new SoraHost(directory).Fans;
            Assert(fans.Preferences.Profiles.Count == 0 && fans.Message.Contains("Cannot load", StringComparison.Ordinal)); return Task.CompletedTask;
        });
        await Check("Null mouse firmware states and reactive entries are rejected without crashing", () =>
        {
            var directory = Path.Combine(root, "mouse-corrupt"); Directory.CreateDirectory(Path.Combine(directory, "logitech"));
            File.WriteAllText(Path.Combine(directory, "logitech", "firmware-lighting.json"), "{\"Identity\":\"mouse\",\"States\":null}");
            File.WriteAllText(Path.Combine(directory, "logitech", "reactive-lighting.json"), "[null]");
            Assert(new SoraHost(directory).LogitechLighting.Zones.Length == 0); return Task.CompletedTask;
        });
        await Check("Mouse control enable choice reloads; failed disable leaves previous choice intact", async () =>
        {
            var directory = Path.Combine(root, "mouse-enabled"); var mouseDirectory = Path.Combine(directory, "logitech"); Directory.CreateDirectory(mouseDirectory);
            var path = Path.Combine(mouseDirectory, "control-enabled.json"); File.WriteAllText(path, "true");
            var host = new SoraHost(directory); Assert(host.Logitech.Enabled);
            Directory.CreateDirectory(path + ".tmp"); var failed = false;
            try { await host.Logitech.DisableAsync(); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
            Assert(failed && host.Logitech.Enabled && new SoraHost(directory).Logitech.Enabled);
            Directory.Delete(path + ".tmp"); await host.Logitech.DisableAsync();
            Assert(!host.Logitech.Enabled && !new SoraHost(directory).Logitech.Enabled);
        });
        await Check("Mouse recovery detects lost RGB, respects identity, and ignores streaming zones", () =>
        {
            byte[] color = LogitechLighting.Encode(1, "AA44FF", 5000, 80);
            var states = new Dictionary<byte, byte[]> { [0] = color };
            var fresh = new LogitechSnapshot("G502 HERO", "mouse", 0xc08b, "2.0", [], [], 1000, [1000], 2, [], [new(0, "DPI", [0, 1, 3, 10], color)]);
            Assert(LogitechReactiveLighting.FirmwareMatches("mouse", states, [], fresh));
            var changed = fresh with { Lighting = [new(0, "DPI", [0, 1, 3, 10], LogitechLighting.Encode(1, "FFFFFF", 5000, 100))] };
            Assert(!LogitechReactiveLighting.FirmwareMatches("mouse", states, [], changed));
            Assert(!LogitechReactiveLighting.FirmwareMatches("other mouse", states, [], fresh));
            Assert(LogitechReactiveLighting.FirmwareMatches("mouse", states, [new(0, "Screen sampler", "AA44FF", 80, "Whole screen")], changed));
            return Task.CompletedTask;
        });
        await Check("Theme switches update existing controls and restore the light palette", async () =>
        {
            ThemeManager.Apply(false);
            var label = new TextBlock(); label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var window = new Window { Content = label, Left = -12000, ShowInTaskbar = false, Width = 100, Height = 100 };
            window.Show();
            await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var light = ((SolidColorBrush)label.Foreground).Color;
            try
            {
                ThemeManager.Apply(true);
                await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert(((SolidColorBrush)label.Foreground).Color != light);
                Assert(((SolidColorBrush)Application.Current.FindResource("WindowBrush")).Color == Colors.Black);
                Assert(((SolidColorBrush)Application.Current.FindResource("SidebarBrush")).Color == Colors.Black);
                ThemeManager.Apply(false);
                await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert(((SolidColorBrush)label.Foreground).Color == light);
            }
            finally { window.Close(); ThemeManager.Apply(true); }
        });
        await Check("Shared chart movement constraints keep temperatures ordered and endpoint at full speed", () =>
        {
            var chart = new FanCurveChart { Profile = FanProfile.Preset(0, "Balanced") };
            FanPoint? moved = null;
            chart.PointMoved += (i, p) => moved = p;
            // Exercise the same constraints used by mouse and keyboard input.
            var move = typeof(FanCurveChart).GetMethod("MovePoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            move.Invoke(chart, [3, 0, 0d]); Assert(moved == new FanPoint(66, 100));
            move.Invoke(chart, [0, 100, 100d]); Assert(moved == new FanPoint(44, 45));
            return Task.CompletedTask;
        });
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = results.Values.All(x => x), checks = results, hardwareWrites = 0, dataDirectory = root }, new JsonSerializerOptions { WriteIndented = true }));
        return results.Values.All(x => x);
    }
}
