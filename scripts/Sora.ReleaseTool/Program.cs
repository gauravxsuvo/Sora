using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Sora.Core.Updates;

try
{
    if (args is ["init-key", var keyFile, var policyFile, var keyId])
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Create the protected release key on Windows.");
        var keyPath = Path.GetFullPath(keyFile);
        var parent = Path.GetDirectoryName(keyPath)!;
        if (Directory.Exists(parent)) throw new IOException("Choose a new private key directory; existing directories are never reused.");
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        var user = WindowsIdentity.GetCurrent().User!; security.SetOwner(user);
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(parent).Create(security);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using (var file = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(file)) writer.Write(key.ExportPkcs8PrivateKeyPem());
        var policy = JsonSerializer.Deserialize<UpdatePolicy>(File.ReadAllBytes(policyFile), UpdatePolicy.Json) ?? throw new IOException("Invalid update policy.");
        if (policy.PublicKeys.ContainsKey(keyId)) throw new IOException("The public key ID already exists; inspect before rotation.");
        policy.PublicKeys.Add(keyId, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        File.WriteAllBytes(policyFile, JsonSerializer.SerializeToUtf8Bytes(policy, new JsonSerializerOptions(UpdatePolicy.Json) { WriteIndented = true }));
        Console.WriteLine("Release key created with a private Windows ACL. Only its public key was added to the policy. Back up the private key securely before distributing Sora.");
        return 0;
    }
    if (args is ["prepare", var installer, var version, var sequenceText, var notesFile, var policyPath, var signingKeyPath, var outputDirectory])
    {
        _ = ReleaseFeed.ParseVersion(version);
        var sequence = long.Parse(sequenceText, System.Globalization.CultureInfo.InvariantCulture);
        if (Directory.Exists(outputDirectory)) throw new IOException("Choose a new output directory; preserve prior release metadata.");
        var policy = JsonSerializer.Deserialize<UpdatePolicy>(File.ReadAllBytes(policyPath), UpdatePolicy.Json) ?? throw new IOException("Invalid update policy.");
        var fileVersion = FileVersionInfo.GetVersionInfo(installer);
        if (fileVersion.ProductName?.Trim() != "Sora" || new Version(fileVersion.FileMajorPart, fileVersion.FileMinorPart, fileVersion.FileBuildPart) != ReleaseFeed.ParseVersion(version))
            throw new IOException("The installer product/version does not match this release.");
        using var key = ECDsa.Create(); key.ImportFromPem(File.ReadAllText(signingKeyPath));
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var trustedKeyId = policy.PublicKeys.Single(p => p.Value == publicKey).Key;
        using var input = File.OpenRead(installer);
        var now = DateTimeOffset.UtcNow;
        var origin = policy.ValidateUrl(policy.FeedUrl).GetLeftPart(UriPartial.Authority);
        var release = new SoraRelease(1, "Sora", sequence, policy.Channel, version, now, now.AddDays(30), "win-x64", "10.0.19041", "1.0.0",
            File.ReadAllText(notesFile), $"{origin}/releases/{version}/win-x64/Sora-Setup-{version}-x64.exe", input.Length, Convert.ToHexString(SHA256.HashData(input)));
        var payload = JsonSerializer.SerializeToUtf8Bytes(release, UpdatePolicy.Json);
        var envelope = new ReleaseEnvelope(trustedKeyId, "ECDSA-P256-SHA256-P1363", Convert.ToBase64String(payload),
            Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, UpdatePolicy.Json);
        _ = ReleaseFeed.Verify(bytes, policy, now, 0);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(Path.Combine(outputDirectory, "stable.json"), bytes);
        File.WriteAllBytes(Path.Combine(outputDirectory, "release-payload.json"), JsonSerializer.SerializeToUtf8Bytes(release, new JsonSerializerOptions(UpdatePolicy.Json) { WriteIndented = true }));
        Console.WriteLine($"Signed release {version}, sequence {sequence}; feed expires {release.ExpiresAt:O}. Upload artifacts, verify public bytes, then promote stable.json.");
        return 0;
    }
    if (args is ["verify", var envelopePath, var verificationPolicy, var artifact])
    {
        var policy = JsonSerializer.Deserialize<UpdatePolicy>(File.ReadAllBytes(verificationPolicy), UpdatePolicy.Json)!;
        var release = ReleaseFeed.Verify(File.ReadAllBytes(envelopePath), policy, DateTimeOffset.UtcNow, 0);
        ReleaseDownload.VerifyFile(artifact, release);
        Console.WriteLine($"Verified Sora {release.Version}, feed sequence {release.Sequence}, SHA-256 {release.Sha256}.");
        return 0;
    }
    Console.Error.WriteLine("Usage:\n  init-key PRIVATE_KEY_PATH POLICY_PATH KEY_ID\n  prepare INSTALLER VERSION SEQUENCE NOTES_FILE POLICY_PATH PRIVATE_KEY_PATH NEW_OUTPUT_DIRECTORY\n  verify SIGNED_FEED POLICY_PATH INSTALLER");
    return 2;
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
