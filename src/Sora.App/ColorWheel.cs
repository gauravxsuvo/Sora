using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Sora.App;

/// <summary>
/// HSV colour picker: a hue ring around a saturation (left to right) / value (top to bottom) square, like GCC's.
/// Drag on either, or use the keyboard: Left/Right hue, Up/Down brightness, Shift+Left/Right saturation.
/// </summary>
internal sealed class ColorWheel : Grid
{
    private const double RingWidth = 16;
    private readonly Image ring = new() { IsHitTestVisible = false }, square = new() { IsHitTestVisible = false };
    private readonly Ellipse hueMarker = Marker(), squareMarker = Marker();
    private double hue, saturation = 1, value = 1, side;
    private string reportedHex = "#FF0000";
    private enum Drag { None, Hue, Square }
    private Drag drag;

    /// <summary>Raised while the user drags or presses keys, with the new colour as #RRGGBB.</summary>
    public event Action<string>? ColorPicked;

    protected override AutomationPeer OnCreateAutomationPeer() => new ColourPeer(this);
    private sealed class ColourPeer(ColorWheel owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        protected override string GetClassNameCore() => "ColourPicker";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string GetNameCore() => string.IsNullOrEmpty(base.GetNameCore()) ? "Colour picker" : base.GetNameCore();
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Value ? this : base.GetPattern(pattern);
        public bool IsReadOnly => !owner.IsEnabled;
        public string Value => owner.Hex;
        public void SetValue(string value)
        {
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            if (!TryParse(value, out _)) throw new ArgumentException("Use #RRGGBB.", nameof(value));
            owner.Hex = value; owner.Changed();
        }
    }

    public ColorWheel()
    {
        Focusable = true;
        SetResourceReference(FocusVisualStyleProperty, "FocusRing");
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Children.Add(ring);
        Children.Add(square);
        Children.Add(hueMarker);
        Children.Add(squareMarker);
        SizeChanged += (_, _) => Draw();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.35;
        System.Windows.Automation.AutomationProperties.SetHelpText(this, "Left and Right change the hue, Up and Down the brightness, Shift with Left and Right the saturation.");
    }

    /// <summary>The colour as #RRGGBB.</summary>
    public string Hex
    {
        get { var c = FromHsv(hue, saturation, value); return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }
        set
        {
            // Typing a hex that equals the current colour keeps the hue, which grey and black would otherwise lose.
            if (string.Equals(value, Hex, StringComparison.OrdinalIgnoreCase) || !TryParse(value, out var color)) return;
            (var h, saturation, this.value) = ToHsv(color);
            if (saturation > 0 && this.value > 0) hue = h;
            Draw();
            AnnounceValue();
        }
    }

