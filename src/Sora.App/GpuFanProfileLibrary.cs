using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sora.Core;

namespace Sora.App;

// Editor profiles never imply permission to program a GPU. Selection and saving only stage settings.
internal sealed class GpuFanProfileLibrary
{
    private readonly string path;
    private readonly JsonSerializerOptions json = new() { WriteIndented = true };
    private sealed record Catalog(int Version, Dictionary<string, GpuFanProfile[]> Adapters);

    public GpuFanProfileLibrary(string dataDirectory) => path = Path.Combine(dataDirectory, "gpu-fans-profiles.json");

    private Catalog Load()
    {
        if (!File.Exists(path)) return new(1, new(StringComparer.Ordinal));
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("The GPU profile library exceeds 2 MiB.");
        var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path)) ?? throw new InvalidDataException("The GPU profile library is empty.");
        if (catalog.Version != 1 || catalog.Adapters == null) throw new InvalidDataException("Unsupported GPU profile library version.");
        foreach (var pair in catalog.Adapters)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) throw new InvalidDataException("Invalid GPU profile identity.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in pair.Value)
            {
                if (profile == null) throw new InvalidDataException("Missing GPU profile.");
                profile.Validate();
                if (!names.Add(profile.Name)) throw new InvalidDataException("Duplicate GPU profile name: " + profile.Name);
            }
        }
        return catalog;
    }

    public GpuFanProfile[] Read(string identity) => Load().Adapters.GetValueOrDefault(identity) ?? Array.Empty<GpuFanProfile>();

    public void Save(string identity, GpuFanProfile profile)
    {
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Select a GPU before saving its profile.");
        profile.Validate();
        var catalog = Load();
        var profiles = (catalog.Adapters.GetValueOrDefault(identity) ?? Array.Empty<GpuFanProfile>()).ToList();
        var index = profiles.FindIndex(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) profiles[index] = profile; else profiles.Add(profile);
        catalog.Adapters[identity] = profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        Write(catalog);
    }

    public void Delete(string identity, string name)
    {
        var catalog = Load();
        if (!catalog.Adapters.TryGetValue(identity, out var profiles)) return;
        catalog.Adapters[identity] = profiles.Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        Write(catalog);
    }

    private void Write(Catalog catalog)
    {
        var text = JsonSerializer.Serialize(catalog, json);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 2 * 1024 * 1024) throw new InvalidDataException("The GPU profile library exceeds 2 MiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", text);
        File.Move(path + ".tmp", path, true);
    }
}
