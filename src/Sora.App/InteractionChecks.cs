using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;

namespace Sora.App;

internal static class InteractionChecks
{
    internal static async Task<object> CheckResizeAsync(Window window, FrameworkElement page)
    {
        var width = window.Width; var height = window.Height;
        var results = new List<object>(); var passed = true;
        try
        {
            foreach (var size in new[] { (1320d, 860d), (1080d, 860d) })
            {
                window.Width = size.Item1; window.Height = size.Item2;
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var screen = ProductionScreenChecks.Run(page);
                var good = JsonSerializer.SerializeToElement(screen).GetProperty("Passed").GetBoolean();
                var navigation = Descendants(window).OfType<RadioButton>().Where(b => b.Name.StartsWith("Nav", StringComparison.Ordinal)).Select(b =>
                {
                    var surface = b.Template.FindName("SelectionSurface", b) as Border;
                    var expected = b.IsChecked == true ? 1d : 0d;
                    return new { b.Name, Correct = surface != null && Math.Abs((double)surface.GetAnimationBaseValue(UIElement.OpacityProperty) - expected) < .001 };
                }).ToArray();
                good &= navigation.All(n => n.Correct);
                passed &= good;
                results.Add(new { VisibleWidth = size.Item1 - 16, VisibleHeight = size.Item2 - 8, Passed = good, Navigation = navigation, Screen = screen });
            }
        }
        finally { window.Width = width; window.Height = height; window.UpdateLayout(); }
        return new { Passed = passed, Resizes = results, HardwareActionsInvoked = 0, ProductionSettingsWrites = 0 };
    }
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    // Read-only inspection of the actual rendered production page.
    internal static object Audit(FrameworkElement page)
    {
        var controls = Descendants(page).OfType<FrameworkElement>().Where(c => c.IsVisible && c.TemplatedParent == null
            && c is ButtonBase or TextBox or ComboBox or Slider or ColorWheel or TabItem).ToArray();
        var failures = new List<string>();
        var results = controls.Select(c =>
        {
            var name = UIElementAutomationPeer.CreatePeerForElement(c)?.GetName() ?? "";
            if (string.IsNullOrWhiteSpace(name)) failures.Add(c.GetType().Name + " " + c.Name + ": missing name");
            var focus = c is Control control ? control.FocusVisualStyle != null || control.Template != null : c.FocusVisualStyle != null;
            return new { c.Name, Type = c.GetType().Name, AccessibleName = name, c.IsEnabled, c.Focusable, FocusPresentation = focus };
        }).ToArray();
        double Contrast(Color a, Color b)
        {
            double L(Color c) { double V(byte v) { var x = v / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); } return .2126 * V(c.R) + .7152 * V(c.G) + .0722 * V(c.B); }
            var x = L(a); var y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
        }
        var background = ((SolidColorBrush)page.FindResource("WindowBrush")).Color;
        var contrast = new[] { "TextBrush", "SecondaryTextBrush", "TertiaryTextBrush", "AccentTextBrush", "ErrorTextBrush" }.Select(key =>
        {
            var ratio = Contrast(((SolidColorBrush)page.FindResource(key)).Color, background);
            if (ratio < 4.5) failures.Add(key + ": contrast below 4.5:1");
            return new { Token = key, Ratio = Math.Round(ratio, 2), Pass = ratio >= 4.5 };
        }).ToArray();
        return new { Passed = failures.Count == 0, Controls = results, Contrast = contrast, Failures = failures, HardwareActionsInvoked = 0, SettingsWrites = 0 };
    }

    // Real WPF controls in an owned test window. All edits remain in this fixture.
    internal static async Task<object> RunAsync(Window owner)
    {
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool value) => checks[name] = value;
        var originalFocus = Keyboard.FocusedElement as UIElement;
        var root = new StackPanel { Margin = new Thickness(20) };
        var toggle = new CheckBox { Content = "Test preference", IsChecked = false };
        var combo = new ComboBox { ItemsSource = new[] { "First", "Second" }, SelectedIndex = 0 };
        var slider = new Slider { Minimum = 0, Maximum = 10, Value = 5, SmallChange = 1 };
        var wheel = new ColorWheel { Width = 150, Height = 150, Hex = "#4488CC" };
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "First", Content = new TextBlock { Text = "First page" } }); tabs.Items.Add(new TabItem { Header = "Second", Content = new TextBlock { Text = "Second page" } });
        var trigger = new Button { Content = "Open confirmation" };
        foreach (var element in new FrameworkElement[] { toggle, combo, slider, wheel, tabs, trigger }) root.Children.Add(element);
        var window = new Window { Owner = owner, Title = "Sora interaction checks", Content = root, Width = 500, Height = 650, ShowInTaskbar = false, Left = -2500, Top = -2500 };
        try
        {
            window.Show(); window.UpdateLayout();
            var togglePeer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle)!.GetPattern(PatternInterface.Toggle)!;
            togglePeer.Toggle();
            var knob = (FrameworkElement)toggle.Template.FindName("Knob", toggle);
            await Task.Delay(170);
            Check("Toggle native state and final knob position", toggle.IsChecked == true && knob.RenderTransform is TranslateTransform { X: 16 });
            togglePeer.Toggle(); ControlState.SetReduceMotion(root, true);
            Check("Live local reduced motion stops in-flight switch", knob.RenderTransform is TranslateTransform off && !off.HasAnimatedProperties && off.X == 0);
            togglePeer.Toggle();
            Check("Reduced-motion toggle has immediate correct position", knob.RenderTransform is TranslateTransform on && !on.HasAnimatedProperties && on.X == 16);
            var overlay = new Border { Width = 100, Height = 20 }; root.Children.Add(overlay); window.UpdateLayout();
            InteractionMotion.SetOpacityTarget(overlay, 0); InteractionMotion.SetOpacityTarget(overlay, 1);
            Check("Reduced-motion hover settles immediately", overlay.Opacity == 1 && !overlay.HasAnimatedProperties);
            InteractionMotion.SetOpacityTarget(overlay, 0);
            ControlState.SetReduceMotion(root, false);
            InteractionMotion.SetOpacityTarget(overlay, 1);
            Check("Hover policy follows Windows preference", overlay.HasAnimatedProperties == SystemParameters.ClientAreaAnimation);
            await Task.Delay(180);
            Check("Hover completes without retained animation value", Math.Abs(overlay.Opacity - 1) < .001);
            combo.IsDropDownOpen = true; window.UpdateLayout();
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            Check("Dropdown opens with bounded popup", popup.IsOpen && combo.MaxDropDownHeight == 340);
            ControlState.SetReduceMotion(root, true); window.UpdateLayout();
            Check("Dropdown respects inherited reduced motion", popup.PopupAnimation == PopupAnimation.None);
            combo.IsDropDownOpen = false;
            var wheelPeer = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(wheel)!.GetPattern(PatternInterface.Value)!;
            var picked = 0; wheel.ColorPicked += _ => picked++;
            wheelPeer.SetValue("#123456");
            Check("Colour Value automation preserves exact hexadecimal", wheel.Hex == "#123456" && picked == 1);
            wheel.IsEnabled = false;
            try { wheelPeer.SetValue("#FFFFFF"); Check("Disabled colour picker rejects edits", false); }
            catch (ElementNotEnabledException) { Check("Disabled colour picker rejects edits", wheel.Hex == "#123456"); }
            wheel.IsEnabled = true;
            window.Activate(); trigger.Focus();
            var dialog = new AppDialog(window, "Interaction check", "This dialog performs no hardware operations.", "Confirm", "Cancel");
            var safeFocus = false;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                safeFocus = dialog.CancelButton.IsKeyboardFocused;
                dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            }));
            Check("Dialog Escape cancels safely", dialog.ShowDialog() == false && safeFocus);
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check("Dialog restores original control focus", trigger.IsKeyboardFocused);
            Check("Dialog traps tab traversal within its content", KeyboardNavigation.GetTabNavigation((DependencyObject)dialog.Content) == KeyboardNavigationMode.Cycle);
            var second = (TabItem)tabs.Items[1]; second.Focus(); window.UpdateLayout();
            Check("Tab header has visible focus outline", ((Border)second.Template.FindName("TabFocus", second)).Visibility == Visibility.Visible);
            Check("Tab content changes without layout animation", ((ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs)).RenderTransform == Transform.Identity);
            var sliderPeer = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(slider)!.GetPattern(PatternInterface.RangeValue)!;
            sliderPeer.SetValue(7); Check("Slider retains native RangeValue interaction", slider.Value == 7);
        }
        finally { window.Close(); if (originalFocus is { IsVisible: true, IsEnabled: true }) originalFocus.Focus(); }
        return new { Passed = checks.Values.All(x => x), Checks = checks, WindowsAnimationsEnabled = SystemParameters.ClientAreaAnimation, HardwareActionsInvoked = 0, ProductionSettingsWrites = 0 };
    }
}
