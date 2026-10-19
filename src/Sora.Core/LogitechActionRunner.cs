namespace Sora.Core;

public interface ILogitechOutput
{
    void Key(int key, bool down);
    void Mouse(string kind, bool down);
    void Scroll(bool horizontal, int delta);
    void Launch(string target, bool url);
    void ReleaseAll();
}

public sealed class LogitechActionRunner : IAsyncDisposable
{
    private readonly ILogitechOutput output;
    private LogitechControlProfile profile;
    private readonly Dictionary<int, (CancellationTokenSource Hold, CancellationTokenSource Cancel, Task Task, string Mode)> jobs = [];
    private readonly Dictionary<int, LogitechAction> held = [];
    private readonly CancellationTokenSource lifetime = new();
    public event Action<string>? Failed;
    public LogitechActionRunner(ILogitechOutput output, LogitechControlProfile profile) { this.output = output; this.profile = profile; }
    public async Task UseProfileAsync(LogitechControlProfile next)
    {
        var pending = jobs.Values.ToArray();
        foreach (var job in pending) { job.Hold.Cancel(); job.Cancel.Cancel(); }
        try { await Task.WhenAll(pending.Select(job => job.Task)); }
        finally
        {
            foreach (var action in held.Values) Release(action);
            held.Clear();
            output.ReleaseAll();
            foreach (var job in pending) { job.Hold.Dispose(); job.Cancel.Dispose(); }
            jobs.Clear();
        }
        profile = next;
    }
    public async Task HandleAsync(int button, LogitechAction action, bool down)
    {
        if (!down)
        {
            if (held.Remove(button, out var original)) Release(original);
            if (jobs.TryGetValue(button, out var running) && running.Mode is "WhileHeld" or "Sequence")
            {
                running.Hold.Cancel();
                if (running.Mode == "WhileHeld") { running.Cancel.Cancel(); await running.Task; running.Hold.Dispose(); running.Cancel.Dispose(); jobs.Remove(button); }
                // Sequence release steps run independently; never stall other mouse buttons behind their delays.
            }
            return;
        }
        if (action.Kind == "Macro")
        {
            var macro = profile.Macros.Single(m => m.Id == action.Target);
            if (jobs.Remove(button, out var prior))
            {
                prior.Hold.Cancel(); prior.Cancel.Cancel(); await prior.Task; prior.Hold.Dispose(); prior.Cancel.Dispose();
                if (macro.Mode == "Toggle") return;
            }
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var hold = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            jobs[button] = (hold, cancel, RunMacroAsync(macro, hold.Token, cancel.Token), macro.Mode); return;
        }
        if (action.Kind == "Key") { foreach (var key in action.Keys!) output.Key(key, true); held[button] = action; }
        else if (action.Kind.StartsWith("Mouse", StringComparison.Ordinal)) { output.Mouse(action.Kind, true); held[button] = action; }
        else if (action.Kind.StartsWith("Scroll", StringComparison.Ordinal)) output.Scroll(action.Kind is "ScrollLeft" or "ScrollRight", action.Kind is "ScrollLeft" or "ScrollDown" ? -120 : 120);
        else if (action.Kind is "Launch" or "Url") output.Launch(action.Target!, action.Kind == "Url");
    }
    private void Release(LogitechAction action)
    {
        if (action.Kind == "Key") foreach (var key in action.Keys!.Reverse()) output.Key(key, false);
        else if (action.Kind.StartsWith("Mouse", StringComparison.Ordinal)) output.Mouse(action.Kind, false);
    }
    private async Task RunMacroAsync(LogitechMacro macro, CancellationToken token, CancellationToken overall)
    {
        async Task Steps(string phase, CancellationToken cancellation)
        {
            foreach (var step in macro.Steps.Where(s => s.Phase == phase))
            {
                await Task.Delay(step.DelayMs, cancellation); var pressed = new List<int>();
                try { foreach (var key in step.Keys) { output.Key(key, true); pressed.Add(key); } await Task.Delay(10, cancellation); }
                finally { foreach (var key in pressed.AsEnumerable().Reverse()) output.Key(key, false); }
            }
        }
        try
        {
            await Steps("Press", token);
            if (macro.Mode is "WhileHeld" or "Toggle" or "Sequence")
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    await Steps(macro.Mode == "Sequence" ? "Hold" : "Press", token);
                    await Task.Delay(10, token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException) { Failed?.Invoke(error.Message); }
        finally
        {
            if (macro.Mode == "Sequence" && !overall.IsCancellationRequested)
                try { await Steps("Release", overall); } catch (OperationCanceledException) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); foreach (var job in jobs.Values) { job.Hold.Cancel(); job.Cancel.Cancel(); }
        try { await Task.WhenAll(jobs.Values.Select(j => j.Task)); }
        finally
        {
            foreach (var action in held.Values) Release(action); held.Clear();
            foreach (var job in jobs.Values) { job.Hold.Dispose(); job.Cancel.Dispose(); } jobs.Clear(); output.ReleaseAll(); lifetime.Dispose();
        }
    }
}
