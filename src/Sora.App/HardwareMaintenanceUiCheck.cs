using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Sora.Core;

namespace Sora.App;

internal static class HardwareMaintenanceUiCheck
{
    private sealed record Result(string Name, bool Passed);
    internal static bool Run(string output)
    {
        var checks = new List<Result>();
        void Check(string name, bool passed) => checks.Add(new(name, passed));
        var updates = new UpdatesPanel();
        Check("Application updates have their own section", updates.FindName("CheckButton") is Button button && (string)button.Content == "Check BIOS and drivers");
        var appCard = new ApplicationUpdateCard();
        Check("Application installation is a distinct user action", (string)appCard.AppInstall.Content == "Install and restart" && appCard.AppInstall.Visibility == Visibility.Collapsed);
        Check("Firmware coverage is always explicit before a catalog check", updates.CoverageText.Text == HardwareMaintenance.Coverage);
        Check("All eight manual support routes have an action", updates.MaintenanceList.Children.OfType<StackPanel>().Count(row => row.Children.OfType<Button>().Count() == 1) == 8);
        var ssd = new SsdPanel();
        var drive = new StorageDrive(0, "KIOXIA-EXCERIA G3 SSD", "EVFATR.0", "fixture", 1, "NVMe", Array.Empty<string>(), true, true,
            new StorageHealth("NVMe", "Good", 40, 100, "1", Array.Empty<StorageAttribute>(), ""));
        ssd.ShowDrive(drive);
        Check("Good SMART health still shows matching firmware advisory", ssd.FirmwareNoticeCard.Visibility == Visibility.Visible && ssd.FirmwareNoticeDetail.Text.Contains("EVFATR.1"));
        ssd.ShowDrive(drive with { Firmware = "EVFATR.1" });
        Check("Other firmware clears the old advisory without claiming up to date", ssd.FirmwareNoticeCard.Visibility == Visibility.Collapsed && ssd.FirmwareNoticeDetail.Text.Length == 0);
        ssd.ShowDrive(drive with { Model = "KIOXIA-EXCERIA PLUS G3 SSD" });
        Check("Similar model does not inherit advice", ssd.FirmwareNoticeCard.Visibility == Visibility.Collapsed);
        ssd.ShowDrive(drive); ssd.ShowDrive(null);
        Check("Failed or empty inventory clears advisory and identity", ssd.FirmwareNoticeCard.Visibility == Visibility.Collapsed && ssd.FirmwareNoticeDetail.Text.Length == 0 && ssd.ModelText.Text != drive.Model);
        var passed = checks.All(value => value.Passed);
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, new { capturedAt = DateTimeOffset.Now, passed, configurationWrites = 0, synthetic = true, browserLaunched = false, checks = checks.Select(value => new { name = value.Name, passed = value.Passed }) }, new JsonSerializerOptions { WriteIndented = true });
        stream.Flush(true);
        return passed;
    }
}
