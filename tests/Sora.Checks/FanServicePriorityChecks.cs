using System.Text.Json;
using Sora.Core;

internal static class FanServicePriorityChecks
{
    public static void Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "sora-fan-priority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var baseline = Baseline();
            (Memory System, FanServicePriority Priority, string Journal) Setup(string name, bool gcc = true, bool deepcool = true)
            {
                var path = Path.Combine(root, name, "ownership.json");
                var memory = new Memory(baseline, path, gcc, deepcool);
                return (memory, new FanServicePriority(memory, path), path);
            }
            var (idle, idlePriority, _) = Setup("idle", false, false);
            Require(!idlePriority.Acquire() && idle.Captures == 0 && idle.Events.Count == 0, "Stopped helpers caused hardware access.");
            var (normal, priority, journal) = Setup("normal");
            Require(priority.Acquire() && normal.Captures == 1 && normal.Restores == 1 && normal.Services().All(s => !s.Running), "Automatic takeover failed.");
            Require(FanMaintenance.Matches(baseline, normal.Current) && File.Exists(journal), "Stop-time drift was not restored and journaled.");
            Require(!priority.Acquire() && normal.Captures == 1, "An ordinary ownership check rewrote hardware.");
            priority.Release();
            Require(normal.Services().All(s => s.Running) && !File.Exists(journal), "Original service state was not restored.");
            var (one, onePriority, _) = Setup("one", true, false);
            onePriority.Acquire(); onePriority.Release();
            Require(one.Services()[0].Running && !one.Services()[1].Running && !one.Events.Contains("Start FanControl"), "A previously stopped helper was started.");
            var (restart, restartPriority, _) = Setup("restart");
            restartPriority.Acquire(); restart.Current = Drift(baseline); var live = restart.Current;
            restart.Running["FanControl"] = true; restartPriority.Acquire();
            Require(restart.Captures == 2 && FanMaintenance.Matches(live, restart.Current), "A restarted helper used the old session baseline.");
            var (unknown, unknownPriority, _) = Setup("unrelated"); unknown.ExtraWriter = "ArgusMonitor";
            Reject(() => unknownPriority.Acquire()); Require(unknown.Events.Count == 0 && unknown.Captures == 0, "An unrelated controller was stopped or overwritten.");
            var (failure, failurePriority, failureJournal) = Setup("stop-failure"); failure.FailStop = "EasyTuneEngineService";
            Reject(() => failurePriority.Acquire());
            Require(File.Exists(failureJournal) && failure.Restores == 0, "Failed stop discarded recovery evidence.");
            failure.FailStop = null;
            var recovered = new FanServicePriority(failure, failureJournal); recovered.Release();
            Require(failure.Events.IndexOf("Restore") < failure.Events.FindIndex(e => e.StartsWith("Start ", StringComparison.Ordinal)) && FanMaintenance.Matches(baseline, failure.Current), "Crash recovery restarted a helper before restoring the saved curve.");
            var (restoreFailure, restorePriority, restoreJournal) = Setup("restore-failure"); restoreFailure.FailRestore = true;
            Reject(() => restorePriority.Acquire()); Reject(restorePriority.Release);
            Require(!restoreFailure.Events.Any(e => e.StartsWith("Start ", StringComparison.Ordinal)) && File.Exists(restoreJournal), "Unverified recovery restarted a writer.");
            restoreFailure.FailRestore = false; restorePriority.Release();
            var (late, latePriority, lateJournal) = Setup("late-process"); late.ExtraWriter = "FanControlService";
            Reject(() => latePriority.Acquire()); Require(late.Restores == 0 && File.Exists(lateJournal), "Recovery raced a lingering writer.");
            late.ExtraWriter = null; latePriority.Release();
            var (disk, diskPriority, diskJournal) = Setup("disk-full"); Directory.CreateDirectory(diskJournal + ".tmp");
            Reject(() => diskPriority.Acquire()); Require(disk.Events.Count == 0, "Service stopped before durable backup succeeded.");
            var (resume, resumePriority, resumeJournal) = Setup("resume-failure"); resumePriority.Acquire(); resume.FailStart = "EasyTuneEngineService";
            Reject(resumePriority.Release);
            Require(resume.Running["FanControl"] && !resume.Running["EasyTuneEngineService"] && File.Exists(resumeJournal), "One restart failure skipped another service or erased recovery state.");
            resume.FailStart = null; new FanServicePriority(resume, resumeJournal).Release();
            Require(resume.Events.Count(e => e == "Start FanControl") == 1 && !File.Exists(resumeJournal), "Retry restarted an already released service.");
            var (corrupt, corruptPriority, corruptJournal) = Setup("corrupt"); Directory.CreateDirectory(Path.GetDirectoryName(corruptJournal)!); File.WriteAllText(corruptJournal, "{}");
            Reject(() => corruptPriority.Acquire()); Require(corrupt.Events.Count == 0, "Malformed ownership data changed a service.");
            Require(!FanServicePriority.Supports(new("FanControl", @"C:\Other\FanControlService.exe", true))
                && !FanServicePriority.Supports(new("Unrelated", @"C:\DeepCool\FanControlService.exe", true)), "Automatic takeover accepted an unrelated service.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Require(bool pass, string error) { if (!pass) throw new Exception(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or AggregateException) { return; }
        throw new Exception("Unsafe service operation was accepted.");
    }
    private static FanSnapshot Baseline()
    {
        var fixture = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "gigabyte-z890-fans.json")))!;
        return new(GigabyteFans.SupportedBoard, FanMaintenance.Controller, DateTimeOffset.UtcNow, [35, 40, 45, 42, 46, 35],
            fixture.Select(h => new FanHeaderState(h.Header, h.Name, 1200, (h.Registers.Single(r => r.Register == FanProtocol.ControlRegister(h.Header)).Value & 0x80) != 0,
                FanProtocol.Decode(h.Header, h.Registers.ToDictionary(r => r.Register, r => r.Value)), h.Registers) { FirmwareStop = new(false, 2, 0, 0) }).ToArray())
            { FirmwareInterface = FanMaintenance.FirmwareInterface };
    }
    private static FanSnapshot Drift(FanSnapshot baseline) => baseline with
    {
        Headers = baseline.Headers.Select(h => h with { Registers = h.Registers.Select(r => r.Register == FanProtocol.ControlRegister(h.Header) ? r : r with { Value = (byte)(r.Value ^ 1) }).ToArray() }).ToArray()
    };
    private sealed record Fixture(int Header, string Name, FanRegisterValue[] Registers);
    private sealed class Memory(FanSnapshot baseline, string journal, bool gcc, bool deepcool) : IFanPrioritySystem
    {
        public Dictionary<string, bool> Running { get; } = new() { ["EasyTuneEngineService"] = gcc, ["FanControl"] = deepcool };
        public FanSnapshot Current { get; set; } = baseline;
        public int Captures, Restores;
        public string? ExtraWriter, FailStop, FailStart;
        public bool FailRestore;
        public List<string> Events { get; } = [];
        public ManagedFanService[] Services() => [new("EasyTuneEngineService", @"C:\Gigabyte\EasyTuneEngineService.exe", Running["EasyTuneEngineService"]), new("FanControl", @"C:\DeepCool\service\FanControlService.exe", Running["FanControl"])];
        public string[] Writers() => Services().Where(s => s.Running).Select(s => s.Name == "FanControl" ? "FanControlService" : s.Name).Concat(ExtraWriter == null ? [] : new[] { ExtraWriter }).ToArray();
        public FanSnapshot Capture() { Captures++; return Current; }
        public void Restore(FanSnapshot saved) { Events.Add("Restore"); if (FailRestore) throw new IOException("Injected readback failure."); Current = saved; Restores++; }
        public void Stop(ManagedFanService service)
        {
            Require(File.Exists(journal), "Service stop preceded durable baseline.");
            Events.Add("Stop " + service.Name); Running[service.Name] = false; Current = Drift(Current);
            if (FailStop == service.Name) throw new IOException("Injected late Stop failure.");
        }
        public void Start(ManagedFanService service) { Events.Add("Start " + service.Name); if (FailStart == service.Name) throw new IOException("Injected restart failure."); Running[service.Name] = true; }
    }
}
