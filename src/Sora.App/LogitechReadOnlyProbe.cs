using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

// Runs before SoraHost exists: feature queries only, no ownership or saved-profile application.
internal static class LogitechReadOnlyProbe
{
    internal static async Task<bool> RunAsync(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var info = LogitechDevice.Enumerate().Single();
            await using var device = new LogitechDevice(info);
            var snapshot = await device.ReadAsync(timeout.Token);
            var mapping = await device.ReadButtonMapAsync(snapshot, timeout.Token);
            var indicator = await device.ReadDpiIndicatorAsync(snapshot, timeout.Token);
            var settings = await device.ReadSettingsAsync(snapshot);
            var onboard = await device.ReadOnboardAsync(snapshot, timeout.Token);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                capturedAt = DateTimeOffset.Now, version = typeof(App).Assembly.GetName().Version?.ToString(),
                configurationWrites = 0, info, snapshot, buttonMap = Convert.ToHexString(mapping),
                indicator, settings, onboard
            }, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { configurationWrites = 0, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return false;
        }
    }
}
