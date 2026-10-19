using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sora.Core;

namespace Sora.App;

// A view of SoraHost. It is created when opened and destroyed when closed to the tray.
public partial class MainWindow : Window
{
    private readonly SoraHost host;
    private readonly string? screenshotPath;
    private ComponentGallery? componentGallery;
    private Notice? developmentReadOnlyNotice;
    private bool initialized, closingForTray, shuttingDown;

    internal MainWindow(SoraHost host, string? screenshotPath, string page = "display")
    {
        this.host = host;
        this.screenshotPath = screenshotPath;
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionLabel.Text = version == null ? "Sora" : $"Sora {version.Major}.{version.Minor}" + (version.Build > 0 ? "." + version.Build : "");
        PreviewKeyDown += (_, e) =>
        {
            if (System.Windows.Input.Keyboard.Modifiers != ModifierKeys.Control) return;
            var target = e.Key switch { Key.D1 or Key.NumPad1 => NavDisplay, Key.D2 or Key.NumPad2 => NavKeyboard, Key.D3 or Key.NumPad3 => NavLighting, Key.D4 or Key.NumPad4 => NavFans, Key.D5 or Key.NumPad5 => NavGpu, Key.D6 or Key.NumPad6 => NavStorage, Key.D7 or Key.NumPad7 => NavWifi, Key.D8 or Key.NumPad8 => NavUpdates, Key.D9 or Key.NumPad9 => NavLogitech, Key.D0 or Key.NumPad0 => NavSettings, _ => null };
            if (target != null) { target.IsChecked = true; e.Handled = true; }
        };
        Lighting.Attach(host.Lighting);
        Fans.Attach(host.Fans);
        GpuFans.Attach(host.GpuFans);
        Storage.Attach(host);
        Wifi.Attach(host);
        Logitech.Attach(host.Logitech, host.LogitechLighting);
        var componentPreview = screenshotPath != null && Array.IndexOf(Environment.GetCommandLineArgs(), "--components") >= 0;
        if (componentPreview) ShowComponentPreview();
        else (page switch { "keyboard" => NavKeyboard, "lighting" => NavLighting, "fans" => NavFans, "gpu" => NavGpu, "ssd" => NavStorage, "wifi" => NavWifi, "updates" => NavUpdates, "logitech" => NavLogitech, "settings" => NavSettings, _ => NavDisplay }).IsChecked = true;
        if (screenshotPath != null) { WindowStartupLocation = WindowStartupLocation.Manual; Left = -12000; ShowInTaskbar = false; }
        var settings = host.Settings;
        FahrenheitCheck.IsChecked = settings.Fahrenheit;
        ZerosCheck.IsChecked = settings.LeadingZeros;
        TrayCheck.IsChecked = settings.MinimizeToTray;
        AutoDisplayCheck.IsChecked = settings.AutoStartDisplay;
        StartupCheck.IsChecked = SafeStartupEnabled();
        initialized = true;
        host.Updated += Render;
        host.Watched = true;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => { host.Updated -= Render; host.Watched = false; Wifi.Dispose(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && host.Settings.MinimizeToTray && screenshotPath == null) ((App)Application.Current).CloseWindowToTray(); };
        Render();
    }

    // Unsaved or in-flight keyboard edits must survive closing to the tray.
    internal bool KeepAliveWhenHidden => Keyboard.IsBusy || Keyboard.HasPendingEdits || Lighting.HasPendingEdits || host.Lighting.Busy || Fans.HasPendingEdits || host.Fans.Busy || GpuFans.HasPendingEdits || GpuFans.IsBusy || Logitech.HasPendingEdits || Logitech.IsBusy;

