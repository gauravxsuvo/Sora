using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Sora.Core;
using Microsoft.Win32;

namespace Sora.App;

// Everything that keeps running in the tray: CPU sensors, the LD360 output, settings and the log.
// The window is only a view of this object and can be created and destroyed at any time.
internal sealed class SoraHost
{
    private readonly SensorClient sensors = new();
    private readonly DisplayController display = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string dataDirectory = SoraData.DirectoryPath;
    private Task sensorTask = Task.CompletedTask;
    private CancellationTokenSource? sensorLifetime;
    private SensorSample? latest;
    private DateTime conflictCheckedAt = DateTime.MinValue, presenceCheckedAt = DateTime.MinValue;
    private string? lastLoggedStatus;
    private bool stopping;
    private readonly object stopSync = new();
    private Task? stopTask;
    private bool hardwareOutputEnabled;
    private bool suspended;
    private int displayRequestRevision;
    private readonly SemaphoreSlim tickGate = new(1, 1);
    private CancellationTokenSource? recovery;
    private Task recoveryTask = Task.CompletedTask;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

    public DisplaySettings Settings { get; private set; } = new();
    public bool Requested { get; private set; }
    public string? Conflict { get; private set; }
    public SensorStatus Sensors { get; private set; } = SensorStatus.Starting;
    // Whether an LD360 is plugged in: true/false from a read-only USB scan, null until the first scan.
    public bool? CoolerPresent { get; private set; }
    public HardwareInventory Hardware { get; private set; } = HardwareDiscovery.Capture();
    private DateTime hardwareCheckedAt;
    private Func<Task<HardwareInventory>> captureHardware = () => Task.Run(HardwareDiscovery.Capture);
    internal void SetHardwareInventoryForCheck(HardwareInventory inventory)
    {
        if (hardwareOutputEnabled) throw new InvalidOperationException("Hardware fixtures require a read-only session.");
        Hardware = inventory; hardwareCheckedAt = DateTime.MaxValue;
    }
    public string? CpuName => sensors.CpuName;
    public SensorSample? Latest => Volatile.Read(ref latest);
    public SensorSample? Fresh => Latest is { } sample && sample.IsFresh(DateTimeOffset.UtcNow) ? sample : null;
    public DisplayStatus Status => display.Status;
    public string DataDirectory => dataDirectory;
    // True while a window is showing readings; conflict checks are only needed then or while the display runs.
    public bool Watched { get; set; }
    public LightingService Lighting { get; }
    public FanService Fans { get; }
    public GpuFanService GpuFans { get; }
    public LogitechService Logitech { get; }
    public LogitechReactiveLighting LogitechLighting { get; }
    public event Action? Updated;

    public SoraHost(string? dataDirectoryOverride = null)
    {
        if (dataDirectoryOverride == null) SoraData.Migrate();
        if (dataDirectoryOverride != null) dataDirectory = Path.GetFullPath(dataDirectoryOverride);
        try
        {
            var file = Path.Combine(dataDirectory, "settings.json");
            if (File.Exists(file)) Settings = JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllText(file)) ?? new();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { Log("Settings: " + error.Message); }
        sensors.SampleReceived += sample => Volatile.Write(ref latest, sample);
        sensors.StatusChanged += status =>
        {
            // Readings never outlive a sensor failure.
            if (status.State != SensorState.Live) Volatile.Write(ref latest, null);
            var previous = Sensors;
            Sensors = status;
            if (previous.State != status.State || previous.Problem != status.Problem) Log("Sensors: " + status.State + (status.State == SensorState.Unavailable ? " · " + status.Message + (status.Detail == null ? "" : " · " + status.Detail) : ""));
        };
        timer.Tick += async (_, _) => await TickAsync();
        Lighting = new LightingService(this);
        Lighting.Load();
        Fans = new FanService(this);
        Fans.Load();
        GpuFans = new GpuFanService(this);
        Logitech = new LogitechService(this);
        LogitechLighting = new LogitechReactiveLighting(this);
    }

