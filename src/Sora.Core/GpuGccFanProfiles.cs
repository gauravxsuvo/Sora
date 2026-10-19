using System.Text.Json;

namespace Sora.Core;

public static class GpuGccFanProfiles
{
    // GCC GFans .dat / vfan.dat files contain JSON FanSetting[], with each fan explicitly indexed.
    public static GpuFanProfile Import(string text, string name, uint? selectedFan = null)
    {
        if (text.Length > 65536) throw new ArgumentException("GCC GPU profile exceeds 64 KiB.");
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() is < 1 or > 3) throw new ArgumentException("Expected a GCC GFans FanSetting array.");
        var profiles = new Dictionary<uint, GpuFanProfile>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("IsSelect", out var isSelected) && !isSelected.GetBoolean()) continue;
            uint fan = entry.GetProperty("FanIndex").GetUInt32(); int mode = entry.GetProperty("Mode").GetInt32();
            if (fan > 2 || mode is < 0 or > 3 || profiles.ContainsKey(fan)) throw new ArgumentException("Unknown GCC fan channel or mode.");
            var curve = entry.GetProperty("Curve").GetProperty("curve");
            if (curve.ValueKind != JsonValueKind.Array || curve.GetArrayLength() != 3) throw new ArgumentException("The verified GCC native route needs three RPM points.");
            var rpmPoints = curve.EnumerateArray().Select(p => new GpuRpmPoint(p.GetProperty("Temperature").GetDouble(), p.GetProperty("Rpm").GetUInt32())).ToArray();
            bool stop = entry.GetProperty("Stop").GetBoolean();
            var profile = new GpuFanProfile { Name = name, Mode = mode == 1 ? GpuFanMode.HardwareFixed : GpuFanMode.HardwareCurve, RpmPoints = mode == 1 ? new GpuFanProfile().RpmPoints : rpmPoints, FixedRpm = rpmPoints[0].Rpm, AutoFanStop = stop, FanIndices = [fan] };
            profile.Validate(); profiles.Add(fan, profile);
        }
        if (selectedFan is uint selected)
            return profiles.TryGetValue(selected, out var single) ? single : throw new ArgumentException("The GCC file does not contain the selected fan.");
        if (profiles.Count != 3) throw new ArgumentException("Select an individual fan before importing a partial GCC profile.");
        var first = profiles.Values.First();
        if (profiles.Values.Any(p => p.Mode != first.Mode || p.AutoFanStop != first.AutoFanStop || p.FixedRpm != first.FixedRpm || !p.RpmPoints.SequenceEqual(first.RpmPoints))) throw new ArgumentException("GCC stores different fan curves. Select an individual fan and import that channel to preserve its exact settings.");
        return first with { FanIndices = [0, 1, 2] };
    }
}
