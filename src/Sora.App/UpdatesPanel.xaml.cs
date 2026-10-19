using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sora.Core;

namespace Sora.App;

// Updates page: GCC's BIOS and driver check, read-only; downloads go to Downloads\Sora updates and are never run.
public partial class UpdatesPanel : UserControl
{
    private static readonly HttpClient Http = CreateHttp();
    private CancellationTokenSource? operation;
    private bool busy;
    public event Action<string>? StatusChanged;

    public UpdatesPanel()
    {
        InitializeComponent();
        CoverageText.Text = HardwareMaintenance.Coverage;
        foreach (var route in HardwareMaintenance.Routes)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 6) };
            row.Children.Add(new TextBlock { Text = route.Name, FontSize = 15, FontWeight = FontWeights.SemiBold });
            row.Children.Add(Caption(route.Coverage));
            var button = new Button { Content = "Open official support", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            button.Click += (_, _) =>
            {
                try { MaintenanceLinks.Open(route.Url); }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { SetStatus("Cannot open support: " + error.Message); }
            };
            row.Children.Add(button);
            MaintenanceList.Children.Add(row);
        }
        Unloaded += (_, _) => operation?.Cancel();
    }

    public static string DownloadFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Sora updates");

    internal static HttpClient CreateHttp()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Sora/" + (version?.ToString(3) ?? "1.0"));
        return http;
    }

    private void SetStatus(string text, string? sidebar = null) { StatusText.Text = text; StatusChanged?.Invoke(sidebar ?? text); }

    private async void CheckClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        busy = true; CheckButton.IsEnabled = false; ControlState.SetIsBusy(CheckButton, true);
        operation?.Cancel();
        var cancellation = operation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SetStatus("Checking Gigabyte's BIOS and driver lists…", "Checking…");
        try
        {
            var report = await GigabyteUpdates.CheckAsync(Http, cancellation.Token);
            Render(report);
            SetStatus(report.ResultDetail + " Checked " + report.CheckedAt.ToString("t") + ".", report.ResultLabel);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { SetStatus("The check failed: " + error.Message, "Check failed"); }
        finally { busy = false; CheckButton.IsEnabled = true; ControlState.SetIsBusy(CheckButton, false); }
    }

    private void Render(UpdateReport report)
    {
        BoardText.Text = report.Board ?? "Unknown motherboard";
        BoardDetail.Text = "BIOS " + (report.Bios ?? "unknown") + (report.BoardSection != null ? " · Gigabyte list entry " + report.BoardSection : "") + " · checked " + report.CheckedAt.ToString("g");
        UpdateList.Children.Clear(); CurrentList.Children.Clear();
        foreach (var item in report.Updates) UpdateList.Children.Add(Row(item, true));
        foreach (var item in report.UpToDate) CurrentList.Children.Add(Row(item, false));
        EmptyText.Text = report.Updates.Count == 0 ? report.EmptyMessage : "";
        EmptyText.Visibility = report.Updates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CurrentEmpty.Visibility = report.UpToDate.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = report.Updates.Count == 0 ? "" : report.Updates.Count + " found";
        NotesText.Text = string.Join("\n", report.Notes);
        NotesText.Visibility = report.Notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement Row(UpdateItem item, bool available)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = (item.Kind == UpdateKind.Bios ? "BIOS · " : "") + item.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var versions = available ? $"Installed {item.Installed}  →  GCC list {item.Latest}" : $"Installed {item.Installed} · GCC list {item.Latest}";
        text.Children.Add(Caption(versions + (item.Size > 0 ? " · " + Size(item.Size) : ""), "SecondaryTextBrush", 13));
        text.Children.Add(Caption("For: " + item.For));
        if (item.Description.Length > 0 && !string.Equals(item.Description.Trim(), item.Name.Trim(), StringComparison.OrdinalIgnoreCase)) text.Children.Add(Caption(item.Description));
        if (item.Kind == UpdateKind.Bios && available)
            text.Children.Add(Caption("Sora only downloads the BIOS. To install it, unzip it to a USB stick and use Q-Flash (press End or Delete at boot). Read Gigabyte's release notes first, keep the PC powered during the flash, and expect BIOS settings to reset.", "WarnTextBrush"));
        grid.Children.Add(text);
        if (available)
        {
            var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Top, MinWidth = 120 };
            var button = new Button { Content = "Download", HorizontalAlignment = HorizontalAlignment.Right };
            var progress = new ProgressBar { Height = 4, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed, Maximum = 1 };
            var result = Caption("", "SecondaryTextBrush");
            result.HorizontalAlignment = HorizontalAlignment.Right; result.TextAlignment = TextAlignment.Right; result.MaxWidth = 220;
            button.Click += async (_, _) => await DownloadAsync(item, button, progress, result);
            actions.Children.Add(button); actions.Children.Add(progress); actions.Children.Add(result);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
        }
        return new Border { Child = grid, BorderBrush = (Brush)FindResource("HairlineBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8) };
    }

    private async Task DownloadAsync(UpdateItem item, Button button, ProgressBar progress, TextBlock result)
    {
        button.IsEnabled = false; ControlState.SetIsBusy(button, true); progress.Visibility = Visibility.Visible; progress.Value = 0;
        result.Foreground = (Brush)FindResource("SecondaryTextBrush"); result.Text = "Downloading…";
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var file = await GigabyteUpdates.DownloadAsync(Http, item, DownloadFolder, new Progress<double>(p => progress.Value = p), cancellation.Token);
            // Installers must be validly signed; the BIOS zip has no signature and is checked by its exact size.
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                var (valid, signer) = await Task.Run(() => Authenticode.Verify(file));
                if (!valid) { File.Delete(file); throw new IOException("The file's digital signature is not valid, so it was deleted."); }
                result.Text = "Saved · signed by " + signer;
            }
            else result.Text = "Saved · " + Size(new FileInfo(file).Length) + ", complete";
            button.Content = "Show file"; button.IsEnabled = true;
            button.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); } catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { } };
            progress.Visibility = Visibility.Collapsed;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            result.Foreground = (Brush)FindResource("ErrorTextBrush"); result.Text = error is OperationCanceledException ? "Download timed out." : error.Message;
            button.IsEnabled = true; progress.Visibility = Visibility.Collapsed;
        }
        finally { ControlState.SetIsBusy(button, false); }
    }

    private void FolderClick(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(DownloadFolder); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DownloadFolder + "\"") { UseShellExecute = true }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { SetStatus(error.Message); }
    }

    private TextBlock Caption(string text, string brush = "TertiaryTextBrush", double size = 12) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource(brush) };

    private static string Size(long bytes) => bytes >= 1 << 30 ? $"{bytes / (double)(1 << 30):0.0} GB" : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}

/// <summary>Windows' own Authenticode check (WinVerifyTrust, the same as Explorer's "Digital Signatures" tab), read-only.</summary>
internal static class Authenticode
{
    public static (bool Valid, string Signer) Verify(string path)
    {
        var file = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WinTrustData { cbStruct = (uint)Marshal.SizeOf<WinTrustData>(), dwUIChoice = 2, fdwRevocationChecks = 0, dwUnionChoice = 1, pFile = filePtr, dwStateAction = 0, dwProvFlags = 0x40 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            var valid = WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
            string signer;
            try { signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).GetNameInfo(X509NameType.SimpleName, false); }
            catch (System.Security.Cryptography.CryptographicException) { signer = "unknown"; }
            return (valid, signer);
        }
        finally { Marshal.FreeHGlobal(filePtr); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice; public uint fdwRevocationChecks;
        public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
        public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);
}
