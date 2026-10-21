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
    private int selected;
    private int hover = -1;
    public FanCurveChart()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        System.Windows.Automation.AutomationProperties.SetName(this, "Fan curve editor. Click empty plot space to add a point. Drag or use arrow keys to adjust it. Space selects the next point; Delete removes it. Shift moves by five.");
    }
    public event Action<int, FanPoint>? PointMoved;
    public event Action<int, FanPoint>? PointAdded;
    public event Action<int>? PointRemoved;
    public event Action<string>? EditRejected;
    public FanProfile? Profile { get => profile; set { profile = value; selected = Math.Clamp(selected, 0, Math.Max(0, (value?.Points?.Length ?? 0) - 1)); InvalidateVisual(); } }
    private double? temperature;
    public double? Temperature { get => temperature; set { if (temperature == value) return; temperature = value; InvalidateVisual(); } }
    private const double LeftPad = 48, RightPad = 24, TopPad = 36, BottomPad = 34;
    private Point Position(double t, double duty) => new(LeftPad + t / 100 * Math.Max(1, ActualWidth - LeftPad - RightPad), TopPad + (100 - duty) / 100 * Math.Max(1, ActualHeight - TopPad - BottomPad));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var muted = TryFindResource("TertiaryTextBrush") as Brush ?? Brushes.Gray;
        var grid = new Pen(TryFindResource("SeparatorBrush") as Brush ?? Brushes.LightGray, 1);
        var accent = TryFindResource("ChartBrush") as Brush ?? Brushes.SeaGreen;
        var surface = TryFindResource("InsetBrush") as Brush ?? Brushes.WhiteSmoke;
        var plot = new Rect(Position(0, 100), Position(100, 0));
        dc.DrawRoundedRectangle(surface, null, plot, 8, 8);
        void Label(string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface((FontFamily)FindResource("UIFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        for (int p = 0; p <= 100; p += 25)
        {
            var a = Position(0, p); var b = Position(100, p); dc.DrawLine(grid, a, b); Label(p + "%", 0, a.Y - 7);
        }
        for (int t = 0; t <= 100; t += 20) { var p = Position(t, 0); dc.DrawLine(grid, Position(t, 0), Position(t, 100)); Label(t + "°", p.X - 7, p.Y + 9); }
        Label("FAN DUTY (%)", LeftPad, 5);
        Label("TEMPERATURE (°C)", Math.Max(LeftPad, ActualWidth - 132), ActualHeight - 13);
        if (profile == null) return;
        var points = profile.Mode == FanCurveMode.Fixed ? new[] { new FanPoint(0, profile.FixedDutyPercent), new FanPoint(profile.FullSpeedC, profile.FixedDutyPercent), new FanPoint(profile.FullSpeedC, 100) } : profile.Points;
        if (points.Length == 0) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Position(0, profile.StopBelowC > 0 ? 0 : points[0].DutyPercent), false, false);
            if (profile.StopBelowC > 0)
            {
                ctx.LineTo(Position(profile.StopBelowC, 0), true, false);
                ctx.LineTo(Position(profile.StopBelowC, points[0].DutyPercent), true, false);
            }
            FanPoint? previous = null;
            foreach (var p in points)
            {
                if (previous != null && profile.Mode == FanCurveMode.Step) ctx.LineTo(Position(p.TemperatureC, previous.DutyPercent), true, false);
                ctx.LineTo(Position(p.TemperatureC, p.DutyPercent), true, false); previous = p;
            }
            ctx.LineTo(Position(100, 100), true, false);
        }
        var fill = PathGeometry.CreateFromGeometry(geometry);
        var area = fill.Figures[0];
        area.Segments.Add(new LineSegment(Position(100, 0), true));
        area.Segments.Add(new LineSegment(Position(0, 0), true));
        area.IsClosed = true; area.IsFilled = true;
        dc.PushOpacity(.12); dc.DrawGeometry(accent, null, fill); dc.Pop();
        dc.DrawGeometry(null, new Pen(accent, 2.5), geometry);
        if (profile.Mode != FanCurveMode.Fixed) for (var i = 0; i < points.Length; i++)
        {
            var p = points[i]; var active = i == hover || i == drag || IsKeyboardFocused && i == selected;
            if (active) { dc.PushOpacity(.2); dc.DrawEllipse(accent, null, Position(p.TemperatureC, p.DutyPercent), 12, 12); dc.Pop(); }
            dc.DrawEllipse(surface, new Pen(accent, active ? 3 : 2), Position(p.TemperatureC, p.DutyPercent), active ? 6 : 4.5, active ? 6 : 4.5);
        }
        var detail = drag >= 0 ? drag : hover >= 0 ? hover : IsKeyboardFocused ? selected : -1;
        if (detail >= 0 && detail < points.Length && profile.Mode != FanCurveMode.Fixed)
            Label($"Point {detail + 1} · {points[detail].TemperatureC}°C / {points[detail].DutyPercent:0.#}%", Math.Max(LeftPad, ActualWidth / 2 - 75), 5);
        if (Temperature is { } temperature && double.IsFinite(temperature))
        {
            temperature = Math.Clamp(temperature, 0, 100);
            var marker = new Pen(muted, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(marker, Position(temperature, 0), Position(temperature, 100));
            Label($"{temperature:0}°C now", Math.Clamp(Position(temperature, 0).X + 5, LeftPad, Math.Max(LeftPad, ActualWidth - 80)), TopPad + 6);
        }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        drag = HitOrInsertPoint(e.GetPosition(this));
        if (drag >= 0) { selected = drag; Focus(); CaptureMouse(); InvalidateVisual(); e.Handled = true; }
    }
    // Also used by the real editor regression checks; only the parent owns the draft/table.
    internal int HitOrInsertPoint(Point mouse)
    {
        if (profile == null || profile.Mode == FanCurveMode.Fixed || profile.Points.Length < 2) return -1;
        if (profile.Points.Any(p => p.TemperatureC is < 1 or > 95 || !double.IsFinite(p.DutyPercent) || p.DutyPercent is < 0 or > 100)
            || profile.Points.Zip(profile.Points.Skip(1), (a, b) => a.TemperatureC >= b.TemperatureC || a.DutyPercent > b.DutyPercent).Any(invalid => invalid))
        {
            EditRejected?.Invoke("Fix the temperature order and fan duty values in the table before editing the chart."); return -1;
        }
        var hit = Array.FindIndex(profile.Points, p => (Position(p.TemperatureC, p.DutyPercent) - mouse).Length < 12);
        if (hit >= 0) return selected = hit;
        if (!new Rect(Position(0, 100), Position(100, 0)).Contains(mouse)) return -1;
        if (profile.StopBelowC >= 95) { EditRejected?.Invoke("Set a stop temperature below 95°C before adding points."); return -1; }
        int t = Math.Clamp((int)Math.Round((mouse.X - LeftPad) / Math.Max(1, ActualWidth - LeftPad - RightPad) * 100), Math.Max(1, profile.StopBelowC + 1), 95);
        double duty = Math.Clamp(Math.Round(100 - (mouse.Y - TopPad) / Math.Max(1, ActualHeight - TopPad - BottomPad) * 100), 0, 100);
        int at = Array.FindIndex(profile.Points, p => p.TemperatureC >= t);
        if (at >= 0 && profile.Points[at].TemperatureC == t)
        {
            MovePoint(at, t, duty); return selected = at;
        }
        if (profile.Points.Length >= 11) { EditRejected?.Invoke("This controller supports 11 points. Remove a point before adding another."); return -1; }
        if (at < 0) at = profile.Points.Length;
        double low = at == 0 ? 0 : profile.Points[at - 1].DutyPercent;
        double high = at == profile.Points.Length ? 100 : profile.Points[at].DutyPercent;
        var point = new FanPoint(t, Math.Clamp(duty, low, high));
        int count = profile.Points.Length;
        PointAdded?.Invoke(at, point);
        // A locked/invalid editor may refuse the edit. Never start dragging another point in that case.
        return profile.Points.Length == count + 1 && profile.Points[at] == point ? selected = at : -1;
    }
    internal void RemoveSelectedPoint()
    {
        if (profile == null || profile.Mode == FanCurveMode.Fixed) return;
        if (profile.Points.Length <= 2) { EditRejected?.Invoke("Keep at least two curve points."); return; }
        if (selected == profile.Points.Length - 1) { EditRejected?.Invoke("Keep the final 100% point; drag it to change the full-speed temperature."); return; }
        PointRemoved?.Invoke(selected);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (profile == null || profile.Mode == FanCurveMode.Fixed) return;
        var mouse = e.GetPosition(this);
        hover = Array.FindIndex(profile.Points, p => (Position(p.TemperatureC, p.DutyPercent) - mouse).Length < 14);
        Cursor = hover >= 0 || drag >= 0 ? Cursors.SizeAll : Cursors.Cross;
        InvalidateVisual();
        if (drag < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        MovePoint(drag, (int)Math.Round((mouse.X - LeftPad) / Math.Max(1, ActualWidth - LeftPad - RightPad) * 100), Math.Round(100 - (mouse.Y - TopPad) / Math.Max(1, ActualHeight - TopPad - BottomPad) * 100));
    }
    private void MovePoint(int index, int requestedTemperature, double requestedDuty)
    {
        if (profile == null || index < 0 || index >= profile.Points.Length) return;
        int minT = index == 0 ? Math.Max(1, profile.StopBelowC + 1) : profile.Points[index - 1].TemperatureC + 1;
        int maxT = index == profile.Points.Length - 1 ? 95 : profile.Points[index + 1].TemperatureC - 1;
        if (index == profile.Points.Length - 1) minT = Math.Max(50, minT);
        if (maxT < minT) return;
        int t = Math.Clamp(requestedTemperature, minT, maxT);
        double low = index == 0 ? 0 : profile.Points[index - 1].DutyPercent;
        double high = index == profile.Points.Length - 1 ? 100 : profile.Points[index + 1].DutyPercent;
        if (high < low) return;
        double duty = index == profile.Points.Length - 1 ? 100 : Math.Clamp(requestedDuty, low, high);
        if (profile.Points[index] != new FanPoint(t, duty)) PointMoved?.Invoke(index, new(t, duty));
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (profile == null || profile.Mode == FanCurveMode.Fixed || profile.Points.Length == 0) return;
        var p = profile.Points[selected]; var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
        switch (e.Key)
        {
            case Key.Space: selected = (selected + 1) % profile.Points.Length; break;
            case Key.Left: MovePoint(selected, p.TemperatureC - step, p.DutyPercent); break;
            case Key.Right: MovePoint(selected, p.TemperatureC + step, p.DutyPercent); break;
            case Key.Up: MovePoint(selected, p.TemperatureC, p.DutyPercent + step); break;
            case Key.Down: MovePoint(selected, p.TemperatureC, p.DutyPercent - step); break;
            case Key.Delete: case Key.Back: RemoveSelectedPoint(); break;
            default: return;
        }
        e.Handled = true; InvalidateVisual();
    }
    protected override void OnMouseLeave(MouseEventArgs e) { hover = -1; InvalidateVisual(); base.OnMouseLeave(e); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { drag = -1; ReleaseMouseCapture(); InvalidateVisual(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { drag = -1; base.OnLostMouseCapture(e); }
}
