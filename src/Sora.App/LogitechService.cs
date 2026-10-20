using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

internal sealed class LogitechService
{
    private readonly SoraHost host;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim inputGate = new(1, 1);
    private readonly string directory;
    private readonly List<LogitechControlProfile> profiles = [];
    private LogitechDevice? device;
    private LogitechSnapshot? original;
    private byte[]? originalMap, originalSelection, originalStage;
    private LogitechDpiIndicatorState? originalIndicator;
    private bool indicatorAlwaysOn;
    private DateTime indicatorHideAt;
    private CancellationTokenSource? runtime;
    private Task inputTask = Task.CompletedTask;
    private LogitechActionRunner? actions;
    private bool suspended, stopping;
    private DateTime retryAt;
    private DateTime healthAt;
    private LogitechButtonState? buttonState;
    private string? indicatorProblem;
    private sealed record RuntimeStage(string Profile, int[] Stages, int ShiftDpi, int Stage);
    private string RuntimePath => Path.Combine(directory, "control-runtime.json");
    public LogitechControlProfile? Profile { get; private set; }
    public LogitechSnapshot? Snapshot { get; private set; }
    public bool Enabled { get; private set; }
    public bool Running => device != null && runtime is { IsCancellationRequested: false };
    public int? CurrentDpi { get; private set; }
    public int? CurrentStage { get; private set; }
    public bool DpiShiftHeld { get; private set; }
    public ushort PressedButtons { get; private set; }
    public string Status { get; private set; } = "Mouse controls inactive";
    public event Action? Updated;

