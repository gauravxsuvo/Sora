using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core.Updates;
using Sora.Update.Windows;

namespace Sora.App;

internal sealed class ApplicationUpdateService : IDisposable
{
    private readonly System.Net.Http.HttpClient http;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private readonly UpdateChannelSettings channelSettings;
    private readonly Func<string, bool, string> stagePath;
    private string StatePath => channelSettings.StatePath(Policy.Channel);
    private byte[]? envelope;
    private string? transaction;
    private bool disposed;
    public UpdatePolicy Policy { get; private set; }
    public UpdateState State { get; private set; } = new();
    public SoraRelease? Release { get; private set; }
    public string InstalledVersion { get; }
    public string Status { get; private set; } = "Check for a new Sora release.";
    public double Progress { get; private set; }
    public bool Busy { get; private set; }
    public bool Installing { get; private set; }
    public bool CanCheck { get; private set; } = true;
    public bool Downloaded => transaction != null;
    public bool Available => Release != null && ReleaseVersion.Parse(Release.Version).CompareTo(ReleaseVersion.Parse(InstalledVersion)) > 0;
    public event Action? Changed;
    public event Action? UpdateAvailable;

    public ApplicationUpdateService(string? installedVersion = null, string? dataDirectory = null,
        System.Net.Http.HttpClient? httpClient = null, UpdatePolicy? updatePolicy = null, Func<string, bool, string>? stageDirectory = null)
    {
        InstalledVersion = installedVersion ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        channelSettings = new(dataDirectory ?? SoraData.DirectoryPath);
        http = httpClient ?? UpdatesPanel.CreateHttp();
        Policy = updatePolicy ?? BundledUpdatePolicy.Load();
        stagePath = stageDirectory ?? ((id, create) => UpdatePaths.Stage(id, create));
        try
        {
            Policy = Policy.ForChannel(channelSettings.ReadChannel(InstalledVersion));
            State = channelSettings.Load(Policy.Channel);
            Status = "Check for new Sora " + Policy.Channel + " releases.";
        }
        catch (Exception e) when (e is not OutOfMemoryException) { CanCheck = false; State = new(AutomaticChecks: false); Status = "Updater settings could not be read; checks are disabled until the file is repaired: " + e.Message; }
        if (!Policy.Configured) Status = "Release signing is not configured in this build.";
    }

