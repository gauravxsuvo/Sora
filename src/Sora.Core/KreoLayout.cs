namespace Sora.Core;

/// <summary>A key's position and size in key units (1u = one standard key pitch).</summary>
public sealed record KeyPlacement(KreoKey Key, double X, double Y, double Width, double Height);

/// <summary>
/// Turns the vendor's pixel hotspot map into a physical key-unit grid. The hotspots jitter by a pixel or two and
/// describe the legend area rather than the keycap, so sizes are derived from the key pitch and snapped to 0.25u.
/// Key labels, indices and actions are passed through untouched.
/// </summary>
public static class KreoLayout
{
    public static IReadOnlyList<KeyPlacement> Build(KreoModel model)
    {
        var keys = model.Keys.Where(k => k.Width > 0 && k.Height > 0).ToArray();
        if (keys.Length == 0) return [];
        var unitWidth = Mode(keys.Select(k => k.Width));
        var unitHeight = Mode(keys.Select(k => k.Height));
        // Pitch: the most typical distance between neighbouring 1u keys on the same row.
        var gaps = new List<double>();
        foreach (var a in keys.Where(k => k.Width == unitWidth))
        {
            var next = keys.Where(b => b.Width == unitWidth && Math.Abs(b.Y - a.Y) <= 4 && b.X > a.X).OrderBy(b => b.X).FirstOrDefault();
            if (next != null && next.X - a.X < unitWidth * 2) gaps.Add(next.X - a.X);
        }
        if (gaps.Count == 0) throw new InvalidDataException(model.Name + " has no regular key rows.");
        gaps.Sort();
        var pitch = gaps[gaps.Count / 2];

        static double Snap(double value) => Math.Round(value * 4, MidpointRounding.AwayFromZero) / 4;
        var raw = keys.Select(k =>
        {
            var width = Math.Max(1, Snap((k.Width - unitWidth) / pitch + 1));
            var height = Math.Max(1, Snap((k.Height - unitHeight) / pitch + 1));
            return (Key: k, X: (k.X + k.Width / 2) / pitch - width / 2, Y: (k.Y + k.Height / 2) / pitch - height / 2, Width: width, Height: height);
        }).ToArray();
        var originX = raw.Min(p => p.X);
        var originY = raw.Min(p => p.Y);
        var placed = raw.Select(p => new KeyPlacement(p.Key, Snap(p.X - originX), Snap(p.Y - originY), p.Width, p.Height)).ToList();

        // Tall keys (numpad + and Enter) line up with the column above them.
        for (var i = 0; i < placed.Count; i++)
        {
            var key = placed[i];
            if (key.Height <= 1) continue;
            var above = placed.Where(p => p != key && p.Y + p.Height <= key.Y + 0.01 && Math.Abs(p.X - key.X) is > 0 and <= 0.25 && Math.Abs(p.Width - key.Width) < 0.01)
                .OrderByDescending(p => p.Y).FirstOrDefault();
            if (above != null) placed[i] = key with { X = above.X };
        }
        return placed;
    }

    public static (double Width, double Height) Size(IReadOnlyList<KeyPlacement> layout) =>
        layout.Count == 0 ? (0, 0) : (layout.Max(p => p.X + p.Width), layout.Max(p => p.Y + p.Height));

    private static double Mode(IEnumerable<double> values) => values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
}
