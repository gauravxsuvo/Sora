using System;
using System.Linq;
using System.Windows.Media;
using Sora.Core;

namespace Sora.App;

// Read-only description of the cooler's physical lighting connection. On the qualified
// Z890 board, ARGB header 2 is controller zone 6; zone 2 is the SSD heatsink.
internal sealed record CoolerLighting(BoardZoneLighting? Header, bool SpatialRainbow = false, Color? BoardFrame = null, bool FrameRunning = true)
{
    internal const int HeaderZone = 6;
    internal static CoolerLighting From(LightingSettings settings, Color? frame = null, bool frameRunning = true)
    {
        var all = settings.BoardZones?.OfType<BoardZoneLighting>().ToArray();
        var header = all?.FirstOrDefault(z => z.Zone == HeaderZone);
        if (header != null && (!Enum.IsDefined(header.Effect) || header.Brightness is < 0 or > 8 || header.Speed is < 0 or > 9
            || !ColorWheel.TryParse(header.Color, out _))) header = null;
        bool rainbow = header != null && all is { Length: > 0 }
            && GigabyteRgb.CanUseGlobalRainbow(all.Select(z => new BoardZoneEffect(z.Zone, z.Effect, 0, 0, 0, z.Brightness, z.Speed)).ToArray(), GigabyteRgb.ZonesFor(GigabyteFans.SupportedBoard));
        return new(header, rainbow, frame, frameRunning);
    }
    internal string Caption => BoardFrame != null ? FrameRunning ? "ARGB header 2 · synced" : "ARGB header 2 · last sent"
        : Header == null ? "ARGB header 2 · no saved look"
        : $"ARGB header 2 · {Header.Effect switch { BoardEffect.ColorCycle => "Color cycle", BoardEffect.DoubleFlash => "Double flash", BoardEffect.Wave => "Color cycle", _ => Header.Effect.ToString() }}";
    internal bool Animated => BoardFrame != null ? FrameRunning : Header is { Effect: not BoardEffect.Static and not BoardEffect.Off };

    internal Color ColorAt(double seconds, double position = 0)
    {
        if (BoardFrame is { } live) return live;
        if (Header == null || Header.Effect == BoardEffect.Off) return Colors.Black;
        if (SpatialRainbow)
            return ColorWheel.FromHsv((position + seconds / 3.85) * 360, 1, 1); // GCC's global wave has a fixed pace/peak.
        var effect = SpatialRainbow ? Header.Effect : Header.Effect is BoardEffect.Rainbow or BoardEffect.Wave ? BoardEffect.ColorCycle : Header.Effect;
        var (r, g, b) = LightingService.ParseColor(Header.Color);
        // Reuse the controller's actual brightness levels and speed tables, rather than
        // creating a second set of lighting settings. Built-in animation phase is estimated.
        var packet = GigabyteRgb.BuildEffect(effect, r, g, b, Math.Clamp(Header.Brightness, 0, 8), Math.Clamp(Header.Speed, 0, 9));
        double level = effect == BoardEffect.Pulse ? 1 : packet[10] / 255d;
        double rise = BitConverter.ToUInt16(packet, 20) / 1000d;
        double fall = BitConverter.ToUInt16(packet, 22) / 1000d;
        double pause = BitConverter.ToUInt16(packet, 24) / 1000d;
        switch (effect)
        {
            case BoardEffect.Pulse:
                double t = seconds % (rise + fall + pause);
                level *= t < rise ? (1 - Math.Cos(t / rise * Math.PI)) / 2
                    : t < rise + fall ? (1 + Math.Cos((t - rise) / fall * Math.PI)) / 2 : 0;
                break;
            case BoardEffect.Flash:
                level *= seconds % (rise + fall + pause) < rise ? 1 : 0;
                break;
            case BoardEffect.DoubleFlash:
                double flash = seconds % (2 * (rise + fall) + pause);
                level *= flash < rise || flash >= rise + fall && flash < 2 * rise + fall ? 1 : 0;
                break;
            case BoardEffect.ColorCycle:
                return ColorWheel.FromHsv(seconds / ((rise + fall) * 7) * 360, 1, level);
        }
        return Color.FromRgb((byte)Math.Round(r * level), (byte)Math.Round(g * level), (byte)Math.Round(b * level));
    }
}
