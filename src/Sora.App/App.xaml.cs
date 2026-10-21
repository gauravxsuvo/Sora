using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Sora.Core;
using Sora.Update.Windows;

namespace Sora.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--traffic-light-check"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.IndexOf(e.Args, "--output");
            if (index < 0 || index + 1 >= e.Args.Length) { Shutdown(2); return; }
            Shutdown(TrafficLightChecks.Run(e.Args[index + 1]) ? 0 : 1); return;
        }
        if (e.Args.Contains("--tray-menu-check"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.IndexOf(e.Args, "--output");
            if (index < 0 || index + 1 >= e.Args.Length) { Shutdown(2); return; }
            try { Shutdown(await TrayMenuChecks.RunAsync(e.Args[index + 1]) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(e.Args[index + 1] + ".error.json", JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--app-audit-check"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.IndexOf(e.Args, "--output");
            if (index < 0 || index + 1 >= e.Args.Length) { Shutdown(2); return; }
            Shutdown(await AppAuditChecks.RunAsync(e.Args[index + 1]) ? 0 : 1); return;
        }
        if (e.Args.Contains("--application-update-ui-check"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.IndexOf(e.Args, "--output");
            if (index < 0 || index + 1 >= e.Args.Length) { Shutdown(2); return; }
            try { Shutdown(await ApplicationUpdateUiCheck.RunAsync(e.Args[index + 1]) ? 0 : 1); }
            catch (Exception error)
            {
                var output = e.Args[index + 1];
                if (!File.Exists(output)) File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--maintenance-ui-check"))
        {
            var outputIndex = Array.IndexOf(e.Args, "--output");
            if (outputIndex < 0 || outputIndex + 1 >= e.Args.Length) { Shutdown(2); return; }
            try { Shutdown(HardwareMaintenanceUiCheck.Run(e.Args[outputIndex + 1]) ? 0 : 1); }
            catch (Exception) { Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--fan-maintenance-capture") || e.Args.Contains("--fan-maintenance-restore"))
        {
            var outputIndex = Array.IndexOf(e.Args, "--output");
            var baselineIndex = Array.IndexOf(e.Args, "--baseline");
            if (outputIndex < 0 || outputIndex + 1 >= e.Args.Length ||
                (e.Args.Contains("--fan-maintenance-restore") && (baselineIndex < 0 || baselineIndex + 1 >= e.Args.Length)))
            { Shutdown(2); return; }
            var baseline = e.Args.Contains("--fan-maintenance-restore") ? e.Args[baselineIndex + 1] : null;
            Shutdown(await Task.Run(() => FanMaintenanceCheck.Run(e.Args[outputIndex + 1], baseline)) ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--logitech-diagnose"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "logitech-readonly.json");
            Shutdown(await LogitechReadOnlyProbe.RunAsync(output) ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--logitech-runtime-check"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "logitech-runtime-check.json");
            try { Shutdown(await LogitechRuntimeCheck.RunAsync(output) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--logitech-physical-check"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "logitech-physical-check.json");
            Shutdown(await LogitechPhysicalCheck.RunAsync(output) ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--lighting-editor-check"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "lighting-editor-check.json");
            Shutdown(LightingPanel.RunEditorChecks(output) ? 0 : 1); return;
        }
        if (e.Args.Contains("--gpu-fan-lifecycle-check"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "gpu-fan-lifecycle-check.json");
            Shutdown(await GpuFanLifecycleCheck.RunAsync(output) ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--hardware-tools-probe"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "hardware-tools-probe.json");
            await HardwareToolsProbe.RunAsync(output);
            Shutdown();
            return;
        }
        base.OnStartup(e);
        if (e.Args.Contains("--fan-service-check"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "fan-service-check.json");
            Shutdown(await FanCheck.ServiceCheckAsync(output) ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--fan-probe"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "fan-probe.json");
            Shutdown(await Task.Run(() => FanCheck.Probe(output)) ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--keyboard-verify-input"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "keyboard-input-check.json");
            Shutdown(await KeyboardInputCheck.RunAsync(output, wireless: e.Args.Contains("--receiver")) ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--receiver-diagnose"))
        {
            // Read-only: the 2.4 GHz receiver's lighting and battery reads, three times each, with every raw report.
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "receiver-diagnostics.json");
            try
            {
                var log = new List<object>();
                var descriptors = KreoKeyboard.Descriptors();
                if (KreoReceiver.Find() is { } found)
                {
                    using var receiver = new KreoReceiver(found);
                    for (var attempt = 0; attempt < 3; attempt++)
                        foreach (var (command, length) in new[] { (KreoReceiver.ReadPower, 2), (KreoReceiver.ReadLighting, 128) })
                        {
                            var started = DateTimeOffset.Now;
                            var reply = await receiver.ReadAsync(command, 0, length, TimeSpan.FromSeconds(4));
                            log.Add(new { attempt, command = $"0x{command:X2}", ms = (DateTimeOffset.Now - started).TotalMilliseconds, reply.Complete, reply.Error, reply.Packets, reply.PacketsReceived, reply.BadChecksums, data = Convert.ToHexString(reply.Data), raw = reply.RawReports });
                        }
                }
                File.WriteAllText(output, JsonSerializer.Serialize(new { descriptors, receiver = KreoReceiver.Find(), conflict = KreoKeyboard.FindConflict(), log }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--keyboard-diagnose"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "keyboard-diagnostics.json");
            try
            {
                var probes = await Task.Run(KreoKeyboard.Probe);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output, JsonSerializer.Serialize(new { version = typeof(App).Assembly.GetName().Version?.ToString(), descriptors = KreoKeyboard.Descriptors(), probes, configurationWrites = 0 }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(probes.Count == 1 && probes[0].Error == null ? 0 : 1);
            }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--lighting-probe"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "lighting-probe.json");
            try { Shutdown(await Task.Run(() => LightingCheck.Probe(output)) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--board-test"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "board-test.json");
            try { Shutdown(await Task.Run(() => LightingCheck.BoardTest(output)) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--board-rainbow-test"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "board-rainbow-test.json");
            try { Shutdown(await Task.Run(() => LightingCheck.BoardRainbowTest(output)) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--board-builtin-default"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "board-builtin-default.json");
            try { Shutdown(await Task.Run(() => LightingCheck.BoardBuiltinDefault(output)) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--board-default-rainbow"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "board-default-rainbow.json");
            try { Shutdown(await Task.Run(() => LightingCheck.BoardDefaultRainbow(output)) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--updates-check"))
        {
            // Read-only: downloads Gigabyte's public index files and lists matches; nothing is downloaded beyond them or installed.
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "updates-check.json");
            try
            {
                using var http = UpdatesPanel.CreateHttp();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var report = await GigabyteUpdates.CheckAsync(http, timeout.Token);
                File.WriteAllText(output, JsonSerializer.Serialize(new { report, devices = GigabyteUpdates.Devices().Count }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--lighting-service-check"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "lighting-service-check.json");
            try { Shutdown(await LightingCheck.ServiceCheck(output, 8) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--lighting-test"))
        {
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "lighting-test.json");
            try { Shutdown(await Task.Run(() => LightingCheck.Test(output, 10)) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--diagnose"))
        {
            var outputIndex = Array.IndexOf(e.Args, "--output");
            var output = outputIndex >= 0 && outputIndex + 1 < e.Args.Length ? e.Args[outputIndex + 1] : Path.Combine(AppContext.BaseDirectory, "diagnostics.json");
            var samplesIndex = Array.IndexOf(e.Args, "--samples");
            var samples = samplesIndex >= 0 && samplesIndex + 1 < e.Args.Length && int.TryParse(e.Args[samplesIndex + 1], out var count) ? Math.Clamp(count, 1, 30) : 1;
            try { Shutdown(await DiagnoseAsync(output, samples) ? 0 : 1); }
            catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = error.ToString() })); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--startup-task-inspect"))
        {
            var outputIndex = Array.IndexOf(e.Args, "--output");
            if (outputIndex < 0 || outputIndex + 1 >= e.Args.Length) { Shutdown(1); return; }
            try
            {
                var inspection = StartupTask.InspectForLaunch();
                // CreateNew protects prior evidence. This branch never starts the hardware host.
                using var output = new FileStream(e.Args[outputIndex + 1], FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(output, new { executable = Environment.ProcessPath, target = StartupTask.TargetPath, inspection }, new JsonSerializerOptions { WriteIndented = true });
                output.Flush(true);
                Shutdown(inspection.Readable ? 0 : 1);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { Console.Error.WriteLine(error.Message); Shutdown(1); }
            return;
        }
        if (Array.IndexOf(e.Args, "--startup-task") is var taskIndex and >= 0)
        {
            // Administrative switch: --startup-task on|off.
            try { if (StartupRouting.ParseExplicitAction(taskIndex + 1 < e.Args.Length ? e.Args[taskIndex + 1] : null)) StartupTask.Enable(); else StartupTask.Disable(); Shutdown(0); }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or System.ComponentModel.Win32Exception) { Console.Error.WriteLine(error.Message); Shutdown(1); }
            return;
        }
        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        var screenshot = screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length ? e.Args[screenshotIndex + 1] : null;
        var measureIndex = Array.IndexOf(e.Args, "--measure");
        measure = measureIndex >= 0 && measureIndex + 1 < e.Args.Length ? e.Args[measureIndex + 1] : null;
        var startup = e.Args.Contains(StartupTask.Argument);
        IDisposable? launchGate;
        try { launchGate = UpdatePaths.EnterGate(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!startup) MessageBox.Show(error.Message, "Sora update");
            Shutdown(); return;
        }
        using var ownedLaunchGate = launchGate;
        page = e.Args.Contains("--keyboard") ? "keyboard" : e.Args.Contains("--lighting") ? "lighting" : e.Args.Contains("--fans") ? "fans" : e.Args.Contains("--gpu") ? "gpu" : e.Args.Contains("--ssd") ? "ssd" : e.Args.Contains("--wifi") ? "wifi" : e.Args.Contains("--updates") ? "updates" : e.Args.Contains("--logitech") ? "logitech" : e.Args.Contains("--settings") ? "settings" : "display";
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (screenshot == null && measure == null)
        {
            instance = new Mutex(true, @"Local\Sora.LD360.Desktop" + InstanceSuffix, out var created);
            if (!created)
            {
                // A wake-triggered launch must stay invisible when the tray instance is already running.
                if (startup) { instance.Dispose(); instance = null; Shutdown(); return; }
                // Ask the running Sora to show its window; versions before 0.4 do not answer.
                using var open = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEventName);
                using var opened = new EventWaitHandle(false, EventResetMode.AutoReset, OpenedEventName);
                open.Set();
                if (!startup && !opened.WaitOne(3000)) MessageBox.Show("An older Sora is already running. Exit it from its tray menu (Stop display and exit), then open Sora again.", "Sora");
                instance.Dispose(); instance = null;
                Shutdown();
                return;
            }
            openRequests = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEventName);
            openedReply = new EventWaitHandle(false, EventResetMode.AutoReset, OpenedEventName);
            ThreadPool.RegisterWaitForSingleObject(openRequests, (_, _) => { openedReply.Set(); Dispatcher.InvokeAsync(() => ShowWindow()); }, null, -1, false);
        }
        if (screenshot != null && e.Args.Contains("--components"))
            SoraData.UseIsolatedPreviewDirectory(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshot))!, "component-preview-data", Guid.NewGuid().ToString("N")));
        else if (screenshot != null && e.Args.Contains("--isolated-ui-check"))
            SoraData.UseIsolatedPreviewDirectory(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshot))!, "screen-check-data", Guid.NewGuid().ToString("N")), developmentGallery: false);
        host = new SoraHost();
        if (screenshot != null && SoraData.IsIsolated && !File.Exists(Path.Combine(host.DataDirectory, "settings.json")))
        {
            Directory.CreateDirectory(host.DataDirectory);
            File.WriteAllText(Path.Combine(host.DataDirectory, "settings.json"), JsonSerializer.Serialize(host.Settings));
        }
        var fanPreviewIndex = Array.IndexOf(e.Args, "--fan-preview");
        if (screenshot != null && fanPreviewIndex >= 0 && fanPreviewIndex + 1 < e.Args.Length)
            host.Fans.SetPreview(JsonSerializer.Deserialize<FanSnapshot>(File.ReadAllText(e.Args[fanPreviewIndex + 1])) ?? throw new InvalidDataException("Missing fan preview snapshot."));
        // A tray app that drives hardware should log and keep running rather than vanish.
        DispatcherUnhandledException += (_, error) => { host.Log("Unhandled: " + error.Exception); error.Handled = true; };
        host.Start(applyHardware: screenshot == null && measure == null);
        if (measure == null && (screenshot == null || SoraData.IsIsolated))
        {
            ApplicationUpdates = new ApplicationUpdateService();
            ApplicationUpdates.UpdateAvailable += () => tray?.ShowNotification("Sora update available", "Sora " + ApplicationUpdates.Release?.Version + " is ready to download. Open Updates from Sora's tray menu.");
            if (screenshot == null && !SoraData.IsIsolated) _ = ApplicationUpdates.RunScheduledChecksAsync();
        }
        if (screenshot == null)
        {
            ThemeManager.Apply(host.Settings.DarkMode);
            tray = new NativeTray("Sora", TrayMenu);
            tray.Activated += () => ShowWindow();
            host.Updated += UpdateTray;
            if (measure == null) host.Log("Startup task: " + StartupTask.InspectForLaunch().Message);
        }
        if (measure != null) _ = MemoryReport.RunAsync(this, measure);
        if (screenshot != null)
        {
            ShowWindow(screenshot);
            // --size WxH renders the screenshot at a specific window size.
            var sizeIndex = Array.IndexOf(e.Args, "--size");
            if (sizeIndex >= 0 && sizeIndex + 1 < e.Args.Length && e.Args[sizeIndex + 1].Split('x') is [var w, var h] && double.TryParse(w, out var width) && double.TryParse(h, out var height)) { window!.Width = width; window.Height = height; }
        }
        else if (!startup && !e.Args.Contains("--tray")) ShowWindow();
        else _ = TrimAsync(15000);
        var updateIndex = Array.IndexOf(e.Args, "--update-complete");
        if (updateIndex >= 0 && updateIndex + 1 < e.Args.Length && string.Equals(Environment.ProcessPath, UpdatePaths.InstalledExe, StringComparison.OrdinalIgnoreCase))
        {
            try { File.WriteAllText(Path.Combine(UpdatePaths.Stage(e.Args[updateIndex + 1]), "healthy.txt"), Environment.ProcessId.ToString()); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { host.Log("Update startup acknowledgement: " + error.Message); }
        }
    }

    // SORA_INSTANCE lets a test copy run beside an installed Sora without sharing its single-instance names.
    private static readonly string InstanceSuffix = Environment.GetEnvironmentVariable("SORA_INSTANCE") is { Length: > 0 } suffix ? "." + suffix : "";
    private static readonly string OpenEventName = @"Local\Sora.Open" + InstanceSuffix, OpenedEventName = @"Local\Sora.Opened" + InstanceSuffix;
    private SoraHost? host;
    private NativeTray? tray;
    private MainWindow? window;
    private EventWaitHandle? openRequests, openedReply;
    private string page = "display";
    private string? measure;
    private bool exiting;

    internal bool WindowOpen => window != null;
    internal ApplicationUpdateService? ApplicationUpdates { get; private set; }
    private bool updatePrepared;
    internal bool CanPrepareUpdate => !exiting && !updatePrepared && host != null && (window == null || !window.KeepAliveWhenHidden);
    internal async Task<bool> PrepareUpdateAsync()
    {
        if (!CanPrepareUpdate || host == null) return false;
        updatePrepared = true;
        if (window != null) window.IsEnabled = false;
        await host.StopAsync();
        if (host.CleanupFailed)
        {
            return false;
        }
        return true;
    }
    internal void CancelPreparedUpdate(string reason)
    {
        if (!updatePrepared) return;
        AppDialog.ShowMessage(window, "Sora update stopped", reason + "\n\nSora's controls have been stopped. Sora will close; reopen it from its shortcut. Details are in sora.log.");
        exiting = true; ApplicationUpdates?.Dispose(); tray?.Dispose(); Shutdown();
    }
    internal void FinishUpdateExit()
    {
        if (!updatePrepared) throw new InvalidOperationException("Update cleanup has not been prepared.");
        exiting = true;
        ApplicationUpdates?.Dispose(); tray?.Dispose();
        Shutdown();
    }

    internal void ShowWindow(string? screenshot = null)
    {
        if (exiting || updatePrepared || host == null) return;
        if (window == null)
        {
            window = new MainWindow(host, screenshot, page);
            if (measure != null) { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -12000; window.ShowInTaskbar = false; }
            window.Closed += (_, _) => { window = null; _ = TrimAsync(1500); };
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    // Closing to the tray destroys the window; it is rebuilt on demand. Unsaved keyboard edits keep it alive but hidden.
    internal void CloseWindowToTray()
    {
        if (window == null) return;
        if (window.KeepAliveWhenHidden) window.Hide();
        else window.CloseForTray();
    }

    private IReadOnlyList<NativeTray.MenuItem?> TrayMenu() =>
    [
        new("Open Sora", () => ShowWindow(), Icon: "IconMonitor"),
        new(ApplicationUpdates?.Available == true ? "Update available…" : "Check for updates", () => { page = "updates"; ShowWindow(); window?.OpenUpdates(); }, !updatePrepared, "IconDownload"),
        host!.Hardware.Available(HardwareModule.Cooler) || host.Requested
            ? host.Requested ? new("Stop cooler display", () => _ = host.SetRequestedAsync(false), !updatePrepared, "IconMonitor") : new("Start cooler display", () => _ = host.SetRequestedAsync(true), !updatePrepared, "IconMonitor")
            : null,
        null,
        new("Settings", () => { page = "settings"; ShowWindow(); window?.OpenSettings(); }, Icon: "IconSettings"),
        new("Exit Sora", () => _ = ExitAsync(), Icon: "IconClose"),
    ];

    private void UpdateTray()
    {
        if (tray == null || host == null) return;
        tray.SetTooltip(!host.Hardware.Available(HardwareModule.Cooler) && !host.Requested ? "Sora · hardware controls" : host.Status.Sending && host.Fresh is { } sample ? $"Sora · {host.FormatTemperature(sample, "0")}{host.Unit} on LD360" : host.Requested ? "Sora · waiting for the LD360" : "Sora · display off");
    }

    // Give the released window's memory back to Windows.
    private static async Task TrimAsync(int delayMs)
    {
        await Task.Delay(delayMs);
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        EmptyWorkingSet(process.Handle);
    }

    internal async Task ExitAsync()
    {
        if (exiting || ApplicationUpdates?.Installing == true) return;
        exiting = true;
        ApplicationUpdates?.Dispose();
        var failures = await CleanupSequence.RunAsync(
            ("Window", () => window?.ShutdownAsync() ?? Task.CompletedTask),
            ("Hardware", () => host?.StopAsync() ?? Task.CompletedTask),
            ("Tray", () => { tray?.Dispose(); return Task.CompletedTask; }));
        foreach (var failure in failures) host?.Log(failure.Part + " application exit: " + failure.Error);
        Shutdown();
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);

    // Windows is shutting down or signing out: Sora gets no normal exit, so a board that keeps its lighting gets its saved look now.
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e) { host?.Lighting.SettleBoardForPowerOff(); base.OnSessionEnding(e); }
    protected override void OnExit(ExitEventArgs e) { tray?.Dispose(); openRequests?.Dispose(); openedReply?.Dispose(); instance?.Dispose(); base.OnExit(e); }

    private static async Task<bool> DiagnoseAsync(string output, int requestedSamples)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15 + requestedSamples));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readings = new List<SensorSample>();
        var client = new SensorClient();
        var lastStatus = "Connecting";
        client.StatusChanged += value => lastStatus = value.Message + (value.Detail == null ? "" : " · " + value.Detail);
        client.SampleReceived += sample => { readings.Add(sample); if (readings.Count >= requestedSamples) completion.TrySetResult(); };
        var devices = HidDevice.Enumerate();
        string? outputHandleError = null;
        try { await using var handle = HidDevice.Open(); }
        catch (Exception error) { outputHandleError = error.Message; }
        var sensorTask = client.RunAsync(timeout.Token);
        string? sensorError = null;
        try { await completion.Task.WaitAsync(timeout.Token); }
        catch (OperationCanceledException) { sensorError = lastStatus; }
        timeout.Cancel();
        await sensorTask;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { version = typeof(App).Assembly.GetName().Version?.ToString(), sensorSource = SensorClient.SourceName, cpuName = client.CpuName, devices, outputHandleReady = outputHandleError == null, outputHandleError, reading = readings.LastOrDefault(), readings, rawCpuSensors = client.LastSensors, sensorError, conflict = DisplayController.FindConflict(), hardwareWrites = 0 }, new JsonSerializerOptions { WriteIndented = true }));
        return sensorError == null && readings.Count >= requestedSamples;
    }
}
