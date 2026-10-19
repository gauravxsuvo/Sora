using System.Security.Cryptography;

namespace Sora.Core.Updates;

public static class ReleaseDownload
{
    // Caller owns a private staging directory. Never infer executable names from server headers.
    public static async Task DownloadAsync(HttpClient http, UpdatePolicy policy, SoraRelease release, string destination,
        IProgress<double>? progress, CancellationToken token)
    {
        if (File.Exists(destination) || File.Exists(destination + ".partial")) throw new IOException("Choose an empty update staging directory.");
        var partial = destination + ".partial";
        try
        {
            using var response = await http.GetAsync(policy.ValidateUrl(release.InstallerUrl), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            ReleaseFeed.RequireSuccess(response);
            if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new InvalidDataException("Installer size does not match the signed release.");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > release.Size) throw new InvalidDataException("Installer exceeds the signed size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    progress?.Report((double)total / release.Size);
                }
                if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer verification failed. Download it again.");
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(true);
            }
            File.Move(partial, destination);
        }
        catch { if (File.Exists(partial)) File.Delete(partial); throw; }
    }

    public static void VerifyFile(string path, SoraRelease release)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != release.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staged installer no longer matches the signed release.");
    }
}
