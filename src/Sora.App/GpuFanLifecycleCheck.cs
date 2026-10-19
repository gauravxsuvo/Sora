using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

// An isolated simulated driver exercises application ownership/persistence. It never constructs NvidiaGpuFans or starts SoraHost.
internal static class GpuFanLifecycleCheck
{
    public static async Task<bool> RunAsync(string output)
    {
        var results = new List<object>(); string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "gpu-lifecycle-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        static void Assert(bool condition, string message) { if (!condition) throw new IOException(message); }
        async Task Check(string name, Func<Task> action)
        {
            try { await action(); results.Add(new { name, passed = true }); }
            catch (Exception error) { results.Add(new { name, passed = false, error = error.ToString() }); }
        }
        (SoraHost Host, Memory State, GpuFanService Service) Setup(string name)
        {
            var host = new SoraHost(Path.Combine(root, name)); var state = new Memory();
            return (host, state, new GpuFanService(host, () => state.Connect(), () => { }, false));
        }
        GpuFanService Reopen(SoraHost host, Memory state) => new(host, () => state.Connect(), () => { }, false);
        string Backup(SoraHost host) => Path.Combine(host.DataDirectory, "gpu-fans-original.json");
        string Desired(SoraHost host) => Path.Combine(host.DataDirectory, "gpu-fans-profile.json");
        var native = new GpuFanProfile { Name = "Native saved", Mode = GpuFanMode.HardwareFixed, FixedRpm = 1500, AutoFanStop = false };
        var software = new GpuFanProfile { Name = "Software saved", Mode = GpuFanMode.Fixed, FixedDutyPercent = 50 };
        await Check("First launch with no applied profile does not open the driver or write settings", async () =>
        {
            var (_, state, service) = Setup("fresh"); await service.ApplyAtStartupAsync(); Assert(state.Connections == 0 && state.Writes.Count == 0, "Fresh startup touched the driver."); await service.StopAsync();
        });
        await Check("Native Exit retains programmed state and original backup; reopening recovers original first then reapplies", async () =>
        {
            var (host, state, service) = Setup("native-exit");
            state.BeforeWrite = () => Assert(File.Exists(Backup(host)) && JsonSerializer.Deserialize<GpuFanBackup[]>(File.ReadAllText(Backup(host)))!.Single().Control.SequenceEqual(state.Original.Control), "Original backup was not durable before write.");
            await service.ApplyAsync(state.Identity, native); Assert(service.IsActive(state.Identity), service.Message);
            var target = state.Current; string backupBytes = File.ReadAllText(Backup(host)); int writes = state.Writes.Count;
            await service.StopAsync(); Assert(state.Writes.Count == writes && NvidiaGpuFans.Equivalent(target, state.Current), "Native Exit restored the old profile.");
            Assert(File.Exists(Desired(host)) && File.Exists(Backup(host)), "Native Exit lost desired/original state.");
            var reopened = Reopen(host, state); await reopened.ApplyAtStartupAsync();
            Assert(reopened.IsActive(state.Identity) && NvidiaGpuFans.Equivalent(target, state.Current), reopened.Message);
            Assert(NvidiaGpuFans.Equivalent(state.Original, state.Writes[writes]) && NvidiaGpuFans.Equivalent(target, state.Writes[writes + 1]), "Recovery did not precede reapplication.");
            Assert(File.ReadAllText(Backup(host)) == backupBytes, "Reapplication overwrote the first original backup."); await reopened.StopAsync();
        });
        await Check("Software Exit restores original and reopening reapplies saved duty", async () =>
        {
            var (host, state, service) = Setup("software-exit"); await service.ApplyAsync(state.Identity, software); Assert(service.IsActive(state.Identity), service.Message); var target = state.Current;
            await service.StopAsync(); Assert(NvidiaGpuFans.Equivalent(state.Original, state.Current), "Software Exit left manual speed active.");
            Assert(!File.Exists(Backup(host)) && File.Exists(Desired(host)), "Software Exit lost desired profile or retained stale backup.");
            var reopened = Reopen(host, state); await reopened.ApplyAtStartupAsync(); Assert(NvidiaGpuFans.Equivalent(target, state.Current) && reopened.IsActive(state.Identity), reopened.Message); await reopened.StopAsync();
        });
        await Check("Suspend restores software control and disposes its driver; resume reconnects and reapplies", async () =>
        {
            var (_, state, service) = Setup("software-sleep"); await service.ApplyAsync(state.Identity, software); var target = state.Current;
            await service.SuspendAsync(); Assert(state.Disposals == 1 && NvidiaGpuFans.Equivalent(state.Original, state.Current) && !service.IsActive(state.Identity), "Software suspend left control or connection active.");
            await service.ApplyAtStartupAsync(); Assert(state.Connections == 2 && NvidiaGpuFans.Equivalent(target, state.Current) && service.IsActive(state.Identity), service.Message); await service.StopAsync();
        });
        await Check("Native suspend preserves GPU-managed profile and resume reapplies after a simulated driver reset", async () =>
        {
            var (host, state, service) = Setup("native-sleep"); await service.ApplyAsync(state.Identity, native); var target = state.Current; string originalFile = File.ReadAllText(Backup(host));
            int writes = state.Writes.Count; await service.SuspendAsync(); Assert(state.Writes.Count == writes && NvidiaGpuFans.Equivalent(target, state.Current), "Native suspend changed the profile.");
            state.Current = state.Original; await service.ApplyAtStartupAsync(); Assert(state.Connections == 2 && NvidiaGpuFans.Equivalent(target, state.Current), service.Message);
            Assert(File.ReadAllText(Backup(host)) == originalFile, "Resume recaptured custom settings as the original."); await service.StopAsync();
        });
        await Check("Repeated resume repairs a reset active native profile without replacing its original backup", async () =>
        {
            var (host, state, service) = Setup("native-reset"); await service.ApplyAsync(state.Identity, native); var target = state.Current; string originalFile = File.ReadAllText(Backup(host));
            state.Current = state.Original; await service.ApplyAtStartupAsync(); Assert(NvidiaGpuFans.Equivalent(target, state.Current) && File.ReadAllText(Backup(host)) == originalFile, "Active driver reset was not repaired."); await service.StopAsync();
        });
        await Check("Explicit Restore clears reapplication but preserves named profiles", async () =>
        {
            var (host, state, service) = Setup("explicit-restore"); service.Profiles.Save(state.Identity, native); await service.ApplyAsync(state.Identity, native); await service.RestoreAsync(state.Identity);
            Assert(NvidiaGpuFans.Equivalent(state.Original, state.Current) && !File.Exists(Desired(host)) && !File.Exists(Backup(host)), service.Message);
            Assert(service.Profiles.Read(state.Identity).Single().Name == native.Name, "Explicit Restore deleted the profile library."); await service.StopAsync();
            int connections = state.Connections; var reopened = Reopen(host, state); await reopened.ApplyAtStartupAsync(); Assert(state.Connections == connections && reopened.CurrentProfile(state.Identity) == null, "Restore still reapplies on reopening."); await reopened.StopAsync();
        });
        await Check("Corrupt original backup blocks every write and remains byte-identical", async () =>
        {
            var (host, state, service) = Setup("corrupt"); Directory.CreateDirectory(host.DataDirectory); File.WriteAllText(Backup(host), "{corrupt");
            await service.ApplyAsync(state.Identity, native); Assert(state.Writes.Count == 0 && File.ReadAllText(Backup(host)) == "{corrupt" && service.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase), "Corrupt backup was overwritten or ignored."); await service.StopAsync();
        });
        await Check("Foreign GPU identity blocks recovery/reapplication and preserves both saved files", async () =>
        {
            var (host, state, service) = Setup("foreign"); await service.ApplyAsync(state.Identity, native); await service.StopAsync();
            string backup = File.ReadAllText(Backup(host)), desired = File.ReadAllText(Desired(host)); int writes = state.Writes.Count; state.ReportForeignGpu = true;
            var reopened = Reopen(host, state); await reopened.ApplyAtStartupAsync();
            Assert(reopened.StartupPending && state.Writes.Count == writes && File.ReadAllText(Backup(host)) == backup && File.ReadAllText(Desired(host)) == desired, "Foreign GPU received a backup/profile or changed saved files."); await reopened.StopAsync();
        });
        await Check("Transient startup conflict retries observably then successfully reapplies", async () =>
        {
            var (host, state, service) = Setup("retry"); await service.ApplyAsync(state.Identity, native); var target = state.Current; await service.StopAsync(); state.Current = state.Original;
            int attempts = 0; var reopened = new GpuFanService(host, () => state.Connect(), () => { if (++attempts == 1) throw new IOException("Simulated startup controller conflict"); }, false);
            await reopened.ApplyAtStartupAsync(); Assert(attempts >= 2 && reopened.IsActive(state.Identity) && NvidiaGpuFans.Equivalent(target, state.Current), reopened.Message);
            Assert(File.ReadAllText(Path.Combine(host.DataDirectory, "sora.log")).Contains("attempt 1/3 failed", StringComparison.Ordinal), "Retry was not observable in the log."); await reopened.StopAsync();
        });
        await Check("Cancellation leaves pending recovery and saved files intact", async () =>
        {
            var (host, state, service) = Setup("cancelled"); await service.ApplyAsync(state.Identity, native); await service.StopAsync(); int writes = state.Writes.Count;
            var reopened = Reopen(host, state); using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await reopened.ApplyAtStartupAsync(cancellation.Token);
            Assert(state.Writes.Count == writes && File.Exists(Backup(host)) && File.Exists(Desired(host)), "Cancellation mutated hardware or deleted recovery files."); await reopened.StopAsync();
        });
        await Check("Failed apply rolls back to the previous active profile without overwriting remembered intent", async () =>
        {
            var (host, state, service) = Setup("failed-replace"); await service.ApplyAsync(state.Identity, native); var previous = state.Current; string desired = File.ReadAllText(Desired(host)); state.FailNextWriteAfterMutation = true;
            await service.ApplyAsync(state.Identity, native with { FixedRpm = 1900 }); Assert(NvidiaGpuFans.Equivalent(previous, state.Current) && service.CurrentProfile(state.Identity)?.FixedRpm == 1500 && File.ReadAllText(Desired(host)) == desired, "A failed replacement lost the previous profile."); await service.StopAsync();
        });
        await Check("Remembered native target preserves accumulated individual-fan changes across reopening", async () =>
        {
            var (host, state, service) = Setup("per-channel"); await service.ApplyAsync(state.Identity, native with { FanIndices = [0], FixedRpm = 1400 }); await service.ApplyAsync(state.Identity, native with { FanIndices = [1], FixedRpm = 1800 }); var target = state.Current;
            await service.StopAsync(); state.Current = state.Original; var reopened = Reopen(host, state); await reopened.ApplyAtStartupAsync(); Assert(NvidiaGpuFans.Equivalent(target, state.Current), "Reopening retained only the most recently edited fan."); await reopened.StopAsync();
        });
        bool success = results.All(result => JsonSerializer.SerializeToElement(result).GetProperty("passed").GetBoolean());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllText(output, JsonSerializer.Serialize(new { passed = success, hardwareWrites = 0, driver = "injected memory-only GPU", checks = results, isolatedDataDirectory = root }, new JsonSerializerOptions { WriteIndented = true })); return success;
    }
    private sealed class Memory
    {
        public string Identity => Original.Identity;
        public GpuFanBackup Original { get; }
        public GpuFanBackup Current { get; set; }
        public List<GpuFanBackup> Writes { get; } = new();
        public int Connections, Disposals;
        public bool ReportForeignGpu, FailNextWriteAfterMutation;
        public Action? BeforeWrite;
        public Memory()
        {
            static void Put(byte[] data, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);
            byte[] control = new byte[NvidiaGpuFans.ControlSize], curve = new byte[220], stop = new byte[292]; Put(control, 0, 0x105AC); Put(control, 8, 3); Put(curve, 0, 0x200DC); Put(curve, 4, 3); Put(stop, 0, 0x10124); Put(stop, 4, 3);
            for (int fan = 0; fan < 3; fan++)
            {
                Put(control, 44 + fan * 44, (uint)fan + 1); Put(curve, 20 + fan * 52, (uint)fan); Put(stop, 36 + fan * 8, (uint)fan); Put(stop, 40 + fan * 8, 1);
                var points = new[] { new GpuRpmPoint(55, 998), new(76, 2400), new(84, 3000) };
                for (int point = 0; point < 3; point++) { int offset = 24 + fan * 52 + point * 12; Put(curve, offset, (uint)(points[point].TemperatureC * 256)); Put(curve, offset + 4, (uint)Math.Round(points[point].Rpm / 30d) * 65536 / 100); Put(curve, offset + 8, points[point].Rpm); }
            }
            Original = new GpuFanBackup("nvidia:00000002:2C0510DE:41A81458", control) { FirmwareCurve = curve, FirmwareStop = stop }; Current = Original;
        }
        public IGpuFanAccess Connect() { Connections++; return new Connection(this); }
        private sealed class Connection(Memory memory) : IGpuFanAccess
        {
            private bool disposed;
            public GpuFanSnapshot[] Read()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return [new GpuFanSnapshot(memory.ReportForeignGpu ? "foreign-gpu" : memory.Identity, "Simulated RTX 5070 Ti", 2, 0x2C0510DE, 0x41A81458, 46, [new(1, 0, 0, 30, 100, 0), new(2, 0, 0, 30, 100, 0), new(3, 0, 0, 30, 100, 0)], true, "Memory-only lifecycle fixture", DateTimeOffset.Now) { Hardware = GpuNativeFanProtocol.Decode(memory.Current.FirmwareCurve!, memory.Current.FirmwareStop!) }];
            }
            public GpuFanBackup Capture(string identity) { ObjectDisposedException.ThrowIf(disposed, this); if (memory.ReportForeignGpu || identity != memory.Identity) throw new IOException("Foreign GPU capture denied"); return memory.Current; }
            public void Write(GpuFanBackup target)
            {
                ObjectDisposedException.ThrowIf(disposed, this); NvidiaGpuFans.ValidateBackup(target); memory.BeforeWrite?.Invoke(); memory.Current = target; memory.Writes.Add(target);
                if (memory.FailNextWriteAfterMutation) { memory.FailNextWriteAfterMutation = false; throw new IOException("Simulated partial apply failure"); }
            }
            public void Dispose() { if (disposed) return; disposed = true; memory.Disposals++; }
        }
    }
}
