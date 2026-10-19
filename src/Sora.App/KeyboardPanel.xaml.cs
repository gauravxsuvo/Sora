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
    // The receiver is there but the keyboard did not answer: retried quietly while the page is open, until it does.
    private bool notResponding, retrying;
    private readonly System.Windows.Threading.DispatcherTimer wakeRetry = new() { Interval = TimeSpan.FromSeconds(8) };
    // Connected through the 2.4 GHz receiver: lighting is editable; per-key colours can't be read, keys and palette aren't written yet.
    private bool wireless;
    private int? battery;
    private sealed class KeyboardDisconnectedException() : IOException("Connect a supported keyboard in wired USB mode to read its settings. You can explore the layout below.");
    // Per-key colours are remembered from the last USB read so the board can show them over 2.4 GHz.
    private DateTimeOffset? colorsSavedAt;
    private readonly Dictionary<int, Button> keyButtons = new();
    private readonly string dataDirectory = Path.Combine(SoraData.DirectoryPath, "keyboard");
    public Task PendingOperation { get; private set; } = Task.CompletedTask;
    public bool HasSnapshot => baseline != null;
    public bool IsBusy => busy;
    public bool HasPendingEdits => PendingChanges() > 0;
    // Short status for the sidebar; the page header chip carries the same tone.
    public string SidebarStatus => busy && !retrying ? "Kreo · Working…" : wirelessOnly ? "Kreo · 2.4 GHz receiver" : statusTone == "Bad" ? "Kreo · Needs attention" : baseline != null ? model.Name + (wireless ? " · 2.4 GHz" : " · USB") : "Kreo · Not connected";
    public string StatusTone => busy && !retrying ? "Warn" : statusTone;

    private void SetStatus(string text, string tone)
    {
        statusTone = tone; KeyboardStatus.Text = text; KeyboardDot.Fill = (Brush)FindResource(tone);
    }

    public KeyboardPanel()
    {
        InitializeComponent();
        ModelPicker.ItemsSource = KreoModel.All; ModelPicker.SelectedItem = model;
        BuildBrushSwatches();
        ready = true; DrawBoard(); UpdateAvailability();
        wakeRetry.Tick += async (_, _) => { if (notResponding && !busy && IsVisible && PendingChanges() == 0) await ReadAsync(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) wakeRetry.Start(); else wakeRetry.Stop(); };
        Unloaded += (_, _) => wakeRetry.Stop();
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
        if (effect == 21) return wireless && colorsSavedAt == null ? null : Color.FromRgb(edited.Colors[key.Index], edited.Colors[key.Index + 126], edited.Colors[key.Index + 252]);
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
            parts.Glow.Background = lit is { } glow ? new SolidColorBrush(Color.FromArgb(64, glow.R, glow.G, glow.B)) : null;
            parts.Legend.Foreground = lit is { } tint ? new SolidColorBrush(Mix(tint, Colors.White, 0.55)) : new SolidColorBrush(edited == null || edited.Lighting[10] != 0 ? Color.FromRgb(0xD1, 0xD1, 0xD6) : Color.FromRgb(0xBE, 0xC0, 0xC6));
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
        BoardHint.Text = edited == null ? name != null ? "Selected key: " + name + " · layout preview" : "Layout preview · select a key to inspect"
            : ModeProfiles.IsChecked == true ? "Profiles cover the whole keyboard"
            : name != null ? "Selected key: " + name
            : wireless && !assigning && name == null ? "Painting single keys needs the USB cable"
            : assigning ? "Click a key to change what it sends" : "Click a key to paint it";
        ClearSelectionButton.Visibility = selected != null && ModeProfiles.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        PaintTarget.Text = name == null ? "No key selected" : "Selected key: " + name;
        PaintSelectedButton.Content = name == null ? "Paint selected key" : "Paint " + name;
        PaintAllButton.Content = "Fill whole keyboard";
        PaintAllButton.ToolTip = "Fills every model colour slot, including auxiliary slots in the vendor definition.";
        if (wireless) PaintTarget.Text = "Painting needs USB";
        PaintHint.Text = wireless ? "Per-key painting requires wired USB; receiver lighting effects remain available." : "Colour edits stage the selected key; Apply saves them.";
        SelectionHint.Text = selected == null ? "Select a key in the keyboard above to choose its colour." : "Brush colour will be staged for " + name + ".";
        SelectionHint.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
        if (selected != null && edited != null && (!wireless || colorsSavedAt != null))
        {
            KeyColorRow.Visibility = Visibility.Visible;
            var stored = Color.FromRgb(edited.Colors[selected.Index], edited.Colors[selected.Index + 126], edited.Colors[selected.Index + 252]);
            KeyColorSwatch.Background = new SolidColorBrush(stored);
            KeyColorInfo.Text = wireless ? "Color at last USB connection #" + Hex(stored)
                : edited.Lighting[10] == 21 ? "Current color #" + Hex(stored) : "Stored color #" + Hex(stored) + " · shows in Custom per-key";
        }
        else KeyColorRow.Visibility = Visibility.Collapsed;
        AssignEmpty.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
        AssignEditor.Visibility = selected == null ? Visibility.Collapsed : Visibility.Visible;
        ActionLabel.Text = selected == null ? "" : edited == null ? name + " · layout preview" : name + " sends " + KreoActions.Describe(model, edited.Keys.AsSpan(selected.Index * 4, 4).ToArray());
        SyncSelectedColour();
        UpdateMappingPreview(); UpdatePaintPreview();
    }

    private void SyncSelectedColour()
    {
        if (selected == null || edited == null || wireless && colorsSavedAt == null) return;
        var previous = updating; updating = true;
        try { PaintColor.Text = "#" + Hex(Color.FromRgb(edited.Colors[selected.Index], edited.Colors[selected.Index + 126], edited.Colors[selected.Index + 252])); }
        finally { updating = previous; }
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb((byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));
    private static string Hex(Color color) => Convert.ToHexString(new[] { color.R, color.G, color.B });

    // Gives the keyboard card at least 300 px; the editors take what remains and scroll beyond that.
    private void PanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var fixedHeight = ((FrameworkElement)Content is Grid root ? root.Children.OfType<FrameworkElement>().Where(c => c.Visibility != Visibility.Collapsed && Grid.GetRow(c) is 0 or 1 or 5).Sum(c => c.ActualHeight + c.Margin.Top + c.Margin.Bottom) : 0) + 16;
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
            var swatch = new Button { Style = (Style)FindResource("KeyboardSwatch"), Width = 20, Height = 20, Padding = new Thickness(0), Margin = new Thickness(0, 2, 5, 2), Background = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2])), ToolTip = hex };
            AutomationProperties.SetName(swatch, "Brush " + hex);
            swatch.Click += (_, _) => PaintColor.Text = hex;
            BrushSwatches.Children.Add(swatch);
        }
    }
    private void ModelChanged(object sender, SelectionChangedEventArgs e) { if (!ready || updating || edited != null || ModelPicker.SelectedItem is not KreoModel choice) return; model = choice; selected = null; DrawBoard(); UpdateAvailability(); }

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
                if (devices.Count == 0) throw new KeyboardDisconnectedException();
                if (devices.Count != 1) throw new IOException("Connect one Kreo keyboard at a time.");
                using var keyboard = new KreoKeyboard(devices[0]);
                return (Device: devices[0], Model: keyboard.Definition, State: keyboard.ReadState(), Wireless: false, Battery: (int?)null);
            });
            wirelessOnly = false; wireless = result.Wireless; battery = result.Battery;
            device = result.Device; model = result.Model; baseline = result.State; selected = null;
            if (wireless) { colorsSavedAt = LoadRememberedColors(baseline); }
            else { colorsSavedAt = null; RememberColors(baseline); }
            edited = baseline.Copy();
            updating = true; ModelPicker.SelectedItem = model; TargetKey.ItemsSource = KreoActions.ForModel(model); updating = false;
            LoadControls(); DrawBoard();
            SetStatus(model.Name + (wireless ? " · 2.4 GHz" + (battery is { } level ? $" · {level}%" : "") : " · USB"), "Good");
            KeyboardDetail.Text = !wireless ? "Ready. Changes stay in the editor until you apply them."
                : colorsSavedAt is { } saved ? $"Connected through the 2.4 GHz receiver. Key colors are from your last USB connection ({saved.ToLocalTime():d MMM, HH:mm}); the receiver can't report them."
                : "Connected through the 2.4 GHz receiver. Connect the USB cable once so Sora can show per-key colors; the receiver can't report them.";
        });
        return PendingOperation;
    }

    private async void ReadKeyboard(object sender, RoutedEventArgs e)
    {
        if (PendingChanges() > 0 && !AppDialog.Confirm(Window.GetWindow(this), "Refresh keyboard?", "Refreshing will discard the staged edits in this window. The keyboard is not changed by refreshing.", "Refresh keyboard", "Keep edits")) return;
        await ReadAsync();
    }

    private async Task RunOperation(Func<Task> action, bool applying = false)
    {
        // A quiet retry keeps the "not responding" chip instead of flashing "Working…".
        retrying = notResponding;
        busy = true; UpdateAvailability(); if (!retrying) SetStatus("Working…", "Warn");
        ControlState.SetIsBusy(applying ? ApplyButton : ReadButton, true);
        notResponding = false;
        try { await action(); }
        catch (KeyboardDisconnectedException error)
        {
            device = null; wirelessOnly = false; wireless = false;
            SetStatus("Not connected", "Idle");
            KeyboardDetail.Text = edited == null ? error.Message : "Editing the last read settings offline. Reconnect and read the keyboard before applying; profiles can still be saved.";
        }
        catch (KreoReceiverTimeoutException error)
        {
            // Not a fault: the keyboard is asleep or out of range. Sora asks again every few seconds while this page is open.
            wirelessOnly = true; wireless = false; device = null; notResponding = true;
            SetStatus("2.4 GHz · waiting for the keyboard", "Warn"); KeyboardDetail.Text = error.Message;
        }
        catch (KreoWirelessOnlyException error)
        {
            // Staged edits are kept, but nothing can be applied until the cable is back.
            wirelessOnly = true; wireless = false; device = null;
            SetStatus("2.4 GHz · settings need USB", "Warn"); KeyboardDetail.Text = error.Message;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { device = null; wirelessOnly = false; SetStatus("Connection error", "Bad"); KeyboardDetail.Text = error.Message; }
        finally { ControlState.SetIsBusy(ReadButton, false); ControlState.SetIsBusy(ApplyButton, false); busy = false; retrying = false; UpdateKeys(); UpdateAvailability(); }
    }

    private void LoadControls()
    {
        if (edited == null) return;
        updating = true;
        EffectPicker.ItemsSource = model.Effects.Concat(new[] { new KreoEffect(21, "Custom per-key", false, false, false), new KreoEffect(0, "Off", false, false, false) }).ToArray();
        EffectPicker.SelectedItem = EffectPicker.Items.Cast<KreoEffect>().FirstOrDefault(e => e.Id == edited.Lighting[10]);
        if (EffectPicker.SelectedItem == null)
        {
            EffectPicker.ItemsSource = EffectPicker.Items.Cast<KreoEffect>().Append(new KreoEffect(edited.Lighting[10], "Device effect " + edited.Lighting[10], false, false, false)).ToArray();
            EffectPicker.SelectedIndex = EffectPicker.Items.Count - 1;
        }
        LoadEffectParameters(); updating = false; UpdateAvailability(); UpdateSwatches();
    }

    private void LoadEffectParameters()
    {
        if (edited == null) return;
        var id = edited.Lighting[10];
        ColorMode.SelectedIndex = 0;
        if (id is > 0 and <= 18)
        {
            Speed.Value = Math.Clamp((int)edited.Lighting[56 + 2 * id], 0, 4);
            Brightness.Value = Math.Clamp(edited.Lighting[57 + 2 * id] >> 4, 0, 4);
            var slot = edited.Lighting[57 + 2 * id] & 15; colorSlot = slot < 7 ? slot : 0; ColorMode.SelectedIndex = slot == 7 ? 1 : 0;
            var offset = id * 21 + colorSlot * 3; EffectHex.Text = "#" + Convert.ToHexString(edited.EffectPalette!.AsSpan(offset, 3));
        }
        else { EffectHex.Text = "—"; EffectColorPreview.Background = (Brush)FindResource("InsetBrush"); }
        ControlState.SetValidation(EffectHex, EffectColorError, null);
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
            ControlState.SetValidation(EffectHex, EffectColorError, null); EffectColorPreview.Background = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2]));
            Changed(); UpdateSwatches();
        }
        catch (FormatException) { ControlState.SetValidation(EffectHex, EffectColorError, ControlState.HexColourError); UpdateAvailability(); }
    }

    private void UpdateSwatches()
    {
        EffectSwatches.Children.Clear();
        if (edited == null || EffectPicker.SelectedItem is not KreoEffect effect || !effect.Color) return;
        for (var slot = 0; slot < 7; slot++)
        {
            var index = slot; var offset = effect.Id * 21 + slot * 3;
            var rgb = edited.EffectPalette!.AsSpan(offset, 3).ToArray();
            var button = new Button { Style = (Style)FindResource("KeyboardSwatch"), Width = 20, Height = 20, Padding = new Thickness(0), Margin = new Thickness(0, 2, 4, 2), Background = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2])), Content = slot == colorSlot && ColorMode.SelectedIndex == 0 ? "✓" : "", Foreground = rgb.Sum(b => b) > 460 ? Brushes.Black : Brushes.White, ToolTip = "#" + Convert.ToHexString(rgb) };
            button.Click += (_, _) =>
            {
                colorSlot = index; updating = true; EffectHex.Text = "#" + Convert.ToHexString(rgb); ColorMode.SelectedIndex = 0; updating = false;
                ControlState.SetValidation(EffectHex, EffectColorError, null);
                KreoProtocol.SetLighting(edited!, model, effect.Id, (int)Brightness.Value, (int)Speed.Value, colorSlot);
                Changed(); UpdateSwatches();
            };
            EffectSwatches.Children.Add(button);
        }
        var active = edited.EffectPalette!.AsSpan(effect.Id * 21 + colorSlot * 3, 3); EffectColorPreview.Background = new SolidColorBrush(Color.FromRgb(active[0], active[1], active[2]));
    }

    private void PaintHexChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || updating) return;
        UpdatePaintPreview();
        if (PaintError.Text.Length == 0 && selected != null && edited != null && !wireless && !busy)
        {
            var rgb = KreoProtocol.ParseHex(PaintColor.Text);
            if (edited.Colors[selected.Index] != rgb[0] || edited.Colors[selected.Index + 126] != rgb[1] || edited.Colors[selected.Index + 252] != rgb[2])
            { Paint(false); return; }
        }
        UpdateAvailability();
    }
    private void UpdatePaintPreview()
    {
        try
        {
            var rgb = KreoProtocol.ParseHex(PaintColor.Text); var colour = Color.FromRgb(rgb[0], rgb[1], rgb[2]);
            ControlState.SetValidation(PaintColor, PaintError, null);
            PaintColorPreview.Background = new SolidColorBrush(colour);
            foreach (Button swatch in BrushSwatches.Children)
            {
                swatch.Content = swatch.Background is SolidColorBrush brush && brush.Color == colour ? "✓" : "";
                swatch.Foreground = swatch.Background is SolidColorBrush background && background.Color.R + background.Color.G + background.Color.B > 460 ? Brushes.Black : Brushes.White;
            }
        }
        catch (FormatException) { ControlState.SetValidation(PaintColor, PaintError, ControlState.HexColourError); foreach (Button swatch in BrushSwatches.Children) swatch.Content = ""; }
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
        MappingPreview.Text = selected == null ? "First select a key on the keyboard." : TargetKey.SelectedItem is not KreoAction target ? "Choose its new action." : selected.Label + " will send " + target.Label + " after you apply.";
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

    private (int Colours, int Assignments, int Lighting, int Palette) PendingBreakdown()
    {
        if (edited == null || baseline == null) return (0, 0, 0, 0);
        return (
            model.Keys.Count(k => edited.Colors[k.Index] != baseline.Colors[k.Index] || edited.Colors[k.Index + 126] != baseline.Colors[k.Index + 126] || edited.Colors[k.Index + 252] != baseline.Colors[k.Index + 252]),
            model.Keys.Count(k => !edited.Keys.AsSpan(k.Index * 4, 4).SequenceEqual(baseline.Keys.AsSpan(k.Index * 4, 4))),
            edited.Lighting.SequenceEqual(baseline.Lighting) ? 0 : 1,
            edited.EffectPalette!.AsSpan(0, 420).SequenceEqual(baseline.EffectPalette!.AsSpan(0, 420)) ? 0 : 1);
    }
    private int PendingChanges() { var parts = PendingBreakdown(); return parts.Colours + parts.Assignments + parts.Lighting + parts.Palette; }
    private string PendingDescription()
    {
        if (edited == null || baseline == null) return "";
        var parts = PendingBreakdown(); var labels = new List<string>();
        var auxiliaryColours = model.Keys.Count(k => (k.Width <= 0 || k.Height <= 0) && (edited!.Colors[k.Index] != baseline!.Colors[k.Index] || edited.Colors[k.Index + 126] != baseline.Colors[k.Index + 126] || edited.Colors[k.Index + 252] != baseline.Colors[k.Index + 252]));
        var keyColours = parts.Colours - auxiliaryColours;
        if (keyColours > 0) labels.Add(keyColours + " key colour" + (keyColours == 1 ? "" : "s"));
        if (auxiliaryColours > 0) labels.Add(auxiliaryColours + " auxiliary colour slot" + (auxiliaryColours == 1 ? "" : "s"));
        if (parts.Assignments > 0) labels.Add(parts.Assignments + " key assignment" + (parts.Assignments == 1 ? "" : "s"));
        if (parts.Lighting > 0) labels.Add("1 effect settings group");
        if (parts.Palette > 0) labels.Add("1 palette group");
        return string.Join(" · ", labels);
    }
    private void Changed() { UpdateKeys(); UpdateAvailability(); KeyboardDetail.Text = device == null ? "Offline edits are staged. Save a profile or reconnect and read the keyboard before applying." : "Edits are staged. Apply to keyboard saves them to the device."; }
    private void UpdateAvailability()
    {
        if (!ready) return;
        var connected = edited != null && device != null;
        var canEdit = edited != null && !busy;
        Editors.IsEnabled = !busy; Board.IsEnabled = !busy;
        ReadButton.IsEnabled = !busy; ModelPicker.IsEnabled = edited == null && !busy;
        ReadButton.Content = connected ? "Refresh" : "Connect";
        LayoutPreview.Visibility = edited != null ? Visibility.Collapsed : Visibility.Visible;
        var effect = EffectPicker.SelectedItem as KreoEffect;
        EffectPicker.IsEnabled = canEdit;
        EffectPlaceholder.Visibility = effect == null ? Visibility.Visible : Visibility.Collapsed;
        Brightness.IsEnabled = canEdit && effect?.Brightness == true; Speed.IsEnabled = canEdit && effect?.Speed == true;
        Brightness.Visibility = edited != null && effect?.Brightness == true ? Visibility.Visible : Visibility.Collapsed;
        Speed.Visibility = edited != null && effect?.Speed == true ? Visibility.Visible : Visibility.Collapsed;
        BrightnessUnavailable.Visibility = Brightness.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        SpeedUnavailable.Visibility = Speed.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        BrightnessUnavailable.Text = SpeedUnavailable.Text = edited == null ? "Not read yet" : "Not used by this effect";
        ColorMode.IsEnabled = EffectSwatches.IsEnabled = EffectHex.IsEnabled = canEdit && effect?.Color == true;
        EffectColorPreview.Opacity = EffectHex.IsEnabled ? 1 : 0.4;
        BrightnessLabel.Text = effect?.Brightness == true ? (Brightness.Value * 25).ToString("0") + "%" : "—";
        SpeedLabel.Text = effect?.Speed == true ? (Speed.Value + 1).ToString("0") + " / 5" : "—";
        EffectHint.Text = effect == null ? "Effect values become available after a keyboard read." : effect.Id == 0 ? "Keyboard lighting is off." : effect.Id == 21 ? "Use per-key colour to paint the keyboard." : !effect.Color ? "This effect uses its built-in colours." : "Each effect keeps its own speed, brightness and palette.";
        EffectColorHint.Text = effect?.Color != true ? "Custom color is unavailable for this effect." : "Hex edits change this effect’s color without switching to per-key mode.";
        PaintColor.IsEnabled = BrushSwatches.IsEnabled = canEdit && !wireless;
        PaintColorPreview.Opacity = PaintColor.IsEnabled ? 1 : 0.4;
        PaintSelectedButton.IsEnabled = canEdit && selected != null && PaintError.Text.Length == 0; PaintAllButton.IsEnabled = canEdit && PaintError.Text.Length == 0;
        TargetKey.IsEnabled = canEdit && selected != null && selected.VirtualKey != 0xfa;
        TargetPlaceholder.Visibility = TargetKey.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;
        TargetPlaceholder.Text = edited == null ? "Read keyboard to view actions" : "Choose a new action";
        AssignButton.IsEnabled = canEdit && selected != null && selected.VirtualKey != 0xfa && TargetKey.SelectedItem is KreoAction target && !edited!.Keys.AsSpan(selected.Index * 4, 4).SequenceEqual(target.Bytes);
        ResetKeyButton.IsEnabled = canEdit && selected != null && selected.VirtualKey != 0xfa;
        UndoKeysButton.IsEnabled = canEdit && baseline != null && !edited!.Keys.SequenceEqual(baseline.Keys);
        KeyTest.IsEnabled = connected;
        SaveProfileButton.IsEnabled = LoadProfileButton.IsEnabled = canEdit && !wireless;
        RestoreBackupButton.IsEnabled = canEdit && !wireless && File.Exists(BackupPath);
        ProfilesHint.Text = edited == null ? "Read your keyboard once before saving or loading profiles; a complete device snapshot is required." : wireless ? "Full profiles require wired USB because the receiver cannot read per-key colours." : "Profiles can be edited and saved offline from this snapshot. Apply requires the connected keyboard.";
        var changes = PendingChanges();
        EditFooter.Visibility = edited == null ? Visibility.Collapsed : Visibility.Visible;
        ChangeSummary.Text = changes == 0 ? "No pending changes" : changes + (changes == 1 ? " staged change" : " staged changes");
        PendingDetails.Text = PendingDescription();
        PendingDetails.Visibility = changes == 0 ? Visibility.Collapsed : Visibility.Visible;
        ChangeSummary.ToolTip = "Counts changed key colours and assignments individually, plus one group each for effect settings and the effect palette. Repeated edits to the same value are not counted twice.";
        ApplyButton.IsEnabled = connected && !busy && changes > 0 && (!(effect?.Color == true) || EffectColorError.Text.Length == 0);
        DiscardButton.IsEnabled = changes > 0 && !busy;
        if (wireless)
        {
            // The receiver protocol has no per-key colour command (even KB Hub paints keys only over USB).
            PaintSelectedButton.IsEnabled = PaintAllButton.IsEnabled = false;
            var needsCable = edited != null && baseline != null && !edited.Colors.SequenceEqual(baseline.Colors);
            if (needsCable) { ApplyButton.IsEnabled = false; KeyboardDetail.Text = "These edits paint individual keys, which the 2.4 GHz receiver can't carry. Discard them or connect the USB cable."; }
        }
    }
    private void DiscardEdits(object sender, RoutedEventArgs e) { if (baseline == null) return; edited = baseline.Copy(); LoadControls(); UpdateKeys(); UpdateAvailability(); KeyboardDetail.Text = "Edits discarded. The keyboard was not changed."; }

    // Per-key colours from the last USB read, shown over 2.4 GHz where they can't be read.
    private string ColorMemoryPath => Path.Combine(dataDirectory, model.Name.Replace(' ', '-') + "-per-key-colors.json");
    private sealed record ColorMemory(string Model, DateTimeOffset Saved, string Colors);
    private void RememberColors(KreoState state)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(ColorMemoryPath, JsonSerializer.Serialize(new ColorMemory(model.Name, DateTimeOffset.Now, Convert.ToHexString(state.Colors))));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private DateTimeOffset? LoadRememberedColors(KreoState state)
    {
        try
        {
            if (!File.Exists(ColorMemoryPath)) return null;
            var memory = JsonSerializer.Deserialize<ColorMemory>(File.ReadAllText(ColorMemoryPath));
            var colors = memory == null ? null : Convert.FromHexString(memory.Colors);
            if (colors?.Length != state.Colors.Length || memory!.Model != model.Name) return null;
            colors.CopyTo(state.Colors, 0);
            return memory.Saved;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or FormatException) { return null; }
    }

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
            // Receiver backups hold only what the receiver can read; they are kept apart from full USB profiles.
            var receiverBackup = Path.Combine(dataDirectory, model.Name.Replace(' ', '-') + "-2.4GHz-backup.json");
            var applyModel = model;
            PendingOperation = RunOperation(async () =>
            {
                var actual = await Task.Run(async () =>
                {
                    using var receiver = new KreoReceiver(selectedDevice);
                    return await receiver.ApplyAsync(expected, desired, applyModel, saved =>
                    {
                        Directory.CreateDirectory(dataDirectory);
                        File.WriteAllText(receiverBackup, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
                    });
                });
                baseline = actual; edited = actual.Copy(); LoadControls(); UpdateKeys();
                SetStatus(model.Name + " · Saved over 2.4 GHz", "Good");
                KeyboardDetail.Text = "Saved through the receiver; device readback matches. To check a remap, use the test field in Key assignment.";
            }, applying: true);
            return PendingOperation;
        }
        PendingOperation = RunOperation(async () =>
        {
            var actual = await Task.Run(() => { using var keyboard = new KreoKeyboard(selectedDevice); return keyboard.Apply(expected, desired, state => Save(backupPath, state)); });
            baseline = actual; edited = actual.Copy(); RememberColors(actual); LoadControls(); UpdateKeys();
            SetStatus(model.Name + " · Saved", "Good");
            KeyboardDetail.Text = "Device readback matches. To check a remap, use the test field in Key assignment.";
        }, applying: true);
        return PendingOperation;
    }

    public object RunEditorChecks()
    {
        if (edited == null || baseline == null) throw new IOException("Connect a keyboard before running editor checks.");
        if (wireless) throw new IOException("Use wired USB for complete keyboard editor checks.");
        var readDevice = device; var readBaseline = baseline.Copy(); var readEdited = edited.Copy();
        var readWireless = wireless; var readStatus = KeyboardStatus.Text; var readTone = statusTone; var readDetail = KeyboardDetail.Text; var readBrush = PaintColor.Text;
        try
        {
            // Exercise the empty, disconnected UI without synthesizing any device settings.
            device = null; baseline = edited = null; wireless = false; selected = null;
            updating = true; EffectPicker.ItemsSource = null; TargetKey.ItemsSource = null; ColorMode.SelectedIndex = 0; updating = false;
            UpdateKeys(); UpdateAvailability();
            if (EffectPicker.IsEnabled || EffectPlaceholder.Visibility != Visibility.Visible || PaintAllButton.IsEnabled || PaintSelectedButton.IsEnabled || EditFooter.Visibility != Visibility.Collapsed) throw new IOException("Disconnected controls are misleading.");
            if (ColorMode.SelectedIndex != 0 || ColorMode.IsEnabled || Brightness.IsEnabled || Speed.IsEnabled || PaintColor.IsEnabled || BrushSwatches.IsEnabled) throw new IOException("Disconnected values or disabled states are incorrect.");
            if (Brightness.Visibility != Visibility.Collapsed || Speed.Visibility != Visibility.Collapsed || BrightnessUnavailable.Visibility != Visibility.Visible || SpeedUnavailable.Visibility != Visibility.Visible) throw new IOException("Unavailable sliders retain misleading thumbs.");
            ModeAssign.IsChecked = true;
            keyButtons[model.Keys.Single(k => k.Label == "A").Index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (Editors.SelectedIndex != 1 || AssignButton.IsEnabled || TargetKey.IsEnabled || selected?.Label != "A") throw new IOException("Disconnected layout inspection or assignment state failed.");
            ModeProfiles.IsChecked = true;
            if (Editors.SelectedIndex != 2 || SaveProfileButton.IsEnabled || LoadProfileButton.IsEnabled) throw new IOException("Profiles require a complete snapshot.");
            // Restore the actual read snapshot; all following operations are editor-only.
            device = readDevice; baseline = readBaseline.Copy(); edited = readEdited.Copy(); selected = null;
            TargetKey.ItemsSource = KreoActions.ForModel(model); ModeLighting.IsChecked = true; LoadControls(); UpdateKeys(); UpdateAvailability();
            var effects = EffectPicker.Items.Cast<KreoEffect>().ToArray();
            if (effects.Count(e => e.Name == "Off") != 1 || effects.Select(e => e.Name).Distinct().Count() != effects.Length) throw new IOException("Duplicate effect labels.");
            EffectPicker.SelectedItem = effects.Single(e => e.Id == 1);
            EffectPicker.IsDropDownOpen = true; UpdateLayout();
            if (EffectPicker.SelectedItem == null || EffectPicker.SelectionBoxItem == null || EffectPlaceholder.Visibility != Visibility.Collapsed) throw new IOException("Selected effect is not rendered.");
            EffectPicker.IsDropDownOpen = false;
            ColorMode.SelectedIndex = 1;
            if (ColorMode.SelectionBoxItem == null) throw new IOException("Colour mode is blank.");
            ColorMode.SelectedIndex = 0;
            EffectPicker.SelectedItem = effects.First(e => e.Brightness && e.Speed);
            Brightness.Value = Brightness.Value == 4 ? 3 : 4; Speed.Value = Speed.Value == 4 ? 2 : 4;
            if (PendingChanges() == 0) throw new IOException("Sliders did not stage lighting edits.");
            EffectPicker.SelectedItem = effects.Single(e => e.Id == 1);
            EffectHex.Text = "#NOPE";
            if (ApplyButton.IsEnabled || EffectColorError.Text.Length == 0) throw new IOException("Invalid effect colour allows Apply.");
            if (!ControlState.GetIsInvalid(EffectHex)) throw new IOException("Invalid effect colour has no shared validation state.");
            ((Button)EffectSwatches.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); EffectHex.Text = "#123ABC";
            if (ControlState.GetIsInvalid(EffectHex)) throw new IOException("Valid effect colour retains an invalid outline.");
            if (edited.Lighting[10] != 1 || !edited.EffectPalette!.AsSpan(21, 3).SequenceEqual(Convert.FromHexString("123ABC"))) throw new IOException("Effect hex did not update its palette.");
            var a = model.Keys.Single(k => k.Label == "A"); SelectKey(a);
            TargetKey.SelectedItem = TargetKey.Items.Cast<KreoAction>().Single(k => k.Label == "B");
            AssignButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!edited.Keys.AsSpan(a.Index * 4, 4).SequenceEqual(KreoActions.ForVirtualKey(0x42)) || edited.Keys[510] != 0x5a || edited.Keys[511] != 0xa5) throw new IOException("Assignment did not produce a complete key-map block.");
            if (keyButtons.Count != layout.Count || keyButtons[a.Index].Tag as string != "Selected" || keyButtons.Values.Count(b => b.Tag as string == "Selected") != 1) throw new IOException("Key selection is not shown on exactly one keycap.");
            keyButtons[a.Index].ApplyTemplate();
            if (BoardHint.Text != "Selected key: A" || PaintTarget.Text != "Selected key: A" || ((Border)keyButtons[a.Index].Template.FindName("SelectionHalo", keyButtons[a.Index])).Visibility != Visibility.Visible) throw new IOException("Selected-key wording or contrast outline is missing.");
            var mappingBeforePaint = edited.Keys.ToArray();
            var coloursBeforeInvalid = edited.Colors.ToArray();
            PaintColor.Text = "#NOPE";
            if (PaintSelectedButton.IsEnabled || PaintAllButton.IsEnabled) throw new IOException("Invalid brush colour allows painting.");
            if (!edited.Colors.SequenceEqual(coloursBeforeInvalid)) throw new IOException("Invalid input changed staged colours.");
            ((Button)BrushSwatches.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (PaintColor.Text != BrushColors[1]) throw new IOException("Brush palette did not update hex input.");
            var paletteColour = (Color)ColorConverter.ConvertFromString(BrushColors[1]);
            if (edited.Colors[a.Index] != paletteColour.R || edited.Colors[a.Index + 126] != paletteColour.G || edited.Colors[a.Index + 252] != paletteColour.B) throw new IOException("Palette did not stage the selected key.");
            if (((Button)BrushSwatches.Children[1]).Content as string != "✓" || (PaintColorPreview.Background as SolidColorBrush)?.Color != paletteColour) throw new IOException("Palette selection or preview is out of sync.");
            PaintColor.Text = "#F012AB"; PaintSelectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!edited.Keys.SequenceEqual(mappingBeforePaint)) throw new IOException("Painting changed a key assignment.");
            if (edited.Colors[a.Index] != 0xf0 || edited.Colors[a.Index + 126] != 0x12 || edited.Colors[a.Index + 252] != 0xab || edited.Lighting[10] != 21) throw new IOException("Per-key hex editor failed.");
            SelectKey(model.Keys.Single(k => k.Label == "B")); SelectKey(a);
            if (PaintColor.Text != "#F012AB" || !KeyColorInfo.Text.Contains("#F012AB") || KeyColor(a) != Color.FromRgb(0xf0, 0x12, 0xab)) throw new IOException("Selected colour representations diverged.");
            keyButtons[a.Index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (selected != null || keyButtons.Values.Any(b => b.Tag != null) || PaintSelectedButton.IsEnabled) throw new IOException("Clicking the selected key did not clear the selection.");
            var changes = PendingChanges();
            PaintAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (model.Keys.Any(k => edited.Colors[k.Index] != 0xf0 || edited.Colors[k.Index + 126] != 0x12 || edited.Colors[k.Index + 252] != 0xab)) throw new IOException("Fill all did not paint every model key.");
            DiscardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (PendingChanges() != 0 || ApplyButton.IsEnabled) throw new IOException("Discard did not reset the editor.");
            edited.Colors[a.Index] ^= 0xff;
            edited.Keys[a.Index * 4] ^= 0x01;
            edited.Lighting[10] ^= 0x01;
            edited.EffectPalette![0] ^= 0xff;
            if (PendingBreakdown() != (1, 1, 1, 1) || PendingChanges() != 4) throw new IOException("Pending breakdown does not match state differences.");
            edited = baseline.Copy(); LoadControls(); UpdateKeys();
            device = null; UpdateAvailability();
            if (!SaveProfileButton.IsEnabled || !LoadProfileButton.IsEnabled || !EffectPicker.IsEnabled || ApplyButton.IsEnabled) throw new IOException("Offline snapshot editing or profile availability failed.");
            EffectPicker.SelectedItem = effects.First(e => e.Brightness && e.Speed);
            if (!Brightness.IsEnabled || !Speed.IsEnabled || Brightness.Visibility != Visibility.Visible || Speed.Visibility != Visibility.Visible) throw new IOException("Offline snapshot sliders should remain editable for supported effects.");
            var roundTrip = JsonSerializer.Deserialize<KreoState>(JsonSerializer.Serialize(edited))!;
            KreoProtocol.Import(roundTrip, baseline);
            device = readDevice; wireless = true; UpdateAvailability();
            if (PaintAllButton.IsEnabled || PaintSelectedButton.IsEnabled || SaveProfileButton.IsEnabled || LoadProfileButton.IsEnabled || !EffectPicker.IsEnabled) throw new IOException("Receiver-only restrictions failed.");
            return new { connectedSnapshotRead = readDevice != null, disconnectedControls = true, tabNavigation = true, dropdownValues = true, sliders = true, invalidHex = true, effectHex = true, completeKeyMap = true, perKeyHex = true, brushPalette = true, selectedColourSynchronization = true, pendingBreakdown = true, unavailableThumbsHidden = true, paintKeepsMapping = true, fillAll = true, singleSelection = true, toggleDeselect = true, keycaps = keyButtons.Count, stagedChanges = changes, discard = true, offlineSnapshotProfiles = true, profileRoundTrip = true, receiverRestrictionsSimulated = true, configurationWrites = 0 };
        }
        finally
        {
            device = readDevice; baseline = readBaseline; edited = readEdited; wireless = readWireless; selected = null;
            ModeLighting.IsChecked = true; TargetKey.ItemsSource = KreoActions.ForModel(model);
            LoadControls(); PaintColor.Text = readBrush; UpdateKeys(); UpdateAvailability(); SetStatus(readStatus, readTone); KeyboardDetail.Text = readDetail;
        }
    }

    internal void ShowSection(string section)
    {
        if (section == "profiles") ModeProfiles.IsChecked = true;
        else if (section == "assignment") ModeAssign.IsChecked = true;
        else ModeLighting.IsChecked = true;
    }

    public void PreparePreview(string scenario)
    {
        if (edited == null) return;
        SelectKey(model.Keys.Single(k => k.Label == "A"));
        if (scenario == "assignment") { ModeAssign.IsChecked = true; TargetKey.SelectedItem = TargetKey.Items.Cast<KreoAction>().Single(k => k.Label == "B"); AssignButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        else if (scenario == "effects") EffectPicker.IsDropDownOpen = true;
        else if (scenario == "paint" || scenario == "paint-blue")
        {
            // Staged only, for screenshots: a blue keyboard with a few painted keys. Nothing is applied.
            PaintColor.Text = "#0A84FF"; PaintAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var (label, hex) in new[] { ("W", "#FF9F0A"), ("A", "#FF9F0A"), ("S", "#FF9F0A"), ("D", "#FF9F0A"), ("Esc", "#FF453A") })
            { SelectKey(model.Keys.Single(k => k.Label == label)); PaintColor.Text = hex; PaintSelectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            SelectKey(model.Keys.Single(k => k.Label == (scenario == "paint-blue" ? "F" : "A")));
        }
    }
}
