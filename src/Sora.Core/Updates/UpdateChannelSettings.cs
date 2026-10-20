using System.Text.Json;

namespace Sora.Core.Updates;

public sealed class UpdateChannelSettings(string directory)
{
    private string PreferencePath => Path.Combine(directory, "app-update-channel.json");
    public static string Validate(string channel) => channel is "stable" or "beta" ? channel : throw new InvalidDataException("Unknown Sora update channel.");
    public string ReadChannel(string installedVersion)
    {
        var fallback = ReleaseVersion.Parse(installedVersion).Channel;
        if (!File.Exists(PreferencePath)) return fallback;
        if (new FileInfo(PreferencePath).Length > 32) throw new InvalidDataException("Invalid update channel preference.");
        return Validate(JsonSerializer.Deserialize<string>(File.ReadAllBytes(PreferencePath)) ?? "");
    }
    public string StatePath(string channel) => Path.Combine(directory, Validate(channel) == "stable" ? "app-updates.json" : "app-updates-beta.json");
    public UpdateState Load(string channel, bool automaticFallback = true)
    {
        var path = StatePath(channel);
        if (File.Exists(path)) return UpdateState.Load(path);
        // Keep the old opt-out preference, but never copy stable replay counters,
        // skips or timestamps into the independent beta channel.
        if (channel == "beta" && File.Exists(StatePath("stable"))) automaticFallback = UpdateState.Load(StatePath("stable")).AutomaticChecks;
        return new(AutomaticChecks: automaticFallback);
    }
    public void SaveChannel(string channel)
    {
        Validate(channel);
        Directory.CreateDirectory(directory);
        var temp = PreferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(channel)); File.Move(temp, PreferencePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
