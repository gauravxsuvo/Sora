using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sora.Core.Updates;

internal static class ApplicationUpdateChecks
{
    internal static async Task Run(Action<bool, string> require)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var policy = new UpdatePolicy("https://sora.portways.app/channels/stable.json", ["sora.portways.app"],
            new() { ["test-only"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }, "stable");
        var now = DateTimeOffset.UtcNow;
        var artifact = Encoding.UTF8.GetBytes("synthetic installer bytes; never executed");
        var release = new SoraRelease(1, "Sora", 12, "stable", "0.12.0", now, now.AddDays(30), "win-x64", "10.0.19041", "1.0.0", "Test notes",
            "https://sora.portways.app/releases/0.12.0/win-x64/Sora-Setup-0.12.0-x64.exe", artifact.Length, Convert.ToHexString(SHA256.HashData(artifact)));
        byte[] Sign(SoraRelease value, string id = "test-only", string algorithm = "ECDSA-P256-SHA256-P1363")
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, UpdatePolicy.Json);
            return JsonSerializer.SerializeToUtf8Bytes(new ReleaseEnvelope(id, algorithm, Convert.ToBase64String(payload),
                Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))), UpdatePolicy.Json);
        }
        void Reject(Action action, string name)
        {
            var rejected = false;
            try { action(); } catch (Exception e) when (e is InvalidDataException or CryptographicException or JsonException or FormatException) { rejected = true; }
            require(rejected, name);
        }
        SoraRelease Verify(SoraRelease value) => ReleaseFeed.Verify(Sign(value), policy, now, 12, new Version(10, 0, 22631));
        require(Verify(release) == release, "A valid signed release is accepted");
        require(ReleaseFeed.ParseVersion("0.12.0") > ReleaseFeed.ParseVersion("0.9.10"), "Numeric versions compare correctly");
        foreach (var invalid in new[] { "v0.12.0", "0.12", "0.12.0-beta", "0.012.0", "0.12.0.0", "70000.1.1" }) Reject(() => ReleaseFeed.ParseVersion(invalid), "Malformed version rejected: " + invalid);
        Reject(() => ReleaseFeed.Verify(Sign(release, "unknown"), policy, now, 0), "Unknown key rejected");
        Reject(() => ReleaseFeed.Verify(Sign(release, algorithm: "none"), policy, now, 0), "Unknown algorithm rejected");
        var envelope = JsonSerializer.Deserialize<ReleaseEnvelope>(Sign(release), UpdatePolicy.Json)!;
        var changed = JsonSerializer.SerializeToUtf8Bytes(envelope with { Payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(release with { Size = release.Size + 1 }, UpdatePolicy.Json)) }, UpdatePolicy.Json);
        Reject(() => ReleaseFeed.Verify(changed, policy, now, 0), "Tampered metadata rejected before policy parsing");
        foreach (var invalid in new[] {
            release with { Sequence = 11 }, release with { SchemaVersion = 2 }, release with { Product = "Other" },
            release with { Platform = "win-arm64" }, release with { Channel = "beta" }, release with { ExpiresAt = now },
            release with { PublishedAt = now.AddHours(1) }, release with { ExpiresAt = now.AddDays(46) },
            release with { Size = 0 }, release with { Sha256 = new string('x', 64) }, release with { Notes = new string('x', 16001) },
            release with { MinimumUpdaterVersion = "2.0.0" }, release with { MinimumWindowsVersion = "11.0" },
            release with { InstallerUrl = "https://evil.invalid/releases/0.12.0/win-x64/Sora-Setup-0.12.0-x64.exe" },
            release with { InstallerUrl = release.InstallerUrl.Replace("https:", "http:") },
            release with { InstallerUrl = release.InstallerUrl + "?replace=true" },
            release with { InstallerUrl = release.InstallerUrl.Replace("0.12.0-x64.exe", "0.12.1-x64.exe") } })
            Reject(() => Verify(invalid), "Invalid signed policy rejected: " + invalid);
        Reject(() => ReleaseFeed.Verify(new byte[ReleaseFeed.MaximumEnvelopeBytes + 1], policy, now, 0), "Oversized feed rejected");
        Reject(() => policy.ValidateUrl("https://user@sora.portways.app/"), "Credentials in URL rejected");
        Reject(() => policy.ValidateUrl("https://sora.portways.app:444/"), "Nonstandard port rejected");
        using var bounded = new ByteArrayContent(new byte[100]);
        try { await ReleaseFeed.ReadBoundedAsync(bounded, 99, default); require(false, "Oversized HTTP feed rejected"); } catch (InvalidDataException) { }

        var root = Path.Combine(AppContext.BaseDirectory, "update-check-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var http = new HttpClient(new FakeHttp(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(artifact) }));
            var destination = Path.Combine(root, "installer.exe");
            await ReleaseDownload.DownloadAsync(http, policy, release, destination, null, default);
            ReleaseDownload.VerifyFile(destination, release);
            require(File.ReadAllBytes(destination).SequenceEqual(artifact), "Verified download preserves exact bytes");
            File.WriteAllBytes(destination, [1]); Reject(() => ReleaseDownload.VerifyFile(destination, release), "Staged file replacement rejected");
            using var bad = new HttpClient(new FakeHttp(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[artifact.Length]) }));
            var badPath = Path.Combine(root, "corrupt.exe");
            try { await ReleaseDownload.DownloadAsync(bad, policy, release, badPath, null, default); require(false, "Corrupt download rejected"); } catch (InvalidDataException) { }
            require(!File.Exists(badPath) && !File.Exists(badPath + ".partial"), "A failed download leaves no executable or partial file");
            foreach (var code in new[] { HttpStatusCode.Redirect, HttpStatusCode.TooManyRequests, HttpStatusCode.NotFound })
            {
                using var rejectedHttp = new HttpClient(new FakeHttp(() => new HttpResponseMessage(code) { Content = new ByteArrayContent([]) }));
                var failed = false;
                try { await ReleaseDownload.DownloadAsync(rejectedHttp, policy, release, badPath, null, default); }
                catch (Exception e) when (e is InvalidDataException or HttpRequestException) { failed = true; }
                require(failed && !File.Exists(badPath), "Redirect/error cannot produce an installer: " + code);
            }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await ReleaseDownload.DownloadAsync(http, policy, release, badPath, null, cancellation.Token); require(false, "Cancellation observed"); } catch (OperationCanceledException) { }
            var statePath = Path.Combine(root, "state.json");
            var state = new UpdateState(false, 12, now, "0.12.0", "0.12.0"); state.Save(statePath);
            require(UpdateState.Load(statePath) == state, "Updater preferences and replay state survive restart");
            File.WriteAllText(statePath, "broken");
            Reject(() => UpdateState.Load(statePath), "Corrupt replay state is not silently reset");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class FakeHttp(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response()); }
    }
}
