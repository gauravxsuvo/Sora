using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Sora.Core.Updates;

namespace Sora.App;

internal static class ApplicationUpdateUiCheck
{
    internal static async Task<bool> RunAsync(string output)
    {
        output = Path.GetFullPath(output);
        if (File.Exists(output) || Directory.Exists(output + ".data")) throw new IOException("Choose a new updater UI evidence path.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var root = output + ".data";
        Directory.CreateDirectory(root);
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool value) { checks[name] = value; if (!value) throw new InvalidOperationException(name); }
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var policy = new UpdatePolicy("https://sora.portways.app/channels/stable.json", ["sora.portways.app"], new() { ["fixture"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }, "stable");
        var artifact = Encoding.UTF8.GetBytes("simulated installer bytes; never executed");
        var now = DateTimeOffset.UtcNow;
        SoraRelease Release(string channel) => new(1, "Sora", channel == "beta" ? 3 : 100, channel, channel == "beta" ? "0.13.0-beta.4" : "0.13.0", now, now.AddDays(1), "win-x64", "10.0.19041", "1.1.0", "Fixture release notes",
            "https://sora.portways.app/releases/" + (channel == "beta" ? "0.13.0-beta.4/win-x64/Sora-Setup-0.13.0-beta.4-x64.exe" : "0.13.0/win-x64/Sora-Setup-0.13.0-x64.exe"), artifact.Length, Convert.ToHexString(SHA256.HashData(artifact)));
        byte[] Sign(SoraRelease release)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(release, UpdatePolicy.Json);
            return JsonSerializer.SerializeToUtf8Bytes(new ReleaseEnvelope("fixture", "ECDSA-P256-SHA256-P1363", Convert.ToBase64String(payload), Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))), UpdatePolicy.Json);
        }
        var paths = new List<string>();
        var wrongChannel = false; var corruptInstaller = false; var tamperSignature = false;
        var handler = new FixtureHttp(request =>
        {
            var path = request.RequestUri!.AbsolutePath; paths.Add(path);
            if (path.StartsWith("/channels/", StringComparison.Ordinal))
            {
                var release = Release(wrongChannel || path.Contains("stable") ? "stable" : "beta");
                var bytes = Sign(release);
                if (tamperSignature)
                {
                    var envelope = JsonSerializer.Deserialize<ReleaseEnvelope>(bytes, UpdatePolicy.Json)!;
                    bytes = JsonSerializer.SerializeToUtf8Bytes(envelope with { Payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(release with { Size = 999 }, UpdatePolicy.Json)) }, UpdatePolicy.Json);
                }
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(corruptInstaller ? new byte[artifact.Length] : artifact) };
        });
        string Stage(string id, bool create)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("Invalid fixture stage ID.");
            var stage = Path.Combine(root, "stages", id);
            if (create) Directory.CreateDirectory(stage);
            return stage;
        }
        using var service = new ApplicationUpdateService("0.13.0-beta.3", root, new HttpClient(handler), policy, Stage);
        var card = new ApplicationUpdateCard(); card.AttachForCheck(service);
        var window = new Window { Content = card, Width = 760, Height = 750, Left = -12000, ShowInTaskbar = false };
        window.Show();
        async Task Click(Button button)
        {
            if (!button.IsEnabled) throw new InvalidOperationException("Disabled updater button: " + button.Name);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await card.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (service.Busy && DateTime.UtcNow < deadline) await Task.Delay(10);
            if (service.Busy) throw new TimeoutException("Updater fixture did not finish.");
        }
        try
        {
            Check("Beta build defaults to Beta with full installed suffix", service.Policy.Channel == "beta" && card.ChannelPicker.SelectedIndex == 1 && card.VersionText.Text.Contains("0.13.0-beta.3"));
            await Click(card.AppCheck);
            Check("Check now uses signed beta feed and discovers beta.4", paths.Contains("/channels/beta.json") && service.Available && card.AppDownload.Visibility == Visibility.Visible && service.State.HighestSequence == 3);
            await Click(card.AppDownload);
            var stage = Directory.GetDirectories(Path.Combine(root, "stages")).Single();
            Check("Download button verifies exact installer bytes and stages beta identity", service.Downloaded && card.AppInstall.Visibility == Visibility.Visible && File.ReadAllBytes(Path.Combine(stage, "installer.exe")).SequenceEqual(artifact) && File.ReadAllText(Path.Combine(stage, "channel.txt")) == "beta");
            Check("No installation is started by checking or downloading", !service.Installing && !File.Exists(Path.Combine(stage, "result.json")));
            card.ChannelPicker.SelectedIndex = 0;
            Check("Switching channel invalidates the previous ready-to-install download", service.Policy.Channel == "stable" && !service.Downloaded && !service.Available && card.AppInstall.Visibility == Visibility.Collapsed);
            await Click(card.AppCheck);
            Check("Newer stable graduation is offered after explicit channel switch", service.Available && service.Release?.Version == "0.13.0" && service.State.HighestSequence == 100);
            card.ChannelPicker.SelectedIndex = 1;
            Check("Switching back retains beta replay history separately", service.State.HighestSequence == 3 && !service.Available);
            await Click(card.AppCheck); await Click(card.AppSkip);
            Check("Skip persists within the beta channel", service.State.SkippedVersion == "0.13.0-beta.4");
            card.ChannelPicker.SelectedIndex = 0;
            Check("Beta skip never suppresses stable offers", service.State.SkippedVersion == null && service.State.HighestSequence == 100);
            card.ChannelPicker.SelectedIndex = 1;
            wrongChannel = true;
            await Click(card.AppCheck);
            Check("Wrong-channel metadata cannot enable Download", !service.Available && card.AppDownload.Visibility == Visibility.Collapsed && service.Status.Contains("Could not check"));
            wrongChannel = false; tamperSignature = true;
            await Click(card.AppCheck);
            Check("Tampered signature cannot enable Download", !service.Available && service.Status.Contains("signature"));
            tamperSignature = false; corruptInstaller = true;
            await Click(card.AppCheck); await Click(card.AppDownload);
            Check("Corrupt installer leaves no ready-to-install transaction", !service.Downloaded && card.AppInstall.Visibility == Visibility.Collapsed && service.Status.Contains("verification failed") && Directory.GetFiles(Path.Combine(root, "stages"), "*.partial", SearchOption.AllDirectories).Length == 0);
            Check("Channel selection survives service restart", new UpdateChannelSettings(root).ReadChannel("0.13.0") == "beta");
            using var newer = new ApplicationUpdateService("0.14.0-beta.1", Path.Combine(root, "newer"), new HttpClient(new FixtureHttp(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Sign(Release("stable"))) })), policy, Stage);
            newer.SetChannel("stable"); await newer.CheckAsync();
            Check("Selecting stable never offers an older numeric downgrade", !newer.Available);
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, new { passed = checks.Values.All(value => value), synthetic = true, configurationWrites = 0, installerExecutions = 0, checks }, new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        finally { window.Close(); }
    }
    private sealed class FixtureHttp(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
}
