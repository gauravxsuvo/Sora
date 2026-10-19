using Sora.Core.Updates;

internal static class UninstallRemovalChecks
{
    internal static void Run(Action<bool, string> require)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "uninstall-removal-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var data = Path.Combine(root, "Sora");
            var other = Path.Combine(root, "OtherApp"); Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "keep.txt"), "unrelated");
            Directory.CreateDirectory(Path.Combine(data, "data", "profiles"));
            File.WriteAllText(Path.Combine(data, "data", "profiles", "test.json"), "{}");
            OwnedDirectoryRemoval.Validate(root, "Sora"); OwnedDirectoryRemoval.Remove(root, "Sora");
            require(!Directory.Exists(data) && File.Exists(Path.Combine(other, "keep.txt")), "Removal deletes exactly the named Sora directory, preserving neighbors");
            foreach (var name in new[] { "..", ".", "../OtherApp", @"..\OtherApp", "C:", "" })
            {
                var rejected = false;
                try { OwnedDirectoryRemoval.Remove(root, name); } catch (IOException) { rejected = true; }
                require(rejected && File.Exists(Path.Combine(other, "keep.txt")), "Unsafe removal child name rejected: " + name);
            }
            File.WriteAllText(data, "wrong type");
            var wrongType = false;
            try { OwnedDirectoryRemoval.Remove(root, "Sora"); } catch (IOException) { wrongType = true; }
            require(wrongType && File.Exists(data), "A file substituted for the data directory is refused");
            File.Delete(data);
            OwnedDirectoryRemoval.Remove(root, "Sora");
            require(File.Exists(Path.Combine(other, "keep.txt")), "Already-absent data is safe and idempotent");
        }
        finally { Directory.Delete(root, true); }
    }
}
