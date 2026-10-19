using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;

namespace Sora.App;

// Imported from the user's local G HUB installation, never embedded in Sora.exe.
internal sealed record LogitechArtwork(BitmapSource Image, Rect Logo, Rect Dpi, string Source, BitmapSource? SideImage = null, Dictionary<int, Point>? FrontButtons = null, Dictionary<int, Point>? SideButtons = null)
{
    private static readonly Lazy<LogitechArtwork?> shared = new(Load);
    internal static LogitechArtwork? Current => shared.Value;
    private static readonly string Cache = Path.Combine(SoraData.DirectoryPath, "logitech", "artwork");

    private static LogitechArtwork? Load()
    {
        var cached = TryRead(Path.Combine(Cache, "g502-hero.png"), Path.Combine(Cache, "g502-hero.json"), Path.Combine(Cache, "g502-hero-side.png"));
        if (cached?.SideImage != null) return cached;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LGHUB");
            using var current = ReadJson(Path.Combine(root, "current.json"), 8 * 1024 * 1024);
            var build = current.RootElement.GetProperty("buildId").GetString();
            if (string.IsNullOrEmpty(build) || !build.All(char.IsAsciiDigit)) return cached;
            var depot = Path.Combine(root, "depots", build, "g502_hero");
            using var manifest = ReadJson(Path.Combine(depot, "manifest.json"), 1024 * 1024);
            var model = manifest.RootElement.GetProperty("devices").EnumerateArray().Single(d => d.GetProperty("modelId").GetString() == "g502_hero");
            string Resource(string key)
            {
                var relative = model.GetProperty("resources").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key).GetProperty("src").GetString()!;
                var path = Path.GetFullPath(Path.Combine(depot, relative));
                if (!path.StartsWith(Path.GetFullPath(depot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("G HUB resource path leaves its depot.");
                return path;
            }
            var image = Resource("device_image"); var metadata = Resource("image_metadata");
            var side = Resource("device_side");
            var installed = TryRead(image, metadata, side);
            if (installed == null) return cached;
            try
            {
                Directory.CreateDirectory(Cache);
                File.Copy(image, Path.Combine(Cache, "g502-hero.png"), true);
                File.Copy(metadata, Path.Combine(Cache, "g502-hero.json"), true);
                File.Copy(side, Path.Combine(Cache, "g502-hero-side.png"), true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* The installed image still works if the cache cannot be written. */ }
            return installed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException or KeyNotFoundException) { return cached; }
    }

    private static LogitechArtwork? TryRead(string imagePath, string metadataPath, string sidePath)
    {
        try
        {
            if (new FileInfo(imagePath).Length > 8 * 1024 * 1024) return null;
            using var metadata = ReadJson(metadataPath, 1024 * 1024);
            var layout = metadata.RootElement.GetProperty("images").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "device_image");
            var origin = layout.GetProperty("origin"); var width = origin.GetProperty("width").GetInt32(); var height = origin.GetProperty("height").GetInt32();
            if (width is < 1 or > 4096 || height is < 1 or > 4096) return null;
            Rect Zone(string id)
            {
                var zone = layout.GetProperty("zones").EnumerateArray().Single(z => z.GetProperty("id").GetString() == id);
                var rect = zone.GetProperty("components")[0].GetProperty("renderings")[0].GetProperty("rect");
                double Number(string key) => double.Parse(rect.GetProperty(key).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var bounds = new Rect(Number("x"), Number("y"), Number("width"), Number("height"));
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || !new Rect(0, 0, width, height).Contains(bounds)) throw new InvalidDataException("Invalid G HUB lighting region.");
                return bounds;
            }
            var logo = Zone("ZONE_BRANDING"); var dpi = Zone("ZONE_PRIMARY");
            using var stream = File.OpenRead(imagePath);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames[0];
            if (bitmap.PixelWidth != width || bitmap.PixelHeight != height) return null;
            bitmap.Freeze();
            Dictionary<int, Point> Markers(JsonElement image)
            {
                var result = new Dictionary<int, Point>();
                foreach (var entry in image.GetProperty("assignments").EnumerateArray())
                {
                    var match = Regex.Match(entry.GetProperty("slotId").GetString() ?? "", @"_g(\d+)_m1$");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var button) || button is < 1 or > 11) continue;
                    var marker = entry.GetProperty("marker"); result[button - 1] = new Point(marker.GetProperty("x").GetDouble(), marker.GetProperty("y").GetDouble());
                }
                return result;
            }
            BitmapSource? sideBitmap = null; Dictionary<int, Point>? sideMarkers = null;
            if (File.Exists(sidePath) && new FileInfo(sidePath).Length <= 8 * 1024 * 1024)
            {
                var sideLayout = metadata.RootElement.GetProperty("images").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "device_side");
                using var sideStream = File.OpenRead(sidePath); var frame = new PngBitmapDecoder(sideStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var sideOrigin = sideLayout.GetProperty("origin");
                if (frame.PixelWidth == sideOrigin.GetProperty("width").GetInt32() && frame.PixelHeight == sideOrigin.GetProperty("height").GetInt32() && frame.PixelWidth <= 4096 && frame.PixelHeight <= 4096) { frame.Freeze(); sideBitmap = frame; sideMarkers = Markers(sideLayout); }
            }
            return new(bitmap, logo, dpi, imagePath, sideBitmap, Markers(layout), sideMarkers);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException or KeyNotFoundException) { return null; }
    }
    private static JsonDocument ReadJson(string path, int maximum)
    {
        if (new FileInfo(path).Length > maximum) throw new InvalidDataException("G HUB metadata is too large.");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }
}
