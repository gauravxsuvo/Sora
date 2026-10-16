using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Sora.Core;

namespace Sora.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--keyboard-verify-input"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.IndexOf(e.Args, "--output");
            var output = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "keyboard-input-check.json");
            Shutdown(await KeyboardInputCheck.RunAsync(output) ? 0 : 1);
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
                File.WriteAllText(output, JsonSerializer.Serialize(new { version = "0.4", descriptors = KreoKeyboard.Descriptors(), probes, configurationWrites = 0 }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(probes.Count == 1 && probes[0].Error == null ? 0 : 1);
            }
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
        if (Array.IndexOf(e.Args, "--startup-task") is var taskIndex and >= 0)
        {
            // Administrative switch: --startup-task on|off.
            try { if (taskIndex + 1 < e.Args.Length && e.Args[taskIndex + 1] == "off") StartupTask.Disable(); else StartupTask.Enable(); Shutdown(0); }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { Console.Error.WriteLine(error.Message); Shutdown(1); }
            return;
        }
        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        var screenshot = screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length ? e.Args[screenshotIndex + 1] : null;
        var measureIndex = Array.IndexOf(e.Args, "--measure");
        measure = measureIndex >= 0 && measureIndex + 1 < e.Args.Length ? e.Args[measureIndex + 1] : null;
        var startup = e.Args.Contains(StartupTask.Argument);
        page = e.Args.Contains("--keyboard") ? "keyboard" : "display";
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (screenshot == null && measure == null)
        {
            instance = new Mutex(true, @"Local\Sora.LD360.Desktop" + InstanceSuffix, out var created);
            if (!created)
            {
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
        host = new SoraHost();
        // A tray app that drives hardware should log and keep running rather than vanish.
        DispatcherUnhandledException += (_, error) => { host.Log("Unhandled: " + error.Exception); error.Handled = true; };
        host.Start();
        if (screenshot == null)
        {
            tray = new NativeTray("Sora", TrayMenu);
            tray.Activated += () => ShowWindow();
            host.Updated += UpdateTray;
            if (measure == null) try { StartupTask.RepointIfMoved(); } catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { host.Log("Startup task: " + error.Message); }
        }
        if (measure != null) _ = MemoryReport.RunAsync(this, measure);
        if (screenshot != null)
        {
            ShowWindow(screenshot);
            // --size WxH renders the screenshot at a specific window size.
            var sizeIndex = Array.IndexOf(e.Args, "--size");
            if (sizeIndex >= 0 && sizeIndex + 1 < e.Args.Length && e.Args[sizeIndex + 1].Split('x') is [var w, var h] && double.TryParse(w, out var width) && double.TryParse(h, out var height)) { window!.Width = width; window.Height = height; }
        }
        else if (!startup) ShowWindow();
        else _ = TrimAsync(15000);
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

    internal void ShowWindow(string? screenshot = null)
    {
        if (exiting || host == null) return;
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
        new("Open Sora", () => ShowWindow()),
        host!.Requested ? new("Stop display", () => _ = host.SetRequestedAsync(false)) : new("Start display", () => _ = host.SetRequestedAsync(true)),
        null,
        new("Exit Sora", () => _ = ExitAsync()),
    ];

    private void UpdateTray()
    {
        if (tray == null || host == null) return;
        tray.SetTooltip(host.Status.Sending && host.Fresh is { } sample ? $"Sora · {host.FormatTemperature(sample, "0")}{host.Unit} on LD360" : host.Requested ? "Sora · waiting for the LD360" : "Sora · display off");
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
        if (exiting) return;
        exiting = true;
        if (window != null) await window.ShutdownAsync();
        if (host != null) await host.StopAsync();
        tray?.Dispose();
        Shutdown();
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);

    protected override void OnExit(ExitEventArgs e) { tray?.Dispose(); openRequests?.Dispose(); openedReply?.Dispose(); instance?.Dispose(); base.OnExit(e); }

    private static async Task<bool> DiagnoseAsync(string output, int requestedSamples)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15 + requestedSamples));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readings = new List<SensorSample>();
        var client = new SensorClient();
        var lastStatus = "Connecting";
        client.StatusChanged += value => lastStatus = value;
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
        File.WriteAllText(output, JsonSerializer.Serialize(new { version = "0.4", sensorSource = SensorClient.SourceName, cpuName = client.CpuName, devices, outputHandleReady = outputHandleError == null, outputHandleError, reading = readings.LastOrDefault(), readings, rawCpuSensors = client.LastSensors, sensorError, conflict = DisplayController.FindConflict(), hardwareWrites = 0 }, new JsonSerializerOptions { WriteIndented = true }));
        return sensorError == null && readings.Count >= requestedSamples;
    }
}
