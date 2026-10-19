using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.ComponentModel;
using System;

namespace Sora.App;

/// <summary>Presentation-only states. Device operations still own availability, validation and cancellation.</summary>
public static class ControlState
{
    public const string HexColourError = "Use six hex digits, e.g. #769BFF.";
    public static void SetValidation(TextBox input, TextBlock message, string? error)
    {
        SetIsInvalid(input, !string.IsNullOrEmpty(error));
        message.Text = error ?? "";
        AutomationProperties.SetHelpText(input, error ?? "");
    }
    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.RegisterAttached("ReduceMotion", typeof(bool), typeof(ControlState), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, (_, _) => InteractionMotion.StopReducedMotion()));
    public static bool GetReduceMotion(DependencyObject target) => (bool)target.GetValue(ReduceMotionProperty);
    public static void SetReduceMotion(DependencyObject target, bool value) => target.SetValue(ReduceMotionProperty, value);
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached("IsBusy", typeof(bool), typeof(ControlState), new PropertyMetadata(false));
    public static bool GetIsBusy(DependencyObject target) => (bool)target.GetValue(IsBusyProperty);
    public static void SetIsBusy(DependencyObject target, bool value) => target.SetValue(IsBusyProperty, value);
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.RegisterAttached("IsInvalid", typeof(bool), typeof(ControlState), new PropertyMetadata(false));
    public static bool GetIsInvalid(DependencyObject target) => (bool)target.GetValue(IsInvalidProperty);
    public static void SetIsInvalid(DependencyObject target, bool value) => target.SetValue(IsInvalidProperty, value);
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(ControlState), new PropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject target) => (string)target.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject target, string value) => target.SetValue(PlaceholderProperty, value);
}

public enum NoticeTone { Neutral, Success, Warning, Error, Information, Loading }

/// <summary>Live OS preference. A local ReduceMotion state can further restrict motion for a view.</summary>
public sealed class MotionPreferences : DependencyObject
{
    public static readonly DependencyProperty ReducedProperty = DependencyProperty.Register(nameof(Reduced), typeof(bool), typeof(MotionPreferences));
    public bool Reduced { get => (bool)GetValue(ReducedProperty); private set => SetValue(ReducedProperty, value); }
    public MotionPreferences()
    {
        Reduced = !SystemParameters.ClientAreaAnimation;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                Dispatcher.InvokeAsync(() => Reduced = !SystemParameters.ClientAreaAnimation);
        };
    }
}

/// <summary>Persistent page feedback; never disappears before it can be read.</summary>
public sealed class Notice : ContentControl
{
    private TextBlock? observedText;
    private string lastAnnouncement = "";
    private bool announcementPending;
    private static readonly DependencyPropertyDescriptor TextDescriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(NoticeTone), typeof(Notice), new PropertyMetadata(NoticeTone.Neutral, (target, _) => ((Notice)target).Announce()));
    public NoticeTone Tone { get => (NoticeTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public Notice()
    {
        SetResourceReference(StyleProperty, typeof(Notice));
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Loaded += (_, _) => ObserveText();
        Unloaded += (_, _) => StopObservingText();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new NoticePeer(this);
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (IsLoaded) { ObserveText(); Announce(); }
    }
    private void Announce()
    {
        if (announcementPending || !IsLoaded) return;
        announcementPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            announcementPending = false;
            if (!IsLoaded) return;
            var message = Tone + ": " + (Content is TextBlock text ? text.Text : Content?.ToString() ?? "");
            if (message == lastAnnouncement) return;
            lastAnnouncement = message;
            if (Tone != NoticeTone.Neutral) InteractionMotion.RevealNow(this);
            UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }));
    }
    private void TextChanged(object? sender, System.EventArgs e) => Announce();
    private void StopObservingText() { if (observedText != null) TextDescriptor.RemoveValueChanged(observedText, TextChanged); observedText = null; }
    private void ObserveText() { StopObservingText(); if (Content is TextBlock text) { observedText = text; TextDescriptor.AddValueChanged(text, TextChanged); } }
    private sealed class NoticePeer(Notice owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => "Notice";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => (owner.Tone == NoticeTone.Neutral ? "" : owner.Tone + ": ") + (owner.Content is TextBlock text ? text.Text : owner.Content?.ToString() ?? "");
    }
}
