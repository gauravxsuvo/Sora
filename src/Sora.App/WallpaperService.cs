using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sora.App;

internal sealed class WallpaperService : IDisposable
{
    private readonly SoraHost host;
    private readonly string settingsFile;
    private readonly DesktopWallpaper desktop = new();
    private readonly DispatcherTimer desktopCheck = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly CancellationTokenSource lifetime = new();
    private MediaPlayer? player;
    private bool? desktopVisible;
    private bool disposed;

    internal string? PathToVideo { get; private set; }
    internal bool Busy { get; private set; }
    internal string Status { get; private set; } = "Choose a video to start your animated wallpaper.";
    internal event Action? Changed;

    internal WallpaperService(SoraHost host)
    {
        this.host = host;
        settingsFile = Path.Combine(host.DataDirectory, "wallpaper.json");
        desktopCheck.Tick += (_, _) => { if (!Busy) ShowOnDesktop(); };
    }

    internal async Task RestoreAsync()
    {
        try
        {
            if (File.Exists(settingsFile))
                PathToVideo = JsonSerializer.Deserialize<string>(File.ReadAllText(settingsFile));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            ReportError("Could not load the saved wallpaper", error);
        }
        if (PathToVideo != null) await OpenAsync(PathToVideo, save: false);
    }

    internal Task ApplyAsync(string path) => OpenAsync(path, save: true);

    private async Task OpenAsync(string path, bool save)
    {
        if (Busy || disposed) return;
        Busy = true;
        SetStatus("Opening video…");
        MediaPlayer? candidate = null;
        var token = lifetime.Token;
        try
        {
            if (!Path.IsPathFullyQualified(path) || new Uri(path, UriKind.Absolute) is not { IsFile: true, IsUnc: false } source ||
                Path.GetExtension(path).ToLowerInvariant() is not (".mp4" or ".wmv"))
                throw new IOException("Choose a local MP4 or WMV video.");
            if (!File.Exists(path)) throw new FileNotFoundException("The video could not be found. Choose its new location.");

            candidate = new MediaPlayer { IsMuted = true };
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler ready = (_, _) => opened.TrySetResult();
            EventHandler<System.Windows.Media.ExceptionEventArgs> failed = (_, e) => opened.TrySetException(e.ErrorException);
            candidate.MediaOpened += ready;
            candidate.MediaFailed += failed;
            try
            {
                candidate.Open(source);
                candidate.Play();
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            }
            finally
            {
                candidate.MediaOpened -= ready;
                candidate.MediaFailed -= failed;
            }
            token.ThrowIfCancellationRequested();
            if (!candidate.HasVideo) throw new IOException("The selected file has no playable video track.");
            if (save)
            {
                Directory.CreateDirectory(host.DataDirectory);
                File.WriteAllText(settingsFile + ".tmp", JsonSerializer.Serialize(path));
                File.Move(settingsFile + ".tmp", settingsFile, overwrite: true);
            }

            StopPlayback();
            PathToVideo = path;
            player = candidate;
            candidate = null;
            player.MediaEnded += Loop;
            player.MediaFailed += PlaybackFailed;
            player.Pause();
            player.Position = TimeSpan.Zero;
            ShowOnDesktop();
            desktopCheck.Start();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            ReportError(save ? "Could not apply the wallpaper" : "Could not start the saved wallpaper", error);
        }
        finally
        {
            candidate?.Close();
            Busy = false;
            if (!disposed) Changed?.Invoke();
        }
    }

    internal void Disable()
    {
        if (Busy || disposed) return;
        try
        {
            File.Delete(settingsFile);
            StopPlayback();
            PathToVideo = null;
            SetStatus("Animated wallpaper is off.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ReportError("Could not turn off the wallpaper", error);
        }
    }

    private void ShowOnDesktop()
    {
        if (player == null) return;
        bool visible;
        try
        {
            visible = desktop.Show(player);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or COMException)
        {
            desktop.Dispose();
            if (desktopVisible != false) host.Log("Wallpaper desktop: " + error.Message);
            visible = false;
        }
        if (desktopVisible == visible) return;
        desktopVisible = visible;
        if (visible) player.Play();
        else player.Pause();
        SetStatus(visible ? "Playing on the desktop." : "Waiting for the Windows desktop…");
    }

    private void Loop(object? sender, EventArgs e)
    {
        if (sender != player || player == null) return;
        player.Position = TimeSpan.Zero;
        if (desktopVisible == true) player.Play();
    }

    private void PlaybackFailed(object? sender, System.Windows.Media.ExceptionEventArgs e)
    {
        if (sender != player) return;
        StopPlayback();
        ReportError("Wallpaper playback stopped", e.ErrorException);
    }

    private void SetStatus(string message)
    {
        if (Status == message) return;
        Status = message;
        Changed?.Invoke();
    }

    private void ReportError(string message, Exception error)
    {
        host.Log(message + ": " + error.Message);
        SetStatus(message + ". " + error.Message);
    }

    private void StopPlayback()
    {
        desktopCheck.Stop();
        desktop.Dispose();
        desktopVisible = null;
        if (player == null) return;
        player.MediaEnded -= Loop;
        player.MediaFailed -= PlaybackFailed;
        player.Close();
        player = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        StopPlayback();
    }
}
