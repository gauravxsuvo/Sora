using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Sora.Core;

namespace Sora.App;

// Lighting edits are saved immediately and applied after the user pauses editing.
public partial class LightingPanel : UserControl
{
    private sealed record MemoryOption(EneMode Mode, string Name, bool Color, bool Speed, string Hint);
    private sealed record BoardOption(BoardEffect Effect, string Name, bool Color, bool Brightness, bool Speed, string Hint);
    private sealed record SyncOption(SyncEffect Effect, string Name, bool Color, bool Speed, string Hint);

    private static readonly MemoryOption[] MemoryOptions =
    [
        new(EneMode.Rainbow, "Rainbow", false, true, "A rainbow wave runs along the sticks. This is how the memory ships."),
        new(EneMode.Static, "Static", true, false, "One steady colour on every LED."),
        new(EneMode.Breathing, "Breathing", true, true, "Fades the colour in and out."),
        new(EneMode.Flashing, "Flashing", true, true, "Blinks the colour on and off."),
        new(EneMode.ColorCycle, "Color cycle", false, true, "All LEDs step through the spectrum together."),
        new(EneMode.Off, "Off", false, false, "Turns the memory lighting off."),
    ];
    private static readonly BoardOption[] BoardOptions =
    [
        new(BoardEffect.Rainbow, "Rainbow", false, false, false, "Selected lights cycle through the spectrum independently. Select all lights for GCC's spatial rainbow wave; plain RGB zones show white in that whole-board wave."),
        new(BoardEffect.Static, "Static", true, true, false, ""),
        new(BoardEffect.Pulse, "Pulse", true, false, true, "Pulse always peaks at full brightness, as in GCC."),
        new(BoardEffect.Flash, "Flash", true, true, true, ""),
        new(BoardEffect.DoubleFlash, "Double flash", true, true, true, ""),
        new(BoardEffect.ColorCycle, "Color cycle", false, true, true, "The whole light steps through the spectrum."),
        new(BoardEffect.Off, "Off", false, false, false, ""),
    ];
    private static readonly SyncOption[] SyncOptions =
    [
        new(SyncEffect.Rainbow, "Rainbow", false, true, "A rainbow wave on the sticks and around the ARGB fans. The GAME ON logo, I/O cover and SSD heatsink show white, as in GCC; speed sets the memory, the board's wave runs at GCC's pace."),
        new(SyncEffect.Static, "Static", true, false, "One steady colour everywhere."),
        new(SyncEffect.Breathing, "Breathing", true, true, "Fades the colour in and out on every light at once. Sora drives both in lockstep while it runs, as GCC does in sync."),
        new(SyncEffect.Flashing, "Flashing", true, true, "Blinks the colour on every light at once. Sora drives both in lockstep while it runs, as GCC does in sync."),
        new(SyncEffect.ColorCycle, "Color cycle", false, true, "Every light glides through the spectrum together. Sora drives both in lockstep while it runs, as GCC does in sync."),
        new(SyncEffect.Off, "Off", false, false, "Turns the memory and motherboard lighting off."),
    ];
    private static readonly string[] SwatchColors = ["#FFFFFF", "#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#0A84FF", "#BF5AF2", "#FF375F"];

    private readonly DispatcherTimer conflictTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer autoApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private LightingService? service;
    private bool ready, updating, memoryDirty, boardDirty, syncDirty;
    private string? boardModel;
    private IReadOnlyList<BoardZone> zoneList = [];
    private List<BoardZoneLighting> boardZones = [];
    private readonly HashSet<int> selectedZones = [];
    private readonly HashSet<int> dirtyZones = [];
    private readonly HashSet<int> dirtyModules = [];
    private readonly Dictionary<int, ModuleLighting> moduleLooks = new();
    private readonly Dictionary<int, string> invalidModuleColors = new();
    private readonly Dictionary<int, string> invalidZoneColors = new();
    private sealed record ModuleChoice(int Slot, string Label);
    private string? message;
    private bool messageIsError;
    private LightingSettings? editBaseline;
    private int editorRevision;

    /// <summary>Raised when the sidebar status may have changed.</summary>
    public event Action? StatusChanged;

