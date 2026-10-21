using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sora.App;

public partial class LogitechPanel
{
    // Reveal the complete labelled RGB row, including a focus-outline gutter,
    // rather than scrolling only the focused text/caret against the viewport edge.
    private void RgbBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (ReferenceEquals(e.TargetObject, RgbFields)) return;
        e.Handled = true;
        RevealRgbFields();
    }

    private void RevealRgbFields()
    {
        var bounds = RgbFields.TransformToAncestor(LightingScroll).TransformBounds(new Rect(RgbFields.RenderSize));
        if (bounds.Top >= 4 && bounds.Bottom <= LightingScroll.ViewportHeight - 4) return;
        RgbFields.BringIntoView(new Rect(0, -4, RgbFields.ActualWidth, RgbFields.ActualHeight + 8));
    }

    private LogitechReactiveLighting? reactive;
    private readonly Dictionary<byte, LogitechReactiveZone> reactiveDrafts = [];
    private bool invalidRgb, linkZonesPending;
    private bool IsReactive => (EffectPicker.SelectedItem as EffectOption)?.Mode is 100 or 101;
    private byte[] LightingBaseline(byte zone) => snapshot == null ? [] : reactive?.SavedFirmwareState(snapshot.Identity, zone) ?? snapshot.Lighting?.SingleOrDefault(z => z.Index == zone)?.State ?? [];
    private bool LightingDirty(byte zone) => invalidLight || linkZonesPending || (reactiveDrafts.TryGetValue(zone, out var draft)
        ? reactive?.Zones.SingleOrDefault(z => z.Zone == zone) != draft
        : reactive?.Zones.Any(z => z.Zone == zone) == true || lightDrafts.TryGetValue(zone, out var raw) && !raw.SequenceEqual(LightingBaseline(zone)));
    private LogitechReactiveZone ReactiveFromFields(byte zone)
    {
        var setting = new LogitechReactiveZone(zone, (EffectPicker.SelectedItem as EffectOption)?.Mode == 100 ? "Screen sampler" : "Audio visualizer", HexInput.Text.TrimStart('#'), (int)BrightnessSlider.Value, (ScreenAreaPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Whole screen");
        setting.Validate(); return setting;
    }
    private void LoadReactiveEditor()
    {
        reactiveDrafts.Clear(); foreach (var zone in reactive?.Zones ?? []) reactiveDrafts[zone.Zone] = zone;
        var wasRendering = rendering; rendering = true;
        LinkZonesCheck.IsChecked = reactive?.LinkedZones == true;
        rendering = wasRendering;
        linkZonesPending = false; PopulateZone(); LoadPalette();
    }
    private void PopulateReactiveFields(byte zone)
    {
        if (!reactiveDrafts.TryGetValue(zone, out var setting)) return;
        EffectPicker.SelectedIndex = Array.FindIndex((EffectOption[])EffectPicker.ItemsSource, e => e.Mode == (setting.Mode == "Screen sampler" ? 100 : 101));
        HexInput.Text = "#" + setting.Hex; BrightnessSlider.Value = setting.Brightness;
        ScreenAreaPicker.SelectedIndex = Array.IndexOf(new[] { "Whole screen", "Left half", "Right half" }, setting.ScreenArea);
    }
    private void SyncRgbFields()
    {
        try
        {
            var color = Convert.FromHexString(HexInput.Text.TrimStart('#')); if (color.Length != 3) return;
            rendering = true; RedInput.Text = color[0].ToString(); GreenInput.Text = color[1].ToString(); BlueInput.Text = color[2].ToString();
            if (ColorPickerHost.Content is ColorWheel wheel) wheel.Hex = HexInput.Text;
        }
        catch (FormatException) { }
        finally { rendering = false; }
    }
    private void RgbChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || rendering) return;
        if (!byte.TryParse(RedInput.Text, out var red) || !byte.TryParse(GreenInput.Text, out var green) || !byte.TryParse(BlueInput.Text, out var blue)) { invalidRgb = invalidLight = true; ShowError("RGB channels must be whole numbers from 0 to 255.", true); SetEnabled(); return; }
        invalidRgb = false; HexInput.Text = $"#{red:X2}{green:X2}{blue:X2}";
    }
    private void ReactiveOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || rendering) return;
        if (ReferenceEquals(sender, LinkZonesCheck))
        {
            try { reactive?.SaveLinkedZones(LinkZonesCheck.IsChecked == true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowError("Could not save linked lighting: " + error.Message); return; }
            linkZonesPending = LinkZonesCheck.IsChecked == true;
        }
        LightEdited();
    }
    private void WindowsLightingClick(object sender, RoutedEventArgs e) => FileAction(() => Process.Start(new ProcessStartInfo("ms-settings:personalization-dynamiclighting") { UseShellExecute = true }));
    private void SaveColorClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        var hex = HexInput.Text.ToUpperInvariant(); if (Convert.FromHexString(hex.TrimStart('#')).Length != 3) throw new InvalidDataException("Choose a valid color first.");
        var file = Path.Combine(DataDirectory, "palette.json"); var colors = File.Exists(file) && new FileInfo(file).Length < 4096 ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [] : [];
        Directory.CreateDirectory(DataDirectory); File.WriteAllText(file, JsonSerializer.Serialize(colors.Append(hex).Distinct().TakeLast(24))); LoadPalette();
    });
    private void LoadPalette()
    {
        CustomPalette.Children.Clear();
        try
        {
            var file = Path.Combine(DataDirectory, "palette.json"); if (!File.Exists(file) || new FileInfo(file).Length > 4096) return;
            foreach (var hex in (JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? []).Take(24))
            {
                if (Convert.FromHexString(hex.TrimStart('#')).Length != 3) continue;
                var button = new Button { Style = (Style)FindResource("Swatch"), Width = 25, Height = 25, Margin = new Thickness(0, 0, 6, 6), Background = (Brush)new BrushConverter().ConvertFromString(hex)!, Tag = hex, ToolTip = hex };
                button.Click += SwatchClick; CustomPalette.Children.Add(button);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException) { ShowError("The custom palette could not be loaded."); }
    }
}
