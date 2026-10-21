using System;
using System.Windows;
using System.Windows.Media;

namespace Sora.App;

// Paint the circle and symbol around one centre. Independent WPF elements can
// round their half-pixel offsets differently at Windows' fractional DPI scales.
public sealed class TrafficLightVisual : FrameworkElement
{
    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(nameof(DotBrush), typeof(Brush), typeof(TrafficLightVisual), new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(nameof(Symbol), typeof(Geometry), typeof(TrafficLightVisual), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowSymbolProperty = DependencyProperty.Register(nameof(ShowSymbol), typeof(bool), typeof(TrafficLightVisual), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush DotBrush { get => (Brush)GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }
    public Geometry? Symbol { get => (Geometry?)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public bool ShowSymbol { get => (bool)GetValue(ShowSymbolProperty); set => SetValue(ShowSymbolProperty, value); }
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
    internal const string CloseSymbol = "M4.1,2.9 L7,5.8 L9.9,2.9 L11.1,4.1 L8.2,7 L11.1,9.9 L9.9,11.1 L7,8.2 L4.1,11.1 L2.9,9.9 L5.8,7 L2.9,4.1 Z";
    internal const string MinimizeSymbol = "M3,6 L11,6 L11,8 L3,8 Z";
    internal const string MaximizeSymbol = "M3,3 L8,3 L3,8 Z M11,11 L6,11 L11,6 Z";
    internal const string RestoreSymbol = "M2.8,6.2 L6.2,2.8 L6.2,6.2 Z M11.2,7.8 L7.8,11.2 L7.8,7.8 Z";

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var centre = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        drawing.DrawEllipse(DotBrush, null, centre, 7, 7);
        if (!ShowSymbol || Symbol == null) return;
        drawing.PushTransform(new TranslateTransform(centre.X - 7, centre.Y - 7));
        drawing.DrawGeometry(Ink, null, Symbol);
        drawing.Pop();
    }
}
