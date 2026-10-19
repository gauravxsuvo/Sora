using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

internal sealed class GpuFanService
{
    private sealed record StoredProfile(string Identity, GpuFanProfile Profile, GpuFanBackup? NativeTarget = null);
    private readonly SoraHost host;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, GpuFanProfile> active = new();
    private readonly Dictionary<string, GpuFanBackup> originals = new();
    private readonly HashSet<string> recoveryPending = new();
    private readonly JsonSerializerOptions json = new() { WriteIndented = true };
    private readonly Timer timer;
    private readonly Func<IGpuFanAccess> createAccess;
    private readonly Action checkConflict;
    private readonly bool enableTimer;
    private IGpuFanAccess? access;
    private StoredProfile? desired;
    private bool stopping, loaded, invalidBackup, suspended;
    private int startupRunning;
    public bool Busy { get; private set; }
    public bool HasRead { get; private set; }
    public bool HasPendingEdits => active.Count > 0 || originals.Count > 0;
    public bool StartupPending { get; private set; }
    public GpuFanSnapshot[] Snapshots { get; private set; } = Array.Empty<GpuFanSnapshot>();
    public string Message { get; private set; } = "Read the NVIDIA driver to detect fan channels.";
    public event Action? Changed;
    public event Action<string>? StatusChanged;
    public GpuFanProfileLibrary Profiles => new(host.DataDirectory);
    public bool IsActive(string identity) => !suspended && active.ContainsKey(identity);
    public GpuFanProfile? CurrentProfile(string identity) { Load(); return active.GetValueOrDefault(identity) ?? (desired?.Identity == identity ? desired.Profile : null); }
    public string LifecycleDescription(string identity)
    {
        var profile = CurrentProfile(identity);
        if (profile == null) return "Editor ready · Apply saves this profile for automatic reapplication at startup and after resume.";
        if (!IsActive(identity)) return StartupPending ? "Saved profile · waiting to reapply to this GPU." : "Saved profile · reapplies on the next normal launch or resume.";
        return IsNative(profile) ? "Applied native profile · stays on the GPU after Exit; reapplies at startup and after resume." : "Applied software profile · Sora must run; safely restores original on Exit and reapplies next launch.";
    }
    private string BackupPath => Path.Combine(host.DataDirectory, "gpu-fans-original.json");
    private string ProfilePath => Path.Combine(host.DataDirectory, "gpu-fans-profile.json");
    public GpuFanService(SoraHost host, Func<IGpuFanAccess>? createAccess = null, Action? checkConflict = null, bool enableTimer = true)
    {
        this.host = host; this.createAccess = createAccess ?? (() => new NvidiaGpuFans()); this.checkConflict = checkConflict ?? CheckConflict; this.enableTimer = enableTimer;
        timer = new Timer(_ => _ = TickAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }
    private IGpuFanAccess Access => access ??= createAccess();
    private static bool IsNative(GpuFanProfile profile) => profile.Mode is GpuFanMode.HardwareCurve or GpuFanMode.HardwareFixed;
    public void Load()
    {
        if (loaded) return; loaded = true;
        try
        {
            if (File.Exists(BackupPath))
            {
                if (new FileInfo(BackupPath).Length > 1024 * 1024) throw new InvalidDataException("GPU backup exceeds 1 MiB.");
                var backups = JsonSerializer.Deserialize<GpuFanBackup[]>(File.ReadAllText(BackupPath)) ?? throw new InvalidDataException("Missing GPU backup entries.");
                var validated = new Dictionary<string, GpuFanBackup>();
                foreach (var backup in backups) { NvidiaGpuFans.ValidateBackup(backup); if (!validated.TryAdd(backup.Identity, backup)) throw new InvalidDataException("Duplicate GPU backup identity."); }
                foreach (var backup in validated.Values) { originals.Add(backup.Identity, backup); recoveryPending.Add(backup.Identity); }
                Message = "Saved original GPU settings will be recovered before the last applied profile is reapplied.";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { invalidBackup = true; Message = "Cannot load GPU fan backup; automatic and manual changes are blocked: " + e.Message; host.Log(Message); }
        try
        {
            if (File.Exists(ProfilePath))
            {
                if (new FileInfo(ProfilePath).Length > 65536) throw new InvalidDataException("GPU applied profile exceeds 64 KiB.");
                var saved = JsonSerializer.Deserialize<StoredProfile>(File.ReadAllText(ProfilePath)) ?? throw new InvalidDataException("Missing GPU applied profile.");
                if (string.IsNullOrWhiteSpace(saved.Identity) || saved.Profile == null) throw new InvalidDataException("Missing applied GPU identity/profile.");
                saved.Profile.Validate();
                if (saved.NativeTarget != null) { NvidiaGpuFans.ValidateBackup(saved.NativeTarget); if (saved.NativeTarget.Identity != saved.Identity || !IsNative(saved.Profile)) throw new InvalidDataException("GPU saved target identity or mode mismatch."); }
                desired = saved; StartupPending = true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Message = "Cannot load the last GPU profile: " + e.Message; host.Log(Message); }
    }
    private void Notify(string message) { Message = message; Changed?.Invoke(); StatusChanged?.Invoke(message); }
    private void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(host.DataDirectory); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, json)); File.Move(path + ".tmp", path, true);
    }
    private void SaveBackups()
    {
        if (originals.Count == 0) { if (File.Exists(BackupPath)) File.Delete(BackupPath); }
        else Save(BackupPath, originals.Values.ToArray());
    }
    private void StartTimer() { if (enableTimer && !stopping && !suspended) timer.Change(2000, 2000); }
    private GpuFanSnapshot ReadSelected(string identity)
    {
        Snapshots = Access.Read(); HasRead = true;
        var snapshot = Snapshots.SingleOrDefault(s => s.Identity == identity) ?? throw new IOException("The saved GPU is not connected; profile retained for retry.");
        if (!snapshot.CanControl) throw new NotSupportedException(snapshot.Notice);
        return snapshot;
    }
    public async Task ReadAsync(bool foreground = true)
    {
        await gate.WaitAsync();
        try
        {
            if (stopping) return; Busy = foreground; if (foreground) Changed?.Invoke(); Load();
            Snapshots = await Task.Run(() => Access.Read()); HasRead = true;
            if (active.Count == 0 && !StartupPending && !invalidBackup) Notify(Snapshots.Length == 0 ? "No NVIDIA GPU detected. AMD and Intel GPU fan writes are not implemented." : recoveryPending.Count > 0 ? "Original GPU backup available for recovery." : "GPU temperatures and fan channels read. No settings changed.");
            else Changed?.Invoke();
        }
        catch (Exception e) { HasRead = true; Snapshots = Array.Empty<GpuFanSnapshot>(); Notify("GPU read failed: " + e.Message); host.Log(Message); }
        finally { Busy = false; gate.Release(); Changed?.Invoke(); }
    }
    private static void CheckConflict()
    {
        foreach (string name in new[] { "GCC", "GCC32", "GvLedService", "MSIAfterburner", "PrecisionX_x64", "FanControl" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) throw new InvalidOperationException($"Close {name} before changing GPU fans; another controller can overwrite Sora."); }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
    private void EnsureWritable() { if (invalidBackup) throw new InvalidDataException("The original GPU backup is invalid; it will not be overwritten."); checkConflict(); }
    private GpuFanBackup Compile(GpuFanBackup current, GpuFanSnapshot snapshot, GpuFanProfile profile)
    {
        if (profile.Mode != GpuFanMode.Automatic && snapshot.TemperatureC == null) throw new IOException("GPU temperature is unavailable; manual control is blocked.");
        return IsNative(profile) ? GpuNativeFanProtocol.Compile(current, snapshot, profile) : NvidiaGpuFans.WithDuty(profile.Mode == GpuFanMode.Automatic && originals.TryGetValue(snapshot.Identity, out var first) ? first : current, snapshot, profile.Mode == GpuFanMode.Automatic ? null : profile.DutyAt(snapshot.TemperatureC!.Value));
    }
    private void RecoverStaleBackups()
    {
        foreach (var identity in recoveryPending.ToArray())
        {
            ReadSelected(identity);
            GpuFanTransactions.Apply(Access, originals[identity]); recoveryPending.Remove(identity);
            host.Log("GPU original-state recovery verified for " + identity + ".");
        }
    }
    private void Program(string identity, GpuFanProfile profile, GpuFanBackup? savedTarget = null)
    {
        EnsureWritable(); var snapshot = ReadSelected(identity); var before = Access.Capture(identity);
        if (profile.Mode != GpuFanMode.Automatic && snapshot.TemperatureC == null) throw new IOException("GPU temperature is unavailable; profile reapplication is blocked.");
        var target = savedTarget ?? Compile(before, snapshot, profile); NvidiaGpuFans.ValidateBackup(target);
        if (target.Identity != identity) throw new InvalidDataException("Saved GPU target identity mismatch.");
        bool addedBackup = !originals.ContainsKey(identity);
        if (addedBackup)
        {
            originals.Add(identity, before);
            try { SaveBackups(); } catch { originals.Remove(identity); throw; }
        }
        try
        {
            GpuFanTransactions.Apply(Access, target);
            var next = new StoredProfile(identity, profile, IsNative(profile) ? target : null);
            Save(ProfilePath, next); desired = next; active[identity] = profile; StartupPending = false;
        }
        catch (Exception error)
        {
            try
            {
                GpuFanTransactions.Apply(Access, before);
                if (addedBackup) { originals.Remove(identity); SaveBackups(); }
            }
            catch (Exception rollbackError) { throw new AggregateException("GPU apply failed and restoration is pending; original backup retained.", error, rollbackError); }
            throw;
        }
        StartTimer();
        try { Snapshots = Access.Read(); HasRead = true; }
        catch (Exception error) { Snapshots = Array.Empty<GpuFanSnapshot>(); host.Log("GPU profile applied and read back; subsequent telemetry refresh failed: " + error.Message); }
    }
    public async Task ApplyAsync(string identity, GpuFanProfile profile)
    {
        profile.Validate(); await gate.WaitAsync();
        try
        {
            if (stopping || suspended) return; Busy = true; Changed?.Invoke(); Load(); EnsureWritable();
            if (recoveryPending.Count > 0) throw new InvalidOperationException("Original-state recovery is pending. Restore original before applying another profile.");
            if (IsNative(profile) && profile.FanIndices.Length < 3 && active.TryGetValue(identity, out var previous) && previous.Mode is GpuFanMode.Fixed or GpuFanMode.Curve) throw new InvalidOperationException("Restore the software-controlled GPU profile or select all native fan channels before switching modes.");
            await Task.Run(() => Program(identity, profile));
            Notify(IsNative(profile) ? "Native RPM profile applied and read back. It stays on the GPU after Exit and reapplies at startup / resume." : "GPU profile applied and read back. It reapplies at startup / resume; software control safely restores original on Exit.");
        }
        catch (Exception e) { Notify("GPU apply failed: " + e.Message); host.Log(Message); }
        finally { Busy = false; gate.Release(); Changed?.Invoke(); }
    }
    /// <summary>Normal host startup/resume only; diagnostics never call this. Recover existing originals before taking ownership in a new session.</summary>
    public async Task ApplyAtStartupAsync(CancellationToken token = default)
    {
        if (Interlocked.Exchange(ref startupRunning, 1) != 0) return;
        try
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                token.ThrowIfCancellationRequested(); await gate.WaitAsync(token);
                try
                {
                    if (stopping) return; suspended = false; Load();
                    if (desired == null && recoveryPending.Count == 0) return;
                    StartupPending = true; Busy = true; Changed?.Invoke(); EnsureWritable();
                    await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested(); RecoverStaleBackups(); token.ThrowIfCancellationRequested();
                        if (desired is { } saved) Program(saved.Identity, saved.Profile, saved.NativeTarget);
                        else { originals.Clear(); SaveBackups(); }
                    }, token);
                    StartupPending = false;
                    Notify(desired == null ? "Original GPU fan state recovered; no startup profile is selected." : "Last applied GPU profile reapplied and read back after startup / resume.");
                    host.Log(Message); return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    Notify($"GPU startup / resume attempt {attempt}/3 failed: {e.Message}" + (attempt == 3 ? " Saved profile and original backup retained; retry after the GPU is available." : " Retrying…")); host.Log(Message);
                }
                finally { Busy = false; gate.Release(); Changed?.Invoke(); }
                if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(attempt), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { host.Log("GPU startup / resume reapplication cancelled; saved state retained."); }
        finally { Interlocked.Exchange(ref startupRunning, 0); }
    }
    private async Task TickAsync()
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            if (stopping || suspended || active.Count == 0) return;
            await Task.Run(() =>
            {
                try
                {
                    checkConflict(); Snapshots = Access.Read();
                    foreach (var (identity, profile) in active.ToArray())
                    {
                        var snapshot = Snapshots.SingleOrDefault(s => s.Identity == identity) ?? throw new IOException("The controlled GPU disconnected.");
                        if (profile.Mode is GpuFanMode.Automatic or GpuFanMode.HardwareCurve or GpuFanMode.HardwareFixed) continue;
                        if (snapshot.TemperatureC == null) throw new IOException("GPU temperature lost; restoring original fan control.");
                        var current = Access.Capture(identity); var target = NvidiaGpuFans.WithDuty(current, snapshot, profile.DutyAt(snapshot.TemperatureC.Value));
                        if (!NvidiaGpuFans.Equivalent(target, current)) GpuFanTransactions.Apply(Access, target);
                    }
                }
                catch (Exception e)
                {
                    foreach (var identity in active.Keys.ToArray())
                    {
                        if (IsNative(active[identity])) continue;
                        try { GpuFanTransactions.Apply(Access, originals[identity]); active.Remove(identity); originals.Remove(identity); }
                        catch (Exception restoreError) { host.Log("GPU background restoration: " + restoreError); }
                    }
                    SaveBackups(); Notify("GPU controller read/control error: " + e.Message + " Saved profile retained for startup / resume."); host.Log(Message);
                }
            });
            Changed?.Invoke();
        }
        catch (Exception e) { host.Log("GPU background controller: " + e); }
        finally { gate.Release(); }
    }
    public async Task RestoreAsync(string identity)
    {
        await gate.WaitAsync();
        try
        {
            if (stopping) return; Busy = true; Changed?.Invoke(); Load(); EnsureWritable();
            if (originals.TryGetValue(identity, out var original)) { await Task.Run(() => { ReadSelected(identity); GpuFanTransactions.Apply(Access, original); }); originals.Remove(identity); recoveryPending.Remove(identity); SaveBackups(); }
            else if (desired?.Identity != identity) { Notify("No saved original control for this GPU."); return; }
            active.Remove(identity);
            if (desired?.Identity == identity) { if (File.Exists(ProfilePath)) File.Delete(ProfilePath); desired = null; StartupPending = false; }
            Snapshots = await Task.Run(() => Access.Read()); Notify("Original GPU fan control restored and read back. Automatic reapplication cleared; named profiles remain available.");
        }
        catch (Exception e) { Notify("GPU restore failed; backup retained: " + e.Message); host.Log(Message); }
        finally { Busy = false; gate.Release(); Changed?.Invoke(); }
    }
    public async Task StopAsync()
    {
        if (stopping) return; timer.Change(Timeout.Infinite, Timeout.Infinite); await gate.WaitAsync();
        try
        {
            if (stopping) return; stopping = true;
            foreach (var identity in active.Keys.ToArray())
            {
                if (IsNative(active[identity])) { host.Log("GPU native profile retained on Exit for " + identity + "; saved original remains available."); continue; }
                try { await Task.Run(() => GpuFanTransactions.Apply(Access, originals[identity])); originals.Remove(identity); active.Remove(identity); SaveBackups(); }
                catch (Exception e) { host.RecordCleanupFailure("GPU exit restoration failed; backup retained: " + e); }
            }
            access?.Dispose(); access = null; timer.Dispose();
        }
        finally { gate.Release(); }
    }
    public async Task SuspendAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (stopping || suspended) return; suspended = true; timer.Change(Timeout.Infinite, Timeout.Infinite);
            foreach (var (identity, profile) in active.ToArray())
            {
                if (!IsNative(profile))
                {
                    try { await Task.Run(() => GpuFanTransactions.Apply(Access, originals[identity])); host.Log("GPU software control restored before suspend for " + identity + "."); }
                    catch (Exception error) { host.Log("GPU suspend restoration failed; original backup retained: " + error); }
                }
            }
            foreach (var identity in originals.Keys) recoveryPending.Add(identity);
            StartupPending = desired != null; access?.Dispose(); access = null;
            Notify("GPU fan polling paused for sleep. Saved profile reapplies after resume.");
        }
        finally { gate.Release(); Changed?.Invoke(); }
    }
}
