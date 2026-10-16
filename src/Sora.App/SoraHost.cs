using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Sora.Core;

namespace Sora.App;

// Everything that keeps running in the tray: CPU sensors, the LD360 output, settings and the log.
// The window is only a view of this object and can be created and destroyed at any time.
internal sealed class SoraHost
{
    private readonly SensorClient sensors = new();
    private readonly DisplayController display = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
    private Task sensorTask = Task.CompletedTask;
    private SensorSample? latest;
    private DateTime conflictCheckedAt = DateTime.MinValue, presenceCheckedAt = DateTime.MinValue;
    private string? lastLoggedStatus;
    private bool stopping;

    public DisplaySettings Settings { get; private set; } = new();
    public bool Requested { get; private set; }
    public string? Conflict { get; private set; }
    public SensorStatus Sensors { get; private set; } = SensorStatus.Starting;
    // Whether an LD360 is plugged in: true/false from a read-only USB scan, null until the first scan.
    public bool? CoolerPresent { get; private set; }
    public string? CpuName => sensors.CpuName;
    public SensorSample? Latest => Volatile.Read(ref latest);
    public SensorSample? Fresh => Latest is { } sample && sample.IsFresh(DateTimeOffset.UtcNow) ? sample : null;
    public DisplayStatus Status => display.Status;
    public string DataDirectory => dataDirectory;
    // True while a window is showing readings; conflict checks are only needed then or while the display runs.
    public bool Watched { get; set; }
    public event Action? Updated;

    public SoraHost()
    {
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
    }

    public void Start()
    {
        Requested = Settings.AutoStartDisplay;
        sensorTask = sensors.RunAsync(lifetime.Token);
        timer.Start();
        _ = TickAsync();
    }

    public Task SetRequestedAsync(bool on) { Requested = on; return TickAsync(); }

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
        if (stopping) return;
        try
        {
            // A process snapshot every 5 s instead of every second, and none at all while nobody needs the answer.
            if ((Requested || Watched) && DateTime.UtcNow - conflictCheckedAt > TimeSpan.FromSeconds(5))
            {
                Conflict = DisplayController.FindConflict();
                conflictCheckedAt = DateTime.UtcNow;
            }
            await display.TickAsync(Latest, Settings, Requested, Conflict, lifetime.Token);
            // While sending, the writes themselves prove the cooler is there. Otherwise scan every 3 s, only while someone is looking.
            if (display.Status.Sending) CoolerPresent = true;
            else if (Watched && DateTime.UtcNow - presenceCheckedAt > TimeSpan.FromSeconds(3))
            {
                presenceCheckedAt = DateTime.UtcNow;
                try { CoolerPresent = await Task.Run(() => HidDevice.FindDisplays().Count > 0); }
                catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { CoolerPresent = null; Log("LD360 scan: " + error.Message); }
            }
            var status = display.Status;
            if (lastLoggedStatus != status.Title) { Log(status.Title + " · " + status.Detail); lastLoggedStatus = status.Title; }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Log(error.ToString()); }
        Updated?.Invoke();
    }

    public async Task StopAsync()
    {
        if (stopping) return;
        stopping = true;
        timer.Stop();
        Requested = false;
        lifetime.Cancel();
        try { await display.TickAsync(null, Settings, false, DisplayController.FindConflict()); }
        catch (Exception error) when (error is not OutOfMemoryException) { Log(error.Message); }
        await display.DisposeAsync();
        try { await sensorTask; } catch (Exception error) when (error is not OutOfMemoryException) { Log(error.Message); }
        lifetime.Dispose();
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
