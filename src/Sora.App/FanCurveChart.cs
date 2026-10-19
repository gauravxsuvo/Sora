using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sora.Core;

namespace Sora.App;

public sealed class FanCurveChart : FrameworkElement
{
    private FanProfile? profile;
    private int drag = -1;
    public event Action<int, FanPoint>? PointMoved;
    public FanProfile? Profile { get => profile; set { profile = value; InvalidateVisual(); } }
    private double? temperature;
    public double? Temperature { get => temperature; set { if (temperature == value) return; temperature = value; InvalidateVisual(); } }
    private const double LeftPad = 35, RightPad = 12, TopPad = 12, BottomPad = 25;
    private Point Position(double t, double duty) => new(LeftPad + t / 100 * Math.Max(1, ActualWidth - LeftPad - RightPad), TopPad + (100 - duty) / 100 * Math.Max(1, ActualHeight - TopPad - BottomPad));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var muted = TryFindResource("TertiaryTextBrush") as Brush ?? Brushes.Gray;
        var grid = new Pen(TryFindResource("SeparatorBrush") as Brush ?? Brushes.LightGray, 1);
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        void Label(string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface((FontFamily)FindResource("UIFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        for (int p = 0; p <= 100; p += 25)
        {
            var a = Position(0, p); var b = Position(100, p); dc.DrawLine(grid, a, b); Label(p + "%", 0, a.Y - 7);
        }
        for (int t = 0; t <= 100; t += 20) { var p = Position(t, 0); Label(t + "°", p.X - 7, p.Y + 7); }
        if (profile == null) return;
        var points = profile.Mode == FanCurveMode.Fixed ? new[] { new FanPoint(0, profile.FixedDutyPercent), new FanPoint(profile.FullSpeedC, profile.FixedDutyPercent), new FanPoint(profile.FullSpeedC, 100) } : profile.Points;
        if (points.Length == 0) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Position(0, points[0].DutyPercent), false, false);
            FanPoint? previous = null;
            foreach (var p in points)
            {
                if (previous != null && profile.Mode == FanCurveMode.Step) ctx.LineTo(Position(p.TemperatureC, previous.DutyPercent), true, false);
                ctx.LineTo(Position(p.TemperatureC, p.DutyPercent), true, false); previous = p;
            }
            ctx.LineTo(Position(100, 100), true, false);
        }
        dc.DrawGeometry(null, new Pen(accent, 2), geometry);
        if (profile.Mode != FanCurveMode.Fixed) foreach (var p in points) dc.DrawEllipse(accent, null, Position(p.TemperatureC, p.DutyPercent), 4, 4);
        if (Temperature is { } temperature)
        {
            var marker = new Pen(TryFindResource("Good") as Brush ?? Brushes.SeaGreen, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(marker, Position(temperature, 0), Position(temperature, 100));
            Label($"{temperature:0}°C now", Math.Clamp(Position(temperature, 0).X + 5, LeftPad, Math.Max(LeftPad, ActualWidth - 70)), TopPad);
        }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (profile == null || profile.Mode == FanCurveMode.Fixed) return;
        var mouse = e.GetPosition(this);
        drag = Array.FindIndex(profile.Points, p => (Position(p.TemperatureC, p.DutyPercent) - mouse).Length < 12);
        if (drag >= 0) { CaptureMouse(); e.Handled = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag < 0 || profile == null || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        int minT = drag == 0 ? Math.Max(1, profile.StopBelowC + 1) : profile.Points[drag - 1].TemperatureC + 1;
        int maxT = drag == profile.Points.Length - 1 ? 95 : profile.Points[drag + 1].TemperatureC - 1;
        if (maxT < minT) return;
        int t = Math.Clamp((int)Math.Round((mouse.X - LeftPad) / Math.Max(1, ActualWidth - LeftPad - RightPad) * 100), minT, maxT);
        double low = drag == 0 ? 0 : profile.Points[drag - 1].DutyPercent;
        double high = drag == profile.Points.Length - 1 ? 100 : profile.Points[drag + 1].DutyPercent;
        double duty = drag == profile.Points.Length - 1 ? 100 : Math.Clamp(Math.Round(100 - (mouse.Y - TopPad) / Math.Max(1, ActualHeight - TopPad - BottomPad) * 100), low, high);
        if (drag == profile.Points.Length - 1) t = Math.Max(50, t);
        PointMoved?.Invoke(drag, new(t, duty));
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { drag = -1; ReleaseMouseCapture(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { drag = -1; base.OnLostMouseCapture(e); }
}
