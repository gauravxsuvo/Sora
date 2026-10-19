using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sora.App;

// Opt-in screenshot verification. Reads presentation state and scrolls/restores
// viewports; never invokes device actions or modifies an editor value.
internal static class ProductionScreenChecks
{
    internal static object Run(FrameworkElement page)
    {
        page.UpdateLayout();
        var results = new List<object>();
        var failures = new List<string>();
        var inputs = Descendants(page).OfType<Control>()
            .Where(c => c.IsVisible && c.TemplatedParent == null &&
                c is TextBox or ComboBox or Slider or CheckBox or DataGrid).ToArray();
        foreach (var control in inputs)
        {
            var shared = page.TryFindResource(control.GetType()) as Style;
            var sharedTemplate = shared?.Setters.OfType<Setter>().LastOrDefault(s => s.Property == Control.TemplateProperty)?.Value;
            var templateMatches = sharedTemplate == null || ReferenceEquals(control.Template, sharedTemplate);
            var peer = UIElementAutomationPeer.CreatePeerForElement(control);
            var name = peer?.GetName() ?? "";
            var named = !string.IsNullOrWhiteSpace(name);
            // WPF tables can use column/row names; report their container name separately.
            if (!templateMatches) failures.Add(control.Name + ": bypasses shared template");
            if (!named && control is not DataGrid) failures.Add(control.Name + ": missing accessible name");
            var heightFits = control is not TextBox && control is not ComboBox || control.ActualHeight >= 31;
            if (!heightFits) failures.Add(control.Name + ": input height below shared size");
            results.Add(new { control.Name, Type = control.GetType().Name, AccessibleName = name, control.IsEnabled, control.ActualHeight, SharedTemplate = templateMatches });
        }
        var scrolling = new List<object>();
        foreach (var scroll in Descendants(page).OfType<ScrollViewer>().Where(s => s.IsVisible && s.ScrollableHeight > 0).ToArray())
        {
            var offset = scroll.VerticalOffset;
            scroll.ScrollToEnd(); page.UpdateLayout();
            var reachedEnd = scroll.VerticalOffset >= scroll.ScrollableHeight - 1;
            if (!reachedEnd) failures.Add(scroll.Name + ": cannot reach lower content");
            scrolling.Add(new { scroll.Name, scroll.ScrollableHeight, ReachedEnd = reachedEnd });
            scroll.ScrollToVerticalOffset(offset); page.UpdateLayout();
        }
        return new { Passed = failures.Count == 0, Page = page.Name, Controls = results, Scrolling = scrolling, Failures = failures, DeviceActionsInvoked = 0 };
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
