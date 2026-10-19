using System.Text.Json;

namespace Sora.Core;

public sealed record LogitechPreset(int Version, string Name, ushort ProductId, int Dpi, int ReportRate);

public static class LogitechProfiles
{
    public static void Validate(LogitechPreset profile, LogitechSnapshot state)
    {
        if (profile.Version != 1 || profile.ProductId != state.ProductId || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80 || state.Sensors.Length != 1 || !state.Sensors[0].Accepts(profile.Dpi) || !state.ReportRates.Contains(profile.ReportRate))
            throw new InvalidDataException("This preset does not match the connected mouse's model or sensitivity limits.");
    }
    public static LogitechPreset Load(string path, LogitechSnapshot state)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("The sensitivity preset is too large.");
        var profile = JsonSerializer.Deserialize<LogitechPreset>(File.ReadAllText(path)) ?? throw new InvalidDataException("The sensitivity preset is empty.");
        Validate(profile, state); return profile;
    }
    public static void Save(string path, LogitechPreset profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