    public async Task RunScheduledChecksAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30 + Random.Shared.Next(30)), lifetime.Token);
            while (!lifetime.IsCancellationRequested)
            {
                if (CanCheck && Policy.Configured && State.AutomaticChecks && !Busy &&
                    (State.LastChecked == null || DateTimeOffset.UtcNow - State.LastChecked >= TimeSpan.FromHours(24)))
                    await CheckAsync(notify: true);
                await Task.Delay(TimeSpan.FromMinutes(60 + Random.Shared.Next(15)), lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void SetAutomatic(bool enabled) => SaveState(State with { AutomaticChecks = enabled });
    public void SetChannel(string channel)
    {
        if (Busy || Installing || disposed) throw new InvalidOperationException("Wait for the current update operation to finish before changing channels.");
        var policy = Policy.ForChannel(channel);
        if (CanCheck && policy.Channel == Policy.Channel) return;
        var nextState = channelSettings.Load(channel, State.AutomaticChecks);
        channelSettings.SaveChannel(channel); // Persist only after the target state was validated.
        Policy = policy; State = nextState; CanCheck = true;
        Release = null; envelope = null; transaction = null; Progress = 0;
        SetStatus("Using the " + channel + " channel. Check now for a newer release. Switching channels never downgrades an installed version.");
    }
    public void SkipVersion()
    {
        if (Release != null) { SaveState(State with { SkippedVersion = Release.Version }); SetStatus("Sora " + Release.Version + " skipped. You can still download it here."); }
    }
    public void Cancel() => operation?.Cancel();

    public async Task CheckAsync(bool notify = false)
    {
        if (Busy || disposed || !Policy.Configured || !CanCheck) return;
        Busy = true; SetStatus("Checking for Sora " + Policy.Channel + " updates…");
        using var timeout = operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var bytes = await ReleaseFeed.FetchAsync(http, Policy, timeout.Token);
            var release = ReleaseFeed.Verify(bytes, Policy, DateTimeOffset.UtcNow, State.HighestSequence, Environment.OSVersion.Version);
            // Persist replay protection before accepting the feed. Failed persistence is a failed check.
            SaveState(State with { HighestSequence = release.Sequence, LastChecked = DateTimeOffset.UtcNow });
            if (Release?.Sha256 != release.Sha256) transaction = null;
            Release = release; envelope = bytes;
            SetStatus(Available ? "Sora " + release.Version + " is available." : "No newer Sora release is available on the " + Policy.Channel + " channel.");
            if (notify && Available && State.SkippedVersion != release.Version && State.NotifiedVersion != release.Version)
            {
                SaveState(State with { NotifiedVersion = release.Version });
                UpdateAvailable?.Invoke();
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { SetStatus(e is OperationCanceledException ? "Sora update check cancelled or timed out." : "Could not check for Sora updates: " + e.Message); }
        finally { operation = null; Busy = false; Changed?.Invoke(); }
    }

    public async Task DownloadAsync()
    {
        if (Busy || !Available || envelope == null || disposed) return;
        Busy = true; Progress = 0; transaction = null; SetStatus("Downloading Sora…");
        using var timeout = operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromHours(1));
        try
        {
            var release = ReleaseFeed.Verify(envelope, Policy, DateTimeOffset.UtcNow, State.HighestSequence, Environment.OSVersion.Version);
            var id = Guid.NewGuid().ToString("N");
            var stage = stagePath(id, true);
            var free = new DriveInfo(Path.GetPathRoot(stage)!).AvailableFreeSpace;
            if (free < release.Size * 3 + 256L * 1024 * 1024) throw new IOException("Free more disk space before downloading this update.");
            await ReleaseDownload.DownloadAsync(http, Policy, release, Path.Combine(stage, "installer.exe"),
                new Progress<double>(value => { Progress = value; Changed?.Invoke(); }), timeout.Token);
            File.WriteAllBytes(Path.Combine(stage, "release.json"), envelope);
            File.WriteAllText(Path.Combine(stage, "channel.txt"), Policy.Channel);
            File.WriteAllText(Path.Combine(stage, "sequence.txt"), State.HighestSequence.ToString(CultureInfo.InvariantCulture));
            transaction = id;
            SetStatus("Download verified. Ready to install and restart.");
        }
        catch (Exception e) when (e is not OutOfMemoryException) { SetStatus(e is OperationCanceledException ? "Download cancelled or timed out. Choose Download to try again." : "Sora download failed: " + e.Message); }
        finally { operation = null; Busy = false; Changed?.Invoke(); }
    }

    public async Task InstallAsync(App app)
    {
        if (Busy || transaction == null || disposed) return;
        if (!string.Equals(Environment.ProcessPath, UpdatePaths.InstalledExe, StringComparison.OrdinalIgnoreCase))
        { SetStatus("Install this updater-enabled build in Program Files before using in-app installation."); return; }
        if (!app.CanPrepareUpdate) { SetStatus("Finish device operations and apply or discard unsaved device edits before installing."); return; }
        if (!AppDialog.Confirm(System.Windows.Application.Current.MainWindow, "Install Sora update?",
            "Sora will release its hardware controls, close, install the verified update and reopen. Your saved profiles and startup preference will be retained.", "Install and restart")) return;
        Busy = true; Installing = true; SetStatus("Preparing the installer…");
        try
        {
            var id = transaction;
            var stage = stagePath(id, false);
            var release = ReleaseFeed.Verify(File.ReadAllBytes(Path.Combine(stage, "release.json")), Policy, DateTimeOffset.UtcNow, State.HighestSequence, Environment.OSVersion.Version);
            // Recheck promotion/withdrawal before stopping any hardware, including after a long wait to install.
            using var checkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var currentEnvelope = await ReleaseFeed.FetchAsync(http, Policy, checkTimeout.Token);
            var promoted = ReleaseFeed.Verify(currentEnvelope, Policy, DateTimeOffset.UtcNow, State.HighestSequence, Environment.OSVersion.Version);
            SaveState(State with { HighestSequence = promoted.Sequence, LastChecked = DateTimeOffset.UtcNow });
            if (promoted.Version != release.Version || promoted.Sha256 != release.Sha256) throw new IOException("The promoted release has changed. Check for updates and download the current release.");
            File.WriteAllBytes(Path.Combine(stage, "release.json"), currentEnvelope);
            File.WriteAllText(Path.Combine(stage, "channel.txt"), Policy.Channel);
            File.WriteAllText(Path.Combine(stage, "sequence.txt"), promoted.Sequence.ToString(CultureInfo.InvariantCulture));
            ReleaseDownload.VerifyFile(Path.Combine(stage, "installer.exe"), release);
            var source = Path.Combine(UpdatePaths.InstallDirectory, "UpdateRunner"); UpdatePaths.RejectLinks(source);
            foreach (var name in new[] { "Sora.UpdateRunner.exe", "Sora.UpdateRunner.dll", "Sora.UpdateRunner.deps.json", "Sora.UpdateRunner.runtimeconfig.json" })
            {
                var file = Path.Combine(source, name); UpdatePaths.RejectLinks(file);
                var stagedFile = Path.Combine(stage, name); UpdatePaths.RejectLinks(stagedFile);
                File.Copy(file, stagedFile, true);
            }
            var pipeId = Guid.NewGuid().ToString("N");
            using var pipe = new NamedPipeServerStream("Sora.Update." + pipeId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var current = Process.GetCurrentProcess();
            var info = new ProcessStartInfo(Path.Combine(stage, "Sora.UpdateRunner.exe")) { UseShellExecute = false, WorkingDirectory = stage };
            foreach (var arg in new[] { id, current.Id.ToString(CultureInfo.InvariantCulture), current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), pipeId, app.WindowOpen ? "window" : "tray" }) info.ArgumentList.Add(arg);
            using var runner = Process.Start(info) ?? throw new IOException("The update helper could not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            if (await reader.ReadLineAsync(timeout.Token) != "READY") throw new IOException("The update helper did not accept the verified update.");
            SetStatus("Releasing hardware controls…");
            // A failed cleanup never sends INSTALL. The runner times out or observes EOF and leaves Setup untouched.
            if (!await app.PrepareUpdateAsync()) throw new IOException("Hardware cleanup did not complete successfully. Installation stopped. Close Sora and inspect sora.log before trying again.");
            await writer.WriteLineAsync("INSTALL"); await writer.FlushAsync();
            app.FinishUpdateExit();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { SetStatus("Sora installation stopped: " + e.Message); app.CancelPreparedUpdate(e.Message); }
        finally { Busy = false; Installing = false; Changed?.Invoke(); }
    }

    private void SaveState(UpdateState value) { value.Save(StatePath); State = value; Changed?.Invoke(); }
    private void SetStatus(string value) { Status = value; Changed?.Invoke(); }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); http.Dispose(); lifetime.Dispose(); }
}
