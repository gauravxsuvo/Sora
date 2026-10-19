using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.Win32;
using Sora.Core;

namespace Sora.App;

public partial class LogitechPanel
{
    private LogitechService? controls;
    private LogitechControlProfile? controlDraft;
    private string controlBaseline = "";
    private int baselineDpi, baselineRate;
    private int[] capturedShortcut = [];
    private string? editingMacro;
    private bool pollingLive;
    private bool dpiSelectionPending;
    private Point? assignmentDragStart;
    private bool ControlsDirty => controlDraft != null && JsonSerializer.Serialize(controlDraft) != controlBaseline;
    private async Task PollLiveAsync()
    {
        if (pollingLive || IsBusy || snapshot == null || selectedDevice < 0) return;
        pollingLive = true;
        try
        {
            if (controls?.Running == true) { await controls.RefreshLiveDpiAsync(); RuntimeUpdated(); return; }
            var old = snapshot;
            var live = await WithDeviceAsync(async device =>
            {
                var mode = await device.RequestAsync(old.Features.Single(f => f.Id == 0x8100).Index, 2);
                var dpi = await device.RequestAsync(old.Features.Single(f => f.Id == 0x2201).Index, 2, [0]);
                return (mode: (int)mode[0], dpi: dpi[1] << 8 | dpi[2]);
            });
            if (IsBusy || snapshot == null) return;
            snapshot = snapshot with { OnboardMode = live.mode, Sensors = snapshot.Sensors.Select(s => s with { Dpi = live.dpi }).ToArray() };
            ModeText.Text = live.mode == 2 ? (ghubRunning ? "G HUB software control" : "Software mode · Sora controls inactive") : "Firmware control";
            LiveDpiText.Text = $"Live mouse DPI: {live.dpi:N0}"; StatusChanged?.Invoke($"G502 HERO · {live.dpi:N0} DPI");
            DpiScale.LiveDpi = live.dpi; DpiScale.LiveShiftHeld = false; DpiScale.InvalidateVisual();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ConnectionText.Text = "Mouse read unavailable · reconnecting";
            LiveDpiText.Text = "Live mouse DPI: unavailable";
            DpiScale.LiveDpi = null; DpiScale.InvalidateVisual();
        }
        finally { pollingLive = false; }
    }
    internal void Attach(LogitechService service, LogitechReactiveLighting lighting)
    {
        controls = service; reactive = lighting; service.Updated += RuntimeUpdated;
        var picker = new ColorWheel { Width = 210, Height = 210 }; picker.ColorPicked += hex => HexInput.Text = hex; ColorPickerHost.Content = picker;
        Unloaded += (_, _) => service.Updated -= RuntimeUpdated;
        Loaded += (_, _) => { service.Updated -= RuntimeUpdated; service.Updated += RuntimeUpdated; RuntimeUpdated(); };
        AssignmentPreview.ShowAssignments = true; AssignmentPreview.Width = 500;
        AssignmentPreview.AllowDrop = true;
        ActionList.PreviewMouseLeftButtonDown += (_, e) => assignmentDragStart = e.GetPosition(ActionList);
        ActionList.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || assignmentDragStart is not { } start || ActionList.SelectedItem is not LogitechAction action || (e.GetPosition(ActionList) - start).Length < 8) return;
            assignmentDragStart = null;
            DragDrop.DoDragDrop(ActionList, new DataObject(typeof(LogitechAction), action), DragDropEffects.Copy);
        };
        AssignmentPreview.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(typeof(LogitechAction)) && !IsBusy ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        AssignmentPreview.Drop += (_, e) => FileAction(() =>
        {
            if (IsBusy || e.Data.GetData(typeof(LogitechAction)) is not LogitechAction action) return;
            AssignmentPreview.SelectButtonAt(e.GetPosition(AssignmentPreview));
            AssignPrepared(action); e.Handled = true;
        });
        AssignmentPreview.ButtonSelected += index => ButtonPicker.SelectedIndex = index;
        DpiScale.StageSelected += index =>
        {
            StagePicker.SelectedIndex = index;
            dpiSelectionPending = DpiScale.LiveDpi != controlDraft?.Stages[index];
            SetEnabled();
        };
        DpiScale.StagesChanged += values => { StagesInput.Text = string.Join(", ", values); };
    }
    private void RuntimeUpdated()
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(RuntimeUpdated); return; }
        if (controls == null) return;
        if (controls.Running && controls.CurrentDpi.HasValue)
        {
            LiveDpiText.Text = $"Live mouse DPI: {controls.CurrentDpi:N0}" + (controls.DpiShiftHeld ? " · DPI Shift held" : "");
            StatusChanged?.Invoke($"G502 HERO · {controls.CurrentDpi:N0} DPI");
            DpiScale.LiveDpi = controls.CurrentDpi; DpiScale.LiveShiftHeld = controls.Running && controls.DpiShiftHeld; DpiScale.InvalidateVisual();
            if (snapshot != null) snapshot = snapshot with { OnboardMode = 2, Sensors = snapshot.Sensors.Select(s => s with { Dpi = controls.CurrentDpi.Value }).ToArray() };
        }
        if (controls.Running) ModeText.Text = "Sora software control";
        else if (controls.Enabled)
        {
            ModeText.Text = "Sora controls reconnecting";
            LiveDpiText.Text = "Live mouse DPI: checking hardware…";
            DpiScale.LiveDpi = null; DpiScale.LiveShiftHeld = false; DpiScale.InvalidateVisual();
        }
        if (!IsBusy && !HasPendingEdits && controls.Enabled) EditStatus.Text = controls.Status;
        if (controls.Running) ConflictText.Visibility = Visibility.Collapsed;
        SetEnabled();
        AssignmentPreview.PressedButtons = controls.PressedButtons; AssignmentPreview.InvalidateVisual();
    }
    private void LoadControlEditor()
    {
        if (snapshot == null) return;
        dpiSelectionPending = false;
        controlDraft = controls?.Profile is { } saved ? Clone(saved) : LogitechControlProfile.Create(draftDpi, draftRate);
        try { controlDraft.Validate(snapshot); } catch (InvalidDataException) { controlDraft = LogitechControlProfile.Create(draftDpi, draftRate); }
        rendering = true;
        StagesInput.Text = string.Join(", ", controlDraft.Stages); ShiftDpiInput.Text = controlDraft.ShiftDpi.ToString(); ControlProfileName.Text = controlDraft.Name;
        StagePicker.ItemsSource = controlDraft.Stages.Select(v => v + " DPI").ToArray(); StagePicker.SelectedIndex = controlDraft.DefaultStage;
        if (controls?.Profile != null) { draftDpi = controlDraft.Stages[controlDraft.DefaultStage]; draftRate = controlDraft.ReportRate; DpiInput.Text = draftDpi.ToString(); DpiValue.Text = draftDpi.ToString("N0"); DpiSlider.Value = draftDpi; RatePicker.SelectedIndex = Array.IndexOf(snapshot.ReportRates, draftRate); }
        ButtonPicker.ItemsSource = Enumerable.Range(1, 11).Select(i => $"G{i} · {controlDraft.Buttons[i - 1].Name}").ToArray(); ButtonPicker.SelectedIndex = 5;
        MacroPicker.DisplayMemberPath = "Name"; MacroPicker.ItemsSource = controlDraft.Macros;
        baselineDpi = draftDpi; baselineRate = draftRate; controlBaseline = JsonSerializer.Serialize(controlDraft);
        LiveDpiText.Text = $"Live mouse DPI: {snapshot.Sensors[0].Dpi:N0}";
        rendering = false; UpdateDpiScale(); ShowAssignment(); PopulateActions(); RuntimeUpdated();
    }
    private static LogitechControlProfile Clone(LogitechControlProfile p) => JsonSerializer.Deserialize<LogitechControlProfile>(JsonSerializer.Serialize(p))!;
    private void UpdateDpiScale()
    {
        if (controlDraft == null || snapshot == null) return;
        var sensor = snapshot.Sensors[0]; DpiScale.Minimum = sensor.Minimum; DpiScale.Maximum = sensor.Maximum; DpiScale.Step = sensor.Step > 0 ? sensor.Step : 50;
        DpiScale.Stages = controlDraft.Stages; DpiScale.CurrentStage = controlDraft.DefaultStage; DpiScale.ShiftDpi = controlDraft.ShiftDpi; DpiScale.InvalidateVisual();
        DpiScale.LiveDpi = controls?.Running == true ? controls.CurrentDpi : snapshot.Sensors[0].Dpi;
        DpiScale.LiveShiftHeld = controls?.Running == true && controls.DpiShiftHeld;
        DpiScale.InvalidateVisual();
    }
    private void StagesChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || rendering || controlDraft == null || snapshot == null) return;
        try
        {
            var values = StagesInput.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            if (values.Length is < 1 or > 5 || values.Distinct().Count() != values.Length || values.Any(d => !snapshot.Sensors[0].Accepts(d))) throw new InvalidDataException("Use one to five distinct supported DPI speeds.");
            var index = Array.IndexOf(values, draftDpi); if (index < 0) index = 0;
            controlDraft = controlDraft with { Stages = values, DefaultStage = index };
            rendering = true; StagePicker.ItemsSource = values.Select(v => v + " DPI").ToArray(); StagePicker.SelectedIndex = index;
            draftDpi = values[index]; DpiInput.Text = draftDpi.ToString(); DpiValue.Text = draftDpi.ToString("N0"); DpiSlider.Value = draftDpi; rendering = false;
            ClearInputError(); UpdateDpiScale(); SetEnabled();
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException) { ShowError(error.Message, true); SetEnabled(); }
    }
    private void ShiftChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || rendering || controlDraft == null || snapshot == null) return;
        if (!int.TryParse(ShiftDpiInput.Text, out var shift) || !snapshot.Sensors[0].Accepts(shift)) { ShowError("Choose a supported DPI Shift speed.", true); SetEnabled(); return; }
        controlDraft = controlDraft with { ShiftDpi = shift }; ClearInputError(); UpdateDpiScale(); SetEnabled();
    }
    private void StageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || rendering || controlDraft == null || StagePicker.SelectedIndex < 0) return;
        controlDraft = controlDraft with { DefaultStage = StagePicker.SelectedIndex }; DpiSlider.Value = controlDraft.Stages[controlDraft.DefaultStage];
        dpiSelectionPending = DpiScale.LiveDpi != controlDraft.Stages[controlDraft.DefaultStage];
        UpdateDpiScale(); SetEnabled();
    }
    private void UpdateCurrentStage()
    {
        if (controlDraft == null || controlDraft.Stages.Length == 0) return;
        var values = controlDraft.Stages.ToArray(); var index = Array.IndexOf(values, draftDpi);
        if (index < 0) { index = Math.Clamp(controlDraft.DefaultStage, 0, values.Length - 1); values[index] = draftDpi; }
        controlDraft = controlDraft with { Stages = values, DefaultStage = index, ReportRate = draftRate };
        rendering = true; StagesInput.Text = string.Join(", ", values); StagePicker.ItemsSource = values.Select(v => v + " DPI").ToArray(); StagePicker.SelectedIndex = controlDraft.DefaultStage; rendering = false; UpdateDpiScale();
    }
    private LogitechControlProfile BuildControls()
    {
        if (snapshot == null || controlDraft == null) throw new InvalidOperationException("Read the mouse before editing controls.");
        var stages = StagesInput.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        if (!int.TryParse(ShiftDpiInput.Text, out var shift)) throw new InvalidDataException("Enter a valid DPI Shift speed.");
        var profile = controlDraft with { Name = ControlProfileName.Text.Trim(), Stages = stages, ShiftDpi = shift, ReportRate = draftRate };
        profile.Validate(snapshot); return profile;
    }
    private async void StopControlsClick(object sender, RoutedEventArgs e)
    {
        if (controls == null) return;
        await RunAsync(async () => { await controls.DisableAsync(); EditStatus.Text = controls.Status; }, "Restoring original mouse control…");
        if (!HasPendingEdits) await ReadAsync();
    }
    private void ControlsDefaultClick(object sender, RoutedEventArgs e)
    {
        if (snapshot == null) return;
        controlDraft = LogitechControlProfile.Create(); StageControlProfile(controlDraft);
    }
    private void StageControlProfile(LogitechControlProfile profile)
    {
        rendering = true; controlDraft = Clone(profile); StagesInput.Text = string.Join(", ", profile.Stages); ShiftDpiInput.Text = profile.ShiftDpi.ToString(); ControlProfileName.Text = profile.Name;
        StagePicker.ItemsSource = profile.Stages.Select(v => v + " DPI").ToArray(); StagePicker.SelectedIndex = profile.DefaultStage;
        draftDpi = profile.Stages[profile.DefaultStage]; draftRate = profile.ReportRate;
        DpiInput.Text = draftDpi.ToString(); DpiValue.Text = draftDpi.ToString("N0"); DpiSlider.Value = draftDpi; RatePicker.SelectedIndex = Array.IndexOf(snapshot!.ReportRates, draftRate);
        MacroPicker.ItemsSource = profile.Macros; rendering = false; UpdateDpiScale(); ShowAssignment(); PopulateActions(); SetEnabled();
    }
    private void ControlNameChanged(object sender, TextChangedEventArgs e) { if (ready && !rendering && controlDraft != null) { controlDraft = controlDraft with { Name = ControlProfileName.Text }; SetEnabled(); } }
    private void ImportControlsClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        if (snapshot == null) throw new InvalidOperationException("Connect your mouse first.");
        var dialog = new OpenFileDialog { Filter = "Sora control profiles (*.json)|*.json" };
        if (dialog.ShowDialog() == true) { var profile = LogitechControlProfile.Load(dialog.FileName); profile.Validate(snapshot); StageControlProfile(profile); }
    });
    private void ExportControlsClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        var profile = BuildControls(); var dialog = new SaveFileDialog { Filter = "Sora control profiles (*.json)|*.json", FileName = "sora-g502-profile.json" };
        if (dialog.ShowDialog() == true) LogitechControlProfile.Save(dialog.FileName, profile);
    });
    private void FrontViewClick(object sender, RoutedEventArgs e) { AssignmentPreview.SideView = false; AssignmentPreview.InvalidateVisual(); }
    private void SideViewClick(object sender, RoutedEventArgs e) { AssignmentPreview.SideView = true; AssignmentPreview.InvalidateVisual(); }
    private void AssignmentLayerChanged(object sender, RoutedEventArgs e) { if (ready) ShowAssignment(); }
    private void AssignmentButtonChanged(object sender, SelectionChangedEventArgs e) { if (ready && !rendering) ShowAssignment(); }
    private void LegendSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssignmentLegend.SelectedIndex >= 0 && AssignmentLegend.SelectedIndex != ButtonPicker.SelectedIndex)
            ButtonPicker.SelectedIndex = AssignmentLegend.SelectedIndex;
    }
    private void ShowAssignment()
    {
        if (controlDraft == null || ButtonPicker.SelectedIndex < 0) return;
        var index = ButtonPicker.SelectedIndex; AssignmentPreview.SelectedButton = index; AssignmentPreview.InvalidateVisual();
        var action = (GShiftLayerCheck.IsChecked == true ? controlDraft.ShiftButtons : controlDraft.Buttons)[index];
        AssignmentPreview.AssignmentNames = (GShiftLayerCheck.IsChecked == true ? controlDraft.ShiftButtons : controlDraft.Buttons).Select(a => a.Name).ToArray();
        var labels = AssignmentPreview.AssignmentNames.Select((name, i) => $"G{i + 1} · {name}").ToArray();
        if (AssignmentLegend.ItemsSource is not string[] previous || !previous.SequenceEqual(labels)) AssignmentLegend.ItemsSource = labels;
        AssignmentLegend.SelectedIndex = index;
        AssignmentPreview.InvalidateVisual();
        AssignmentCurrentText.Text = $"G{index + 1} → {action.Name}" + (index == 0 ? " · kept native" : "");
    }
    private void ActionFilterChanged(object sender, RoutedEventArgs e) { if (ready && !rendering) PopulateActions(); }
    private void PopulateActions()
    {
        if (controlDraft == null || ActionCategory == null || ActionSearch == null || ActionList == null) return;
        var category = (ActionCategory.SelectedItem as ComboBoxItem)?.Content?.ToString(); var list = new List<LogitechAction>();
        if (category == "Commands")
        {
            foreach (var (name, key) in new[] { ("Copy", 67), ("Cut", 88), ("Paste", 86), ("Undo", 90), ("Select all", 65), ("Save", 83), ("New tab", 84), ("Close tab", 87), ("Find", 70), ("Open", 79) }) list.Add(new("Key", name, [17, key]));
            list.Add(new("Key", "Redo", [17, 16, 90])); list.Add(new("Key", "Switch window", [18, 9]));
        }
        else if (category == "Keys")
        {
            foreach (var key in Enumerable.Range(65, 26).Concat(Enumerable.Range(48, 10)).Concat(Enumerable.Range(112, 24))) list.Add(new("Key", key >= 112 ? "F" + (key - 111) : ((char)key).ToString(), [key]));
            foreach (var (name, code) in new[] { ("Space", 32), ("Enter", 13), ("Escape", 27), ("Tab", 9), ("Backspace", 8), ("Delete", 46), ("Left", 37), ("Right", 39), ("Up", 38), ("Down", 40), ("Ctrl", 17), ("Shift", 16), ("Alt", 18) }) list.Add(new("Key", name, [code]));
        }
        else if (category == "Actions") { list.Add(new("Launch", "Launch application")); list.Add(new("Url", "Open website")); list.Add(new("Key", "Show desktop", [91, 68])); list.Add(new("Key", "Screenshot", [91, 16, 83])); }
        else if (category == "Macros") list.AddRange(controlDraft.Macros.Select(m => new LogitechAction("Macro", m.Name, Target: m.Id)));
        else
        {
            list.AddRange(LogitechControlProfile.Create().Buttons.Skip(1).Where(a => a.Kind != "ProfileNext"));
            list.Add(new("DpiCycle", "DPI Cycle")); list.Add(new("GShift", "G-Shift")); list.Add(new("ProfileNext", "Next Sora control profile")); list.Add(new("Default", "Same as default layer")); list.Add(new("Disabled", "Disabled"));
            foreach (var (name, code) in new[] { ("Volume up", 175), ("Volume down", 174), ("Mute", 173), ("Play / pause", 179), ("Next track", 176), ("Previous track", 177), ("Browser back", 166), ("Browser forward", 167) }) list.Add(new("Key", name, [code]));
            list.Add(new("ScrollUp", "Scroll up")); list.Add(new("ScrollDown", "Scroll down"));
        }
        var search = ActionSearch.Text; ActionList.ItemsSource = list.Where(a => string.IsNullOrWhiteSpace(search) || a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private void Assign(LogitechAction action)
    {
        if (controlDraft == null || ButtonPicker.SelectedIndex < 0) return;
        var index = ButtonPicker.SelectedIndex; if (index == 0) { ShowError("Primary click stays native so you can always recover control."); return; }
        var array = (GShiftLayerCheck.IsChecked == true ? controlDraft.ShiftButtons : controlDraft.Buttons).ToArray(); array[index] = action;
        controlDraft = GShiftLayerCheck.IsChecked == true ? controlDraft with { ShiftButtons = array } : controlDraft with { Buttons = array };
        ShowAssignment(); ClearInputError(); SetEnabled();
    }
    private void UseAssignmentClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        if (ActionList.SelectedItem is not LogitechAction action) throw new InvalidOperationException("Select an assignment first.");
        AssignPrepared(action);
    });
    private void AssignPrepared(LogitechAction action)
    {
        if (action.Kind is "Launch" or "Url") action = action with { Target = ActionTarget.Text.Trim(), Name = action.Name + " · " + ActionTarget.Text.Trim() };
        Assign(action);
    }
    private void UseShortcutClick(object sender, RoutedEventArgs e) { if (capturedShortcut.Length > 0) Assign(new("Key", ShortcutCapture.Text, capturedShortcut.ToArray())); }
    private void BrowseActionClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe" }; if (dialog.ShowDialog() == true) ActionTarget.Text = dialog.FileName;
    }
    private static int[] Chord(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key; var list = new List<int>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) list.Add(17); if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) list.Add(16); if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) list.Add(18); if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) list.Add(91);
        var code = KeyInterop.VirtualKeyFromKey(key); if (code is > 0 and <= 254 && !list.Contains(code)) list.Add(code); return list.ToArray();
    }
    private void CaptureShortcut(object sender, KeyEventArgs e) { capturedShortcut = Chord(e); ShortcutCapture.Text = string.Join(" + ", capturedShortcut.Select(k => KeyInterop.KeyFromVirtualKey(k).ToString())); e.Handled = true; }
    private void CaptureMacroChord(object sender, KeyEventArgs e)
    {
        var keys = Chord(e); MacroChordCapture.Text = string.Join("+", keys); e.Handled = true;
        if (keys.Any(k => k is not (16 or 17 or 18 or 91))) MacroSteps.AppendText("\nPress | 50 | " + string.Join("+", keys));
    }
    private void MacroSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || rendering || MacroPicker.SelectedItem is not LogitechMacro macro) return;
        editingMacro = macro.Id; MacroName.Text = macro.Name; MacroSteps.Text = string.Join("\n", macro.Steps.Select(s => $"{s.Phase} | {s.DelayMs} | {string.Join("+", s.Keys)}"));
        MacroMode.SelectedIndex = Array.FindIndex(new[] { "NoRepeat", "WhileHeld", "Toggle", "Sequence" }, m => m == macro.Mode);
    }
    private void NewMacroClick(object sender, RoutedEventArgs e) { editingMacro = null; MacroName.Text = "New macro"; MacroSteps.Text = "Press | 50 | 17+67"; MacroMode.SelectedIndex = 0; }
    private void SaveMacroClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        if (controlDraft == null || snapshot == null) throw new InvalidOperationException("Connect your mouse first.");
        var steps = MacroSteps.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries); if (parts.Length != 3) throw new InvalidDataException("Use phase | delay | key+key on each line.");
            return new LogitechMacroStep(parts[0], int.Parse(parts[1]), parts[2].Split('+', StringSplitOptions.TrimEntries).Select(int.Parse).ToArray());
        }).ToArray();
        var macro = new LogitechMacro(editingMacro ?? Guid.NewGuid().ToString("N"), MacroName.Text.Trim(), ((ComboBoxItem)MacroMode.SelectedItem).Tag.ToString()!, steps);
        var next = controlDraft with { Macros = controlDraft.Macros.Where(m => m.Id != macro.Id).Append(macro).ToArray() }; next.Validate(snapshot); controlDraft = next; editingMacro = macro.Id;
        rendering = true; MacroPicker.ItemsSource = next.Macros; MacroPicker.SelectedItem = macro; rendering = false; PopulateActions(); SetEnabled();
    });
    private void DeleteMacroClick(object sender, RoutedEventArgs e)
    {
        if (controlDraft == null || editingMacro == null) return;
        var id = editingMacro;
        LogitechAction[] Remove(LogitechAction[] a) => a.Select(x => x.Kind == "Macro" && x.Target == id ? new LogitechAction("Disabled", "Disabled (macro removed)") : x).ToArray();
        controlDraft = controlDraft with { Macros = controlDraft.Macros.Where(m => m.Id != id).ToArray(), Buttons = Remove(controlDraft.Buttons), ShiftButtons = Remove(controlDraft.ShiftButtons) };
        MacroPicker.ItemsSource = controlDraft.Macros; NewMacroClick(sender, e); PopulateActions(); ShowAssignment(); SetEnabled();
    }
}
