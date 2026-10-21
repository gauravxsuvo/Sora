using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sora.Core;

namespace Sora.App;

internal sealed class GpuFanPointEditor
{
    public double TemperatureC { get; set; }
    public double DutyPercent { get; set; }
}
internal sealed class GpuRpmPointEditor
{
    public double TemperatureC { get; set; }
    public uint Rpm { get; set; }
}

public partial class GpuFanPanel : UserControl
{
    private GpuFanService? service;
    private readonly ObservableCollection<GpuFanPointEditor> points = new();
    private readonly ObservableCollection<GpuRpmPointEditor> rpmPoints = new();
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool loading = true, dirty;
    private string? editorIdentity, catalogIdentity;
    public bool HasPendingEdits => dirty;
    public bool IsBusy => service?.Busy ?? false;
    public event Action<string>? StatusChanged;
    public Task ReadAsync() => service?.ReadAsync() ?? Task.CompletedTask;
    public GpuFanPanel()
    {
        InitializeComponent(); PointsGrid.ItemsSource = points; RpmPointsGrid.ItemsSource = rpmPoints;
        SetProfile(new()); loading = false;
        poll.Tick += async (_, _) => { if (IsVisible && service != null && !service.Busy && !service.HasPendingEdits) await service.ReadAsync(false); };
        Loaded += (_, _) => { if (service != null) { service.Changed -= OnServiceChanged; service.Changed += OnServiceChanged; } if (IsVisible) poll.Start(); };
        Unloaded += (_, _) => { poll.Stop(); if (service != null) service.Changed -= OnServiceChanged; };
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) poll.Start(); else poll.Stop(); };
    }
    internal void Attach(GpuFanService value)
    {
        if (service != null) service.Changed -= OnServiceChanged;
        service = value; value.Changed += OnServiceChanged; Render();
    }
    private void OnServiceChanged() { if (Dispatcher.CheckAccess()) Render(); else Dispatcher.BeginInvoke(Render); }
    private GpuFanSnapshot? Selected => GpuBox.SelectedItem as GpuFanSnapshot;
    private void Render()
    {
        if (service == null) return;
        string? selected = Selected?.Identity; loading = true;
        GpuBox.ItemsSource = service.Snapshots;
        GpuBox.SelectedItem = service.Snapshots.FirstOrDefault(g => g.Identity == selected) ?? service.Snapshots.FirstOrDefault();
        loading = false; RenderSnapshot();
        var rememberedProfile = Selected == null ? null : service.CurrentProfile(Selected.Identity);
        if (Selected != null && !dirty && (editorIdentity != Selected.Identity || rememberedProfile != null && (rememberedProfile.Name != NameBox.Text || (int)rememberedProfile.Mode != ModeBox.SelectedIndex)))
        {
            editorIdentity = Selected.Identity;
            SetProfile(rememberedProfile ?? new GpuFanProfile { Mode = Selected.Hardware?.CanProgram == true ? GpuFanMode.HardwareCurve : GpuFanMode.Automatic, RpmPoints = Selected.Hardware?.Fans[0].Points ?? new GpuFanProfile().RpmPoints }); dirty = false;
        }
        ProfileStateText.Text = dirty ? "Unsaved editor changes · Apply changes the GPU and remembers this profile for startup / resume." : Selected != null ? service.LifecycleDescription(Selected.Identity) : "Read the driver to select a GPU.";
        RefreshProfileList();
        StatusText.Text = service.Message; RefreshButton.IsEnabled = !service.Busy; ControlState.SetIsBusy(RefreshButton, service.Busy);
        ApplyButton.IsEnabled = !service.Busy && Selected?.CanControl == true;
        RestoreButton.IsEnabled = !service.Busy && Selected != null && (service.HasPendingEdits || service.CurrentProfile(Selected.Identity) != null);
        NameBox.IsReadOnly = service.Busy; ModeBox.IsEnabled = !service.Busy; DutySlider.IsEnabled = !service.Busy; PointsGrid.IsReadOnly = service.Busy;
        RpmPointsGrid.IsReadOnly = service.Busy; FixedRpmSlider.IsEnabled = !service.Busy; StopBox.IsEnabled = !service.Busy && ModeBox.SelectedIndex == 3;
        FanTargetBox.IsEnabled = !service.Busy; SavedProfileBox.IsEnabled = !service.Busy;
        StatusChanged?.Invoke(service.Busy ? "Working…" : !service.HasRead ? "Not checked" : service.Snapshots.Length == 0 ? "Unavailable" : service.Snapshots[0].TemperatureC is double temperature ? $"{temperature:0}°C" : "Ready");
    }
    private void RenderSnapshot()
    {
        var gpu = Selected;
        TemperatureText.Text = gpu?.TemperatureC is double value ? value.ToString("0", CultureInfo.CurrentCulture) + " °C" : "— °C";
        FansText.Text = gpu == null ? "Read the driver to see fan channels." : string.Join("\n", gpu.Fans.Select(f => $"Fan {f.Id}    {f.Rpm:N0} RPM    {f.DutyPercent}%    {(f.ControlMode == 0 ? "GPU managed" : f.ControlMode == 1 ? "Manual duty" : "Read only")}    Allowed {f.MinimumDutyPercent}–{f.MaximumDutyPercent}%"));
        bool stoppedWithAutoStop = gpu != null && gpu.Fans.Any(f => f.Rpm == 0 && gpu.Hardware?.Fans.Any(native => native.Index + 1 == f.Id && native.ZeroRpmEnabled) == true);
        FanStopNotice.Visibility = stoppedWithAutoStop ? Visibility.Visible : Visibility.Collapsed;
        FanStopNotice.Text = "0 RPM · firmware Auto Fan Stop is enabled. The GPU can stop its fans at low load / temperature and restart them when needed. There is no universal 50°C switch point. Disable Auto fan stop in a native profile and Apply if you want the fans to keep running.";
        SupportText.Text = gpu?.Notice ?? "NVIDIA driver required. AMD and Intel GPU fan writes are unavailable.";
        HardwareReadText.Text = gpu?.Hardware is { } hardware ? string.Join("\n", hardware.Fans.Select(f => $"Native fan {f.Index + 1}: " + string.Join(" · ", f.Points.Select(p => $"{p.TemperatureC:0.#}°C / {p.Rpm} RPM")) + $" · Auto stop {(f.ZeroRpmEnabled ? "on" : "off")}")) : "Native RPM programming is verified only for GV-N507TWF3-16GD with the recovered three-channel firmware interface.";
    }
    private GpuFanProfile ReadProfile()
    {
        foreach (var grid in new[] { PointsGrid, RpmPointsGrid })
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true) || HasValidationError(grid)) throw new ArgumentException("Correct the invalid curve cell before applying or exporting.");
        var profile = new GpuFanProfile { Name = NameBox.Text.Trim(), Mode = (GpuFanMode)Math.Max(0, ModeBox.SelectedIndex), FixedDutyPercent = DutySlider.Value, Points = points.Select(p => new GpuFanPoint(p.TemperatureC, p.DutyPercent)).ToArray(), RpmPoints = rpmPoints.Select(p => new GpuRpmPoint(p.TemperatureC, p.Rpm)).ToArray(), FixedRpm = (uint)FixedRpmSlider.Value, AutoFanStop = StopBox.IsChecked == true, FanIndices = FanTargetBox.SelectedIndex > 0 ? [(uint)(FanTargetBox.SelectedIndex - 1)] : [0, 1, 2] };
        profile.Validate(); return profile;
    }
    private void SetProfile(GpuFanProfile profile)
    {
        bool previous = loading; loading = true;
        NameBox.Text = profile.Name; ModeBox.SelectedIndex = (int)profile.Mode; DutySlider.Value = profile.FixedDutyPercent;
        points.Clear(); foreach (var p in profile.Points) points.Add(new() { TemperatureC = p.TemperatureC, DutyPercent = p.DutyPercent });
        rpmPoints.Clear(); foreach (var p in profile.RpmPoints) rpmPoints.Add(new() { TemperatureC = p.TemperatureC, Rpm = p.Rpm });
        FixedRpmSlider.Value = profile.FixedRpm; StopBox.IsChecked = profile.AutoFanStop;
        FanTargetBox.SelectedIndex = profile.FanIndices.Length == 1 ? (int)profile.FanIndices[0] + 1 : 0;
        loading = previous; RefreshEditor();
        SyncProfileSelection();
    }
    private void RefreshEditor()
    {
        if (CurvePanel == null) return;
        CurvePanel.Visibility = ModeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        FixedPanel.Visibility = ModeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        HardwarePanel.Visibility = ModeBox.SelectedIndex >= 3 ? Visibility.Visible : Visibility.Collapsed;
        RpmPointsGrid.Visibility = ModeBox.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        RpmFixedPanel.Visibility = ModeBox.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        StopBox.IsEnabled = !IsBusy && ModeBox.SelectedIndex == 3;
        ApplyButton.Content = ModeBox.SelectedIndex >= 3 && FanTargetBox.SelectedIndex > 0 ? $"Apply to fan {FanTargetBox.SelectedIndex}" : "Apply to all GPU fans";
        ModeHint.Text = ModeBox.SelectedIndex == 0 ? "The driver manages temperature response and the original native curve / zero-RPM settings. Apply remembers this choice for startup and resume." : ModeBox.SelectedIndex >= 3 ? "The GPU runs native RPM curves and Auto Fan Stop. Applied settings remain after Exit and reapply on startup / resume. Fixed RPM rises to 3000 RPM at 85°C. Restore original returns to the backed-up settings and clears automatic reapplication." : "Duty applies to every reported fan channel while Sora runs. At 85°C Sora requests 100%; a lost sensor restores original control. Exit safely restores original settings; your applied profile automatically returns next launch / resume. Native RPM modes run directly on the GPU.";
        Chart.Points = points.Select(p => new GpuFanPoint(p.TemperatureC, p.DutyPercent)).ToArray(); Chart.InvalidateVisual();
        RpmChart.Points = ModeBox.SelectedIndex == 4 ? new[] { new GpuFanPoint(30, FixedRpmSlider.Value / 30), new GpuFanPoint(84, FixedRpmSlider.Value / 30), new GpuFanPoint(85, 100) } : rpmPoints.Select(p => new GpuFanPoint(p.TemperatureC, p.Rpm / 30d)).ToArray(); RpmChart.Maximum = 3000; RpmChart.Unit = "RPM"; RpmChart.InvalidateVisual();
        try { ReadProfile(); ValidationText.Text = ""; } catch (ArgumentException e) { ValidationText.Text = e.Message; }
    }
    private void EditorChanged(object sender, RoutedEventArgs e) { if (!loading) { dirty = true; RefreshEditor(); } }
    private void ModeChanged(object sender, SelectionChangedEventArgs e) { if (!loading) { dirty = true; RefreshEditor(); } }
    private void DutyChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (DutyText != null) DutyText.Text = $"Fixed duty · {e.NewValue:0}%"; if (!loading) dirty = true; }
    private void FixedRpmChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (FixedRpmText != null) FixedRpmText.Text = $"Fixed target · {e.NewValue:0} RPM"; if (!loading) { dirty = true; RefreshEditor(); } }
    private void PointEdited(object sender, DataGridCellEditEndingEventArgs e) { dirty = true; Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshEditor)); }
    private void GpuChanged(object sender, SelectionChangedEventArgs e) { if (!loading) { RenderSnapshot(); RefreshProfileList(true); } }
    private async void ReadClick(object sender, RoutedEventArgs e) { if (service != null) await service.ReadAsync(); }
    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (service == null || Selected == null) return;
        try { var profile = ReadProfile(); await service.ApplyAsync(Selected.Identity, profile); if (service.Message.Contains("applied and read back", StringComparison.Ordinal)) dirty = false; Render(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { ValidationText.Text = error.Message; }
    }
    private async void RestoreClick(object sender, RoutedEventArgs e) { if (service != null && Selected != null) await service.RestoreAsync(Selected.Identity); }
    private void LoadHardwareClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || Selected?.Hardware is not { } hardware) return;
        int index = Math.Max(0, FanTargetBox.SelectedIndex - 1);
        rpmPoints.Clear(); foreach (var p in hardware.Fans[index].Points) rpmPoints.Add(new() { TemperatureC = p.TemperatureC, Rpm = p.Rpm }); StopBox.IsChecked = FanTargetBox.SelectedIndex == 0 ? hardware.Fans.All(f => f.ZeroRpmEnabled) : hardware.Fans[index].ZeroRpmEnabled; dirty = true; RefreshEditor();
    }
    private void FanTargetChanged(object sender, SelectionChangedEventArgs e) { if (!loading) { dirty = true; RefreshEditor(); } }
    private void RefreshProfileList(bool force = false)
    {
        if (service == null || Selected == null) return;
        if (!force && catalogIdentity == Selected.Identity && SavedProfileBox.Items.OfType<GpuFanProfile>().Any(p => p.Name == service.CurrentProfile(Selected.Identity)?.Name)) { SyncProfileSelection(); return; }
        try
        {
            bool previous = loading; loading = true; var catalog = service.Profiles.Read(Selected.Identity).ToList();
            var current = service.CurrentProfile(Selected.Identity);
            if (current != null) { catalog.RemoveAll(p => string.Equals(p.Name, current.Name, StringComparison.OrdinalIgnoreCase)); catalog.Add(current); }
            SavedProfileBox.ItemsSource = catalog.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray(); catalogIdentity = Selected.Identity; loading = previous; SyncProfileSelection();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { loading = false; ValidationText.Text = error.Message; }
    }
    private void SyncProfileSelection()
    {
        if (SavedProfileBox == null) return; bool previous = loading; loading = true;
        SavedProfileBox.SelectedItem = SavedProfileBox.Items.OfType<GpuFanProfile>().FirstOrDefault(p => string.Equals(p.Name, NameBox.Text, StringComparison.OrdinalIgnoreCase));
        SavedProfilePlaceholder.Visibility = SavedProfileBox.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;
        SavedProfilePlaceholder.Text = SavedProfileBox.Items.Count == 0 ? "No saved profiles" : "Select saved profile"; loading = previous;
    }
    private void SavedProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedProfilePlaceholder != null) SavedProfilePlaceholder.Visibility = SavedProfileBox.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;
        if (!loading && !IsBusy && SavedProfileBox.SelectedItem is GpuFanProfile profile) { SetProfile(profile); dirty = true; ProfileStateText.Text = "Profile selected in editor · Apply remembers it for startup / resume."; }
    }
    private void SaveProfileClick(object sender, RoutedEventArgs e)
    {
        if (service == null || Selected == null || IsBusy) return;
        try { service.Profiles.Save(Selected.Identity, ReadProfile()); RefreshProfileList(true); StatusText.Text = "Named profile saved for this GPU. Apply to send it to hardware."; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException or System.Collections.Generic.KeyNotFoundException) { ValidationText.Text = error.Message; }
    }
    private void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (service == null || Selected == null || IsBusy || SavedProfileBox.SelectedItem is not GpuFanProfile profile) return;
        try { service.Profiles.Delete(Selected.Identity, profile.Name); RefreshProfileList(true); StatusText.Text = "Saved profile deleted. Current hardware settings are unchanged."; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { ValidationText.Text = error.Message; }
    }
    internal void PrepareCurvePreview()
    {
        ModeBox.SelectedIndex = Selected?.Hardware?.CanProgram == true ? 3 : 2; RefreshEditor();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { UpdateLayout(); if (ModeBox.SelectedIndex >= 3) RpmChart.BringIntoView(); else Chart.BringIntoView(); }));
    }
    private static bool HasValidationError(DependencyObject parent)
    {
        if (Validation.GetHasError(parent)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) if (HasValidationError(VisualTreeHelper.GetChild(parent, i))) return true;
        return false;
    }
    private void DefaultClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SetProfile(Selected?.FactoryHardware is { } factory ? new GpuFanProfile { Name = "Factory curve", Mode = GpuFanMode.HardwareCurve, RpmPoints = factory.Fans[0].Points } : new GpuFanProfile());
        dirty = true; StatusText.Text = "Factory/default curve staged in the editor. Apply to send it to the GPU.";
    }
    private void AddPointClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || points.Count >= 16) return;
        var before = points[^2]; var last = points[^1];
        points.Insert(points.Count - 1, new() { TemperatureC = (before.TemperatureC + last.TemperatureC) / 2, DutyPercent = (before.DutyPercent + last.DutyPercent) / 2 }); dirty = true; RefreshEditor();
    }
    private void RemovePointClick(object sender, RoutedEventArgs e) { if (!IsBusy && points.Count > 2) { points.RemoveAt(points.Count - 2); dirty = true; RefreshEditor(); } }
    private void ImportClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog { Filter = "Sora / GCC GPU fan profiles|*.json;*.dat|All files|*.*", Title = "Import Sora or GCC GPU fan profile" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 65536) throw new InvalidDataException("GPU profile exceeds 64 KiB.");
            string text = File.ReadAllText(dialog.FileName);
            var profile = text.TrimStart().StartsWith("[", StringComparison.Ordinal) ? GpuGccFanProfiles.Import(text, Path.GetFileNameWithoutExtension(dialog.FileName), FanTargetBox.SelectedIndex > 0 ? (uint)(FanTargetBox.SelectedIndex - 1) : null) : JsonSerializer.Deserialize<GpuFanProfile>(text) ?? throw new InvalidDataException("Missing GPU profile.");
            profile.Validate(); SetProfile(profile); dirty = true; StatusText.Text = "Profile imported into editor. Apply to send it to the GPU.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException or System.Collections.Generic.KeyNotFoundException) { ValidationText.Text = "Cannot import GPU profile: " + error.Message; }
    }
    private void ExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = ReadProfile(); var dialog = new SaveFileDialog { Filter = "Sora GPU fan profile (*.json)|*.json", FileName = "gpu-fan-profile.json" };
            if (dialog.ShowDialog() == true) { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true })); StatusText.Text = "Sora profile exported."; }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { ValidationText.Text = error.Message; }
    }
}

