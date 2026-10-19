using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

internal static class FanCheck
{
    public static async Task<bool> ServiceCheckAsync(string output)
    {
        var log = new Dictionary<string, object?>();
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "fan-service-check-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        var host = new SoraHost(directory);
        bool success = false;
        try
        {
            if (!await host.Fans.ReadAsync()) throw new IOException(host.Fans.Message);
            var before = host.Fans.Snapshot!; log["before"] = before;
            if (before.TemperaturesC[0] is not > 10) throw new IOException("Temperature source is not valid for a non-stopping firmware test.");
            const int header = 1;
            var profile = new FanProfile { Header = header, DeviceKind = FanDeviceKind.Fan, TemperatureSource = 0,
                Points = [new(1, 100), new(85, 100)], FirmwareStop = new FanStopConfig(true, 0, 1, 2) };
            if (!await host.Fans.ApplyAsync(profile)) throw new IOException(host.Fans.Message);
            log["applied"] = host.Fans.Snapshot;
            if (!await host.Fans.RestoreAsync(header)) throw new IOException(host.Fans.Message);
            var explicitRestore = host.Fans.Snapshot!; log["explicitRestore"] = explicitRestore;
            bool Matches(FanSnapshot a, FanSnapshot b) => a.Headers.Zip(b.Headers).All(pair =>
                pair.First.Registers.SequenceEqual(pair.Second.Registers) && pair.First.FirmwareStop == pair.Second.FirmwareStop);
            if (!Matches(before, explicitRestore)) throw new IOException("Service restore changed the saved native or firmware settings.");
            if (!await host.Fans.ApplyAsync(profile)) throw new IOException(host.Fans.Message);
            await host.Fans.StopAsync();
            var afterExit = await Task.Run(() => { using var fans = new GigabyteFans(); return GigabyteFanFirmware.Enrich(fans.ReadSnapshot()); });
            log["afterExit"] = afterExit;
            if (!Matches(before, afterExit)) throw new IOException("Exit restoration did not match the original settings.");
            log["checks"] = new[] { "Native and firmware apply/readback", "Persistent backups saved before writes", "Explicit restore: all six headers unchanged", "Exit restore: all six headers unchanged" };
            log["loadedAssemblies"] = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.GetName().FullName).ToArray();
            success = true;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { log["error"] = e.ToString(); }
        finally { await host.Fans.StopAsync(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true }));
        return success;
    }
    // A diagnostics mode reads settings and tachometers only. No fan, RGB, flash or firmware writes.
    public static bool Probe(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        try
        {
            using var fans = new GigabyteFans();
            var snapshot = fans.ReadSnapshot();
            File.WriteAllText(output, JsonSerializer.Serialize(new { snapshot, conflict = GigabyteFans.FindConflict(), configurationWrites = 0 }, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { File.WriteAllText(output, JsonSerializer.Serialize(new { error = e.ToString(), configurationWrites = 0 }, new JsonSerializerOptions { WriteIndented = true })); return false; }
    }
}
