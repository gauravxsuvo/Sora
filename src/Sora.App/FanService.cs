using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

internal sealed record FanPreferences
{
    public bool ApplyAtStartup { get; init; } = true;
    public bool RestoreOnExit { get; init; } = true;
    public bool TakePriority { get; init; } = true;
    public Dictionary<int, FanProfile> Profiles { get; init; } = new();
    public Dictionary<int, string> HeaderNames { get; init; } = new();
    public Dictionary<int, FanDeviceKind> HeaderDevices { get; init; } = new();
}
internal sealed record FanBackup(string Board, string Controller, Dictionary<int, FanRegisterValue[]> Headers);
internal sealed record FanFirmwareBackup(string Board, Dictionary<int, FanStopConfig> Headers);
internal sealed record FanReadResult(FanSnapshot Snapshot, bool VendorServiceInstalled, string? Notice, string? ConflictingProcess = null);

internal sealed class FanService(SoraHost host, Func<FanReadResult>? readHardware = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<int, FanRegisterValue[]> sessionOriginal = new();
    private readonly Dictionary<int, FanStopConfig> sessionFirmwareOriginal = new();
    private readonly JsonSerializerOptions json = new() { WriteIndented = true };
    private bool stopping;
    private bool preview;
    private bool preserveMessage;
    private bool actionBlocked;
    private readonly FanServicePriority priority = new(new WindowsFanPrioritySystem(), Path.Combine(host.DataDirectory, "fans-priority-session.json"));
    private readonly HashSet<int> activeHeaders = new();
    private DateTime priorityCheckedAt;
    public bool CanTakePriority => Preferences.TakePriority && ConflictingProcess is "EasyTuneEngineService" or "FanControlService";
    public bool VendorServicePaused { get; private set; }
    public bool VendorServiceInstalled { get; private set; } = true;
    private CancellationTokenSource? responseCancellation;
    public bool Busy { get; private set; }
    public bool Reading { get; private set; }
    public string? ConflictingProcess { get; private set; }
    internal bool IsPreview => preview;
    public string Message { get; private set; } = "Read the motherboard to see fan speeds and curves.";
    public FanSnapshot? Snapshot { get; private set; }
    public FanPreferences Preferences { get; private set; } = new();
    public bool Measuring => responseCancellation != null;
    public event Action? Changed;
    internal void SetPreview(FanSnapshot snapshot) { preview = true; Snapshot = snapshot; ConflictingProcess = null; Message = "Recorded hardware readings · preview only"; }
    private string SettingsPath => Path.Combine(host.DataDirectory, "fans.json");
    private string BackupPath => Path.Combine(host.DataDirectory, "fans-original.json");
    private string FirmwareBackupPath => Path.Combine(host.DataDirectory, "fans-firmware-original.json");
    private string ServiceOwnershipPath => Path.Combine(host.DataDirectory, "fans-service-ownership.json");

    public void Load()
    {
        try
        {
            if (File.Exists(ServiceOwnershipPath))
            {
                using var owner = JsonDocument.Parse(File.ReadAllText(ServiceOwnershipPath));
                VendorServicePaused = owner.RootElement.GetProperty("service").GetString() == "EasyTuneEngineService" && owner.RootElement.GetProperty("wasRunning").GetBoolean();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { host.Log("Fan service ownership record: " + e.Message); }
        try
        {
            if (File.Exists(SettingsPath)) Preferences = JsonSerializer.Deserialize<FanPreferences>(File.ReadAllText(SettingsPath)) ?? new();
            if (Preferences.Profiles == null || Preferences.HeaderNames == null || Preferences.HeaderDevices == null) throw new InvalidDataException("Missing fan preferences.");
            foreach (var (h, p) in Preferences.Profiles) { p.Validate(); if (h != p.Header) throw new InvalidDataException("Invalid header in fan settings."); }
            foreach (var (h, name) in Preferences.HeaderNames) ValidateDisplayName(h, name);
            foreach (var (h, kind) in Preferences.HeaderDevices)
                if (h is < 0 or > 5 || !Enum.IsDefined(kind)) throw new InvalidDataException("Unknown header device type.");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
        { Preferences = new(); Message = "Cannot load saved fan settings: " + e.Message; host.Log(Message); }
    }
    private void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(host.DataDirectory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, json));
        File.Move(path + ".tmp", path, true);
    }
    public void SetOptions(bool startup, bool restore, bool? takePriority = null)
    {
        var next = Preferences with { ApplyAtStartup = startup, RestoreOnExit = restore, TakePriority = takePriority ?? Preferences.TakePriority };
        Save(SettingsPath, next); Preferences = next; Changed?.Invoke();
    }
    public string DisplayName(int header) => Preferences.HeaderNames.TryGetValue(header, out var name) ? name : FanProtocol.HeaderNames[header];
    public FanDeviceKind DeviceKind(int header) => Preferences.HeaderDevices.TryGetValue(header, out var kind) ? kind : Preferences.Profiles.TryGetValue(header, out var profile) ? profile.DeviceKind : FanDeviceKind.Unknown;
    public void SetDeviceKind(int header, FanDeviceKind kind)
    {
        if (header is < 0 or > 5 || !Enum.IsDefined(kind)) throw new ArgumentException("Unknown header device type.");
        if (preview) throw new InvalidOperationException("Saving device types is disabled in screenshot previews.");
        if (Busy) throw new InvalidOperationException("Wait for the current fan operation to finish.");
        var devices = new Dictionary<int, FanDeviceKind>(Preferences.HeaderDevices) { [header] = kind };
        var next = Preferences with { HeaderDevices = devices };
        Save(SettingsPath, next); Preferences = next;
        Message = "Connected device type saved. Fan speeds and curves are unchanged."; preserveMessage = true; Changed?.Invoke();
    }
    private static void ValidateDisplayName(int header, string name)
    {
        if (header is < 0 or > 5 || name == null || name.Length > 48 || name.Any(char.IsControl))
            throw new ArgumentException("Use a display name of at most 48 characters without control characters.");
    }
    public void SetDisplayName(int header, string name)
    {
        name = name.Trim(); ValidateDisplayName(header, name);
        if (preview) throw new InvalidOperationException("Saving names is disabled in screenshot previews.");
        var names = new Dictionary<int, string>(Preferences.HeaderNames);
        if (name.Length == 0 || name == FanProtocol.HeaderNames[header]) names.Remove(header);
        else names[header] = name;
        var next = Preferences with { HeaderNames = names };
        Save(SettingsPath, next); Preferences = next;
        Message = "Header display name saved."; preserveMessage = true; Changed?.Invoke();
    }
    public Task<bool> PauseVendorServiceAsync() => RunAsync(() =>
    {
        if (preview) throw new InvalidOperationException("Service changes are disabled in screenshot previews.");
        priority.Acquire();
        ConflictingProcess = GigabyteFans.FindConflictProcess();
        return "Fan helpers paused with current settings preserved. Apply your curve; previously running services resume when Sora exits.";
    });
    public Task<bool> InstallStandaloneInterfaceAsync() => RunAsync(() =>
    {
        if (preview) throw new InvalidOperationException("Firmware-interface setup is disabled in screenshot previews.");
        GigabyteFanFirmware.InstallStandaloneInterface();
        if (Snapshot != null) Snapshot = GigabyteFanFirmware.Enrich(Snapshot);
        return "Sora's fan-only BIOS interface is registered. Firmware Zero-RPM no longer needs GCC's WMI definition.";
    });
    private void CheckConflict()
    {
        if (preview) throw new InvalidOperationException("Hardware writes are disabled in screenshot previews.");
        ConflictingProcess = GigabyteFans.FindConflictProcess();
        actionBlocked = ConflictingProcess != null;
        if (actionBlocked) throw new IOException("Fan settings were not applied. Exit " + ConflictingProcess + " before changing fan settings.");
    }
    private void PrepareControl()
    {
        if (preview) throw new InvalidOperationException("Hardware writes are disabled in screenshot previews.");
        if (Preferences.TakePriority) priority.Acquire();
        CheckConflict();
    }
    private async Task<bool> BeginOperationAsync()
    {
        if (stopping || Busy) return false;
        Busy = true; Changed?.Invoke();
        // A user action waits for an in-flight read instead of silently dropping the click.
        await gate.WaitAsync();
        if (!stopping) return true;
        Busy = false; gate.Release(); Changed?.Invoke(); return false;
    }
    private async Task<bool> RunAsync(Func<string> action)
    {
        if (!await BeginOperationAsync()) return false;
        actionBlocked = false;
        try
        {
            Message = await Task.Run(action);
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { Message = e.Message; host.Log("Fans: " + e); return false; }
        finally { preserveMessage = true; Busy = false; gate.Release(); Changed?.Invoke(); }
    }
    private static FanReadResult ReadHardware()
    {
        FanSnapshot snapshot;
        using (var fans = new GigabyteFans()) snapshot = fans.ReadSnapshot();
        bool installed = GigabyteFanServiceControl.IsInstalled;
        string? firmwareError = null;
        try { snapshot = GigabyteFanFirmware.Enrich(snapshot); }
        catch (Exception e) when (e is not OutOfMemoryException) { firmwareError = "Firmware Zero-RPM unavailable: " + e.Message; }
        return new(snapshot, installed, firmwareError, GigabyteFans.FindConflictProcess());
    }
    public async Task<bool> ReadAsync()
    {
        if (preview) return true;
        if (stopping || Busy || !await gate.WaitAsync(0)) return false;
        Reading = true;
        try
        {
            // Polls publish a complete snapshot once. They never disable or rebuild the editor.
            var result = await Task.Run(readHardware ?? ReadHardware);
            Snapshot = result.Snapshot; VendorServiceInstalled = result.VendorServiceInstalled; ConflictingProcess = result.ConflictingProcess;
            if (actionBlocked && ConflictingProcess == null) { actionBlocked = false; preserveMessage = false; }
            if (!preserveMessage) Message = "Live · " + Snapshot.Controller + " · " + (ConflictingProcess != null ? "Fan changes blocked by " + ConflictingProcess + "." : result.Notice ?? "Hardware curves keep running in the controller.");
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { Message = e.Message; preserveMessage = false; Snapshot = null; host.Log("Fan readings: " + e); return false; }
        finally { Reading = false; gate.Release(); Changed?.Invoke(); }
    }
    private void Backup(int header, FanRegisterValue[] values)
    {
        var backup = File.Exists(BackupPath) ? JsonSerializer.Deserialize<FanBackup>(File.ReadAllText(BackupPath)) ?? throw new InvalidDataException("Fan backup is empty.")
            : new FanBackup(GigabyteFans.SupportedBoard, "ITE IT8696E", new());
        if (backup.Board != GigabyteFans.SupportedBoard || backup.Controller != "ITE IT8696E" || backup.Headers == null)
            throw new InvalidDataException("Fan backup belongs to a different motherboard.");
        if (!backup.Headers.ContainsKey(header)) { backup.Headers.Add(header, values); Save(BackupPath, backup); }
        sessionOriginal.TryAdd(header, values);
    }
    private void BackupFirmware(int header, FanStopConfig config)
    {
        var backup = File.Exists(FirmwareBackupPath) ? JsonSerializer.Deserialize<FanFirmwareBackup>(File.ReadAllText(FirmwareBackupPath)) ?? throw new InvalidDataException("Firmware fan backup is empty.")
            : new FanFirmwareBackup(GigabyteFans.SupportedBoard, new());
        if (backup.Board != GigabyteFans.SupportedBoard || backup.Headers == null) throw new InvalidDataException("Firmware fan backup belongs to a different board.");
        if (!backup.Headers.ContainsKey(header)) { backup.Headers.Add(header, config); Save(FirmwareBackupPath, backup); }
        sessionFirmwareOriginal.TryAdd(header, config);
    }
    public Task<bool> ApplyAsync(FanProfile profile) => RunAsync(() =>
    {
        profile.Validate();
        if (DeviceKind(profile.Header) == FanDeviceKind.FixedSpeedPump || profile.DeviceKind == FanDeviceKind.FixedSpeedPump)
            throw new InvalidOperationException("This connection is marked as a fixed-speed pump. Sora monitors its RPM without applying a fan curve.");
        _ = FanProtocol.Compile(profile); // Reject an unrepresentable curve before any service transition.
        PrepareControl(); // No controller handles may be held while a helper stops.
        using var firmware = profile.FirmwareStop == null ? null : new GigabyteFanFirmware();
        var firmwareBefore = firmware?.Read(profile.Header);
        using var fans = new GigabyteFans();
        if (fans.ReadSnapshot().TemperaturesC[profile.TemperatureSource] == null)
            throw new IOException("The selected temperature source has no valid reading. Choose a live motherboard sensor.");
        var before = FanTransactions.Capture(fans, profile.Header);
        Backup(profile.Header, before);
        if (firmwareBefore != null) BackupFirmware(profile.Header, firmwareBefore);
        CheckConflict();
        try
        {
            FanTransactions.Apply(fans, profile);
            if (firmware != null) FanFirmwareTransactions.Apply(firmware, profile.Header, profile.FirmwareStop!);
            var profiles = new Dictionary<int, FanProfile>(Preferences.Profiles) { [profile.Header] = profile };
            var next = Preferences with { Profiles = profiles };
            Save(SettingsPath, next); Preferences = next;
            activeHeaders.Add(profile.Header);
        }
        catch (Exception applyError)
        {
            var errors = new List<Exception> { applyError };
            try { if (firmwareBefore != null) FanFirmwareTransactions.Restore(firmware!, profile.Header, firmwareBefore); } catch (Exception e) { errors.Add(e); }
            try { FanTransactions.Restore(fans, profile.Header, before); } catch (Exception e) { errors.Add(e); }
            if (errors.Count > 1) throw new AggregateException("Fan apply failed and restoration needs attention. Original settings are saved.", errors);
            throw new IOException("Fan apply failed. Previous native and firmware settings were restored and verified.", applyError);
        }
        Snapshot = fans.ReadSnapshot();
        if (firmware != null) Snapshot = GigabyteFanFirmware.Enrich(Snapshot);
        return DisplayName(profile.Header) + " applied and read back. Allow 10–15 seconds for RPM to settle. Original settings are backed up.";
    });
    public Task<bool> RestoreAsync(int header) => RunAsync(() =>
    {
        var backup = JsonSerializer.Deserialize<FanBackup>(File.ReadAllText(BackupPath)) ?? throw new InvalidDataException("No fan backup exists.");
        if (backup.Board != GigabyteFans.SupportedBoard || backup.Controller != "ITE IT8696E" || !backup.Headers.TryGetValue(header, out var values))
            throw new InvalidDataException("No original settings were saved for this header.");
        PrepareControl();
        using var fans = new GigabyteFans();
        if (File.Exists(FirmwareBackupPath))
        {
            var firmwareBackup = JsonSerializer.Deserialize<FanFirmwareBackup>(File.ReadAllText(FirmwareBackupPath)) ?? throw new InvalidDataException("Firmware fan backup is empty.");
            if (firmwareBackup.Board != GigabyteFans.SupportedBoard) throw new InvalidDataException("Firmware backup belongs to a different board.");
            if (firmwareBackup.Headers.TryGetValue(header, out var originalFirmware))
            {
                using var firmware = new GigabyteFanFirmware();
                FanFirmwareTransactions.Restore(firmware, header, originalFirmware);
                sessionFirmwareOriginal.Remove(header);
            }
        }
        FanTransactions.Restore(fans, header, values);
        sessionOriginal.Remove(header);
        activeHeaders.Remove(header);
        var profiles = new Dictionary<int, FanProfile>(Preferences.Profiles); profiles.Remove(header);
        var next = Preferences with { Profiles = profiles }; Save(SettingsPath, next); Preferences = next;
        Snapshot = fans.ReadSnapshot();
        Snapshot = GigabyteFanFirmware.Enrich(Snapshot);
        return FanProtocol.HeaderNames[header] + " original settings restored and verified.";
    });
    public async Task MaintainPriorityAsync()
    {
        if (preview || stopping || Busy || !Preferences.TakePriority || activeHeaders.Count == 0 || DateTime.UtcNow - priorityCheckedAt < TimeSpan.FromSeconds(5)) return;
        priorityCheckedAt = DateTime.UtcNow;
        try
        {
            // Ordinary checks never lock or rebuild the editor. A restarted helper triggers a handoff.
            if (!await Task.Run(() => priority.NeedsAcquire) || stopping || Busy) return;
            foreach (var header in activeHeaders.ToArray())
            {
                if (stopping) return;
                if (Preferences.Profiles.TryGetValue(header, out var profile) && !await ApplyAsync(profile)) return;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Message = "Cannot reclaim fan control: " + error.Message; preserveMessage = true; host.Log(Message); Changed?.Invoke(); }
    }
    public async Task ApplyAtStartupAsync(CancellationToken token)
    {
        if (!Preferences.ApplyAtStartup || token.IsCancellationRequested) return;
        foreach (var profile in Preferences.Profiles.Values.ToArray())
        {
            if (token.IsCancellationRequested || stopping) return;
            if (DeviceKind(profile.Header) == FanDeviceKind.FixedSpeedPump) continue;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (token.IsCancellationRequested || stopping) return;
                if (!Preferences.Profiles.TryGetValue(profile.Header, out var current)) break;
                if (await ApplyAsync(current)) break;
                host.Log($"Fan startup / resume attempt {attempt + 1}/3: {Message}");
                if (attempt < 2)
                    try { await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), token); }
                    catch (OperationCanceledException) { return; }
            }
        }
    }
    public void CancelResponseMeasurement() => responseCancellation?.Cancel();
    public async Task<bool> CalibrateMinimumAsync(int header, FanDeviceKind kind)
    {
        if (kind != FanDeviceKind.Fan || DeviceKind(header) is FanDeviceKind.Pump or FanDeviceKind.FixedSpeedPump) { Message = "Choose Fan as the device type to calibrate minimum duty. Pumps are excluded."; Changed?.Invoke(); return false; }
        if (!await BeginOperationAsync()) return false;
        responseCancellation = new CancellationTokenSource(); Changed?.Invoke();
        var cancellation = responseCancellation;
        bool touched = sessionOriginal.ContainsKey(header), firmwareTouched = sessionFirmwareOriginal.ContainsKey(header), success = false;
        try
        {
            await Task.Run(PrepareControl);
            var result = await FanCalibration.RunAsync(() => new FanCalibrationSession(header, (native, firmware) =>
            {
                Backup(header, native); if (firmware != null) BackupFirmware(header, firmware);
            }), sample =>
            {
                Message = $"Calibrating {FanProtocol.HeaderNames[header]} · {sample.Phase} · {sample.DutyPercent}% · {sample.Rpm:0} RPM"; Changed?.Invoke();
            }, cancellation.Token);
            Save(Path.Combine(host.DataDirectory, $"fan-calibration-{header}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"), result);
            Message = $"Calibration restored the original settings. Minimum running: {result.MinimumRunningDutyPercent}%. " +
                (result.MinimumStartingDutyPercent is { } start ? $"Minimum starting: {start}%. Allow at least 5% extra margin." : "Starting duty could not be established because the fan did not fully stop.");
            if (!touched) sessionOriginal.Remove(header);
            if (!firmwareTouched) sessionFirmwareOriginal.Remove(header);
            success = true;
        }
        catch (OperationCanceledException) { Message = "Minimum-duty calibration canceled; original settings restored."; }
        catch (Exception e) when (e is not OutOfMemoryException) { Message = e.Message; host.Log("Minimum-duty calibration: " + e); }
        finally { preserveMessage = true; responseCancellation = null; cancellation.Dispose(); Busy = false; gate.Release(); Changed?.Invoke(); }
        return success;
    }
    public async Task<bool> MeasureResponseAsync(int header)
    {
        if (DeviceKind(header) == FanDeviceKind.FixedSpeedPump) { Message = "RPM-response sweeps are disabled for fixed-speed pumps."; Changed?.Invoke(); return false; }
        if (!await BeginOperationAsync()) return false;
        responseCancellation = new CancellationTokenSource(); Changed?.Invoke();
        var cancellation = responseCancellation;
        FanRegisterValue[]? before = null;
        bool touched = sessionOriginal.ContainsKey(header), success = false;
        var samples = new List<FanResponseSample>();
        try
        {
            await Task.Run(() =>
            {
                PrepareControl();
                using var fans = new GigabyteFans();
                var state = fans.ReadSnapshot();
                if (state.Headers[header].Rpm is not > 0) throw new IOException("No spinning fan is detected on this header.");
                if (state.TemperaturesC[2] is not { } t || t >= 80) throw new IOException("Wait until the CPU is below 80°C before measuring fan response.");
                before = FanTransactions.Capture(fans, header); Backup(header, before);
            });
            foreach (int duty in new[] { 100, 90, 80, 70, 60, 50, 40 })
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await Task.Run(() =>
                {
                    CheckConflict();
                    using var fans = new GigabyteFans();
                    FanTransactions.Apply(fans, new FanProfile { Header = header, Mode = FanCurveMode.Fixed, FixedDutyPercent = duty, TemperatureSource = 2, FullSpeedC = 85 });
                });
                // Release ISA locks between reads; CPU sensing, lighting and other monitoring keep running.
                for (int wait = 0; wait < 4; wait++)
                {
                    await Task.Delay(1000, cancellation.Token);
                    Snapshot = await Task.Run(() =>
                    {
                        CheckConflict();
                        using var fans = new GigabyteFans();
                        var state = fans.ReadSnapshot();
                        if (state.TemperaturesC[2] is not { } t || t >= 85) throw new IOException("Response measurement stopped: CPU temperature is missing or at least 85°C.");
                        return state;
                    });
                    Message = $"Measuring {FanProtocol.HeaderNames[header]} · {duty}% · {Snapshot.Headers[header].Rpm:0} RPM";
                    Changed?.Invoke();
                }
                var speed = Snapshot!.Headers[header].Rpm;
                if (speed is not > 0) throw new IOException("Response measurement stopped because the fan stopped reporting RPM.");
                samples.Add(new(duty, speed.Value));
            }
            var path = Path.Combine(host.DataDirectory, $"fan-response-{header}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            Save(path, samples);
            Message = "RPM response (100–40%): " + string.Join(" · ", samples.Select(s => $"{s.DutyPercent}% = {s.Rpm:0}")) + ". Original settings restored.";
            success = true;
        }
        catch (OperationCanceledException) { Message = "Response measurement canceled. Restoring the original fan settings."; }
        catch (Exception e) when (e is not OutOfMemoryException) { Message = e.Message; host.Log("Fan response measurement: " + e); }
        finally
        {
            if (before != null)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        // We own these temporary changes: restoration must run even if a competing app has appeared.
                        using var fans = new GigabyteFans();
                        FanTransactions.Restore(fans, header, before);
                        Snapshot = fans.ReadSnapshot();
                    });
                    if (!touched) sessionOriginal.Remove(header);
                }
                catch (Exception e) when (e is not OutOfMemoryException) { success = false; Message = "Response measurement restore failed. Use Restore original. " + e.Message; host.Log(Message); }
            }
            preserveMessage = true; responseCancellation = null; cancellation.Dispose(); Busy = false; gate.Release(); Changed?.Invoke();
        }
        return success;
    }
    public async Task StopAsync()
    {
        if (stopping) return;
        stopping = true;
        CancelResponseMeasurement();
        await gate.WaitAsync();
        bool restored = true;
        try
        {
            if (!Preferences.RestoreOnExit || (sessionOriginal.Count == 0 && sessionFirmwareOriginal.Count == 0)) return;
            await Task.Run(() =>
            {
                PrepareControl();
                using var fans = new GigabyteFans();
                var errors = new List<Exception>();
                if (sessionFirmwareOriginal.Count > 0)
                {
                    try
                    {
                        using var firmware = new GigabyteFanFirmware();
                        foreach (var (h, config) in sessionFirmwareOriginal)
                            try { FanFirmwareTransactions.Restore(firmware, h, config); } catch (Exception e) { errors.Add(e); }
                    }
                    catch (Exception e) { errors.Add(e); }
                }
                foreach (var (h, values) in sessionOriginal)
                    try { FanTransactions.Restore(fans, h, values); } catch (Exception e) { errors.Add(e); }
                if (errors.Count > 0) throw new AggregateException("Could not restore every fan header.", errors);
            });
        }
        catch (Exception e) when (e is not OutOfMemoryException) { restored = false; host.RecordCleanupFailure("Fan exit restore: " + e); Message = "Fan exit restore failed: " + e.Message; }
        finally
        {
            if (restored)
            {
                try { await Task.Run(priority.Release); }
                catch (Exception e) when (e is not OutOfMemoryException) { restored = false; host.RecordCleanupFailure("Fan service handoff: " + e); Message = "Fan service handoff failed: " + e.Message; }
            }
            if (restored && VendorServicePaused)
            {
                try
                {
                    await Task.Run(GigabyteFanServiceControl.Resume);
                    File.Delete(ServiceOwnershipPath); VendorServicePaused = false;
                }
                catch (Exception e) when (e is not OutOfMemoryException) { host.RecordCleanupFailure("Cannot resume GCC fan service: " + e); Message = "Cannot resume GCC fan service: " + e.Message; }
            }
            gate.Release();
        }
    }
}

internal sealed record FanResponseSample(int DutyPercent, double Rpm);
