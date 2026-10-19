using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Sora.Core;

namespace Sora.App;

public partial class LogitechPanel : UserControl
{
    private LogitechSnapshot? snapshot;
    private DeviceInfo[] devices = [];
    private int selectedDevice = -1;
    private int draftDpi, draftRate;
    private bool ready, rendering, invalidLight;
    private int previousZoneIndex;
    private bool ghubRunning;
    private readonly Dictionary<byte, byte[]> lightDrafts = [];
    private readonly List<LogitechPreset> presets = [];
    private static readonly string DataDirectory = Path.Combine(SoraData.DirectoryPath, "logitech");
    private string? OriginalPath => snapshot == null ? null : Path.Combine(DataDirectory, string.Concat(snapshot.Identity.Where(char.IsLetterOrDigit)) + "-original.json");
    public event Action<string>? StatusChanged;
    public bool IsBusy { get; private set; }
    public bool HasPendingEdits => snapshot != null && (HardwareSettingsDirty || (snapshot.Sensors.Length == 1 && (SensitivityDirty || !ValidSensitivity())) || invalidLight || (snapshot.Lighting ?? []).Any(z => LightingDirty(z.Index)));
    private bool SensitivityDirty => snapshot?.Sensors.Length == 1 && (draftDpi != baselineDpi || draftRate != baselineRate || ControlsDirty || dpiSelectionPending);
    private LogitechLightZone? SelectedZone => ZonePicker.SelectedIndex >= 0 ? snapshot?.Lighting?.ElementAtOrDefault(ZonePicker.SelectedIndex) : null;

    public LogitechPanel()
    {
        InitializeComponent(); ready = true;
        void SelectPreviewZone(int index)
        {
            if (IsBusy || snapshot?.Lighting == null || LinkZonesCheck.IsChecked == true) return;
            Sections.SelectedItem = LightingTab;
            ZonePicker.SelectedIndex = Array.FindIndex(snapshot.Lighting, z => z.Index == index); UpdatePreview();
        }
        LightPreview.ZoneSelected += SelectPreviewZone; MousePreview.ZoneSelected += SelectPreviewZone;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) => { if (!IsVisible) return; CheckConflict(); await PollLiveAsync(); SetEnabled(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { CheckConflict(); timer.Start(); } else timer.Stop(); };
        Unloaded += (_, _) => timer.Stop();
        SetEnabled();
    }

    public async Task ReadAsync()
    {
        if (IsBusy) return;
        if (HasPendingEdits) { ShowError("Apply or discard your pending edits before refreshing."); return; }
        await RunAsync(async () =>
        {
            devices = await Task.Run(LogitechDevice.Enumerate);
            rendering = true; DevicePicker.ItemsSource = devices.Select(d => "G502 HERO · " + (string.IsNullOrWhiteSpace(d.SerialNumber) ? $"Mouse {Array.IndexOf(devices, d) + 1}" : d.SerialNumber[^Math.Min(4, d.SerialNumber.Length)..])).ToArray();
            selectedDevice = devices.Length > 0 ? 0 : -1; DevicePicker.SelectedIndex = selectedDevice; rendering = false;
            if (selectedDevice < 0)
            {
                snapshot = null; lightDrafts.Clear(); ConnectionText.Text = "No supported Logitech mouse found";
                DeviceTitle.Text = "Connect your mouse"; DeviceSubtitle.Text = "G502 HERO · wired USB"; ModeText.Text = "Disconnected";
                DpiValue.Text = "—"; ConflictText.Visibility = Visibility.Collapsed; DetailsText.Text = "Connect a wired G502 HERO and refresh. Other Logitech models and wireless receivers are not yet supported.";
                EditStatus.Text = "Connect a G502 HERO over USB, then refresh."; StatusChanged?.Invoke("Mouse · Not connected");
                MousePreview.Connected = LightPreview.Connected = false; MousePreview.InvalidateVisual(); LightPreview.InvalidateVisual();
                MemorySlots.Children.Clear(); return;
            }
            await LoadSelectedAsync();
        }, "Reading Logitech mouse…");
    }

