namespace Sora.Core;

public sealed record FanCalibrationReading(double? CpuTemperatureC, double? Rpm);
public sealed record FanCalibrationSample(int DutyPercent, double Rpm, double CpuTemperatureC, string Phase);
public sealed record FanCalibrationResult(int MinimumRunningDutyPercent, int? MinimumStartingDutyPercent, FanCalibrationSample[] Samples);
public interface IFanCalibrationSession : IDisposable
{
    FanCalibrationReading Read();
    void SetDuty(int dutyPercent);
    void Restore();
}

/// <summary>GCC-derived settling boundary, timing and RPM floor. Hardware access is injected for failure/cancellation tests.</summary>
public static class FanCalibration
{
    public const int MinimumRpm = 400;
    public static async Task<FanCalibrationResult> RunAsync(Func<IFanCalibrationSession> open,
        Action<FanCalibrationSample>? progress, CancellationToken token,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        using var session = await Task.Run(open, CancellationToken.None);
        var samples = new List<FanCalibrationSample>();
        bool changed = false;
        void Check(FanCalibrationReading reading)
        {
            if (reading.CpuTemperatureC is not > 0 or >= 85 || reading.Rpm == null || reading.Rpm < 0 || !double.IsFinite(reading.Rpm.Value))
                throw new IOException("Calibration stopped: CPU temperature is missing or at least 85°C, or RPM is unavailable.");
        }
        async Task<FanCalibrationReading> Settle(int duty, string phase, bool starting = false)
        {
            token.ThrowIfCancellationRequested();
            changed = true;
            await Task.Run(() => session.SetDuty(duty), CancellationToken.None);
            int seconds = starting || duty < 60 ? 5 : 2;
            FanCalibrationReading reading = new(null, null);
            for (int i = 0; i < seconds; i++)
            {
                await delay(TimeSpan.FromSeconds(1), token);
                reading = await Task.Run(session.Read, CancellationToken.None); Check(reading);
            }
            var sample = new FanCalibrationSample(duty, reading.Rpm!.Value, reading.CpuTemperatureC!.Value, phase);
            samples.Add(sample); progress?.Invoke(sample);
            return reading;
        }
        try
        {
            token.ThrowIfCancellationRequested();
            var initial = await Task.Run(session.Read, CancellationToken.None); Check(initial);
            if (initial.CpuTemperatureC >= 80 || initial.Rpm is not > 0)
                throw new IOException("Calibration needs a spinning fan and CPU temperature below 80°C.");
            int running = 100; bool reachedFloor = false;
            for (int duty = 100; duty >= 0; duty -= 5)
            {
                var reading = await Settle(duty, "Running duty");
                if (reading.Rpm < MinimumRpm) { reachedFloor = true; break; }
                running = duty;
            }
            if (!reachedFloor) return new(0, null, samples.ToArray());
            // Establish a stopped fan before testing starting duty; a slowly spinning fan is not a start test.
            var stopped = await Settle(0, "Stop for restart test", true);
            if (stopped.Rpm > 50) return new(running, null, samples.ToArray());
            int? startingDuty = null;
            for (int duty = Math.Max(5, running); duty <= 100; duty += 5)
            {
                // Reset to stopped between failed start attempts, avoiding a misleading cumulative spin-up result.
                if (duty > Math.Max(5, running)) await Settle(0, "Stop for restart test", true);
                var reading = await Settle(duty, "Starting duty", true);
                if (reading.Rpm >= MinimumRpm) { startingDuty = duty; break; }
            }
            if (startingDuty == null) throw new IOException("Fan did not restart reliably at 100%; original settings are being restored.");
            return new(running, startingDuty, samples.ToArray());
        }
        finally
        {
            Exception? restartError = null;
            if (changed)
            {
                try
                {
                    // A running fan's minimum duty may be too low to START it. Spin it up before restoring that duty.
                    await Task.Run(() => session.SetDuty(100), CancellationToken.None);
                    bool spinning = false;
                    for (int i = 0; i < 5; i++)
                    {
                        await delay(TimeSpan.FromSeconds(1), CancellationToken.None);
                        var reading = await Task.Run(session.Read, CancellationToken.None);
                        if (reading.Rpm is > 0 && double.IsFinite(reading.Rpm.Value)) { spinning = true; break; }
                    }
                    if (!spinning) restartError = new IOException("Fan restart at 100% could not be verified.");
                }
                catch (Exception e) { restartError = e; }
            }
            try { await Task.Run(session.Restore, CancellationToken.None); }
            catch (Exception restore) { if (restartError != null) throw new AggregateException("Fan restart and restoration need attention.", restartError, restore); throw; }
            if (restartError != null) throw new IOException("Original settings restored, but fan restart verification failed.", restartError);
        }
    }
}
