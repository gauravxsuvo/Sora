using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

// This entry point runs before the tray host and never loads or applies saved hardware profiles.
internal static class HardwareToolsProbe
{
    public static async Task RunAsync(string output)
    {
        var results = new Dictionary<string, object?>
        {
            ["capturedAt"] = DateTimeOffset.Now,
            ["configurationWrites"] = 0,
            ["activeWifiScanRequested"] = false,
        };
        try
        {
            var drives = await Task.Run(StorageInventory.Scan);
            results["storage"] = drives;
            results["firmwareNotices"] = drives.Select(drive => new { drive.Number, drive.Model, drive.Firmware, Notice = HardwareMaintenance.StorageNotice(drive.Model, drive.Firmware) }).ToArray();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { results["storageError"] = error.ToString(); }
        results["firmwareCoverage"] = HardwareMaintenance.Coverage;
        results["maintenanceRoutes"] = HardwareMaintenance.Routes;
        try
        {
            results["gpu"] = await Task.Run(() => { using var gpu = new NvidiaGpuFans(); return gpu.Read(); });
        }
        catch (Exception error) when (error is not OutOfMemoryException) { results["gpuError"] = error.ToString(); }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            results["wifi"] = await WifiScanner.ReadAsync(timeout.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { results["wifiError"] = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
