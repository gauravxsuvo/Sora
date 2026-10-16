using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using Microsoft.Win32;
using Sora.Core;

namespace Sora.App;

public partial class KeyboardPanel : UserControl
{
    private DeviceInfo? device;
    private KreoModel model = KreoModel.All.First(m => m.Name == "Swarm 75");
    private KreoState? baseline, edited;
    private KreoKey? selected;
    private bool updating, busy, ready;
    private int colorSlot;
    private string statusTone = "Idle";
    // Set when only the 2.4 GHz receiver is present: the keyboard works, but its settings can't be reached.
    private bool wirelessOnly;
    // Connected through the 2.4 GHz receiver: lighting is editable; per-key colours can't be read, keys and palette aren't written yet.
    private bool wireless;
    private int? battery;
    private readonly Dictionary<int, Button> keyButtons = new();
    private readonly string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data", "keyboard");
    public Task PendingOperation { get; private set; } = Task.CompletedTask;
    public bool HasSnapshot => baseline != null;
    public bool IsBusy => busy;
    public bool HasPendingEdits => PendingChanges() > 0;
    // Short status for the sidebar; the page header chip carries the same tone.
    public string SidebarStatus => busy ? "Kreo · Working…" : wirelessOnly ? "Kreo · 2.4 GHz receiver" : statusTone == "Bad" ? "Kreo · Needs attention" : baseline != null ? model.Name + (wireless ? " · 2.4 GHz" : " · USB") : "Kreo · Not connected";
    public string StatusTone => busy ? "Warn" : statusTone;

    private void SetStatus(string text, string tone)
    {
        statusTone = tone; KeyboardStatus.Text = text; KeyboardDot.Fill = (Brush)FindResource(tone);
        (Window.GetWindow(this) as MainWindow)?.RenderKeyboardStatus();
    }

    public KeyboardPanel()
    {
        InitializeComponent();
        ModelPicker.ItemsSource = KreoModel.All; ModelPicker.SelectedItem = model;
        BuildBrushSwatches();
        ready = true; DrawBoard(); UpdateAvailability();
    }

    // Board geometry: one key unit in design pixels and the gap between caps. The Viewbox scales the whole board uniformly.
    private const double Unit = 54, Gap = 6;
    private IReadOnlyList<KeyPlacement> layout = [];
    private readonly Dictionary<int, KeyParts> keyParts = new();
    private sealed record KeyParts(Border Glow, TextBlock Legend, TextBlock Remap, Ellipse Dot);
    private static readonly string[] BrushColors = ["#FFFFFF", "#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#0A84FF", "#BF5AF2", "#FF375F"];

