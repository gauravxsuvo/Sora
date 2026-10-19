using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sora.App;

public sealed class LogitechDpiScale : FrameworkElement
{
    public int[] Stages { get; set; } = [400, 800, 1600];
    public int CurrentStage { get; set; } = 1;
    public int ShiftDpi { get; set; } = 4000;
    public int? LiveDpi { get; set; }
    public bool LiveShiftHeld { get; set; }
    public int Minimum { get; set; } = 100;
    public int Maximum { get; set; } = 25600;
    public int Step { get; set; } = 50;
    public event Action<int>? StageSelected;
    public event Action<int[]>? StagesChanged;
    private int dragged = -1;
    public LogitechDpiScale() { Height = 112; Focusable = true; ToolTip = "Click a speed to select it. Double-click the track to add. Drag a speed to edit; drag it away to remove."; }
    private double Position(int dpi) => 14 + (ActualWidth - 28) * Math.Log((double)dpi / Minimum) / Math.Log((double)Maximum / Minimum);
    private int Value(double x) => Math.Clamp((int)Math.Round((Minimum * Math.Pow((double)Maximum / Minimum, Math.Clamp((x - 14) / Math.Max(1, ActualWidth - 28), 0, 1)) - Minimum) / Step) * Step + Minimum, Minimum, Maximum);
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 40) return;
        var line = new Pen(TryFindResource("SeparatorBrush") as Brush ?? Brushes.Gray, 2); dc.DrawLine(line, new Point(14, 57), new Point(ActualWidth - 14, 57));
        foreach (var dpi in new[] { Minimum, 400, 800, 1600, 4000, 10000, Maximum }.Distinct().Where(v => v >= Minimum && v <= Maximum))
        {
            var x = Position(dpi); dc.DrawLine(line, new Point(x, 53), new Point(x, 62));
        }
        for (var i = 0; i < Stages.Length; i++)
        {
            var active = LiveDpi is { } live && !LiveShiftHeld && Stages[i] == live;
            var x = Position(Stages[i]); dc.DrawEllipse(TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue, null, new Point(x, 57), active ? 6 : 4, active ? 6 : 4);
            Text(dc, Stages[i].ToString(), x - 12, i % 2 == 0 ? 19 : 1, TryFindResource("TextBrush") as Brush ?? Brushes.Black, 12);
        }
        var shiftX = Position(ShiftDpi); var diamond = new StreamGeometry();
        using (var g = diamond.Open()) { g.BeginFigure(new Point(shiftX, 48), true, true); g.LineTo(new Point(shiftX + 8, 57), true, false); g.LineTo(new Point(shiftX, 66), true, false); g.LineTo(new Point(shiftX - 8, 57), true, false); }
        var warning = TryFindResource("Warn") as Brush ?? Brushes.DarkGoldenrod;
        dc.DrawGeometry(warning, null, diamond); Text(dc, "SHIFT " + ShiftDpi, Math.Clamp(shiftX - 25, 0, ActualWidth - 85), 80, warning, 11);
        if (LiveDpi is not { } selected) return;
        var markerX = Position(Math.Clamp(selected, Minimum, Maximum));
        dc.DrawRoundedRectangle(null, new Pen(LiveShiftHeld ? warning : TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue, 1.5), new Rect(markerX - 10, 46, 20, 22), 3, 3);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); var p = e.GetPosition(this); var closest = Stages.Select((v, i) => (i, d: Math.Abs(Position(v) - p.X))).OrderBy(t => t.d).First();
        if (closest.d <= 13) { CurrentStage = dragged = closest.i; StageSelected?.Invoke(CurrentStage); CaptureMouse(); }
        else if (e.ClickCount == 2 && Stages.Length < 5) { var value = Value(p.X); if (!Stages.Contains(value)) StagesChanged?.Invoke(Stages.Append(value).Order().ToArray()); }
        InvalidateVisual(); base.OnMouseLeftButtonDown(e);
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragged >= 0)
        {
            var p = e.GetPosition(this); var next = Stages.ToArray();
            if ((p.Y < -15 || p.Y > Height + 15) && next.Length > 1) next = next.Where((_, i) => i != dragged).ToArray();
            else next[dragged] = Value(p.X);
            dragged = -1; ReleaseMouseCapture(); StagesChanged?.Invoke(next.Distinct().Order().ToArray());
        }
        base.OnMouseLeftButtonUp(e);
    }
    private void Text(DrawingContext dc, string text, double x, double y, Brush brush, int size) => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface((FontFamily)FindResource("UIFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
}
