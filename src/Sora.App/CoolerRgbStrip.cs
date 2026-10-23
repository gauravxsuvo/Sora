using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sora.App;

// Split the existing photograph into a neutral product and an emissive mask once.
// Updating a color then needs only a brush redraw; the glass, casing and alpha never animate.
internal sealed class CoolerRgbStrip : FrameworkElement
{
    private readonly ImageBrush mask;
    private CoolerLighting look = new(null);
    private Color color = Colors.Black;
    private double seconds;
    private static readonly Geometry[] wedges = MakeWedges();
    internal BitmapSource NeutralProduct { get; }

    internal CoolerRgbStrip(BitmapSource source)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = image.PixelWidth * 4;
        var product = new byte[stride * image.PixelHeight]; image.CopyPixels(product, stride, 0);
        var emission = new byte[product.Length];
        for (int i = 0; i < product.Length; i += 4)
        {
            int min = Math.Min(product[i], Math.Min(product[i + 1], product[i + 2]));
            int max = Math.Max(product[i], Math.Max(product[i + 1], product[i + 2]));
            // Neutral glass reflections and highlights stay in the product layer. Smooth
            // feathering includes the RGB glow without a hard outline or a second strip.
            double weight = Math.Clamp((max - min - 10) / 24d, 0, 1);
            if (weight <= 0) continue;
            double unlit = min * .15 + 3;
            for (int channel = 0; channel < 3; channel++)
                product[i + channel] = (byte)Math.Round(product[i + channel] * (1 - weight) + unlit * weight);
            emission[i] = emission[i + 1] = emission[i + 2] = 255;
            emission[i + 3] = (byte)Math.Round(weight * max * product[i + 3] / 255d);
        }
        NeutralProduct = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null, product, stride);
        NeutralProduct.Freeze();
        var alpha = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null, emission, stride); alpha.Freeze();
        mask = new ImageBrush(alpha) { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 550, 550) }; mask.Freeze();
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }
    internal void SetLighting(CoolerLighting next, double elapsed)
    {
        var nextColor = next.ColorAt(elapsed);
        bool changed = color != nextColor || next.SpatialRainbow != look.SpatialRainbow
            || next.SpatialRainbow && next.BoardFrame == null && elapsed != seconds;
        look = next; seconds = elapsed; color = nextColor;
        if (changed) InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, 550, 550)));
        dc.PushOpacityMask(mask);
        dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 550, 550));
        if (look.SpatialRainbow && look.BoardFrame == null)
        {
            const double radius = 240;
            for (int i = 0; i < wedges.Length; i++)
            {
                double a = i * 2 * Math.PI / wedges.Length, b = (i + 1) * 2 * Math.PI / wedges.Length;
                var brush = new LinearGradientBrush(look.ColorAt(seconds, i / (double)wedges.Length), look.ColorAt(seconds, (i + 1d) / wedges.Length),
                    new Point(275 + Math.Cos(a) * radius, 275 + Math.Sin(a) * radius),
                    new Point(275 + Math.Cos(b) * radius, 275 + Math.Sin(b) * radius)) { MappingMode = BrushMappingMode.Absolute };
                dc.DrawGeometry(brush, null, wedges[i]);
            }
        }
        dc.Pop();
        dc.Pop();
    }
    private static Geometry[] MakeWedges()
    {
        var result = new Geometry[96];
        for (int i = 0; i < result.Length; i++)
        {
            // Slight overlap prevents antialiasing cracks between adjacent color wedges.
            double a = i * 2 * Math.PI / result.Length - .004, b = (i + 1) * 2 * Math.PI / result.Length + .004;
            var wedge = new StreamGeometry();
            using (var ctx = wedge.Open())
            {
                ctx.BeginFigure(new Point(275, 275), true, true);
                ctx.LineTo(new Point(275 + Math.Cos(a) * 800, 275 + Math.Sin(a) * 800), true, false);
                ctx.LineTo(new Point(275 + Math.Cos(b) * 800, 275 + Math.Sin(b) * 800), true, false);
            }
            wedge.Freeze(); result[i] = wedge;
        }
        return result;
    }
}
