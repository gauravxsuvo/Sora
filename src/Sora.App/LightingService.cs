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
/// KeepOnBoard: the motherboard keeps the applied lighting through a shutdown (GCC's CC 47 01); see <see cref="LightingService.KeepOnBoardAsync"/>.
/// </summary>
public sealed record ModuleLighting(int Slot, string Name, ushort ChipId, MemoryLighting Lighting);
public sealed record LightingSettings(MemoryLighting? Memory = null, IReadOnlyList<BoardZoneLighting>? BoardZones = null, bool ApplyAtStartup = true, bool Sync = false, SyncLighting? SyncLighting = null, bool RainbowOnExit = false, bool MemorySync = true, IReadOnlyList<ModuleLighting>? MemoryModules = null, bool KeepOnBoard = false, int RetentionVersion = 1);

/// <summary>Registers of one DIMM as they were before Sora first changed them.</summary>
public sealed record DimmBackup(int Slot, string Name, ushort ChipId, string Control, string Colors, int? Brightness = null);

/// <summary>What the page needs to know about the hardware. Errors are short, user-facing sentences.</summary>
public sealed record LightingState(IReadOnlyList<EneDimm> Dimms, string? MemoryName, string? MemoryError, BoardInfo? Board, string? BoardError, string? Conflict, bool HasBackup, DateTimeOffset CheckedAt, string? BoardModel = null, IReadOnlyList<BoardZone>? BoardZones = null);