    private static Ellipse Marker() => new()
    {
        Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8 },
    };

    private double Diameter => Math.Min(ActualWidth, ActualHeight);
    private double SquareSide => Math.Floor((Diameter / 2 - RingWidth - 6) * Math.Sqrt(2));

    private void Draw()
    {
        var diameter = Diameter;
        if (diameter < 40) return;
        if (ring.Source is not BitmapSource existing || Math.Abs(existing.PixelWidth - diameter) > 0.5) ring.Source = RingBitmap((int)diameter);
        ring.Width = ring.Height = (int)diameter;
        side = SquareSide;
        square.Source = SquareBitmap((int)side, hue);
        square.Width = square.Height = (int)side;

        var center = diameter / 2;
        var angle = (hue + 180) * Math.PI / 180;
        var radius = center - RingWidth / 2;
        Place(hueMarker, center + radius * Math.Cos(angle), center + radius * Math.Sin(angle), FromHsv(hue, 1, 1));
        var left = center - side / 2;
        Place(squareMarker, left + saturation * side, left + (1 - value) * side, FromHsv(hue, saturation, value));
        Offset(ring, (ActualWidth - diameter) / 2, (ActualHeight - diameter) / 2);
        Offset(square, (ActualWidth - side) / 2, (ActualHeight - side) / 2);
    }

    private void Place(Ellipse marker, double x, double y, Color fill)
    {
        marker.Fill = new SolidColorBrush(fill);
        marker.Margin = new Thickness((ActualWidth - Diameter) / 2 + x - marker.Width / 2, (ActualHeight - Diameter) / 2 + y - marker.Height / 2, 0, 0);
    }

    private static void Offset(FrameworkElement element, double x, double y)
    {
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.VerticalAlignment = VerticalAlignment.Top;
        element.Margin = new Thickness(x, y, 0, 0);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var (x, y) = Local(e);
        var center = Diameter / 2;
        var distance = Math.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
        drag = distance >= center - RingWidth - 4 && distance <= center + 2 ? Drag.Hue
            : Math.Abs(x - center) <= side / 2 + 4 && Math.Abs(y - center) <= side / 2 + 4 ? Drag.Square
            : Drag.None;
        if (drag == Drag.None) return;
        CaptureMouse();
        Track(x, y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag != Drag.None && IsMouseCaptured) { var (x, y) = Local(e); Track(x, y); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        drag = Drag.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); drag = Drag.None; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Left when shift: saturation = Math.Max(0, saturation - 0.05); break;
            case Key.Right when shift: saturation = Math.Min(1, saturation + 0.05); break;
            case Key.Left: hue = (hue + 355) % 360; break;
            case Key.Right: hue = (hue + 5) % 360; break;
            case Key.Up: value = Math.Min(1, value + 0.05); break;
            case Key.Down: value = Math.Max(0, value - 0.05); break;
            default: return;
        }
        e.Handled = true;
        Changed();
    }

    private (double, double) Local(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        return (point.X - (ActualWidth - Diameter) / 2, point.Y - (ActualHeight - Diameter) / 2);
    }

    private void Track(double x, double y)
    {
        var center = Diameter / 2;
        if (drag == Drag.Hue) hue = (Math.Atan2(y - center, x - center) * 180 / Math.PI + 180 + 360) % 360;
        else
        {
            saturation = Math.Clamp((x - (center - side / 2)) / side, 0, 1);
            value = Math.Clamp(1 - (y - (center - side / 2)) / side, 0, 1);
        }
        Changed();
    }

    private void Changed()
    {
        Draw();
        AnnounceValue();
        ColorPicked?.Invoke(Hex);
    }

    private void AnnounceValue()
    {
        var current = Hex;
        if (current == reportedHex) return;
        var previous = reportedHex; reportedHex = current;
        UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous, current);
    }

    /// <summary>Hue ring, red on the left, going clockwise through yellow, green, cyan, blue and magenta. Edges are anti-aliased.</summary>
    private static BitmapSource RingBitmap(int size)
    {
        var pixels = new byte[size * size * 4];
        var center = size / 2.0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                double dx = x + 0.5 - center, dy = y + 0.5 - center, distance = Math.Sqrt(dx * dx + dy * dy);
                var alpha = Math.Clamp(center - distance, 0, 1) * Math.Clamp(distance - (center - RingWidth), 0, 1);
                if (alpha <= 0) continue;
                var color = FromHsv((Math.Atan2(dy, dx) * 180 / Math.PI + 180 + 360) % 360, 1, 1);
                var i = (y * size + x) * 4;
                // Premultiplied BGRA.
                pixels[i] = (byte)(color.B * alpha); pixels[i + 1] = (byte)(color.G * alpha); pixels[i + 2] = (byte)(color.R * alpha); pixels[i + 3] = (byte)(255 * alpha);
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource SquareBitmap(int size, double hue)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var color = FromHsv(hue, x / (double)(size - 1), 1 - y / (double)(size - 1));
                var i = (y * size + x) * 4;
                pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    internal static Color FromHsv(double hue, double saturation, double value)
    {
        var h = (hue % 360 + 360) % 360 / 60;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = value - c;
        var (r, g, b) = (int)h switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromRgb(Byte(r + m), Byte(g + m), Byte(b + m));
    }

    internal static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));
        var hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }

    internal static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (hex is not { Length: 7 } || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
