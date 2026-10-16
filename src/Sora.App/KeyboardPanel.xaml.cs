using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly Dictionary<int, Button> keyButtons = new();
    private readonly string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data", "keyboard");
    public Task PendingOperation { get; private set; } = Task.CompletedTask;
    public bool HasSnapshot => baseline != null;
    public bool IsBusy => busy;
    public bool HasPendingEdits => PendingChanges() > 0;
    // Short status for the sidebar; the page header chip carries the same tone.
    public string SidebarStatus => busy ? "Kreo · Working…" : statusTone == "Bad" ? "Kreo · Needs attention" : baseline != null ? model.Name + " · USB" : "Kreo · Not connected";
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
        ready = true; DrawBoard(); UpdateAvailability();
    }

    private void DrawBoard()
    {
        Board.Children.Clear(); keyButtons.Clear();
        var keys = model.Keys.Where(k => k.Width > 0 && k.Height > 0).ToArray();
        var minX = keys.Min(k => k.X); var minY = keys.Min(k => k.Y);
        Board.Width = (keys.Max(k => k.X + k.Width) - minX + 8) * 1.45;
        Board.Height = (keys.Max(k => k.Y + k.Height) - minY + 4) * 0.73;
        foreach (var key in keys)
        {
            var button = new Button { Width = (key.Width + 3) * 1.45, Height = (key.Height - 2) * 0.73, FontSize = key.Label.Length > 5 ? 9 : 11, Padding = new Thickness(1), MinHeight = 0 };
            Canvas.SetLeft(button, (key.X - minX) * 1.45); Canvas.SetTop(button, (key.Y - minY) * 0.73);
            button.Click += (_, _) => SelectKey(key);
            Board.Children.Add(button); keyButtons[key.Index] = button;
        }
        UpdateKeys();
    }

    private void SelectKey(KreoKey key)
    {
        selected = key;
        if (edited != null)
        {
            updating = true;
            var action = edited.Keys.AsSpan(key.Index * 4, 4).ToArray();
            TargetKey.SelectedItem = (TargetKey.ItemsSource as IReadOnlyList<KreoAction>)?.FirstOrDefault(a => a.Bytes.SequenceEqual(action));
            if (edited.Lighting[10] == 21) PaintColor.Text = "#" + Convert.ToHexString(new[] { edited.Colors[key.Index], edited.Colors[key.Index + 126], edited.Colors[key.Index + 252] });
            updating = false;
        }
        UpdateKeys(); UpdateAvailability();
    }

    private void UpdateKeys()
    {
        foreach (var key in model.Keys)
        {
            if (!keyButtons.TryGetValue(key.Index, out var button)) continue;
            var color = Color.FromRgb(32, 46, 68);
            if (edited?.Lighting[10] == 21) color = Color.FromRgb(edited.Colors[key.Index], edited.Colors[key.Index + 126], edited.Colors[key.Index + 252]);
            else if (edited != null && edited.Lighting[10] is > 0 and <= 18 && edited.EffectPalette != null)
            {
                var id = edited.Lighting[10]; var slot = edited.Lighting[57 + 2 * id] & 15;
                if (slot < 7) { var offset = id * 21 + slot * 3; color = Color.FromRgb(edited.EffectPalette[offset], edited.EffectPalette[offset + 1], edited.EffectPalette[offset + 2]); }
            }
            var chosen = selected?.Index == key.Index;
            var changed = edited != null && baseline != null && !edited.Keys.AsSpan(key.Index * 4, 4).SequenceEqual(baseline.Keys.AsSpan(key.Index * 4, 4));
            var label = edited == null ? key.Label : KreoActions.Describe(model, edited.Keys.AsSpan(key.Index * 4, 4).ToArray());
            // Keep key outlines and legends readable even for black/off lighting.
            button.Background = new SolidColorBrush(chosen ? Color.FromRgb(98, 128, 214) : Blend(color));
            button.Foreground = Brushes.White;
            button.Content = changed && label.Length < 9 ? key.Label + "→" + label : key.Label;
            button.FontWeight = chosen || changed ? FontWeights.Bold : FontWeights.Normal;
            button.ToolTip = edited == null ? key.Label : key.Label + " → " + label;
        }
        SelectedKeyLabel.Text = selected == null ? "Click a key to select it" : selected.Label + " selected";
        PaintTarget.Text = selected == null ? "Select a key on the keyboard" : "Paint " + selected.Label;
        ActionLabel.Text = selected == null || edited == null ? "Select a key on the keyboard above" : selected.Label + " → " + KreoActions.Describe(model, edited.Keys.AsSpan(selected.Index * 4, 4).ToArray());
        UpdateMappingPreview(); UpdatePaintPreview();
    }

    private static Color Blend(Color color) => Color.FromRgb((byte)(20 + color.R * 0.34), (byte)(27 + color.G * 0.34), (byte)(40 + color.B * 0.34));
    private void ModelChanged(object sender, SelectionChangedEventArgs e) { if (!ready || updating || ModelPicker.SelectedItem is not KreoModel choice) return; model = choice; selected = null; DrawBoard(); }

    public Task ReadAsync()
    {
        if (busy) return PendingOperation;
        PendingOperation = RunOperation(async () =>
        {
            var result = await Task.Run(() =>
            {
                var devices = KreoKeyboard.Enumerate();
                if (devices.Count != 1) throw new IOException(devices.Count == 0 ? "Connect one supported keyboard by USB and switch it to wired mode." : "Connect one Kreo keyboard at a time.");
                using var keyboard = new KreoKeyboard(devices[0]);
                return (Device: devices[0], Model: keyboard.Definition, State: keyboard.ReadState());
            });
            device = result.Device; model = result.Model; baseline = result.State; edited = baseline.Copy(); selected = null;
            updating = true; ModelPicker.SelectedItem = model; TargetKey.ItemsSource = KreoActions.ForModel(model); updating = false;
            LoadControls(); DrawBoard();
            SetStatus(model.Name + " · USB", "Good"); BoardTitle.Text = model.Name + " · Click a key to edit";
            KeyboardDetail.Text = "Ready. Changes stay in the editor until you apply them.";
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
        catch (Exception error) when (error is not OutOfMemoryException) { SetStatus("Needs attention", "Bad"); KeyboardDetail.Text = error.Message; }
        finally { busy = false; UpdateAvailability(); }
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
            var button = new Button { Width = 26, Height = 26, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 5, 5), Background = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2])), Content = slot == colorSlot && ColorMode.SelectedIndex == 0 ? "✓" : "", Foreground = rgb.Sum(b => b) > 460 ? Brushes.Black : Brushes.White, ToolTip = "#" + Convert.ToHexString(rgb) };
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
    private void SaveProfile(object sender, RoutedEventArgs e) => ProfileAction(() => { if (edited == null) return; var dialog = new SaveFileDialog { Filter = "Sora keyboard profile|*.json", FileName = model.Name + " setup.json" }; if (dialog.ShowDialog() == true) { Save(dialog.FileName, edited); KeyboardDetail.Text = "Profile saved."; } });
    private void LoadProfile(object sender, RoutedEventArgs e) => ProfileAction(() => { var dialog = new OpenFileDialog { Filter = "Sora keyboard profile|*.json" }; if (dialog.ShowDialog() == true) { edited = Load(dialog.FileName); LoadControls(); Changed(); } });
    private void RestoreBackup(object sender, RoutedEventArgs e) => ProfileAction(() => { edited = Load(BackupPath); LoadControls(); Changed(); KeyboardDetail.Text = "Backup loaded. Apply to keyboard restores it."; });

    private async void ApplyChanges(object sender, RoutedEventArgs e) => await ApplyAsync();
    public Task ApplyAsync()
    {
        if (busy || device == null || edited == null || baseline == null || !ApplyButton.IsEnabled) return PendingOperation;
        var desired = edited.Copy(); var expected = baseline.Copy(); var selectedDevice = device; var backupPath = BackupPath;
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
        PaintColor.Text = "#F012AB"; PaintSelectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (edited.Colors[a.Index] != 0xf0 || edited.Colors[a.Index + 126] != 0x12 || edited.Colors[a.Index + 252] != 0xab || edited.Lighting[10] != 21) throw new IOException("Per-key hex editor failed.");
        var changes = PendingChanges();
        DiscardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (PendingChanges() != 0 || ApplyButton.IsEnabled) throw new IOException("Discard did not reset the editor.");
        return new { uniqueEffectLabels = true, singleOff = true, effectHex = true, completeKeyMap = true, perKeyHex = true, stagedChanges = changes, discard = true, configurationWrites = 0 };
    }

    public void PreparePreview(string scenario)
    {
        if (edited == null) return;
        SelectKey(model.Keys.Single(k => k.Label == "A"));
        if (scenario == "assignment") { Editors.SelectedIndex = 1; TargetKey.SelectedItem = TargetKey.Items.Cast<KreoAction>().Single(k => k.Label == "B"); AssignButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        else if (scenario == "effects") EffectPicker.IsDropDownOpen = true;
    }
}