/// <summary>Owns all lighting hardware access. One operation at a time; each opens the SMBus or HID handle and closes it again.</summary>
internal sealed class LightingService(SoraHost host)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object settingsGate = new();
    private long editRevision;
    private string SettingsPath => Path.Combine(host.DataDirectory, "lighting.json");
    private string BackupPath => Path.Combine(host.DataDirectory, "lighting-original.json");

    private SyncEngine? engine;
    private bool suspended;
    private bool boardInitialized;
    private SyncLighting? appliedSyncLighting;

    public LightingSettings Settings { get; private set; } = new();
    public LightingState? State { get; private set; }
    public bool Busy => gate.CurrentCount == 0;
    public bool AutomaticChangesAllowed { get; set; }
    public bool SyncApplied => !RestorePending
        && State is { Conflict: null, MemoryError: null, BoardError: null, Dimms.Count: > 0, Board: not null }
        && Settings is { Sync: true, SyncLighting: { } sync }
        && (SyncEngine.Animated(sync.Effect)
            ? engine is { Running: true, HasAppliedFrame: true, CoversBothTargets: true } && engine.Look == sync
            : appliedSyncLighting == sync);
    public bool RestorePending { get; private set; }
    public string? RestoreStatus { get; private set; }
    public event Action? Changed;

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var text = File.ReadAllText(SettingsPath);
            Settings = JsonSerializer.Deserialize<LightingSettings>(text) ?? new();
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty(nameof(LightingSettings.RetentionVersion), out _) &&
                (Settings.Memory != null || Settings.BoardZones != null || Settings.MemoryModules != null || Settings.SyncLighting != null))
            {
                if (!File.Exists(SettingsPath + ".before-retention-v1")) File.Copy(SettingsPath, SettingsPath + ".before-retention-v1");
                Save(Settings with { ApplyAtStartup = true, RainbowOnExit = false, RetentionVersion = 1 });
                host.Log("Lighting retention upgraded: saved colors remain on exit and restore on startup.");
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { host.Log("Lighting settings: " + error.Message); }
    }

    public void SaveStartupPreference(bool applyAtStartup) => Save(Settings with { ApplyAtStartup = applyAtStartup });

    // Persist intent before touching hardware: closing during the debounce or a device failure cannot lose edits.
    public void RememberEdits(MemoryLighting? memory, IReadOnlyList<ModuleLighting>? modules,
        IReadOnlyList<BoardZoneLighting>? board, SyncLighting? sync)
    {
        lock (settingsGate)
        {
            var next = MergeAppliedSettings(Settings, memory, modules, board, null, Settings.ApplyAtStartup);
            if (sync != null)
            {
                var zones = State?.BoardZones ?? GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel());
                next = next with { Sync = true, SyncLighting = sync, Memory = sync.ToMemory(),
                    MemoryModules = State?.Dimms.Select(d => new ModuleLighting(d.Slot, d.Name, d.ChipId, sync.ToMemory())).ToArray(),
                    BoardZones = sync.ToBoard(zones.Select(z => z.Number)) };
            }
            Save(next);
            editRevision++;
        }
    }

    public void RestoreEdits(LightingSettings previous) { lock (settingsGate) { Save(previous); editRevision++; } }

    /// <summary>
    /// Sync on or off is a page preference; the hardware changes only on Apply. Turning it off stops the sync engine; if it was
    /// animating, the devices then get their saved closest built-in effects (Breathing, Pulse, Color cycle...), which the sync Apply
    /// stored, instead of freezing on the last frame. True when that happened.
    /// </summary>
    public async Task<bool> SaveSyncPreferenceAsync(bool sync)
    {
        var animating = !sync && SyncRunning;
        Save(Settings with { Sync = sync, SyncLighting = sync ? null : Settings.SyncLighting });
        if (sync) return false;
        StopEngine();
        if (!animating || Settings is { Memory: null, MemoryModules: null, BoardZones: null }) return false;
        await ApplyAsync(Settings.MemoryModules == null ? Settings.Memory : null, Settings.BoardZones, Settings.ApplyAtStartup, Settings.MemoryModules);
        return true;
    }

    /// <summary>
    /// The motherboard keeps its lighting through a shutdown (GCC's SaveLedParameter, CC 47 01, which GCC sends when it closes) or
    /// goes back to its built-in lighting (CC 47 00, which GCC sends when it connects). Only on the user's request.
    /// With keep on, the board first gets its full saved look (for an animated sync, the closest built-in effects), and Sora sends
    /// 47 01 again after each board Apply and before it closes, so the board keeps what the user applied, never a sync frame.
    /// The RAM has no counterpart Sora will use: its only save is the ENE controller's flash (0x80A0 = 0xAA), which GCC never writes.
    /// </summary>
    public Task<LightingState> KeepOnBoardAsync(bool keep) => Run(() =>
    {
        if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
        try
        {
            using var controller = new GigabyteRgb();
            var applied = keep && PutSavedBoardLook(controller);
            controller.KeepAfterRestart(keep);
            host.Log(keep ? $"Lighting: motherboard keeps its lighting after a restart (47 01){(applied ? ", saved look re-applied first" : ", current lighting")}" : "Lighting: motherboard back to its built-in lighting after a restart (47 00)");
        }
        catch (System.ComponentModel.Win32Exception error) { boardInitialized = false; throw new IOException("Motherboard: " + error.Message, error); }
        Save(Settings with { KeepOnBoard = keep });
        return Scan(out _);
    });

    /// <summary>
    /// The saved look on every board light, with the controller initialization GCC uses. False (nothing sent) when the saved
    /// look does not cover every light: the board then keeps what it shows rather than a guess.
    /// </summary>
    private bool PutSavedBoardLook(GigabyteRgb controller)
    {
        var catalog = GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel());
        if (Settings.BoardZones is not { } zones || !zones.Select(z => z.Zone).Order().SequenceEqual(catalog.Select(z => z.Number).Order())) return false;
        var actual = BoardEffects(zones);
        controller.ApplyZones(actual, catalog, initialize: true);
        boardInitialized = !GigabyteRgb.CanUseGlobalRainbow(actual, catalog);
        return true;
    }

    /// <summary>
    /// Before a shutdown or sleep: if the board should keep its lighting but is showing a sync frame, the sync stops and the saved
    /// look goes back with 47 01, so the board does not start on a frame. Best effort, a second or two at most.
    /// </summary>
    public void SettleBoardForPowerOff()
    {
        if (!Settings.KeepOnBoard || !SyncRunning) return;
        StopEngine();
        if (EneDram.FindConflict() != null || !gate.Wait(2000)) return;
        try
        {
            using var controller = new GigabyteRgb();
            if (PutSavedBoardLook(controller)) controller.KeepAfterRestart(true);
            host.Log("Lighting: sync stopped for power-off; motherboard keeps the saved look");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { host.Log("Lighting before power-off: " + error.Message); }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Applies one look to the RAM and every board light (skipping a device that is not available) and remembers it.
    /// Static, Rainbow and Off are the devices' own effects; Color cycle, Breathing and Flashing run in the sync engine.
    /// </summary>
    public async Task<LightingState> ApplySyncAsync(SyncLighting sync, bool memory, bool board, bool applyAtStartup)
    {
        appliedSyncLighting = null;
        if (EneDram.FindConflict() is { } activeConflict) throw new InvalidOperationException(activeConflict);
        // Saved first, so the engine restart at the end of Run uses this look (and does not resume an older one).
        var availableZones = State?.BoardZones ?? GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel());
        Save(Settings with { Sync = true, SyncLighting = sync, ApplyAtStartup = applyAtStartup });
        if (SyncEngine.Animated(sync.Effect))
        {
            Save(Settings with { Memory = memory ? sync.ToMemory() : Settings.Memory,
                MemoryModules = memory ? State?.Dimms.Select(d => new ModuleLighting(d.Slot, d.Name, d.ChipId, sync.ToMemory())).ToArray() : Settings.MemoryModules,
                BoardZones = board ? sync.ToBoard(availableZones.Select(z => z.Number)) : Settings.BoardZones });
            if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
            var state = await Run(() =>
            {
                // The board keeps the closest built-in effect, not whichever frame is showing when the PC turns off.
                if (board && Settings.KeepOnBoard)
                    try { using var controller = new GigabyteRgb(); if (PutSavedBoardLook(controller)) controller.KeepAfterRestart(true); }
                    catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { host.Log("Lighting keep on board: " + error.Message); }
                return Scan(out _);
            });
            await WaitForSyncFrameAsync(CancellationToken.None);
            return state;
        }
        var zones = State?.BoardZones ?? GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel());
        try
        {
            var state = await ApplyAsync(memory ? sync.ToMemory() : null, board ? sync.ToBoard(zones.Select(z => z.Number)) : null, applyAtStartup);
            if (memory && board) appliedSyncLighting = sync;
            Changed?.Invoke();
            return state;
        }
        catch { StopEngine(); throw; } // Keep the saved intent pending so it can be retried.
    }

    public void SaveExitPreference(bool rainbowOnExit) => Save(Settings with { RainbowOnExit = rainbowOnExit });
    public void SaveMemorySyncPreference(bool sync) => Save(Settings with { MemorySync = sync });
    public void SetSuspended(bool value) { suspended = value; if (value) { appliedSyncLighting = null; SettleBoardForPowerOff(); boardInitialized = false; StopEngine(); } }

    /// <summary>
    /// When Sora closes, like GCC on exit: stop the sync engine and hand the lights back in their default, the RAM's own Rainbow
    /// (its original registers from the backup, read back) and GCC's rainbow wave on the board. Nothing is saved to either device.
    /// Skipped if no lighting is configured or another RGB program is running. Slow cleanup is logged;
    /// the hardware gate stays held until the worker has actually closed its handles.
    /// </summary>
    public async Task ExitLightingAsync()
    {
        // A board that keeps its lighting must not keep a sync frame: it gets the saved look back instead of the rainbow wave.
        var settleBoard = Settings.KeepOnBoard && SyncRunning;
        StopEngine();
        var rainbow = Settings.RainbowOnExit && Settings is not { Memory: null, BoardZones: null, Sync: false };
        if (!rainbow && !settleBoard || EneDram.FindConflict() != null) return;
        if (!await gate.WaitAsync(3000)) { host.RecordCleanupFailure("Lighting exit could not acquire the hardware gate."); return; }
        try
        {
            var work = Task.Run(() =>
            {
                if (rainbow) try
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
                catch (Exception error) when (error is not OutOfMemoryException) { host.RecordCleanupFailure("Lighting on exit, memory: " + error.Message); }
                try
                {
                    using var controller = new GigabyteRgb();
                    if (Settings.KeepOnBoard)
                    {
                        if (settleBoard && PutSavedBoardLook(controller)) controller.KeepAfterRestart(true);
                        host.Log("Lighting on exit: motherboard keeps the saved look");
                    }
                    else
                    {
                        var zones = GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel());
                        controller.ApplyZones(zones.Select(z => new BoardZoneEffect(z.Number, BoardEffect.Rainbow, 0, 0, 0, 8, 8)).ToArray(), zones);
                        host.Log("Lighting on exit: motherboard rainbow wave");
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException) { host.RecordCleanupFailure("Lighting on exit, motherboard: " + error.Message); }
            });
            await CleanupSequence.JoinAsync(work, TimeSpan.FromSeconds(6),
                () => host.Log("Lighting exit is still running; waiting for its hardware handles to close."));
        }
        finally { gate.Release(); }
    }

    /// <summary>True while Sora is driving an animated sync effect.</summary>
    public bool SyncRunning => engine?.Running == true;
    internal CoolerLighting CoolerPreviewLighting
    {
        get
        {
            var active = engine;
            return CoolerLighting.From(Settings, active?.BoardFrame, active?.Running == true);
        }
    }

    public void StopEngine()
    {
        if (engine != null) boardInitialized = false;
        engine?.Stop();
        engine = null;
    }

    /// <summary>Starts the sync engine when sync is on with an animated effect and no other RGB program is running.</summary>
    internal static bool ShouldStartSyncEngine(bool automaticChangesAllowed, bool suspended, LightingSettings settings, bool running) =>
        automaticChangesAllowed && !suspended && !running && settings is { Sync: true, SyncLighting: { } sync } && SyncEngine.Animated(sync.Effect);

    private void StartEngineIfNeeded()
    {
        if (!ShouldStartSyncEngine(AutomaticChangesAllowed, suspended, Settings, engine?.Running == true)) return;
        if (EneDram.FindConflict() != null) return;
        engine = new SyncEngine(host, Settings.SyncLighting!, State == null || State.Dimms.Count > 0, State == null || State.Board != null);
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
    public Task<LightingState> ApplyAsync(MemoryLighting? memory, IReadOnlyList<BoardZoneLighting>? board, bool applyAtStartup, IReadOnlyList<ModuleLighting>? modules = null) => Run(() =>
    {
        appliedSyncLighting = null;
        long revision; lock (settingsGate) revision = editRevision;
        if (EneDram.FindConflict() is { } conflict) throw new InvalidOperationException(conflict);
        var errors = new List<string>();
        MemoryLighting? memoryDone = null;
        IReadOnlyList<BoardZoneLighting>? boardDone = null;
        IReadOnlyList<ModuleLighting>? modulesDone = null;
        IReadOnlyList<ModuleLighting>? legacyModules = null;
        if (memory != null || modules is { Count: > 0 })
        {
            try
            {
                if (modules != null && modules.Select(m => m.Slot).Distinct().Count() != modules.Count) throw new IOException("Memory module settings contain duplicate slots.");
                using var bus = Smbus.Open();
                var dimms = ScanFor(bus);
                if (Settings.MemoryModules == null && Settings.Memory is { } legacy)
                    legacyModules = dimms.Select(d => new ModuleLighting(d.Slot, d.Name, d.ChipId, legacy)).ToArray();
                // Resolve every identity before the first write; a moved or replaced stick must not receive another stick's theme.
                var targets = modules != null ? modules.Select(m => (Dimm: dimms.SingleOrDefault(d => d.Slot == m.Slot && d.Name == m.Name && d.ChipId == m.ChipId)
                    ?? throw new IOException($"The saved memory module in slot {m.Slot} no longer matches. Check memory again."), Look: m.Lighting)).ToArray()
                    : dimms.Select(d => (Dimm: d, Look: memory!)).ToArray();
                // One write for every target: the apply bytes go out back to back, so the sticks' own effects start in step.
                try { EneDram.Apply(bus, targets.Select(t => Target(t.Dimm, t.Look)).ToArray(), EneColorOrder.Rbg); }
                catch { EneDram.Restore(bus, targets.Select(t => t.Dimm).ToArray()); throw; }
                modulesDone = targets.Select(t => new ModuleLighting(t.Dimm.Slot, t.Dimm.Name, t.Dimm.ChipId, t.Look)).ToArray();
                host.Log($"Lighting: {targets.Length} memory module(s) applied together and read back OK ({string.Join(", ", targets.Select(t => $"slot {t.Dimm.Slot} {t.Look.Effect} {t.Look.Color}"))})");
                memoryDone = memory;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException) { errors.Add("Memory: " + error.Message); host.Log("Lighting memory: " + error); }
        }
        if (board is { Count: > 0 })
        {
            try
            {
                using var controller = new GigabyteRgb();
                var catalog = GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel());
                var complete = MergeAppliedSettings(Settings, null, null, board, null, applyAtStartup).BoardZones!;
                var canInitialize = !boardInitialized && complete.Select(z => z.Zone).Order().SequenceEqual(catalog.Select(z => z.Number).Order());
                var actual = BoardEffects(canInitialize ? complete : board);
                controller.ApplyZones(actual, catalog, initialize: canInitialize);
                boardInitialized = (boardInitialized || canInitialize) && !GigabyteRgb.CanUseGlobalRainbow(actual, catalog);
                // Asked for by the user (Keep after shutdown): the board keeps what was just applied, as after GCC's exit.
                if (Settings.KeepOnBoard) controller.KeepAfterRestart(true);
                if (canInitialize) host.Log("Lighting: initialized board controller and restored every saved zone before isolated edits.");
                host.Log("Lighting: board " + string.Join(", ", board.Select(z => $"zone {z.Zone} {z.Effect} {z.Color} b{z.Brightness} s{z.Speed}")));
                boardDone = board;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { boardInitialized = false; errors.Add("Motherboard: " + error.Message); host.Log("Lighting board: " + error); }
        }
        // Only what reached the hardware is saved, so a failed section is not re-sent at every sign-in.
        // "with", not a new record: a new one silently dropped Sync and turned sync mode off after every Apply (0.6.6-0.6.8).
        lock (settingsGate)
            if (revision == editRevision) Save(MergeAppliedSettings(Settings, memoryDone, modulesDone, boardDone, legacyModules, applyAtStartup));
        if (errors.Count > 0) throw new IOException(string.Join(" ", errors));
        return Scan(out _);
    });

    /// <summary>Merges confirmed sections only. Legacy whole-memory looks are materialized before a first partial module edit.</summary>
    internal static LightingSettings MergeAppliedSettings(LightingSettings before, MemoryLighting? memoryDone,
        IReadOnlyList<ModuleLighting>? modulesDone, IReadOnlyList<BoardZoneLighting>? boardDone,
        IReadOnlyList<ModuleLighting>? legacyModules, bool applyAtStartup)
    {
        var mergedModules = modulesDone == null ? before.MemoryModules : memoryDone != null ? modulesDone
            : (before.MemoryModules ?? legacyModules ?? []).Where(m => modulesDone.All(n => n.Slot != m.Slot)).Concat(modulesDone).OrderBy(m => m.Slot).ToArray();
        var mergedBoard = boardDone == null ? before.BoardZones : (before.BoardZones ?? []).Where(z => boardDone.All(n => n.Zone != z.Zone)).Concat(boardDone).OrderBy(z => z.Zone).ToArray();
        return before with { Memory = memoryDone ?? before.Memory, MemoryModules = mergedModules, BoardZones = mergedBoard, ApplyAtStartup = applyAtStartup };
    }

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
        Save(Settings with { Memory = null, MemoryModules = null, Sync = false });
        return Scan(out _);
    });

    /// <summary>In sync mode the saved common effect is authoritative over older individual target looks.</summary>
    internal static LightingSettings ResolveStartupLook(LightingSettings settings, IReadOnlyList<BoardZone> catalog) =>
        settings is { Sync: true, SyncLighting: { } sync }
            ? settings with { Memory = sync.ToMemory(), MemoryModules = null, BoardZones = sync.ToBoard(catalog.Select(z => z.Number)) }
            : settings;

    private async Task WaitForSyncFrameAsync(CancellationToken token)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (engine is { Running: true, HasAppliedFrame: true } && engine.Look == Settings.SyncLighting) return;
            if (!SyncRunning) break;
            await Task.Delay(10, token);
        }
        throw new IOException("Waiting for the lighting engine to apply its first complete frame.");
    }

    /// <summary>Restores saved lighting independently of the window; unavailable devices remain pending until ready.</summary>
    public async Task ApplyAtStartupAsync(CancellationToken token)
    {
        if (!Settings.ApplyAtStartup) return;
        RestorePending = true;
        try
        {
            for (var attempt = 0; !token.IsCancellationRequested && Settings.ApplyAtStartup; attempt++)
            {
                if (attempt > 0) await Task.Delay(20000, token);
                token.ThrowIfCancellationRequested();
                if (!Settings.ApplyAtStartup) return;
                var animated = Settings is { Sync: true, SyncLighting: { } sync } && SyncEngine.Animated(sync.Effect);
                if (!animated && Settings.Memory == null && Settings.MemoryModules == null && Settings.BoardZones == null) return;
                try
                {
                    RestoreStatus = "Restoring saved lighting…";
                    if (EneDram.FindConflict() is { } conflict) throw new IOException(conflict);
                    if (animated)
                    {
                        var state = await RefreshAsync();
                        if (state.MemoryError != null || state.BoardError != null || !SyncRunning)
                            throw new IOException(string.Join(" ", new[] { state.MemoryError, state.BoardError, SyncRunning ? null : "Waiting for the lighting engine." }.Where(s => s != null)));
                        await WaitForSyncFrameAsync(token);
                    }
                    else
                    {
                        var desired = ResolveStartupLook(Settings, State?.BoardZones ?? GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel()));
                        await ApplyAsync(desired.MemoryModules == null ? desired.Memory : null, desired.BoardZones, true, desired.MemoryModules);
                        if (desired is { Sync: true, SyncLighting: { } applied }) appliedSyncLighting = applied;
                    }
                    RestoreStatus = "Saved lighting restored.";
                    host.Log("Lighting: saved startup / resume settings restored.");
                    return;
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or TimeoutException)
                {
                    RestoreStatus = "Saved lighting is waiting to apply: " + error.Message + " Retrying automatically.";
                    host.Log(RestoreStatus);
                    Changed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { RestorePending = false; Changed?.Invoke(); }
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

    /// <summary>A saved look as controller values: brightness scales the single-colour effects, speed 1-5 is the ENE's 4-0.</summary>
    private static EneTarget Target(EneDimm dimm, MemoryLighting look)
    {
        var (red, green, blue) = Scale(ParseColor(look.Color), look.Effect is EneMode.Static or EneMode.Breathing or EneMode.Flashing ? look.Brightness : 100);
        return new EneTarget(dimm, look.Effect, red, green, blue, (byte)(5 - Math.Clamp(look.Speed, 1, 5)), look.Reverse);
    }

    private static IReadOnlyList<EneDimm> ScanFor(Smbus bus)
    {
        var dimms = EneDram.Scan(bus).Dimms;
        return dimms.Count > 0 ? dimms : throw new IOException("No RGB memory controller was found.");
    }

    private void Save(LightingSettings settings)
    {
        lock (settingsGate)
        {
            try
            {
                Directory.CreateDirectory(host.DataDirectory);
                File.WriteAllText(SettingsPath + ".tmp", JsonSerializer.Serialize(settings, Json));
                File.Move(SettingsPath + ".tmp", SettingsPath, true);
                Settings = settings;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { host.Log("Cannot save lighting settings: " + error.Message); throw new IOException("Lighting changes could not be saved: " + error.Message, error); }
        }
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
