using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
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
            var target = e.Key switch { Key.D1 or Key.NumPad1 => NavDisplay, Key.D2 or Key.NumPad2 => NavKeyboard, _ => null };
            if (target != null) { target.IsChecked = true; e.Handled = true; }
        };
        (page == "keyboard" ? NavKeyboard : NavDisplay).IsChecked = true;
        if (screenshotPath != null) { WindowStartupLocation = WindowStartupLocation.Manual; Left = -12000; ShowInTaskbar = false; }
        var settings = host.Settings;
        FahrenheitCheck.IsChecked = settings.Fahrenheit;
        ZerosCheck.IsChecked = settings.LeadingZeros;
        TrayCheck.IsChecked = settings.MinimizeToTray;
        AutoDisplayCheck.IsChecked = settings.AutoStartDisplay;
        StartupCheck.IsChecked = screenshotPath == null && SafeStartupEnabled();
        initialized = true;
        host.Updated += Render;
        host.Watched = true;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => { host.Updated -= Render; host.Watched = false; };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && host.Settings.MinimizeToTray && screenshotPath == null) ((App)Application.Current).CloseWindowToTray(); };
        Render();
    }

    // Unsaved or in-flight keyboard edits must survive closing to the tray.
    internal bool KeepAliveWhenHidden => Keyboard.IsBusy || Keyboard.HasPendingEdits;

    private bool SafeStartupEnabled()
    {
        try { return StartupTask.IsEnabled; }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { host.Log("Startup task query: " + error.Message); return false; }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await host.TickAsync();
        if (NavKeyboard.IsChecked == true) await Keyboard.ReadAsync();
        if (screenshotPath == null) return;
        var arguments = Environment.GetCommandLineArgs();
        if (Array.IndexOf(arguments, "--keyboard-ui-check") >= 0)
        {
            try { var checks = Keyboard.RunEditorChecks(); File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception error) { File.WriteAllText(screenshotPath + ".checks.json", JsonSerializer.Serialize(new { error = error.ToString() })); }
        }
        if (Array.IndexOf(arguments, "--keyboard-assignment-preview") >= 0) Keyboard.PreparePreview("assignment");
        if (Array.IndexOf(arguments, "--keyboard-paint-preview") >= 0) Keyboard.PreparePreview("paint");
        if (Array.IndexOf(arguments, "--keyboard-select-preview") >= 0) Keyboard.PreparePreview("select");
        await Task.Delay(3500);
        await host.TickAsync();
        UpdateLayout();
        var client = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)(client.ActualWidth + client.Margin.Left + client.Margin.Right), (int)(client.ActualHeight + client.Margin.Top + client.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!);
        using (var file = File.Create(screenshotPath)) encoder.Save(file);
        await ((App)Application.Current).ExitAsync();
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
        TemperatureValue.Text = sample == null ? "--" : host.FormatTemperature(sample, "0");
        UsageValue.Text = sample == null ? "--" : Math.Truncate(sample.UsagePercent).ToString("0", CultureInfo.InvariantCulture);
        PowerValue.Text = sample == null ? "--" : Math.Truncate(sample.PowerW).ToString("0", CultureInfo.InvariantCulture);
        TemperatureValue.Foreground = UsageValue.Foreground = PowerValue.Foreground = (Brush)FindResource(sample == null ? "TertiaryTextBrush" : "TextBrush");
        TemperatureUnit.Text = unit;
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

        // Header chip names the output state; the sidebar keeps it to a glanceable word or reading.
        StartButton.Content = requested ? "Stop display" : "Start display";
        DisplayDot.Fill = NavDisplayDot.Fill = (Brush)FindResource(outputTone);
        DeviceLabel.Text = status.Sending ? "Sending to LD360" : requested ? status.Title : "Display off";
        NavDisplayStatus.Text = status.Sending && sample != null ? $"LD360 · Showing {TemperatureValue.Text}{unit}" : requested ? "LD360 · Waiting" : "LD360 · Off";
        StartupHint.Visibility = StartupCheck.IsChecked == true && AutoDisplayCheck.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        RenderKeyboardStatus();
    }

    private void SetRow(TextBlock value, System.Windows.Shapes.Ellipse dot, TextBlock detail, string text, string tone, string description)
    {
        value.Text = text; dot.Fill = (Brush)FindResource(tone); detail.Text = description;
    }

    internal void RenderKeyboardStatus()
    {
        NavKeyboardStatus.Text = Keyboard.SidebarStatus;
        NavKeyboardDot.Fill = (Brush)FindResource(Keyboard.StatusTone);
    }

    // Dark caption that matches the window background (Windows 11; ignored where unsupported).
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        int dark = 1, caption = ToColorRef((Color)FindResource("WindowColor")), text = ToColorRef(Color.FromRgb(0xF5, 0xF5, 0xF7));
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }
    private static int ToColorRef(Color color) => color.R | color.G << 8 | color.B << 16;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private async void NavChanged(object sender, RoutedEventArgs e)
    {
        if (DisplayPage == null || Keyboard == null) return;
        DisplayPage.Visibility = NavDisplay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Keyboard.Visibility = NavKeyboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded && NavKeyboard.IsChecked == true && !Keyboard.HasSnapshot && !Keyboard.IsBusy) await Keyboard.ReadAsync();
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
            MessageBox.Show(this, error.Message, "Sora · Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { StartupCheck.IsEnabled = true; Render(); }
    }

    private void ToggleSensorInfo(object sender, RoutedEventArgs e)
    {
        var show = SensorInfo.Visibility != Visibility.Visible;
        SensorInfo.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
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
