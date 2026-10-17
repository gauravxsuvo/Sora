using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

/// <summary>
/// --lighting-probe: read-only report of the Gigabyte RGB controller and the DIMM lighting controllers.
/// Sends no lighting changes. The only SMBus writes are ENE register-pointer selects, made after a controller
/// has passed the read-only signature check.
/// </summary>
internal static class LightingCheck
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static bool Probe(string output)
    {
        var report = new Dictionary<string, object?>
        {
            ["version"] = "0.6-probe",
            ["takenAt"] = DateTimeOffset.Now,
            ["administrator"] = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator),
            ["conflict"] = EneDram.FindConflict(),
            ["lightingWrites"] = 0,
        };
        var ok = true;
        try { report["boardDescriptors"] = GigabyteRgb.Descriptors(); }
        catch (Exception error) { report["boardDescriptorsError"] = error.Message; ok = false; }
        try
        {
            using var board = new GigabyteRgb();
            var reads = new Dictionary<string, string>();
            foreach (var command in GigabyteRgb.DiagnosticReads)
            {
                try { reads[$"0x{command:X2}"] = Convert.ToHexString(board.Query(command)); }
                catch (Exception error) { reads[$"0x{command:X2}"] = "error: " + error.Message; }
            }
            report["board"] = new { board.Device, board.Info, firmware = board.Info.FirmwareText, reads };
        }
        catch (Exception error) { report["boardError"] = error.ToString(); ok = false; }
        try
        {
            using var bus = Smbus.Open();
            report["dram"] = EneDram.Scan(bus);
        }
        catch (Exception error) { report["dramError"] = error.ToString(); ok = false; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json));
        return ok;
    }

    /// <summary>
    /// --board-test: A) GCC's captured all-zones Static green + apply, without 0x47 00. B) 0x47 00, then Static blue + apply.
    /// Then GCC's captured exit sequence verbatim, which puts the board back where GCC leaves it. Every packet is a GCC capture.
    /// </summary>
    public static bool BoardTest(string output)
    {
        var log = new List<object>();
        var report = new Dictionary<string, object?> { ["version"] = "0.6-board-test", ["startedAt"] = DateTimeOffset.Now, ["steps"] = log };
        var ok = false;
        try
        {
            if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
            using var board = new GigabyteRgb();
            var zones = GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel());
            var colours = new Dictionary<int, (byte, byte, byte)> { [0] = (0, 255, 0), [2] = (0, 0, 255), [9] = (255, 96, 0), [5] = (255, 255, 0), [6] = (0, 255, 255), [7] = (255, 0, 255), [4] = (255, 255, 255) };
            var test = zones.Select(z => { var (r, g, b) = colours.GetValueOrDefault(z.Number, ((byte)255, (byte)255, (byte)255)); return new BoardZoneEffect(z.Number, BoardEffect.Static, r, g, b, 8, 0); }).ToArray();
            var red = zones.Select(z => new BoardZoneEffect(z.Number, BoardEffect.Static, 255, 0, 0, 6, 0)).ToArray();
            foreach (var (what, set, hold) in new[] { ("colours", test, 8000), ("red", red, 5000), ("colours", test, 5000), ("red", red, 0) })
            {
                board.ApplyZones(set);
                log.Add(new { at = DateTimeOffset.Now, what = "ApplyZones: " + what, zones = set });
                Thread.Sleep(hold);
            }
            ok = true;
        }
        catch (Exception error) { report["error"] = error.ToString(); }
        report["finishedAt"] = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json));
        return ok;
    }

    /// <summary>
    /// --board-default-rainbow: GCC's rainbow wave (Wave17) on the whole board, then GCC's own exit command 0x47 01, so the board shows the rainbow
    /// after a restart again. On the user's request only: this is the single place Sora sends 0x47.
    /// </summary>
    public static bool BoardDefaultRainbow(string output)
    {
        var report = new Dictionary<string, object?> { ["version"] = "0.6.2-board-default-rainbow", ["startedAt"] = DateTimeOffset.Now };
        var ok = false;
        try
        {
            if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
            using var board = new GigabyteRgb();
            var effects = LightingService.BoardEffects(GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel()).Select(z => new BoardZoneLighting(z.Number)));
            board.ApplyZones(effects);
            board.KeepAfterRestart(true);
            report["zones"] = effects;
            report["packets"] = new[] { Convert.ToHexString(GigabyteRgb.RainbowWavePacket()), "CC280400", "CC4701" };
            ok = true;
        }
        catch (Exception error) { report["error"] = error.ToString(); }
        report["finishedAt"] = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json));
        return ok;
    }

    /// <summary>--board-rainbow-test: GCC's rainbow wave on the board, nothing kept or saved. The next Apply or restart replaces it.</summary>
    public static bool BoardRainbowTest(string output)
    {
        var report = new Dictionary<string, object?> { ["version"] = "0.6.3-board-rainbow-test", ["startedAt"] = DateTimeOffset.Now };
        var ok = false;
        try
        {
            if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
            using var board = new GigabyteRgb();
            board.ApplyZones(LightingService.BoardEffects(GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel()).Select(z => new BoardZoneLighting(z.Number))));
            ok = true;
        }
        catch (Exception error) { report["error"] = error.ToString(); }
        report["finishedAt"] = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json));
        return ok;
    }

    /// <summary>
    /// --board-builtin-default: GCC's connect command 0x47 00 only. The board stops keeping the last applied lighting and starts
    /// with its own built-in lighting after the next restart. Nothing visible changes now.
    /// </summary>
    public static bool BoardBuiltinDefault(string output)
    {
        var report = new Dictionary<string, object?> { ["version"] = "0.6.2-board-builtin-default", ["startedAt"] = DateTimeOffset.Now };
        var ok = false;
        try
        {
            if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
            using var board = new GigabyteRgb();
            board.KeepAfterRestart(false);
            report["packets"] = new[] { "CC4700" };
            ok = true;
        }
        catch (Exception error) { report["error"] = error.ToString(); }
        report["finishedAt"] = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json));
        return ok;
    }

    /// <summary>
    /// --lighting-service-check: the UI's own path. Scan (saves the original backup), apply Breathing purple to the memory,
    /// hold, then Restore original, comparing every register with the backup. The motherboard is not touched.
    /// </summary>
    public static async Task<bool> ServiceCheck(string output, int holdSeconds)
    {
        var log = new List<object>();
        var report = new Dictionary<string, object?> { ["version"] = "0.6-service-check", ["startedAt"] = DateTimeOffset.Now, ["steps"] = log };
        void Step(string what, object? detail = null) => log.Add(new { at = DateTimeOffset.Now, what, detail });
        static object Describe(LightingState state) => new { state.MemoryName, state.Conflict, state.HasBackup, state.MemoryError, dimms = state.Dimms.Select(d => new { d.Slot, d.Name, control = Convert.ToHexString(d.Original.Control), colors = Convert.ToHexString(d.Original.Colors) }).ToArray() };
        var ok = false;
        var host = new SoraHost();
        try
        {
            var before = await host.Lighting.RefreshAsync();
            Step("scanned", Describe(before));
            if (before.Conflict != null) throw new InvalidOperationException(before.Conflict);
            var applied = await host.Lighting.ApplyAsync(new MemoryLighting(EneMode.Breathing, "#BF5AF2", 100, 3), null, false);
            Step("applied Breathing #BF5AF2, read back", Describe(applied));
            if (applied.Dimms.Any(d => d.Original.Control[1] != (byte)EneMode.Breathing || d.Original.Colors[0] != 0xBF || d.Original.Colors[1] != 0xF2 || d.Original.Colors[2] != 0x5A))
                throw new IOException("The DIMMs do not report Breathing #BF5AF2 after apply.");
            await Task.Delay(holdSeconds * 1000);
            var restored = await host.Lighting.RestoreOriginalAsync();
            Step("restored original", Describe(restored));
            if (restored.Dimms.Count != before.Dimms.Count || restored.MemoryError != null) throw new IOException("Could not read the DIMMs back after restoring: " + restored.MemoryError);
            var mismatch = restored.Dimms.Where(d => before.Dimms.Single(b => b.Slot == d.Slot) is var b && (!b.Original.Control.SequenceEqual(d.Original.Control) || !b.Original.Colors.SequenceEqual(d.Original.Colors))).Select(d => d.Slot).ToArray();
            if (mismatch.Length > 0) throw new IOException("Restore did not match the scan for slot(s) " + string.Join(", ", mismatch));
            Step("every register matches the original scan");
            ok = true;
        }
        catch (Exception error) { report["error"] = error.ToString(); }
        report["settings"] = host.Lighting.Settings;
        report["finishedAt"] = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json));
        return ok;
    }

    /// <summary>
    /// --lighting-test: reversible hardware check. Both DIMMs and the board go Static green for ten seconds, each DIMM is read back,
    /// then every DIMM register Sora touched is restored from the scan and read back again. Refuses to run beside other RGB software.
    /// </summary>
    public static bool Test(string output, int holdSeconds)
    {
        var log = new List<object>();
        var report = new Dictionary<string, object?> { ["version"] = "0.6-test", ["startedAt"] = DateTimeOffset.Now, ["steps"] = log };
        void Step(string what, object? detail = null) => log.Add(new { at = DateTimeOffset.Now, what, detail });
        var ok = false;
        try
        {
            if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
            using var bus = Smbus.Open();
            var probe = EneDram.Scan(bus);
            report["dram"] = probe;
            if (probe.Dimms.Count == 0) throw new IOException("No verified DIMM lighting controller.");
            Step("scanned", probe.Dimms.Select(d => new { d.Slot, d.Name, before = Convert.ToHexString(d.Original.Control) + "/" + Convert.ToHexString(d.Original.Colors) }).ToArray());
            using var board = new GigabyteRgb();
            Step("board", board.Info.Product);
            try
            {
                EneDram.Apply(bus, probe.Dimms, EneMode.Static, 0, 255, 0, 0, false, EneColorOrder.Rbg);
                Step("dram green written and read back", probe.Dimms.Select(d => Convert.ToHexString(EneDram.Read(bus, d).Colors[..3])).ToArray());
                board.ApplyZones(GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel()).Select(z => new BoardZoneEffect(z.Number, BoardEffect.Static, 0, 255, 0, 8, 0)).ToArray());
                Step("board green sent");
                Thread.Sleep(holdSeconds * 1000);
            }
            finally
            {
                EneDram.Restore(bus, probe.Dimms);
                Step("dram restored and read back", probe.Dimms.Select(d => { var now = EneDram.Read(bus, d); return Convert.ToHexString(now.Control) + "/" + Convert.ToHexString(now.Colors); }).ToArray());
            }
            ok = true;
        }
        catch (Exception error) { report["error"] = error.ToString(); }
        report["finishedAt"] = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json));
        return ok;
    }
}
