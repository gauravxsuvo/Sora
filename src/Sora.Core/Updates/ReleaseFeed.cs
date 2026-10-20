using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sora.Core.Updates;

public sealed record UpdatePolicy(string FeedUrl, string[] AllowedHosts, Dictionary<string, string> PublicKeys, string Channel)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    [JsonIgnore]
    public bool Configured => PublicKeys.Count > 0 && AllowedHosts.Length > 0 && Channel is "stable" or "beta";
    public UpdatePolicy ForChannel(string channel)
    {
        UpdateChannelSettings.Validate(channel);
        var origin = ValidateUrl(FeedUrl).GetLeftPart(UriPartial.Authority);
        return this with { FeedUrl = origin + "/channels/" + channel + ".json", Channel = channel };
    }
    public Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !AllowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The release URL is outside Sora's trusted HTTPS hosts.");
        return uri;
    }
}

public sealed record ReleaseEnvelope(string KeyId, string Algorithm, string Payload, string Signature);
public sealed record SoraRelease(int SchemaVersion, string Product, long Sequence, string Channel, string Version,
    DateTimeOffset PublishedAt, DateTimeOffset ExpiresAt, string Platform, string MinimumWindowsVersion,
    string MinimumUpdaterVersion, string Notes, string InstallerUrl, long Size, string Sha256);

public static class ReleaseFeed
{
    public const int MaximumEnvelopeBytes = 128 * 1024;
    public const string UpdaterVersion = "1.1.0";

    public static Version ParseVersion(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Length > 5 || p.Any(c => c is < '0' or > '9') || (p.Length > 1 && p[0] == '0')) ||
            !Version.TryParse(value, out var version) || version.Major > 65535 || version.Minor > 65535 || version.Build > 65535)
            throw new InvalidDataException("Expected a numeric major.minor.patch Sora version.");
        return version;
    }

    public static SoraRelease Verify(ReadOnlySpan<byte> bytes, UpdatePolicy policy, DateTimeOffset now, long highestSequence,
        Version? windowsVersion = null)
    {
        if (!policy.Configured) throw new InvalidDataException("Release signing has not been configured.");
        if (bytes.Length == 0 || bytes.Length > MaximumEnvelopeBytes) throw new InvalidDataException("Invalid release feed size.");
        var envelope = JsonSerializer.Deserialize<ReleaseEnvelope>(bytes, UpdatePolicy.Json) ?? throw new InvalidDataException("Empty feed.");
        if (envelope.Algorithm != "ECDSA-P256-SHA256-P1363" || envelope.KeyId == null || !policy.PublicKeys.TryGetValue(envelope.KeyId, out var publicKey))
            throw new InvalidDataException("Untrusted release signing key or algorithm.");
        var payload = Convert.FromBase64String(envelope.Payload);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        if (key.KeySize != 256 || !key.VerifyData(payload, Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new InvalidDataException("The release signature is invalid.");
        var release = JsonSerializer.Deserialize<SoraRelease>(payload, UpdatePolicy.Json) ?? throw new InvalidDataException("Empty release.");
        if (release.SchemaVersion != 1 || release.Product != "Sora" || release.Channel != policy.Channel || release.Platform != "win-x64" ||
            release.Sequence < 1 || release.Sequence < highestSequence || release.PublishedAt > now.AddMinutes(5) ||
            release.ExpiresAt <= now || release.ExpiresAt <= release.PublishedAt || release.ExpiresAt - release.PublishedAt > TimeSpan.FromDays(45))
            throw new InvalidDataException("The release is incompatible, expired or older than a previously accepted feed.");
        if (ReleaseVersion.Parse(release.Version).Channel != policy.Channel) throw new InvalidDataException("Release version does not match its update channel.");
        if (ParseVersion(release.MinimumUpdaterVersion) > ParseVersion(UpdaterVersion)) throw new InvalidDataException("Install a newer Sora installer from the website to update.");
        if (!Version.TryParse(release.MinimumWindowsVersion, out var minimumWindows) || (windowsVersion != null && windowsVersion < minimumWindows))
            throw new InvalidDataException("This release requires a newer Windows version.");
        if (release.Notes == null || release.Notes.Length > 16000 || release.Size is < 1 or > 2L * 1024 * 1024 * 1024 ||
            release.Sha256 == null || release.Sha256.Length != 64 || release.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid release notes, installer size or hash.");
        var uri = policy.ValidateUrl(release.InstallerUrl);
        var expectedPath = $"/releases/{release.Version}/win-x64/Sora-Setup-{release.Version}-x64.exe";
        if (uri.AbsolutePath != expectedPath || uri.Query.Length != 0) throw new InvalidDataException("Invalid immutable installer path.");
        return release;
    }

    public static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("The response is too large.");
        await using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("The response is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public static async Task<byte[]> FetchAsync(HttpClient http, UpdatePolicy policy, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, policy.ValidateUrl(policy.FeedUrl));
        request.Headers.CacheControl = new() { NoCache = true };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        RequireSuccess(response);
        return await ReadBoundedAsync(response.Content, MaximumEnvelopeBytes, token).ConfigureAwait(false);
    }

    public static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("The release server is busy. Try again later.", null, response.StatusCode);
        // The caller's HttpClient must disable redirects. A redirect must not escape the allowlist.
        if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidDataException("Release redirects are not allowed.");
        response.EnsureSuccessStatusCode();
    }
}
