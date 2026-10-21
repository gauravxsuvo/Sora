using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sora.App;

internal static class TrafficLightChecks
{
    internal static bool Run(string output)
    {
        var evidence = new List<object>();
        var checks = new Dictionary<string, bool>();
        var sheet = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(sheet, BitmapScalingMode.NearestNeighbor);
        using (var drawing = sheet.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 950, 600));
            void Label(string text, double x, double y) => drawing.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 16, Brushes.White, 1), new Point(x, y));
            Label("Production window buttons · enlarged pixels at Windows display scales", 20, 12);
            var symbols = new[] { ("Close", "#FF5F57", TrafficLightVisual.CloseSymbol), ("Minimize", "#FEBC2E", TrafficLightVisual.MinimizeSymbol), ("Maximize", "#28C840", TrafficLightVisual.MaximizeSymbol), ("Restore", "#28C840", TrafficLightVisual.RestoreSymbol) };
            for (var c = 0; c < symbols.Length; c++) Label(symbols[c].Item1, 145 + c * 195, 44);
            var scales = new[] { 1.0, 1.25, 1.5, 1.75, 2.0 };
            for (var r = 0; r < scales.Length; r++)
            {
                var scale = scales[r]; Label($"{scale * 100:0}%", 20, 101 + r * 100);
                for (var c = 0; c < symbols.Length; c++)
                {
                    var (name, color, symbol) = symbols[c];
                    var button = new Button { Width = 28, Height = 28, Style = (Style)Application.Current.FindResource("TrafficLightButton"), Background = (Brush)new BrushConverter().ConvertFromString(color)!, Tag = symbol };
                    button.Measure(new Size(28, 28)); button.Arrange(new Rect(0, 0, 28, 28)); button.ApplyTemplate(); button.UpdateLayout();
                    var dot = (TrafficLightVisual)button.Template.FindName("Dot", button);
                    if (dot.Symbol == null) throw new InvalidOperationException("Production symbol binding is missing: " + name);
                    RenderTargetBitmap Render(bool show)
                    {
                        dot.ShowSymbol = show;
                        dot.InvalidateVisual(); dot.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)(28 * scale), (int)(28 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(button); return bitmap;
                    }
                    var off = Render(false); var on = Render(true);
                    var stride = on.PixelWidth * 4; var a = new byte[stride * on.PixelHeight]; var b = new byte[a.Length];
                    off.CopyPixels(a, stride, 0); on.CopyPixels(b, stride, 0);
                    double dotWeight = 0, dotX = 0, dotY = 0, inkWeight = 0, inkX = 0, inkY = 0;
                    for (var y = 0; y < on.PixelHeight; y++) for (var x = 0; x < on.PixelWidth; x++)
                    {
                        var i = y * stride + x * 4; var alpha = a[i + 3];
                        dotWeight += alpha; dotX += (x + 0.5) * alpha; dotY += (y + 0.5) * alpha;
                        var weight = Math.Max(0, a[i] + a[i + 1] + a[i + 2] - b[i] - b[i + 1] - b[i + 2]);
                        inkWeight += weight; inkX += (x + 0.5) * weight; inkY += (y + 0.5) * weight;
                    }
                    var dx = inkWeight > 0 ? inkX / inkWeight - dotX / dotWeight : 999; var dy = inkWeight > 0 ? inkY / inkWeight - dotY / dotWeight : 999;
                    checks[$"{name} visible and centered at {scale * 100:0}%"] = inkWeight > 0 && Math.Abs(dx) < 0.18 && Math.Abs(dy) < 0.18;
                    evidence.Add(new { name, scale, centerOffsetX = dx, centerOffsetY = dy, inkWeight });
                    drawing.DrawImage(on, new Rect(115 + c * 195, 74 + r * 100, 84, 84));
                }
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var result = new RenderTargetBitmap(950, 600, 96, 96, PixelFormats.Pbgra32); result.Render(sheet);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result)); using (var file = File.Create(output)) encoder.Save(file);
        var passed = checks.Values.All(v => v);
        File.WriteAllText(output + ".checks.json", JsonSerializer.Serialize(new { passed, checks, evidence }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }
}
