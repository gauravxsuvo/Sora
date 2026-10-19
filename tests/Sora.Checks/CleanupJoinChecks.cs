using Sora.Core;

internal static class CleanupJoinChecks
{
    public static async Task Run()
    {
        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        static TaskCompletionSource Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Reproduce the old ownership bug deterministically, without hardware or sleeps.
        var legacy = Pending();
        await Task.WhenAny(legacy.Task, Task.CompletedTask);
        Require(!legacy.Task.IsCompleted, "Legacy async timeout did not reproduce unfinished work.");
        Require(!legacy.Task.Wait(0), "Legacy synchronous timeout did not reproduce unfinished work.");
        legacy.SetResult();

        foreach (var outcome in new[] { "success", "fault", "cancel", "callback-fault", "both-fault" })
        {
            var worker = Pending();
            var warned = Pending();
            var workerError = new IOException("worker failure");
            var callbackError = new IOException("diagnostic callback failure");
            var released = false;
            async Task Owner()
            {
                try
                {
                    await CleanupSequence.JoinAsync(worker.Task, TimeSpan.Zero, () =>
                    {
                        warned.SetResult();
                        if (outcome is "callback-fault" or "both-fault") throw callbackError;
                    });
                }
                finally { released = true; }
            }
            var owner = Owner();
            await warned.Task;
            Require(!owner.IsCompleted && !released, outcome + ": hardware ownership released before the worker completed.");
            if (outcome is "fault" or "both-fault") worker.SetException(workerError);
            else if (outcome == "cancel") worker.SetCanceled();
            else worker.SetResult();
            Exception? observed = null;
            try { await owner; } catch (Exception error) { observed = error; }
            Require(released && worker.Task.IsCompleted, outcome + ": ownership or worker was stranded.");
            if (outcome is "fault" or "both-fault") Require(ReferenceEquals(observed, workerError), "Worker failure was hidden.");
            else if (outcome == "callback-fault") Require(ReferenceEquals(observed, callbackError), "Diagnostic failure was hidden.");
            else if (outcome == "cancel") Require(observed is OperationCanceledException, "Worker cancellation was hidden.");
            else Require(observed == null, "Successful cleanup failed.");
        }

        var warningCount = 0;
        await CleanupSequence.JoinAsync(Task.CompletedTask, TimeSpan.FromMinutes(1), () => warningCount++);
        Require(warningCount == 0, "Fast cleanup emitted a false slow warning.");
        foreach (var finished in new[] { Task.FromException(new IOException("already failed")), Task.FromCanceled(new CancellationToken(true)) })
        {
            Exception? observed = null;
            try { await CleanupSequence.JoinAsync(finished, TimeSpan.FromMinutes(1), () => warningCount++); }
            catch (Exception error) { observed = error; }
            Require(observed != null && warningCount == 0, "Completed worker failure/cancellation was lost or warned as slow.");
        }

        // SyncEngine.Stop uses this synchronous bridge. It must join without relying
        // on the caller's synchronization context (for example, the WPF dispatcher).
        var syncWorker = Pending();
        var syncWarned = Pending();
        var syncOwner = Task.Run(() => CleanupSequence.JoinAsync(syncWorker.Task, TimeSpan.Zero, () => syncWarned.SetResult()).GetAwaiter().GetResult());
        await syncWarned.Task;
        Require(!syncOwner.IsCompleted, "Synchronous Stop returned while its worker was still running.");
        syncWorker.SetResult();
        await syncOwner;

        // A slow, failed cleanup must still allow later independent controllers to close.
        var failedWorker = Pending();
        var failedWarned = Pending();
        var nextRan = false;
        var sequence = CleanupSequence.RunAsync(
            ("Lighting", () => CleanupSequence.JoinAsync(failedWorker.Task, TimeSpan.Zero, () => failedWarned.SetResult())),
            ("Next controller", () => { nextRan = true; return Task.CompletedTask; }));
        await failedWarned.Task;
        Require(!nextRan && !sequence.IsCompleted, "Later cleanup overtook an unfinished hardware worker.");
        failedWorker.SetException(new IOException("late cleanup failure"));
        var failures = await sequence;
        Require(nextRan && failures.Count == 1 && failures[0].Part == "Lighting", "Late failure skipped the next controller or lost its error.");
    }
}
