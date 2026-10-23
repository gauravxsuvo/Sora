using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sora.App;

// Product, ARGB strip and telemetry are independent layers.
public sealed class CoolerDisplayPreview : Viewbox
{
    private readonly Ld360Readout screen = new();
    private readonly CoolerRgbStrip strip;
    private readonly DispatcherTimer lightingTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch lightingClock = Stopwatch.StartNew();
    private LightingService? lighting;
    private bool listening;
    private CoolerLighting? lastLook;
    private static readonly DependencyPropertyKey LightingCaptionKey = DependencyProperty.RegisterReadOnly(nameof(LightingCaption), typeof(string), typeof(CoolerDisplayPreview), new PropertyMetadata("ARGB header 2 · no saved look"));
    public static readonly DependencyProperty LightingCaptionProperty = LightingCaptionKey.DependencyProperty;
    public string LightingCaption => (string)GetValue(LightingCaptionProperty);
    internal bool LightingTimerRunning => lightingTimer.IsEnabled;
    internal string TemperatureText => screen.Temperature;
    internal string PowerText => screen.Power;
    internal string UsageText => screen.Usage;
    internal string UnitText => screen.Unit;
    internal BitmapSource ProductImage { get; }

    static CoolerDisplayPreview()
    {
        ControlState.ReduceMotionProperty.OverrideMetadata(typeof(CoolerDisplayPreview), new FrameworkPropertyMetadata(false,
            (d, _) => { if (d is CoolerDisplayPreview { IsLoaded: true } preview) preview.RefreshLighting(); }));
    }

    public CoolerDisplayPreview()
    {
        Stretch = Stretch.Uniform;
        ProductImage = new BitmapImage(new Uri("pack://application:,,,/Sora;component/Assets/Cooler/ld360-display-blank.png"));
        ProductImage.Freeze();
        strip = new CoolerRgbStrip(ProductImage);
        var layers = new Grid { Width = 550, Height = 550, IsHitTestVisible = false };
        var product = new Image { Source = strip.NeutralProduct, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(product, BitmapScalingMode.HighQuality);
        layers.Children.Add(product);
        layers.Children.Add(strip);
        layers.Children.Add(screen);
        Child = layers;
        lightingTimer.Tick += (_, _) => RefreshLighting();
        Loaded += (_, _) => { Listen(); SystemParameters.StaticPropertyChanged += MotionChanged; RefreshLighting(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) screen.BeginAnimation(OpacityProperty, null); RefreshLighting(); };
        Unloaded += (_, _) =>
        {
            screen.BeginAnimation(OpacityProperty, null); lightingTimer.Stop();
            if (listening && lighting != null) lighting.Changed -= LightingChanged;
            listening = false; SystemParameters.StaticPropertyChanged -= MotionChanged;
        };
        AutomationProperties.SetName(this, "LD360 display preview. Waiting for CPU readings.");
    }

    internal void AttachLighting(LightingService service)
    {
        if (listening && lighting != null) lighting.Changed -= LightingChanged;
        listening = false; lighting = service; Listen(); RefreshLighting();
    }
    private void Listen()
    {
        if (!IsLoaded || lighting == null || listening) return;
        lighting.Changed += LightingChanged; listening = true;
    }
    private void LightingChanged() => Dispatcher.InvokeAsync(RefreshLighting);
    private void MotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.InvokeAsync(RefreshLighting);
    }
    internal void RefreshLighting()
    {
        var look = lighting?.CoolerPreviewLighting ?? new CoolerLighting(null);
        if (look.Header != lastLook?.Header || look.SpatialRainbow != lastLook?.SpatialRainbow) lightingClock.Restart();
        lastLook = look;
        bool motion = InteractionMotion.Allowed(this);
        SetLightingPreview(motion ? look : look with { BoardFrame = null }, motion ? lightingClock.Elapsed.TotalSeconds : .6);
        lightingTimer.IsEnabled = IsLoaded && IsVisible && look.Animated && motion;
    }
    internal void SetLightingPreview(CoolerLighting look, double seconds)
    {
        strip.SetLighting(look, seconds);
        SetValue(LightingCaptionKey, look.Caption);
    }

