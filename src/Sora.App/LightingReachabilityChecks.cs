using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Sora.App;

public partial class LightingPanel
{
    internal void ShowVerificationView(string view)
    {
        UpdateLayout();
        if (view == "bottom") PageScroll.ScrollToEnd();
        else if (view == "colours") (SyncOn ? SyncColorRow : MemoryColorRow).BringIntoView();
        else PageScroll.ScrollToTop();
        UpdateLayout();
    }

    // Reveal each actual visible input/action and wrapped explanation, including
    // disabled controls and generated swatches. Do not invoke or edit anything.
    internal object CheckControlReachability()
    {
        UpdateLayout();
        var original = PageScroll.VerticalOffset;
        var results = new List<object>();
        var passed = true;
        var controls = Children(PageScroll).OfType<FrameworkElement>().Where(c => c.IsVisible && c.TemplatedParent == null
            && (c is ButtonBase or ComboBox or TextBox or Slider or ColorWheel
                || c is TextBlock { TextWrapping: TextWrapping.Wrap })).ToArray();
        foreach (var control in controls)
        {
            control.BringIntoView(); UpdateLayout();
            var bounds = control.TransformToAncestor(PageScroll).TransformBounds(new Rect(control.RenderSize));
            var visible = bounds.Top >= -1 && bounds.Bottom <= PageScroll.ViewportHeight + 1
                && bounds.Left >= -1 && bounds.Right <= PageScroll.ViewportWidth + 1;
            passed &= visible;
            results.Add(new { control.Name, Type = control.GetType().Name,
                Label = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control)?.GetName(),
                FullyWithinScrollViewport = visible, bounds.Top, bounds.Bottom, PageScroll.ViewportHeight });
        }
        var footer = (FrameworkElement)ApplyButton.Parent;
        var scrollBottom = PageScroll.TransformToAncestor(this).Transform(new Point(0, PageScroll.ActualHeight)).Y;
        var footerTop = footer.TransformToAncestor(this).Transform(new Point()).Y;
        var separated = scrollBottom <= footerTop;
        PageScroll.ScrollToVerticalOffset(original); UpdateLayout();
        return new { Passed = passed && separated, ControlsChecked = results.Count, Controls = results,
            ScrollViewportAboveFooter = separated, ScrollBottom = scrollBottom, FooterTop = footerTop, HardwareActionsInvoked = 0 };
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Children(child)) yield return descendant;
        }
    }
}
