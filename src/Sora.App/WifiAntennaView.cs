using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Sora.App;

/// <summary>Interactive, user-controlled reference model. Orientation is never inferred from RF measurements.</summary>
public sealed class WifiAntennaView : Grid
{
    private readonly Viewport3D viewport = new();
    private readonly PerspectiveCamera camera = new();
    private readonly AxisAngleRotation3D yaw = new(new Vector3D(0, 1, 0), -25);
    private readonly AxisAngleRotation3D pitch = new(new Vector3D(1, 0, 0), 0);
    private readonly Model3DGroup antenna = new();
    private readonly TextBlock heading = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    // Measured signal by side: a petal on the floor per measured position, long and green where reception was best.
    private readonly Model3DGroup signals = new();
    private readonly ModelVisual3D signalVisual = new();
    private readonly Canvas labels = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly TextBlock legend = new() { FontSize = 11, Margin = new Thickness(12, 0, 12, 9), VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
    private IReadOnlyList<Core.WifiPositionResult> shownResults = [];
    private const double PetalStart = 1.62, PetalSpan = 30;
    private Point dragStart;
    private bool dragging;
    private double startYaw, startPitch, distance = 11.4;
    public event Action<double>? OrientationChanged;

    public WifiAntennaView()
    {
        ClipToBounds = true; MinHeight = 200; Focusable = true;
        SetResourceReference(BackgroundProperty, "InsetBrush");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        legend.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        Children.Add(viewport); Children.Add(labels); Children.Add(heading); Children.Add(legend);
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(100, 110, 125)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(225, 232, 245), new Vector3D(-3, -5, -4)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(70, 125, 210), new Vector3D(4, -1, 2)));
        scene.Children.Add(CreateRing(1.52, 1.54, -0.04, Color.FromRgb(57, 64, 76)));
        scene.Children.Add(CreateRing(1.13, 1.135, -0.045, Color.FromRgb(39, 44, 53)));
        for (var i = 0; i < 4; i++)
        {
            var angle = i * Math.PI / 2;
            scene.Children.Add(CreateBox(new Point3D(Math.Sin(angle) * 1.54, -0.03, Math.Cos(angle) * 1.54), .055, .025, .055, Color.FromRgb(126, 138, 156)));
        }
        BuildAntenna();
        var transforms = new Transform3DGroup(); transforms.Children.Add(new RotateTransform3D(pitch)); transforms.Children.Add(new RotateTransform3D(yaw));
        antenna.Transform = transforms; scene.Children.Add(antenna);
        viewport.Children.Add(new ModelVisual3D { Content = scene });
        signalVisual.Content = signals; viewport.Children.Add(signalVisual);
        SizeChanged += (_, _) => PlaceLabels();
        ShowSignals([]);
        camera.FieldOfView = 40; viewport.Camera = camera; UpdateCamera(); SetPosition("Front");
        Cursor = Cursors.SizeAll;
        ToolTip = "Drag to inspect the antenna · scroll to zoom · double-click to reset the view";
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ResetView(); return; }
            dragStart = e.GetPosition(this); startYaw = yaw.Angle; startPitch = pitch.Angle; dragging = true;
            CaptureMouse(); Focus(); e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var current = e.GetPosition(this);
            yaw.Angle = startYaw + (current.X - dragStart.X) * .5;
            pitch.Angle = Math.Clamp(startPitch - (current.Y - dragStart.Y) * .3, -30, 25);
            UpdateHeading(); OrientationChanged?.Invoke(Normalize(yaw.Angle));
        };
        MouseLeftButtonUp += (_, e) => { dragging = false; ReleaseMouseCapture(); e.Handled = true; };
        LostMouseCapture += (_, _) => dragging = false;
        MouseWheel += (_, e) => { distance = Math.Clamp(distance - e.Delta / 120d * .35, 8.4, 14); UpdateCamera(); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right) { yaw.Angle += e.Key == Key.Left ? -15 : 15; UpdateHeading(); OrientationChanged?.Invoke(Normalize(yaw.Angle)); e.Handled = true; }
            else if (e.Key == Key.Home) { ResetView(); e.Handled = true; }
        };
        System.Windows.Automation.AutomationProperties.SetName(this, "Interactive antenna reference model. Drag or use arrow keys to rotate; Home resets the view.");
    }

    private static double YawOf(string position) => position switch { "Left" => 65, "Back" => 155, "Right" => 245, _ => -25 };

    /// <summary>
    /// Draws the measured positions around the antenna's base, each where the antenna's front faced while it was measured: petal
    /// length is the average signal, colour its quality, and the strongest side gets an arrow and a beacon. Facing the strongest side
    /// usually means the router is roughly that way; walls and reflections can bend it, so it stays a measurement, not a bearing.
    /// </summary>
    public void ShowSignals(IReadOnlyList<Core.WifiPositionResult> results)
    {
        shownResults = results;
        signals.Children.Clear();
        var best = results.Count > 1 ? results.MaxBy(r => r.AverageDbm) : null;
        foreach (var result in results)
        {
            var strength = Core.WifiAlignment.MeterPercent((int)Math.Round(result.AverageDbm)) / 100d;
            var length = Reach(result.AverageDbm);
            var isBest = best != null && result.Position == best.Position;
            var color = QualityColor(result.AverageDbm);
            signals.Children.Add(CreateSector(YawOf(result.Position), PetalStart, PetalStart + length, -.03, color, isBest ? 1 : .55));
            if (!isBest) continue;
            // An arrow from beyond the petal toward the antenna, and a beacon where the signal comes from.
            var angle = YawOf(result.Position) * Math.PI / 180;
            var (dx, dz) = (Math.Sin(angle), Math.Cos(angle));
            var tip = PetalStart + length + .12;
            signals.Children.Add(CreateArrow(new Point3D(dx * (tip + .34), -.02, dz * (tip + .34)), new Point3D(dx * tip, -.02, dz * tip), color));
            signals.Children.Add(CreateBox(new Point3D(dx * (tip + .46), .26, dz * (tip + .46)), .045, .56, .045, color, emissive: true));
            signals.Children.Add(CreateBox(new Point3D(dx * (tip + .46), .58, dz * (tip + .46)), .14, .14, .14, color, emissive: true));
        }
        legend.Text = results.Count == 0 ? "Measure the positions to see where the signal is strongest."
            : best == null ? $"{results[0].Position}: {results[0].AverageDbm:0.0} dBm. Measure another position to compare."
            : $"Strongest {(results.Count < 4 ? "so far " : "")}facing {best.Position.ToLowerInvariant()} · {best.AverageDbm:0.0} dBm ({Core.WifiAlignment.Quality((int)Math.Round(best.AverageDbm))})";
        Dispatcher.BeginInvoke(PlaceLabels, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Names each petal on screen, projected from its outer end.</summary>
    private void PlaceLabels()
    {
        labels.Children.Clear();
        if (shownResults.Count == 0 || ActualWidth < 1 || signalVisual.TransformToAncestor(viewport) is not { } project) return;
        var best = shownResults.Count > 1 ? shownResults.MaxBy(r => r.AverageDbm) : null;
        foreach (var result in shownResults)
        {
            var angle = YawOf(result.Position) * Math.PI / 180;
            // Beside the petal's outer end; the strongest side's name sits on its beacon.
            var isBest = best != null && result.Position == best.Position;
            var reach = PetalStart + Reach(result.AverageDbm) + (isBest ? .58 : .3);
            if (!project.TryTransform(new Point3D(Math.Sin(angle) * reach, isBest ? .9 : 0, Math.Cos(angle) * reach), out var point)) continue;
            var text = new TextBlock { Text = result.Position + " " + result.AverageDbm.ToString("0") , FontSize = 10.5, FontWeight = FontWeights.SemiBold };
            text.Foreground = new SolidColorBrush(QualityColor(result.AverageDbm));
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(text, Math.Clamp(point.X - text.DesiredSize.Width / 2, 4, Math.Max(4, ActualWidth - text.DesiredSize.Width - 4)));
            Canvas.SetTop(text, Math.Clamp(point.Y - text.DesiredSize.Height / 2, 30, Math.Max(30, ActualHeight - text.DesiredSize.Height - 28)));
            labels.Children.Add(text);
        }
    }

    /// <summary>Petal length: a little for the weakest signal, up to 0.8 for -20 dBm.</summary>
    private static double Reach(double dbm) => .2 + Core.WifiAlignment.MeterPercent((int)Math.Round(dbm)) / 100d * .6;

    /// <summary>The page's quality scale: green from Good up, yellow Fair, orange Weak, red Poor.</summary>
    private static Color QualityColor(double dbm) => dbm > -60 ? Color.FromRgb(48, 209, 88) : dbm > -70 ? Color.FromRgb(255, 214, 10) : dbm > -80 ? Color.FromRgb(255, 159, 10) : Color.FromRgb(255, 69, 58);

    public void SetPosition(string position)
    {
        yaw.Angle = YawOf(position);
        pitch.Angle = 0; UpdateHeading(position);
    }
    public void ResetView() { distance = 11.4; yaw.Angle = -25; pitch.Angle = 0; UpdateCamera(); UpdateHeading("Front"); }
    private void UpdateHeading(string? position = null) => heading.Text = position == null ? "Manual view · " + Normalize(yaw.Angle + 25).ToString("0") + "°" : position + " · reference view";
    private static double Normalize(double value) => (value % 360 + 360) % 360;
    private void UpdateCamera() { Dispatcher.BeginInvoke(PlaceLabels, System.Windows.Threading.DispatcherPriority.Loaded); camera.Position = new Point3D(distance * .38, distance * .3 + 1.1, distance * .9); camera.LookDirection = new Vector3D(-camera.Position.X, 1.35 - camera.Position.Y, -camera.Position.Z); camera.UpDirection = new Vector3D(0, 1, 0); }

    private void BuildAntenna()
    {
        // Single upright ribbed fin and compact foot, modelled from the supplied GCC reference image.
        antenna.Children.Add(CreateBox(new Point3D(0, .105, 0), .94, .16, .62, Color.FromRgb(37, 40, 45)));
        antenna.Children.Add(CreateBox(new Point3D(0, .24, 0), .64, .15, .36, Color.FromRgb(43, 47, 53)));
        antenna.Children.Add(CreateBox(new Point3D(0, 1.78, 0), .64, 3, .22, Color.FromRgb(43, 47, 53)));
        antenna.Children.Add(CreateBox(new Point3D(0, 3.29, 0), .64, .055, .22, Color.FromRgb(74, 80, 88)));
        for (var i = 0; i < 8; i++)
        {
            var x = -.28 + i * .08;
            antenna.Children.Add(CreateBox(new Point3D(x, 1.78, .13), .026, 2.94, .035, Color.FromRgb(84, 91, 99)));
            antenna.Children.Add(CreateBox(new Point3D(x, 1.78, -.13), .026, 2.94, .035, Color.FromRgb(69, 75, 84)));
        }
        antenna.Children.Add(CreateBox(new Point3D(0, .41, .16), .4, .045, .015, Color.FromRgb(50, 141, 232)));
    }

    private static GeometryModel3D MaterialModel(MeshGeometry3D mesh, Color color, bool emissive = false, double glow = 1)
    {
        var material = new MaterialGroup();
        if (emissive)
        {
            // Lit from within so the signal overlay reads in the dark scene; dimmer for the weaker sides.
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb((byte)(color.R * .25), (byte)(color.G * .25), (byte)(color.B * .25)))));
            material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb((byte)(color.R * glow), (byte)(color.G * glow), (byte)(color.B * glow)))));
        }
        else
        {
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(100, 108, 120)), 34));
        }
        if (mesh.CanFreeze) mesh.Freeze(); if (material.CanFreeze) material.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static GeometryModel3D CreateBox(Point3D center, double width, double height, double depth, Color color, bool emissive = false)
    {
        var x = width / 2; var y = height / 2; var z = depth / 2;
        var mesh = new MeshGeometry3D();
        void Face(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var offset = mesh.Positions.Count;
            foreach (var p in new[] { a, b, c, d }) mesh.Positions.Add(new Point3D(p.X + center.X, p.Y + center.Y, p.Z + center.Z));
            foreach (var index in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(offset + index);
        }
        Face(new(-x, -y, z), new(x, -y, z), new(x, y, z), new(-x, y, z));
        Face(new(x, -y, -z), new(-x, -y, -z), new(-x, y, -z), new(x, y, -z));
        Face(new(x, -y, z), new(x, -y, -z), new(x, y, -z), new(x, y, z));
        Face(new(-x, -y, -z), new(-x, -y, z), new(-x, y, z), new(-x, y, -z));
        Face(new(-x, y, z), new(x, y, z), new(x, y, -z), new(-x, y, -z));
        Face(new(-x, -y, -z), new(x, -y, -z), new(x, -y, z), new(-x, -y, z));
        return MaterialModel(mesh, color, emissive);
    }

    /// <summary>A flat ring segment on the floor, centred on a yaw angle (the same angles as the antenna's positions).</summary>
    private static GeometryModel3D CreateSector(double yawDegrees, double inner, double outer, double y, Color color, double glow)
    {
        var mesh = new MeshGeometry3D();
        const int steps = 16;
        for (var step = 0; step < steps; step++)
        {
            var start = (yawDegrees - PetalSpan + step * 2 * PetalSpan / steps) * Math.PI / 180;
            var end = (yawDegrees - PetalSpan + (step + 1) * 2 * PetalSpan / steps) * Math.PI / 180;
            var offset = mesh.Positions.Count;
            mesh.Positions.Add(new(Math.Sin(start) * inner, y, Math.Cos(start) * inner));
            mesh.Positions.Add(new(Math.Sin(start) * outer, y, Math.Cos(start) * outer));
            mesh.Positions.Add(new(Math.Sin(end) * outer, y, Math.Cos(end) * outer));
            mesh.Positions.Add(new(Math.Sin(end) * inner, y, Math.Cos(end) * inner));
            foreach (var index in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(offset + index);
        }
        return MaterialModel(mesh, color, emissive: true, glow: glow);
    }

    /// <summary>A flat arrow on the floor from <paramref name="tail"/> to <paramref name="tip"/>.</summary>
    private static GeometryModel3D CreateArrow(Point3D tail, Point3D tip, Color color)
    {
        var along = tip - tail; along.Normalize();
        var side = new Vector3D(-along.Z, 0, along.X);
        var neck = tip - along * .2;
        var mesh = new MeshGeometry3D();
        foreach (var p in new[] { tail + side * .05, neck + side * .05, neck - side * .05, tail - side * .05, neck + side * .14, tip, neck - side * .14 }) mesh.Positions.Add(p);
        foreach (var index in new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 }) mesh.TriangleIndices.Add(index);
        return MaterialModel(mesh, color, emissive: true);
    }

    private static GeometryModel3D CreateRing(double inner, double outer, double y, Color color)
    {
        var mesh = new MeshGeometry3D();
        for (var segment = 0; segment < 96; segment++)
        {
            var start = segment * Math.PI * 2 / 96; var end = (segment + 1) * Math.PI * 2 / 96; var offset = mesh.Positions.Count;
            mesh.Positions.Add(new(Math.Sin(start) * inner, y, Math.Cos(start) * inner));
            mesh.Positions.Add(new(Math.Sin(start) * outer, y, Math.Cos(start) * outer));
            mesh.Positions.Add(new(Math.Sin(end) * outer, y, Math.Cos(end) * outer));
            mesh.Positions.Add(new(Math.Sin(end) * inner, y, Math.Cos(end) * inner));
            foreach (var index in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(offset + index);
        }
        return MaterialModel(mesh, color);
    }
}