    private bool SafeStartupEnabled()
    {
        var inspection = StartupTask.InspectForLaunch();
        StartupRouteDetails.Text = inspection.Message;
        StartupCheck.IsEnabled = inspection.Readable;
        return inspection.Task?.Enabled == true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await host.TickAsync();
        if (NavKeyboard.IsChecked == true) await Keyboard.ReadAsync();
        if (NavFans.IsChecked == true) await Fans.ReadAsync();
        if (NavGpu.IsChecked == true) await GpuFans.ReadAsync();
        if (NavStorage.IsChecked == true) await Storage.ReadAsync();
        if (NavWifi.IsChecked == true) await Wifi.ReadAsync();
        if (NavLogitech.IsChecked == true) await Logitech.ReadAsync();
        // Read lighting state when its page is opened.
        if (!Lighting.HasState && NavLighting.IsChecked == true) await Lighting.ReadAsync();
        if (screenshotPath == null) return;
        // Record the actual font files selected by WPF; a missing packaged font can silently fall back.
        var fontFiles = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var weight in new[] { FontWeights.Light, FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold })
        {
            var face = new Typeface((FontFamily)FindResource("UIFont"), FontStyles.Normal, weight, FontStretches.Normal);
            fontFiles[weight.ToString()] = face.TryGetGlyphTypeface(out var glyph) ? glyph.FontUri.ToString() : "Unresolved";
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!);
        File.WriteAllText(screenshotPath + ".fonts.json", JsonSerializer.Serialize(fontFiles, new JsonSerializerOptions { WriteIndented = true }));
        var arguments = Environment.GetCommandLineArgs();
        if (Array.IndexOf(arguments, "--settings-navigation-check") >= 0)
            File.WriteAllText(screenshotPath + ".persisted-navigation.json", JsonSerializer.Serialize(await RunActualSettingsNavigationChecksAsync(), new JsonSerializerOptions { WriteIndented = true }));
        if (Array.IndexOf(arguments, "--settings-preference-check") >= 0)
        {
            try { File.WriteAllText(screenshotPath + ".settings-checks.json", JsonSerializer.Serialize(await RunSettingsPreferenceChecksAsync(), new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception error) { File.WriteAllText(screenshotPath + ".settings-checks.json", JsonSerializer.Serialize(new { error = error.ToString() })); }
        }
        if (Array.IndexOf(arguments, "--components") >= 0)
        {
            var gallery = ShowComponentPreview();
            if (Array.IndexOf(arguments, "--component-ui-check") >= 0)
            {
                var noFalseSelection = true;
                foreach (var entry in NavigationEntries())
                {
                    entry.Button.ApplyTemplate();
                    noFalseSelection &= entry.Button.IsChecked != true
                        && ((FrameworkElement)entry.Button.Template.FindName("SelectionMarker", entry.Button)).Visibility == Visibility.Collapsed
                        && !entry.Button.IsKeyboardFocused;
                }
                var routes = new[] { (NavDisplay, (UIElement)DisplayPage), (NavKeyboard, (UIElement)Keyboard), (NavLighting, (UIElement)Lighting), (NavFans, (UIElement)Fans), (NavGpu, (UIElement)GpuFans), (NavStorage, (UIElement)Storage), (NavWifi, (UIElement)Wifi), (NavUpdates, (UIElement)Updates), (NavLogitech, (UIElement)Logitech), (NavSettings, (UIElement)SettingsPage) };
                var routeResults = new System.Collections.Generic.List<object>();
                var productionNavigation = true;
                foreach (var (button, page) in routes)
                {
                    button.IsChecked = true;
                    var selectedCount = 0; var visibleCount = 0;
                    foreach (var (candidate, target) in routes) { if (candidate.IsChecked == true) selectedCount++; if (target.Visibility == Visibility.Visible) visibleCount++; }
                    var matches = componentGallery == null && page.Visibility == Visibility.Visible && button.IsChecked == true && selectedCount == 1 && visibleCount == 1 && !page.IsEnabled && developmentReadOnlyNotice != null;
                    productionNavigation &= matches; routeResults.Add(new { Navigation = button.Name, MatchingPage = ((FrameworkElement)page).Name, passed = matches });
                }
                gallery = ShowComponentPreview();
                File.WriteAllText(screenshotPath + ".route-checks.json", JsonSerializer.Serialize(new { noFalseSelection, productionNavigation, routes = routeResults, returnedToDevelopmentPreview = componentGallery != null, previewDataIsolated = SoraData.IsDevelopmentPreview }));
                try { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(await gallery.RunChecksAsync(screenshotPath), new JsonSerializerOptions { WriteIndented = true })); }
                catch (Exception error) { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(new { error = error.ToString() })); }
            }
            if (Array.IndexOf(arguments, "--components-lower-preview") >= 0) gallery.ShowLowerPreview();
        }
        var scaleIndex = Array.IndexOf(arguments, "--layout-scale");
        if (scaleIndex >= 0 && scaleIndex + 1 < arguments.Length && double.TryParse(arguments[scaleIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var layoutScale) && layoutScale >= 1 && layoutScale <= 2)
            ((FrameworkElement)Content).LayoutTransform = new ScaleTransform(layoutScale, layoutScale);
        if (Array.IndexOf(arguments, "--logitech-ui-check") >= 0)
        {
            try { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(Logitech.RunEditorChecks(), new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception error) { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(new { error = error.ToString() })); }
        }
        var logitechSectionIndex = Array.IndexOf(arguments, "--logitech-section");
        if (Array.IndexOf(arguments, "--logitech-rgb-check") >= 0)
            File.WriteAllText(screenshotPath + ".rgb-checks.json", JsonSerializer.Serialize(await Logitech.CheckRgbInteractionAsync(), new JsonSerializerOptions { WriteIndented = true }));
        if (logitechSectionIndex >= 0 && logitechSectionIndex + 1 < arguments.Length) Logitech.ShowSection(arguments[logitechSectionIndex + 1]);
        if (Array.IndexOf(arguments, "--logitech-layout-check") >= 0)
            File.WriteAllText(screenshotPath + ".layout-checks.json", JsonSerializer.Serialize(Logitech.CheckPinnedLayout(), new JsonSerializerOptions { WriteIndented = true }));
        if (Array.IndexOf(arguments, "--fan-ui-check") >= 0 && Array.IndexOf(arguments, "--fan-preview") >= 0)
        {
            try { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(await Fans.RunEditorChecksAsync(), new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception error) { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(new { error = error.ToString() })); }
        }
        if (Array.IndexOf(arguments, "--fan-live-refresh-check") >= 0)
        {
            try { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(await Fans.RunLiveRefreshChecksAsync(), new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception error) { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(new { error = error.ToString() })); }
        }
        if (Array.IndexOf(arguments, "--keyboard-ui-check") >= 0)
        {
            try { var checks = Keyboard.RunEditorChecks(); File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception error) { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(new { error = error.ToString() })); }
        }
        if (Array.IndexOf(arguments, "--keyboard-assignment-preview") >= 0) Keyboard.PreparePreview("assignment");
        if (Array.IndexOf(arguments, "--keyboard-paint-preview") >= 0) Keyboard.PreparePreview("paint");
        if (Array.IndexOf(arguments, "--keyboard-blue-select-preview") >= 0) Keyboard.PreparePreview("paint-blue");
        if (Array.IndexOf(arguments, "--keyboard-select-preview") >= 0) Keyboard.PreparePreview("select");
        var keyboardSectionIndex = Array.IndexOf(arguments, "--keyboard-section");
        if (keyboardSectionIndex >= 0 && keyboardSectionIndex + 1 < arguments.Length) Keyboard.ShowSection(arguments[keyboardSectionIndex + 1]);
        if (Array.IndexOf(arguments, "--sidebar-focus-preview") >= 0) NavSettings.Focus();
        if (Array.IndexOf(arguments, "--gpu-curve-preview") >= 0 && NavGpu.IsChecked == true) GpuFans.PrepareCurvePreview();
        if (Array.IndexOf(arguments, "--fan-controls-preview") >= 0) { Fans.ShowLowerControlsPreview(); UpdateLayout(); }
        if (Array.IndexOf(arguments, "--wifi-comparison-preview") >= 0) { Wifi.ShowComparisonPreview(); UpdateLayout(); }
        if (Array.IndexOf(arguments, "--fan-radiator-preview") >= 0) { Fans.ShowRadiatorHeaderPreview(); UpdateLayout(); }
        if (Array.IndexOf(arguments, "--screen-ui-check") >= 0)
        {
            var activePage = (FrameworkElement?)componentGallery ?? ((Grid)DisplayPage.Parent).Children.OfType<FrameworkElement>().First(p => p.IsVisible);
            File.WriteAllText(screenshotPath + ".screen-checks.json", JsonSerializer.Serialize(ProductionScreenChecks.Run(activePage), new JsonSerializerOptions { WriteIndented = true }));
        }
        if (Array.IndexOf(arguments, "--ssd-ui-check") >= 0)
            File.WriteAllText(screenshotPath + ".ssd-checks.json", JsonSerializer.Serialize(SsdPanel.RunEmptyStateChecks(), new JsonSerializerOptions { WriteIndented = true }));
        if (Array.IndexOf(arguments, "--interaction-check") >= 0)
            File.WriteAllText(screenshotPath + ".interaction-checks.json", JsonSerializer.Serialize(await InteractionChecks.RunAsync(this), new JsonSerializerOptions { WriteIndented = true }));
        if (Array.IndexOf(arguments, "--interaction-audit") >= 0)
        {
            var page = (FrameworkElement?)componentGallery ?? ((Grid)DisplayPage.Parent).Children.OfType<FrameworkElement>().First(p => p.IsVisible);
            File.WriteAllText(screenshotPath + ".accessibility.json", JsonSerializer.Serialize(InteractionChecks.Audit(page), new JsonSerializerOptions { WriteIndented = true }));
            if (Array.IndexOf(arguments, "--desktop-check") >= 0)
                File.WriteAllText(screenshotPath + ".resize-checks.json", JsonSerializer.Serialize(await InteractionChecks.CheckResizeAsync(this, page), new JsonSerializerOptions { WriteIndented = true }));
            if (Array.IndexOf(arguments, "--interaction-focus-preview") >= 0)
                (InteractionChecks.Descendants(page).OfType<TabItem>().FirstOrDefault(t => t.IsVisible && t.IsSelected)
                    ?? (UIElement?)InteractionChecks.Descendants(page).OfType<ComboBox>().FirstOrDefault(c => c.IsVisible && c.IsEnabled))?.Focus();
        }
        var lightingView = Array.IndexOf(arguments, "--lighting-view");
        if (lightingView >= 0 && lightingView + 1 < arguments.Length) Lighting.ShowVerificationView(arguments[lightingView + 1]);
        if (Array.IndexOf(arguments, "--lighting-reach-check") >= 0)
            File.WriteAllText(screenshotPath + ".reach-checks.json", JsonSerializer.Serialize(Lighting.CheckControlReachability(), new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(screenshotPath + ".ready", "ready");
        await Task.Delay(3500);
        await host.TickAsync();
        UpdateLayout();
        var navigation = new[] { NavDisplay, NavFans, NavGpu, NavKeyboard, NavLighting, NavLogitech, NavStorage, NavWifi, NavUpdates, NavSettings };
        var geometry = new System.Collections.Generic.List<object>();
        foreach (var row in navigation)
        {
            var grid = (Grid)row.Content;
            var label = (TextBlock)grid.Children[1];
            var icon = (FrameworkElement)grid.Children[0];
            geometry.Add(new { row.Name, row.ActualHeight, row.FontSize, Weight = label.FontWeight.ToString(), Selected = row.IsChecked, LabelX = label.TranslatePoint(new Point(), this).X, IconX = icon.TranslatePoint(new Point(), this).X, IconWidth = icon.ActualWidth, FocusOutline = (row.Template.FindName("FocusOutline", row) as FrameworkElement)?.Visibility.ToString() });
        }
        File.WriteAllText(screenshotPath + ".navigation.json", JsonSerializer.Serialize(new { DevelopmentPreview = componentGallery != null, PreviewDataIsolated = SoraData.IsDevelopmentPreview, DataDirectory = host.DataDirectory, Dpi = VisualTreeHelper.GetDpi(this).PixelsPerInchX, Rows = geometry }, new JsonSerializerOptions { WriteIndented = true }));
        var client = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)(client.ActualWidth + client.Margin.Left + client.Margin.Right), (int)(client.ActualHeight + client.Margin.Top + client.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!);
        using (var file = File.Create(screenshotPath)) encoder.Save(file);
        await ((App)Application.Current).ExitAsync();
    }

