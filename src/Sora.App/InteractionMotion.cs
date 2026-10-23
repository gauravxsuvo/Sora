using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sora.App;

// Presentation only: never schedules an operation, changes layout or blocks input.
internal static class InteractionMotion
{
    private static readonly List<WeakReference<UIElement>> active = new();
    internal static bool Allowed(DependencyObject target) => SystemParameters.ClientAreaAnimation
        && !ControlState.GetReduceMotion(target)
        && (target is not FrameworkElement { TemplatedParent: DependencyObject parent } || !ControlState.GetReduceMotion(parent));

    static InteractionMotion()
    {
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                Application.Current?.Dispatcher.InvokeAsync(StopReducedMotion);
        };
    }

    internal static void StopReducedMotion()
    {
        active.RemoveAll(reference =>
        {
            if (!reference.TryGetTarget(out var element)) return true;
            if (Allowed(element)) return false;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (element.RenderTransform is TranslateTransform transform) transform.BeginAnimation(TranslateTransform.XProperty, null);
            return true;
        });
    }

    private static void Animate(UIElement element, Animatable target, DependencyProperty property, double from, double to, string durationKey)
    {
        target.BeginAnimation(property, null);
        target.SetValue(property, to);
        if (!element.IsVisible || !Allowed(element) || Math.Abs(to - from) < .001) return;
        var milliseconds = (element as FrameworkElement)?.TryFindResource(durationKey) as double? ?? 120;
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
        if (active.Count > 256) active.RemoveAll(r => !r.TryGetTarget(out _));
        if (!active.Exists(r => r.TryGetTarget(out var existing) && ReferenceEquals(element, existing))) active.Add(new(element));
    }

    // UIElement is not Animatable, so opacity uses its own BeginAnimation API.
    private static void Opacity(UIElement element, double from, double to, string durationKey)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        // This behaviour owns opacity on a dedicated surface. A current value
        // can be discarded when WPF reevaluates a template during resizing.
        element.SetValue(UIElement.OpacityProperty, to);
        if (!element.IsVisible || !Allowed(element) || Math.Abs(to - from) < .001) return;
        var milliseconds = (element as FrameworkElement)?.TryFindResource(durationKey) as double? ?? 120;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
        if (!active.Exists(r => r.TryGetTarget(out var existing) && ReferenceEquals(element, existing))) active.Add(new(element));
        active.RemoveAll(r => !r.TryGetTarget(out _));
    }

    internal static void FadeIn(UIElement element, double from) => Opacity(element, from, 1, "FeedbackMotionMilliseconds");

    public static readonly DependencyProperty OpacityTargetProperty = DependencyProperty.RegisterAttached("OpacityTarget", typeof(double), typeof(InteractionMotion),
        new PropertyMetadata(1d, (d, e) => { if (d is UIElement element) Opacity(element, element.Opacity, (double)e.NewValue, "FeedbackMotionMilliseconds"); }));
    public static double GetOpacityTarget(DependencyObject d) => (double)d.GetValue(OpacityTargetProperty);
    public static void SetOpacityTarget(DependencyObject d, double value) => d.SetValue(OpacityTargetProperty, value);

    public static readonly DependencyProperty SwitchOffsetProperty = DependencyProperty.RegisterAttached("SwitchOffset", typeof(double), typeof(InteractionMotion),
        new PropertyMetadata(0d, (d, e) =>
        {
            if (d is not UIElement element) return;
            if (element.RenderTransform is not TranslateTransform) element.RenderTransform = new TranslateTransform();
            var transform = (TranslateTransform)element.RenderTransform;
            Animate(element, transform, TranslateTransform.XProperty, transform.X, (double)e.NewValue, "SelectionMotionMilliseconds");
        }));
    public static double GetSwitchOffset(DependencyObject d) => (double)d.GetValue(SwitchOffsetProperty);
    public static void SetSwitchOffset(DependencyObject d, double value) => d.SetValue(SwitchOffsetProperty, value);

    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached("Reveal", typeof(bool), typeof(InteractionMotion), new PropertyMetadata(false, RevealChanged));
    public static bool GetReveal(DependencyObject d) => (bool)d.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject d, bool value) => d.SetValue(RevealProperty, value);
    private static void RevealChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue) { element.Loaded += RevealLoaded; element.IsVisibleChanged += RevealVisible; }
        else { element.Loaded -= RevealLoaded; element.IsVisibleChanged -= RevealVisible; }
    }
    private static void RevealLoaded(object sender, RoutedEventArgs e) => RevealNow((FrameworkElement)sender);
    private static void RevealVisible(object sender, DependencyPropertyChangedEventArgs e) { if ((bool)e.NewValue) RevealNow((FrameworkElement)sender); }
    internal static void RevealNow(FrameworkElement element)
    {
        if (element.IsLoaded && element.IsVisible) Opacity(element, .88, 1, "RevealMotionMilliseconds");
    }

    public static readonly DependencyProperty ContentTransitionProperty = DependencyProperty.RegisterAttached("ContentTransition", typeof(bool), typeof(InteractionMotion), new PropertyMetadata(false, ContentTransitionChanged));
    public static bool GetContentTransition(DependencyObject d) => (bool)d.GetValue(ContentTransitionProperty);
    public static void SetContentTransition(DependencyObject d, bool value) => d.SetValue(ContentTransitionProperty, value);
    private static readonly DependencyPropertyDescriptor ContentDescriptor = DependencyPropertyDescriptor.FromProperty(ContentPresenter.ContentProperty, typeof(ContentPresenter));
    private static void ContentTransitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentPresenter presenter) return;
        if ((bool)e.NewValue) { presenter.Loaded += ObserveContent; presenter.Unloaded += ForgetContent; if (presenter.IsLoaded) ObserveContent(presenter, new()); }
        else { presenter.Loaded -= ObserveContent; presenter.Unloaded -= ForgetContent; ForgetContent(presenter, new()); }
    }
    private static void ObserveContent(object sender, RoutedEventArgs e) { ContentDescriptor.RemoveValueChanged(sender, ContentChanged); ContentDescriptor.AddValueChanged(sender, ContentChanged); }
    private static void ForgetContent(object sender, RoutedEventArgs e) => ContentDescriptor.RemoveValueChanged(sender, ContentChanged);
    private static void ContentChanged(object? sender, EventArgs e) { if (sender is FrameworkElement element) RevealNow(element); }
}
