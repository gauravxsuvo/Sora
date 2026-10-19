namespace Sora.Core;

public sealed record CleanupFailure(string Part, Exception Error);

/// <summary>Attempt every independent cleanup even if an earlier controller fails.</summary>
public static class CleanupSequence
{
    /// <summary>A diagnostic deadline must not release ownership while the worker still uses hardware.</summary>
    public static async Task JoinAsync(Task work, TimeSpan warnAfter, Action onSlow)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(onSlow);
        using var delayCancellation = new CancellationTokenSource();
        var delay = Task.Delay(warnAfter, delayCancellation.Token);
        try
        {
            if (await Task.WhenAny(work, delay).ConfigureAwait(false) != work) onSlow();
        }
        finally
        {
            delayCancellation.Cancel();
            // Also join when the diagnostic callback throws. No caller may release
            // its hardware gate, discard its worker, or exit before this finishes.
            await work.ConfigureAwait(false);
        }
    }

    public static async Task<IReadOnlyList<CleanupFailure>> RunAsync(params (string Part, Func<Task> Run)[] steps)
    {
        var failures = new List<CleanupFailure>();
        foreach (var step in steps)
        {
            try { await step.Run(); }
            catch (Exception error) when (error is not OutOfMemoryException) { failures.Add(new(step.Part, error)); }
        }
        return failures;
    }
}
