using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Sora.App;

public sealed class LogitechMouseView : FrameworkElement
{
    private readonly LogitechArtwork? artwork = LogitechArtwork.Current;
    public event Action<int>? ZoneSelected;
    public event Action<int>? ButtonSelected;
    public bool ShowAssignments { get; set; }
    public bool SideView { get; set; }
    public int SelectedButton { get; set; }
    public ushort PressedButtons { get; set; }
    public string[] AssignmentNames { get; set; } = [];
    public Color LogoColor { get; set; } = Color.FromRgb(83, 208, 239);
    public Color DpiColor { get; set; } = Color.FromRgb(83, 208, 239);
    public int SelectedZone { get; set; } = 1;
    public bool LinkedZones { get; set; }
    public bool Connected { get; set; }
    internal bool HasGHubArtwork => artwork != null;
    public LogitechMouseView()
    {
        Width = 290; Height = 410; Cursor = Cursors.Hand;
        ToolTip = artwork == null ? "Use the button or lighting-zone selector. Mouse controls work without G HUB or a device image." : "Cached device image · select the logo or DPI indicator";
    }
    private Rect ImageBounds
    {
        get
        {
            if (artwork == null) return Rect.Empty;
            var bitmap = SideView && artwork.SideImage != null ? artwork.SideImage : artwork.Image;
            var height = Height - 16; var width = height * bitmap.PixelWidth / bitmap.PixelHeight;
            return new Rect((Width - width) / 2, 8, width, height);
        }
    }
    private Rect OnImage(Rect region)
    {
        var image = ImageBounds; var scale = image.Width / artwork!.Image.PixelWidth;
        return new Rect(image.X + region.X * scale, image.Y + region.Y * scale, region.Width * scale, region.Height * scale);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (LinkedZones && !ShowAssignments) { e.Handled = true; return; }
        if (artwork != null)
        {
            if (ShowAssignments)
            {
                SelectButtonAt(e.GetPosition(this));
                e.Handled = true; return;
            }
            var point = e.GetPosition(this); var logo = OnImage(artwork.Logo); var dpi = OnImage(artwork.Dpi);
            Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            SelectedZone = (point - Center(dpi)).LengthSquared < (point - Center(logo)).LengthSquared ? 0 : 1;
            ZoneSelected?.Invoke(SelectedZone); InvalidateVisual();
        }
        base.OnMouseLeftButtonDown(e);
    }
    internal void SelectButtonAt(Point click)
    {
        if (artwork == null) return;
        var markers = SideView ? artwork.SideButtons : artwork.FrontButtons;
        if (markers?.Count is not > 0) return;
        var bounds = ImageBounds; var bitmap = SideView && artwork.SideImage != null ? artwork.SideImage : artwork.Image; var scale = bounds.Width / bitmap.PixelWidth;
        SelectedButton = markers.OrderBy(m => (click - new Point(bounds.X + m.Value.X * scale, bounds.Y + m.Value.Y * scale)).LengthSquared).First().Key;
        ButtonSelected?.Invoke(SelectedButton); InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (artwork == null)
        {
            Label(dc, "G502 HERO", new Point(86, 174), 18, Colors.LightGray);
            Label(dc, "Device image unavailable", new Point(61, 204), 13, Colors.Gray);
            return;
        }
        var logo = OnImage(artwork.Logo); var dpi = OnImage(artwork.Dpi);
        // G HUB's PNG has transparent LED cutouts. Its own metadata supplies the
        // backing rectangles; the opaque housing masks everything except the LEDs.
        if (!SideView)
        {
            dc.DrawRectangle(new SolidColorBrush(Connected ? LogoColor : Colors.DimGray), null, logo);
            dc.DrawRectangle(new SolidColorBrush(Connected ? DpiColor : Colors.DimGray), null, dpi);
        }
        dc.DrawImage(SideView && artwork.SideImage != null ? artwork.SideImage : artwork.Image, ImageBounds);
        if (ShowAssignments)
        {
            var markers = SideView ? artwork.SideButtons : artwork.FrontButtons;
            var bitmap = SideView && artwork.SideImage != null ? artwork.SideImage : artwork.Image;
            var bounds = ImageBounds; var scale = bounds.Width / bitmap.PixelWidth;
            if (markers != null) foreach (var (index, marker) in markers)
            {
                var point = new Point(bounds.X + marker.X * scale, bounds.Y + marker.Y * scale);
                var brush = (PressedButtons & 1 << index) != 0 ? Brushes.Gold : index == SelectedButton ? Brushes.DodgerBlue : Brushes.White;
                dc.DrawEllipse(brush, new Pen(Brushes.Black, 1), point, index == SelectedButton ? 6 : 4, index == SelectedButton ? 6 : 4);
                var left = SideView || index is 0 or 7 or 10 or 6;
                var group = markers.Keys.Where(i => (SideView || i is 0 or 7 or 10 or 6) == left).OrderBy(i => markers[i].Y).ToArray();
                var row = Array.IndexOf(group, index); var y = 100 + row * 48;
                var edge = left ? 142d : Width - 142;
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 105, 110)), 0.7);
                var end = new Point(edge, y);
                dc.DrawLine(pen, point, new Point(edge + (left ? 14 : -14), y));
                dc.DrawLine(pen, end, new Point(left ? 4 : Width - 4, y));
                // Markers sit over the dark mouse artwork; their labels sit over
                // the light application surface and need the shared text palette.
                var labelColor = ((SolidColorBrush)FindResource(index == SelectedButton ? "AccentTextBrush" : "TextBrush")).Color;
                // Full, wrapping assignment names live in the selectable legend.
                // Keep artwork annotations short so neither side can clip a label.
                Label(dc, $"G{index + 1}", new Point(left ? 4 : edge + 2, y - 20), 16, labelColor);
            }
            return;
        }
        var selected = SelectedZone == 0 ? dpi : logo;
        if (LinkedZones)
        {
            var outline = new Pen(new SolidColorBrush(Color.FromArgb(150, 80, 209, 244)), 1);
            foreach (var region in new[] { dpi, logo })
            {
                var bounds = region; bounds.Inflate(6, 6);
                dc.DrawRoundedRectangle(null, outline, bounds, 4, 4);
            }
            return;
        }
        selected.Inflate(6, 6);
        var cyan = new SolidColorBrush(Color.FromArgb(150, 80, 209, 244));
        dc.DrawRoundedRectangle(null, new Pen(cyan, 0.8), selected, 5, 5);
        var start = new Point(selected.Right, selected.Y + selected.Height / 2);
        dc.DrawLine(new Pen(cyan, 0.8), start, new Point(Width - 4, start.Y));
        Label(dc, SelectedZone == 0 ? "DPI" : "LOGO", new Point(Width - 37, start.Y - 17), 10, Color.FromRgb(123, 209, 230));
    }
    private void Label(DrawingContext dc, string text, Point point, int size, Color color) => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface((FontFamily)FindResource("UIFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
}
