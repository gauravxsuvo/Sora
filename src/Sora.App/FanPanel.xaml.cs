using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sora.Core;

namespace Sora.App;

public partial class FanPanel : UserControl
{
    private FanService? service;
    private bool editing, listening, editorLoaded;
    private int selected;
    private readonly ObservableCollection<FanPointRow> rows = new();
    private readonly Dictionary<int, FanProfile> drafts = new();
    private readonly Dictionary<int, string> nameDrafts = new();
    private readonly Dictionary<int, FanProfile> baselines = new();
    private readonly Dictionary<int, FanProfile> temperatureProfiles = new();
    private readonly HashSet<int> appliedHeaders = new();
    private bool applying;
    private bool invalidEditor;
    private bool refreshing;
    private string? measurementMessage;
    private FanStopConfig? currentFirmware;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    public bool HasPendingEdits => drafts.Count > 0 || nameDrafts.Count > 0 || invalidEditor;
    public bool IsBusy => service?.Busy == true;
    public event Action<string>? StatusChanged;

    public FanPanel()
    {
        InitializeComponent();
        SourceBox.ItemsSource = FanProtocol.TemperatureNames;
        HeaderList.ItemsSource = FanProtocol.HeaderNames.Select(n => new FanHeaderRow(n)).ToArray();
        HeaderList.SelectedIndex = 0;
        PointGrid.ItemsSource = rows;
        rows.CollectionChanged += (_, _) => MarkChanged();
        Chart.PointMoved += (index, point) =>
        {
            editing = true;
            rows[index].Temperature = point.TemperatureC.ToString(CultureInfo.InvariantCulture);
            rows[index].Duty = point.DutyPercent.ToString("0.##", CultureInfo.InvariantCulture);
            editing = false; MarkChanged();
        };
        Loaded += (_, _) => { Listen(); timer.Start(); };
        Unloaded += (_, _) => { timer.Stop(); if (listening && service != null) service.Changed -= Render; listening = false; };
        timer.Tick += async (_, _) => { if (IsVisible) await ReadAsync(refreshEditor: false); };
    }
    internal void Attach(FanService fans)
    {
        if (listening && service != null) service.Changed -= Render;
        listening = false; service = fans;
        if (IsLoaded) Listen(); Render();
    }
    private void Listen() { if (service != null && !listening) { service.Changed += Render; listening = true; } }
    public async Task ReadAsync(bool refreshEditor = true)
    {
        if (service == null || service.Busy || refreshEditor && refreshing) return;
        if (refreshEditor) { refreshing = true; Render(); }
        try
        {
            if (await service.ReadAsync() && refreshEditor && !drafts.ContainsKey(selected) && !invalidEditor) { LoadCurrentProfile(); Render(); }
        }
        finally { if (refreshEditor) { refreshing = false; Render(); } }
    }
    private FanProfile CurrentProfile() => drafts.TryGetValue(selected, out var draft) ? draft : HardwareProfile();
    private FanProfile HardwareProfile()
    {
        if (service?.Snapshot?.Headers[selected] is not { } state)
            return FanProfile.Preset(selected, "Balanced") with { DeviceKind = service?.DeviceKind(selected) ?? FanDeviceKind.Unknown };
        var profile = state.Profile;
        // Keep the user's mode/points when their compiled values match readback. Fixed duty
        // and a two-point step can be indistinguishable in hardware, and PWM has finite precision.
        if (service.Preferences.Profiles.TryGetValue(selected, out var saved))
        {
            var registers = state.Registers.ToDictionary(v => v.Register, v => v.Value);
            if (FanProtocol.Compile(saved).All(w => registers.TryGetValue(w.Register, out var value) && (value & w.Mask) == (w.Value & w.Mask)) &&
                (saved.FirmwareStop == null || saved.FirmwareStop == state.FirmwareStop)) profile = saved;
        }
        return profile with { FirmwareStop = state.FirmwareStop, DeviceKind = service.DeviceKind(selected) };
    }
    private void LoadCurrentProfile()
    {
        if (!drafts.ContainsKey(selected) || !baselines.ContainsKey(selected)) baselines[selected] = HardwareProfile();
        LoadEditor(CurrentProfile());
    }
    private void Render()
    {
        if (service == null) return;
        var snapshot = service.Snapshot;
        if (!editorLoaded && snapshot != null) LoadCurrentProfile();
        if (HeaderList.ItemsSource is FanHeaderRow[] headers)
            for (int i = 0; i < headers.Length; i++)
            {
                headers[i].Name = service.DisplayName(i);
                headers[i].Speed = snapshot?.Headers[i].Rpm is { } rpm ? $"{rpm:0} RPM" : "No reading";
            }
        BoardLabel.Text = snapshot == null ? GigabyteFans.SupportedBoard : snapshot.Board + "\n" + snapshot.Controller;
        Temperatures.Text = snapshot == null ? "No live readings" : string.Join("\n", FanProtocol.TemperatureNames.Select((name, i) => name + "   " + (snapshot.TemperaturesC[i] is { } t ? $"{t:0}°C" : "—")));
        HeaderTitle.Text = service.DisplayName(selected);
        HeaderSpeed.Text = snapshot?.Headers[selected].Rpm is { } speed ? $"{speed:0} RPM" : "— RPM";
        bool fixedPump = DeviceKindBox.SelectedIndex == (int)FanDeviceKind.FixedSpeedPump;
        HeaderDetail.Text = FanProtocol.HeaderNames[selected] + " · " + (fixedPump ? "Fixed-speed pump · RPM monitoring" : snapshot == null ? "Read the controller before applying settings." : (snapshot.Headers[selected].Automatic ? "Hardware automatic control" : "Manual control") + " · PWM/DC wiring mode preserved");
        StatusLabel.Text = snapshot == null ? service.Message : measurementMessage ?? service.Message;
        RenderPending();
        RefreshButton.IsEnabled = !service.Busy && !refreshing; ControlState.SetIsBusy(RefreshButton, service.Busy || refreshing);
        bool blocked = service.ConflictingProcess != null;
        bool canPause = service.ConflictingProcess == "EasyTuneEngineService" && service.VendorServiceInstalled;
        var bannerVisibility = blocked && !fixedPump ? Visibility.Visible : Visibility.Collapsed;
        bool headerViewportChanged = ConflictBanner.Visibility != bannerVisibility;
        ConflictBanner.Visibility = bannerVisibility;
        PauseServiceButton.IsEnabled = !service.Busy && canPause;
        InstallFirmwareButton.IsEnabled = !service.Busy;
        InstallFirmwareButton.Visibility = InstallFirmwareHint.Visibility = snapshot?.FirmwareInterface == GigabyteFanFirmware.WmiClassName ? Visibility.Collapsed : Visibility.Visible;
        PauseServiceButton.Visibility = canPause ? Visibility.Visible : Visibility.Collapsed;
        PauseServiceHint.Text = canPause ? "GCC's fan service is running. Pause it here, then click Apply to fan. It resumes when Sora exits." : "Exit " + service.ConflictingProcess + " to let Sora apply fan settings.";
        MeasureButton.IsEnabled = !service.Busy && !blocked && !fixedPump && snapshot?.Headers[selected].Rpm > 0;
        CalibrateButton.IsEnabled = !service.Busy && !blocked && snapshot?.Headers[selected].Rpm > 0 && DeviceKindBox.SelectedIndex == (int)FanDeviceKind.Fan;
        CancelMeasureButton.Visibility = service.Measuring ? Visibility.Visible : Visibility.Collapsed;
        HeaderList.IsHitTestVisible = StartupCheck.IsHitTestVisible = RestoreCheck.IsHitTestVisible = !service.Busy;
        HeaderNameBox.IsReadOnly = service.Busy;
        RenameHeaderButton.IsEnabled = ResetHeaderNameButton.IsEnabled = !service.Busy;
        ProfileEditor.IsHitTestVisible = !service.Busy;
        ImportButton.IsEnabled = ExportButton.IsEnabled = !service.Busy && !fixedPump;
        RestoreButton.IsEnabled = !service.Busy && !blocked && !fixedPump && snapshot != null;
        RenderApplyState();
        editing = true;
        StartupCheck.IsChecked = service.Preferences.ApplyAtStartup; RestoreCheck.IsChecked = service.Preferences.RestoreOnExit;
        editing = false;
        if (snapshot != null)
        {
            int source = SourceBox.SelectedIndex;
            Chart.Temperature = source is >= 0 and < 6 ? snapshot.TemperaturesC[source] : null;
        }
        StatusChanged?.Invoke(snapshot == null ? "Not connected" : $"{snapshot.Headers.Count(h => h.Rpm > 0)} active headers");
        if (headerViewportChanged) EnsureSelectedHeaderVisible();
    }
    private void LoadEditor(FanProfile p)
    {
        editing = true;
        editorLoaded = true;
        HeaderNameBox.Text = nameDrafts.TryGetValue(selected, out var name) ? name : service?.DisplayName(selected) ?? FanProtocol.HeaderNames[selected];
        ModeBox.SelectedIndex = (int)p.Mode; SourceBox.SelectedIndex = p.TemperatureSource;
        DeviceKindBox.SelectedIndex = (int)p.DeviceKind;
        FixedSlider.Value = p.FixedDutyPercent;
        HysteresisBox.Text = p.HysteresisC.ToString(CultureInfo.InvariantCulture);
        StopBox.Text = p.StopBelowC.ToString(CultureInfo.InvariantCulture);
        FullBox.Text = p.FullSpeedC.ToString(CultureInfo.InvariantCulture);
        currentFirmware = p.FirmwareStop;
        FirmwareStopCheck.IsChecked = currentFirmware?.Enabled == true;
        int off = currentFirmware?.OffC is > 0 ? currentFirmware.OffC : 25;
        FirmwareOffBox.Text = off.ToString(CultureInfo.InvariantCulture);
        FirmwareOnBox.Text = (currentFirmware?.OnC > off ? currentFirmware.OnC : off + 5).ToString(CultureInfo.InvariantCulture);
        rows.Clear();
        foreach (var point in p.Points)
        {
            var row = new FanPointRow { Temperature = point.TemperatureC.ToString(CultureInfo.InvariantCulture), Duty = point.DutyPercent.ToString("0.##", CultureInfo.InvariantCulture) };
            row.PropertyChanged += (_, _) => MarkChanged(); rows.Add(row);
        }
        Chart.Profile = p;
        if (p.Mode != FanCurveMode.Fixed) temperatureProfiles[selected] = p;
        editing = false; UpdateMode();
        invalidEditor = false;
    }
    private FanProfile ReadEditor()
    {
        int Integer(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        double Number(string s)
        {
            double value = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            return double.IsFinite(value) ? value : throw new FormatException("Enter a finite fan duty percentage.");
        }
        return new() { Header = selected, DeviceKind = (FanDeviceKind)DeviceKindBox.SelectedIndex, Mode = (FanCurveMode)ModeBox.SelectedIndex, TemperatureSource = SourceBox.SelectedIndex,
            HysteresisC = Integer(HysteresisBox.Text), StopBelowC = ModeBox.SelectedIndex == (int)FanCurveMode.Fixed ? 0 : Integer(StopBox.Text), FullSpeedC = Integer(FullBox.Text), FixedDutyPercent = FixedSlider.Value,
            FirmwareStop = currentFirmware == null ? null : FirmwareStopCheck.IsChecked == true && ModeBox.SelectedIndex != (int)FanCurveMode.Fixed
                ? new FanStopConfig(true, SourceBox.SelectedIndex, Integer(FirmwareOffBox.Text), Integer(FirmwareOnBox.Text)) : currentFirmware with { Enabled = false },
            Points = rows.Select(r => new FanPoint(Integer(r.Temperature), Number(r.Duty))).ToArray() };
    }
    private void UpdateMode()
    {
        if (ModeBox == null || FixedEditor == null) return;
        bool fixedDuty = ModeBox.SelectedIndex == (int)FanCurveMode.Fixed;
        FixedEditor.Visibility = fixedDuty ? Visibility.Visible : Visibility.Collapsed;
        CurveEditor.Visibility = fixedDuty ? Visibility.Collapsed : Visibility.Visible;
        FullBox.IsEnabled = fixedDuty; StopBox.IsEnabled = !fixedDuty;
        FixedLabel.Text = $"Fixed duty · {FixedSlider.Value:0}%";
        ModeHint.Text = ModeBox.SelectedIndex switch { (int)FanCurveMode.Step => "Stair step holds each duty until the next temperature point.", (int)FanCurveMode.Fixed => "Fixed duty stays constant until the full-speed temperature override.", _ => "Temperature curve ramps smoothly between the points." };
        FirmwareStopCheck.IsEnabled = currentFirmware != null && !fixedDuty;
        FirmwareStopEditor.IsEnabled = FirmwareStopCheck.IsEnabled && FirmwareStopCheck.IsChecked == true;
        FirmwareHint.Text = currentFirmware == null ? "Firmware Zero-RPM is unavailable or has not been read." : "BIOS Zero-RPM is supported. Uses the selected temperature source; applies with the fan profile.";
        CalibrateButton.IsEnabled = service?.Busy == false && service.ConflictingProcess == null && service.Snapshot?.Headers[selected].Rpm > 0 && DeviceKindBox.SelectedIndex == (int)FanDeviceKind.Fan;
        bool fixedPump = DeviceKindBox.SelectedIndex == (int)FanDeviceKind.FixedSpeedPump;
        FanSettingsPanel.Visibility = fixedPump ? Visibility.Collapsed : Visibility.Visible;
        MeasurementControls.Visibility = fixedPump ? Visibility.Collapsed : Visibility.Visible;
        PumpNotice.Visibility = fixedPump ? Visibility.Visible : Visibility.Collapsed;
        PumpNotice.Text = "This pump runs at its own maximum speed. Sora shows its live RPM; fan curves, speed sweeps and stop/start calibration are disabled for this connection. Change the device type if you replace or reconnect it.";
    }
    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (editing || service == null) return;
        var mode = (FanCurveMode)ModeBox.SelectedIndex;
        if (mode != FanCurveMode.Fixed && Chart.Profile?.Mode == FanCurveMode.Fixed)
        {
            try
            {
                var current = ReadEditor();
                var curve = temperatureProfiles.TryGetValue(selected, out var remembered) ? remembered :
                    current.Points.Length > 2 ? current : FanProfile.Preset(selected, "Balanced");
                LoadEditor(current with { Mode = mode, Points = curve.Points, StopBelowC = curve.StopBelowC });
            }
            catch (Exception error) when (error is FormatException or OverflowException) { ShowFeedback(error.Message); }
        }
        MarkChanged();
    }
    private void DeviceKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (editing || service == null) return;
        var kind = (FanDeviceKind)DeviceKindBox.SelectedIndex;
        try
        {
            if (!service.IsPreview) service.SetDeviceKind(selected, kind);
            if (drafts.TryGetValue(selected, out var draft)) drafts[selected] = draft with { DeviceKind = kind };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { measurementMessage = error.Message; }
        UpdateMode(); Render();
    }
    private void MarkChanged()
    {
        if (editing || service == null) return;
        UpdateMode();
        try
        {
            var p = ReadEditor(); invalidEditor = false;
            if (baselines.TryGetValue(selected, out var baseline) && SameSettings(p, baseline)) drafts.Remove(selected);
            else drafts[selected] = p;
            if (p.Points.All(point => point.TemperatureC is >= 0 and <= 100 && point.DutyPercent is >= 0 and <= 100)) Chart.Profile = p;
            if (p.Mode != FanCurveMode.Fixed) temperatureProfiles[selected] = p;
        }
        catch (Exception e) when (e is FormatException or OverflowException) { invalidEditor = true; }
        RenderPending();
    }
    private void RenderPending()
    {
        int count = drafts.Keys.Union(nameDrafts.Keys).Count();
        PendingLabel.Text = invalidEditor ? "Finish entering numeric values before applying." : count == 0 ? "No pending fan edits" : $"Unsaved edits · {count} header(s)";
        RenderApplyState();
    }
    private static bool SameSettings(FanProfile a, FanProfile b)
    {
        bool Duty(double x, double y) => Math.Round(x, 2) == Math.Round(y, 2);
        if (a.Mode != b.Mode || a.TemperatureSource != b.TemperatureSource || a.HysteresisC != b.HysteresisC || a.FirmwareStop != b.FirmwareStop) return false;
        if (a.Mode == FanCurveMode.Fixed) return a.FullSpeedC == b.FullSpeedC && Duty(a.FixedDutyPercent, b.FixedDutyPercent);
        return a.StopBelowC == b.StopBelowC && a.Points.Length == b.Points.Length && a.Points.Zip(b.Points).All(p => p.First.TemperatureC == p.Second.TemperatureC && Duty(p.First.DutyPercent, p.Second.DutyPercent));
    }
    private void RenderApplyState()
    {
        if (service == null || ApplyButton == null) return;
        bool dirty = drafts.ContainsKey(selected) || invalidEditor;
        ApplyButton.Content = applying ? "Applying…" : dirty ? "Apply to fan" : appliedHeaders.Contains(selected) ? "Applied" : "No changes";
        ApplyButton.IsEnabled = !service.Busy && !applying && dirty && !invalidEditor && service.Snapshot != null && service.ConflictingProcess == null && DeviceKindBox.SelectedIndex != (int)FanDeviceKind.FixedSpeedPump;
    }
    private bool EditingIsLocked => service?.Busy == true && (ProfileEditor.IsKeyboardFocusWithin || HeaderList.IsKeyboardFocusWithin || StartupCheck.IsKeyboardFocusWithin || RestoreCheck.IsKeyboardFocusWithin);
    private void GuardEditingKey(object sender, KeyEventArgs e) { if (EditingIsLocked) e.Handled = true; }
    private void GuardEditingText(object sender, TextCompositionEventArgs e) { if (EditingIsLocked) e.Handled = true; }
    private void HeaderNameChanged(object sender, TextChangedEventArgs e)
    {
        if (editing || service == null) return;
        if (HeaderNameBox.Text == service.DisplayName(selected)) nameDrafts.Remove(selected);
        else nameDrafts[selected] = HeaderNameBox.Text;
        RenderPending();
    }
    private void EditorChanged(object sender, RoutedEventArgs e) => MarkChanged();
    private void PointEditEnding(object sender, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(new Action(MarkChanged));
    private void PointKeyUp(object sender, KeyEventArgs e) => MarkChanged();
    private void HeaderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HeaderList.SelectedIndex < 0 || service == null) return;
        if (HeaderList.SelectedIndex == selected) return;
        if (invalidEditor) { HeaderList.SelectedIndex = selected; StatusLabel.Text = "Finish entering numeric values before switching headers."; return; }
        selected = HeaderList.SelectedIndex; measurementMessage = null; LoadCurrentProfile(); Render(); EnsureSelectedHeaderVisible();
    }
    private void EnsureSelectedHeaderVisible()
    {
        if (HeaderList.SelectedItem == null || !IsLoaded) return;
        UpdateLayout(); HeaderList.ScrollIntoView(HeaderList.SelectedItem); HeaderList.UpdateLayout();
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await ReadAsync();
    private void RenameHeaderClick(object sender, RoutedEventArgs e) => SaveHeaderName(HeaderNameBox.Text);
    private void ResetHeaderNameClick(object sender, RoutedEventArgs e) => SaveHeaderName("");
    private void SaveHeaderName(string name)
    {
        if (service == null || service.Busy) return;
        measurementMessage = null;
        try
        {
            service.SetDisplayName(selected, name);
            nameDrafts.Remove(selected);
            HeaderNameBox.Text = service.DisplayName(selected);
            Render();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { ShowFeedback(error.Message); }
    }
    private async void PauseServiceClick(object sender, RoutedEventArgs e)
    {
        measurementMessage = null;
        if (service != null) await service.PauseVendorServiceAsync();
    }
    private async void InstallFirmwareClick(object sender, RoutedEventArgs e)
    {
        measurementMessage = null;
        if (service != null) await service.InstallStandaloneInterfaceAsync();
    }
    private async void MeasureClick(object sender, RoutedEventArgs e)
    {
        if (service == null) return;
        measurementMessage = null;
        await service.MeasureResponseAsync(selected);
        measurementMessage = service.Message; Render();
    }
    private async void CalibrateClick(object sender, RoutedEventArgs e)
    {
        if (service == null) return;
        measurementMessage = null;
        await service.CalibrateMinimumAsync(selected, (FanDeviceKind)DeviceKindBox.SelectedIndex);
        measurementMessage = service.Message; Render();
    }
    private void CancelMeasureClick(object sender, RoutedEventArgs e) => service?.CancelResponseMeasurement();
    private void PresetClick(object sender, RoutedEventArgs e)
    {
        if (service?.Busy == true) return;
        var p = FanProfile.Preset(selected, ((Button)sender).Content.ToString()!) with { DeviceKind = (FanDeviceKind)DeviceKindBox.SelectedIndex, TemperatureSource = Math.Max(0, SourceBox.SelectedIndex), FirmwareStop = currentFirmware == null ? null : currentFirmware with { Enabled = false } };
        if (p.Mode == FanCurveMode.Fixed && temperatureProfiles.TryGetValue(selected, out var curve)) p = p with { Points = curve.Points };
        LoadEditor(p); MarkChanged();
    }
    private void AddPointClick(object sender, RoutedEventArgs e)
    {
        if (rows.Count >= 11 || rows.Count < 2) return;
        try
        {
            var p = ReadEditor();
            int at = 1;
            for (int i = 1; i < p.Points.Length; i++) if (p.Points[i].TemperatureC - p.Points[i - 1].TemperatureC > p.Points[at].TemperatureC - p.Points[at - 1].TemperatureC) at = i;
            var a = p.Points[at - 1]; var b = p.Points[at]; if (b.TemperatureC - a.TemperatureC < 2) return;
            var list = p.Points.ToList(); list.Insert(at, new((a.TemperatureC + b.TemperatureC) / 2, Math.Round((a.DutyPercent + b.DutyPercent) / 2)));
            LoadEditor(p with { Points = list.ToArray() }); MarkChanged();
        }
        catch (Exception error) when (error is FormatException or OverflowException) { ShowFeedback(error.Message); }
    }
    private void RemovePointClick(object sender, RoutedEventArgs e) { if (rows.Count > 2 && PointGrid.SelectedItem is FanPointRow row) rows.Remove(row); }
    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (service == null) return;
        measurementMessage = null;
        try
        {
            PointGrid.CommitEdit(DataGridEditingUnit.Cell, true); PointGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var p = ReadEditor(); p.Validate();
            int h = selected;
            applying = true; RenderApplyState();
            if (await service.ApplyAsync(p)) CompleteAppliedProfile(h, p);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or InvalidOperationException) { ShowFeedback(error.Message); }
        finally { applying = false; Render(); }
    }
    private void CompleteAppliedProfile(int header, FanProfile profile)
    {
        drafts.Remove(header); appliedHeaders.Add(header); baselines[header] = profile;
        // Readback is checked by the service. Keep the successful editor in place rather
        // than clearing/recreating every row and replacing it with rounded decoded values.
        if (selected == header) { invalidEditor = false; Chart.Profile = profile; }
    }
    private async void RestoreClick(object sender, RoutedEventArgs e)
    {
        measurementMessage = null;
        int h = selected;
        if (service != null && await service.RestoreAsync(h)) { drafts.Remove(h); appliedHeaders.Remove(h); if (selected == h) LoadCurrentProfile(); Render(); }
    }
    private void OptionsChanged(object sender, RoutedEventArgs e)
    {
        if (editing || service == null) return;
        try { service.SetOptions(StartupCheck.IsChecked == true, RestoreCheck.IsChecked == true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowFeedback(error.Message); }
    }
    private void ExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = ReadEditor(); p.Validate();
            var dialog = new SaveFileDialog { Filter = "Sora fan profile (*.json)|*.json", FileName = $"Sora-{FanProtocol.HeaderNames[selected].Replace('/', '-')}-fan.json" };
            if (dialog.ShowDialog() == true) File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException) { ShowFeedback(error.Message); }
    }
    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Sora fan profile (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 64_000) throw new InvalidDataException("Fan profile is too large.");
            var p = JsonSerializer.Deserialize<FanProfile>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("Empty fan profile.");
            p = p with { Header = selected }; p.Validate(); LoadEditor(p); MarkChanged(); ShowFeedback("Profile staged for this header. Click Apply to send it.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { ShowFeedback(error.Message); }
    }
    private void ShowFeedback(string message) { measurementMessage = message; Render(); }
    internal void ShowLowerControlsPreview() { UpdateLayout(); EditorScroll.ScrollToEnd(); }
    internal void ShowRadiatorHeaderPreview() { HeaderList.SelectedIndex = 5; }
    internal async Task<Dictionary<string, bool>> RunEditorChecksAsync()
    {
        if (service?.Snapshot is not { } snapshot) throw new InvalidOperationException("Editor checks need a recorded snapshot.");
        var results = new Dictionary<string, bool>();
        string preferences = JsonSerializer.Serialize(service.Preferences);
        void Check(string name, bool pass) { results[name] = pass; if (!pass) throw new InvalidOperationException(name); }
        try
        {
            HeaderList.SelectedIndex = 0; LoadCurrentProfile();
            Check("Apply starts disabled when the fan settings are unchanged", !ApplyButton.IsEnabled && Equals(ApplyButton.Content, "No changes"));
            Check("Recovered CPU source is displayed", SourceBox.SelectedIndex == 2);
            LoadEditor(FanProfile.Preset(0, "Quiet") with { FirmwareStop = snapshot.Headers[0].FirmwareStop }); MarkChanged();
            if (currentFirmware != null)
            {
                FirmwareOffBox.Text = "20"; FirmwareOnBox.Text = "25"; FirmwareStopCheck.IsChecked = true;
                Check("Firmware stop uses the selected live temperature source", ReadEditor().FirmwareStop == new FanStopConfig(true, 2, 20, 25));
            }
            HeaderList.SelectedIndex = 5;
            Check("Switching headers loads that header's curve", ReadEditor().Header == 5 && HeaderTitle.Text == FanProtocol.HeaderNames[5]);
            var selectedRow = (ListBoxItem)HeaderList.ItemContainerGenerator.ContainerFromIndex(5);
            var bounds = selectedRow.TransformToAncestor(HeaderList).TransformBounds(new Rect(selectedRow.RenderSize));
            Check("Selected header stays visible in the scrolling list", bounds.Top >= 0 && bounds.Bottom <= HeaderList.ActualHeight);
            HeaderList.SelectedIndex = 0;
            Check("Draft survives switching headers", ReadEditor().Points[0].DutyPercent == 25 && HasPendingEdits);
            var originalCurve = ReadEditor().Points;
            ModeBox.SelectedIndex = (int)FanCurveMode.Step;
            Check("Mode selection immediately updates the chart to stair step", Chart.Profile?.Mode == FanCurveMode.Step && ModeHint.Text.Contains("holds", StringComparison.Ordinal));
            byte[] ChartPixels()
            {
                Chart.UpdateLayout();
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Chart.ActualWidth, (int)Chart.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(Chart); var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
            }
            var stepPixels = ChartPixels();
            ModeBox.SelectedIndex = (int)FanCurveMode.Curve;
            Check("Curve and stair-step modes render different paths", Chart.Profile?.Mode == FanCurveMode.Curve && !stepPixels.SequenceEqual(ChartPixels()));
            StopBox.Text = "15";
            ModeBox.SelectedIndex = (int)FanCurveMode.Fixed;
            Check("Fixed mode clears native stop in the applied model", ReadEditor().StopBelowC == 0 && !StopBox.IsEnabled);
            Check("Fixed duty disables firmware stop", ReadEditor().FirmwareStop?.Enabled != true);
            ReadEditor().Validate();
            ModeBox.SelectedIndex = (int)FanCurveMode.Curve;
            Check("Returning from fixed duty restores the prior temperature points", ReadEditor().Points.SequenceEqual(originalCurve));
            var appliedProfile = ReadEditor();
            var rowBeforeApply = rows[0]; double scrollBeforeApply = EditorScroll.VerticalOffset;
            CompleteAppliedProfile(selected, appliedProfile); Render();
            Check("Successful Apply becomes disabled Applied without rebuilding the editor", !ApplyButton.IsEnabled && Equals(ApplyButton.Content, "Applied") && ReferenceEquals(rows[0], rowBeforeApply) && EditorScroll.VerticalOffset == scrollBeforeApply);
            MarkChanged();
            Check("An unchanged edit does not re-enable Apply", !ApplyButton.IsEnabled && !drafts.ContainsKey(selected));
            HysteresisBox.Text = "4";
            Check("Changing a fan setting re-enables Apply immediately", ApplyButton.IsEnabled && Equals(ApplyButton.Content, "Apply to fan"));
            HysteresisBox.Text = "3";
            Check("Reverting a fan setting returns to Applied", !ApplyButton.IsEnabled && Equals(ApplyButton.Content, "Applied"));
            for (int i = 0; i < 12; i++) AddPointClick(this, new RoutedEventArgs());
            Check("Point editor caps curve at eleven points", rows.Count == 11);
            ReadEditor().Validate();
            HysteresisBox.Text = "invalid";
            Render();
            Check("Polling preserves incomplete-value feedback", PendingLabel.Text.Contains("numeric values", StringComparison.Ordinal));
            HeaderList.SelectedIndex = 5;
            Check("Incomplete numeric edit cannot be lost on header switch", selected == 0 && HeaderList.SelectedIndex == 0 && HysteresisBox.Text == "invalid");
            HysteresisBox.Text = "3";
            rows[0].Duty = "NaN";
            Check("Non-finite duty cannot reach chart rendering", invalidEditor);
            rows[0].Duty = "25";
            DeviceKindBox.SelectedIndex = (int)FanDeviceKind.Pump;
            Check("Pump headers cannot run stop/start calibration", !CalibrateButton.IsEnabled);
            Check("Screenshot preview rejects hardware applies", !await service.ApplyAsync(FanProfile.Preset(0, "Full speed")));
            Check("Preview apply cannot save fan preferences", JsonSerializer.Serialize(service.Preferences) == preferences);
        }
        finally
        {
            service.SetPreview(snapshot); drafts.Clear(); nameDrafts.Clear(); baselines.Clear(); temperatureProfiles.Clear(); appliedHeaders.Clear(); invalidEditor = false; measurementMessage = null;
            HeaderList.SelectedIndex = 0; selected = 0; LoadCurrentProfile(); Render();
        }
        await RunRefreshChecksAsync(snapshot, Check);
        return results;
    }
    private async Task RunRefreshChecksAsync(FanSnapshot snapshot, Action<string, bool> check)
    {
        var originalService = service!;
        var directory = Path.Combine(AppContext.BaseDirectory, "fan-ui-check-" + Guid.NewGuid().ToString("N"));
        var isolatedHost = new SoraHost(directory);
        var source = new TaskCompletionSource<FanReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pollingService = new FanService(isolatedHost, () => source.Task.GetAwaiter().GetResult());
        Task<bool>? pendingRead = null, pendingApply = null;
        try
        {
            drafts.Clear(); nameDrafts.Clear(); baselines.Clear(); temperatureProfiles.Clear(); appliedHeaders.Clear(); editorLoaded = false; Attach(pollingService);
            pendingRead = pollingService.ReadAsync();
            check("Background reads never mark the editor busy", pollingService.Reading && !pollingService.Busy);
            HeaderNameBox.Text = "Radiator fans"; HeaderNameBox.Focus(); HeaderNameBox.Select(3, 4);
            // Keep a read in flight across two actual timer ticks, reproducing the reported cadence.
            await Task.Delay(6500);
            check("Editor and name input stay enabled across polling ticks", HeaderList.IsEnabled && ProfileEditor.IsEnabled && HeaderNameBox.IsEnabled && HeaderNameBox.SelectionStart == 3 && HeaderNameBox.SelectionLength == 4);
            source.SetResult(new(snapshot, false, null)); await pendingRead;
            check("First successful background read initializes the curve editor", SourceBox.SelectedIndex == snapshot.Headers[0].Profile.TemperatureSource && rows.Count == snapshot.Headers[0].Profile.Points.Length);
            check("Unsaved header name survives a background read", HeaderNameBox.Text == "Radiator fans" && HasPendingEdits);
            HeaderList.SelectedIndex = 5; HeaderList.SelectedIndex = 0;
            check("Unsaved header name survives switching headers", HeaderNameBox.Text == "Radiator fans");
            SaveHeaderName(HeaderNameBox.Text);
            var reloadedHost = new SoraHost(directory);
            check("Saved header names survive restarting Sora", reloadedHost.Fans.DisplayName(0) == "Radiator fans");
            check("Renaming leaves other header labels unchanged", reloadedHost.Fans.DisplayName(5) == FanProtocol.HeaderNames[5]);
            check("Renaming keeps the physical header visible", HeaderDetail.Text.StartsWith("CPU ·", StringComparison.Ordinal));
            DeviceKindBox.SelectedIndex = (int)FanDeviceKind.FixedSpeedPump;
            check("Fixed-speed pump hides irrelevant fan curves and sweeps", FanSettingsPanel.Visibility == Visibility.Collapsed && MeasurementControls.Visibility == Visibility.Collapsed && !ApplyButton.IsEnabled && !CalibrateButton.IsEnabled);
            check("Connection type persists separately from hardware profiles", new SoraHost(directory).Fans.DeviceKind(0) == FanDeviceKind.FixedSpeedPump && pollingService.Preferences.Profiles.Count == 0);
            DeviceKindBox.SelectedIndex = (int)FanDeviceKind.Unknown;
            HysteresisBox.Text = "4";

            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRead = pollingService.ReadAsync();
            // An invalid header guarantees this checks scheduling/failure without any hardware writes.
            pendingApply = pollingService.ApplyAsync(new FanProfile { Header = 99 });
            check("User action queues behind an active read", !pendingApply.IsCompleted && pollingService.Busy && !ProfileEditor.IsHitTestVisible);
            check("Apply locks input without greying or disabling the page", HeaderList.IsEnabled && ProfileEditor.IsEnabled && HeaderNameBox.IsEnabled && HeaderNameBox.IsReadOnly && !HeaderList.IsHitTestVisible);
            HeaderList.ApplyTemplate();
            var surface = (Border)HeaderList.Template.FindName("HeaderListSurface", HeaderList);
            check("Disabled header list retains its dark surface", surface.Background is SolidColorBrush brush && brush.Color.A == 0);
            source.SetResult(new(snapshot, false, null)); await pendingRead;
            check("Invalid apply is rejected", !await pendingApply);
            string failure = pollingService.Message;
            check("Rejected apply preserves live readings and editor", pollingService.Snapshot != null && HeaderList.IsEnabled && ProfileEditor.IsEnabled && HeaderNameBox.Text == "Radiator fans");
            await pollingService.ReadAsync();
            check("Following poll preserves the operation result", pollingService.Message == failure);
            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRead = pollingService.ReadAsync(); source.SetException(new IOException("Injected read failure"));
            await pendingRead;
            check("Read failure clears stale live readings", pollingService.Snapshot == null && !ApplyButton.IsEnabled);
            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRead = pollingService.ReadAsync(); source.SetResult(new(snapshot, false, null)); await pendingRead;
            check("Read recovery restores controls and saved names", pollingService.Snapshot != null && ApplyButton.IsEnabled && HeaderNameBox.Text == "Radiator fans");
            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRead = pollingService.ReadAsync(); source.SetResult(new(snapshot, true, null, "EasyTuneEngineService")); await pendingRead;
            check("GCC conflict is visible above the editor with a pause action", ConflictBanner.Visibility == Visibility.Visible && PauseServiceButton.Visibility == Visibility.Visible && PauseServiceButton.IsEnabled);
            check("GCC conflict blocks Apply, Restore and speed tests", !ApplyButton.IsEnabled && !RestoreButton.IsEnabled && !MeasureButton.IsEnabled && !CalibrateButton.IsEnabled);
            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRead = pollingService.ReadAsync(); source.SetResult(new(snapshot, true, null)); await pendingRead;
            check("Clearing a GCC conflict enables Apply without losing the editor", ConflictBanner.Visibility == Visibility.Collapsed && ApplyButton.IsEnabled && HeaderNameBox.Text == "Radiator fans");
            var savedFixed = FanProfile.Preset(0, "Quiet") with { Mode = FanCurveMode.Fixed, FixedDutyPercent = 60, FirmwareStop = snapshot.Headers[0].FirmwareStop };
            var map = snapshot.Headers[0].Registers.ToDictionary(v => v.Register, v => v.Value);
            foreach (var write in FanProtocol.Compile(savedFixed)) map[write.Register] = (byte)((map[write.Register] & ~write.Mask) | (write.Value & write.Mask));
            var headers = snapshot.Headers.ToArray();
            headers[0] = headers[0] with { Profile = FanProtocol.Decode(0, map), Registers = map.Select(p => new FanRegisterValue(p.Key.Bank, p.Key.Address, p.Value)).OrderBy(v => v.Bank).ThenBy(v => v.Address).ToArray() };
            var fixedSnapshot = snapshot with { Headers = headers };
            var savedPreferences = pollingService.Preferences with { Profiles = new() { [0] = savedFixed } };
            File.WriteAllText(Path.Combine(directory, "fans.json"), JsonSerializer.Serialize(savedPreferences)); pollingService.Load();
            drafts.Clear(); baselines.Clear(); temperatureProfiles.Clear(); editorLoaded = false;
            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRead = pollingService.ReadAsync(); source.SetResult(new(fixedSnapshot, false, null)); await pendingRead;
            check("Fixed-duty readback retains saved temperature points after reopening", ReadEditor().Mode == FanCurveMode.Fixed && ReadEditor().Points.SequenceEqual(savedFixed.Points));
            ModeBox.SelectedIndex = (int)FanCurveMode.Step;
            check("Switching a saved fixed profile to stair step restores a non-flat graph", Chart.Profile?.Mode == FanCurveMode.Step && Chart.Profile.Points.SequenceEqual(savedFixed.Points) && ApplyButton.IsEnabled);
        }
        finally
        {
            source.TrySetResult(new(snapshot, false, null));
            if (pendingRead != null) await pendingRead;
            if (pendingApply != null) await pendingApply;
            await pollingService.StopAsync();
            drafts.Clear(); nameDrafts.Clear(); baselines.Clear(); temperatureProfiles.Clear(); appliedHeaders.Clear(); invalidEditor = false; editorLoaded = false; selected = 0; HeaderList.SelectedIndex = 0;
            Attach(originalService); LoadCurrentProfile(); Render();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    internal async Task<Dictionary<string, bool>> RunLiveRefreshChecksAsync()
    {
        if (service?.Snapshot == null || service.IsPreview) throw new InvalidOperationException("Live refresh checks require actual read-only fan telemetry.");
        var reads = new HashSet<DateTimeOffset> { service.Snapshot.ReadAt };
        var before = service.Snapshot;
        bool stable = true;
        bool unchanged = true;
        HeaderNameBox.Focus(); HeaderNameBox.Select(0, Math.Min(2, HeaderNameBox.Text.Length));
        int selectionStart = HeaderNameBox.SelectionStart, selectionLength = HeaderNameBox.SelectionLength;
        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(100);
            stable &= !service.Busy && HeaderList.IsEnabled && HeaderNameBox.IsEnabled && HeaderNameBox.SelectionStart == selectionStart && HeaderNameBox.SelectionLength == selectionLength;
            if (service.Snapshot != null)
            {
                reads.Add(service.Snapshot.ReadAt);
                unchanged &= before.Headers.Zip(service.Snapshot.Headers).All(pair => pair.First.Registers.SequenceEqual(pair.Second.Registers) && pair.First.FirmwareStop == pair.Second.FirmwareStop);
            }
            else stable = false;
        }
        return new() { ["Live telemetry completed at least three refresh cycles"] = reads.Count >= 4,
            ["Controls and name selection stayed stable throughout live polling"] = stable,
            ["Native and firmware fan configuration stayed unchanged"] = unchanged };
    }
}

internal sealed class FanPointRow : INotifyPropertyChanged
{
    private string temperature = "", duty = "";
    public string Temperature { get => temperature; set { temperature = value; PropertyChanged?.Invoke(this, new(nameof(Temperature))); } }
    public string Duty { get => duty; set { duty = value; PropertyChanged?.Invoke(this, new(nameof(Duty))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
internal sealed class FanHeaderRow(string name) : INotifyPropertyChanged
{
    private string displayName = name;
    public string Name { get => displayName; set { if (displayName == value) return; displayName = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    private string speed = "Not checked";
    public string Speed { get => speed; set { if (speed == value) return; speed = value; PropertyChanged?.Invoke(this, new(nameof(Speed))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
