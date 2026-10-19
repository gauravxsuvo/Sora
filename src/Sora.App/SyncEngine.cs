using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

/// <summary>
/// Sync mode's animated effects (Color cycle, Breathing, Flashing), driven the way GCC drives them in sync: one colour per frame,
/// written as Static to every DIMM and every board light, so both follow one clock (GCC: EneModeLedEffAutoThd and friends,
/// about 9 frames a second on the RAM). Runs while Sora runs; stops on any error or when another RGB program starts.
/// </summary>
internal sealed class SyncEngine
{
    private const int FrameMs = 33;
    private static readonly double[] CyclePeriod = [12.0, 9.0, 6.5, 4.5, 3.0];
    private static readonly double[] BreathPeriod = [4.0, 3.4, 2.8, 2.2, 1.6];
    private static readonly double[] FlashPeriod = [2.4, 2.0, 1.6, 1.2, 0.8];

    private readonly SoraHost host;
    private readonly SyncLighting sync;
    private readonly bool memory, board;
    private readonly CancellationTokenSource stop = new();
    private Task? task;
    private int appliedFrames;

    public SyncEngine(SoraHost host, SyncLighting sync, bool memory, bool board) => (this.host, this.sync, this.memory, this.board) = (host, sync, memory, board);

    public static bool Animated(SyncEffect effect) => effect is SyncEffect.ColorCycle or SyncEffect.Breathing or SyncEffect.Flashing;

    public void Start() => task = Task.Run(Run);

    public bool Running => task is { IsCompleted: false };
    public bool HasAppliedFrame => Volatile.Read(ref appliedFrames) > 0;
    public bool CoversBothTargets => memory && board;
    public SyncLighting Look => sync;

    /// <summary>Stops and waits for the current frame to finish, so the hardware is free when this returns.</summary>
    public void Stop()
    {
        stop.Cancel();
        if (task is { } worker)
            CleanupSequence.JoinAsync(worker, TimeSpan.FromSeconds(3),
                () => host.Log("Lighting sync is still stopping; waiting for its hardware handles to close."))
                .GetAwaiter().GetResult();
    }

    /// <summary>The colour at a moment of the effect, already scaled by the brightness.</summary>
    public static (byte R, byte G, byte B) ColorAt(SyncLighting sync, double seconds)
    {
        var speed = Math.Clamp(sync.Speed, 1, 5) - 1;
        var level = Math.Clamp(sync.Brightness, 10, 100) / 100.0;
        var (r, g, b) = LightingService.ParseColor(sync.Color);
        double factor;
        switch (sync.Effect)
        {
            case SyncEffect.ColorCycle:
                var c = ColorWheel.FromHsv(seconds / CyclePeriod[speed] * 360, 1, level);
                return (c.R, c.G, c.B);
            case SyncEffect.Breathing:
                factor = (1 - Math.Cos(seconds / BreathPeriod[speed] * 2 * Math.PI)) / 2;
                break;
            case SyncEffect.Flashing:
                factor = seconds % FlashPeriod[speed] < 0.16 ? 1 : 0;
                break;
            default:
                factor = 1;
                break;
        }
        factor *= level;
        return ((byte)Math.Round(r * factor), (byte)Math.Round(g * factor), (byte)Math.Round(b * factor));
    }