    private ComponentGallery ShowComponentPreview()
    {
        foreach (var entry in NavigationEntries()) entry.Button.IsChecked = false;
        var content = (Grid)DisplayPage.Parent;
        if (componentGallery != null) content.Children.Remove(componentGallery);
        if (developmentReadOnlyNotice != null) { content.Children.Remove(developmentReadOnlyNotice); developmentReadOnlyNotice = null; }
        content.RowDefinitions.Clear();
        foreach (UIElement page in content.Children) { page.Visibility = Visibility.Collapsed; Grid.SetRow(page, 0); }
        componentGallery = new ComponentGallery(); content.Children.Add(componentGallery); UpdateLayout();
        // RadioButton selects itself when it receives initial keyboard focus. Keep startup
        // focus in the preview rather than letting Windows activate the first sidebar row.
        FocusManager.SetFocusedElement(this, componentGallery);
        System.Windows.Input.Keyboard.Focus(componentGallery);
        return componentGallery;
    }

    private void ShowReadOnlyDevelopmentPage()
    {
        var content = (Grid)DisplayPage.Parent;
        foreach (UIElement page in content.Children)
        {
            if (ReferenceEquals(page, developmentReadOnlyNotice)) continue;
            page.IsEnabled = false; Grid.SetRow(page, 1);
        }
        if (developmentReadOnlyNotice != null) return;
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        developmentReadOnlyNotice = new Notice { Tone = NoticeTone.Information, Content = "Read-only development preview. Open Sora normally to control devices or change settings." };
        developmentReadOnlyNotice.SetResourceReference(MarginProperty, "SectionSpacing");
        content.Children.Add(developmentReadOnlyNotice);
    }

