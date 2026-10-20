using System;
using System.Windows;
using System.Windows.Controls;

namespace Sora.App;

public partial class ApplicationUpdateCard : UserControl
{
    private ApplicationUpdateService? service;
    private ApplicationUpdateService? suppliedService;
    private bool rendering;
    public ApplicationUpdateCard()
    {
        InitializeComponent();
        Loaded += (_, _) => Bind(suppliedService ?? (Application.Current as App)?.ApplicationUpdates);
        Unloaded += (_, _) => Bind(null);
    }
    internal void AttachForCheck(ApplicationUpdateService value) { suppliedService = value; Bind(value); }
    private void Bind(ApplicationUpdateService? value)
    {
        if (service != null) service.Changed -= Render;
        service = value;
        if (service != null) service.Changed += Render;
        Render();
    }
    private void Render()
    {
        rendering = true;
        try
        {
        if (service == null) { VersionText.Text = "Sora application updates"; AppStatus.Text = "Update checks are unavailable in this preview."; AppCheck.IsEnabled = AutomaticChecks.IsEnabled = ChannelPicker.IsEnabled = false; return; }
        ChannelPicker.SelectedIndex = service.Policy.Channel == "beta" ? 1 : 0;
        ChannelPicker.IsEnabled = !service.Busy && !service.Installing;
        ChannelHelp.Text = service.Policy.Channel == "beta" ? "Beta releases include experimental features. Updates are signed and install only when you choose Install and restart." : "Stable releases only. Switching from Beta waits for a newer stable release; it does not downgrade Sora.";
        VersionText.Text = "Installed " + service.InstalledVersion + (service.Available ? "  →  " + service.Release!.Version : "");
        AppStatus.Text = service.Status;
        LastCheckText.Text = service.State.LastChecked is { } at ? "Last successful check: " + at.ToLocalTime().ToString("g") : "No successful check yet.";
        ReleaseNotes.Text = service.Release is { } release && service.Available ? release.Notes + "\n\n" + (release.Size / 1048576d).ToString("0.0") + " MB · Windows x64" : "";
        ReleaseNotes.Visibility = ReleaseNotes.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AppCheck.IsEnabled = service.Policy.Configured && service.CanCheck && !service.Busy;
        AppDownload.Visibility = service.Available && !service.Downloaded ? Visibility.Visible : Visibility.Collapsed;
        AppDownload.IsEnabled = !service.Busy;
        AppInstall.Visibility = service.Downloaded ? Visibility.Visible : Visibility.Collapsed;
        AppInstall.IsEnabled = !service.Busy;
        AppSkip.Visibility = service.Available && !service.Busy ? Visibility.Visible : Visibility.Collapsed;
        AppCancel.Visibility = service.Busy && !service.Downloaded ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Visibility = service.Busy && service.Progress > 0 && !service.Downloaded ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Value = service.Progress;
        AutomaticChecks.IsChecked = service.State.AutomaticChecks;
        AutomaticChecks.IsEnabled = service.Policy.Configured && service.CanCheck && !service.Busy;
        }
        finally { rendering = false; }
    }
    private void ChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rendering || service == null || ChannelPicker.SelectedItem is not ComboBoxItem item || item.Tag is not string channel) return;
        try { service.SetChannel(channel); }
        catch (Exception error) { AppDialog.ShowMessage(Window.GetWindow(this), "Could not change update channel", error.Message); Render(); }
    }
    private async void CheckClick(object sender, RoutedEventArgs e) { if (service != null) await service.CheckAsync(); }
    private async void DownloadClick(object sender, RoutedEventArgs e) { if (service != null) await service.DownloadAsync(); }
    private async void InstallClick(object sender, RoutedEventArgs e) { if (service != null && Application.Current is App app) await service.InstallAsync(app); }
    private void CancelClick(object sender, RoutedEventArgs e) => service?.Cancel();
    private void SkipClick(object sender, RoutedEventArgs e) { try { service?.SkipVersion(); } catch (Exception error) { AppDialog.ShowMessage(Window.GetWindow(this), "Could not save updater settings", error.Message); } }
    private void AutomaticClick(object sender, RoutedEventArgs e) { try { service?.SetAutomatic(AutomaticChecks.IsChecked == true); } catch (Exception error) { AppDialog.ShowMessage(Window.GetWindow(this), "Could not save updater settings", error.Message); Render(); } }
}
