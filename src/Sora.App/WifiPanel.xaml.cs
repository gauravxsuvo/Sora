using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Sora.Core;

namespace Sora.App;

public partial class WifiPanel : UserControl, IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    // Watches for Wi-Fi being turned back on with the same read-only calls; nothing is sent to the card (see WifiScanner's remarks).
    private readonly DispatcherTimer radioCheck = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly WifiAlignment alignment = new();
    private CancellationTokenSource? operation;
    private SoraHost? host;
    private WifiSnapshot? snapshot;
    private bool busy, updating, disposed;
    private DateTimeOffset? liveStarted;
    private DateTimeOffset? lastFresh;
    private int? displayedSignal;
    public bool IsBusy => busy;
    public event Action<string>? StatusChanged;

    public WifiPanel()
    {
        InitializeComponent();
        DrawGauge(); DrawChart(); DrawPositionChart(); UpdatePositionVisuals();
        Loaded += async (_, _) => { if (!disposed && IsVisible) await ReadAsync(); };
        Unloaded += (_, _) => StopWatching();
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopWatching(); else if (IsLoaded && snapshot != null) _ = RefreshAsync(false); };
        radioCheck.Tick += async (_, _) => { if (IsVisible && !disposed) await RefreshAsync(false); else radioCheck.Stop(); };
        timer.Tick += async (_, _) =>
        {
            if (!IsVisible || disposed) { StopWatching(); return; }
            if (liveStarted is { } began && DateTimeOffset.UtcNow - began >= TimeSpan.FromMinutes(5) && alignment.MeasuringPosition == null)
            { LiveCheck.IsChecked = false; SetStatus("Five-minute signal check finished."); return; }
            await RefreshAsync(false);
        };
    }

    internal void Attach(SoraHost soraHost) => host = soraHost;
    public Task ReadAsync() => RefreshAsync(true);
    /// <summary>
    /// A present network device with a Wi-Fi name that Windows could not start (Device Manager error code), as a user-facing
    /// sentence; null if there is none. Read-only WMI query.
    /// </summary>
    private static string? FailedWifiDevice()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass = 'Net' AND ConfigManagerErrorCode <> 0");
            foreach (var device in searcher.Get().Cast<System.Management.ManagementBaseObject>())
            {
                var name = (device["Name"] as string ?? "").Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"Wi-?Fi|Wireless|WLAN|802\.11", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var code = Convert.ToUInt32(device["ConfigManagerErrorCode"], System.Globalization.CultureInfo.InvariantCulture);
                return code == 22
                    ? $"{name} is disabled in Device Manager. Enable it, then press Refresh."
                    : $"{name} has stopped working: Windows could not start it (Device Manager code {code}). Restart the PC, or disable and enable it in Device Manager, then press Refresh.";
            }
        }
        catch (Exception error) when (error is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    private void SetStatus(string text) { StatusText.Text = text; StatusChanged?.Invoke(text); }

    private async Task RefreshAsync(bool scan)
    {
        if (busy || disposed) return;
        busy = true; ScanButton.IsEnabled = false; ControlState.SetIsBusy(ScanButton, true);
        var cancellation = new CancellationTokenSource(); operation = cancellation;
        try
        {
            if (scan) SetStatus("Reading Wi-Fi adapters and nearby networks…");
            var current = await WifiScanner.ReadAsync(cancellation.Token);
            if (disposed || cancellation.IsCancellationRequested) return;
            snapshot = current;
            var adapterId = (AdapterBox.SelectedItem as WifiAdapter)?.Id;
            updating = true;
            AdapterBox.ItemsSource = current.Adapters;
            AdapterBox.SelectedItem = current.Adapters.FirstOrDefault(x => x.Id == adapterId) ?? current.Adapters.FirstOrDefault(x => x.Connection != null) ?? current.Adapters.FirstOrDefault();
            updating = false;
            UpdateTargets();
            var fresh = alignment.Add(current);
            if (fresh) lastFresh = current.CapturedAt;
            RenderSignal(fresh);
            RenderMeasurement();
            // No adapter in the WLAN service usually means the card's driver failed to start; say so instead of a generic hint.
            var failed = current.Adapters.Count == 0 && (scan || current.Notices.Count > 0) ? await Task.Run(FailedWifiDevice) : null;
            if (disposed || cancellation.IsCancellationRequested) return;
            if (scan || current.Notices.Count > 0)
                SetStatus(current.Notices.Count > 0 ? string.Join(" ", current.Notices.Distinct()) : failed is { } device ? device : current.Adapters.Count == 0 ? "No Wi-Fi adapter was found. Check the adapter and its Windows driver." : current.AccessPoints.Count + " access points in Windows' latest results. Windows scans in the background; Sora only reads them, as GCC does.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is Win32Exception or IOException or PlatformNotSupportedException)
        {
            SetStatus(error.Message); host?.Log("Wi-Fi: " + error.Message);
            snapshot = null; lastFresh = null; alignment.Reset();
            WifiOffBanner.Visibility = Visibility.Collapsed; radioCheck.Stop();
            LiveCheck.IsEnabled = MeasureButton.IsEnabled = false;
            updating = true; AdapterBox.ItemsSource = null; TargetBox.ItemsSource = null; updating = false;
            NetworkGrid.ItemsSource = null; ConnectionText.Text = "Wi-Fi details unavailable. Press Refresh after resolving the error.";
            SignalText.Text = "— dBm"; QualityText.Text = "Signal unavailable"; SignalMeter.Value = 0;
            StatisticsText.Text = "No current signal measurements."; displayedSignal = null;
            MinimumText.Text = AverageText.Text = MaximumText.Text = "—"; SignalStateText.Text = "UNAVAILABLE";
            ResultsText.Text = ""; BestText.Text = ""; MeasureButton.Content = "Measure position";
            MeasureText.Text = "Measurement stopped because Wi-Fi details are unavailable. Try again after resolving the error.";
            LiveCheck.IsChecked = false;
            timer.Stop();
            DrawGauge(); DrawChart(); DrawPositionChart(); UpdatePositionVisuals();
        }
        finally
        {
            updating = false; busy = false; ScanButton.IsEnabled = !disposed; ControlState.SetIsBusy(ScanButton, false);
            AdapterBox.IsEnabled = AdapterBox.HasItems;
            TargetBox.IsEnabled = TargetBox.HasItems;
            if (ReferenceEquals(operation, cancellation)) operation = null;
            cancellation.Dispose();
        }
    }

    private void UpdateTargets()
    {
        if (snapshot == null) return;
        var adapter = AdapterBox.SelectedItem as WifiAdapter;
        UpdateRadioWarning(adapter?.RadioOff == true);
        if (adapter == null) LiveCheck.IsEnabled = MeasureButton.IsEnabled = false;
        var entries = snapshot.AccessPoints.Where(p => p.AdapterId == adapter?.Id).ToArray();
        var targets = entries.Select(p => new TargetRow(p, p.Name + " · " + p.Bssid + " · " + p.Band)).ToArray();
        var resolved = adapter == null ? null : alignment.ResolveTarget(adapter.Id, entries, adapter.Connection?.Bssid);
        updating = true;
        TargetBox.ItemsSource = targets;
        TargetBox.SelectedItem = targets.FirstOrDefault(t => t.Point.Bssid == resolved?.Bssid);
        updating = false;
        if (TargetBox.SelectedItem is TargetRow target) SelectTarget(target.Point);
        NetworkGrid.ItemsSource = entries.Select(p => new NetworkRow(p, p.LastSeen == null ? "Unknown" : Math.Max(0, (snapshot.CapturedAt - p.LastSeen.Value).TotalSeconds).ToString("0") + " s ago")).ToArray();
        ConnectionText.Text = adapter == null ? "No Wi-Fi adapter selected." : adapter.Connection is { } c
            ? c.Ssid + " · " + c.Bssid + " · " + c.SignalPercent + "% · receive " + (c.ReceiveKbps / 1000d).ToString("0.#") + " / transmit " + (c.TransmitKbps / 1000d).ToString("0.#") + " Mbps link rate"
            : adapter.Description + " · " + adapter.State + ". Select a nearby access point to inspect cached signal, or connect through Windows for live antenna measurements.";
    }

    private void UpdateRadioWarning(bool radioOff)
    {
        WifiOffBanner.Visibility = radioOff ? Visibility.Visible : Visibility.Collapsed;
        LiveCheck.IsEnabled = MeasureButton.IsEnabled = !radioOff;
        if (radioOff)
        {
            lastFresh = null; LiveCheck.IsChecked = false; timer.Stop();
            if (alignment.MeasuringPosition != null)
            {
                alignment.StopPosition(); MeasurementProgress.Value = 0;
                MeasureText.Text = "Turn on Wi-Fi to measure antenna signal.";
                UpdatePositionVisuals();
            }
            if (IsVisible) radioCheck.Start();
        }
        else radioCheck.Stop();
    }
    private void OpenWifiSettingsClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:network-wifi") { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        { SetStatus("Open Windows Settings → Network & internet → Wi-Fi, then turn Wi-Fi on. " + error.Message); }
    }

    private void SelectTarget(WifiAccessPoint point)
    {
        if (alignment.AdapterId != point.AdapterId || alignment.Bssid != point.Bssid)
        {
            alignment.Select(point.AdapterId, point.Bssid); lastFresh = null; ResultsText.Text = ""; BestText.Text = "";
            MeasureText.Text = "Choose Front, Left, Back or Right to start."; MeasureButton.Content = "Measure position";
            MeasurementProgress.Value = 0; DrawPositionChart(); UpdatePositionVisuals();
        }
    }

    private void RenderSignal(bool fresh)
    {
        if (alignment.Samples.LastOrDefault() is { } sample && lastFresh is { } seen && DateTimeOffset.UtcNow - seen <= TimeSpan.FromSeconds(3))
        {
            SignalText.Text = sample.SignalDbm + " dBm"; QualityText.Text = WifiAlignment.Quality(sample.SignalDbm) + (!timer.IsEnabled ? " · snapshot" : fresh ? " · fresh reading" : " · waiting for update");
            SignalMeter.Value = WifiAlignment.MeterPercent(sample.SignalDbm);
            displayedSignal = sample.SignalDbm; SignalStateText.Text = !timer.IsEnabled ? "SNAPSHOT" : fresh ? "LIVE" : "WAITING";
            MinimumText.Text = alignment.Samples.Min(s => s.SignalDbm).ToString(); AverageText.Text = alignment.Samples.Average(s => s.SignalDbm).ToString("0.0"); MaximumText.Text = alignment.Samples.Max(s => s.SignalDbm).ToString();
            StatisticsText.Text = alignment.Samples.Count + " fresh samples · dBm";
        }
        else
        {
            var cached = snapshot?.AccessPoints.FirstOrDefault(p => p.AdapterId == alignment.AdapterId && p.Bssid == alignment.Bssid);
            SignalText.Text = "— dBm"; SignalMeter.Value = 0;
            displayedSignal = null; SignalStateText.Text = cached == null ? "NO SIGNAL" : "CACHED";
            QualityText.Text = cached == null ? "Target unavailable" : "Cached " + cached.SignalDbm + " dBm · scan again for an update";
            MinimumText.Text = AverageText.Text = MaximumText.Text = "—";
            StatisticsText.Text = "Waiting for fresh measurements · dBm";
        }
        DrawGauge(); DrawChart();
    }

    private void RenderMeasurement()
    {
        if (alignment.MeasuringPosition == null || alignment.StartedAt is not { } start) return;
        var remaining = Math.Max(0, 15 - (DateTimeOffset.UtcNow - start).TotalSeconds);
        MeasurementProgress.Value = 15 - remaining;
        MeasureText.Text = "Measuring " + alignment.MeasuringPosition + " · " + Math.Ceiling(remaining) + " s left · " + alignment.Samples.Count + " fresh samples";
        if (remaining > 0) return;
        try
        {
            // Recovered GCC uses 500 ms readings for 15 s. Require multiple actual readings for a useful comparison.
            if (alignment.Samples.Count < 5 || lastFresh == null || DateTimeOffset.UtcNow - lastFresh.Value > TimeSpan.FromSeconds(3))
                throw new InvalidOperationException("Not enough fresh readings. Connect to the target through Windows and retry this position.");
            var result = alignment.CompletePosition();
            MeasureText.Text = result.Position + " complete · " + result.Count + " samples";
            ResultsText.Text = "Bars show measured averages. Higher dBm is stronger.";
            if (alignment.Results.Count == 4 && alignment.Best is { } best) BestText.Text = "Best position: " + best.Position + " · " + best.AverageDbm.ToString("0.0") + " dBm";
            else BestText.Text = alignment.Results.Count + " of 4 positions measured";
            if (PositionBox.SelectedIndex < 3) PositionBox.SelectedIndex++;
        }
        catch (InvalidOperationException error) { alignment.StopPosition(); MeasureText.Text = error.Message; }
        MeasureButton.Content = "Measure position";
        UpdatePositionVisuals(); DrawPositionChart();
        if (LiveCheck.IsChecked != true) timer.Stop();
    }

    private void DrawChart()
    {
        if (SignalChart == null) return;
        SignalChart.Children.Clear();
        var width = SignalChart.ActualWidth; var height = SignalChart.ActualHeight;
        if (width < 120 || height < 50) return;
        const double left = 38, top = 9, bottom = 24;
        var plotWidth = width - left - 10; var plotHeight = height - top - bottom;
        foreach (var dbm in new[] { -30, -50, -70, -90 })
        {
            var y = top + (1 - WifiAlignment.MeterPercent(dbm) / 100d) * plotHeight;
            SignalChart.Children.Add(new Line { X1 = left, X2 = width - 8, Y1 = y, Y2 = y, Stroke = ResourceBrush("HairlineBrush"), StrokeThickness = 1 });
            AddLabel(SignalChart, dbm.ToString(), 0, y - 7, "SecondaryTextBrush", 10);
        }
        for (var seconds = 60; seconds >= 0; seconds -= 15)
        {
            var x = left + (60 - seconds) / 60d * plotWidth;
            AddLabel(SignalChart, seconds == 0 ? timer.IsEnabled || alignment.Samples.Count == 0 ? "now" : "last" : "−" + seconds + "s", Math.Clamp(x - 12, left, width - 28), height - 16, "TertiaryTextBrush", 10);
        }
        var end = timer.IsEnabled ? DateTimeOffset.UtcNow : alignment.Samples.LastOrDefault()?.At ?? DateTimeOffset.UtcNow;
        var samples = alignment.Samples.Where(s => s.At >= end.AddSeconds(-60) && s.At <= end).ToArray();
        if (samples.Length == 0)
        {
            var empty = AddLabel(SignalChart, "Start monitoring to see live signal history", left + 12, top + plotHeight / 2 - 8, "SecondaryTextBrush", 12);
            empty.MaxWidth = Math.Max(0, plotWidth - 20); return;
        }
        Polyline? line = null; DateTimeOffset? previousAt = null;
        foreach (var sample in samples)
        {
            var point = new Point(left + (sample.At - end.AddSeconds(-60)).TotalSeconds / 60 * plotWidth, top + (1 - WifiAlignment.MeterPercent(sample.SignalDbm) / 100d) * plotHeight);
            if (line == null || previousAt != null && sample.At - previousAt.Value > TimeSpan.FromSeconds(3))
            {
                line = new Polyline { Stroke = ResourceBrush("AccentBrush"), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                SignalChart.Children.Add(line);
            }
            line.Points.Add(point); previousAt = sample.At;
        }
        var last = samples[^1];
        var dot = new Ellipse { Width = 6, Height = 6, Fill = ResourceBrush("AccentBrush") };
        Canvas.SetLeft(dot, left + (last.At - end.AddSeconds(-60)).TotalSeconds / 60 * plotWidth - 3);
        Canvas.SetTop(dot, top + (1 - WifiAlignment.MeterPercent(last.SignalDbm) / 100d) * plotHeight - 3); SignalChart.Children.Add(dot);
    }

    private Brush ResourceBrush(string name) => (Brush)FindResource(name);
    private TextBlock AddLabel(Canvas canvas, string text, double x, double y, string brush, double size = 11)
    {
        var label = new TextBlock { Text = text, Foreground = ResourceBrush(brush), FontSize = size, IsHitTestVisible = false };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label); return label;
    }

    private void DrawGauge()
    {
        if (SignalGauge == null) return;
        SignalGauge.Children.Clear(); var width = SignalGauge.ActualWidth; if (width < 120) return;
        var center = new Point(width / 2, 96); var radius = Math.Min(81, width / 2 - 28);
        Point At(double angle, double r) => new(center.X + Math.Cos(angle * Math.PI / 180) * r, center.Y + Math.Sin(angle * Math.PI / 180) * r);
        void Arc(double fraction, Brush brush, double thickness)
        {
            if (fraction <= 0) return;
            var path = new System.Windows.Shapes.Path { Stroke = brush, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            var geometry = new PathGeometry(); var figure = new PathFigure { StartPoint = At(140, radius) };
            figure.Segments.Add(new ArcSegment(At(140 + 260 * fraction, radius), new Size(radius, radius), 0, fraction * 260 > 180, SweepDirection.Clockwise, true));
            geometry.Figures.Add(figure); path.Data = geometry; SignalGauge.Children.Add(path);
        }
        Arc(1, ResourceBrush("ControlBrush"), 8);
        if (displayedSignal is { } dbm) Arc(WifiAlignment.MeterPercent(dbm) / 100d, ResourceBrush("AccentBrush"), 8);
        for (var index = 0; index <= 14; index++)
        {
            var angle = 140 + index / 14d * 260; var a = At(angle, radius + 11); var b = At(angle, radius + (index % 2 == 0 ? 17 : 14));
            SignalGauge.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = ResourceBrush("TertiaryTextBrush"), StrokeThickness = 1 });
        }
        AddLabel(SignalGauge, "−90", center.X - radius - 10, 163, "TertiaryTextBrush", 10);
        AddLabel(SignalGauge, "−20", center.X + radius - 10, 163, "TertiaryTextBrush", 10);
    }

    private void DrawPositionChart()
    {
        if (PositionChart == null) return;
        AntennaView?.ShowSignals(alignment.Results.ToArray());
        PositionChart.Children.Clear(); var width = PositionChart.ActualWidth; if (width < 160) return;
        var barWidth = Math.Max(30, width - 135);
        for (var i = 0; i < WifiAlignment.Positions.Count; i++)
        {
            var position = WifiAlignment.Positions[i]; var y = i * 35 + 8; var result = alignment.Results.FirstOrDefault(r => r.Position == position);
            AddLabel(PositionChart, position, 0, y + 1, "SecondaryTextBrush");
            var background = new Border { Width = barWidth, Height = 8, CornerRadius = new CornerRadius(4), Background = ResourceBrush("ControlBrush") };
            Canvas.SetLeft(background, 46); Canvas.SetTop(background, y + 6); PositionChart.Children.Add(background);
            if (result == null) { AddLabel(PositionChart, "Not measured", 54 + barWidth, y + 1, "TertiaryTextBrush", 10); continue; }
            var fill = new Border { Width = Math.Max(2, WifiAlignment.MeterPercent((int)Math.Round(result.AverageDbm)) / 100d * barWidth), Height = 8, CornerRadius = new CornerRadius(4), Background = ResourceBrush(alignment.Results.Count == 4 && alignment.Best?.Position == position ? "Good" : "AccentBrush"), ToolTip = result.Count + " samples · min " + result.MinimumDbm + " / max " + result.MaximumDbm + " dBm" };
            Canvas.SetLeft(fill, 46); Canvas.SetTop(fill, y + 6); PositionChart.Children.Add(fill);
            AddLabel(PositionChart, result.AverageDbm.ToString("0.0") + " dBm", 54 + barWidth, y + 1, "TextBrush", 10);
        }
    }

    private void UpdatePositionVisuals()
    {
        if (PositionBox == null || MeasureButton == null || FrontButton == null) return;
        var selected = ((ComboBoxItem)PositionBox.SelectedItem)?.Content?.ToString() ?? "Front";
        var measuring = alignment.MeasuringPosition != null;
        PositionBox.IsEnabled = !measuring;
        var buttons = new[] { FrontButton, LeftButton, BackButton, RightButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            var position = WifiAlignment.Positions[index]; var complete = alignment.Results.Any(r => r.Position == position);
            buttons[index].Content = (complete ? "✓ " : (index + 1) + "  ") + position;
            buttons[index].Background = ResourceBrush(position == selected ? "AccentSubtleBrush" : "ControlBrush");
            buttons[index].BorderBrush = ResourceBrush(position == selected ? "AccentBrush" : "HairlineBrush");
            buttons[index].BorderThickness = new Thickness(1); buttons[index].IsEnabled = !measuring;
        }
        MeasureButton.Content = measuring ? "Cancel measurement" : "Measure " + selected;
        if (PositionHeading != null) PositionHeading.Text = "Position " + (PositionBox.SelectedIndex + 1) + " · " + selected;
    }

    private void PositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AntennaView == null || PositionBox?.SelectedItem is not ComboBoxItem item || MeasureButton == null) return;
        AntennaView.SetPosition(item.Content.ToString()!); UpdatePositionVisuals();
    }
    private void PositionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && alignment.MeasuringPosition == null) PositionBox.SelectedIndex = WifiAlignment.Positions.ToList().IndexOf((string)button.Tag);
    }
    private void ResetViewClick(object sender, RoutedEventArgs e)
    {
        AntennaView.ResetView(); if (PositionBox.SelectedItem is ComboBoxItem item) AntennaView.SetPosition(item.Content.ToString()!);
    }
    private void GaugeSizeChanged(object sender, SizeChangedEventArgs e) => DrawGauge();
    private void PositionChartSizeChanged(object sender, SizeChangedEventArgs e) => DrawPositionChart();

    internal void ShowComparisonPreview()
    {
        UpdateLayout();
        var top = PositionSection.TransformToAncestor(WifiScroll).Transform(new Point()).Y + WifiScroll.VerticalOffset;
        WifiScroll.ScrollToVerticalOffset(Math.Max(0, top - 12));
    }

    private async void ScanClick(object sender, RoutedEventArgs e) => await ReadAsync();
    private void AdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating) return;
        alignment.Select(Guid.Empty, ""); lastFresh = null; UpdateTargets(); RenderSignal(false);
    }
    private void TargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating) return;
        if (TargetBox.SelectedItem is TargetRow target) { SelectTarget(target.Point); RenderSignal(false); }
    }
    private void LiveChanged(object sender, RoutedEventArgs e)
    {
        if (LiveCheck == null) return;
        if (LiveCheck.IsChecked == true) { liveStarted = DateTimeOffset.UtcNow; timer.Start(); }
        else { liveStarted = null; if (alignment.MeasuringPosition == null) timer.Stop(); }
    }
    private void MeasureClick(object sender, RoutedEventArgs e)
    {
        if (alignment.MeasuringPosition != null) { alignment.StopPosition(); UpdatePositionVisuals(); MeasureText.Text = "Measurement cancelled."; MeasurementProgress.Value = 0; if (LiveCheck.IsChecked != true) timer.Stop(); return; }
        if (TargetBox.SelectedItem is not TargetRow target) { MeasureText.Text = "Select an access point first."; return; }
        var connection = snapshot?.Adapters.FirstOrDefault(a => a.Id == target.Point.AdapterId)?.Connection;
        if (connection?.Bssid != target.Point.Bssid)
        { MeasureText.Text = "Connect to this access point through Windows for live antenna measurements. Scanning alone provides cached snapshots."; return; }
        alignment.BeginPosition(((ComboBoxItem)PositionBox.SelectedItem).Content.ToString()!, DateTimeOffset.UtcNow);
        lastFresh = null; UpdatePositionVisuals(); timer.Start(); RenderMeasurement();
    }
    private void ResetClick(object sender, RoutedEventArgs e)
    {
        alignment.Reset(); lastFresh = null; ResultsText.Text = ""; BestText.Text = ""; MeasureText.Text = "Choose Front, Left, Back or Right to start.";
        MeasurementProgress.Value = 0; PositionBox.SelectedIndex = 0; UpdatePositionVisuals(); DrawPositionChart(); if (LiveCheck.IsChecked != true) timer.Stop(); RenderSignal(false);
    }
    private void ExportClick(object sender, RoutedEventArgs e)
    {
        if (snapshot == null) { SetStatus("Scan networks before exporting."); return; }
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "sora-wifi-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv" };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, WifiAlignment.ExportCsv(snapshot), new UTF8Encoding(true)); SetStatus("Wi-Fi snapshot saved."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { SetStatus(error.Message); }
    }
    private void ChartSizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();
    private void StopWatching()
    {
        timer.Stop(); radioCheck.Stop(); operation?.Cancel(); liveStarted = null;
        if (LiveCheck != null) LiveCheck.IsChecked = false;
        if (alignment.MeasuringPosition != null) { alignment.StopPosition(); UpdatePositionVisuals(); MeasureText.Text = "Measurement stopped while this page was hidden."; MeasurementProgress.Value = 0; }
    }
    public void Dispose() { if (disposed) return; disposed = true; StopWatching(); }
    private sealed record TargetRow(WifiAccessPoint Point, string Label);
    private sealed record NetworkRow(WifiAccessPoint Point, string Age)
    {
        public string Name => Point.Name; public string Bssid => Point.Bssid; public int SignalDbm => Point.SignalDbm;
        public string Band => Point.Band; public string ChannelLabel => Point.ChannelLabel; public string Radio => Point.Radio; public string Security => Point.Security;
    }
}
