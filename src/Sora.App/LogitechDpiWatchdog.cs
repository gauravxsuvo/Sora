using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.App;

// Mouse verification must not wait behind fan-service handoffs or cooler scans.
// A focus change requests verification, never a different DPI/profile.
internal static class LogitechDpiWatchdog
{
    internal static async Task RunAsync(Func<IntPtr> foreground, Func<Task<bool>> verify, CancellationToken token,
        Func<CancellationToken, Task>? delay = null, Func<DateTime>? now = null)
    {
        delay ??= t => Task.Delay(250, t);
        now ??= () => DateTime.UtcNow;
        var previous = IntPtr.Zero;
        var verifiedAt = DateTime.MinValue;
        try
        {
            while (true)
            {
                await delay(token); token.ThrowIfCancellationRequested();
                var current = foreground();
                if (current == previous && now() - verifiedAt < TimeSpan.FromSeconds(2)) continue;
                // A busy HID gate is retried next tick, rather than declaring success.
                if (await verify()) { previous = current; verifiedAt = now(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