    public LightingPanel()
    {
        InitializeComponent();
        foreach (var option in MemoryOptions) MemoryEffect.Items.Add(new ComboBoxItem { Content = option.Name, Tag = option });
        foreach (var option in BoardOptions) BoardEffectPicker.Items.Add(new ComboBoxItem { Content = option.Name, Tag = option });
        foreach (var option in SyncOptions) SyncEffectPicker.Items.Add(new ComboBoxItem { Content = option.Name, Tag = option });
        BuildSwatches(SyncSwatches, hex => SyncHex.Text = hex, "Sync");
        SyncWheel.ColorPicked += hex => SyncHex.Text = hex;
        BuildSwatches(MemorySwatches, hex => MemoryHex.Text = hex, "Memory");
        BuildSwatches(BoardSwatches, hex => BoardHex.Text = hex, "Motherboard");
        MemoryWheel.ColorPicked += hex => MemoryHex.Text = hex;
        BoardWheel.ColorPicked += hex => BoardHex.Text = hex;
        conflictTimer.Tick += (_, _) => CheckConflict();
        autoApplyTimer.Tick += async (_, _) =>
        {
            if (service?.AutomaticChangesAllowed != true) { autoApplyTimer.Stop(); return; }
            if (service.Busy) return;
            autoApplyTimer.Stop();
            if (ApplyButton.IsEnabled) ApplyClicked(this, new RoutedEventArgs());
            else if (HasPendingEdits)
            {
                if (service.State == null || service.State.MemoryError != null || service.State.BoardError != null || service.State.Conflict != null)
                    await ReadAsync();
                autoApplyTimer.Interval = TimeSpan.FromSeconds(5); autoApplyTimer.Start();
            }
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) conflictTimer.Start(); else conflictTimer.Stop(); };
        Unloaded += (_, _) => conflictTimer.Stop();
    }

    internal void Attach(LightingService lighting)
    {
        service = lighting;
        boardModel = lighting.State?.BoardModel ?? GigabyteRgb.BoardModel();
        LoadControls();
        ready = true;
        void OnChanged() => Dispatcher.InvokeAsync(() =>
        {
            if (!lighting.Busy && !memoryDirty) LoadMemoryControls();
            Render();
        });
        lighting.Changed += OnChanged;
        Unloaded += (_, _) => lighting.Changed -= OnChanged;
        Render();
    }

    internal bool HasPendingEdits => SyncOn ? syncDirty : memoryDirty || boardDirty;
    private bool SyncOn => SyncToggle.IsChecked == true;
    internal bool HasState => service?.State != null;

    /// <summary>Saved intent stays pending until this session has successfully applied it.</summary>
    private string? SyncSummary => service?.Settings is { Sync: true, SyncLighting: { } sync }
        ? (service.SyncApplied ? "Synced · " : "Saved sync · ") + (sync.Effect == SyncEffect.Off ? "Lights off" : SyncOptions.First(o => o.Effect == sync.Effect).Name)
        : null;

    /// <summary>The board's saved effect when every light has the same one.</summary>
    private string? BoardSummary => service?.Settings.BoardZones is { Count: > 0 } zones && zones.Select(z => z.Effect).Distinct().Count() == 1
        ? BoardOptions.FirstOrDefault(o => o.Effect == zones[0].Effect)?.Name ?? "Color cycle"
        : service?.Settings.BoardZones is { Count: > 0 } ? "custom" : null;

    public string SidebarStatus
    {
        get
        {
            var state = service?.State;
            if (state?.Conflict != null) return state.Conflict.Contains("GIGABYTE") ? "GCC is running" : "Another RGB app";
            if (SyncSummary is { } synced) return synced;
            if (service?.Busy == true && state == null) return "Checking…";
            if (state == null) return "Not checked";
            var ram = state.Dimms.Count > 0 ? "RAM " + ModeName(state.Dimms[0].Original) : state.MemoryError?.Contains("administrator") == true ? "RAM no access" : null;
            var board = state.Board == null ? null : "Board " + (BoardSummary ?? "not set");
            return string.Join(" · ", new[] { ram, board }.Where(t => t != null)) is { Length: > 0 } text ? text : "Not found";
        }
    }

    public string StatusTone => service?.State switch
    {
        { Conflict: not null } => "Warn",
        _ when service?.Settings.Sync == true && !service.SyncApplied => "Warn",
        null => "Idle",
        { Dimms.Count: > 0 } or { Board: not null } => "Good",
        _ => "Bad",
    };

    /// <summary>Scans the hardware. Reads only; nothing changes on the RAM or the board.</summary>
    internal async Task ReadAsync()
    {
        if (service == null || service.Busy) return;
        message = null;
        Render();
        try
        {
            var state = await service.RefreshAsync();
            if (!memoryDirty) LoadMemoryControls();
            if (!boardDirty && state.BoardZones != null && !state.BoardZones.SequenceEqual(zoneList)) LoadBoardZones();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { message = error.Message; messageIsError = true; }
        Render();
    }

    /// <summary>The DIMMs' current effect as editor values, or null if it is not one of the six built-in effects.</summary>
    private static MemoryLighting? FromHardware(LightingState? state, int? slot = null)
    {
        if ((slot == null ? state?.Dimms.FirstOrDefault() : state?.Dimms.FirstOrDefault(d => d.Slot == slot)) is not { } dimm || dimm.Original.Control is not [0x00, <= (byte)EneMode.Rainbow and var mode, var speed, var direction] || dimm.Original.Colors.Length < 3) return null;
        var colors = dimm.Original.Colors;
        // Sora's (and GCC's) Off is Static black; show it as Off, not as a black Static.
        if (EneDram.IsOff(dimm.Original.Control, colors)) return new MemoryLighting(EneMode.Off, "#0A84FF", 100, Math.Clamp(5 - speed, 1, 5));
        return new MemoryLighting((EneMode)mode, $"#{colors[0]:X2}{colors[2]:X2}{colors[1]:X2}", 100, Math.Clamp(5 - speed, 1, 5), direction == 1);
    }

    private void LoadControls(bool keepSync = false)
    {
        var settings = service!.Settings;
        LoadMemoryControls();
        updating = true;
        // Until Sora has applied something, the editor starts from what the RAM is doing now.
        var memory = settings.Memory ?? FromHardware(service.State) ?? new MemoryLighting();
        StartupApply.IsChecked = settings.ApplyAtStartup;
        ExitRainbow.IsChecked = settings.RainbowOnExit;
        KeepOnBoard.IsChecked = settings.KeepOnBoard;
        SyncToggle.IsChecked = settings.Sync;
        updating = false;
        memoryDirty = false;
        LoadBoardZones();
        if (keepSync) return;
        LoadSyncEditor(settings.SyncLighting ?? SyncLighting.From(memory));
        // Sync switched on but never applied: the devices may still differ.
        syncDirty = settings is { Sync: true, SyncLighting: null };
    }

    private void LoadMemoryControls()
    {
        var settings = service!.Settings;
        var chosen = (ModulePicker.SelectedItem as ModuleChoice)?.Slot;
        var was = updating; updating = true;
        MemorySyncToggle.IsChecked = settings.MemorySync;
        moduleLooks.Clear(); dirtyModules.Clear(); invalidModuleColors.Clear();
        foreach (var dimm in service.State?.Dimms ?? [])
        {
            var saved = settings.MemoryModules?.FirstOrDefault(m => m.Slot == dimm.Slot && m.Name == dimm.Name && m.ChipId == dimm.ChipId);
            moduleLooks[dimm.Slot] = saved ?? new ModuleLighting(dimm.Slot, dimm.Name, dimm.ChipId, settings.Memory ?? FromHardware(service.State, dimm.Slot) ?? new());
        }
        ModulePicker.ItemsSource = moduleLooks.Keys.Order().Select(slot => new ModuleChoice(slot, SlotName(slot))).ToArray();
        ModulePicker.SelectedItem = ModulePicker.Items.Cast<ModuleChoice>().FirstOrDefault(m => m.Slot == chosen) ?? ModulePicker.Items.Cast<ModuleChoice>().FirstOrDefault();
        SetMemoryEditor(SelectedModuleLook ?? settings.Memory ?? FromHardware(service.State) ?? new());
        updating = was; memoryDirty = false;
    }
    private MemoryLighting? SelectedModuleLook => ModulePicker.SelectedItem is ModuleChoice selected && moduleLooks.TryGetValue(selected.Slot, out var look) ? look.Lighting : null;
    private void SetMemoryEditor(MemoryLighting look)
    {
        var was = updating; updating = true;
        MemoryEffect.SelectedItem = MemoryEffect.Items.Cast<ComboBoxItem>().First(i => ((MemoryOption)i.Tag).Mode == look.Effect);
        MemoryHex.Text = look.Color; MemoryBrightness.Value = Math.Clamp(look.Brightness, 10, 100);
        MemorySpeed.Value = Math.Clamp(look.Speed, 1, 5); MemoryReverse.IsChecked = look.Reverse;
        updating = was;
    }
    private void ModuleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating) return;
        if (SelectedModuleLook is { } look) SetMemoryEditor(look);
        if (ModulePicker.SelectedItem is ModuleChoice choice && invalidModuleColors.TryGetValue(choice.Slot, out var invalid))
        { updating = true; MemoryHex.Text = invalid; updating = false; }
        Render();
    }
    private void MemorySyncToggled(object sender, RoutedEventArgs e)
    {
        if (!ready || updating || service == null) return;
        service.SaveMemorySyncPreference(MemorySyncToggle.IsChecked == true);
        if (MemorySyncToggle.IsChecked == true) MemoryChanged(sender, e);
        Render();
    }

    /// <summary>Fills the sync card; not a change by itself.</summary>
    private void LoadSyncEditor(SyncLighting sync)
    {
        var was = updating;
        updating = true;
        SyncEffectPicker.SelectedItem = SyncEffectPicker.Items.Cast<ComboBoxItem>().First(i => ((SyncOption)i.Tag).Effect == sync.Effect);
        SyncHex.Text = sync.Color;
        SyncBrightness.Value = Math.Clamp(sync.Brightness, 10, 100);
        SyncSpeed.Value = Math.Clamp(sync.Speed, 1, 5);
        updating = was;
        syncDirty = false;
    }

    private SyncOption SelectedSync => (SyncOption)((ComboBoxItem)SyncEffectPicker.SelectedItem).Tag;
    private SyncLighting EditedSync => new(SelectedSync.Effect, NormalizedHex(SyncHex.Text) ?? "#000000", (int)SyncBrightness.Value, (int)SyncSpeed.Value);

    private void SyncChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || updating) return;
        syncDirty = true;
        message = null;
        Render();
        QueueAutomaticApply();
    }

    private async void SyncToggled(object sender, RoutedEventArgs e)
    {
        if (!ready || updating || service == null) return;
        var settled = service.SaveSyncPreferenceAsync(SyncOn);
        if (SyncOn)
        {
            // Start from the last synced look, or from the memory editor; both devices change only when Apply is pressed.
            LoadSyncEditor(service.Settings.SyncLighting ?? SyncLighting.From(EditedMemory));
            syncDirty = true;
            message = "Press Apply to put the memory and the motherboard on the same lighting.";
        }
        else
        {
            syncDirty = false;
            LoadControls(keepSync: true);
            message = "Sync is off. The memory and motherboard keep their lighting until you change them separately.";
        }
        messageIsError = false;
        Render();
        try
        {
            // An animated sync would otherwise leave both frozen on its last frame.
            if (await settled) message = "Sync is off. The memory and motherboard now run the same effect on their own, so they may drift apart over time.";
        }
        catch (Exception error) when (error is not OutOfMemoryException) { message = error.Message; messageIsError = true; }
        Render();
        QueueAutomaticApply();
    }

    /// <summary>Zones from the board table, each with its saved effect (or a default), all selected.</summary>
    private void LoadBoardZones()
    {
        var saved = service!.Settings.BoardZones;
        zoneList = service.State?.BoardZones ?? GigabyteRgb.ZonesFor(boardModel);
        // Wave (0.6/0.6.1) is no longer offered; it is shown as the Color cycle it is now sent as.
        boardZones = zoneList.Select(z => saved?.FirstOrDefault(s => s.Zone == z.Number) is { } zone ? zone.Effect == BoardEffect.Wave ? zone with { Effect = BoardEffect.ColorCycle } : zone : new BoardZoneLighting(z.Number)).ToList();
        selectedZones.Clear();
        foreach (var zone in zoneList) selectedZones.Add(zone.Number);
        boardDirty = false;
        dirtyZones.Clear();
        invalidZoneColors.Clear();
        BuildZoneChips();
        LoadZoneEditor();
    }

    private void BuildZoneChips()
    {
        BoardZonesPanel.Children.Clear();
        foreach (var zone in zoneList)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), Stroke = (Brush)FindResource("InputBorderBrush"), StrokeThickness = 1 };
            var chip = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)FindResource("ZoneChip"), Tag = zone.Number,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, new TextBlock { Text = zone.Name } } },
                ToolTip = "Click to edit this light. Ctrl+click to add or remove lights from the selection.",
            };
            AutomationProperties.SetName(chip, zone.Name);
            chip.Click += (_, _) =>
            {
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0)
                { selectedZones.Clear(); selectedZones.Add(zone.Number); }
                else if (chip.IsChecked == true) selectedZones.Add(zone.Number);
                else if (selectedZones.Count > 1) selectedZones.Remove(zone.Number);
                else chip.IsChecked = true;
                LoadZoneEditor();
                Render();
            };
            BoardZonesPanel.Children.Add(chip);
        }
    }

    /// <summary>The editor shows the first selected zone; edits go to every selected zone.</summary>
    private void LoadZoneEditor()
    {
        if (boardZones.FirstOrDefault(z => selectedZones.Contains(z.Zone)) is not { } zone) return;
        var was = updating;
        updating = true;
        BoardEffectPicker.SelectedItem = BoardEffectPicker.Items.Cast<ComboBoxItem>().First(i => ((BoardOption)i.Tag).Effect == zone.Effect);
        BoardHex.Text = invalidZoneColors.GetValueOrDefault(zone.Zone) ?? zone.Color;
        BoardBrightness.Value = Math.Clamp(zone.Brightness, 0, 8);
        BoardSpeed.Value = Math.Clamp(zone.Speed, 0, 9);
        updating = was;
    }

    private void SelectAllZones(object sender, RoutedEventArgs e)
    {
        foreach (var zone in zoneList) selectedZones.Add(zone.Number);
        LoadZoneEditor();
        Render();
    }

    private void UpdateZoneChips()
    {
        foreach (var chip in BoardZonesPanel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>())
        {
            var number = (int)chip.Tag;
            chip.IsChecked = selectedZones.Contains(number);
            var zone = boardZones.First(z => z.Zone == number);
            var option = BoardOptions.First(o => o.Effect == zone.Effect);
            var dot = (Ellipse)((StackPanel)chip.Content).Children[0];
            dot.Fill = zone.Effect == BoardEffect.Off ? (Brush)FindResource("ControlDisabledBrush")
                : option.Color ? Brush(zone.Color)
                : new LinearGradientBrush(new GradientStopCollection { new(Hue(0), 0), new(Hue(120), 0.5), new(Hue(240), 1) }, 0);
        }
        BoardAllButton.Visibility = selectedZones.Count == zoneList.Count ? Visibility.Collapsed : Visibility.Visible;
    }

    private MemoryOption SelectedMemory => (MemoryOption)((ComboBoxItem)MemoryEffect.SelectedItem).Tag;
    private BoardOption SelectedBoard => (BoardOption)((ComboBoxItem)BoardEffectPicker.SelectedItem).Tag;

    private MemoryLighting EditedMemory => new(SelectedMemory.Mode, NormalizedHex(MemoryHex.Text) ?? "#000000", (int)MemoryBrightness.Value, (int)MemorySpeed.Value, MemoryReverse.IsChecked == true);
    private BoardZoneLighting EditedZone(BoardZoneLighting zone) => new(zone.Zone, SelectedBoard.Effect, NormalizedHex(BoardHex.Text) ?? zone.Color, (int)BoardBrightness.Value, (int)BoardSpeed.Value);

    private void MemoryChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || updating) return;
        memoryDirty = true;
        foreach (var slot in moduleLooks.Keys.ToArray())
        {
            if (MemorySyncToggle.IsChecked != true && (ModulePicker.SelectedItem as ModuleChoice)?.Slot != slot) continue;
            if (SelectedMemory.Color && NormalizedHex(MemoryHex.Text) == null) invalidModuleColors[slot] = MemoryHex.Text;
            else { invalidModuleColors.Remove(slot); moduleLooks[slot] = moduleLooks[slot] with { Lighting = EditedMemory }; }
            dirtyModules.Add(slot);
        }
        message = null;
        Render();
        QueueAutomaticApply();
    }

    private void BoardChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || updating) return;
        // Each edit belongs to the selection at the moment it is made, including Rainbow.
        boardZones = boardZones.Select(z => selectedZones.Contains(z.Zone) ? EditedZone(z) : z).ToList();
        foreach (var zone in selectedZones)
            if (SelectedBoard.Color && NormalizedHex(BoardHex.Text) == null) invalidZoneColors[zone] = BoardHex.Text;
            else invalidZoneColors.Remove(zone);
        dirtyZones.UnionWith(selectedZones);
        boardDirty = true;
        message = null;
        Render();
        QueueAutomaticApply();
    }

    private void QueueAutomaticApply()
    {
        if (!ready || updating || service?.AutomaticChangesAllowed != true || !HasPendingEdits) return;
        autoApplyTimer.Stop();
        if ((SyncOn && SelectedSync.Color && NormalizedHex(SyncHex.Text) == null)
            || (!SyncOn && ((memoryDirty && SelectedMemory.Color && NormalizedHex(MemoryHex.Text) == null)
                || invalidModuleColors.Count > 0 || invalidZoneColors.Count > 0))) return;
        try
        {
            editBaseline ??= service.Settings;
            editorRevision++;
            service.RememberEdits(
                !SyncOn && memoryDirty && MemorySyncToggle.IsChecked == true ? EditedMemory : null,
                !SyncOn && memoryDirty && MemorySyncToggle.IsChecked != true ? moduleLooks.Values.Where(m => dirtyModules.Contains(m.Slot)).ToArray() : null,
                !SyncOn && boardDirty ? boardZones.Where(z => dirtyZones.Contains(z.Zone)).ToArray() : null,
                SyncOn ? EditedSync : null);
            message = null; messageIsError = false;
            Render();
            if (ApplyButton.IsEnabled)
            {
                message = "Changes saved locally. Applying to available hardware…";
                autoApplyTimer.Interval = TimeSpan.FromMilliseconds(900);
                autoApplyTimer.Start();
            }
            else message = "Changes saved locally; not sent to hardware. Reconnect the edited devices and check again. Discard restores the previous saved look.";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { message = error.Message; messageIsError = true; }
        Render();
    }

    private void StartupApplyChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || updating || service == null) return;
        // Saved immediately: it is a preference, not a hardware change.
        service.SaveStartupPreference(StartupApply.IsChecked == true);
        Render();
    }

    private void ExitRainbowChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || updating || service == null) return;
        service.SaveExitPreference(ExitRainbow.IsChecked == true);
    }

    /// <summary>A hardware change straight away (CC 47 01 or 00 to the board controller), so it is not staged like the editors.</summary>
    private async void KeepOnBoardChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || updating || service == null) return;
        var keep = KeepOnBoard.IsChecked == true;
        if (service.Busy) { updating = true; KeepOnBoard.IsChecked = !keep; updating = false; return; }
        message = null;
        Render();
        try
        {
            await service.KeepOnBoardAsync(keep);
            message = keep
                ? "Saved on the motherboard: it starts with the lighting you applied" + (boardDirty || syncDirty ? " (not the edits you have not applied yet; Apply saves them too)." : ".") + " Each Apply is saved there too."
                : "The motherboard starts with its built-in lighting again after a shutdown.";
            messageIsError = false;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            updating = true; KeepOnBoard.IsChecked = service.Settings.KeepOnBoard; updating = false;
            message = error.Message; messageIsError = true;
        }
        Render();
    }

    private async void ApplyClicked(object sender, RoutedEventArgs e)
    {
        if (service == null || service.Busy) return;
        if (!ApplyButton.IsEnabled)
        {
            message = "Lighting was not sent. Check hardware availability and correct any invalid values before applying.";
            messageIsError = true; Render(); return;
        }
        autoApplyTimer.Stop();
        var appliedRevision = editorRevision;
        if (SyncOn)
        {
            var sync = EditedSync;
            var hasMemory = service.State?.Dimms.Count > 0;
            var hasBoard = service.State?.Board != null;
            message = null;
            Render();
            try
            {
                await service.ApplySyncAsync(sync, hasMemory, hasBoard, StartupApply.IsChecked == true);
                if (appliedRevision != editorRevision) { autoApplyTimer.Start(); return; }
                editBaseline = null;
                LoadControls(keepSync: true);
                syncDirty = false;
                message = (hasMemory, hasBoard) switch
                {
                    (true, true) when SyncEngine.Animated(sync.Effect) => "Memory and motherboard are running in lockstep. Sora keeps them together while it runs in the tray.",
                    (true, true) => "Memory and motherboard are in sync. The memory read back exactly what was written.",
                    (true, false) => "Applied to the memory; the motherboard controller was not found.",
                    _ => "Applied to the motherboard; the memory is not available.",
                };
                messageIsError = false;
            }
            catch (Exception error) when (error is not OutOfMemoryException) { message = error.Message; messageIsError = true; RetryAutomaticApply(); }
            Render();
            return;
        }
        var memory = memoryDirty && MemorySyncToggle.IsChecked == true ? EditedMemory : null;
        var modules = memoryDirty && MemorySyncToggle.IsChecked != true ? moduleLooks.Values.Where(m => dirtyModules.Contains(m.Slot)).ToArray() : null;
        var board = boardDirty ? boardZones.Where(z => dirtyZones.Contains(z.Zone)).ToArray() : null;
        message = null;
        Render();
        try
        {
            await service.ApplyAsync(memory, board, StartupApply.IsChecked == true, modules);
            if (appliedRevision != editorRevision) { autoApplyTimer.Start(); return; }
            editBaseline = null;
            if (memory != null || modules != null) { memoryDirty = false; dirtyModules.Clear(); }
            if (board != null) { boardDirty = false; dirtyZones.Clear(); }
            message = (memory, board) switch
            {
                ({ }, { }) => "Applied to the memory and the motherboard. The memory read back exactly what was written.",
                ({ }, null) => "Applied to the memory. Module settings read back exactly what was written.",
                (null, null) when modules != null => "Applied to the selected memory modules and read back.",
                _ => "Sent to the motherboard controller.",
            };
            messageIsError = false;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { message = error.Message; messageIsError = true; RetryAutomaticApply(); }
        Render();
    }

    private void RetryAutomaticApply()
    {
        if (service?.AutomaticChangesAllowed != true || !HasPendingEdits) return;
        autoApplyTimer.Interval = TimeSpan.FromSeconds(20);
        autoApplyTimer.Start();
    }

    private async void RestoreClicked(object sender, RoutedEventArgs e)
    {
        if (service == null || service.Busy) return;
        message = null;
        Render();
        try
        {
            await service.RestoreOriginalAsync();
            LoadMemoryControls();
            updating = true; SyncToggle.IsChecked = service.Settings.Sync; updating = false;
            message = "The memory is back to its original lighting, read back from both sticks. Sora no longer sets it at sign-in.";
            messageIsError = false;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { message = error.Message; messageIsError = true; }
        Render();
    }

    private void DiscardClicked(object sender, RoutedEventArgs e)
    {
        autoApplyTimer.Stop();
        if (editBaseline != null && service != null) service.RestoreEdits(editBaseline);
        editBaseline = null;
        LoadControls();
        message = null;
        Render();
    }

    private async void RefreshClicked(object sender, RoutedEventArgs e) => await ReadAsync();

    private void CheckConflict()
    {
        if (service?.State is not { } state || service.Busy) return;
        var conflict = EneDram.FindConflict();
        if (conflict == state.Conflict) return;
        // Conflict changed: rescan so the backup and the readings reflect the RAM's own state.
        _ = ReadAsync();
    }

    private void Render()
    {
        if (!ready || service == null) return;
        var state = service.State;
        var busy = service.Busy;
        SeparateCards.IsEnabled = !busy; SyncCard.IsEnabled = !busy; SyncToggle.IsEnabled = !busy;

        // Memory editor.
        var memoryOption = SelectedMemory;
        var memoryColor = NormalizedHex(MemoryHex.Text);
        ControlState.SetValidation(MemoryHex, MemoryColorError, memoryOption.Color && memoryColor == null ? ControlState.HexColourError : null);
        MemoryColorRow.IsEnabled = memoryOption.Color;
        MemoryBrightness.IsEnabled = memoryOption.Color;
        MemorySpeed.IsEnabled = memoryOption.Speed;
        MemoryReverse.Visibility = memoryOption.Mode == EneMode.Rainbow ? Visibility.Visible : Visibility.Collapsed;
        MemoryBrightnessLabel.Text = memoryOption.Color ? $"{(int)MemoryBrightness.Value}%" : "—";
        MemorySpeedLabel.Text = memoryOption.Speed ? $"{(int)MemorySpeed.Value} / 5" : "—";
        MemoryColorPreview.Background = memoryOption.Color ? Brush(memoryColor) : (Brush)FindResource("ControlDisabledBrush");
        if (memoryColor != null) MemoryWheel.Hex = memoryColor;
        MemoryHint.Text = memoryOption.Hint + (memoryOption.Color ? "" : " Uses the memory's built-in colours.");
        var dimmCount = state?.Dimms.Count is > 0 ? state.Dimms.Count : 2;
        ModulePicker.Visibility = MemorySyncToggle.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        MemorySyncToggle.IsEnabled = !busy;
        ModulePicker.IsEnabled = !busy;
        var ledCount = state?.Dimms.FirstOrDefault()?.LedCount ?? 8;
        if (SyncToggle.IsChecked != true) DrawDimms(DimmPreview, dimmCount, ledCount, memoryOption.Mode, memoryColor, MemoryBrightness.Value, MemoryReverse.IsChecked == true);

        // Sync editor and preview.
        var syncOn = SyncOn;
        SyncCard.Visibility = syncOn ? Visibility.Visible : Visibility.Collapsed;
        SeparateCards.Visibility = syncOn ? Visibility.Collapsed : Visibility.Visible;
        var syncOption = SelectedSync;
        var syncColor = NormalizedHex(SyncHex.Text);
        ControlState.SetValidation(SyncHex, SyncColorError, syncOption.Color && syncColor == null ? ControlState.HexColourError : null);
        SyncColorRow.IsEnabled = syncOption.Color;
        SyncBrightness.IsEnabled = syncOption.Color;
        SyncSpeed.IsEnabled = syncOption.Speed;
        SyncBrightnessLabel.Text = syncOption.Color ? $"{(int)SyncBrightness.Value}%" : "—";
        SyncSpeedLabel.Text = syncOption.Speed ? $"{(int)SyncSpeed.Value} / 5" : "—";
        SyncColorPreview.Background = syncOption.Color ? Brush(syncColor) : (Brush)FindResource("ControlDisabledBrush");
        if (syncColor != null) SyncWheel.Hex = syncColor;
        SyncHint.Text = syncOption.Hint;
        if (syncOn)
        {
            DrawDimms(SyncDimmPreview, dimmCount, ledCount, EditedSync.ToMemory().Effect, syncColor, SyncBrightness.Value, false);
            DrawSyncZones(syncOption.Effect, syncColor, SyncBrightness.Value);
            SyncDevicesCaption.Text = string.Join(" · ", new[] { state?.Dimms.Count > 0 ? state.MemoryName : null, state?.Board != null ? $"{zoneList.Count} board lights" : null }.Where(t => t != null));
            SyncNotice.Text = state == null ? "" : (state.Dimms.Count, state.Board) switch
            {
                (0, null) => "Neither the memory nor the motherboard controller is available.",
                (0, _) => "The memory is not available, so only the motherboard will change. " + (state.MemoryError ?? ""),
                (_, null) => "The motherboard controller was not found, so only the memory will change.",
                _ => "",
            };
            SyncNotice.Visibility = SyncNotice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Motherboard editor.
        var boardOption = SelectedBoard;
        var boardColor = NormalizedHex(BoardHex.Text);
        ControlState.SetValidation(BoardHex, BoardColorError, boardOption.Color && boardColor == null ? ControlState.HexColourError : null);
        BoardColorRow.IsEnabled = boardOption.Color;
        BoardBrightness.IsEnabled = boardOption.Brightness;
        BoardSpeed.IsEnabled = boardOption.Speed;
        BoardBrightnessLabel.Text = boardOption.Brightness ? $"{new[] { 10, 20, 30, 40, 50, 60, 70, 80, 100 }[(int)BoardBrightness.Value]}%" : "—";
        BoardSpeedLabel.Text = boardOption.Speed ? $"{(int)BoardSpeed.Value + 1} / 10" : "—";
        BoardColorPreview.Background = boardOption.Color ? Brush(boardColor) : (Brush)FindResource("ControlDisabledBrush");
        if (boardColor != null) BoardWheel.Hex = boardColor;
        UpdateZoneChips();
        var editing = selectedZones.Count == zoneList.Count ? $"Editing all {zoneList.Count} lights." : "Editing " + string.Join(", ", zoneList.Where(z => selectedZones.Contains(z.Number)).Select(z => z.Name)) + ".";
        BoardHint.Text = string.Join(" ", new[] { editing, boardOption.Hint, service.Settings.BoardZones == null ? "Choose a look to save and apply it automatically." : "" }.Where(text => text.Length > 0));

        // Hardware facts.
        MemoryCaption.Text = state == null ? (busy ? "Checking…" : "Not checked yet")
            : state.Dimms.Count > 0 ? $"{state.MemoryName} · {state.Dimms[0].LedCount} LEDs each"
            : "Not available";
        MemoryNotice.Text = state?.Dimms.Count == 0 ? state.MemoryError ?? "No RGB memory controller was found." : "";
        MemoryNotice.Visibility = MemoryNotice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BoardCaption.Text = state == null ? (busy ? "Checking…" : "")
            : state.Board is { } board ? $"RGB Fusion · IT5711 · firmware {board.FirmwareText}"
            : "Not found";
        ConflictBanner.Visibility = state?.Conflict != null ? Visibility.Visible : Visibility.Collapsed;
        ConflictTitle.Text = state?.Conflict?.Replace(" Exit it first.", "") ?? "";
        BackupText.Text = state?.HasBackup == true ? "Saved before Sora first changed it. Restore puts it back exactly." + (syncOn ? " Restoring also turns sync off." : "")
            : state?.Conflict != null ? "Saved once the other RGB program has exited."
            : "Saved the first time Sora reads your RAM.";

        // Status chip.
        var (chip, tone) = busy ? ("Working…", "Idle")
            : state == null ? ("Not checked", "Idle")
            : state.Conflict != null ? ("Another RGB app is running", "Warn")
            : service.RestorePending ? ("Restoring saved lighting", "Warn")
            : syncOn && !syncDirty && SyncSummary is { } synced ? (synced + (service.SyncApplied && service.SyncRunning ? " · live" : ""), service.SyncApplied ? "Good" : "Warn")
            : state.Dimms.Count > 0 ? ($"Memory {ModeName(state.Dimms[0].Original).ToLowerInvariant()} now", "Good")
            : ("Memory not available", "Bad");
        StatusText.Text = chip;
        StatusDot.Fill = (Brush)FindResource(tone);

        // Action bar.
        var memoryValid = (!memoryOption.Color || memoryColor != null) && invalidModuleColors.Count == 0;
        var boardValid = (!boardOption.Color || boardColor != null) && invalidZoneColors.Count == 0;
        var memoryAvailable = state?.Dimms.Count > 0;
        var boardAvailable = state?.Board != null;
        var unavailable = !memoryAvailable || !boardAvailable;
        AvailabilityNotice.Visibility = unavailable ? Visibility.Visible : Visibility.Collapsed;
        AvailabilityNotice.Content = !memoryAvailable && !boardAvailable
            ? "Offline lighting editor. No lighting controller is available. Colour, zone and RAM-sync controls edit local preferences; they do not confirm a device connection. Apply and Restore require available hardware."
            : "Some lighting hardware is unavailable. You can edit saved preferences offline; only available devices can receive lighting. Changes for unavailable devices remain unsent.";
        SyncCaption.Text = unavailable ? "Saved preference · applies when the required controllers are available." : "One effect, colour, brightness and speed for the RAM and every board light.";
        var canApply = syncOn
            ? !busy && state != null && state.Conflict == null && (!syncOption.Color || syncColor != null) && (syncDirty || !service.SyncApplied) && (memoryAvailable || boardAvailable)
            : !busy && state != null && state.Conflict == null && memoryValid && boardValid
              && (memoryDirty || boardDirty) && (!memoryDirty || memoryAvailable) && (!boardDirty || boardAvailable);
        ApplyButton.IsEnabled = canApply;
        DiscardButton.IsEnabled = !busy && (syncOn ? syncDirty : memoryDirty || boardDirty);
        RestoreButton.IsEnabled = !busy && state?.HasBackup == true && state.Conflict == null && memoryAvailable;
        KeepOnBoard.IsEnabled = !busy && state?.Conflict == null && boardAvailable;
        RefreshButton.IsEnabled = !busy;
        ControlState.SetIsBusy(RefreshButton, busy);
        ChangeSummary.Text = busy ? "Working…" : syncOn ? (syncDirty ? "Sync lighting changed" : service.SyncApplied ? "In sync" : "Saved sync not applied") : (memoryDirty, boardDirty) switch
        {
            (true, true) => "Memory and motherboard changed",
            (true, false) => "Memory changed",
            (false, true) => "Motherboard changed",
            _ => "No pending changes",
        };
        ApplyDetail.Foreground = (Brush)FindResource(messageIsError && message != null ? "ErrorTextBrush" : "TertiaryTextBrush");
        ApplyDetail.Text = message
            ?? (state?.Conflict != null ? "Applying is paused while another RGB program is running."
            : state == null ? "Press Check again to read your hardware."
            : !memoryAvailable && !boardAvailable ? "Offline · preferences can be edited locally. No lighting has been sent. Check again after reconnecting or opening Sora as administrator."
            : (memoryDirty && !memoryAvailable || boardDirty && !boardAvailable) ? "Edits include unavailable hardware and have not been sent. Reconnect and check again, or discard the edits."
            : service.RestorePending ? service.RestoreStatus ?? "Restoring saved lighting…"
            : "Changes are saved automatically and applied after you pause editing. Saved lighting returns when Sora starts, including in the tray.");
        StatusChanged?.Invoke();
    }

    /// <summary>The board lights as pills, coloured the way the chosen sync effect shows them.</summary>
    private void DrawSyncZones(SyncEffect effect, string? hex, double brightness)
    {
        SyncZonePreview.Children.Clear();
        var color = hex != null ? ParseColor(hex) : Colors.Black;
        var scaled = Color.FromRgb((byte)(color.R * brightness / 100), (byte)(color.G * brightness / 100), (byte)(color.B * brightness / 100));
        var spectrum = new LinearGradientBrush(new GradientStopCollection { new(Hue(0), 0), new(Hue(120), 0.5), new(Hue(240), 1) }, 0);
        foreach (var zone in zoneList)
        {
            Brush fill = effect switch
            {
                SyncEffect.Off => (Brush)FindResource("ControlDisabledBrush"),
                SyncEffect.Rainbow => zone.Addressable ? spectrum : Brushes.White,
                SyncEffect.ColorCycle => spectrum,
                _ => new SolidColorBrush(scaled),
            };
            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), Fill = fill, Stroke = (Brush)FindResource("InputBorderBrush"), StrokeThickness = 1 };
            SyncZonePreview.Children.Add(new Border
            {
                Background = (Brush)FindResource("SurfaceRaisedBrush"), BorderBrush = (Brush)FindResource("HairlineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13),
                Padding = new Thickness(9, 4, 11, 4), Margin = new Thickness(0, 0, 6, 6),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, new TextBlock { Text = zone.Name, FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush") } } },
            });
        }
    }

    private void DrawDimms(Panel target, int count, int leds, EneMode mode, string? hex, double brightness, bool reverse)
    {
        target.Children.Clear();
        var color = hex != null ? ParseColor(hex) : Colors.Black;
        var scaled = Color.FromRgb((byte)(color.R * brightness / 100), (byte)(color.G * brightness / 100), (byte)(color.B * brightness / 100));
        for (var stick = 0; stick < count; stick++)
        {
            var module = target == DimmPreview && MemorySyncToggle.IsChecked != true && service?.State?.Dimms.ElementAtOrDefault(stick) is { } dimm
                ? moduleLooks.GetValueOrDefault(dimm.Slot)?.Lighting : null;
            var shownMode = module?.Effect ?? mode;
            var moduleColor = module == null ? scaled : ParseColor(module.Color);
            if (module != null) moduleColor = Color.FromRgb((byte)(moduleColor.R * module.Brightness / 100), (byte)(moduleColor.G * module.Brightness / 100), (byte)(moduleColor.B * module.Brightness / 100));
            var bar = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Height = 10, Margin = new Thickness(10, 0, 10, 0) };
            for (var led = 0; led < leds; led++)
            {
                var fill = shownMode switch
                {
                    EneMode.Off => Color.FromRgb(0x2A, 0x2B, 0x2F),
                    EneMode.ColorCycle or EneMode.Rainbow => Hue(((module?.Reverse ?? reverse) && shownMode == EneMode.Rainbow ? leds - 1 - led : led) * 300.0 / Math.Max(1, leds - 1)),
                    _ => moduleColor,
                };
                bar.Children.Add(new Border { Background = new SolidColorBrush(fill), CornerRadius = new CornerRadius(2), Margin = new Thickness(1, 0, 1, 0), Effect = shownMode == EneMode.Off ? null : new System.Windows.Media.Effects.DropShadowEffect { Color = fill, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.7 } });
            }
            var body = new Grid { MaxWidth = 440, Margin = new Thickness(0, stick == 0 ? 0 : 10, 0, 0) };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var diffuser = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1D, 0x21)), CornerRadius = new CornerRadius(6, 6, 0, 0), Padding = new Thickness(0, 5, 0, 5), Child = bar };
            var spreader = new Border { Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2B, 0x30), Color.FromRgb(0x1E, 0x1F, 0x23), 90), CornerRadius = new CornerRadius(0, 0, 4, 4), BorderBrush = (Brush)FindResource("HairlineBrush"), BorderThickness = new Thickness(1, 0, 1, 1) };
            Grid.SetRow(spreader, 1);
            spreader.Child = new TextBlock { Text = SlotName(service?.State?.Dimms.ElementAtOrDefault(stick)?.Slot ?? stick * 2 + 1), FontSize = 11, Foreground = (Brush)FindResource("TertiaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            body.Children.Add(diffuser);
            body.Children.Add(spreader);
            target.Children.Add(body);
        }
    }

    private void BuildSwatches(WrapPanel panel, Action<string> pick, string owner)
    {
        foreach (var hex in SwatchColors)
        {
            var swatch = new Button { Style = (Style)FindResource("Swatch"), Width = 18, Height = 18, Padding = new Thickness(0), Margin = new Thickness(0, 2, 5, 2), Background = Brush(hex), ToolTip = hex };
            AutomationProperties.SetName(swatch, owner + " colour " + hex);
            swatch.Click += (_, _) => pick(hex);
            panel.Children.Add(swatch);
        }
    }

    /// <summary>Gigabyte's silkscreen names: SPD slots 0-3 are DDR5_A1, A2, B1, B2 on four-slot boards.</summary>
    private static string SlotName(int slot) => slot is >= 0 and < 4 ? $"DDR5_{(char)('A' + slot / 2)}{slot % 2 + 1}" : $"Slot {slot}";

    private static string ModeName(EneSnapshot snapshot) => EneDram.IsOff(snapshot.Control, snapshot.Colors) ? "Off"
        : snapshot.Control.Length >= 2 && snapshot.Control[0] == 0 && snapshot.Control[1] <= 5 ? MemoryOptions.First(o => (byte)o.Mode == snapshot.Control[1]).Name
        : "custom";

    private static string? NormalizedHex(string text)
    {
        var value = text.Trim();
        if (!value.StartsWith('#')) value = "#" + value;
        return value.Length == 7 && value.Skip(1).All(Uri.IsHexDigit) ? value.ToUpperInvariant() : null;
    }

    private static Color ParseColor(string hex) { var (r, g, b) = LightingService.ParseColor(hex); return Color.FromRgb(r, g, b); }
    private static Brush Brush(string? hex) => hex == null ? Brushes.Transparent : new SolidColorBrush(ParseColor(hex));

    private static Color Hue(double degrees)
    {
        var h = (degrees % 360 + 360) % 360 / 60;
        var x = (byte)Math.Round(255 * (1 - Math.Abs(h % 2 - 1)));
        return (int)h switch { 0 => Color.FromRgb(255, x, 0), 1 => Color.FromRgb(x, 255, 0), 2 => Color.FromRgb(0, 255, x), 3 => Color.FromRgb(0, x, 255), 4 => Color.FromRgb(x, 0, 255), _ => Color.FromRgb(255, 0, x) };
    }
}