    private void Render()
    {
        var sample = host.Fresh;
        var unit = host.Unit;
        var sensors = host.Sensors;
        var status = host.Status;
        var requested = host.Requested;

        // Readings: only a fresh sample from live sensors is shown; otherwise dashes.
        var format = host.Settings.LeadingZeros ? "000" : "0";
        TemperatureValue.Text = sample == null ? "–" : host.FormatTemperature(sample, "0");
        UsageValue.Text = sample == null ? "--" : Math.Truncate(sample.UsagePercent).ToString("0", CultureInfo.InvariantCulture);
        PowerValue.Text = sample == null ? "--" : Math.Truncate(sample.PowerW).ToString("0", CultureInfo.InvariantCulture);
        TemperatureValue.Foreground = UsageValue.Foreground = PowerValue.Foreground = (Brush)FindResource(sample == null ? "TertiaryTextBrush" : "TextBrush");
        TemperatureUnavailableHint.Text = sensors.State == SensorState.Unavailable ? "CPU sensor data is unavailable" : host.Latest != null ? "Waiting for a fresh reading" : "Waiting for CPU sensor data";
        TemperatureUnavailableHint.Visibility = sample == null ? Visibility.Visible : Visibility.Collapsed;
        TemperatureUnit.Text = "\u2009" + unit;
        CpuLabel.Text = host.CpuName ?? "CPU package";
        LastUpdate.Text = sample != null ? "Updated " + sample.ReceivedAt.ToLocalTime().ToString("HH:mm:ss")
            : sensors.State == SensorState.Unavailable ? "No readings · sensors unavailable"
            : host.Latest != null ? "Readings are stale" : "Waiting for the first reading";
        PreviewTemperature.Text = (sample == null ? "—" : host.FormatTemperature(sample, format)) + " " + unit;
        PreviewUsage.Text = (sample == null ? "—" : Math.Truncate(sample.UsagePercent).ToString(format, CultureInfo.InvariantCulture)) + " %  USAGE";
        PreviewPower.Text = (sample == null ? "—" : Math.Truncate(sample.PowerW).ToString(format, CultureInfo.InvariantCulture)) + " W  POWER";
        PreviewCaption.Text = status.Sending ? "On the cooler now" : "Preview · not being sent";

        // Status rows: sensors, the physical cooler and the output are independent.
        var (sensorValue, sensorTone, sensorDetail) = sensors.State switch
        {
            SensorState.Live => ("Live", "Good", "LibreHardwareMonitor via PawnIO, read every second."),
            SensorState.Starting => ("Starting", "Idle", "Opening the sensor library."),
            _ => sensors.Problem switch
            {
                SensorProblem.Permission => ("No access", "Bad", "Sora isn't running as administrator, so CPU registers can't be read. Open Sora from its shortcut and accept the Windows prompt."),
                SensorProblem.DriverMissing => ("Driver missing", "Bad", "Install the PawnIO driver, then restart Sora."),
                SensorProblem.Unsupported => ("Not supported", "Bad", sensors.Message),
                _ => ("No reading", "Bad", sensors.Message + (sensors.Detail == null ? "" : " " + sensors.Detail)),
            },
        };
        SetRow(SensorValue, SensorDot, SensorDetail, sensorValue, sensorTone, sensorDetail);
        var (coolerValue, coolerTone, coolerDetail) = status.Sending || host.CoolerPresent == true ? ("Connected", "Good", "Plugged in over USB.")
            : host.CoolerPresent == false ? ("Not found", "Warn", "Check the LD360's USB cable to the motherboard header.")
            : ("Checking", "Idle", "Looking for the cooler on USB.");
        SetRow(CoolerValue, CoolerDot, CoolerDetail, coolerValue, coolerTone, coolerDetail);
        var (outputValue, outputTone, outputDetail) = status.Sending ? ("Sending", "Good", status.Detail)
            : requested ? (status.Title, "Warn", status.Detail)
            : ("Off", "Idle", host.Conflict ?? "Press Start display to show readings on the cooler.");
        SetRow(OutputValue, OutputDot, OutputDetail, outputValue, outputTone, outputDetail);

        // The active page names the output state.
        StartButton.Content = requested ? "Stop display" : "Start display";
        DisplayDot.Fill = (Brush)FindResource(outputTone);
        DeviceLabel.Text = status.Sending ? "Sending to LD360" : requested ? status.Title : "Display off";
        StartupHint.Visibility = StartupCheck.IsChecked == true && AutoDisplayCheck.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetRow(TextBlock value, System.Windows.Shapes.Ellipse dot, TextBlock detail, string text, string tone, string description)
    {
        value.Text = text; dot.Fill = (Brush)FindResource(tone); detail.Text = description;
    }

    // Keep the native Windows caption, resize frame and system buttons in step with the light client surface.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        int dark = 0;
        int caption = ToColorRef((Color)FindResource("WindowColor"));
        int text = ToColorRef((Color)FindResource("WindowCaptionTextColor"));
        int border = ToColorRef((Color)FindResource("WindowBorderColor"));
        int corners = 2;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }
    private static int ToColorRef(Color color) => color.R | color.G << 8 | color.B << 16;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private (RadioButton Button, string Terms)[] NavigationEntries() => new (RadioButton, string)[]
    {
        (NavDisplay, "cooler display deepcool ld360 "),
        (NavKeyboard, "keyboard kreo "),
        (NavLighting, "lighting rgb memory motherboard "),
        (NavFans, "fan control motherboard "),
        (NavGpu, "gpu fans graphics card "),
        (NavStorage, "ssd tool storage drive health smart "),
        (NavWifi, "wi-fi wifi antenna wireless "),
        (NavUpdates, "updates bios drivers "),
        (NavLogitech, "logitech mouse g502 "),
        (NavSettings, "settings app startup tray windows task scheduler")
    };

