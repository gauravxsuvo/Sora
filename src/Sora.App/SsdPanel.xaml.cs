using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sora.Core;

namespace Sora.App;

public partial class SsdPanel : UserControl
{
    private SoraHost? host;
    private bool reading;
    private bool readOnce;
    private string? scanError;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    public event Action<string>? StatusChanged;

    public SsdPanel()
    {
        InitializeComponent();
        timer.Tick += async (_, _) => { if (IsVisible) await ReadAsync(); };
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible) { timer.Start(); if (!readOnce) await ReadAsync(); }
            else timer.Stop();
        };
        Unloaded += (_, _) => timer.Stop();
        Loaded += async (_, _) => { if (IsVisible) { timer.Start(); if (!readOnce) await ReadAsync(); } };
    }

    internal void Attach(SoraHost value) => host = value;

    public async Task ReadAsync()
    {
        if (reading) return;
        reading = true;
        scanError = null;
        RefreshButton.IsEnabled = false; ControlState.SetIsBusy(RefreshButton, true);
        try
        {
            var selected = (DriveList.SelectedItem as StorageDrive)?.Number;
            var drives = await Task.Run(StorageInventory.Scan);
            var listScroll = FindScrollViewer(DriveList);
            var smartScroll = FindScrollViewer(SmartGrid);
            var listOffset = listScroll?.VerticalOffset ?? 0;
            var smartOffset = smartScroll?.VerticalOffset ?? 0;
            DriveList.ItemsSource = drives;
            DriveList.SelectedItem = drives.FirstOrDefault(d => d.Number == selected) ?? drives.FirstOrDefault();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                listScroll?.ScrollToVerticalOffset(listOffset);
                smartScroll?.ScrollToVerticalOffset(smartOffset);
            }));
            readOnce = true;
            SetStatus(drives.Count == 0 ? "No physical drives found." : $"{drives.Count} drive(s) · Read {DateTime.Now:T} · Refreshes every 5 seconds while visible.");
            if (drives.Count == 0) ShowDrive(null);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            scanError = error is UnauthorizedAccessException || error.Message.Contains("access", StringComparison.OrdinalIgnoreCase)
                ? "Windows denied drive access. Open Sora as administrator, then rescan. No health data was read."
                : "Drive scanning failed. Rescan to retry. " + error.Message;
            // A failed refresh must not keep old readings presented as current.
            DriveList.ItemsSource = null;
            ShowDrive(null);
            SetStatus("Drive scan unavailable: " + error.Message);
            host?.Log("SSD scan: " + error.Message);
        }
        finally { reading = false; RefreshButton.IsEnabled = true; ControlState.SetIsBusy(RefreshButton, false); }
    }

    private void SetStatus(string text) { StatusText.Text = text; StatusChanged?.Invoke(text); }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await ReadAsync();
    private void DriveChanged(object sender, SelectionChangedEventArgs e) => ShowDrive(DriveList.SelectedItem as StorageDrive);
    internal void ShowDrive(StorageDrive? drive)
    {
        DriveEmpty.Visibility = DriveList.HasItems ? Visibility.Collapsed : Visibility.Visible;
        DriveList.Visibility = DriveList.HasItems ? Visibility.Visible : Visibility.Collapsed;
        DriveEmpty.Text = scanError ?? "No physical drives were returned. Rescan drives to try again.";
        HealthMetrics.Visibility = drive == null ? Visibility.Collapsed : Visibility.Visible;
        HealthEmpty.Visibility = drive == null ? Visibility.Visible : Visibility.Collapsed;
        SmartGrid.Visibility = drive?.Health.Attributes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SmartEmpty.Visibility = SmartGrid.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        SmartEmpty.Text = drive == null ? "No S.M.A.R.T. readings. Read a drive successfully to view its reported attributes and enable CSV export."
            : "This drive returned no S.M.A.R.T. attributes. Health cannot be inferred from an empty table.";
        ModelText.Text = drive?.Model ?? "Drive readings unavailable";
        IdentityText.Text = drive == null ? "No current drive readings." :
            $"Disk {drive.Number} · {drive.Bus} · {drive.CapacityBytes / 1_000_000_000d:F1} GB\nFirmware: {Unknown(drive.Firmware)}\nSerial: {Unknown(drive.Serial)}\nVolumes: {(drive.Volumes.Count == 0 ? "No mounted drive letters" : string.Join(", ", drive.Volumes))}{(drive.SystemDisk ? " · Windows system disk" : "")}\nPower-on hours: {drive.Health.PowerOnHours ?? "Unknown"} · TRIM: {(drive.Trim.HasValue ? drive.Trim.Value ? "Supported" : "Not supported" : "Unknown")}";
        HealthText.Text = drive?.Health.Status ?? "Unknown";
        TemperatureText.Text = drive?.Health.TemperatureC is { } t ? $"{t:F1} °C / {32 + 1.8 * t:F1} °F" : "Temperature unavailable";
        LifeText.Text = drive?.Health.LifeRemaining is { } life ? life + "%" : "Unknown";
        HealthDetail.Text = drive?.Health.Protocol == "NVMe" && drive.Health.Attributes.Count > 0
            ? "An endurance estimate from the drive's reported wear. Warnings and available spare space also affect health."
            : drive?.Health.Detail ?? "";
        SmartGrid.ItemsSource = drive?.Health.Attributes;
        var ataColumns = drive?.Health.Protocol == "ATA" ? Visibility.Visible : Visibility.Collapsed;
        SmartGrid.Columns[4].Visibility = ataColumns;
        SmartGrid.Columns[5].Visibility = ataColumns;
        ExportButton.IsEnabled = drive?.Health.Attributes.Count > 0;
        EraseText.Text = drive?.SecureEraseStatus ?? "Secure erase is unavailable in Sora.";
        var notice = HardwareMaintenance.StorageNotice(drive?.Model, drive?.Firmware);
        FirmwareNoticeCard.Visibility = notice == null ? Visibility.Collapsed : Visibility.Visible;
        FirmwareNoticeTitle.Text = notice?.Title ?? "";
        FirmwareNoticeDetail.Text = notice?.Detail ?? "";
    }
    private void FirmwareNoticeClick(object sender, RoutedEventArgs e)
    {
        if (DriveList.SelectedItem is not StorageDrive drive || HardwareMaintenance.StorageNotice(drive.Model, drive.Firmware) is not { } notice) return;
        try { MaintenanceLinks.Open(notice.Url); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { SetStatus("Cannot open advisory: " + error.Message); }
    }
    private static string Unknown(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    internal static object RunEmptyStateChecks()
    {
        var panel = new SsdPanel();
        var checks = new System.Collections.Generic.Dictionary<string, bool>();
        void Check(string name, bool value) { checks[name] = value; if (!value) throw new InvalidOperationException(name); }
        panel.scanError = "Windows denied drive access. Open Sora as administrator, then rescan. No health data was read.";
        panel.ShowDrive(null);
        Check("Drive panel explains permission failure", panel.DriveEmpty.Visibility == Visibility.Visible && panel.DriveEmpty.Text.Contains("administrator"));
        Check("Unread metrics are replaced by an explanation", panel.HealthMetrics.Visibility == Visibility.Collapsed && panel.HealthEmpty.Visibility == Visibility.Visible);
        Check("SMART has an explicit empty state", panel.SmartGrid.Visibility == Visibility.Collapsed && panel.SmartEmpty.Visibility == Visibility.Visible);
        Check("Rescan stays available and export stays disabled", panel.RefreshButton.IsEnabled && !panel.ExportButton.IsEnabled);
        var drive = new StorageDrive(0, "Synthetic test drive", "fixture", "fixture", 1000000000, "NVMe", [], false, true,
            new StorageHealth("NVMe", "Healthy", 35, 95, "100", [new StorageAttribute("01", "Fixture attribute", "1")], "fixture"));
        panel.scanError = null; panel.DriveList.ItemsSource = new[] { drive }; panel.DriveList.SelectedItem = drive;
        Check("Successful read restores metrics and attributes", panel.HealthMetrics.Visibility == Visibility.Visible && panel.SmartGrid.Visibility == Visibility.Visible && panel.ExportButton.IsEnabled);
        panel.DriveList.ItemsSource = null; panel.ShowDrive(null);
        Check("Losing data clears previous healthy metrics and export", panel.HealthMetrics.Visibility == Visibility.Collapsed && !panel.ExportButton.IsEnabled && panel.FirmwareNoticeCard.Visibility == Visibility.Collapsed);
        return new { checks, HardwareReads = 0, HardwareWrites = 0, Fixture = "Synthetic state only" };
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject target)
    {
        if (target is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(target); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(target, i)) is { } child) return child;
        return null;
    }
    private void ExportClick(object sender, RoutedEventArgs e)
    {
        if (DriveList.SelectedItem is not StorageDrive drive || drive.Health.Attributes.Count == 0) return;
        var dialog = new SaveFileDialog { FileName = $"SSD{drive.Number}-{DateTime.Now:yyyyMMddTHHmmss}.csv", Filter = "CSV files (*.csv)|*.csv", DefaultExt = ".csv" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, StorageSmart.ToCsv(drive), new UTF8Encoding(true)); SetStatus("Saved S.M.A.R.T. report to " + dialog.FileName); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { SetStatus("Cannot save report: " + error.Message); }
    }
}