    private void DrawBoard()
    {
        Board.Children.Clear(); keyButtons.Clear(); keyParts.Clear();
        layout = KreoLayout.Build(model);
        var (width, height) = KreoLayout.Size(layout);
        Board.Width = width * Unit; Board.Height = height * Unit;
        foreach (var place in layout)
        {
            var key = place.Key;
            var legend = new TextBlock { Text = Legend(key), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = FitFont(Legend(key), place.Width * Unit - Gap - 14), Tag = FitFont(Legend(key), place.Width * Unit - Gap - 14), Margin = new Thickness(2, 0, 2, 2) };
            var remap = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 1), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("AccentTextBrush"), Visibility = Visibility.Collapsed };
            var dot = new Ellipse { Width = 7, Height = 7, Fill = (Brush)FindResource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 5, 0), Visibility = Visibility.Collapsed };
            var glow = new Border { CornerRadius = new CornerRadius(6) };
            var content = new Grid(); content.Children.Add(glow); content.Children.Add(legend); content.Children.Add(remap); content.Children.Add(dot);
            var button = new Button { Style = (Style)FindResource("Keycap"), Width = place.Width * Unit - Gap, Height = place.Height * Unit - Gap, Content = content };
            AutomationProperties.SetName(button, FullName(key));
            Canvas.SetLeft(button, place.X * Unit + Gap / 2); Canvas.SetTop(button, place.Y * Unit + Gap / 2);
            button.Click += (_, _) => SelectKey(selected?.Index == key.Index ? null : key);
            Board.Children.Add(button); keyButtons[key.Index] = button; keyParts[key.Index] = new KeyParts(glow, legend, remap, dot);
        }
        UpdateKeys();
    }

    // Largest legend size (16 down to 11 design px) that fits the cap without trimming.
    private double FitFont(string text, double available)
    {
        var typeface = new Typeface((FontFamily)FindResource("UIFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        for (var size = 16.0; size > 11; size--)
            if (new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, 1).Width <= available) return size;
        return 11;
    }

    // Short legends for the cap; the full name stays in the tooltip and accessibility name.
    private static string Legend(KreoKey key) => key.Label switch
    {
        "LShift" or "RShift" => "Shift", "LCtrl" or "RCtrl" => "Ctrl", "LAlt" or "RAlt" => "Alt", "LWin" => "Win", "FN" => "Fn",
        "CapsLock" => "Caps Lock", "Delete" => "Del", "Up" => "↑", "Down" => "↓", "Left" => "←", "Right" => "→", "Space" => "", _ => key.Label,
    };
    private static string FullName(KreoKey key) => key.Label switch { "LShift" => "Left Shift", "RShift" => "Right Shift", "LCtrl" => "Left Ctrl", "RCtrl" => "Right Ctrl", "LAlt" => "Left Alt", "RAlt" => "Right Alt", "LWin" => "Windows", "FN" => "Fn", "CapsLock" => "Caps Lock", _ => key.Label };

    private void SelectKey(KreoKey? key)
    {
        selected = key;
        if (edited != null && key != null)
        {
            updating = true;
            var action = edited.Keys.AsSpan(key.Index * 4, 4).ToArray();
            TargetKey.SelectedItem = (TargetKey.ItemsSource as IReadOnlyList<KreoAction>)?.FirstOrDefault(a => a.Bytes.SequenceEqual(action));
            updating = false;
        }
        UpdateKeys(); UpdateAvailability();
    }
    private void ClearSelection(object sender, RoutedEventArgs e) => SelectKey(null);

    // The colour a key is lit with right now, or null when it is unlit or the colour isn't known (multicolour animation).
    private Color? KeyColor(KreoKey key)
    {
        if (edited == null) return null;
        var effect = edited.Lighting[10];
        // Per-key colours can't be read over the receiver, so they are not shown rather than guessed.
        if (effect == 21) return wireless ? null : Color.FromRgb(edited.Colors[key.Index], edited.Colors[key.Index + 126], edited.Colors[key.Index + 252]);
        if (effect is > 0 and <= 18 && edited.EffectPalette != null)
        {
            var slot = edited.Lighting[57 + 2 * effect] & 15;
            if (slot < 7) { var offset = effect * 21 + slot * 3; return Color.FromRgb(edited.EffectPalette[offset], edited.EffectPalette[offset + 1], edited.EffectPalette[offset + 2]); }
        }
        return null;
    }

    private void UpdateKeys()
    {
        var assigning = ModeAssign.IsChecked == true;
        foreach (var place in layout)
        {
            var key = place.Key;
            if (!keyButtons.TryGetValue(key.Index, out var button) || !keyParts.TryGetValue(key.Index, out var parts)) continue;
            var lit = KeyColor(key) is { } c && c.R + c.G + c.B > 24 ? c : (Color?)null;
            // Lit keys glow in their real colour; the legend takes a lighter tint of it so it stays readable.
            parts.Glow.Background = lit is { } glow ? new RadialGradientBrush(Color.FromArgb(125, glow.R, glow.G, glow.B), Color.FromArgb(30, glow.R, glow.G, glow.B)) { RadiusX = 0.75, RadiusY = 0.9, Center = new Point(0.5, 0.45), GradientOrigin = new Point(0.5, 0.45) } : null;
            parts.Legend.Foreground = lit is { } tint ? new SolidColorBrush(Mix(tint, Colors.White, 0.55)) : new SolidColorBrush(edited == null || edited.Lighting[10] != 0 ? Color.FromRgb(0xD1, 0xD1, 0xD6) : Color.FromRgb(0x8E, 0x8E, 0x93));
            var mappingChanged = edited != null && baseline != null && !edited.Keys.AsSpan(key.Index * 4, 4).SequenceEqual(baseline.Keys.AsSpan(key.Index * 4, 4));
            var action = edited == null ? key.Label : KreoActions.Describe(model, edited.Keys.AsSpan(key.Index * 4, 4).ToArray());
            parts.Remap.Text = mappingChanged ? "→ " + action : "";
            parts.Remap.Visibility = mappingChanged ? Visibility.Visible : Visibility.Collapsed;
            parts.Legend.VerticalAlignment = mappingChanged ? VerticalAlignment.Top : VerticalAlignment.Center;
            parts.Legend.Margin = mappingChanged ? new Thickness(2, 1, 2, 0) : new Thickness(2, 0, 2, 2);
            parts.Legend.FontSize = mappingChanged ? Math.Min(13, (double)parts.Legend.Tag) : (double)parts.Legend.Tag;
            // Colour edits are visible on the cap itself; the dot marks a staged remap.
            parts.Dot.Visibility = mappingChanged ? Visibility.Visible : Visibility.Collapsed;
            var chosen = selected?.Index == key.Index;
            button.Tag = chosen ? "Selected" : null;
            Panel.SetZIndex(button, chosen ? 1 : 0);
            // The Fn key keeps its layer function, so it is dimmed while assigning.
            button.Opacity = assigning && key.VirtualKey == 0xfa ? 0.45 : 1;
            button.ToolTip = FullName(key) + (mappingChanged ? " → " + action : edited != null && action != key.Label ? " · sends " + action : "") + (lit is { } hex && edited?.Lighting[10] == 21 ? " · #" + Hex(hex) : "");
        }
        var name = selected == null ? null : FullName(selected);
        BoardHint.Text = wirelessOnly ? "Editing needs the USB cable" : edited == null || device == null ? "Connect the keyboard to edit it"
            : ModeProfiles.IsChecked == true ? "Profiles cover the whole keyboard"
            : name != null ? name + " selected"
            : wireless ? (assigning ? "Remapping needs the USB cable" : "Per-key painting needs the USB cable")
            : assigning ? "Click a key to change what it sends" : "Click a key to paint it";
        ClearSelectionButton.Visibility = selected != null && ModeProfiles.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        PaintTarget.Text = name == null ? "No key selected" : name + " selected";
        PaintSelectedButton.Content = name == null ? "Paint selected key" : "Paint " + name;
        PaintAllButton.Content = "Fill all " + layout.Count + " keys";
        if (wireless) PaintTarget.Text = "Needs the USB cable";
        if (selected != null && edited != null && !wireless)
        {
            KeyColorRow.Visibility = Visibility.Visible;
            var stored = Color.FromRgb(edited.Colors[selected.Index], edited.Colors[selected.Index + 126], edited.Colors[selected.Index + 252]);
            KeyColorSwatch.Background = new SolidColorBrush(stored);
            KeyColorInfo.Text = edited.Lighting[10] == 21 ? "Current color #" + Hex(stored) : "Stored color #" + Hex(stored) + " · shows in Custom per-key";
        }
        else KeyColorRow.Visibility = Visibility.Collapsed;
        AssignEmpty.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
        AssignEditor.Visibility = selected == null ? Visibility.Collapsed : Visibility.Visible;
        ActionLabel.Text = selected == null || edited == null ? "" : name + " sends " + KreoActions.Describe(model, edited.Keys.AsSpan(selected.Index * 4, 4).ToArray());
        UpdateMappingPreview(); UpdatePaintPreview();
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb((byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));
    private static string Hex(Color color) => Convert.ToHexString(new[] { color.R, color.G, color.B });

    // Gives the keyboard card at least 300 px; the editors take what remains and scroll beyond that.
    private void PanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var fixedHeight = ((FrameworkElement)Content is Grid root ? root.Children.OfType<FrameworkElement>().Where(c => Grid.GetRow(c) is 0 or 5).Sum(c => c.ActualHeight + c.Margin.Top + c.Margin.Bottom) : 0) + 20 + 16;
        EditorsScroll.MaxHeight = Math.Max(120, ActualHeight - fixedHeight - 300);
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        Editors.SelectedIndex = ModeAssign.IsChecked == true ? 1 : ModeProfiles.IsChecked == true ? 2 : 0;
        UpdateKeys();
    }

    // Arrow keys move between caps by position; Space or Enter selects; Esc clears the selection.
    private void BoardKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && selected != null) { SelectKey(null); e.Handled = true; return; }
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        var current = layout.FirstOrDefault(p => keyButtons.TryGetValue(p.Key.Index, out var b) && b.IsKeyboardFocused) ?? layout.FirstOrDefault(p => p.Key.Index == selected?.Index) ?? layout.FirstOrDefault();
        if (current == null) return;
        var (cx, cy) = (current.X + current.Width / 2, current.Y + current.Height / 2);
        var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), _ => (0, 1) };
        var next = layout.Where(p => p != current)
            .Select(p => (Place: p, X: p.X + p.Width / 2 - cx, Y: p.Y + p.Height / 2 - cy))
            .Where(p => p.X * dx + p.Y * dy > 0.3)
            .OrderBy(p => Math.Abs(p.X * dx + p.Y * dy) + 2.5 * Math.Abs(p.X * dy + p.Y * dx))
            .Select(p => p.Place).FirstOrDefault();
        if (next != null && keyButtons.TryGetValue(next.Key.Index, out var target)) target.Focus();
        e.Handled = true;
    }

    private void BuildBrushSwatches()
    {
        foreach (var hex in BrushColors)
        {
            var rgb = KreoProtocol.ParseHex(hex);
            var swatch = new Button { Style = (Style)FindResource("Swatch"), Width = 18, Height = 18, Padding = new Thickness(0), Margin = new Thickness(0, 2, 5, 2), Background = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2])), ToolTip = hex };
            AutomationProperties.SetName(swatch, "Brush " + hex);
            swatch.Click += (_, _) => PaintColor.Text = hex;
            BrushSwatches.Children.Add(swatch);
        }
    }
    private void ModelChanged(object sender, SelectionChangedEventArgs e) { if (!ready || updating || ModelPicker.SelectedItem is not KreoModel choice) return; model = choice; selected = null; DrawBoard(); }

    public Task ReadAsync()
    {
        if (busy) return PendingOperation;
        PendingOperation = RunOperation(async () =>
        {
            var result = await Task.Run(async () =>
            {
                var devices = KreoKeyboard.Enumerate();
                // Only the Swarm 75 is paired with the 2.4 GHz receiver in KB Hub's definitions.
                if (devices.Count == 0 && KreoReceiver.Find() is { } receiverDevice)
                {
                    if (KreoKeyboard.FindConflict() is { } conflict) throw new IOException(conflict);
                    var swarm = KreoModel.All.Single(m => m.Name == "Swarm 75");
                    using var receiver = new KreoReceiver(receiverDevice);
                    var snapshot = await receiver.ReadStateAsync(swarm);
                    return (Device: receiverDevice, Model: swarm, State: snapshot.State, Wireless: true, Battery: snapshot.BatteryPercent);
                }
                if (devices.Count == 0 && KreoKeyboard.ReceiverPresent()) throw new KreoWirelessOnlyException();
                if (devices.Count != 1) throw new IOException(devices.Count == 0 ? "Connect one supported keyboard by USB and switch it to wired mode." : "Connect one Kreo keyboard at a time.");
                using var keyboard = new KreoKeyboard(devices[0]);
                return (Device: devices[0], Model: keyboard.Definition, State: keyboard.ReadState(), Wireless: false, Battery: (int?)null);
            });
            wirelessOnly = false; wireless = result.Wireless; battery = result.Battery;
            device = result.Device; model = result.Model; baseline = result.State; edited = baseline.Copy(); selected = null;
            updating = true; ModelPicker.SelectedItem = model; TargetKey.ItemsSource = KreoActions.ForModel(model); updating = false;
            LoadControls(); DrawBoard();
            SetStatus(model.Name + (wireless ? " · 2.4 GHz" + (battery is { } level ? $" · {level}%" : "") : " · USB"), "Good");
            KeyboardDetail.Text = wireless ? "Connected through the 2.4 GHz receiver. Lighting can be changed here; key assignments, effect colors and per-key colors need the USB cable."
                : "Ready. Changes stay in the editor until you apply them.";
        });
        return PendingOperation;
    }

    private async void ReadKeyboard(object sender, RoutedEventArgs e)
    {
        if (PendingChanges() > 0 && MessageBox.Show("Refreshing will discard the edits in this window. Refresh the keyboard?", "Sora", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await ReadAsync();
    }

    private async Task RunOperation(Func<Task> action)
    {
        busy = true; UpdateAvailability(); SetStatus("Working…", "Warn");
        try { await action(); }
        catch (KreoReceiverTimeoutException error)
        {
            // Not a fault: the keyboard is asleep or out of range. Nothing can be applied until it answers.
            wirelessOnly = true; wireless = false; device = null;
            SetStatus("2.4 GHz · not responding", "Warn"); KeyboardDetail.Text = error.Message;
        }
        catch (KreoWirelessOnlyException error)
        {
            // Staged edits are kept, but nothing can be applied until the cable is back.
            wirelessOnly = true; wireless = false; device = null;
            SetStatus("2.4 GHz · settings need USB", "Warn"); KeyboardDetail.Text = error.Message;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { wirelessOnly = false; SetStatus("Needs attention", "Bad"); KeyboardDetail.Text = error.Message; }
        finally { busy = false; UpdateKeys(); UpdateAvailability(); }
    }

    private void LoadControls()
    {
        if (edited == null) return;
        updating = true;
        EffectPicker.ItemsSource = model.Effects.Concat(new[] { new KreoEffect(21, "Custom per-key", false, false, false), new KreoEffect(0, "Off", false, false, false) }).ToArray();
        EffectPicker.SelectedItem = EffectPicker.Items.Cast<KreoEffect>().FirstOrDefault(e => e.Id == edited.Lighting[10]);
        LoadEffectParameters(); updating = false; UpdateAvailability(); UpdateSwatches();
    }

    private void LoadEffectParameters()
    {
        if (edited == null) return;
        var id = edited.Lighting[10];
        if (id is > 0 and <= 18)
        {
            Speed.Value = Math.Clamp((int)edited.Lighting[56 + 2 * id], 0, 4);
            Brightness.Value = Math.Clamp(edited.Lighting[57 + 2 * id] >> 4, 0, 4);
            var slot = edited.Lighting[57 + 2 * id] & 15; colorSlot = slot < 7 ? slot : 0; ColorMode.SelectedIndex = slot == 7 ? 1 : 0;
            var offset = id * 21 + colorSlot * 3; EffectHex.Text = "#" + Convert.ToHexString(edited.EffectPalette!.AsSpan(offset, 3));
        }
        EffectColorError.Text = "";
    }

    private void LightingChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || updating || edited == null || EffectPicker.SelectedItem is not KreoEffect effect) return;
        if (sender == EffectPicker)
        {
            var storedSpeed = effect.Id is > 0 and <= 18 ? Math.Clamp((int)edited.Lighting[56 + 2 * effect.Id], 0, 4) : 0;
            var storedOptions = effect.Id is > 0 and <= 18 ? edited.Lighting[57 + 2 * effect.Id] : 0;
            KreoProtocol.SetLighting(edited, model, effect.Id, Math.Clamp(storedOptions >> 4, 0, 4), storedSpeed, Math.Clamp(storedOptions & 15, 0, 7));
            updating = true; LoadEffectParameters(); updating = false;
        }
        else KreoProtocol.SetLighting(edited, model, effect.Id, (int)Brightness.Value, (int)Speed.Value, ColorMode.SelectedIndex == 1 ? 7 : colorSlot);
        Changed(); UpdateSwatches();
    }

    private void ColorModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating || edited == null || EffectPicker.SelectedItem is not KreoEffect effect || !effect.Color) return;
        KreoProtocol.SetLighting(edited, model, effect.Id, (int)Brightness.Value, (int)Speed.Value, ColorMode.SelectedIndex == 1 ? 7 : colorSlot);
        Changed();
    }

    private void EffectHexChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || updating || edited == null || EffectPicker.SelectedItem is not KreoEffect effect || !effect.Color) return;
        try
        {
            var rgb = KreoProtocol.ParseHex(EffectHex.Text);
            KreoProtocol.SetEffectColor(edited, model, effect.Id, colorSlot, rgb);
            updating = true; ColorMode.SelectedIndex = 0; updating = false;
            EffectColorError.Text = ""; EffectColorPreview.Background = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2]));
            Changed(); UpdateSwatches();
        }
        catch (FormatException) { EffectColorError.Text = "Use six hex digits, e.g. #769BFF."; UpdateAvailability(); }
    }

    private void UpdateSwatches()
    {
        EffectSwatches.Children.Clear();
        if (edited == null || EffectPicker.SelectedItem is not KreoEffect effect || !effect.Color) return;
        for (var slot = 0; slot < 7; slot++)
        {
            var index = slot; var offset = effect.Id * 21 + slot * 3;
            var rgb = edited.EffectPalette!.AsSpan(offset, 3).ToArray();
            var button = new Button { Style = (Style)FindResource("Swatch"), Width = 19, Height = 19, Padding = new Thickness(0), Margin = new Thickness(0, 2, 3, 2), Background = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2])), Content = slot == colorSlot && ColorMode.SelectedIndex == 0 ? "✓" : "", Foreground = rgb.Sum(b => b) > 460 ? Brushes.Black : Brushes.White, ToolTip = "#" + Convert.ToHexString(rgb) };
            button.Click += (_, _) =>
            {
                colorSlot = index; updating = true; EffectHex.Text = "#" + Convert.ToHexString(rgb); ColorMode.SelectedIndex = 0; updating = false;
                EffectColorError.Text = "";
                KreoProtocol.SetLighting(edited!, model, effect.Id, (int)Brightness.Value, (int)Speed.Value, colorSlot);
                Changed(); UpdateSwatches();
            };
            EffectSwatches.Children.Add(button);
        }
        var active = edited.EffectPalette!.AsSpan(effect.Id * 21 + colorSlot * 3, 3); EffectColorPreview.Background = new SolidColorBrush(Color.FromRgb(active[0], active[1], active[2]));
    }

    private void PaintHexChanged(object sender, TextChangedEventArgs e) { if (!ready) return; UpdatePaintPreview(); UpdateAvailability(); }
    private void UpdatePaintPreview()
    {
        try { var rgb = KreoProtocol.ParseHex(PaintColor.Text); PaintColorPreview.Background = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2])); PaintError.Text = ""; }
        catch (FormatException) { PaintError.Text = "Use six hex digits, e.g. #769BFF."; }
    }
    private void Paint(bool all)
    {
        if (edited == null || !all && selected == null) return;
        try
        {
            var rgb = KreoProtocol.ParseHex(PaintColor.Text);
            foreach (var key in all ? model.Keys : new[] { selected! }) KreoProtocol.Paint(edited, key.Index, rgb[0], rgb[1], rgb[2]);
            KreoProtocol.SetLighting(edited, model, 21, 4, 0, 0); LoadControls(); Changed();
        }
        catch (FormatException) { PaintError.Text = "Enter a valid hex color first."; }
    }
    private void PaintKey(object sender, RoutedEventArgs e) => Paint(false);
    private void PaintAll(object sender, RoutedEventArgs e) => Paint(true);

    private void TargetChanged(object sender, SelectionChangedEventArgs e) { if (ready) { UpdateMappingPreview(); UpdateAvailability(); } }
    private void UpdateMappingPreview()
    {
        MappingPreview.Text = wireless ? "Key assignments are saved over the USB cable for now. Connect it and switch the keyboard to wired mode to remap keys." : selected == null ? "First select a key on the keyboard." : TargetKey.SelectedItem is not KreoAction target ? "Choose its new action." : selected.Label + " will send " + target.Label + " after you apply.";
    }
    private void AssignKey(object sender, RoutedEventArgs e)
    {
        if (edited == null || selected == null || selected.VirtualKey == 0xfa || TargetKey.SelectedItem is not KreoAction target) return;
        KreoProtocol.Remap(edited, selected.Index, target.Bytes); Changed();
    }
    private void ResetKey(object sender, RoutedEventArgs e)
    {
        if (edited == null || selected == null || selected.VirtualKey == 0xfa) return;
        KreoProtocol.Remap(edited, selected.Index, KreoActions.ForKey(selected)); SelectKey(selected); Changed();
    }
    private void UndoKeys(object sender, RoutedEventArgs e) { if (edited != null && baseline != null) { baseline.Keys.CopyTo(edited.Keys, 0); Changed(); } }
    private void TestFocused(object sender, KeyboardFocusChangedEventArgs e) { KeyTest.Text = "Press the physical key now…"; }
    private void TestKeyPressed(object sender, KeyEventArgs e) { var key = e.Key == Key.System ? e.SystemKey : e.Key; KeyTest.Text = (Keyboard.Modifiers == ModifierKeys.None ? "" : Keyboard.Modifiers + " + ") + key; e.Handled = true; }

    private int PendingChanges()
    {
        if (edited == null || baseline == null) return 0;
        var count = model.Keys.Count(k => !edited.Keys.AsSpan(k.Index * 4, 4).SequenceEqual(baseline.Keys.AsSpan(k.Index * 4, 4)));
        count += model.Keys.Count(k => edited.Colors[k.Index] != baseline.Colors[k.Index] || edited.Colors[k.Index + 126] != baseline.Colors[k.Index + 126] || edited.Colors[k.Index + 252] != baseline.Colors[k.Index + 252]);
        if (!edited.Lighting.SequenceEqual(baseline.Lighting)) count++;
        if (!edited.EffectPalette!.AsSpan(0, 420).SequenceEqual(baseline.EffectPalette!.AsSpan(0, 420))) count++;
        return count;
    }
    private void Changed() { UpdateKeys(); UpdateAvailability(); KeyboardDetail.Text = "Edits are staged. Apply to keyboard saves them to the device."; }
    private void UpdateAvailability()
    {
        if (!ready) return;
        var connected = edited != null && device != null;
        Editors.IsEnabled = connected && !busy; Board.IsEnabled = !busy;
        ReadButton.IsEnabled = !busy; ModelPicker.IsEnabled = !connected && !busy;
        ReadButton.Content = connected ? "Refresh" : "Connect";
        LayoutPreview.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        var effect = EffectPicker.SelectedItem as KreoEffect;
        Brightness.IsEnabled = effect?.Brightness == true; Speed.IsEnabled = effect?.Speed == true;
        ColorMode.IsEnabled = EffectSwatches.IsEnabled = EffectHex.IsEnabled = effect?.Color == true;
        BrightnessLabel.Text = (Brightness.Value * 25).ToString("0") + "%"; SpeedLabel.Text = (Speed.Value + 1).ToString("0") + " / 5";
        EffectHint.Text = effect?.Id == 0 ? "Keyboard lighting is off." : effect?.Id == 21 ? "Use the per-key RGB controls to paint the keyboard." : effect?.Color != true ? "This effect uses its built-in colors." : "Effects keep their own speed, brightness and palette.";
        EffectColorHint.Text = effect?.Color != true ? "Custom color is unavailable for this effect." : "Hex edits change this effect’s color without switching to per-key mode.";
        PaintSelectedButton.IsEnabled = selected != null && PaintError.Text.Length == 0; PaintAllButton.IsEnabled = PaintError.Text.Length == 0;
        AssignButton.IsEnabled = connected && selected != null && selected.VirtualKey != 0xfa && TargetKey.SelectedItem is KreoAction target && !edited!.Keys.AsSpan(selected.Index * 4, 4).SequenceEqual(target.Bytes);
        var changes = PendingChanges();
        ChangeSummary.Text = changes == 0 ? "No pending changes" : changes + (changes == 1 ? " pending change" : " pending changes");
        ApplyButton.IsEnabled = connected && !busy && changes > 0 && (!(effect?.Color == true) || EffectColorError.Text.Length == 0);
        DiscardButton.IsEnabled = changes > 0 && !busy;
        if (wireless)
        {
            // Over 2.4 GHz only the lighting block is written; anything else waits for the cable.
            PaintSelectedButton.IsEnabled = PaintAllButton.IsEnabled = EffectHex.IsEnabled = AssignButton.IsEnabled = false;
            var needsCable = edited != null && baseline != null && (!edited.Keys.SequenceEqual(baseline.Keys) || !edited.Colors.SequenceEqual(baseline.Colors) || !edited.EffectPalette!.SequenceEqual(baseline.EffectPalette!));
            if (needsCable) { ApplyButton.IsEnabled = false; KeyboardDetail.Text = "These edits include key assignments or colors, which need the USB cable. Discard them or connect the cable."; }
        }
    }
    private void DiscardEdits(object sender, RoutedEventArgs e) { if (baseline == null) return; edited = baseline.Copy(); LoadControls(); UpdateKeys(); UpdateAvailability(); KeyboardDetail.Text = "Edits discarded. The keyboard was not changed."; }

    private string BackupPath => Path.Combine(dataDirectory, model.Name.Replace(' ', '-') + "-backup.json");
    private static void Save(string path, KreoState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true);
    }
    private KreoState Load(string path)
    {
        if (new FileInfo(path).Length > 20000) throw new IOException("This profile is too large.");
        var profile = JsonSerializer.Deserialize<KreoState>(File.ReadAllText(path)) ?? throw new IOException("Empty keyboard profile.");
        return KreoProtocol.Import(profile, baseline!);
    }
    private void ProfileAction(Action action) { try { action(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { KeyboardDetail.Text = error.Message; } }
    private void SaveProfile(object sender, RoutedEventArgs e) => ProfileAction(() => { if (wireless) { KeyboardDetail.Text = "Profiles need the USB cable: per-key colors can't be read over 2.4 GHz, so a profile saved or loaded now could be incomplete."; return; } if (edited == null) return; var dialog = new SaveFileDialog { Filter = "Sora keyboard profile|*.json", FileName = model.Name + " setup.json" }; if (dialog.ShowDialog() == true) { Save(dialog.FileName, edited); KeyboardDetail.Text = "Profile saved."; } });
    private void LoadProfile(object sender, RoutedEventArgs e) => ProfileAction(() => { if (wireless) { KeyboardDetail.Text = "Profiles need the USB cable: per-key colors can't be read over 2.4 GHz, so a profile saved or loaded now could be incomplete."; return; } var dialog = new OpenFileDialog { Filter = "Sora keyboard profile|*.json" }; if (dialog.ShowDialog() == true) { edited = Load(dialog.FileName); LoadControls(); Changed(); } });
    private void RestoreBackup(object sender, RoutedEventArgs e) => ProfileAction(() => { if (wireless) { KeyboardDetail.Text = "Profiles need the USB cable: per-key colors can't be read over 2.4 GHz, so a profile saved or loaded now could be incomplete."; return; } edited = Load(BackupPath); LoadControls(); Changed(); KeyboardDetail.Text = "Backup loaded. Apply to keyboard restores it."; });

    private async void ApplyChanges(object sender, RoutedEventArgs e) => await ApplyAsync();
    public Task ApplyAsync()
    {
        if (busy || device == null || edited == null || baseline == null || !ApplyButton.IsEnabled) return PendingOperation;
        var desired = edited.Copy(); var expected = baseline.Copy(); var selectedDevice = device; var backupPath = BackupPath;
        if (wireless)
        {
            // A separate lighting-only backup: a full profile would carry placeholder per-key colours.
            var lightingBackup = Path.Combine(dataDirectory, model.Name.Replace(' ', '-') + "-2.4GHz-lighting-backup.json");
            PendingOperation = RunOperation(async () =>
            {
                var lighting = await Task.Run(async () =>
                {
                    using var receiver = new KreoReceiver(selectedDevice);
                    return await receiver.ApplyLightingAsync(expected.Lighting, desired.Lighting, current =>
                    {
                        Directory.CreateDirectory(dataDirectory);
                        File.WriteAllText(lightingBackup, JsonSerializer.Serialize(new { model = model.Name, lighting = Convert.ToHexString(current) }));
                    });
                });
                baseline = expected with { Lighting = lighting }; edited = baseline.Copy(); LoadControls(); UpdateKeys();
                SetStatus(model.Name + " · Saved over 2.4 GHz", "Good");
                KeyboardDetail.Text = "Lighting saved through the receiver; device readback matches.";
            });
            return PendingOperation;
        }
        PendingOperation = RunOperation(async () =>
        {
            var actual = await Task.Run(() => { using var keyboard = new KreoKeyboard(selectedDevice); return keyboard.Apply(expected, desired, state => Save(backupPath, state)); });
            baseline = actual; edited = actual.Copy(); LoadControls(); UpdateKeys();
            SetStatus(model.Name + " · Saved", "Good");
            KeyboardDetail.Text = "Device readback matches. To check a remap, use the test field in Key assignment.";
        });
        return PendingOperation;
    }

    public object RunEditorChecks()
    {
        if (edited == null || baseline == null) throw new IOException("Connect a keyboard before running editor checks.");
        var effects = EffectPicker.Items.Cast<KreoEffect>().ToArray();
        if (effects.Count(e => e.Name == "Off") != 1 || effects.Select(e => e.Name).Distinct().Count() != effects.Length) throw new IOException("Duplicate effect labels.");
        EffectPicker.SelectedItem = effects.Single(e => e.Id == 1);
        ((Button)EffectSwatches.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); EffectHex.Text = "#123ABC";
        if (edited.Lighting[10] != 1 || !edited.EffectPalette!.AsSpan(21, 3).SequenceEqual(Convert.FromHexString("123ABC"))) throw new IOException("Effect hex did not update its palette.");
        var a = model.Keys.Single(k => k.Label == "A"); SelectKey(a);
        TargetKey.SelectedItem = TargetKey.Items.Cast<KreoAction>().Single(k => k.Label == "B");
        AssignButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!edited.Keys.AsSpan(a.Index * 4, 4).SequenceEqual(KreoActions.ForVirtualKey(0x42)) || edited.Keys[510] != 0x5a || edited.Keys[511] != 0xa5) throw new IOException("Assignment did not produce a complete key-map block.");
        if (keyButtons.Count != layout.Count || keyButtons[a.Index].Tag as string != "Selected" || keyButtons.Values.Count(b => b.Tag as string == "Selected") != 1) throw new IOException("Key selection is not shown on exactly one keycap.");
        var mappingBeforePaint = edited.Keys.ToArray();
        PaintColor.Text = "#F012AB"; PaintSelectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!edited.Keys.SequenceEqual(mappingBeforePaint)) throw new IOException("Painting changed a key assignment.");
        if (edited.Colors[a.Index] != 0xf0 || edited.Colors[a.Index + 126] != 0x12 || edited.Colors[a.Index + 252] != 0xab || edited.Lighting[10] != 21) throw new IOException("Per-key hex editor failed.");
        keyButtons[a.Index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (selected != null || keyButtons.Values.Any(b => b.Tag != null) || PaintSelectedButton.IsEnabled) throw new IOException("Clicking the selected key did not clear the selection.");
        var changes = PendingChanges();
        DiscardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (PendingChanges() != 0 || ApplyButton.IsEnabled) throw new IOException("Discard did not reset the editor.");
        return new { uniqueEffectLabels = true, singleOff = true, effectHex = true, completeKeyMap = true, perKeyHex = true, paintKeepsMapping = true, singleSelection = true, toggleDeselect = true, keycaps = keyButtons.Count, stagedChanges = changes, discard = true, configurationWrites = 0 };
    }

    public void PreparePreview(string scenario)
    {
        if (edited == null) return;
        SelectKey(model.Keys.Single(k => k.Label == "A"));
        if (scenario == "assignment") { ModeAssign.IsChecked = true; TargetKey.SelectedItem = TargetKey.Items.Cast<KreoAction>().Single(k => k.Label == "B"); AssignButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        else if (scenario == "effects") EffectPicker.IsDropDownOpen = true;
        else if (scenario == "paint")
        {
            // Staged only, for screenshots: a blue keyboard with a few painted keys. Nothing is applied.
            PaintColor.Text = "#0A84FF"; PaintAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var (label, hex) in new[] { ("W", "#FF9F0A"), ("A", "#FF9F0A"), ("S", "#FF9F0A"), ("D", "#FF9F0A"), ("Esc", "#FF453A") })
            { SelectKey(model.Keys.Single(k => k.Label == label)); PaintColor.Text = hex; PaintSelectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            SelectKey(model.Keys.Single(k => k.Label == "A"));
        }
    }
}
