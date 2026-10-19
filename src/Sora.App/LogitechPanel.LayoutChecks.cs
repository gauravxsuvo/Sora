using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Sora.App;

public partial class LogitechPanel
{
    internal async Task<object> CheckRgbInteractionAsync()
    {
        if (snapshot?.Lighting?.Length != 2 || HasPendingEdits)
            throw new InvalidOperationException("Read the connected mouse with no pending edits before checking RGB interaction.");
        var checks = new Dictionary<string, bool>();
        var bounds = new List<object>();
        Sections.SelectedItem = LightingTab; UpdateLayout();
        LightingScroll.ScrollToEnd(); UpdateLayout();
        Sections.SelectedItem = SensitivityTab; Sections.SelectedItem = LightingTab;
        await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks["Opening LIGHTSYNC resets scroll to top"] = LightingScroll.VerticalOffset == 0;
        bool FullyVisible(FrameworkElement element)
        {
            var rect = element.TransformToAncestor(LightingScroll).TransformBounds(new Rect(element.RenderSize));
            return rect.Top >= 0 && rect.Bottom <= LightingScroll.ViewportHeight;
        }
        checks["Initial sync label and toggle fully visible"] = FullyVisible(LinkZonesCheck);
        checks["Initial full-size wheel fully visible"] = FullyVisible(ColorPickerHost);
        checks["Initial complete RGB row fully visible"] = FullyVisible(RgbFields);
        var imageBefore = LightPreview.TransformToAncestor(this).TransformBounds(new Rect(LightPreview.RenderSize));
        var originalHex = HexInput.Text;
        try
        {
            EffectPicker.SelectedIndex = Array.FindIndex((EffectOption[])EffectPicker.ItemsSource, f => f.Mode == 1);
            RedInput.Text = "12"; GreenInput.Text = "34"; BlueInput.Text = "56";
            checks["RGB editing updates hex and stages colour"] = HexInput.Text == "#0C2238" && HasPendingEdits && !invalidRgb;
            LightingScroll.ScrollToTop(); UpdateLayout();
            Window.GetWindow(this)?.Activate();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            RedInput.Focus();
            foreach (var field in new[] { RedInput, GreenInput, BlueInput })
            {
                await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                checks[field.Name + " receives keyboard focus"] = field.IsKeyboardFocused;
                checks[field.Name + " focus requires no scrolling at reference size"] = LightingScroll.VerticalOffset == 0;
                checks["Sync row remains complete with " + field.Name + " focus"] = FullyVisible(LinkZonesCheck);
                foreach (var input in new[] { RedInput, GreenInput, BlueInput })
                {
                    var rect = input.TransformToAncestor(LightingScroll).TransformBounds(new Rect(input.RenderSize));
                    var fits = rect.Top >= 3 && rect.Bottom <= LightingScroll.ViewportHeight - 3
                        && rect.Left >= 0 && rect.Right <= LightingScroll.ViewportWidth;
                    checks[input.Name + " fully visible when " + field.Name + " focused"] = fits;
                    bounds.Add(new { Focus = field.Name, Input = input.Name, rect.Top, rect.Bottom, LightingScroll.ViewportHeight, FullyVisible = fits });
                }
                if (field != BlueInput) field.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
            var imageAfter = LightPreview.TransformToAncestor(this).TransformBounds(new Rect(LightPreview.RenderSize));
            checks["Illustration remains stationary"] = imageBefore == imageAfter;
            var footerTop = ((FrameworkElement)ApplyButton.Parent).TransformToAncestor(this).Transform(new Point()).Y;
            var scrollBottom = LightingScroll.TransformToAncestor(this).Transform(new Point(0, LightingScroll.ActualHeight)).Y;
            checks["RGB viewport remains above action bar"] = scrollBottom <= footerTop - 8;
        }
        finally { DiscardClick(this, new RoutedEventArgs()); }
        checks["Discard restores original colour without hardware writes"] = !HasPendingEdits && HexInput.Text == originalHex;
        return new { Passed = checks.Values.All(v => v), Checks = checks, Bounds = bounds, HardwareSettingsWrites = 0 };
    }

    internal object CheckPinnedLayout()
    {
        UpdateLayout();
        var assignments = AssignmentsTab.IsSelected;
        var scroll = assignments ? AssignmentScroll : LightingScroll;
        var image = assignments ? AssignmentPreview : LightPreview;
        var original = scroll.VerticalOffset;
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
        var before = Bounds(image);
        var footerTop = Bounds((FrameworkElement)ApplyButton.Parent).Top;
        var checks = new Dictionary<string, bool>();
        checks["Complete illustration above footer"] = before.Top >= Bounds(Sections).Top && before.Bottom <= footerTop - 8;
        scroll.ScrollToEnd(); UpdateLayout();
        var after = Bounds(image);
        checks["Illustration stays fixed while form scrolls"] = Math.Abs(before.Top - after.Top) < 1 && Math.Abs(before.Bottom - after.Bottom) < 1;
        checks["Scroll viewport clears footer"] = Bounds(scroll).Bottom <= footerTop - 8;
        var revealed = new List<object>();
        foreach (var element in LayoutDescendants(scroll).OfType<FrameworkElement>().Where(e => e.IsVisible && e.TemplatedParent == null
            && e is ButtonBase or TextBox or ComboBox or Slider or ColorWheel).ToArray())
        {
            element.BringIntoView(); UpdateLayout();
            var bounds = element.TransformToAncestor(scroll).TransformBounds(new Rect(element.RenderSize));
            var fits = bounds.Top >= -1 && bounds.Bottom <= scroll.ViewportHeight + 1 && bounds.Left >= -1 && bounds.Right <= scroll.ViewportWidth + 1;
            revealed.Add(new { element.Name, FullyVisible = fits });
            checks["Reachable " + (element.Name.Length > 0 ? element.Name : "action " + revealed.Count)] = fits;
        }
        if (assignments)
        {
            AssignmentLegend.BringIntoView(); scroll.ScrollToEnd(); UpdateLayout();
            var last = (FrameworkElement)AssignmentLegend.ItemContainerGenerator.ContainerFromIndex(AssignmentLegend.Items.Count - 1);
            var bounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
            checks["Final assignment label above footer"] = bounds.Top >= -1 && bounds.Bottom <= scroll.ViewportHeight - 8;
        }
        else
        {
            scroll.ScrollToTop(); UpdateLayout();
            var bounds = ColorPickerHost.TransformToAncestor(scroll).TransformBounds(new Rect(ColorPickerHost.RenderSize));
            checks["Initial full-size picker fits reference-height viewport"] = scroll.ViewportHeight < 400 || bounds.Top >= 0 && bounds.Bottom <= scroll.ViewportHeight - 16;
        }
        scroll.ScrollToVerticalOffset(original); UpdateLayout();
        return new { Passed = checks.Values.All(v => v), checks, Controls = revealed, IllustrationBefore = before.ToString(), IllustrationAfterScroll = after.ToString(), HardwareActionsInvoked = 0 };
    }

    private static IEnumerable<DependencyObject> LayoutDescendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in LayoutDescendants(child)) yield return descendant;
        }
    }
}
