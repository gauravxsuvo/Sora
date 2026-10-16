using System.ComponentModel;
using System.Diagnostics;

namespace Sora.Core;

public sealed record DisplaySettings(bool Fahrenheit = false, bool LeadingZeros = false, bool MinimizeToTray = true, bool AutoStartDisplay = false);
public sealed record DisplayStatus(string Title, string Detail, bool Sending, long FramesSent);

public sealed class DisplayController(Func<IDisplayTransport>? open = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<IDisplayTransport> factory = open ?? HidDevice.Open;
    private IDisplayTransport? device;
    private bool? leadingZeros;
    private long framesSent;
    public DisplayStatus Status { get; private set; } = new("Ready", "Start when the original DeepCool app is closed.", false, 0);

    public static string? FindConflict()
    {
        // One process snapshot covers both names; GetProcessesByName would take a full snapshot per name.
        var processes = Process.GetProcesses();
        try
        {
            string? found = null;
            foreach (var process in processes)
            {
                var name = process.ProcessName;
                if (name.Equals("DeepCool", StringComparison.OrdinalIgnoreCase)) return "Exit DeepCool from its system tray icon.";
                if (name.Equals("DeepCoolDisplayService", StringComparison.OrdinalIgnoreCase)) found = "Stop Deep Cool Display Service in Windows Services.";
            }
            return found;
        }
        finally { foreach (var process in processes) process.Dispose(); }
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
                Set("Ready", conflict ?? "Display stopped. Live sensor readings are still available.", false);
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
                Set("Waiting for live sensors", "Check PawnIO access. Stale readings are never sent.", false);
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
            Set("Connected to LD360", "Sending live CPU readings · refreshes every second", true);
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