public sealed class GpuFanChart : FrameworkElement
{
    public GpuFanPoint[] Points { get; set; } = Array.Empty<GpuFanPoint>();
    public double Maximum { get; set; } = 100;
    public string Unit { get; set; } = "%";
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); double width = Math.Max(1, ActualWidth - 72), height = Math.Max(1, ActualHeight - 64);
        var grid = new Pen(TryFindResource("SeparatorBrush") as Brush ?? Brushes.LightGray, 1); var accent = TryFindResource("ChartBrush") as Brush ?? Brushes.SeaGreen;
        Point Map(double temperature, double duty) => new(48 + width * temperature / 100, 30 + height * (1 - duty / 100));
        var surface = TryFindResource("InsetBrush") as Brush ?? Brushes.WhiteSmoke;
        dc.DrawRoundedRectangle(surface, null, new Rect(Map(0, 100), Map(100, 0)), 8, 8);
        for (int i = 0; i <= 5; i++) { dc.DrawLine(grid, Map(i * 20, 0), Map(i * 20, 100)); dc.DrawLine(grid, Map(0, i * 20), Map(100, i * 20)); }
        var valid = Points.Where(p => double.IsFinite(p.TemperatureC) && double.IsFinite(p.DutyPercent) && p.TemperatureC is >= 0 and <= 100 && p.DutyPercent is >= 0 and <= 100).ToArray();
        if (valid.Length > 1)
        {
            var area = new StreamGeometry();
            using (var path = area.Open())
            {
                path.BeginFigure(Map(valid[0].TemperatureC, 0), true, true);
                foreach (var p in valid) path.LineTo(Map(p.TemperatureC, p.DutyPercent), true, false);
                path.LineTo(Map(valid[^1].TemperatureC, 0), true, false);
            }
            dc.PushOpacity(.12); dc.DrawGeometry(accent, null, area); dc.Pop();
        }
        for (int i = 1; i < valid.Length; i++) dc.DrawLine(new Pen(accent, 2.5), Map(valid[i - 1].TemperatureC, valid[i - 1].DutyPercent), Map(valid[i].TemperatureC, valid[i].DutyPercent));
        foreach (var p in valid) dc.DrawEllipse(surface, new Pen(accent, 2), Map(p.TemperatureC, p.DutyPercent), 4.5, 4.5);
        FormattedText Label(string text) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface((FontFamily)FindResource("UIFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10, TryFindResource("TertiaryTextBrush") as Brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(Label(Unit == "RPM" ? "FAN SPEED (RPM)" : "FAN DUTY (%)"), new Point(48, 5));
        dc.DrawText(Label("TEMPERATURE (°C)"), new Point(Math.Max(48, ActualWidth - 132), ActualHeight - 13));
        for (int i = 0; i <= 5; i++)
        {
            dc.DrawText(Label((i * 20).ToString(CultureInfo.InvariantCulture) + "°"), new Point(41 + width * i / 5, height + 39)); dc.DrawText(Label((i * Maximum / 5).ToString("0", CultureInfo.InvariantCulture)), new Point(2, 23 + height * (1 - i / 5d)));
        }
    }
}
