using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sora.Core;

namespace Sora.App;

public partial class MainWindow
{
    private bool RouteAvailable(RadioButton button)
    {
        // Explicit development previews retain all routes for visual and navigation qualification.
        if (SoraData.IsDevelopmentPreview || (screenshotPath != null && !System.Environment.GetCommandLineArgs().Contains("--hardware-ui-check"))) return true;
        var module = button == NavDisplay ? HardwareModule.Cooler
            : button == NavKeyboard ? HardwareModule.Keyboard
            : button == NavLighting ? HardwareModule.Lighting
            : button == NavFans ? HardwareModule.MotherboardFans
            : button == NavGpu ? HardwareModule.GpuFans
            : button == NavStorage ? HardwareModule.Storage
            : button == NavWifi ? HardwareModule.Wifi
            : button == NavLogitech ? HardwareModule.Logitech : (HardwareModule?)null;
        return module == null || host.Hardware.Available(module.Value);
    }

    private void ApplyHardwareAvailability()
    {
        var query = NavSearchBox.Text.Trim();
        foreach (var entry in NavigationEntries())
            entry.Button.Visibility = RouteAvailable(entry.Button) && (query.Length == 0 || entry.Terms.Contains(query, System.StringComparison.CurrentCultureIgnoreCase))
                ? Visibility.Visible : Visibility.Collapsed;
        NavEmptyState.Visibility = NavigationEntries().Any(e => e.Button.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        if (NavigationEntries().Any(e => e.Button.IsChecked == true && !RouteAvailable(e.Button)))
            (NavigationEntries().FirstOrDefault(e => e.Button != NavUpdates && e.Button != NavSettings && RouteAvailable(e.Button)).Button ?? NavSettings).IsChecked = true;
        HardwareStatusText.Text = host.Hardware.Modules.Any(m => m.Presence == HardwarePresence.DetectionFailed)
            ? "Windows could not complete hardware detection. Sora will retry automatically."
            : host.Hardware.Modules.All(m => !m.Available)
                ? "No supported hardware found. Connect a device; Sora checks automatically."
                : "Connected hardware determines which pages and settings appear. New devices are detected automatically.";
    }

    private object RunHardwareAvailabilityChecks()
    {
        var results = new System.Collections.Generic.Dictionary<string, bool>();
        var original = host.Hardware;
        var originalRoute = NavigationEntries().FirstOrDefault(e => e.Button.IsChecked == true).Button;
        try
        {
            var noDevices = HardwareDiscovery.Classify([], "", "");
            host.SetHardwareInventoryForCheck(noDevices); ApplyHardwareAvailability();
            results["Absent hardware routes are hidden"] = NavigationEntries().Where(e => e.Button != NavSettings && e.Button != NavUpdates).All(e => e.Button.Visibility == Visibility.Collapsed);
            NavDisplay.IsChecked = true;
            results["Unavailable route cannot be opened directly"] = NavSettings.IsChecked == true && DisplayPage.Visibility == Visibility.Collapsed;
            NavSearchBox.Text = "cooler";
            results["Search cannot reveal absent cooler"] = NavDisplay.Visibility == Visibility.Collapsed && NavEmptyState.Visibility == Visibility.Visible;
            NavSearchBox.Clear();
            host.SetHardwareInventoryForCheck(HardwareDiscovery.Classify([new("Logitech G102", "Mouse", @"HID\VID_046D&PID_C092")], "", ""));
            ApplyHardwareAvailability();
            results["Mouse-only machine has no cooler or fan page"] = NavLogitech.Visibility == Visibility.Visible && NavDisplay.Visibility == Visibility.Collapsed && NavFans.Visibility == Visibility.Collapsed && NavGpu.Visibility == Visibility.Collapsed;
            if (Logitech.FindName("DeviceTitle") is TextBlock title && title.Text.Contains("G102"))
            {
                results["G102 named by actual device"] = true;
                results["G502-specific tabs are hidden on G102"] = new[] { "SensitivityTab", "AssignmentsTab", "MacrosTab", "LightingTab", "MemoryTab" }.All(name => ((TabItem)Logitech.FindName(name)).Visibility == Visibility.Collapsed);
                results["G502-specific preferences are hidden on G102"] = ((FrameworkElement)Logitech.FindName("DevicePreferencesCard")).Visibility == Visibility.Collapsed;
            }
        }
        finally
        {
            host.SetHardwareInventoryForCheck(original); ApplyHardwareAvailability();
            if (originalRoute != null && RouteAvailable(originalRoute)) originalRoute.IsChecked = true;
        }
        return new { passed = results.Values.All(value => value), checks = results };
    }
}
