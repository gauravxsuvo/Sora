using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        await Check("DPI verification survives blocked host polling, retries busy focus switches and preserves intentional Shift", async () =>
        {
            using var cancel = new System.Threading.CancellationTokenSource();
            var ticks = System.Threading.Channels.Channel.CreateUnbounded<TaskCompletionSource>();
            TaskCompletionSource? previous = null;
            async Task Delay(System.Threading.CancellationToken token)
            {
                previous?.TrySetResult();
                previous = await ticks.Reader.ReadAsync(token);
            }
            var focus = new IntPtr(100); var time = DateTime.UtcNow;
            var actual = 800; var expected = 800; var count = 0; var available = true;
            var monitor = LogitechDpiWatchdog.RunAsync(() => focus, () =>
            {
                count++; if (!available) return Task.FromResult(false);
                actual = expected; return Task.FromResult(true);
            }, cancel.Token, Delay, () => time);
            async Task Tick()
            {
                var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await ticks.Writer.WriteAsync(complete); await complete.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            try
            {
                await Tick(); Assert(count == 1 && actual == 800);
                actual = 1600; focus = new IntPtr(200); available = false;
                await Tick(); Assert(count == 2 && actual == 1600);
                available = true; await Tick(); Assert(count == 3 && actual == 800);
                await Tick(); Assert(count == 3);
                actual = 3200; time = time.AddSeconds(2); await Tick(); Assert(count == 4 && actual == 800);
                expected = 4000; focus = new IntPtr(300); await Tick(); Assert(actual == 4000);
                expected = 800; focus = new IntPtr(400); await Tick(); Assert(actual == 800);
            }
            finally { cancel.Cancel(); await monitor.WaitAsync(TimeSpan.FromSeconds(2)); }
        });
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
        await Check("Legacy fan defaults migrate once without losing the saved curve or header", () =>
        {
            var directory = Path.Combine(root, "legacy-retention"); Directory.CreateDirectory(directory);
            var profile = FanProfile.Preset(5, "Quiet") with { Points = [new(19, 12.94), new(30, 14.02), new(46, 17), new(60, 29), new(75, 51), new(86, 100)] };
            var legacy = JsonSerializer.SerializeToNode(new FanPreferences { ApplyAtStartup = false, RestoreOnExit = true, Profiles = new() { [5] = profile }, HeaderNames = new() { [5] = "Radiator fans" } })!.AsObject();
            legacy.Remove("RetentionVersion"); legacy.Remove("SelectedHeader");
            File.WriteAllText(Path.Combine(directory, "fans.json"), legacy.ToJsonString());
            var fans = new SoraHost(directory).Fans;
            Assert(fans.Preferences.ApplyAtStartup && !fans.Preferences.RestoreOnExit && fans.Preferences.TakePriority && fans.Preferences.SelectedHeader == 5);
            Assert(fans.Preferences.Profiles[5].Points.SequenceEqual(profile.Points) && fans.DisplayName(5) == "Radiator fans" && File.Exists(Path.Combine(directory, "fans.json.before-retention-v1")));
            fans.SetOptions(false, true, false); fans.RememberHeader(2);
            var reopened = new SoraHost(directory).Fans.Preferences;
            Assert(!reopened.ApplyAtStartup && reopened.RestoreOnExit && !reopened.TakePriority && reopened.SelectedHeader == 2);
            return Task.CompletedTask;
        });
        await Check("Legacy RGB retains the saved look and respects later explicit exit choices", () =>
        {
            var directory = Path.Combine(root, "legacy-lighting"); Directory.CreateDirectory(directory);
            var legacy = JsonSerializer.SerializeToNode(new LightingSettings(Memory: new MemoryLighting(EneMode.Static, "#8300FF", 30, 3), ApplyAtStartup: false, RainbowOnExit: true))!.AsObject();
            legacy.Remove("RetentionVersion"); File.WriteAllText(Path.Combine(directory, "lighting.json"), legacy.ToJsonString());
            var lighting = new SoraHost(directory).Lighting;
            Assert(lighting.Settings.ApplyAtStartup && !lighting.Settings.RainbowOnExit && lighting.Settings.Memory?.Color == "#8300FF");
            lighting.SaveExitPreference(true);
            Assert(new SoraHost(directory).Lighting.Settings.RainbowOnExit);
            return Task.CompletedTask;
        });
        await Check("Keyboard photo and key map preserve model selection and key inspection", () =>
        {
            new KeyboardPanel().RunViewChecks(); return Task.CompletedTask;
        });
        await Check("Fixed mouse color and 30 percent brightness survive save, reopen, zone switching and discard", () =>
        {
            var directory = Path.Combine(root, "mouse-editor-retention");
            var first = new SoraHost(directory);
            var choice = new LogitechLightingChoice(1, "8300FF", 5000, 30);
            first.LogitechLighting.RememberFirmware("test-mouse", 1, choice.Encode(), choice);
            var dpiChoice = new LogitechLightingChoice(10, "00CCFF", 7000, 55);
            first.LogitechLighting.RememberFirmware("test-mouse", 0, dpiChoice.Encode(), dpiChoice);
            var reopened = new SoraHost(directory);
            Assert(reopened.LogitechLighting.SavedChoice("test-mouse", 1) == choice);
            Assert(reopened.LogitechLighting.SavedChoice("test-mouse", 0) == dpiChoice);
            var reading = new LogitechSnapshot("G502 HERO", "test-mouse", 0xc08b, "4.2", [], [], 1000, [1000], 2, [],
                [new(0, "DPI", [0, 1, 10, 3], LogitechLighting.Encode(1, "FFFFFF", 5000, 100)), new(1, "Logo", [0, 1, 10, 3], choice.Encode())]);
            var panel = new LogitechPanel(); panel.Attach(reopened.Logitech, reopened.LogitechLighting);
            panel.CheckRetainedLightingEditor(reading);
            return Task.CompletedTask;
        });
        await Check("Bad mouse editor metadata retains valid hardware state and old files remain readable", () =>
        {
            var directory = Path.Combine(root, "mouse-metadata-recovery"); var mouse = Path.Combine(directory, "logitech"); Directory.CreateDirectory(mouse);
            var choice = new LogitechLightingChoice(1, "8300FF", 5000, 30);
            var path = Path.Combine(mouse, "firmware-lighting.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { Identity = "mouse", States = new Dictionary<byte, byte[]> { [1] = choice.Encode() }, Choices = new Dictionary<byte, LogitechLightingChoice> { [1] = choice with { Brightness = 90 } } }));
            var lighting = new SoraHost(directory).LogitechLighting;
            Assert(lighting.SavedFirmwareState("mouse", 1)!.SequenceEqual(choice.Encode()));
            Assert(lighting.SavedChoice("mouse", 1)!.Encode().SequenceEqual(choice.Encode()));
            Assert(lighting.SavedChoice("mouse", 1)!.Brightness == 100);
            Assert(lighting.SavedChoice("other-mouse", 1) == null);
            return Task.CompletedTask;
        });
        await Check("Mouse hardware state and profile and last DPI stage survive reopening", () =>
        {
            var directory = Path.Combine(root, "mouse-retention"); var mouse = Path.Combine(directory, "logitech"); Directory.CreateDirectory(mouse);
            var state = LogitechLighting.Encode(1, "8300FF", 5000, 30);
            File.WriteAllText(Path.Combine(mouse, "firmware-lighting.json"), JsonSerializer.Serialize(new { Identity = "test-mouse", States = new Dictionary<byte, byte[]> { [1] = state } }));
            var profile = LogitechControlProfile.Create(); LogitechControlProfile.Save(Path.Combine(mouse, "control-profile.json"), profile);
            File.WriteAllText(Path.Combine(mouse, "control-enabled.json"), "true");
            File.WriteAllText(Path.Combine(mouse, "control-runtime.json"), JsonSerializer.Serialize(new { Profile = profile.Name, profile.Stages, profile.ShiftDpi, Stage = 2 }));
            var host = new SoraHost(directory);
            Assert(host.LogitechLighting.SavedFirmwareState("test-mouse", 1)!.SequenceEqual(state) && host.LogitechLighting.SavedFirmwareState("another-mouse", 1) == null);
            var copy = host.LogitechLighting.SavedFirmwareState("test-mouse", 1)!; copy[0] ^= 1;
            Assert(host.LogitechLighting.SavedFirmwareState("test-mouse", 1)!.SequenceEqual(state));
            var resume = typeof(LogitechService).GetMethod("ResumeStage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert(host.Logitech.Enabled && host.Logitech.Profile!.Stages.SequenceEqual(profile.Stages) && (int)resume.Invoke(host.Logitech, [profile])! == 2);
            return Task.CompletedTask;
        });
        await Check("Keeping fan curves on exit leaves the helper stopped across reopen", async () =>
        {
            var directory = Path.Combine(root, "fan-lease"); var host = new SoraHost(directory); var system = new RetentionFanSystem();
            var service = new FanService(host, prioritySystem: system); service.Load();
            Assert(await service.PauseVendorServiceAsync()); await service.StopAsync();
            Assert(!system.Running && system.Starts == 0 && system.Captures == 1 && File.Exists(Path.Combine(directory, "fans-priority-session.json")));
            var reopened = new FanService(host, prioritySystem: system); reopened.Load(); Assert(await reopened.PauseVendorServiceAsync());
            Assert(system.Captures == 1); reopened.SetOptions(true, true); await reopened.StopAsync();
            Assert(system.Running && system.Starts == 1 && !File.Exists(Path.Combine(directory, "fans-priority-session.json")));
        });
        await Check("Application icon has transparent margins instead of a dark backing tile", () =>
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Sora;component/Assets/Sora.ico"))!.Stream;
            var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[^1], PixelFormats.Bgra32, null, 0);
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4]; frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            Assert(pixels[(frame.PixelWidth / 2) * 4 + 3] == 0 && pixels[(frame.PixelHeight / 2 * frame.PixelWidth) * 4 + 3] == 0 && pixels.Where((_, i) => i % 4 == 3).Any(a => a > 0));
            return Task.CompletedTask;
        });
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = results.Values.All(x => x), checks = results, hardwareWrites = 0, dataDirectory = root }, new JsonSerializerOptions { WriteIndented = true }));
        return results.Values.All(x => x);
    }

    private sealed class RetentionFanSystem : IFanPrioritySystem
    {
        public bool Running = true;
        public int Starts, Captures;
        public ManagedFanService[] Services() => [new("EasyTuneEngineService", @"C:\GIGABYTE\EasyTuneEngineService.exe", Running)];
        public string[] Writers() => Running ? ["EasyTuneEngineService"] : [];
        public FanSnapshot Capture()
        {
            Captures++;
            return new(GigabyteFans.SupportedBoard, FanMaintenance.Controller, DateTimeOffset.UtcNow, [35, 40, 45, 42, 46, 35],
                Enumerable.Range(0, 6).Select(h => new FanHeaderState(h, "Header", 1200, true, FanProfile.Preset(h, "Balanced"),
                    FanProtocol.Registers(h).Select(r => new FanRegisterValue(r.Bank, r.Address, (byte)(r == FanProtocol.ControlRegister(h) ? 128 : 0))).ToArray()) { FirmwareStop = new(false, 2, 0, 0) }).ToArray())
                { FirmwareInterface = FanMaintenance.FirmwareInterface };
        }
        public void Restore(FanSnapshot snapshot) => FanMaintenance.Validate(snapshot);
        public void Stop(ManagedFanService service) => Running = false;
        public void Start(ManagedFanService service) { Running = true; Starts++; }
    }
}