    private async Task LoadSelectedAsync()
    {
        snapshot = null; lightDrafts.Clear(); invalidLight = false;
        ConnectionText.Text = "Reading mouse…"; DpiValue.Text = "—"; ModeText.Text = "Checking";
        snapshot = await WithDeviceAsync(d => d.ReadAsync());
        draftDpi = snapshot.Sensors.SingleOrDefault()?.Dpi ?? 0; draftRate = snapshot.ReportRate ?? 0;
        lightDrafts.Clear(); foreach (var zone in snapshot.Lighting ?? []) lightDrafts[zone.Index] = zone.State.ToArray();
        rendering = true;
        DeviceTitle.Text = snapshot.Name; DeviceSubtitle.Text = "Wired · USB 046D:C08B · HID++ " + snapshot.Protocol;
        ConnectionText.Text = "Connected directly over USB"; ModeText.Text = snapshot.OnboardMode == 1 ? "Firmware control" : "Software control";
        ConflictText.Text = string.Join("\n", snapshot.Warnings); ConflictText.Visibility = snapshot.Warnings.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var sensor = snapshot.Sensors.SingleOrDefault();
        if (sensor != null)
        {
            DpiSlider.Minimum = sensor.Minimum; DpiSlider.Maximum = sensor.Maximum; DpiSlider.TickFrequency = Math.Max(1, sensor.Step);
            DpiSlider.Value = draftDpi; DpiValue.Text = draftDpi.ToString("N0"); DpiInput.Text = draftDpi.ToString();
            RangeText.Text = $"{sensor.Minimum:N0} – {sensor.Maximum:N0} DPI" + (sensor.Step > 0 ? $" · {sensor.Step} DPI steps" : " · supported values only");
        }
        RatePicker.ItemsSource = snapshot.ReportRates.Select(r => $"{r:N0} Hz  ·  {1000.0 / r:0.###} ms").ToArray(); RatePicker.SelectedIndex = Array.IndexOf(snapshot.ReportRates, draftRate);
        DpiStops.Children.Clear();
        foreach (var value in new[] { 400, 800, 1600, 3200, 6400 }.Where(v => sensor?.Accepts(v) == true))
        {
            var button = new Button { Content = value.ToString(), Tag = value, Style = (Style)FindResource("SmallButton"), Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => DpiSlider.Value = (int)button.Tag; DpiStops.Children.Add(button);
        }
        ZonePicker.ItemsSource = snapshot.Lighting?.Select(z => z.Index == 0 ? "DPI LEDs" : z.Index == 1 ? "Logo" : z.Name).ToArray(); ZonePicker.SelectedIndex = snapshot.Lighting?.Length > 1 ? 1 : snapshot.Lighting?.Length > 0 ? 0 : -1;
        DetailsText.Text = $"{snapshot.Name}\nUSB 046D:{snapshot.ProductId:X4} · HID++ {snapshot.Protocol}\nDevice identity: {snapshot.Identity}\n{snapshot.Features.Length} discovered features\n\n" + string.Join("\n", snapshot.Features.Select(f => $"0x{f.Id:X4} · index {f.Index} · version {f.Version}"));
        MemorySlots.Children.Clear(); MemoryStatus.Text = "Read the five onboard slots to inspect saved assignments. Software-mode G HUB assignments are separate from onboard memory.";
        rendering = false; PopulateZone(); LoadPresets(); UpdatePreview();
        LoadControlEditor();
        LoadReactiveEditor();
        CheckConflict();
        await ReadHardwareSettingsAsync();
        StatusChanged?.Invoke($"G502 HERO · {draftDpi:N0} DPI");
        EditStatus.Text = "Settings read from your mouse."; ErrorText.Visibility = Visibility.Collapsed;
    }

    private async Task<T> WithDeviceAsync<T>(Func<LogitechDevice, Task<T>> action)
    {
        var info = devices.ElementAtOrDefault(selectedDevice) ?? throw new IOException("No Logitech mouse is selected.");
        if (controls != null) return await controls.UseDeviceAsync(action);
        return await Task.Run(async () => { await using var device = new LogitechDevice(info); return await action(device); });
    }

    private async Task RunAsync(Func<Task> action, string message)
    {
        if (IsBusy) return;
        IsBusy = true; EditStatus.Text = message; ErrorText.Visibility = Visibility.Collapsed; ErrorText.Tag = null; SetEnabled();
        try { await action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or JsonException or FormatException or NotSupportedException or ArgumentException)
        {
            ShowError(error.Message); EditStatus.Text = "Operation did not complete.";
            if (snapshot == null) { ConnectionText.Text = "Mouse unavailable · refresh to retry"; StatusChanged?.Invoke("Mouse · Unavailable"); }
        }
        finally { IsBusy = false; rendering = false; SetEnabled(); }
    }

    private void SetEnabled()
    {
        if (!ready) return;
        ControlState.SetValidation(HexInput, HexError, invalidLight && !invalidRgb ? ControlState.HexColourError : null);
        foreach (var channel in new[] { RedInput, GreenInput, BlueInput })
        {
            var invalid = invalidRgb && !byte.TryParse(channel.Text, out _);
            ControlState.SetIsInvalid(channel, invalid);
            System.Windows.Automation.AutomationProperties.SetHelpText(channel, invalid ? "Use a whole number from 0 to 255." : "");
        }
        RefreshButton.IsEnabled = !IsBusy; ControlState.SetIsBusy(RefreshButton, IsBusy); DevicePicker.IsEnabled = !IsBusy && devices.Length > 1;
        MacroPicker.IsEnabled = !IsBusy && MacroPicker.HasItems;
        ControlState.SetPlaceholder(MacroPicker, MacroPicker.HasItems ? "Choose a saved macro" : "No saved macros");
        ZonePicker.IsEnabled = !IsBusy && LinkZonesCheck.IsChecked != true;
        ApplyDeviceSettingsButton.IsEnabled = !IsBusy && snapshot != null && hardwareSettings != null && !ghubRunning;
        DpiAlwaysOnCheck.IsEnabled = StartupEffectCheck.IsEnabled = !IsBusy && snapshot != null && hardwareSettings != null;
        OriginalButton.IsEnabled = !IsBusy && OriginalPath is { } original && File.Exists(original);
        Sections.IsEnabled = !IsBusy; SensitivityEditor.IsEnabled = snapshot?.Sensors.Length == 1; LightEditor.IsEnabled = snapshot?.Lighting?.Length > 0;
        AssignmentEditor.IsEnabled = MacroEditor.IsEnabled = GShiftLayerCheck.IsEnabled = snapshot != null;
        MemoryReadButton.IsEnabled = !IsBusy && snapshot?.Features.Any(f => f.Id == 0x8100) == true;
        var lighting = LightingTab.IsSelected; var zone = SelectedZone;
        var profileSection = SensitivityTab.IsSelected || AssignmentsTab.IsSelected || MacrosTab.IsSelected;
        var editorVisible = lighting || profileSection ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.Visibility = OriginalButton.Visibility = editorVisible;
        DiscardButton.Visibility = editorVisible == Visibility.Visible || DetailsTab.IsSelected ? Visibility.Visible : Visibility.Collapsed;
        var dirty = lighting ? zone != null && LightingDirty(zone.Index) : SensitivityDirty || controls?.Running != true;
        var valid = lighting ? zone != null && TryLightState(out _) : ValidSensitivity();
        ApplyButton.IsEnabled = !IsBusy && dirty && valid && (lighting || profileSection) && !ghubRunning;
        DiscardButton.IsEnabled = !IsBusy && (HasPendingEdits || ErrorText.Tag is true);
        ApplyButton.Content = IsBusy ? "Working…" : lighting ? "Apply lighting" : "Apply profile";
        ApplyButton.ToolTip = ghubRunning ? "Exit G HUB from its tray menu before applying." : snapshot?.OnboardMode == 1 && !lighting ? "Sora takes software control on Apply. Your saved onboard slots are preserved." : "Apply and verify by device readback.";
        if (!IsBusy && snapshot != null && ErrorText.Visibility != Visibility.Visible)
            EditStatus.Text = HasPendingEdits ? "Unapplied changes · Apply profile saves DPI and assignments" : controls?.Running == true ? controls.Status : "Settings read from your mouse.";
    }

    private bool ValidSensitivity()
    {
        if (snapshot?.Sensors.SingleOrDefault()?.Accepts(draftDpi) != true || !snapshot.ReportRates.Contains(draftRate) || !int.TryParse(DpiInput.Text, out var input) || input != draftDpi) return false;
        if (controlDraft == null) return true;
        try { BuildControls(); return true; } catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException) { return false; }
    }
    private void ShowError(string text, bool invalidInput = false) { ErrorText.Text = text; ErrorText.Tag = invalidInput; ErrorText.Visibility = Visibility.Visible; }
    private void ClearInputError() { ErrorText.Visibility = Visibility.Collapsed; ErrorText.Tag = null; }
    private void CheckConflict()
    {
        ghubRunning = LogitechDevice.IsGHubRunning();
        if (snapshot == null) return;
        var otherWarnings = snapshot.Warnings.Where(w => !w.StartsWith("G HUB agent")).ToList();
        if (ghubRunning) otherWarnings.Insert(0, "G HUB is running. Exit it from its tray menu to apply Sora settings.");
        else if (snapshot.OnboardMode == 1 && controls?.Running != true) otherWarnings.Insert(0, "The mouse is using firmware control. Apply profile lets Sora take software control; saved onboard slots stay unchanged.");
        ConflictText.Text = string.Join("\n", otherWarnings); ConflictText.Visibility = otherWarnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SensitivityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready || rendering) return;
        draftDpi = (int)DpiSlider.Value; rendering = true; DpiInput.Text = draftDpi.ToString(); rendering = false;
        dpiSelectionPending = DpiScale.LiveDpi != draftDpi;
        DpiValue.Text = draftDpi.ToString("N0"); UpdateCurrentStage(); ClearInputError(); SetEnabled();
    }
    private void DpiInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || rendering) return;
        if (!int.TryParse(DpiInput.Text, out var dpi) || snapshot?.Sensors.SingleOrDefault()?.Accepts(dpi) != true) { ShowError("Enter a DPI value within the mouse's supported range and step size.", true); SetEnabled(); return; }
        draftDpi = dpi; rendering = true; DpiSlider.Value = dpi; rendering = false; DpiValue.Text = dpi.ToString("N0");
        dpiSelectionPending = DpiScale.LiveDpi != dpi; UpdateCurrentStage(); ClearInputError(); SetEnabled();
    }
    private void RateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || rendering || snapshot == null || RatePicker.SelectedIndex < 0) return;
        draftRate = snapshot.ReportRates[RatePicker.SelectedIndex]; if (controlDraft != null) controlDraft = controlDraft with { ReportRate = draftRate }; SetEnabled();
    }
    private void SectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || !ReferenceEquals(e.Source, Sections)) return;
        SetEnabled();
        if (LightingTab.IsSelected) LightingScroll.ScrollToTop();
    }

    private void PopulateZone()
    {
        var zone = SelectedZone; if (zone == null) return;
        var state = lightDrafts[zone.Index]; previousZoneIndex = ZonePicker.SelectedIndex; rendering = true;
        EffectPicker.ItemsSource = zone.Effects.Where(m => m is 0 or 1 or 3 or 10).Select(m => new EffectOption(m, LogitechLighting.EffectName(m))).Concat(new[] { new EffectOption(100, "Screen sampler"), new EffectOption(101, "Audio visualizer") }).ToArray();
        EffectPicker.DisplayMemberPath = nameof(EffectOption.Name); EffectPicker.SelectedIndex = Array.FindIndex(((EffectOption[])EffectPicker.ItemsSource), f => f.Mode == state[0]);
        HexInput.Text = "#" + Convert.ToHexString(state.AsSpan(1, 3));
        BrightnessSlider.Value = state[0] == 3 ? state[8] == 0 ? 100 : state[8] : state[0] == 10 ? state[7] == 0 ? 100 : state[7] : 100;
        var period = state[0] == 3 ? state[6] << 8 | state[7] : state[0] == 10 ? state[4] << 8 | state[5] : 5000;
        PeriodSlider.Value = Math.Clamp(period, 1000, 20000);
        PopulateReactiveFields(zone.Index);
        rendering = false; SyncRgbFields(); UpdateLightFields(); UpdatePreview(); SetEnabled();
    }
    private void ZoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || rendering) return;
        if (invalidLight) { rendering = true; ZonePicker.SelectedIndex = previousZoneIndex; rendering = false; ShowError("Fix the current hex color or discard edits before changing zones.", true); return; }
        PopulateZone();
    }
    private void EffectChanged(object sender, SelectionChangedEventArgs e) => LightEdited();
    private void LightChanged(object sender, RoutedEventArgs e) { if (ReferenceEquals(sender, HexInput)) invalidRgb = false; LightEdited(); }
    private void LightEdited()
    {
        if (!ready || rendering || SelectedZone == null) return;
        if (!TryLightState(out var state)) { invalidLight = true; ShowError("Enter a six-digit hex color such as #53D0EF.", true); SetEnabled(); return; }
        invalidLight = false; ClearInputError();
        var targets = LinkZonesCheck.IsChecked == true ? snapshot!.Lighting!.Select(z => z.Index) : new[] { SelectedZone.Index };
        foreach (var target in targets)
        {
            if (IsReactive) reactiveDrafts[target] = ReactiveFromFields(target);
            else { reactiveDrafts.Remove(target); lightDrafts[target] = state.ToArray(); }
        }
        SyncRgbFields(); UpdateLightFields(); UpdatePreview(); SetEnabled();
    }
    private bool TryLightState(out byte[] state)
    {
        state = [];
        try
        {
            if (invalidRgb) return false;
            if (EffectPicker.SelectedItem is not EffectOption effect) return false;
            if (IsReactive) { ReactiveFromFields(SelectedZone?.Index ?? 0).Validate(); state = LogitechLighting.Encode(1, HexInput.Text, 5000, (int)BrightnessSlider.Value); }
            else state = LogitechLighting.Encode(effect.Mode, effect.Mode is 0 or 3 ? "FFFFFF" : HexInput.Text, (int)PeriodSlider.Value, (int)BrightnessSlider.Value);
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidDataException) { return false; }
    }
    private void UpdateLightFields()
    {
        var mode = (EffectPicker.SelectedItem as EffectOption)?.Mode;
        HexInput.IsEnabled = mode is 1 or 10 or 101; BrightnessSlider.IsEnabled = mode != 0; PeriodSlider.IsEnabled = mode is 3 or 10;
        ColorPickerHost.IsEnabled = RedInput.IsEnabled = GreenInput.IsEnabled = BlueInput.IsEnabled = HexInput.IsEnabled;
        ScreenAreaPicker.IsEnabled = mode == 100;
        ReactiveHint.Text = mode == 100 ? "Samples the primary display at 10 frames/s. Sora must stay running." : mode == 101 ? "Reacts to the default speaker output level. No microphone recording. Sora must stay running." : "Firmware effect · no streaming needed.";
        BrightnessLabel.Text = $"Brightness · {BrightnessSlider.Value:0}%"; PeriodLabel.Text = $"Cycle duration · {PeriodSlider.Value / 1000:0.0} s";
    }
    private void SwatchClick(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is string color) HexInput.Text = color; }
    private void UpdatePreview()
    {
        foreach (var view in new[] { MousePreview, LightPreview })
        {
            view.Connected = snapshot != null; view.SelectedZone = SelectedZone?.Index ?? 1;
            view.LinkedZones = LinkZonesCheck.IsChecked == true;
            foreach (var (index, data) in lightDrafts)
            {
                var color = data[0] == 0 ? Colors.Black : data[0] == 3 ? Color.FromRgb(83, 208, 239) : Color.FromRgb(data[1], data[2], data[3]);
                if (data[0] is 3 or 10)
                {
                    var level = data[0] == 3 ? data[8] : data[7];
                    var brightness = level == 0 ? 100 : level;
                    color = Color.FromRgb((byte)(color.R * brightness / 100), (byte)(color.G * brightness / 100), (byte)(color.B * brightness / 100));
                }
                if (index == 1) view.LogoColor = color; else view.DpiColor = color;
                if (reactiveDrafts.TryGetValue(index, out var effect))
                {
                    var rgb = Convert.FromHexString(effect.Hex); var reactiveColor = Color.FromRgb((byte)(rgb[0] * effect.Brightness / 100), (byte)(rgb[1] * effect.Brightness / 100), (byte)(rgb[2] * effect.Brightness / 100));
                    if (index == 1) view.LogoColor = reactiveColor; else view.DpiColor = reactiveColor;
                }
            }
            view.InvalidateVisual();
        }
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (snapshot == null || !ApplyButton.IsEnabled) return;
        var expected = snapshot; var lights = LightingTab.IsSelected; var zone = SelectedZone;
        await RunAsync(async () =>
        {
            Directory.CreateDirectory(DataDirectory);
            var identity = string.Concat(expected.Identity.Where(char.IsLetterOrDigit));
            var backup = Path.Combine(DataDirectory, identity + "-original.json");
            if (!File.Exists(backup)) File.WriteAllText(backup, JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));
            if (!lights)
            {
                if (controls == null) throw new InvalidOperationException("The mouse control service is unavailable.");
                await controls.ApplyAsync(BuildControls()); snapshot = controls.Snapshot ?? throw new IOException("No profile readback was returned.");
                controlDraft = Clone(controls.Profile!); controlBaseline = JsonSerializer.Serialize(controlDraft); baselineDpi = draftDpi; baselineRate = draftRate; ModeText.Text = "Sora software control";
                dpiSelectionPending = false;
            }
            else
            {
                if (zone == null || reactive == null) throw new InvalidOperationException("The lighting service is unavailable.");
                var targets = LinkZonesCheck.IsChecked == true ? expected.Lighting!.Select(z => z.Index).ToArray() : new[] { zone.Index };
                foreach (var target in targets)
                {
                    if (reactiveDrafts.TryGetValue(zone.Index, out var software)) await reactive.SetAsync(software with { Zone = target });
                    else await reactive.ApplyFirmwareAsync(target, lightDrafts[zone.Index].ToArray());
                }
                snapshot = await WithDeviceAsync(device => device.ReadAsync());
                foreach (var target in targets) lightDrafts[target] = snapshot.Lighting!.Single(z => z.Index == target).State.ToArray();
                reactiveDrafts.Clear(); foreach (var software in reactive.Zones) reactiveDrafts[software.Zone] = software;
                linkZonesPending = false;
            }
            ClearInputError(); StatusChanged?.Invoke($"G502 HERO · {snapshot.Sensors.SingleOrDefault()?.Dpi:N0} DPI");
            EditStatus.Text = "Applied and verified by device readback.";
        }, lights ? "Applying lighting and checking readback…" : "Applying sensitivity and checking readback…");
    }
    private void DiscardClick(object sender, RoutedEventArgs e)
    {
        if (snapshot == null || IsBusy) return;
        rendering = true;
        draftDpi = snapshot.Sensors.SingleOrDefault()?.Dpi ?? 0; draftRate = snapshot.ReportRate ?? 0;
        DpiSlider.Value = draftDpi; DpiInput.Text = draftDpi.ToString(); DpiValue.Text = draftDpi.ToString("N0"); RatePicker.SelectedIndex = Array.IndexOf(snapshot.ReportRates, draftRate);
        lightDrafts.Clear(); foreach (var z in snapshot.Lighting ?? []) lightDrafts[z.Index] = z.State.ToArray();
        rendering = false; invalidLight = invalidRgb = false; ClearInputError(); LoadReactiveEditor(); SetEnabled();
        if (controlBaseline.Length > 0) StageControlProfile(JsonSerializer.Deserialize<LogitechControlProfile>(controlBaseline)!);
        dpiSelectionPending = false; SetEnabled();
        if (hardwareSettings != null) { DpiAlwaysOnCheck.IsChecked = hardwareSettings.DpiAlwaysOn; StartupEffectCheck.IsChecked = hardwareSettings.StartupEffect; }
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await ReadAsync();
    private async void DeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || rendering || DevicePicker.SelectedIndex == selectedDevice) return;
        if (HasPendingEdits) { rendering = true; DevicePicker.SelectedIndex = selectedDevice; rendering = false; ShowError("Apply or discard pending edits before selecting another mouse."); return; }
        selectedDevice = DevicePicker.SelectedIndex;
        if (selectedDevice >= 0) await RunAsync(LoadSelectedAsync, "Reading selected mouse…");
    }

    private void LoadPresets()
    {
        presets.Clear();
        if (snapshot == null) return;
        if (Directory.Exists(DataDirectory)) foreach (var file in Directory.EnumerateFiles(DataDirectory, "preset-*.json").Take(100))
        {
            try { presets.Add(LogitechProfiles.Load(file, snapshot)); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { ShowError("A saved sensitivity preset could not be loaded: " + Path.GetFileName(file)); }
        }
        rendering = true; PresetPicker.ItemsSource = presets.Select(p => p.Name).ToArray(); rendering = false;
    }
    private LogitechPreset CurrentPreset() => snapshot == null || !ValidSensitivity() ? throw new InvalidOperationException("Choose valid sensitivity settings first.") : new(1, PresetName.Text.Trim(), snapshot.ProductId, draftDpi, draftRate);
    private void StagePreset(LogitechPreset preset)
    {
        rendering = true; draftDpi = preset.Dpi; draftRate = preset.ReportRate; DpiSlider.Value = draftDpi; DpiInput.Text = draftDpi.ToString(); DpiValue.Text = draftDpi.ToString("N0"); RatePicker.SelectedIndex = Array.IndexOf(snapshot!.ReportRates, draftRate); PresetName.Text = preset.Name; rendering = false; UpdateCurrentStage(); ClearInputError(); SetEnabled();
    }
    private void PresetChanged(object sender, SelectionChangedEventArgs e) { if (ready && !rendering && PresetPicker.SelectedIndex >= 0) StagePreset(presets[PresetPicker.SelectedIndex]); }
    private void SavePresetClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        var profile = CurrentPreset(); LogitechProfiles.Validate(profile, snapshot!);
        var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile.Name)))[..16];
        LogitechProfiles.Save(Path.Combine(DataDirectory, "preset-" + id + ".json"), profile); LoadPresets(); EditStatus.Text = "Preset saved. Apply to send it to your mouse.";
    });
    private void ImportClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        if (snapshot == null) throw new InvalidOperationException("Connect your mouse first.");
        var dialog = new OpenFileDialog { Title = "Import Logitech sensitivity preset", Filter = "Sora sensitivity preset (*.json)|*.json" };
        if (dialog.ShowDialog() == true) StagePreset(LogitechProfiles.Load(dialog.FileName, snapshot));
    });
    private void OriginalClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        if (snapshot == null || OriginalPath is not { } path || !File.Exists(path)) throw new InvalidOperationException("No original settings have been saved for this mouse.");
        if (new FileInfo(path).Length > 131072) throw new InvalidDataException("Original-settings backup is too large.");
        var original = JsonSerializer.Deserialize<LogitechSnapshot>(File.ReadAllText(path));
        if (original == null || original.Identity != snapshot.Identity || original.ProductId != snapshot.ProductId || original.Sensors?.Length != 1 || !original.ReportRate.HasValue) throw new InvalidDataException("The original backup does not match this mouse.");
        var preset = new LogitechPreset(1, "Original sensitivity", original.ProductId, original.Sensors[0].Dpi, original.ReportRate.Value);
        LogitechProfiles.Validate(preset, snapshot);
        foreach (var zone in original.Lighting ?? [])
        {
            var current = snapshot.Lighting?.SingleOrDefault(z => z.Index == zone.Index);
            if (current == null || zone.State == null || zone.State.Length != 11 || !current.Effects.Contains(zone.State[0])) throw new InvalidDataException("The saved lighting does not match this mouse.");
        }
        StagePreset(preset); invalidLight = false;
        foreach (var zone in original.Lighting ?? []) lightDrafts[zone.Index] = zone.State.ToArray();
        PopulateZone(); SetEnabled(); EditStatus.Text = "Original settings staged. Apply each section to restore it.";
    });
    private void ExportClick(object sender, RoutedEventArgs e) => FileAction(() =>
    {
        var preset = CurrentPreset(); LogitechProfiles.Validate(preset, snapshot!);
        var dialog = new SaveFileDialog { Title = "Export Logitech sensitivity preset", Filter = "Sora sensitivity preset (*.json)|*.json", FileName = "sora-g502-sensitivity.json" };
        if (dialog.ShowDialog() == true) LogitechProfiles.Save(dialog.FileName, preset);
    });
    private void FileAction(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException) { ShowError(e.Message); }
    }

    private async void ReadMemoryClick(object sender, RoutedEventArgs e)
    {
        if (snapshot == null) return;
        await RunAsync(async () =>
        {
            var slots = await WithDeviceAsync(device => device.ReadOnboardAsync(snapshot)); MemorySlots.Children.Clear();
            foreach (var slot in slots)
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = $"SLOT {slot.Slot} · {slot.Name}" + (slot.Enabled ? "" : " · Disabled"), Style = (Style)FindResource("SectionText") });
                content.Children.Add(new TextBlock { Text = slot.ChecksumValid ? string.Join(" / ", slot.Dpi.Where(d => d > 0)) + $" DPI · {slot.ReportRate} Hz · checksum verified" : "Checksum invalid. These settings have not been interpreted.", Style = (Style)FindResource("CaptionText"), Margin = new Thickness(0, 8, 0, 12) });
                if (slot.ChecksumValid)
                {
                    var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
                    for (var row = 0; row <= slot.Bindings.Length; row++)
                    {
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        var labels = row == 0 ? new[] { "Button", "Default layer", "G-Shift layer" } : new[] { $"G{row}", slot.Bindings[row - 1], slot.ShiftBindings[row - 1] };
                        for (var col = 0; col < 3; col++)
                        {
                            var label = new TextBlock { Text = labels[col], Style = (Style)FindResource("CaptionText"), Margin = new Thickness(0, 4, 10, 4) };
                            Grid.SetRow(label, row); Grid.SetColumn(label, col); grid.Children.Add(label);
                        }
                    }
                    content.Children.Add(grid);
                }
                MemorySlots.Children.Add(new Border { Child = content, Style = (Style)FindResource("Card"), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 12) });
            }
            MemoryStatus.Text = $"Read {slots.Length} slots. Onboard contents are separate from the current software profile; no mouse memory was written.";
        }, "Reading onboard profiles…");
    }
    private void ReportClick(object sender, RoutedEventArgs e) => FileAction(() => Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Logitech-GHUB.md")) { UseShellExecute = true }));
    internal void ShowSection(string section)
    {
        Sections.SelectedItem = section switch { "lighting" or "lighting-picker" or "lighting-image" or "lighting-rgb" => LightingTab, "memory" => MemoryTab, "details" => DetailsTab, "assignments" or "assignments-labels" or "side" => AssignmentsTab, "macros" => MacrosTab, _ => SensitivityTab };
        if (section == "side") SideViewClick(this, new RoutedEventArgs());
        if (section == "assignments") FrontViewClick(this, new RoutedEventArgs());
        if (section == "memory") ReadMemoryClick(this, new RoutedEventArgs());
        UpdateLayout();
        if (section == "lighting") { LightingScroll.ScrollToTop(); UpdateLayout(); }
        if (section == "lighting-picker") { ColorPickerHost.BringIntoView(); UpdateLayout(); }
        if (section == "lighting-rgb") { RevealRgbFields(); UpdateLayout(); }
        if (section == "lighting-image") { LightPreview.BringIntoView(); UpdateLayout(); }
        if (section == "assignments-labels") { AssignmentLegend.BringIntoView(); UpdateLayout(); }
    }
    internal object RunEditorChecks()
    {
        if (snapshot?.Sensors.Length != 1 || snapshot.Lighting?.Length != 2) throw new InvalidOperationException("Connect the G502 HERO before running editor checks.");
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool value) { checks[name] = value; if (!value) throw new InvalidOperationException("Logitech UI check failed: " + name); }
        var originalDpi = snapshot.Sensors[0].Dpi;
        DpiInput.Text = "801";
        Check("invalidDpiBlocksApply", !ValidSensitivity() && !ApplyButton.IsEnabled && HasPendingEdits);
        Sections.SelectedItem = LightingTab;
        Check("invalidDpiSurvivesSectionChange", DpiInput.Text == "801" && HasPendingEdits);
        HexInput.Text = "#invalid";
        Check("sharedHexValidation", ControlState.GetIsInvalid(HexInput) && HexError.Text == ControlState.HexColourError);
        var selected = ZonePicker.SelectedIndex; ZonePicker.SelectedIndex = 1 - selected;
        Check("invalidHexBlocksZoneChange", ZonePicker.SelectedIndex == selected && HexInput.Text == "#invalid" && !ApplyButton.IsEnabled);
        DiscardClick(this, new RoutedEventArgs());
        Check("discardRestoresAllSections", !HasPendingEdits && draftDpi == baselineDpi);
        Sections.SelectedItem = SensitivityTab;
        var savedLiveDpi = DpiScale.LiveDpi;
        var savedSnapshot = snapshot;
        // Simulate the read model as well as the view: UpdateDpiScale correctly
        // restores the view from the model when the selected stage changes.
        var differentLiveDpi = draftDpi == 1600 ? 800 : 1600;
        snapshot = snapshot with { Sensors = snapshot.Sensors.Select(s => s with { Dpi = differentLiveDpi }).ToArray() };
        DpiScale.LiveDpi = differentLiveDpi;
        StageChanged(this, new SelectionChangedEventArgs(ComboBox.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        Check("selectingSavedSpeedCanApplyOverDifferentLiveDpi", dpiSelectionPending && HasPendingEdits && (ghubRunning || ApplyButton.IsEnabled));
        snapshot = savedSnapshot;
        DiscardClick(this, new RoutedEventArgs()); DpiScale.LiveDpi = savedLiveDpi; DpiScale.InvalidateVisual();
        DpiSlider.Value = originalDpi == 1600 ? 800 : 1600;
        Check("validSensitivityIsStaged", HasPendingEdits && snapshot.Sensors[0].Dpi == originalDpi);
        Sections.SelectedItem = LightingTab;
        var zone = SelectedZone!; var oldState = zone.State.ToArray();
        EffectPicker.SelectedIndex = Array.FindIndex((EffectOption[])EffectPicker.ItemsSource, f => f.Mode == 1); HexInput.Text = "#FF5E81";
        var staged = lightDrafts[zone.Index].ToArray(); ZonePicker.SelectedIndex = 1 - ZonePicker.SelectedIndex; ZonePicker.SelectedIndex = selected;
        Check("lightingDraftSurvivesZoneChange", lightDrafts[zone.Index].SequenceEqual(staged) && zone.State.SequenceEqual(oldState));
        if (ghubRunning) Check("ghubConflictBlocksWrites", !ApplyButton.IsEnabled);
        DiscardClick(this, new RoutedEventArgs()); Sections.SelectedItem = SensitivityTab;
        ShiftDpiInput.Text = "801";
        Check("invalidShiftBlocksApply", !ValidSensitivity() && HasPendingEdits && !ApplyButton.IsEnabled);
        DiscardClick(this, new RoutedEventArgs());
        StagesInput.Text = "400, broken";
        Check("invalidStagesBlocksApply", !ValidSensitivity() && HasPendingEdits && !ApplyButton.IsEnabled);
        DiscardClick(this, new RoutedEventArgs());
        Sections.SelectedItem = AssignmentsTab; ButtonPicker.SelectedIndex = 5; GShiftLayerCheck.IsChecked = true;
        Assign(new LogitechAction("Key", "Test shortcut", [17, 67]));
        Check("alternateAssignmentStaged", controlDraft!.ShiftButtons[5].Kind == "Key" && ControlsDirty);
        Check("primaryClickPreserved", controlDraft.Buttons[0].Kind == "Native");
        SideViewClick(this, new RoutedEventArgs()); Check("sideArtworkLoaded", AssignmentPreview.HasGHubArtwork && AssignmentPreview.SideView);
        DiscardClick(this, new RoutedEventArgs()); GShiftLayerCheck.IsChecked = false;
        Sections.SelectedItem = LightingTab;
        EffectPicker.SelectedIndex = Array.FindIndex((EffectOption[])EffectPicker.ItemsSource, f => f.Mode == 1);
        RedInput.Text = "999";
        Check("invalidRgbBlocksApply", invalidLight && !ApplyButton.IsEnabled);
        Check("sharedRgbValidation", ControlState.GetIsInvalid(RedInput) && !string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetHelpText(RedInput)));
        DiscardClick(this, new RoutedEventArgs());
        Sections.SelectedItem = SensitivityTab;
        if (!ghubRunning)
        {
            var mode = snapshot.OnboardMode; snapshot = snapshot with { OnboardMode = 1 };
            ShiftDpiInput.Text = controlDraft!.ShiftDpi == 4000 ? "800" : "4000";
            Check("firmwareModeAllowsSoftwareApply", ApplyButton.IsEnabled);
            snapshot = snapshot with { OnboardMode = mode }; DiscardClick(this, new RoutedEventArgs());
        }
        Check("finalStateClean", !HasPendingEdits);
        Check("ghubImageLoaded", MousePreview.HasGHubArtwork && LightPreview.HasGHubArtwork);
        if (SoraData.IsIsolated)
        {
            SaveColorClick(this, new RoutedEventArgs());
            var swatch = CustomPalette.Children.OfType<Button>().FirstOrDefault();
            Check("savedPaletteUsesSharedSwatch", swatch != null && ReferenceEquals(swatch.Style, FindResource("Swatch")));
            Check("savedPaletteHasAccessibleColour", swatch != null && !string.IsNullOrWhiteSpace(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(swatch)?.GetName()));
        }
        Sections.SelectedItem = MacrosTab;
        Check("emptyMacroChoiceExplainsAvailability", MacroPicker.HasItems || !MacroPicker.IsEnabled && ControlState.GetPlaceholder(MacroPicker) == "No saved macros");
        Check("multilineInputStartsAtTop", MacroSteps.VerticalContentAlignment == VerticalAlignment.Top);
        var macroCount = controlDraft!.Macros.Length;
        NewMacroClick(this, new RoutedEventArgs()); MacroName.Text = "Phase 4 verification";
        SaveMacroClick(this, new RoutedEventArgs());
        Check("macroCanBeStagedAndSelected", controlDraft.Macros.Length == macroCount + 1 && MacroPicker.IsEnabled && MacroPicker.SelectedItem is LogitechMacro);
        DeleteMacroClick(this, new RoutedEventArgs());
        Check("macroDeletionPreservesOtherMacros", controlDraft.Macros.Length == macroCount);
        DiscardClick(this, new RoutedEventArgs()); Sections.SelectedItem = SensitivityTab;
        Check("finalMacroDraftDiscarded", !HasPendingEdits);
        var connectedSnapshot = snapshot;
        snapshot = null; SetEnabled();
        Check("disconnectedEditorsDisabled", !AssignmentEditor.IsEnabled && !MacroEditor.IsEnabled && !ApplyDeviceSettingsButton.IsEnabled && !DpiAlwaysOnCheck.IsEnabled);
        snapshot = connectedSnapshot; SetEnabled();
        Check("retainedSnapshotEditorsAvailable", AssignmentEditor.IsEnabled && MacroEditor.IsEnabled);
        Sections.SelectedItem = AssignmentsTab; UpdateLayout();
        Check("allAssignmentNamesUnabridged", AssignmentLegend.Items.Count == 11 && AssignmentLegend.Items.Cast<string>().Any(s => s.Contains("Next Sora control profile")));
        AssignmentLegend.SelectedIndex = 8;
        Check("legendSelectsEditableAssignment", ButtonPicker.SelectedIndex == 8 && AssignmentPreview.SelectedButton == 8);
        ButtonPicker.SelectedIndex = 5;
        Check("selectorUpdatesLegend", AssignmentLegend.SelectedIndex == 5);
        Sections.SelectedItem = LightingTab; UpdateLayout();
        ColorPickerHost.BringIntoView(); UpdateLayout();
        var pickerBounds = ColorPickerHost.TransformToAncestor(LightingScroll).TransformBounds(new Rect(ColorPickerHost.RenderSize));
        Check("completeColourPickerReachable", pickerBounds.Top >= -1 && pickerBounds.Bottom <= LightingScroll.ViewportHeight + 1);
        LightingScroll.ScrollToTop(); Sections.SelectedItem = SensitivityTab;
        return new { checks, hardwareSettingsWrites = 0, snapshot.Name, snapshot.Protocol };
    }
    private sealed record EffectOption(int Mode, string Name);
}