    private void NavSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (NavSearchBox == null || NavEmptyState == null) return;
        var query = NavSearchBox.Text.Trim();
        var matches = 0;
        foreach (var entry in NavigationEntries())
        {
            var visible = query.Length == 0 || entry.Terms.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            entry.Button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) matches++;
        }
        NavSearchPlaceholder.Visibility = NavSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NavSearchClear.Visibility = NavSearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        NavEmptyState.Visibility = matches == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (NavSearchBox.Text.Length > 0) { NavSearchBox.Clear(); e.Handled = true; }
            return;
        }
        if (e.Key != Key.Enter || NavSearchBox.Text.Trim().Length == 0) return;
        foreach (var entry in NavigationEntries())
        {
            if (entry.Button.Visibility != Visibility.Visible) continue;
            entry.Button.IsChecked = true;
            NavSearchBox.Clear();
            entry.Button.Focus();
            e.Handled = true;
            return;
        }
    }

    private void ClearNavSearch(object sender, RoutedEventArgs e)
    {
        NavSearchBox.Clear();
        NavSearchBox.Focus();
    }

    private async void NavChanged(object sender, RoutedEventArgs e)
    {
        if (DisplayPage == null || SettingsPage == null || Keyboard == null || Lighting == null || Fans == null || GpuFans == null || Storage == null || Wifi == null || Updates == null || Logitech == null) return;
        if (componentGallery != null)
        {
            ((Panel)componentGallery.Parent).Children.Remove(componentGallery);
            componentGallery = null;
        }
        if (NavSearchBox != null && NavSearchBox.Text.Length > 0) NavSearchBox.Clear();
        SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DisplayPage.Visibility = NavDisplay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Keyboard.Visibility = NavKeyboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Lighting.Visibility = NavLighting.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Fans.Visibility = NavFans.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GpuFans.Visibility = NavGpu.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Storage.Visibility = NavStorage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Wifi.Visibility = NavWifi.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Updates.Visibility = NavUpdates.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Logitech.Visibility = NavLogitech.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        foreach (var page in ((Grid)DisplayPage.Parent).Children.OfType<FrameworkElement>())
            if (page.IsVisible) InteractionMotion.RevealNow(page);
        if (SoraData.IsDevelopmentPreview) { ShowReadOnlyDevelopmentPage(); return; }
        if (IsLoaded && NavFans.IsChecked == true) await Fans.ReadAsync();
        if (IsLoaded && NavGpu.IsChecked == true) await GpuFans.ReadAsync();
        if (IsLoaded && NavStorage.IsChecked == true) await Storage.ReadAsync();
        if (IsLoaded && NavWifi.IsChecked == true) await Wifi.ReadAsync();
        if (IsLoaded && NavLogitech.IsChecked == true) await Logitech.ReadAsync();
        if (IsLoaded && NavKeyboard.IsChecked == true && !Keyboard.HasSnapshot && !Keyboard.IsBusy) await Keyboard.ReadAsync();
        if (IsLoaded && NavLighting.IsChecked == true && !Lighting.HasState) await Lighting.ReadAsync();
    }

    private async void ToggleDisplay(object sender, RoutedEventArgs e) => await host.SetRequestedAsync(!host.Requested);

    private async void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        await host.SaveSettingsAsync(new(FahrenheitCheck.IsChecked == true, ZerosCheck.IsChecked == true, TrayCheck.IsChecked == true, AutoDisplayCheck.IsChecked == true));
        Render();
    }

    private async void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        var enable = StartupCheck.IsChecked == true;
        try
        {
            StartupCheck.IsEnabled = false;
            await Task.Run(() => { if (enable) StartupTask.Enable(); else StartupTask.Disable(); });
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            host.Log("Startup task: " + error.Message);
            initialized = false; StartupCheck.IsChecked = !enable; initialized = true;
            AppDialog.ShowMessage(this, "Start with Windows", error.Message);
        }
        finally { initialized = false; StartupCheck.IsChecked = SafeStartupEnabled(); initialized = true; Render(); }
    }

    private void OpenTaskScheduler(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "taskschd.msc")) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        {
            AppDialog.ShowMessage(this, "Task Scheduler", error.Message);
        }
    }

    private void ToggleSensorInfo(object sender, RoutedEventArgs e)
    {
        var show = SensorInfo.Visibility != Visibility.Visible;
        SensorInfo.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) InteractionMotion.RevealNow(SensorInfo);
        System.Windows.Automation.AutomationProperties.SetHelpText(SensorInfoButton, show ? "Sensor details expanded" : "Sensor details collapsed");
        SensorInfoButton.Content = show ? "Hide details" : "About sensors";
    }

    // WPF forbids Close/Hide inside Closing, so the close either proceeds (window rebuilt later) or is deferred.
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (shuttingDown || closingForTray) return;
        if (host.Settings.MinimizeToTray && screenshotPath == null)
        {
            if (!KeepAliveWhenHidden) return;
            e.Cancel = true;
            Dispatcher.BeginInvoke(Hide);
            return;
        }
        e.Cancel = true;
        Dispatcher.BeginInvoke(() => _ = ((App)Application.Current).ExitAsync());
    }

    internal void CloseForTray() { closingForTray = true; Close(); }

    // Waits for any keyboard write to finish before the app exits.
    internal async Task ShutdownAsync()
    {
        shuttingDown = true;
        Keyboard.IsEnabled = false;
        await Keyboard.PendingOperation;
        Close();
    }
}
