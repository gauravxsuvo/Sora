using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sora.Core;

namespace Sora.App;

/// <summary>Opt-in in-process component preview; never connected to a device service.</summary>
public partial class ComponentGallery : UserControl
{
    private bool initialized;
    private bool sampleBusy;
    private readonly CancellationTokenSource lifetime = new();
    private const string PreviewMessage = "Examples use local sample data. Device settings and saved profiles are never changed.";
    private sealed class PreviewPoint { public double Temperature { get; set; } = 40; public double Duty { get; set; } = 50; }
    private readonly PreviewPoint point = new();
    public ComponentGallery()
    {
        InitializeComponent();
        Table.ItemsSource = new[] { point };
        foreach (var hex in new[] { "#FFFFFF", "#FF453A", "#FF9F0A", "#30D158", "#0A84FF" })
        {
            var button = new Button { Width = 22, Height = 22, Margin = new Thickness(0, 0, 5, 5), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), ToolTip = hex, Tag = hex };
            button.SetResourceReference(StyleProperty, "Swatch"); AutomationProperties.SetName(button, "Use colour " + hex);
            button.Click += (_, _) => Hex.Text = hex; Palette.Children.Add(button);
        }
        Wheel.ColorPicked += value => Hex.Text = value;
        initialized = true; Wheel.Hex = Hex.Text; UpdatePalette(); ValidateSampleHex();
        Unloaded += (_, _) => lifetime.Cancel();
    }
    private void ActionClick(object sender, RoutedEventArgs e) { Feedback.Tone = NoticeTone.Success; Feedback.Content = "Sample action completed. No device settings were changed."; }
    private void HelpClick(object sender, RoutedEventArgs e) { Feedback.Tone = NoticeTone.Information; Feedback.Content = "Tab moves focus. Space activates. Arrow keys adjust values. Examples stay local."; }
    private void SaveSampleClick(object sender, RoutedEventArgs e) { if (!DisabledButton.IsEnabled) return; Feedback.Tone = NoticeTone.Success; Feedback.Content = "Sample profile accepted in memory. No file was saved."; }
    private void ProfileNameChanged(object sender, TextChangedEventArgs e) { if (initialized) DisabledButton.IsEnabled = !sampleBusy && !string.IsNullOrWhiteSpace(Input.Text); }
    private void ResetClick(object sender, RoutedEventArgs e) => ResetSample();
    private void ResetSample()
    {
        Input.Clear(); InvalidInput.Text = "#12"; Hex.Text = "#0A84FF"; Toggle.IsChecked = true; Range.Value = 60; Picker.SelectedIndex = 0;
        FirstMode.IsChecked = true; point.Temperature = 40; point.Duty = 50; Table.Items.Refresh(); Progress.Value = 60;
        Feedback.Tone = NoticeTone.Neutral; Feedback.Content = PreviewMessage;
    }
    private void SampleToggleChanged(object sender, RoutedEventArgs e) { if (initialized) Range.IsEnabled = Toggle.IsChecked == true; }
    private void SampleModeChanged(object sender, RoutedEventArgs e) { if (initialized) ModeHint.Text = FirstMode.IsChecked == true ? "Lighting preview · choose a colour above." : "Profiles preview · sample values stay local."; }
    private void ValidationChanged(object sender, TextChangedEventArgs e) { if (initialized) ValidateSampleHex(); }
    private void ValidateSampleHex()
    {
        try { KreoProtocol.ParseHex(InvalidInput.Text); ControlState.SetValidation(InvalidInput, InvalidMessage, null); }
        catch (FormatException) { ControlState.SetValidation(InvalidInput, InvalidMessage, ControlState.HexColourError); }
    }
    private async void SampleTaskClick(object sender, RoutedEventArgs e) => await RunSampleTaskAsync();
    private async Task RunSampleTaskAsync(int duration = 600)
    {
        if (sampleBusy) return;
        sampleBusy = true; BusyButton.IsEnabled = ResetButton.IsEnabled = DisabledButton.IsEnabled = false; ControlState.SetIsBusy(BusyButton, true);
        InfoNotice.Tone = NoticeTone.Loading; InfoNotice.Content = "Running the local sample task."; Progress.Value = 0;
        Feedback.Tone = NoticeTone.Loading; Feedback.Content = "Running a local sample task.";
        try
        {
            for (var step = 1; step <= 5; step++) { await Task.Delay(Math.Max(1, duration / 5), lifetime.Token); Progress.Value = step * 20; }
            Feedback.Tone = NoticeTone.Success; Feedback.Content = "Sample task completed. Nothing was saved or sent to a device.";
        }
        catch (OperationCanceledException) { }
        finally
        {
            sampleBusy = false; BusyButton.IsEnabled = ResetButton.IsEnabled = true; ControlState.SetIsBusy(BusyButton, false);
            InfoNotice.Tone = NoticeTone.Information; InfoNotice.Content = "This is an information message."; DisabledButton.IsEnabled = !string.IsNullOrWhiteSpace(Input.Text);
        }
    }
    private void OpenDialog(object sender, RoutedEventArgs e)
    {
        var confirmed = AppDialog.Confirm(Window.GetWindow(this), "Discard staged edits?", "This preview demonstrates the shared confirmation dialog. No device settings will be changed.", "Discard edits", "Keep edits");
        Feedback.Content = confirmed ? "Preview confirmed." : "Preview cancelled. Edits kept.";
    }
    private void HexChanged(object sender, TextChangedEventArgs e)
    {
        if (!initialized) return;
        try { var rgb = KreoProtocol.ParseHex(Hex.Text); ControlState.SetValidation(Hex, HexError, null); Wheel.Hex = "#" + Convert.ToHexString(rgb); UpdatePalette(); }
        catch (FormatException) { ControlState.SetValidation(Hex, HexError, ControlState.HexColourError); }
    }
    private void UpdatePalette()
    {
        foreach (Button button in Palette.Children)
        {
            button.Content = string.Equals(button.Tag as string, Wheel.Hex, StringComparison.OrdinalIgnoreCase) ? "✓" : "";
            button.Foreground = button.Tag as string == "#FFFFFF" || button.Tag as string == "#FF9F0A" ? Brushes.Black : Brushes.White;
        }
    }
    internal void ShowLowerPreview() => Scroller.ScrollToEnd();
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    internal async Task<object> RunChecksAsync(string? previewPath = null)
    {
        var passed = new List<string>();
        var sliderArrowChecked = false;
        var sliderModifiers = ModifierKeys.None;
        void Check(bool condition, string name) { if (!condition) throw new IOException("Component check failed: " + name); passed.Add(name); }
        UpdateLayout();
        Check(SoraData.IsDevelopmentPreview, "isolated preview storage");
        Check(Primary.ActualHeight >= 32 && Input.ActualHeight >= 32 && Picker.ActualHeight >= 32, "shared control dimensions");
        var feedbackHeight = Feedback.ActualHeight;
        Primary.Focus(); UpdateLayout();
        Check(Primary.IsKeyboardFocused && ((Border)Primary.Template.FindName("FocusOutline", Primary)).Visibility == Visibility.Visible, "visible button focus");
        Primary.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(ResetButton.IsKeyboardFocused, "Tab follows action order");
        BusyButton.Focus(); BusyButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(Input.IsKeyboardFocused, "Tab skips unavailable sample action"); Primary.Focus();
        var source = PresentationSource.FromVisual(Primary)!;
        Primary.RaiseEvent(new KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
        Check(Primary.IsPressed && ((Border)Primary.Template.FindName("Overlay", Primary)).Background == FindResource("PressedOverlayBrush"), "keyboard pressed state");
        Primary.RaiseEvent(new KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = System.Windows.Input.Keyboard.KeyUpEvent });
        ((IInvokeProvider)new ButtonAutomationPeer(Primary).GetPattern(PatternInterface.Invoke)).Invoke();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Feedback.Content.ToString()!.Contains("completed"), "button automation invokes existing action");
        var width = Primary.ActualWidth; ControlState.SetIsBusy(Primary, true); Primary.IsEnabled = false; UpdateLayout();
        Check(((ProgressBar)Primary.Template.FindName("BusyIndicator", Primary)).Visibility == Visibility.Visible && Primary.ActualWidth == width, "loading without layout shift");
        ControlState.SetReduceMotion(this, true);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout();
        var busyProgress = (ProgressBar)Primary.Template.FindName("BusyIndicator", Primary);
        var animation = (ScaleTransform)((Border)busyProgress.Template.FindName("PART_Indicator", busyProgress)).RenderTransform;
        Check(!animation.HasAnimatedProperties && ((Popup)Picker.Template.FindName("PART_Popup", Picker)).PopupAnimation == PopupAnimation.None, "reduced motion stops progress and popup animation");
        ControlState.SetReduceMotion(this, false);
        var blocked = false; try { ((IInvokeProvider)new ButtonAutomationPeer(Primary).GetPattern(PatternInterface.Invoke)).Invoke(); } catch (ElementNotEnabledException) { blocked = true; }
        Check(blocked, "disabled action cannot be invoked"); Primary.IsEnabled = true; ControlState.SetIsBusy(Primary, false);
        Check(((TextBlock)Input.Template.FindName("Placeholder", Input)).Visibility == Visibility.Visible, "empty input placeholder");
        Input.Text = "My profile"; UpdateLayout(); Check(((TextBlock)Input.Template.FindName("Placeholder", Input)).Visibility == Visibility.Collapsed, "placeholder yields to input"); Input.Clear();
        Check(!DisabledButton.IsEnabled, "sample save requires a name"); Input.Text = "Local sample";
        DisabledButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(Feedback.Content.ToString()!.Contains("in memory"), "sample save stays in memory"); Input.Clear();
        HelpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(Feedback.Tone == NoticeTone.Information, "help action provides instructions");
        Check(((Border)InvalidInput.Template.FindName("Body", InvalidInput)).BorderBrush == FindResource("ErrorTextBrush"), "validation outline");
        Check(InvalidMessage.Text == ControlState.HexColourError && AutomationProperties.GetHelpText(InvalidInput) == ControlState.HexColourError, "shared accessible hex validation");
        InvalidInput.Text = "#123ABC"; Check(!ControlState.GetIsInvalid(InvalidInput) && InvalidMessage.Text.Length == 0, "correcting sample hex clears validation"); InvalidInput.Text = "#12";
        Picker.IsDropDownOpen = true; Picker.SelectedIndex = 1; UpdateLayout();
        Check(Picker.SelectionBoxItem != null && ((Popup)Picker.Template.FindName("PART_Popup", Picker)).IsOpen, "dropdown popup and value"); Picker.IsDropDownOpen = false; Picker.SelectedIndex = 0;
        var pickerPopup = (Popup)Picker.Template.FindName("PART_Popup", Picker);
        for (var attempt = 0; attempt < 50 && pickerPopup.Child.IsVisible; attempt++) await Task.Delay(20);
        Picker.Focus(); Picker.RaiseEvent(new KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Down) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
        Check(Picker.SelectedIndex == 1, "dropdown arrow key interaction"); Picker.SelectedIndex = 0;
        ControlState.SetPlaceholder(Picker, "Choose an effect"); Picker.SelectedIndex = -1; UpdateLayout();
        Check(((TextBlock)Picker.Template.FindName("Placeholder", Picker)).Visibility == Visibility.Visible, "empty dropdown placeholder"); Picker.SelectedIndex = 0;
        ((IToggleProvider)new ToggleButtonAutomationPeer(Toggle).GetPattern(PatternInterface.Toggle)).Toggle();
        Check(Toggle.IsChecked == false, "toggle automation"); Toggle.IsChecked = true;
        Check(Range.IsEnabled, "toggle controls sample availability");
        ((IRangeValueProvider)new SliderAutomationPeer(Range).GetPattern(PatternInterface.RangeValue)).SetValue(75);
        Check(Range.Value == 75, "slider automation"); Range.Value = 60;
        Range.Focus();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        sliderModifiers = System.Windows.Input.Keyboard.Modifiers;
        if (sliderModifiers == ModifierKeys.None)
        {
            Range.RaiseEvent(new KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Right) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            Check(Range.Value > 60, "slider arrow key interaction"); sliderArrowChecked = true;
        }
        else
        {
            // Synthetic key events still use the user's live modifier state. Do
            // not release their keys or pretend a Shift+Right event was Right.
            Slider.IncreaseSmall.Execute(null, Range);
            Check(Range.Value > 60, "native slider keyboard command (ambient modifier held)");
        }
        Range.Value = 60;
        var wheelValue = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(Wheel).GetPattern(PatternInterface.Value);
        wheelValue.SetValue("#FF453A"); Check(Hex.Text == "#FF453A", "colour picker automation");
        ((Button)Palette.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Hex.Text == "#FF9F0A" && Wheel.Hex == "#FF9F0A" && ((Button)Palette.Children[2]).Content as string == "✓", "palette hex and wheel synchronization"); Hex.Text = "#0A84FF";
        SecondMode.IsChecked = true; Check(FirstMode.IsChecked == false, "exclusive segmented choice"); FirstMode.IsChecked = true;
        Tabs.SelectedIndex = 1; Check(Tabs.SelectedContent != null, "tab selection"); Tabs.SelectedIndex = 0;
        Check(((TextBlock)((TabItem)Tabs.Items[0]).Content).FontWeight == FontWeights.Normal, "selected tab does not bold body content");
        var semanticLabels = new HashSet<string>();
        foreach (var notice in new[] { InfoNotice, SuccessNotice, WarningNotice, ErrorNotice })
        {
            notice.ApplyTemplate();
            var label = (TextBlock)notice.Template.FindName("StatusLabel", notice);
            semanticLabels.Add(label.Text);
            Check(label.Visibility == Visibility.Visible && ((FrameworkElement)notice.Template.FindName("StatusIcon", notice)).Visibility == Visibility.Visible, "feedback icon and label: " + notice.Tone);
        }
        Check(semanticLabels.Count == 4, "feedback meaning does not depend on colour");
        var taskWidth = BusyButton.ActualWidth;
        var sampleTask = RunSampleTaskAsync(); UpdateLayout();
        Check(sampleBusy && !BusyButton.IsEnabled && InfoNotice.Tone == NoticeTone.Loading && BusyButton.ActualWidth == taskWidth, "sample loading state has a real operation and stable geometry");
        await sampleTask;
        Check(Progress.Value == 100 && BusyButton.IsEnabled && Feedback.Tone == NoticeTone.Success, "sample task completes locally");
        UpdateLayout(); Check(Math.Abs(Feedback.ActualHeight - feedbackHeight) < 1, "feedback changes do not move sections"); Progress.Value = 60;
        Scroller.ScrollToEnd(); UpdateLayout(); Check(Scroller.VerticalOffset > 0, "scrolling to overflow content"); Scroller.ScrollToTop();
        Scroller.ScrollToEnd(); UpdateLayout();
        Check(Table.TransformToAncestor(Scroller).TransformBounds(new Rect(Table.RenderSize)).Bottom <= Scroller.ViewportHeight + 1, "lower controls fit the scroll viewport");
        var activeScrollers = 0; foreach (var scroll in VisualDescendants<ScrollViewer>(this)) if (scroll.ScrollableHeight > 0) activeScrollers++;
        Check(activeScrollers == 1, "only the outer gallery scrolls vertically");
        Check(Table.BorderBrush == FindResource("HairlineBrush"), "table frame is neutral without keyboard focus");
        Table.ScrollIntoView(point); Table.CurrentCell = new DataGridCellInfo(point, Table.Columns[0]); Table.BeginEdit(); UpdateLayout();
        var editing = Table.Columns[0].GetCellContent(point) as TextBox;
        Check(editing != null, "table enters native edit mode"); editing!.Text = "45";
        editing.Focus(); UpdateLayout();
        var cell = editing.Parent as DataGridCell;
        if (cell == null) foreach (var candidate in VisualDescendants<DataGridCell>(Table)) if (candidate.IsKeyboardFocusWithin) { cell = candidate; break; }
        Check(cell != null && ((Border)cell.Template.FindName("CellFocus", cell)).Visibility == Visibility.Visible, "table accent identifies the focused cell");
        Table.CommitEdit(DataGridEditingUnit.Cell, true); Table.CommitEdit(DataGridEditingUnit.Row, true);
        Check(point.Temperature == 45, "table edit commits through binding"); point.Temperature = 40; Table.Items.Refresh();
        Choices.SelectedIndex = 1; Check(Choices.SelectedItem != null && ((ListBoxItem)Choices.SelectedItem).IsSelected, "list selection"); Choices.SelectedIndex = 0;
        var indicator = (FrameworkElement)Progress.Template.FindName("PART_Indicator", Progress);
        var track = (FrameworkElement)Progress.Template.FindName("PART_Track", Progress);
        Check(track.ActualWidth > 0 && Math.Abs(indicator.ActualWidth / track.ActualWidth - 0.6) < 0.02, "determinate progress reflects value");
        var owner = Window.GetWindow(this);
        var tip = new ToolTip { Content = "Connect a device to enable this action. This explanation stays readable when the action is disabled.", PlacementTarget = DisabledButton, IsOpen = true };
        tip.UpdateLayout(); Check(tip.ActualWidth <= 360 && tip.ActualWidth > 0, "bounded tooltip"); tip.IsOpen = false;
        var dialog = new AppDialog(owner, "Preview confirmation", "No hardware operation is performed.", "Confirm", "Cancel");
        var cancelFocused = false;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
        {
            cancelFocused = dialog.CancelButton.IsKeyboardFocused;
            if (previewPath != null)
            {
                var body = (FrameworkElement)dialog.Content;
                var bitmap = new RenderTargetBitmap((int)(body.ActualWidth + body.Margin.Left + body.Margin.Right), (int)(body.ActualHeight + body.Margin.Top + body.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(dialog);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.ChangeExtension(previewPath, "dialog.png")); encoder.Save(stream);
            }
            dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Check(dialog.ShowDialog() == false && dialog.Owner == owner && dialog.CancelButton.IsDefault, "owned modal dialog safe cancellation");
        Check(cancelFocused, "dialog focuses safe cancellation");
        var accepted = new AppDialog(owner, "Preview confirmation", "No hardware operation is performed.", "Confirm", "Cancel");
        accepted.Loaded += (_, _) => accepted.Dispatcher.BeginInvoke(new Action(() => accepted.AcceptButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        Check(accepted.ShowDialog() == true, "dialog explicit confirmation");
        var escaped = new AppDialog(owner, "Preview confirmation", "Escape keeps sample changes.", "Confirm", "Cancel");
        escaped.Loaded += (_, _) => escaped.Dispatcher.BeginInvoke(new Action(() => escaped.RaiseEvent(new KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(escaped)!, Environment.TickCount, Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent })));
        Check(escaped.ShowDialog() == false, "Escape dismisses dialog safely");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(System.Windows.Input.Keyboard.FocusedElement is DependencyObject focus && Window.GetWindow(focus) == owner, "dialog returns focus to owner");
        var noticePeer = UIElementAutomationPeer.CreatePeerForElement(Feedback);
        Check(noticePeer != null && noticePeer.GetName().Contains("completed") && AutomationProperties.GetLiveSetting(Feedback) == AutomationLiveSetting.Polite, "notification accessibility");
        ResetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Feedback.Tone == NoticeTone.Neutral && Feedback.Content.ToString() == PreviewMessage && Progress.Value == 60, "reset restores sample values");
        Primary.Focus();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Scroller.ScrollToTop();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        return new { passed, configurationWrites = 0, dialogs = "actual owned modal windows", reducedMotion = !SystemParameters.ClientAreaAnimation, sliderArrowChecked, sliderModifiers = sliderModifiers.ToString() };
    }
}
