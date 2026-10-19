namespace Sora.Core.Updates;

/// <summary>Deletes one explicitly named application directory without following junctions or symlinks.</summary>
public static class OwnedDirectoryRemoval
{
    public static void Validate(string parent, string childName)
    {
        if (string.IsNullOrWhiteSpace(childName) || childName is "." or ".." || childName.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new IOException("Invalid application directory name.");
        var target = Path.GetFullPath(Path.Combine(parent, childName));
        if (!string.Equals(Path.GetDirectoryName(target), Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Removal escaped its approved parent.");
        for (var ancestor = new DirectoryInfo(parent); ancestor != null; ancestor = ancestor.Parent)
            if (Attributes(ancestor.FullName) is { } parentAttributes && (parentAttributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("An application data path is a link: " + ancestor.FullName);
        var targetAttributes = Attributes(target);
        if (targetAttributes == null) return;
        if ((targetAttributes & FileAttributes.Directory) == 0) throw new IOException("An application directory was replaced by a file: " + target);
        var pending = new Stack<string>(); pending.Push(target);
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Application data contains a link: " + directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Application data contains a link: " + entry);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    public static void Remove(string parent, string childName)
    {
        Validate(parent, childName);
        var path = Path.Combine(parent, childName);
        if (Attributes(path) != null) Directory.Delete(path, recursive: true);
    }

    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        // Permission failures must stop cleanup, not masquerade as successfully removed data.
    }
}