    private void Run()
    {
        Smbus? bus = null;
        GigabyteRgb? controller = null;
        var frames = 0;
        long slowest = 0, busy = 0;
        try
        {
            IReadOnlyList<EneDimm> dimms = [];
            IReadOnlyList<int> zones = [];
            var (r0, g0, b0) = ColorAt(sync, 0);
            // Setup with the normal, fully verified paths: Static on the DIMMs (read back) and on every board light.
            // A device that cannot be opened is left out; the other one still runs.
            if (memory)
            {
                try
                {
                    bus = Smbus.Open();
                    dimms = EneDram.Scan(bus).Dimms;
                    if (dimms.Count > 0) EneDram.Apply(bus, dimms, EneMode.Static, r0, g0, b0, 0, false, EneColorOrder.Rbg);
                }
                catch (Exception error) when (error is System.IO.IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
                {
                    host.Log("Lighting sync: memory left out: " + error.Message);
                    dimms = [];
                    bus?.Dispose();
                    bus = null;
                }
            }
            if (board)
            {
                try
                {
                    controller = new GigabyteRgb();
                    zones = GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel()).Select(z => z.Number).ToArray();
                    controller.ApplyZones(zones.Select(z => new BoardZoneEffect(z, BoardEffect.Static, r0, g0, b0, 8, 0)).ToArray(), GigabyteRgb.ZonesFor(GigabyteRgb.BoardModel()), initialize: true);
                }
                catch (Exception error) when (error is System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    host.Log("Lighting sync: motherboard left out: " + error.Message);
                    zones = [];
                    controller?.Dispose();
                    controller = null;
                }
            }
            if (dimms.Count == 0 && zones.Count == 0) return;
            host.Log($"Lighting sync: {sync.Effect} {sync.Color} {sync.Brightness}% speed {sync.Speed} on {dimms.Count} DIMM(s) and {zones.Count} board light(s)");
            // Block writes (GCC's way, one transaction for all colours) are used only after one has been read back exactly.
            var method = EneFrameWrite.Bytes;
            if (dimms.Count > 0)
            {
                // Each block method is tried with a test colour that differs from the current one and must read back exactly.
                foreach (var candidate in new[] { EneFrameWrite.SmbusBlock, EneFrameWrite.I2cBlock })
                {
                    try
                    {
                        var (tr, tg, tb) = ((byte)(r0 ^ 1), (byte)(g0 ^ 1), (byte)(b0 ^ 1));
                        EneDram.WriteFrame(bus!, dimms, tr, tg, tb, EneColorOrder.Rbg, candidate);
                        var expected = EneDram.Encode(tr, tg, tb, EneColorOrder.Rbg);
                        if (dimms.All(d => EneDram.Read(bus!, d).Colors.Chunk(3).All(led => led.SequenceEqual(expected)))) { method = candidate; break; }
                        host.Log($"Lighting sync: {candidate} did not read back");
                    }
                    catch (Exception error) when (error is System.IO.IOException or InvalidOperationException) { host.Log($"Lighting sync: {candidate} unavailable: " + error.Message); }
                }
                // Whatever happened above, start from a verified Static frame.
                EneDram.Apply(bus!, dimms, EneMode.Static, r0, g0, b0, 0, false, EneColorOrder.Rbg);
                host.Log("Lighting sync: memory frames by " + method);
            }
            byte? lastBrightness = null;
            var clock = Stopwatch.StartNew();
            long checkedAt = 0, conflictAt = 0;
            var last = (r0, g0, b0);
            while (!stop.IsCancellationRequested)
            {
                var frameStart = clock.ElapsedMilliseconds;
                var color = ColorAt(sync, frameStart / 1000.0);
                if (color != last)
                {
                    if (zones.Count > 0) controller!.SendFrame(zones, color.R, color.G, color.B);
                    if (dimms.Count > 0)
                    {
                        EneDram.WriteFrame(bus!, dimms, color.R, color.G, color.B, EneColorOrder.Rbg, method, lastBrightness);
                        lastBrightness = EneDram.GccBrightness(EneMode.Static, color.R, color.G, color.B);
                    }
                    last = color;
                    frames++;
                    Volatile.Write(ref appliedFrames, frames);
                }
                // Every two seconds: stop if another RGB program appeared. Every ten: re-check each DIMM is still the same ENE controller.
                if (frameStart - conflictAt > 2000)
                {
                    conflictAt = frameStart;
                    if (EneDram.FindConflict() is { } conflict) { host.Log("Lighting sync stopped: " + conflict); return; }
                }
                if (frameStart - checkedAt > 10000)
                {
                    checkedAt = frameStart;
                    foreach (var dimm in dimms) EneDram.Read(bus!, dimm);
                }
                var spent = clock.ElapsedMilliseconds - frameStart;
                slowest = Math.Max(slowest, spent);
                busy += spent;
                if (stop.Token.WaitHandle.WaitOne((int)Math.Max(5, FrameMs - spent))) break;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { host.Log("Lighting sync stopped: " + error.Message); }
        finally
        {
            controller?.Dispose();
            bus?.Dispose();
            host.Log($"Lighting sync ended after {frames} frame(s); average {(frames == 0 ? 0 : busy / frames)} ms, slowest {slowest} ms");
        }
    }
}
