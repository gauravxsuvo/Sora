using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Sora.App;

internal static class ThemeManager
{
    private static readonly Dictionary<string, object> light = new();
    internal static bool IsDark { get; private set; }
    private static readonly Dictionary<string, string> dark = new()
    {
        ["WindowBrush"] = "#000000", ["SidebarBrush"] = "#000000",
        // Neutral surfaces and status colors from status.portways.app; retain the AMOLED shell.
        ["SurfaceBrush"] = "#101010", ["SurfaceRaisedBrush"] = "#181818",
        ["InsetBrush"] = "#0A0A0A", ["ControlBrush"] = "#181818",
        ["ControlSelectedBrush"] = "#252525", ["ControlDisabledBrush"] = "#151515",
        ["SwitchOffBrush"] = "#444444", ["HoverOverlayBrush"] = "#12FFFFFF",
        ["PressedOverlayBrush"] = "#20FFFFFF", ["NavHoverBrush"] = "#12FFFFFF",
        ["NavSelectedBrush"] = "#1C1C1C", ["SearchFieldBrush"] = "#101010",
        ["SearchBorderBrush"] = "#484848", ["ScrollThumbBrush"] = "#555555",
        ["ScrollThumbHoverBrush"] = "#777777", ["WarningSurfaceBrush"] = "#322313",
        ["WarningBorderBrush"] = "#70532F", ["PopoverBrush"] = "#181818",
        ["HairlineBrush"] = "#484848", ["SeparatorBrush"] = "#303030",
        ["InputBorderBrush"] = "#606060", ["TextBrush"] = "#F5F5F5",
        ["SecondaryTextBrush"] = "#B4B4B4", ["TertiaryTextBrush"] = "#A0A0A0",
        ["DisabledTextBrush"] = "#808080", ["OnAccentBrush"] = "#111111",
        ["AccentBrush"] = "#F5F5F5", ["AccentTextBrush"] = "#E5E5E5",
        ["AccentSubtleBrush"] = "#16FFFFFF", ["ChartBrush"] = "#00D492", ["Good"] = "#00D492",
        ["Warn"] = "#FFD43B", ["Bad"] = "#FF6268", ["Idle"] = "#A0A0A0",
        ["ErrorTextBrush"] = "#FF6268", ["WarnTextBrush"] = "#FFD43B",
        ["Muted"] = "#B4B4B4", ["KeySelectionSeparatorBrush"] = "#383838"
    };

    internal static void Apply(bool useDark)
    {
        var app = Application.Current;
        if (app == null) return;
        foreach (var key in dark.Keys.Concat(new[] { "WindowColor", "WindowCaptionTextColor", "WindowBorderColor", "AccentColor" }))
            if (!light.ContainsKey(key) && app.TryFindResource(key) is { } original) light[key] = original;
        IsDark = useDark;
        foreach (var (key, color) in dark)
            app.Resources[key] = useDark ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) : light[key];
        app.Resources["WindowColor"] = useDark ? Colors.Black : light["WindowColor"];
        app.Resources["WindowCaptionTextColor"] = useDark ? Color.FromRgb(245, 245, 245) : light["WindowCaptionTextColor"];
        app.Resources["WindowBorderColor"] = useDark ? Color.FromRgb(56, 56, 56) : light["WindowBorderColor"];
        app.Resources["AccentColor"] = useDark ? Color.FromRgb(245, 245, 245) : light["AccentColor"];
        foreach (Window window in app.Windows) Redraw(window);
    }

    private static void Redraw(DependencyObject root)
    {
        if (root is UIElement element) element.InvalidateVisual();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Redraw(VisualTreeHelper.GetChild(root, i));
    }
}