    // Screenshot/measurement sessions read sensors but never start hardware output or saved profiles.
    public void Start(bool applyHardware = true)
    {
        hardwareOutputEnabled = applyHardware;
        Lighting.AutomaticChangesAllowed = applyHardware;
        Requested = false;
        if (Hardware.Available(HardwareModule.Cooler) || Hardware.Available(HardwareModule.MotherboardFans) || Hardware.Available(HardwareModule.GpuFans) || !applyHardware)
        {
            sensorLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            sensorTask = sensors.RunAsync(sensorLifetime.Token);
        }
        timer.Start();
        _ = TickAsync();
        if (applyHardware)
        {
            SystemEvents.PowerModeChanged += PowerModeChanged;
            // No wait at sign-in: the user wants their lighting straight away; each part retries on its own if hardware is not ready yet.
            QueueRecovery("startup", 0);
        }
    }

    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _ = dispatcher.InvokeAsync(async () =>
        {
            if (stopping) return;
            if (e.Mode == PowerModes.Suspend)
            {
                suspended = true; recovery?.Cancel();
                Volatile.Write(ref latest, null);
                Lighting.SetSuspended(true); Fans.CancelResponseMeasurement();
                await GpuFans.SuspendAsync();
                await Logitech.SetSuspendedAsync(true);
                await LogitechLighting.SuspendAsync(true);
                await display.DisposeAsync();
                Log("Suspended: output paused; saved hardware profiles retained.");
            }
            else if (e.Mode == PowerModes.Resume)
            {
                suspended = false; Lighting.SetSuspended(false); Volatile.Write(ref latest, null);
                await Logitech.SetSuspendedAsync(false);
                await LogitechLighting.SuspendAsync(false);
                conflictCheckedAt = presenceCheckedAt = DateTime.MinValue;
                QueueRecovery("resume", 5000);
            }
        });
    }
    private void QueueRecovery(string reason, int delayMs)
    {
        recovery?.Cancel();
        var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        recovery = pending;
        var previous = recoveryTask;
        recoveryTask = RecoverAsync(previous, pending, reason, delayMs);
    }
    private async Task RecoverAsync(Task previous, CancellationTokenSource pending, string reason, int delayMs)
    {
        var token = pending.Token;
        try
        {
            await previous;
            await Task.Delay(delayMs, token);
            if (stopping || suspended) return;
            Log("Restoring saved settings after " + reason + ".");
            if (reason == "startup" && displayRequestRevision == 0) Requested = Settings.AutoStartDisplay && Hardware.Available(HardwareModule.Cooler);
            await TickAsync();
            // Each independent controller catches its own errors; one unavailable device must not block the others.
            await Task.WhenAll(
                Hardware.Available(HardwareModule.Lighting) ? RestorePartAsync("Lighting", () => Lighting.ApplyAtStartupAsync(token)) : Task.CompletedTask,
                Hardware.Available(HardwareModule.MotherboardFans) ? RestorePartAsync("Motherboard fans", () => Fans.ApplyAtStartupAsync(token)) : Task.CompletedTask,
                Hardware.Available(HardwareModule.GpuFans) ? RestorePartAsync("GPU fans", () => GpuFans.ApplyAtStartupAsync(token)) : Task.CompletedTask);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException) { Log("Hardware recovery: " + error); }
        finally { if (ReferenceEquals(recovery, pending)) recovery = null; pending.Dispose(); }
    }
    private async Task RestorePartAsync(string name, Func<Task> restore)
    {
        try { await restore(); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException) { Log(name + " recovery: " + error.Message); }
    }

    public Task SetRequestedAsync(bool on) { displayRequestRevision++; Requested = on && Hardware.Available(HardwareModule.Cooler); return TickAsync(); }

    public Task SaveSettingsAsync(DisplaySettings settings)
    {
        Settings = settings;
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var path = Path.Combine(dataDirectory, "settings.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Log("Cannot save settings: " + error.Message); }
        return TickAsync();
    }

    public string FormatTemperature(SensorSample sample, string format) =>
        (Settings.Fahrenheit ? 32 + 1.8 * sample.TemperatureC : sample.TemperatureC).ToString(format, CultureInfo.InvariantCulture);
    public string Unit => Settings.Fahrenheit ? "°F" : "°C";

    public async Task TickAsync()
    {
        if (stopping || suspended || !await tickGate.WaitAsync(0)) return;
        try
        {
            if (stopping || suspended) return;
            if (DateTime.UtcNow - hardwareCheckedAt > TimeSpan.FromSeconds(5))
            {
                hardwareCheckedAt = DateTime.UtcNow;
                Hardware = await captureHardware();
                if (stopping || suspended) return;
                CoolerPresent = Hardware.Presence(HardwareModule.Cooler) switch { HardwarePresence.Present => true, HardwarePresence.Absent => false, _ => null };
                var needSensors = Hardware.Available(HardwareModule.Cooler) || Hardware.Available(HardwareModule.MotherboardFans) || Hardware.Available(HardwareModule.GpuFans) || !hardwareOutputEnabled;
                if (needSensors && sensorLifetime == null)
                {
                    sensorLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    sensorTask = sensors.RunAsync(sensorLifetime.Token);
                }
                else if (!needSensors && sensorLifetime != null)
                {
                    sensorLifetime.Cancel(); await sensorTask; sensorLifetime.Dispose(); sensorLifetime = null;
                    Volatile.Write(ref latest, null);
                }
            }
            var coolerAvailable = Hardware.Available(HardwareModule.Cooler);
            // A process snapshot every 5 s instead of every second, and none at all while nobody needs the answer.
            if (coolerAvailable && (Requested || Watched) && DateTime.UtcNow - conflictCheckedAt > TimeSpan.FromSeconds(5))
            {
                Conflict = DisplayController.FindConflict();
                conflictCheckedAt = DateTime.UtcNow;
            }
            if (coolerAvailable || display.Status.Sending)
                await display.TickAsync(Latest, Settings, Requested && coolerAvailable, Conflict, lifetime.Token);
            // While sending, the writes themselves prove the cooler is there. Otherwise scan every 3 s, only while someone is looking.
            if (display.Status.Sending) CoolerPresent = true;
            else if (coolerAvailable && Watched && DateTime.UtcNow - presenceCheckedAt > TimeSpan.FromSeconds(3))
            {
                presenceCheckedAt = DateTime.UtcNow;
                try { CoolerPresent = await Task.Run(() => HidDevice.FindDisplays().Count > 0); }
                catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { CoolerPresent = null; Log("LD360 scan: " + error.Message); }
            }
            var status = display.Status;
            if (Hardware.Available(HardwareModule.Logitech) || Logitech.Running)
                await Logitech.TickAsync(hardwareOutputEnabled);
            if (Hardware.Available(HardwareModule.Logitech) || LogitechLighting.Running)
                await LogitechLighting.TickAsync(hardwareOutputEnabled);
            if (coolerAvailable && lastLoggedStatus != status.Title) { Log(status.Title + " · " + status.Detail); lastLoggedStatus = status.Title; }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Log(error.ToString()); }
        finally { tickGate.Release(); }
        if (!stopping) Updated?.Invoke();
    }

    public Task StopAsync()
    {
        lock (stopSync) return stopTask ??= StopCoreAsync();
    }

    internal bool CleanupFailed { get; private set; }
    internal async Task<bool> CheckStopDuringScanAsync()
    {
        if (hardwareOutputEnabled) throw new InvalidOperationException("Shutdown fixtures require a read-only session.");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HardwareInventory>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = Hardware;
        captureHardware = () => { entered.TrySetResult(); return release.Task; };
        hardwareCheckedAt = DateTime.MinValue;
        var polling = TickAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var shutdown = StopAsync();
            await Task.Delay(50);
            var waitedForScan = !shutdown.IsCompleted;
            release.TrySetResult(original);
            await Task.WhenAll(polling, shutdown).WaitAsync(TimeSpan.FromSeconds(15));
            return waitedForScan && !CleanupFailed;
        }
        finally { release.TrySetResult(original); }
    }
    internal void RecordCleanupFailure(string message) { CleanupFailed = true; Log(message); }

    private async Task StopCoreAsync()
    {
        stopping = true;
        Lighting.AutomaticChangesAllowed = false;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        timer.Stop();
        Requested = false;
        var failures = await CleanupSequence.RunAsync(
            ("Cancel runtime", () => { lifetime.Cancel(); return Task.CompletedTask; }),
            ("Cancel recovery", () => { recovery?.Cancel(); return Task.CompletedTask; }),
            ("Hardware polling", async () => { await tickGate.WaitAsync(); tickGate.Release(); }),
            ("Startup recovery", () => recoveryTask),
            ("Motherboard fans", Fans.StopAsync),
            ("GPU fans", GpuFans.StopAsync),
            ("Mouse controls", Logitech.StopAsync),
            ("Mouse lighting", LogitechLighting.StopAsync),
            ("Lighting", () => hardwareOutputEnabled ? Lighting.ExitLightingAsync() : Task.CompletedTask),
            ("Display close", () => display.TickAsync(null, Settings, false, DisplayController.FindConflict())),
            ("Display disposal", async () => await display.DisposeAsync()),
            ("Sensors", async () => { await sensorTask; sensorLifetime?.Dispose(); sensorLifetime = null; }),
            ("Runtime disposal", () => { lifetime.Dispose(); return Task.CompletedTask; }));
        foreach (var failure in failures) RecordCleanupFailure(failure.Part + " exit cleanup: " + failure.Error);
    }

    public void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var path = Path.Combine(dataDirectory, "sora.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Debug.WriteLine(error.Message); }
    }
}
