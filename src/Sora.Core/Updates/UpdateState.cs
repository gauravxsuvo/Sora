using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sora.Core.Updates;

public sealed record UpdateState([property: JsonRequired] bool AutomaticChecks = true, [property: JsonRequired] long HighestSequence = 0,
    [property: JsonRequired] DateTimeOffset? LastChecked = null, [property: JsonRequired] string? SkippedVersion = null,
    [property: JsonRequired] string? NotifiedVersion = null)
{
    public static UpdateState Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 8192) throw new InvalidDataException("Invalid updater state. Inspect the updater settings file.");
        var state = JsonSerializer.Deserialize<UpdateState>(File.ReadAllBytes(path), UpdatePolicy.Json) ?? throw new InvalidDataException("Invalid updater state.");
        if (state.HighestSequence < 0 || state.LastChecked > DateTimeOffset.UtcNow.AddMinutes(5)) throw new InvalidDataException("Invalid updater sequence or last-check time.");
        return state;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, UpdatePolicy.Json)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
