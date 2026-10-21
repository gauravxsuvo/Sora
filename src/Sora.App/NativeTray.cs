using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Sora.App;

// Notification-area icon built on Shell_NotifyIcon and a plain hidden Win32 window.
// The icon stays native; its themed WPF menu is created only when opened.
internal sealed class NativeTray : IDisposable
{
    public sealed record MenuItem(string Text, Action Run, bool Enabled = true, string Icon = "IconInfo");
    private ContextMenu? popup;

    private const int WM_NULL = 0x0000, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_TRAY = 0x8001;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const int MF_STRING = 0, MF_GRAYED = 1, MF_SEPARATOR = 0x800, TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;
    private const string ClassName = "SoraTrayWindow";

    private readonly WndProcDelegate proc;
    private readonly IntPtr hwnd, icon;
    private readonly int taskbarCreated;
    private readonly Func<IReadOnlyList<MenuItem?>> menu;
    private string tooltip;
    private bool disposed;

    public event Action? Activated;

    public NativeTray(string tooltip, Func<IReadOnlyList<MenuItem?>> menu)
    {
        this.tooltip = tooltip;
        this.menu = menu;
        proc = WndProc;
        var instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEXW { cbSize = Marshal.SizeOf<WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc), hInstance = instance, lpszClassName = ClassName };
        if (RegisterClassExW(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410) throw new Win32Exception();
        // A hidden top-level window, not a message-only one, so Explorer's TaskbarCreated broadcast still arrives.
        hwnd = CreateWindowExW(0, ClassName, "Sora", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new Win32Exception();
        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        if (Environment.ProcessPath is { } exe) ExtractIconExW(exe, 0, IntPtr.Zero, out icon, 1);
        Notify(NIM_ADD);
    }

    public void SetTooltip(string text)
    {
        text = text.Length > 127 ? text[..127] : text;
        if (text == tooltip || disposed) return;
        tooltip = text;
        Notify(NIM_MODIFY);
    }

    public void ShowNotification(string title, string message)
    {
        if (disposed) return;
        var data = new NOTIFYICONDATAW { cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = hwnd, uID = 1, uFlags = 0x10,
            szTip = "", szInfoTitle = title.Length > 63 ? title[..63] : title, szInfo = message.Length > 255 ? message[..255] : message, dwInfoFlags = 1 };
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void Notify(int message)
    {
        var data = new NOTIFYICONDATAW { cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = hwnd, uID = 1, uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_TRAY, hIcon = icon, szTip = tooltip, szInfo = "", szInfoTitle = "" };
        Shell_NotifyIconW(message, ref data);
    }

    private IntPtr WndProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_TRAY)
        {
            var mouse = (int)lParam.ToInt64();
            if (mouse == WM_LBUTTONUP) Activated?.Invoke();
            else if (mouse == WM_RBUTTONUP) ShowMenu();
            return IntPtr.Zero;
        }
        if (message == taskbarCreated && !disposed) Notify(NIM_ADD);
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        if (popup != null) popup.IsOpen = false;
        var opened = BuildMenu(menu()); popup = opened;
        opened.Placement = PlacementMode.MousePoint;
        opened.Opened += (_, _) => opened.Focus();
        opened.Closed += (_, _) => { if (ReferenceEquals(popup, opened)) popup = null; PostMessageW(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero); };
        SetForegroundWindow(hwnd);
        opened.IsOpen = true;
    }

    internal static ContextMenu BuildMenu(IReadOnlyList<MenuItem?> items)
    {
        var result = new ContextMenu { Style = (Style)Application.Current.FindResource("TrayMenu") };
        System.Windows.Automation.AutomationProperties.SetName(result, "Sora tray menu");
        foreach (var item in items)
        {
            if (item == null) { result.Items.Add(new Separator { Style = (Style)Application.Current.FindResource("TrayMenuSeparator") }); continue; }
            var row = new System.Windows.Controls.MenuItem
            {
                Header = item.Text, IsEnabled = item.Enabled,
                Style = (Style)Application.Current.FindResource("TrayMenuItem"),
                Icon = new ContentControl { Content = Application.Current.FindResource(item.Icon), Style = (Style)Application.Current.FindResource("LineIcon"), Width = 16, Height = 16 }
            };
            System.Windows.Automation.AutomationProperties.SetName(row, item.Text);
            row.Click += (_, _) => { result.IsOpen = false; Application.Current.Dispatcher.BeginInvoke(item.Run); };
            result.Items.Add(row);
        }
        return result;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (popup != null) popup.IsOpen = false;
        var data = new NOTIFYICONDATAW { cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = hwnd, uID = 1, szTip = "", szInfo = "", szInfoTitle = "" };
        Shell_NotifyIconW(NIM_DELETE, ref data);
        if (icon != IntPtr.Zero) DestroyIcon(icon);
        DestroyWindow(hwnd);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize; public IntPtr hWnd; public int uID; public int uFlags; public int uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATAW data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconExW(string file, int index, IntPtr large, out IntPtr small, int count);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, int flags, IntPtr id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, int flags, int x, int y, IntPtr hwnd, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessageW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
}