    public LogitechService(SoraHost host)
    {
        this.host = host; directory = Path.Combine(host.DataDirectory, "logitech");
        try
        {
            var file = Path.Combine(directory, "control-profile.json"); if (File.Exists(file)) Profile = LogitechControlProfile.Load(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { host.Log("Logitech profile: " + e.Message); }
        try
        {
            var catalog = Path.Combine(directory, "control-profiles.json");
            if (File.Exists(catalog))
            {
                if (new FileInfo(catalog).Length > 1_048_576) throw new InvalidDataException("The Logitech profile library is too large.");
                var loaded = JsonSerializer.Deserialize<List<LogitechControlProfile>>(File.ReadAllText(catalog)) ?? throw new InvalidDataException("The Logitech profile library is empty.");
                if (loaded.Count > 32 || loaded.Any(p => p == null || string.IsNullOrWhiteSpace(p.Name)) || loaded.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != loaded.Count)
                    throw new InvalidDataException("The Logitech profile library must contain at most 32 uniquely named profiles.");
                profiles.AddRange(loaded);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { host.Log("Logitech profile library: " + e.Message); profiles.Clear(); }
        try
        {
            var enabled = Path.Combine(directory, "control-enabled.json"); Enabled = File.Exists(enabled) && JsonSerializer.Deserialize<bool>(File.ReadAllText(enabled));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { host.Log("Logitech control state: " + e.Message); }
        if (Profile != null && !profiles.Any(p => SameProfileName(p.Name, Profile.Name))) profiles.Add(Profile);
    }
    public async Task<T> UseDeviceAsync<T>(Func<LogitechDevice, Task<T>> action, DeviceInfo? selected = null)
    {
        await gate.WaitAsync();
        try
        {
            await inputGate.WaitAsync();
            try
            {
                if (Running && device != null && (selected == null || device.Matches(selected))) return await action(device);
                var info = selected ?? LogitechDevice.EnumerateG502().Single();
                await using var connection = new LogitechDevice(info);
                return await action(connection);
            }
            finally { inputGate.Release(); }
        }
        finally { gate.Release(); }
    }
    private void SaveRuntimeStage(int stage)
    {
        if (Profile == null) return;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(RuntimePath + ".tmp", JsonSerializer.Serialize(new RuntimeStage(Profile.Name, Profile.Stages, Profile.ShiftDpi, stage)));
            File.Move(RuntimePath + ".tmp", RuntimePath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { host.Log("Mouse stage could not be saved: " + error.Message); }
    }
    private int ResumeStage(LogitechControlProfile profile)
    {
        try
        {
            if (File.Exists(RuntimePath))
            {
                var saved = JsonSerializer.Deserialize<RuntimeStage>(File.ReadAllText(RuntimePath));
                if (saved != null && saved.Profile == profile.Name && saved.Stages?.SequenceEqual(profile.Stages) == true && saved.ShiftDpi == profile.ShiftDpi && saved.Stage >= 0 && saved.Stage < profile.Stages.Length) return saved.Stage;
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { host.Log("Mouse stage recovery: " + error.Message); }
        return profile.DefaultStage;
    }
    public async Task ApplyAsync(LogitechControlProfile profile, bool resume = false)
    {
        await gate.WaitAsync();
        try
        {
            if (LogitechDevice.IsGHubRunning()) throw new InvalidOperationException("Exit G HUB from its tray menu before applying. Sora will take software control without changing onboard profiles.");
            await StopCoreAsync();
            var infos = LogitechDevice.EnumerateG502();
            if (infos.Length != 1) throw new IOException("Connect exactly one G502 HERO to apply its software profile.");
            device = new LogitechDevice(infos[0]);
            var fresh = await device.ReadAsync(); profile.Validate(fresh);
            var initialStage = resume ? ResumeStage(profile) : profile.DefaultStage;
            EnsureProfileCapacity(profile);
            // Retain the requested profile even when hardware recovery must retry.
            // The active runtime is started only after all hardware readbacks pass.
            RememberProfile(profile); Profile = profile; Enabled = true;
            File.WriteAllText(Path.Combine(directory, "control-enabled.json"), "true");
            await RestoreInterruptedAsync(fresh);
            original = await device.ReadAsync(); originalMap = await device.ReadButtonMapAsync(original);
            var onboard = original.Features.Single(f => f.Id == 0x8100).Index;
            originalSelection = await device.RequestAsync(onboard, 4); originalStage = await device.RequestAsync(onboard, 11);
            originalIndicator = await device.ReadDpiIndicatorAsync(original);
            indicatorAlwaysOn = originalIndicator.AlwaysOn;
            SaveOwnership();
            await device.SetModeAsync(original, 2);
            await device.SetDpiIndicatorControlAsync(original, true);
            await device.SetButtonSpyAsync(original, true);
            // Standard buttons stay native when unchanged in both layers. Do not
            // replace ordinary right/middle/back/forward input with SendInput.
            var map = LogitechButtonRouting.NativeMap(profile);
            await device.WriteButtonMapAsync(original, map);
            await device.SetLiveDpiAsync(original, profile.Stages[initialStage]);
            await device.RequestAsync(original.Features.Single(f => f.Id == 0x8060).Index, 2, [(byte)(1000 / profile.ReportRate)]);
            Snapshot = await device.ReadAsync();
            if (Snapshot.Sensors[0].Dpi != profile.Stages[initialStage] || Snapshot.ReportRate != profile.ReportRate || Snapshot.OnboardMode != 2) throw new IOException("Software profile readback did not match.");
            indicatorProblem = null;
            await ShowIndicatorAsync(device, Snapshot, profile, Snapshot.Sensors[0].Dpi, false, CancellationToken.None);
            Profile = profile; Enabled = true; CurrentDpi = Snapshot.Sensors[0].Dpi;
            CurrentStage = initialStage; DpiShiftHeld = false;
            RememberProfile(profile);
            File.WriteAllText(Path.Combine(directory, "control-enabled.json"), "true");
            actions = new LogitechActionRunner(new LogitechInput(), profile); actions.Failed += SetFailure;
            runtime = new CancellationTokenSource(); var state = new LogitechButtonState(profile); state.UseProfile(profile, initialStage); buttonState = state;
            SaveRuntimeStage(initialStage);
            var connection = device; var snapshot = Snapshot; var runner = actions; var token = runtime.Token;
            inputTask = Task.Run(() => ProcessButtonsAsync(connection, snapshot, state, runner, token));
            healthAt = DateTime.UtcNow.AddSeconds(2);
            Status = indicatorProblem ?? "Sora software control · applied and verified"; host.Log($"Logitech: profile active, stage {initialStage + 1}, actual DPI {CurrentDpi}."); Publish();
        }
        catch
        {
            await StopCoreAsync(); retryAt = DateTime.UtcNow.AddSeconds(5); throw;
        }
        finally { gate.Release(); }
    }
    private async Task ProcessButtonsAsync(LogitechDevice connection, LogitechSnapshot snapshot, LogitechButtonState state, LogitechActionRunner runner, CancellationToken token)
    {
        try
        {
            await foreach (var bitmap in connection.ButtonReports.ReadAllAsync(token))
            {
                await inputGate.WaitAsync(token);
                try
                {
                var transitions = state.Update(bitmap);
                var dpiFeedback = transitions.Any(t => t.Action.Kind == "DpiShift" || t.Down && t.Action.Kind is "DpiUp" or "DpiDown" or "DpiCycle");
                foreach (var transition in transitions)
                {
                    if (transition.Down && transition.Action.Kind == "ProfileNext")
                        snapshot = await CycleProfileAsync(connection, snapshot, state, runner, token);
                    else await runner.HandleAsync(transition.Button, transition.Action, transition.Down);
                }
                // DPI actions always write and verify the hardware, including boundary
                // presses. A cached value must never suppress a requested DPI change.
                if (dpiFeedback || CurrentDpi != state.CurrentDpi)
                {
                    await connection.SetLiveDpiAsync(snapshot, state.CurrentDpi, token); CurrentDpi = state.CurrentDpi;
                }
                if (dpiFeedback || CurrentStage != state.Stage || DpiShiftHeld != state.ShiftHeld)
                    await ShowIndicatorAsync(connection, snapshot, Profile!, state.CurrentDpi, state.ShiftHeld, token);
                if (CurrentStage != state.Stage) SaveRuntimeStage(state.Stage);
                CurrentStage = state.Stage; DpiShiftHeld = state.ShiftHeld;
                Snapshot = snapshot = snapshot with { Sensors = snapshot.Sensors.Select(s => s with { Dpi = state.CurrentDpi }).ToArray() };
                PressedButtons = bitmap; Publish();
                }
                finally { inputGate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException) { SetFailure("Input stopped: " + error.Message); runtime?.Cancel(); }
        finally { await runner.DisposeAsync(); }
    }
    private async Task ShowIndicatorAsync(LogitechDevice connection, LogitechSnapshot snapshot, LogitechControlProfile profile, int dpi, bool shift, CancellationToken token)
    {
        if (originalIndicator == null) return;
        try
        {
            await connection.SetDpiIndicatorControlAsync(snapshot, true, token);
            await connection.SetDpiIndicatorAsync(snapshot, originalIndicator.Index, LogitechDpiIndicator.Level(profile, dpi), true, token);
            indicatorProblem = null;
            indicatorHideAt = indicatorAlwaysOn || shift ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(2);
        }
        catch (IOException error) when (!token.IsCancellationRequested)
        {
            indicatorProblem = "Mouse controls active · DPI indicator pending: " + error.Message;
            indicatorHideAt = DateTime.UtcNow.AddSeconds(5);
            host.Log(indicatorProblem); Status = indicatorProblem;
        }
    }
    public async Task RefreshLiveDpiAsync()
    {
        if (!Running || LogitechDevice.IsGHubRunning() || !await gate.WaitAsync(0)) return;
        try
        {
            await inputGate.WaitAsync();
            try
            {
                if (!Running || device == null || Snapshot == null) return;
                var token = runtime!.Token;
                var mode = await device.RequestAsync(Snapshot.Features.Single(f => f.Id == 0x8100).Index, 2, token: token);
                if (mode[0] != 2) throw new IOException("The mouse returned to firmware control; reapplying the saved Sora profile.");
                var mapping = await device.ReadButtonMapAsync(Snapshot, token);
                if (Profile == null || !mapping.SequenceEqual(LogitechButtonRouting.NativeMap(Profile)))
                    throw new IOException("The mouse button forwarding map changed; restoring Sora controls.");
                var actual = await device.ReadLiveDpiAsync(Snapshot, token);
                if (buttonState != null && actual != buttonState.CurrentDpi)
                {
                    host.Log($"Logitech: actual DPI drifted to {actual}; restoring active stage {buttonState.CurrentDpi}.");
                    await device.SetButtonSpyAsync(Snapshot, true, token);
                    await device.SetLiveDpiAsync(Snapshot, buttonState.CurrentDpi, token);
                    actual = await device.ReadLiveDpiAsync(Snapshot, token);
                }
                CurrentDpi = actual;
                Snapshot = Snapshot with { Sensors = Snapshot.Sensors.Select(s => s with { Dpi = CurrentDpi.Value }).ToArray() };
                if (indicatorProblem != null && Profile != null && DateTime.UtcNow >= indicatorHideAt)
                    await ShowIndicatorAsync(device, Snapshot, Profile, actual, DpiShiftHeld, token);
                Status = indicatorProblem ?? "Sora controls active · actual DPI " + actual.ToString("N0");
                Publish();
            }
            finally { inputGate.Release(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            runtime?.Cancel(); CurrentDpi = null;
            retryAt = DateTime.UtcNow.AddSeconds(1);
            SetFailure("Mouse control interrupted · reconnecting automatically: " + error.Message);
        }
        finally { gate.Release(); }
    }
    public async Task RefreshIndicatorPolicyAsync()
    {
        await gate.WaitAsync();
        try
        {
            await inputGate.WaitAsync();
            try
            {
                if (!Running || device == null || Snapshot == null || Profile == null || CurrentDpi == null) return;
                indicatorAlwaysOn = (await device.ReadDpiIndicatorAsync(Snapshot)).AlwaysOn;
                await ShowIndicatorAsync(device, Snapshot, Profile, CurrentDpi.Value, DpiShiftHeld, runtime!.Token);
            }
            finally { inputGate.Release(); }
        }
        finally { gate.Release(); }
    }
    private static bool SameProfileName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private void EnsureProfileCapacity(LogitechControlProfile profile)
    {
        if (!profiles.Any(p => SameProfileName(p.Name, profile.Name)) && profiles.Count >= 32)
            throw new InvalidOperationException("The Logitech profile library is full. Remove a saved profile before adding another.");
    }
    private void RememberProfile(LogitechControlProfile profile)
    {
        var index = profiles.FindIndex(p => SameProfileName(p.Name, profile.Name));
        if (index < 0) profiles.Add(profile); else profiles[index] = profile;
        Directory.CreateDirectory(directory);
        var catalog = Path.Combine(directory, "control-profiles.json");
        File.WriteAllText(catalog + ".tmp", JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(catalog + ".tmp", catalog, true);
        LogitechControlProfile.Save(Path.Combine(directory, "control-profile.json"), profile);
    }
    private async Task<LogitechSnapshot> CycleProfileAsync(LogitechDevice connection, LogitechSnapshot snapshot, LogitechButtonState state, LogitechActionRunner runner, CancellationToken token)
    {
        if (profiles.Count < 2 || Profile == null)
        {
            Status = "Save at least two named Sora profiles to use profile cycling."; Publish(); return snapshot;
        }
        var current = Profile;
        var currentIndex = profiles.FindIndex(p => SameProfileName(p.Name, current.Name));
        if (currentIndex < 0) currentIndex = 0;
        var next = profiles[(currentIndex + 1) % profiles.Count]; next.Validate(snapshot);
        if (!LogitechButtonRouting.CanChangeWhileHeld(current, next, state.Bitmap))
        {
            Status = "Release mouse buttons before switching a profile that changes their native routing.";
            Publish(); return snapshot;
        }
        var before = await connection.ReadAsync(token);
        var beforeMap = await connection.ReadButtonMapAsync(before, token);
        var nextMap = LogitechButtonRouting.NativeMap(next);
        var oldStage = state.Stage;
        var previousDpi = before.Sensors.Single().Dpi;
        var previousRate = before.ReportRate ?? throw new IOException("The mouse report rate is unavailable; profile cycling was stopped.");
        var targetDpi = state.ShiftHeld ? next.ShiftDpi : next.Stages[next.DefaultStage];
        await connection.SetSensitivityAsync(before, targetDpi, next.ReportRate, token);
        try
        {
            await connection.WriteButtonMapAsync(before, nextMap, token);
            await runner.UseProfileAsync(next);
            state.UseProfile(next);
            var readback = await connection.ReadAsync(token);
            if (readback.Sensors.Single().Dpi != targetDpi || readback.ReportRate != next.ReportRate || readback.OnboardMode != 2 ||
                !(await connection.ReadButtonMapAsync(readback, token)).SequenceEqual(nextMap))
                throw new IOException("The next Sora profile did not read back correctly.");
            LogitechControlProfile.Save(Path.Combine(directory, "control-profile.json"), next);
            Profile = next; Snapshot = readback; CurrentDpi = targetDpi;
            CurrentStage = null; // The same numeric stage in another profile may have a different LED level.
            Status = "Sora profile: " + next.Name; host.Log("Logitech profile cycled to " + next.Name); Publish();
            return readback;
        }
        catch (Exception error)
        {
            // Cancellation of the new profile must not cancel restoration. Recover
            // the button route even if releasing a macro or another domain fails.
            var failures = await CleanupSequence.RunAsync(
                ("Button routing", () => connection.WriteButtonMapAsync(before, beforeMap)),
                ("Input profile", () => runner.UseProfileAsync(current)),
                ("Runtime stage", () => { state.UseProfile(current, oldStage); return Task.CompletedTask; }),
                ("Sensitivity", async () =>
                {
                    var fresh = await connection.ReadAsync();
                    await connection.SetSensitivityAsync(fresh, previousDpi, previousRate);
                    CurrentDpi = previousDpi;
                }));
            if (failures.Count > 0)
                throw new IOException("Profile cycling failed and the previous profile could not be completely restored. Original recovery backup retained.",
                    new AggregateException(new[] { error }.Concat(failures.Select(f => new IOException(f.Part + " restoration failed.", f.Error)))));
            throw;
        }
    }
    public async Task TickAsync(bool allowHardware)
    {
        if (!allowHardware || suspended || stopping || gate.CurrentCount == 0) return;
        if (!Enabled)
        {
            if (File.Exists(Path.Combine(directory, "control-ownership.json")) && !LogitechDevice.IsGHubRunning() && DateTime.UtcNow >= retryAt)
            {
                retryAt = DateTime.UtcNow.AddSeconds(5); await gate.WaitAsync();
                try
                {
                    var info = LogitechDevice.EnumerateG502().Single(); device = new LogitechDevice(info); await RestoreInterruptedAsync(await device.ReadAsync()); Status = "Original mouse controls restored"; Publish();
                }
                catch (Exception error) when (error is not OutOfMemoryException) { SetFailure("Restoration pending: " + error.Message); }
                finally { if (device != null) { await device.DisposeAsync(); device = null; } gate.Release(); }
            }
            return;
        }
        if (LogitechDevice.IsGHubRunning())
        {
            if (device != null) { await gate.WaitAsync(); try { await StopCoreAsync(); } finally { gate.Release(); } }
            Status = "G HUB is running · Sora controls paused"; return;
        }
        if (Running && DateTime.UtcNow >= healthAt)
        {
            healthAt = DateTime.UtcNow.AddSeconds(2);
            await RefreshLiveDpiAsync();
        }
        if (Running && originalIndicator != null && DateTime.UtcNow >= indicatorHideAt)
        {
            await gate.WaitAsync();
            try
            {
                await inputGate.WaitAsync();
                try
                {
                if (Running && device != null && Snapshot != null && !DpiShiftHeld && !indicatorAlwaysOn && DateTime.UtcNow >= indicatorHideAt)
                {
                    await device.SetDpiIndicatorAsync(Snapshot, originalIndicator.Index, 0, false, runtime!.Token);
                    indicatorHideAt = DateTime.MaxValue;
                }
                }
                finally { inputGate.Release(); }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { indicatorHideAt = DateTime.UtcNow.AddSeconds(5); SetFailure("DPI indicator: " + error.Message); }
            finally { gate.Release(); }
        }
        if (!Running && Profile != null && DateTime.UtcNow >= retryAt)
        {
            retryAt = DateTime.UtcNow.AddSeconds(5);
            try { await ApplyAsync(Profile, resume: true); }
            catch (Exception error) when (error is not OutOfMemoryException) { SetFailure(error.Message); }
        }
    }
    public async Task SetSuspendedAsync(bool value)
    {
        suspended = value;
        if (value) { await gate.WaitAsync(); try { await StopCoreAsync(); } finally { gate.Release(); } }
    }
    public async Task DisableAsync()
    {
        Enabled = false; Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "control-enabled.json"), "false");
        await gate.WaitAsync();
        try { await StopCoreAsync(); Status = File.Exists(Path.Combine(directory, "control-ownership.json")) ? "Sora controls stopped · restoration pending; reconnect the original mouse" : "Sora software controls stopped · original settings restored"; Publish(); }
        finally { gate.Release(); }
    }
    public async Task StopAsync() { stopping = true; await gate.WaitAsync(); try { await StopCoreAsync(); } finally { gate.Release(); } }
    private async Task StopCoreAsync()
    {
        runtime?.Cancel(); try { await inputTask; } catch (Exception e) when (e is not OutOfMemoryException) { host.Log("Logitech input cleanup: " + e.Message); }
        runtime?.Dispose(); runtime = null; actions = null; buttonState = null; PressedButtons = 0; DpiShiftHeld = false; CurrentDpi = null;
        if (device == null) return;
        try
        {
            if (original != null && originalMap != null && !LogitechDevice.IsGHubRunning())
            {
                await RestoreAsync(original, originalMap, originalSelection, originalStage, originalIndicator);
                File.Delete(Path.Combine(directory, "control-ownership.json"));
            }
            else if (LogitechDevice.IsGHubRunning()) File.Delete(Path.Combine(directory, "control-ownership.json")); // New controller owns the hardware; never stop its spy or overwrite its settings.
        }
        catch (Exception error) when (error is not OutOfMemoryException) { host.RecordCleanupFailure("Mouse restoration pending: " + error.Message); SetFailure("Restoration pending: " + error.Message); }
        finally { await device.DisposeAsync(); device = null; original = null; originalMap = null; originalSelection = originalStage = null; originalIndicator = null; CurrentStage = null; DpiShiftHeld = false; Snapshot = null; }
    }
    private async Task RestoreAsync(LogitechSnapshot before, byte[] map, byte[]? selection, byte[]? stage, LogitechDpiIndicatorState? indicator = null)
    {
        if (device == null) return;
        await device.WriteButtonMapAsync(before, map); await device.SetButtonSpyAsync(before, false);
        await device.SetModeAsync(before, 2); await device.SetLiveDpiAsync(before, before.Sensors[0].Dpi);
        await device.RequestAsync(before.Features.Single(f => f.Id == 0x8060).Index, 2, [(byte)(1000 / before.ReportRate!.Value)]);
        if (indicator != null)
        {
            // SetLEDState is rejected while firmware owns feature 1300. An interrupted
            // startup may have restored ownership already, so acquire it before restoring
            // the display, then return ownership to the saved controller.
            await device.SetDpiIndicatorControlAsync(before, true);
            // This firmware does not echo its LED pattern. Use the saved firmware stage when
            // restoring an illuminated indicator whose pattern was not reported.
            var level = indicator.Level;
            if (indicator.Mode == 2 && level == 0) level = stage?.Length > 0 && stage[0] < 5 ? (byte)(stage[0] + 1) : (byte)1;
            await device.SetDpiIndicatorAsync(before, indicator.Index, level, indicator.Mode == 2);
            await device.SetDpiIndicatorControlAsync(before, indicator.SoftwareControl);
        }
        if (before.OnboardMode == 1)
        {
            await device.SetModeAsync(before, 1);
            var feature = before.Features.Single(f => f.Id == 0x8100).Index;
            if (selection?.Length > 1 && selection[1] is > 0 and <= 5) await device.RequestAsync(feature, 3, [selection[0], selection[1]]);
            if (stage?.Length > 0 && stage[0] < 5) await device.RequestAsync(feature, 12, [stage[0]]);
        }
        var restored = await device.ReadAsync();
        if (restored.OnboardMode != before.OnboardMode || restored.Sensors[0].Dpi != before.Sensors[0].Dpi || restored.ReportRate != before.ReportRate) throw new IOException("Original mouse mode/sensitivity readback differs; backup retained.");
        CurrentDpi = restored.Sensors[0].Dpi;
    }
    private sealed record Ownership(LogitechSnapshot Snapshot, byte[] Mapping, byte[]? Selection, byte[]? Stage, LogitechDpiIndicatorState? Indicator = null);
    private void SaveOwnership()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "control-ownership.json"), JsonSerializer.Serialize(new Ownership(original!, originalMap!, originalSelection, originalStage, originalIndicator), new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task RestoreInterruptedAsync(LogitechSnapshot fresh)
    {
        var file = Path.Combine(directory, "control-ownership.json"); if (!File.Exists(file)) return;
        if (new FileInfo(file).Length > 131072) throw new InvalidDataException("Mouse restoration backup is too large.");
        var saved = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(file)) ?? throw new InvalidDataException("Mouse restoration backup is empty.");
        if (saved.Snapshot.Identity != fresh.Identity || saved.Snapshot.ProductId != fresh.ProductId) throw new InvalidOperationException("The original mouse must be reconnected to restore its previous controls.");
        await RestoreAsync(saved.Snapshot, saved.Mapping, saved.Selection, saved.Stage, saved.Indicator); File.Delete(file);
    }
    private void SetFailure(string message) { Status = message; host.Log("Logitech: " + message); Publish(); }
    private void Publish() => Updated?.Invoke();
}
