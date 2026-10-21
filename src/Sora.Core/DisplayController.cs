using System.ComponentModel;
using System.Diagnostics;

namespace Sora.Core;

public sealed record DisplaySettings(bool Fahrenheit = false, bool LeadingZeros = false, bool MinimizeToTray = true, bool AutoStartDisplay = false, bool DarkMode = true);
public sealed record DisplayStatus(string Title, string Detail, bool Sending, long FramesSent);

public sealed class DisplayController(Func<IDisplayTransport>? open = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<IDisplayTransport> factory = open ?? HidDevice.Open;
    private IDisplayTransport? device;
    private bool? leadingZeros;
    private long framesSent;
    public DisplayStatus Status { get; private set; } = new("Off", "Nothing is being sent to the cooler.", false, 0);

    public static string? FindConflict()
    {
        // The vendor changed its helper executable names in DeepCool 1.2.14.
        var processes = Process.GetProcesses();
        try
        {
            var names = new List<string>();
            foreach (var process in processes)
            {
                try { names.Add(process.ProcessName); }
                catch (InvalidOperationException) { } // A process can exit after the snapshot.
            }
            return FindConflict(names);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static string? FindConflict(IEnumerable<string> processNames)
    {
        var names = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        if (names.Contains("DeepCool")) return "Exit DeepCool from its system tray icon.";
        if (names.Contains("JZFSDisplayService")) return "Stop JZFS Display Service in Windows Services.";
        if (names.Contains("DeepCoolDisplayService")) return "Stop Deep Cool Display Service in Windows Services.";
        return null;
    }

    public async Task TickAsync(SensorSample? sample, DisplaySettings settings, bool requested, string? conflict, CancellationToken token = default)
    {
        if (requested) { if (!await gate.WaitAsync(0, token)) return; }
        else await gate.WaitAsync(token);
        try
        {
            if (!requested)
            {
                if (device != null)
                {
                    try { if (conflict == null) await device.WriteAsync(LdProtocol.Close(), token); }
                    finally { await ReleaseAsync(); }
                }
                Set("Off", conflict ?? "Nothing is being sent to the cooler.", false);
                return;
            }
            if (conflict != null)
            {
                await ReleaseAsync();
                Set("DeepCool is still running", conflict, false);
                return;
            }
            if (sample == null || !sample.IsFresh(DateTimeOffset.UtcNow))
            {
                await ReleaseAsync();
                Set("Waiting for sensors", "Nothing is sent until there is a live reading.", false);
                return;
            }
            sample.Validate();
            if (device == null)
            {
                device = factory();
                await device.WriteAsync(LdProtocol.Initialize(), token);
            }
            if (leadingZeros != settings.LeadingZeros)
            {
                await device.WriteAsync(LdProtocol.LeadingZeros(settings.LeadingZeros), token);
                leadingZeros = settings.LeadingZeros;
            }
            await device.WriteAsync(LdProtocol.Telemetry(sample, settings.Fahrenheit), token);
            framesSent++;
            Set("Sending", "Live CPU readings, once a second.", true);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or Win32Exception or OperationCanceledException or UnauthorizedAccessException or ArgumentException)
        {
            await ReleaseAsync();
            Set("Reconnecting", error.Message, false);
        }
        finally { gate.Release(); }
    }
    private void Set(string title, string detail, bool sending) => Status = new(title, detail, sending, framesSent);
    private async Task ReleaseAsync()
    {
        var previous = device;
        device = null;
        leadingZeros = null;
        if (previous != null) await previous.DisposeAsync();
    }
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { await ReleaseAsync(); }
        finally { gate.Release(); }
    }
}
