using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

/// <summary>Memory lighting as the user chose it. Brightness 0-100 scales the colour; speed 1 (slow) to 5 (fast).</summary>
public sealed record MemoryLighting(EneMode Effect = EneMode.Static, string Color = "#0A84FF", int Brightness = 100, int Speed = 3, bool Reverse = false);

/// <summary>One motherboard zone with GCC's ranges: brightness 0-8, speed 0-9. The default is GCC's own rainbow (brightness 8, speed 8).</summary>
public sealed record BoardZoneLighting(int Zone, BoardEffect Effect = BoardEffect.Rainbow, string Color = "#0A84FF", int Brightness = 8, int Speed = 8);

/// <summary>The effects both the RAM and the board can run, for sync mode.</summary>
public enum SyncEffect { Rainbow, Static, Breathing, Flashing, ColorCycle, Off }

/// <summary>
/// One look for the RAM and every board light. Brightness 10-100 %, speed 1 (slow) to 5 (fast). Each device gets its closest
/// built-in effect: Breathing is the board's Pulse, Flashing its Flash, Rainbow the RAM's rainbow wave and GCC's board wave.
/// </summary>
public sealed record SyncLighting(SyncEffect Effect = SyncEffect.Rainbow, string Color = "#0A84FF", int Brightness = 100, int Speed = 3)
{
    private static readonly int[] BoardPercent = [10, 20, 30, 40, 50, 60, 70, 80, 100];

    public MemoryLighting ToMemory() => new(Effect switch
    {
        SyncEffect.Rainbow => EneMode.Rainbow, SyncEffect.Static => EneMode.Static, SyncEffect.Breathing => EneMode.Breathing,
        SyncEffect.Flashing => EneMode.Flashing, SyncEffect.ColorCycle => EneMode.ColorCycle, _ => EneMode.Off,
    }, Color, Math.Clamp(Brightness, 10, 100), Math.Clamp(Speed, 1, 5));

    public IReadOnlyList<BoardZoneLighting> ToBoard(IEnumerable<int> zones)
    {
        var effect = Effect switch
        {
            SyncEffect.Rainbow => BoardEffect.Rainbow, SyncEffect.Static => BoardEffect.Static, SyncEffect.Breathing => BoardEffect.Pulse,
            SyncEffect.Flashing => BoardEffect.Flash, SyncEffect.ColorCycle => BoardEffect.ColorCycle, _ => BoardEffect.Off,
        };
        // Nearest of GCC's nine brightness levels; speed 1-5 spread over GCC's 0-9.
        var level = Array.IndexOf(BoardPercent, BoardPercent.MinBy(p => Math.Abs(p - Math.Clamp(Brightness, 10, 100))));
        var speed = (int)Math.Round((Math.Clamp(Speed, 1, 5) - 1) * 9 / 4.0);
        return zones.Select(zone => new BoardZoneLighting(zone, effect, Color, level, speed)).ToArray();
    }

    /// <summary>The closest sync look to a memory setting, used when sync is first turned on.</summary>
    public static SyncLighting From(MemoryLighting memory) => new(memory.Effect switch
    {
        EneMode.Rainbow => SyncEffect.Rainbow, EneMode.Static => SyncEffect.Static, EneMode.Breathing => SyncEffect.Breathing,
        EneMode.Flashing => SyncEffect.Flashing, EneMode.ColorCycle => SyncEffect.ColorCycle, _ => SyncEffect.Off,
    }, memory.Color, memory.Brightness, memory.Speed);
}

/// <summary>
/// A null section has never been applied, and Sora leaves that hardware alone. With Sync on, Apply writes SyncLighting to both
/// devices and stores the result in Memory and BoardZones too, so sign-in re-applies it the same way.
/// RainbowOnExit: like GCC, Sora hands the lights back in their default when it closes (the RAM's own Rainbow, GCC's board wave).
/// </summary>
public sealed record LightingSettings(MemoryLighting? Memory = null, IReadOnlyList<BoardZoneLighting>? BoardZones = null, bool ApplyAtStartup = true, bool Sync = false, SyncLighting? SyncLighting = null, bool RainbowOnExit = true);

