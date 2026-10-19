using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sora.App;

/// <summary>Owned modal application messages. File pickers retain their native Windows dialogs.</summary>
internal sealed class AppDialog : Window
{
    internal Button AcceptButton { get; }
    internal Button CancelButton { get; }

    internal AppDialog(Window? owner, string title, string message, string accept, string? cancel)
    {
        Title = "Sora · " + title; Owner = owner;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Sora;component/Assets/Sora.ico"));
        Width = 480; MinWidth = 360; MaxWidth = 640; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 80);
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = owner == null;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush"); SetResourceReference(FontFamilyProperty, "UIFont");
        var root = new DockPanel { Margin = new Thickness(24) };
        var returnFocus = Keyboard.FocusedElement as UIElement;
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        InteractionMotion.SetReveal(root, true);
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetHelpText(this, message);
        if (owner != null) ControlState.SetReduceMotion(this, ControlState.GetReduceMotion(owner));
        Closed += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (owner?.IsVisible == true && returnFocus is { IsVisible: true, IsEnabled: true, Focusable: true }) returnFocus.Focus();
        }));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        AcceptButton = new Button { Content = accept, MinWidth = 96, IsDefault = cancel == null };
        AcceptButton.SetResourceReference(StyleProperty, "PrimaryButton");
        AcceptButton.Click += (_, _) => DialogResult = true;
        CancelButton = new Button { Content = cancel ?? "Close", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true, IsDefault = cancel != null };
        CancelButton.Click += (_, _) => DialogResult = false;
        if (cancel != null) actions.Children.Add(CancelButton);
        actions.Children.Add(AcceptButton);
        var body = new StackPanel();
        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 10) }; heading.SetResourceReference(StyleProperty, "SectionText"); body.Children.Add(heading);
        var text = new TextBlock { Text = message }; text.SetResourceReference(StyleProperty, "SecondaryText");
        AutomationProperties.SetName(text, message); body.Children.Add(text);
        root.Children.Add(new ScrollViewer { Content = body, MaxHeight = Math.Max(140, MaxHeight - 140), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
        Loaded += (_, _) => (cancel == null ? AcceptButton : CancelButton).Focus();
        // A closing popup can queue focus restoration after Loaded. Establish the
        // safe initial target once native activation and content rendering finish.
        ContentRendered += (_, _) => (cancel == null ? AcceptButton : CancelButton).Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    internal static bool Confirm(Window? owner, string title, string message, string accept, string cancel = "Cancel") => new AppDialog(owner, title, message, accept, cancel).ShowDialog() == true;
    internal static void ShowMessage(Window? owner, string title, string message) => new AppDialog(owner, title, message, "OK", null).ShowDialog();
}
