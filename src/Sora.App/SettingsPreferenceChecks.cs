using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

public partial class MainWindow
{
    private async Task<object> RunActualSettingsNavigationChecksAsync()
    {
        var source = Path.Combine(host.DataDirectory, "settings.json");
        var before = File.ReadAllBytes(source);
        var stored = JsonSerializer.Deserialize<DisplaySettings>(before)!;
        var startupBefore = StartupTask.InspectForLaunch();
        var visits = new System.Collections.Generic.List<object>();
        foreach (var name in new[] { "Settings", "Cooler display", "Settings", "Cooler display", "Settings" })
        {
            (name == "Settings" ? NavSettings : NavDisplay).IsChecked = true;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var disk = JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllBytes(source))!;
            var unchanged = before.AsSpan().SequenceEqual(File.ReadAllBytes(source));
            if (!unchanged || disk != stored || host.Settings != stored) throw new InvalidOperationException("Navigation changed persisted preferences.");
            visits.Add(new { Page = name, Stored = disk, HostMatchesStorage = host.Settings == disk, BytesUnchanged = unchanged });
        }
        var startupAfter = StartupTask.InspectForLaunch();
        return new { ProcessId = Environment.ProcessId, StoragePath = source,
            SHA256Before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)),
            SHA256After = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))),
            StoredBefore = stored, StoredAfter = JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllBytes(source)),
            StartupEnabledBefore = startupBefore.Task?.Enabled, StartupEnabledAfter = startupAfter.Task?.Enabled,
            StartupRegistrationUnchanged = Equals(startupBefore.Task, startupAfter.Task), Visits = visits, ProductionSettingsWrites = 0 };
    }

    // Exercise the real toggle handlers against a copy of persisted preferences.
    // The production file is read and compared only; Windows startup is untouched.
    private async Task<object> RunSettingsPreferenceChecksAsync()
    {
        var source = Path.Combine(host.DataDirectory, "settings.json");
        var original = File.ReadAllBytes(source);
        var saved = JsonSerializer.Deserialize<DisplaySettings>(original) ?? throw new InvalidDataException("Missing settings.");
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshotPath!))!, "settings-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "settings.json"), original);
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool value) { checks[name] = value; if (!value) throw new InvalidOperationException(name); }
        MainWindow? testWindow = null;
        try
        {
            var testHost = new SoraHost(directory);
            testWindow = new MainWindow(testHost, Path.Combine(directory, "unused.png"));
            Check("Actual window matches persisted tray and cooler startup", TrayCheck.IsChecked == saved.MinimizeToTray && AutoDisplayCheck.IsChecked == saved.AutoStartDisplay);
            Check("Copied preferences initialize the shared toggles", testWindow.TrayCheck.IsChecked == saved.MinimizeToTray && testWindow.AutoDisplayCheck.IsChecked == saved.AutoStartDisplay);
            testWindow.TrayCheck.IsChecked = !saved.MinimizeToTray;
            testWindow.AutoDisplayCheck.IsChecked = !saved.AutoStartDisplay;
            await Task.Yield();
            var changed = JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllText(Path.Combine(directory, "settings.json")))!;
            Check("Real toggle handlers save both changed preferences", changed.MinimizeToTray == !saved.MinimizeToTray && changed.AutoStartDisplay == !saved.AutoStartDisplay);
            Check("Other saved fields are preserved", changed.Fahrenheit == saved.Fahrenheit && changed.LeadingZeros == saved.LeadingZeros);
            testWindow.NavSettings.IsChecked = true; testWindow.NavDisplay.IsChecked = true; testWindow.NavSettings.IsChecked = true;
            Check("Navigation preserves toggle values", testWindow.TrayCheck.IsChecked == changed.MinimizeToTray && testWindow.AutoDisplayCheck.IsChecked == changed.AutoStartDisplay);
            var restarted = new SoraHost(directory);
            var reopened = new MainWindow(restarted, Path.Combine(directory, "unused-reopened.png"));
            Check("New host and window reload the saved values", reopened.TrayCheck.IsChecked == changed.MinimizeToTray && reopened.AutoDisplayCheck.IsChecked == changed.AutoStartDisplay);
            reopened.shuttingDown = true; reopened.Close();
            testWindow.TrayCheck.IsChecked = saved.MinimizeToTray; testWindow.AutoDisplayCheck.IsChecked = saved.AutoStartDisplay;
            Check("Both directions persist and reload", new SoraHost(directory).Settings == saved);
            Check("Actual persisted settings remain unchanged", original.AsSpan().SequenceEqual(File.ReadAllBytes(source)));
            return new { checks, SourcePreferences = saved, ProductionSettingsWrites = 0, TestData = directory };
        }
        finally { if (testWindow != null) { testWindow.shuttingDown = true; testWindow.Close(); } }
    }
}