/// <summary>Registers of one DIMM as they were before Sora first changed them.</summary>
public sealed record DimmBackup(int Slot, string Name, ushort ChipId, string Control, string Colors, int? Brightness = null);

/// <summary>What the page needs to know about the hardware. Errors are short, user-facing sentences.</summary>
public sealed record LightingState(IReadOnlyList<EneDimm> Dimms, string? MemoryName, string? MemoryError, BoardInfo? Board, string? BoardError, string? Conflict, bool HasBackup, DateTimeOffset CheckedAt, string? BoardModel = null, IReadOnlyList<BoardZone>? BoardZones = null);

/// <summary>Owns all lighting hardware access. One operation at a time; each opens the SMBus or HID handle and closes it again.</summary>
internal sealed class LightingService(SoraHost host)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string SettingsPath => Path.Combine(host.DataDirectory, "lighting.json");
    private string BackupPath => Path.Combine(host.DataDirectory, "lighting-original.json");

    private SyncEngine? engine;

    public LightingSettings Settings { get; private set; } = new();
    public LightingState? State { get; private set; }
    public bool Busy => gate.CurrentCount == 0;
    public event Action? Changed;

    public void Load()
    {
        try { if (File.Exists(SettingsPath)) Settings = JsonSerializer.Deserialize<LightingSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { host.Log("Lighting settings: " + error.Message); }
    }

    public void SaveStartupPreference(bool applyAtStartup) => Save(Settings with { ApplyAtStartup = applyAtStartup });

    /// <summary>Sync on or off is a page preference; the hardware changes only on Apply. Turning it off stops the sync engine.</summary>
    public void SaveSyncPreference(bool sync)
    {
        Save(Settings with { Sync = sync });
        if (!sync) StopEngine();
    }

    /// <summary>
    /// Applies one look to the RAM and every board light (skipping a device that is not available) and remembers it.
    /// Static, Rainbow and Off are the devices' own effects; Color cycle, Breathing and Flashing run in the sync engine.
    /// </summary>
    public async Task<LightingState> ApplySyncAsync(SyncLighting sync, bool memory, bool board, bool applyAtStartup)
    {
        // Saved first, so the engine restart at the end of Run uses this look (and does not resume an older one).
        Save(Settings with { Sync = true, SyncLighting = sync, ApplyAtStartup = applyAtStartup });
        if (SyncEngine.Animated(sync.Effect))
        {
            if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
            return await Run(() => Scan(out _));
        }
        var zones = State?.BoardZones ?? GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel());
        return await ApplyAsync(memory ? sync.ToMemory() : null, board ? sync.ToBoard(zones.Select(z => z.Number)) : null, applyAtStartup);
    }

    public void SaveExitPreference(bool rainbowOnExit) => Save(Settings with { RainbowOnExit = rainbowOnExit });

    /// <summary>
    /// When Sora closes, like GCC on exit: stop the sync engine and hand the lights back in their default, the RAM's own Rainbow
    /// (its original registers from the backup, read back) and GCC's rainbow wave on the board. Nothing is saved to either device.
    /// Skipped if Sora never changed the lighting or another RGB program is running. Gives up after a few seconds.
    /// </summary>
    public async Task ExitLightingAsync()
    {
        StopEngine();
        if (!Settings.RainbowOnExit || Settings is { Memory: null, BoardZones: null, Sync: false } || EneDram.FindConflict() != null) return;
        if (!await gate.WaitAsync(3000)) return;
        try
        {
            var work = Task.Run(() =>
            {
                try
                {
                    using var bus = Smbus.Open();
                    var dimms = EneDram.Scan(bus).Dimms;
                    var backup = ReadBackup();
                    var originals = dimms.Select(dimm => backup?.SingleOrDefault(b => b.Slot == dimm.Slot && b.Name == dimm.Name && b.ChipId == dimm.ChipId) is { } saved
                        && Convert.FromHexString(saved.Control) is [0x00, (byte)EneMode.Rainbow, ..] and var control
                            ? dimm with { Original = new EneSnapshot(control, Convert.FromHexString(saved.Colors), dimm.HasBrightness ? (byte?)(saved.Brightness ?? 9) : null) }
                            : null).ToArray();
                    // The RAM's own Rainbow from the backup when it was Rainbow; otherwise the built-in Rainbow.
                    if (dimms.Count > 0 && originals.All(d => d != null)) EneDram.Restore(bus, originals!);
                    else if (dimms.Count > 0) EneDram.Apply(bus, dimms, EneMode.Rainbow, 0, 0, 0, 0, true, EneColorOrder.Rbg);
                    host.Log($"Lighting on exit: memory Rainbow on {dimms.Count} DIMM(s), read back OK");
                }
                catch (Exception error) when (error is not OutOfMemoryException) { host.Log("Lighting on exit, memory: " + error.Message); }
                try
                {
                    using var controller = new GigabyteRgb();
                    controller.ApplyZones(GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel()).Select(z => new BoardZoneEffect(z.Number, BoardEffect.Rainbow, 0, 0, 0, 8, 8)).ToArray());
                    host.Log("Lighting on exit: motherboard rainbow wave");
                }
                catch (Exception error) when (error is not OutOfMemoryException) { host.Log("Lighting on exit, motherboard: " + error.Message); }
            });
            await Task.WhenAny(work, Task.Delay(6000));
        }
        finally { gate.Release(); }
    }

    /// <summary>True while Sora is driving an animated sync effect.</summary>
    public bool SyncRunning => engine?.Running == true;

    public void StopEngine()
    {
        engine?.Stop();
        engine = null;
    }

    /// <summary>Starts the sync engine when sync is on with an animated effect and no other RGB program is running.</summary>
    private void StartEngineIfNeeded()
    {
        if (Settings is not { Sync: true, SyncLighting: { } sync } || !SyncEngine.Animated(sync.Effect) || engine?.Running == true) return;
        if (EneDram.FindConflict() != null) return;
        engine = new SyncEngine(host, sync, State == null || State.Dimms.Count > 0, State == null || State.Board != null);
        engine.Start();
    }

    /// <summary>Reads the board controller and scans the DIMMs. No lighting is changed.</summary>
    public Task<LightingState> RefreshAsync()
    {
        // While the sync engine runs, the RAM scan shares the SMBus with it (both take the system SMBus lock per operation), so it
        // keeps running. The board's info read is a request and a reply that an engine frame could split, so the last reading is kept.
        var live = SyncRunning && State?.Board != null;
        return Run(() => Scan(out _, readBoard: !live), pauseEngine: !live);
    }

    /// <summary>Applies the given sections (null sections are skipped), reads back the DIMMs and saves the settings.</summary>
    public Task<LightingState> ApplyAsync(MemoryLighting? memory, IReadOnlyList<BoardZoneLighting>? board, bool applyAtStartup) => Run(() =>
    {
        if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
        var errors = new List<string>();
        MemoryLighting? memoryDone = null;
        IReadOnlyList<BoardZoneLighting>? boardDone = null;
        if (memory != null)
        {
            try
            {
                using var bus = Smbus.Open();
                var dimms = ScanFor(bus);
                var (red, green, blue) = Scale(ParseColor(memory.Color), memory.Effect is EneMode.Static or EneMode.Breathing or EneMode.Flashing ? memory.Brightness : 100);
                EneDram.Apply(bus, dimms, memory.Effect, red, green, blue, (byte)(5 - Math.Clamp(memory.Speed, 1, 5)), memory.Reverse, EneColorOrder.Rbg);
                host.Log($"Lighting: memory {memory.Effect} {memory.Color} {memory.Brightness}% speed {memory.Speed} on {dimms.Count} DIMM(s), read back OK");
                memoryDone = memory;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException) { errors.Add("Memory: " + error.Message); host.Log("Lighting memory: " + error); }
        }
        if (board != null)
        {
            try
            {
                using var controller = new GigabyteRgb();
                controller.ApplyZones(BoardEffects(board));
                host.Log("Lighting: board " + string.Join(", ", board.Select(z => $"zone {z.Zone} {z.Effect} {z.Color} b{z.Brightness} s{z.Speed}")));
                boardDone = board;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { errors.Add("Motherboard: " + error.Message); host.Log("Lighting board: " + error); }
        }
        // Only what reached the hardware is saved, so a failed section is not re-sent at every sign-in.
        // "with", not a new record: a new one silently dropped Sync and turned sync mode off after every Apply (0.6.6-0.6.8).
        Save(Settings with { Memory = memoryDone ?? Settings.Memory, BoardZones = boardDone ?? Settings.BoardZones, ApplyAtStartup = applyAtStartup });
        if (errors.Count > 0) throw new IOException(string.Join(" ", errors));
        return Scan(out _);
    });

    /// <summary>Puts the DIMMs back to the registers captured before Sora first changed them, and forgets the saved memory lighting.</summary>
    public Task<LightingState> RestoreOriginalAsync() => Run(() =>
    {
        if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
        var backup = ReadBackup() ?? throw new IOException("No backup of the original memory lighting exists yet.");
        using (var bus = Smbus.Open())
        {
            var dimms = ScanFor(bus).Select(dimm =>
            {
                var saved = backup.SingleOrDefault(b => b.Slot == dimm.Slot && b.Name == dimm.Name && b.ChipId == dimm.ChipId)
                    ?? throw new IOException($"The backup does not match the DIMM in slot {dimm.Slot}.");
                var control = Convert.FromHexString(saved.Control);
                var colors = Convert.FromHexString(saved.Colors);
                // Backups from 0.6-0.6.2 have no brightness: use GCC's value for that effect (9 for the RAM's own Rainbow).
                var brightness = !dimm.HasBrightness ? null : saved.Brightness is { } value ? (byte)value
                    : control is [0x00, <= (byte)EneMode.Rainbow and var mode, ..] && colors.Length >= 3 ? EneDram.GccBrightness((EneMode)mode, colors[0], colors[2], colors[1]) : null;
                return dimm with { Original = new EneSnapshot(control, colors, brightness) };
            }).ToArray();
            EneDram.Restore(bus, dimms);
        }
        host.Log("Lighting: memory restored to its original state, read back OK");
        // Restoring the RAM's own lighting also leaves sync mode, or the sync engine would paint over it.
        Save(Settings with { Memory = null, Sync = false });
        return Scan(out _);
    });

    /// <summary>At sign-in: re-applies the saved sections. Waits for other RGB software to finish starting and gives up quietly after a few tries.</summary>
    public async Task ApplyAtStartupAsync(CancellationToken token)
    {
        if (!Settings.ApplyAtStartup) return;
        var animated = Settings is { Sync: true, SyncLighting: { } sync } && SyncEngine.Animated(sync.Effect);
        if (!animated && Settings.Memory == null && Settings.BoardZones == null) return;
        for (var attempt = 0; attempt < 4 && !token.IsCancellationRequested; attempt++)
        {
            try { await Task.Delay(attempt == 0 ? 8000 : 20000, token); } catch (OperationCanceledException) { return; }
            if (EneDram.FindConflict() is { } conflict) { host.Log("Lighting at startup postponed: " + conflict); continue; }
            // Scan first (the page and the sidebar then show real readings); the engine starts at the end of the scan.
            if (animated) { await RefreshAsync(); return; }
            try { await ApplyAsync(Settings.Memory, Settings.BoardZones, true); return; }
            catch (Exception error) when (error is IOException or InvalidOperationException) { host.Log("Lighting at startup: " + error.Message); }
        }
    }

    private async Task<LightingState> Run(Func<LightingState> work, bool pauseEngine = true)
    {
        await gate.WaitAsync();
        Changed?.Invoke();
        try { return State = await Task.Run(() => { if (pauseEngine) StopEngine(); return work(); }); }
        finally
        {
            gate.Release();
            // Every other hardware access pauses the sync engine; it resumes here if sync is still on.
            StartEngineIfNeeded();
            Changed?.Invoke();
        }
    }

    private LightingState Scan(out IReadOnlyList<EneDimm> dimms, bool readBoard = true)
    {
        dimms = [];
        var conflict = EneDram.FindConflict();
        string? memoryError = null, boardError = null;
        BoardInfo? board = null;
        try
        {
            using var bus = Smbus.Open();
            dimms = EneDram.Scan(bus).Dimms;
            if (dimms.Count == 0) memoryError = "No RGB memory controller was found.";
            // The backup must be the RAM's own state, so never take it while another RGB program is in control.
            else if (conflict == null && ReadBackup() == null) WriteBackup(dimms);
        }
        catch (SensorUnavailableException error) { memoryError = error.Problem == SensorProblem.Permission ? "Memory lighting needs administrator rights. Open Sora from its shortcut." : error.Message; }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException) { memoryError = error.Message; host.Log("Lighting scan: " + error); }
        if (!readBoard) board = State?.Board;
        else
        {
            try { using var controller = new GigabyteRgb(); board = controller.Info; }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { boardError = error.Message; }
        }
        var model = GigabyteRgb.BoardModel();
        return new LightingState(dimms, dimms.Count > 0 ? MemoryDescription(dimms.Count) : null, memoryError, board, boardError, conflict, ReadBackup() != null, DateTimeOffset.Now, model, board == null ? null : GigabyteRgb.ZonesFor(model));
    }

    /// <summary>The zones as controller effects. Rainbow stays Rainbow here: ApplyZones turns it into GCC's whole-board wave.</summary>
    public static BoardZoneEffect[] BoardEffects(IEnumerable<BoardZoneLighting> zones) =>
        zones.Select(z => { var (r, g, b) = ParseColor(z.Color); return new BoardZoneEffect(z.Zone, z.Effect, r, g, b, z.Brightness, z.Speed); }).ToArray();

    private static IReadOnlyList<EneDimm> ScanFor(Smbus bus)
    {
        var dimms = EneDram.Scan(bus).Dimms;
        return dimms.Count > 0 ? dimms : throw new IOException("No RGB memory controller was found.");
    }

    private void Save(LightingSettings settings)
    {
        Settings = settings;
        try
        {
            Directory.CreateDirectory(host.DataDirectory);
            File.WriteAllText(SettingsPath + ".tmp", JsonSerializer.Serialize(settings, Json));
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { host.Log("Cannot save lighting settings: " + error.Message); }
    }

    private IReadOnlyList<DimmBackup>? ReadBackup()
    {
        try { return File.Exists(BackupPath) ? JsonSerializer.Deserialize<DimmBackup[]>(File.ReadAllText(BackupPath)) : null; }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { host.Log("Lighting backup: " + error.Message); return null; }
    }

    private void WriteBackup(IReadOnlyList<EneDimm> dimms)
    {
        try
        {
            Directory.CreateDirectory(host.DataDirectory);
            File.WriteAllText(BackupPath, JsonSerializer.Serialize(dimms.Select(d => new DimmBackup(d.Slot, d.Name, d.ChipId, Convert.ToHexString(d.Original.Control), Convert.ToHexString(d.Original.Colors), d.Original.Brightness)).ToArray(), Json));
            host.Log("Lighting: saved the original memory lighting to " + BackupPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { host.Log("Cannot save lighting backup: " + error.Message); }
    }

    /// <summary>"ADATA AX5U6000C3616G-SLABR × 2" from Windows' memory inventory, or a generic name.</summary>
    private static string MemoryDescription(int count)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Manufacturer, PartNumber FROM Win32_PhysicalMemory");
            var modules = searcher.Get().Cast<System.Management.ManagementBaseObject>()
                .Select(m => ((m["Manufacturer"] as string ?? "").Trim().Replace("A-DATA Technology", "ADATA") + " " + (m["PartNumber"] as string ?? "").Trim()).Trim())
                .Where(text => text.Length > 0).Distinct().ToArray();
            if (modules.Length == 1) return $"{modules[0]} × {count}";
        }
        catch (Exception error) when (error is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return count == 1 ? "1 RGB memory module" : $"{count} RGB memory modules";
    }

    public static (byte, byte, byte) ParseColor(string hex) =>
        hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? ((byte)(value >> 16), (byte)(value >> 8), (byte)value) : throw new FormatException("Colours are #RRGGBB.");

    private static (byte, byte, byte) Scale((byte R, byte G, byte B) color, int percent)
    {
        var factor = Math.Clamp(percent, 0, 100) / 100.0;
        return ((byte)Math.Round(color.R * factor), (byte)Math.Round(color.G * factor), (byte)Math.Round(color.B * factor));
    }
}
