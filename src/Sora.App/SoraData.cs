using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sora.App;

// One settings directory for every release and for tray launches. Older copies are left intact.
internal static class SoraData
{
    public static string DirectoryPath { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sora", "data");
    internal static bool IsDevelopmentPreview { get; private set; }
    internal static bool IsIsolated { get; private set; }
    internal static void UseIsolatedPreviewDirectory(string path, bool developmentGallery = true)
    {
        DirectoryPath = Path.GetFullPath(path);
        IsDevelopmentPreview = developmentGallery;
        IsIsolated = true;
    }

    public static void Migrate()
    {
        if (IsIsolated) return;
        var marker = Path.Combine(DirectoryPath, "migration-v1.json");
        if (File.Exists(marker)) return;
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(AppContext.BaseDirectory, "data") };
        // Development and published builds in the same checkout previously held independent settings.
        for (var parent = new DirectoryInfo(AppContext.BaseDirectory); parent != null; parent = parent.Parent)
        {
            if (parent.Name.Equals("dist", StringComparison.OrdinalIgnoreCase))
                foreach (var release in parent.EnumerateDirectories("Sora-*"))
                    if ((release.Attributes & FileAttributes.ReparsePoint) == 0) candidates.Add(Path.Combine(release.FullName, "data"));
            if (!File.Exists(Path.Combine(parent.FullName, "Sora.sln")) && !Directory.Exists(Path.Combine(parent.FullName, "src", "Sora.App"))) continue;
            var dist = Path.Combine(parent.FullName, "dist");
            if (Directory.Exists(dist))
                foreach (var release in new DirectoryInfo(dist).EnumerateDirectories("Sora-*"))
                    if ((release.Attributes & FileAttributes.ReparsePoint) == 0) candidates.Add(Path.Combine(release.FullName, "data"));
            candidates.Add(Path.Combine(parent.FullName, "src", "Sora.App", "bin", "Debug", "net8.0-windows", "data"));
            break;
        }
        Directory.CreateDirectory(DirectoryPath);
        var copied = new List<object>();
        var files = candidates.Where(Directory.Exists).SelectMany(root =>
            System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                .Select(path => (Root: root, File: new FileInfo(path))));
        foreach (var group in files.GroupBy(f => Path.GetRelativePath(f.Root, f.File.FullName), StringComparer.OrdinalIgnoreCase))
        {
            var destination = Path.Combine(DirectoryPath, group.Key);
            if (File.Exists(destination)) continue;
            foreach (var source in group.OrderByDescending(f => f.File.LastWriteTimeUtc))
            {
                // An interrupted or corrupt legacy JSON must not replace a valid older profile.
                if (source.File.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    try { using var json = JsonDocument.Parse(File.ReadAllText(source.File.FullName)); }
                    catch (JsonException) { continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source.File.FullName, destination);
                copied.Add(new { destination = group.Key, source = source.File.FullName });
                break;
            }
        }
        File.WriteAllText(marker, JsonSerializer.Serialize(new { migratedAt = DateTimeOffset.Now, copied }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