    internal void UpdateReadings(string? temperature, string? power, string? usage, string unit, bool sending)
    {
        // Each physical field has three digits. Missing readings stay visibly unavailable.
        static string Field(string? text) => text is { Length: <= 3 } ? text.PadLeft(3) : "---";
        var next = (Field(temperature), Field(power), Field(usage), unit);
        bool changed = next != (screen.Temperature, screen.Power, screen.Usage, screen.Unit);
        (screen.Temperature, screen.Power, screen.Usage, screen.Unit) = next;
        if (changed)
        {
            screen.InvalidateVisual();
            if (temperature != null && IsVisible && InteractionMotion.Allowed(this))
            {
                InteractionMotion.FadeIn(screen, .65);
            }
            else { screen.BeginAnimation(OpacityProperty, null); screen.Opacity = 1; }
        }
        AutomationProperties.SetName(this, $"LD360 {(sending ? "live display" : "preview")}. CPU temperature {temperature ?? "unavailable"} {unit}; power {power ?? "unavailable"} watts; usage {usage ?? "unavailable"} percent.");
    }

    private sealed class Ld360Readout : FrameworkElement
    {
        internal string Temperature = "---", Power = "---", Usage = "---", Unit = "°C";
        private static readonly int[] masks = [0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f];
        // Clockwise from the top, followed by the middle segment; chamfered like the real LCD.
        private static readonly Point[][] segments =
        [
            [new(8, 0), new(56, 0), new(49, 8), new(15, 8)],
            [new(58, 3), new(58, 41), new(53, 46), new(48, 40), new(48, 13)],
            [new(58, 51), new(58, 89), new(48, 79), new(48, 53), new(53, 47)],
            [new(8, 92), new(56, 92), new(49, 84), new(15, 84)],
            [new(6, 51), new(6, 89), new(16, 79), new(16, 53), new(11, 47)],
            [new(6, 3), new(6, 41), new(11, 46), new(16, 40), new(16, 13)],
            [new(11, 46), new(18, 41), new(46, 41), new(53, 46), new(46, 51), new(18, 51)]
        ];
        private static readonly Geometry[] segmentGeometry = BuildSegments();
        private static Geometry[] BuildSegments()
        {
            var result = new Geometry[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(segments[i][0], true, true);
                    for (int j = 1; j < segments[i].Length; j++) ctx.LineTo(segments[i][j], true, false);
                }
                geometry.Freeze(); result[i] = geometry;
            }
            return result;
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            DrawRow(dc, Power, "POWER", "W", 108);
            DrawRow(dc, Temperature, "CPU TEMP.", Unit, 224);
            DrawRow(dc, Usage, "USAGE", "%", 340);
        }
        private void DrawRow(DrawingContext dc, string value, string label, string unit, double y)
        {
            dc.PushTransform(new RotateTransform(-90, 126, y + 46));
            Text(dc, label, 126, y + 46, 18, true);
            dc.Pop();
            for (int digit = 0; digit < value.Length; digit++)
            {
                char c = value[digit]; int mask = c is >= '0' and <= '9' ? masks[c - '0'] : c == '-' ? 0x40 : 0;
                dc.PushTransform(new TranslateTransform(158 + digit * 72, y));
                for (int segment = 0; segment < 7; segment++)
                    if ((mask & (1 << segment)) != 0) dc.DrawGeometry(Brushes.WhiteSmoke, null, segmentGeometry[segment]);
                dc.Pop();
            }
            Text(dc, unit, 405, y + 46, 23, true);
        }
        private void Text(DrawingContext dc, string text, double centerX, double centerY, double size, bool bold)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                size, Brushes.WhiteSmoke, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(formatted, new Point(centerX - formatted.Width / 2, centerY - formatted.Height / 2));
        }
    }
}
