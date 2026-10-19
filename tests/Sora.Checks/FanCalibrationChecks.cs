using Sora.Core;

internal static class FanCalibrationChecks
{
    public static async Task Run()
    {
        static void Assert(bool test, string message) { if (!test) throw new Exception(message); }
        var fan = new FakeFan(); var progress = new List<FanCalibrationSample>();
        var result = await FanCalibration.RunAsync(() => fan, progress.Add, CancellationToken.None, (_, _) => Task.CompletedTask);
        Assert(result.MinimumRunningDutyPercent == 30 && result.MinimumStartingDutyPercent == 40, "Separate running and starting duty must be measured");
        Assert(fan.Restored && fan.Disposed, "Successful calibration must restore and dispose");
        Assert(progress.Any(s => s.DutyPercent == 0) && progress.Any(s => s.Phase == "Starting duty"), "Calibration must establish stopped/restarted state");
        var hot = new FakeFan { HotAfter = 2 };
        try { await FanCalibration.RunAsync(() => hot, null, CancellationToken.None, (_, _) => Task.CompletedTask); throw new Exception("Hot CPU was ignored"); } catch (IOException) { }
        Assert(hot.Restored && hot.Disposed && hot.Writes == 3, "Thermal abort must command full speed then restore");
        using var cancellation = new CancellationTokenSource(); var canceled = new FakeFan(); int waits = 0;
        try
        {
            await FanCalibration.RunAsync(() => canceled, null, cancellation.Token, (_, token) =>
            { if (++waits == 3) cancellation.Cancel(); return token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask; });
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException) { }
        Assert(canceled.Restored && canceled.Disposed, "Cancellation must restore without the canceled token");
        var noStop = new FakeFan { NeverStops = true };
        var noStopResult = await FanCalibration.RunAsync(() => noStop, null, CancellationToken.None, (_, _) => Task.CompletedTask);
        Assert(noStopResult.MinimumStartingDutyPercent == null && noStop.Restored, "A fan that never stops has no proven starting duty");
        var broken = new FakeFan { CannotRestart = true };
        try { await FanCalibration.RunAsync(() => broken, null, CancellationToken.None, (_, _) => Task.CompletedTask); throw new Exception("Restart failure ignored"); } catch (IOException) { }
        Assert(broken.Restored && broken.Disposed, "Restart failure must restore");
        var missing = new FakeFan { MissingRpm = true };
        try { await FanCalibration.RunAsync(() => missing, null, CancellationToken.None, (_, _) => Task.CompletedTask); throw new Exception("Missing RPM ignored"); } catch (IOException) { }
        Assert(missing.Writes == 0 && missing.Restored, "Invalid initial telemetry must cause no duty writes");
    }
    private sealed class FakeFan : IFanCalibrationSession
    {
        public int HotAfter { get; init; } = int.MaxValue;
        public bool NeverStops { get; init; }
        public bool CannotRestart { get; init; }
        public bool MissingRpm { get; init; }
        public bool Restored { get; private set; }
        public bool Disposed { get; private set; }
        public int Writes { get; private set; }
        private int duty = 100;
        private bool spinning = true;
        public FanCalibrationReading Read() => new(Writes >= HotAfter ? 85 : 45,
            MissingRpm ? null : NeverStops ? Math.Max(500, duty * 20) : spinning ? duty * 20 : 0);
        public void SetDuty(int value)
        {
            Writes++; duty = value;
            if (value < 30) spinning = false;
            else if (!spinning && value >= 40 && !CannotRestart) spinning = true;
        }
        public void Restore() => Restored = true;
        public void Dispose() => Disposed = true;
    }
}
