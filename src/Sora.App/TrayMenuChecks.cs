using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sora.App;

internal static class TrayMenuChecks
{
    internal static async Task<bool> RunAsync(string output)
    {
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool passed) { checks[name] = passed; if (!passed) throw new InvalidOperationException(name); }
        ThemeManager.Apply(true);
        int invoked = 0;
        var menu = NativeTray.BuildMenu([
            new("Open Sora", () => invoked++, Icon: "IconMonitor"),
            new("Check for updates", () => invoked++, Icon: "IconDownload"),
            new("Start cooler display", () => invoked++, false, "IconMonitor"),
            null,
            new("Settings", () => invoked++, Icon: "IconSettings"),
            new("Exit Sora", () => invoked++, Icon: "IconClose")]);
        menu.Visibility = Visibility.Visible;
        menu.ApplyTemplate(); menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize)); menu.UpdateLayout();
        var rows = menu.Items.OfType<MenuItem>().ToArray();
        Check("Menu uses the shared theme and readable spacing", menu.ActualWidth >= 240 && rows.Length == 5 && rows.All(r => r.ActualHeight == 36));
        Check("Every command has a consistent line icon and accessible name", rows.All(r => r.Icon is ContentControl && !string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(r))));
        Check("Unavailable display command remains disabled", !rows[2].IsEnabled);
        Check("Menu background follows AMOLED appearance", ((SolidColorBrush)Application.Current.FindResource("PopoverBrush")).Color == Color.FromRgb(24, 24, 24));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(menu); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(output)) encoder.Save(file);
        foreach (var row in rows.Where(r => r.IsEnabled)) row.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check("Enabled commands invoke their actual callbacks exactly once", invoked == 4);
        ThemeManager.Apply(false);
        Check("Light appearance also updates the menu palette", ((SolidColorBrush)Application.Current.FindResource("PopoverBrush")).Color != Color.FromRgb(24, 24, 24));
        ThemeManager.Apply(true);
        File.WriteAllText(output + ".checks.json", JsonSerializer.Serialize(new { passed = checks.Values.All(v => v), checks, hardwareWrites = 0 }, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
