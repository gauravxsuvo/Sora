using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Sora.Core;

public sealed record ManagedFanService(string Name, string Executable, bool Running);
public interface IFanPrioritySystem
{
    ManagedFanService[] Services();
    string[] Writers();
    FanSnapshot Capture();
    void Restore(FanSnapshot snapshot);
    void Stop(ManagedFanService service);
    void Start(ManagedFanService service);
}

// All handles/mutexes are released before stopping a service. Some helpers restore stale
// startup registers on Stop, so preserve every header and firmware setting across the stop.
public sealed class FanServicePriority(IFanPrioritySystem system, string journalPath)
{
    private sealed record Session(int Version, string Machine, FanSnapshot Baseline, ManagedFanService[] Services, bool RecoveryPending);
    private Session? session;
    private bool loaded;
    public bool HasOwnership { get { Load(); return session != null; } }
    public bool NeedsAcquire { get { Load(); return session?.RecoveryPending == true || system.Services().Any(s => s.Running); } }
    public static bool Supports(ManagedFanService service)
    {
        var path = service.Executable.Trim().Trim('"').Replace('/', '\\');
        return service.Name == "EasyTuneEngineService" && path.EndsWith("\\EasyTuneEngineService.exe", StringComparison.OrdinalIgnoreCase)
            || service.Name == "FanControl" && path.Contains("\\DeepCool\\", StringComparison.OrdinalIgnoreCase) && path.EndsWith("\\FanControlService.exe", StringComparison.OrdinalIgnoreCase);
    }
    private static string ProcessName(ManagedFanService service) => service.Name == "FanControl" ? "FanControlService" : "EasyTuneEngineService";
    private void Load()
    {
        if (loaded) return;
        if (File.Exists(journalPath))
        {
            var saved = JsonSerializer.Deserialize<Session>(File.ReadAllText(journalPath)) ?? throw new InvalidDataException("Fan priority recovery record is empty.");
            if (saved.Version != 1 || saved.Machine != Environment.MachineName || saved.Baseline == null || saved.Services == null || saved.Services.Any(s => s == null || !Supports(s))
                || saved.Services.Select(s => s.Name).Distinct().Count() != saved.Services.Length)
                throw new InvalidDataException("Fan priority recovery record is invalid. Preserve it for recovery.");
            FanMaintenance.Validate(saved.Baseline);
            session = saved;
        }
        loaded = true;
    }
    private void Save(Session next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(journalPath))!);
        using (var file = new FileStream(journalPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            JsonSerializer.Serialize(file, next, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true);
        }
        File.Move(journalPath + ".tmp", journalPath, true); session = next;
    }
    public bool Acquire()
    {
        Load();
        var services = system.Services();
        if (services.Any(s => !Supports(s))) throw new InvalidDataException("Unknown fan service; automatic handoff refused.");
        var running = services.Where(s => s.Running).ToArray();
        // Never stop a helper if a desktop controller would prevent baseline recovery afterwards.
        var managedProcesses = services.Select(ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unmanaged = system.Writers().FirstOrDefault(p => !managedProcesses.Contains(p));
        if (unmanaged != null) throw new IOException("Exit " + unmanaged + " before Sora takes fan control.");
        if (running.Length == 0 && session?.RecoveryPending != true) return false;
        var baseline = session?.RecoveryPending == true ? session.Baseline : system.Capture();
        FanMaintenance.Validate(baseline);
        var owned = (session?.Services ?? []).Concat(running).DistinctBy(s => s.Name).ToArray();
        Save(new(1, Environment.MachineName, baseline, owned, true)); // Durable BEFORE the first Stop.
        foreach (var service in running) system.Stop(service);
        if (system.Services().Any(s => s.Running) || system.Writers().Length != 0)
            throw new IOException("A fan controller is still running. The recovery baseline is saved; Sora has not applied a new curve.");
        system.Restore(baseline); // Backend must restore AND verify both register domains.
        Save(session! with { RecoveryPending = false });
        return true;
    }
    public void Release()
    {
        Load(); if (session == null) return;
        if (session.RecoveryPending) Acquire(); // Failed stop/recovery must finish before a service restarts.
        var errors = new List<Exception>();
        foreach (var service in session!.Services.ToArray())
        {
            try
            {
                // Do not start a replacement executable installed under an old service name.
                var current = system.Services().SingleOrDefault(s => s.Name == service.Name);
                if (current != null && !string.Equals(current.Executable, service.Executable, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Fan service executable changed: " + service.Name);
                if (current != null) system.Start(service);
                Save(session with { Services = session.Services.Where(s => s.Name != service.Name).ToArray() });
            }
            catch (Exception error) when (error is not OutOfMemoryException) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Some fan services could not be resumed. Their ownership record is retained.", errors);
        File.Delete(journalPath); session = null;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsFanPrioritySystem : IFanPrioritySystem
{
    public ManagedFanService[] Services()
    {
        var result = new List<ManagedFanService>();
        foreach (var name in new[] { "EasyTuneEngineService", "FanControl" })
        {
            try
            {
                using var service = new ManagementObject("Win32_Service.Name='" + name + "'"); service.Get();
                var state = Convert.ToString(service["State"]);
                var entry = new ManagedFanService(name, Convert.ToString(service["PathName"]) ?? "", state != "Stopped");
                if (!FanServicePriority.Supports(entry)) continue;
                if (state is not ("Running" or "Stopped")) throw new IOException("Wait for " + name + " to finish starting or stopping.");
                result.Add(entry);
            }
            catch (ManagementException error) when (error.ErrorCode == ManagementStatus.NotFound) { }
        }
        return result.ToArray();
    }
    public string[] Writers() => GigabyteFans.FindConflictProcesses();
    public FanSnapshot Capture()
    {
        using var native = new GigabyteFans();
        var snapshot = GigabyteFanFirmware.Enrich(native.ReadSnapshot());
        if (snapshot.FirmwareInterface != FanMaintenance.FirmwareInterface)
            throw new IOException("Install standalone firmware fan support on the Fan control page before automatic helper handoff.");
        FanMaintenance.Validate(snapshot); return snapshot;
    }
    public void Restore(FanSnapshot snapshot)
    {
        if (Writers().Length != 0) throw new IOException("A fan controller restarted before baseline recovery.");
        using var native = new GigabyteFans(); using var firmware = new GigabyteFanFirmware();
        FanMaintenance.Restore(native, firmware, snapshot);
        if (!FanMaintenance.Matches(snapshot, GigabyteFanFirmware.Enrich(native.ReadSnapshot())))
            throw new IOException("Fan settings could not be verified after pausing the helper. The recovery record is retained.");
    }
    public void Stop(ManagedFanService service) => Change(service, "StopService", "Stopped");
    public void Start(ManagedFanService service) => Change(service, "StartService", "Running");
    private static void Change(ManagedFanService expected, string method, string state)
    {
        if (!FanServicePriority.Supports(expected)) throw new InvalidDataException("Unsupported fan service.");
        using var service = new ManagementObject("Win32_Service.Name='" + expected.Name + "'"); service.Get();
        if (!string.Equals(Convert.ToString(service["PathName"]), expected.Executable, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Fan service executable changed: " + expected.Name);
        if (Convert.ToString(service["State"]) == state) return;
        var code = Convert.ToUInt32(service.InvokeMethod(method, null));
        if (code != 0) throw new IOException(expected.Name + " " + method + " failed (" + code + ").");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do
        {
            service.Get(); if (Convert.ToString(service["State"]) == state) return; Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);
        throw new TimeoutException(expected.Name + " did not become " + state + ".");
    }
}
