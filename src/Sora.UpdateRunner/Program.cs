using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Sora.Core.Updates;
using Sora.Update.Windows;

namespace Sora.UpdateRunner;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? stage = null;
        IDisposable? gate = null;
        var committed = false;
        Process? parent = null;
        var tray = false;
        try
        {
            if (args.Length != 5 || !int.TryParse(args[1], out var parentId) || !long.TryParse(args[2], out var started) ||
                !Guid.TryParseExact(args[3], "N", out _) || args[4] is not ("window" or "tray")) throw new ArgumentException("Invalid update handoff.");
            tray = args[4] == "tray";
            stage = UpdatePaths.Stage(args[0]);
            if (!Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(stage, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Run the updater from its protected transaction directory.");
            gate = UpdatePaths.EnterGate();
            parent = Process.GetProcessById(parentId);
            if (parent.StartTime.ToUniversalTime().Ticks != started || parent.SessionId != Process.GetCurrentProcess().SessionId ||
                !string.Equals(parent.MainModule?.FileName, UpdatePaths.InstalledExe, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The initiating Sora process could not be verified.");
            RequireNoOtherSora(parentId);
            var policy = BundledUpdatePolicy.Load();
            var envelope = ReadEnvelope(stage);
            var sequence = long.Parse(File.ReadAllText(Path.Combine(stage, "sequence.txt")), System.Globalization.CultureInfo.InvariantCulture);
            var release = ReleaseFeed.Verify(envelope, policy, DateTimeOffset.UtcNow, sequence, Environment.OSVersion.Version);
            var installedVersion = InstalledVersion();
            if (ReleaseFeed.ParseVersion(release.Version) <= installedVersion) throw new IOException("The update is not newer than the installed Sora.");
            var installer = Path.Combine(stage, "installer.exe");
            UpdatePaths.RejectLinks(installer);
            // Keep the file open through execution so it cannot be replaced after verification.
            using var heldInstaller = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
            ReleaseDownload.VerifyFile(installer, release);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using (var pipe = new NamedPipeClientStream(".", "Sora.Update." + args[3], PipeDirection.InOut, PipeOptions.CurrentUserOnly))
            {
                pipe.Connect(15000);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                writer.WriteLine("READY");
                var permission = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                if (permission != "INSTALL") throw new IOException("Sora did not confirm successful hardware cleanup.");
                committed = true;
            }
            if (!parent.WaitForExit(60000)) throw new IOException("Sora has not exited. Installation was cancelled without stopping it.");
            RequireNoOtherSora(-1);
            // Repeat freshness checks after the cleanup handoff, before executing any installer bytes.
            _ = ReleaseFeed.Verify(envelope, policy, DateTimeOffset.UtcNow, sequence, Environment.OSVersion.Version);
            var info = new ProcessStartInfo(installer) { UseShellExecute = false, WorkingDirectory = stage };
            foreach (var argument in new[] { "/SILENT", "/NORESTART", "/NOCLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS", "/RESTARTEXITCODE=3010", "/LOG=" + Path.Combine(stage, "installer.log") })
                info.ArgumentList.Add(argument);
            using var setup = Process.Start(info) ?? throw new IOException("Setup could not start.");
            setup.WaitForExit();
            if (setup.ExitCode == 3010)
            {
                Result(stage, "reboot-required", "Installation requires a Windows restart. Restart Windows before opening Sora.");
                MessageBox(IntPtr.Zero, "Sora was installed and Windows needs to restart. Restart Windows before opening Sora again.", "Sora update", 0x40);
                return 3;
            }
            if (setup.ExitCode != 0) throw new IOException("Setup failed with exit code " + setup.ExitCode + ". See installer.log; use the retained installer for repair.");
            if (InstalledVersion() != ReleaseFeed.ParseVersion(release.Version)) throw new IOException("The installed Sora version did not match the update.");
            Result(stage, "installed", "Sora " + release.Version + " installed.");
            // Release the startup gate before launching the new host.
            gate.Dispose(); gate = null;
            var launch = new ProcessStartInfo(UpdatePaths.InstalledExe) { UseShellExecute = false, WorkingDirectory = UpdatePaths.InstallDirectory };
            launch.ArgumentList.Add("--update-complete"); launch.ArgumentList.Add(args[0]);
            if (tray) launch.ArgumentList.Add("--tray");
            using var restarted = Process.Start(launch) ?? throw new IOException("The updated Sora could not start. Open Sora from its shortcut.");
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline && !File.Exists(Path.Combine(stage, "healthy.txt")) && !restarted.HasExited) Thread.Sleep(250);
            if (!File.Exists(Path.Combine(stage, "healthy.txt"))) throw new IOException("Sora installed, but startup was not confirmed. Open Sora from its shortcut and inspect update logs.");
            Result(stage, "complete", "Sora " + release.Version + " installed and startup confirmed.");
            return 0;
        }
        catch (Exception error)
        {
            if (stage != null)
            {
                try { Result(stage, "failed", error.Message); } catch (Exception) { }
            }
            // Never start the old/new app automatically on a failed or interrupted install.
            if (committed) MessageBox(IntPtr.Zero, error.Message + "\n\nUpdate logs: " + stage, "Sora update", 0x10);
            return 1;
        }
        finally { parent?.Dispose(); gate?.Dispose(); }
    }

    private static byte[] ReadEnvelope(string stage)
    {
        var path = Path.Combine(stage, "release.json"); UpdatePaths.RejectLinks(path);
        if (new FileInfo(path).Length > ReleaseFeed.MaximumEnvelopeBytes) throw new IOException("Invalid staged release metadata.");
        return File.ReadAllBytes(path);
    }
    private static Version InstalledVersion()
    {
        UpdatePaths.RejectLinks(UpdatePaths.InstallDirectory);
        var version = FileVersionInfo.GetVersionInfo(UpdatePaths.InstalledExe);
        return new(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart);
    }
    private static void RequireNoOtherSora(int allowedPid)
    {
        foreach (var process in Process.GetProcessesByName("Sora"))
            using (process) if (process.Id != allowedPid) throw new IOException("Exit other Sora copies and sessions before updating.");
    }
    private static void Result(string stage, string status, string message) => File.WriteAllText(Path.Combine(stage, "result.json"),
        JsonSerializer.Serialize(new { status, message, at = DateTimeOffset.UtcNow }, UpdatePolicy.Json));
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
