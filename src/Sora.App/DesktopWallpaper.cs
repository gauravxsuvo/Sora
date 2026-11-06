using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Sora.App;

internal sealed class DesktopWallpaper : IDisposable
{
    private const int ChildWindow = 0x40000000;
    private const int Visible = 0x10000000;
    private const int NoActivate = 0x08000000;
    private const int Layered = 0x00080000;
    private const int Transparent = 0x00000020;
    private const int NoRedirectionBitmap = 0x00200000;
    private const int ExtendedStyleIndex = -20;
    private const uint CreateWallpaperLayer = 0x052C;
    private const uint AbortIfHung = 0x0002;
    private const uint NextWindow = 2;
    private const uint LayeredAlpha = 2;
    private const uint NoActivatePosition = 0x0010;
    private const uint ShowWindow = 0x0040;
    private const uint NoZOrder = 0x0004;
    private HwndSource? surface;
    private IntPtr parent, icons, hostWindow;
    private int width, height;

    internal bool Show(MediaPlayer player)
    {
        if (surface is { IsDisposed: false } && IsWindow(surface.Handle) && IsWindow(hostWindow) && IsWindow(parent) &&
            (icons == IntPtr.Zero || IsWindow(icons)))
        {
            if (Position()) return true;
        }
        Dispose();
        if (!FindLayer() || !GetClientRect(parent, out var bounds) || bounds.Right <= 0 || bounds.Bottom <= 0)
            return false;

        // Explorer's raised desktop composites a constant-alpha child. WPF's per-pixel
        // layered window can render frames internally without showing them on that desktop.
        hostWindow = CreateWindowEx(NoActivate | Layered | Transparent, "Static", "Sora Desktop Wallpaper Host", ChildWindow,
            0, 0, bounds.Right, bounds.Bottom, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hostWindow == IntPtr.Zero || !SetLayeredWindowAttributes(hostWindow, 0, 255, LayeredAlpha))
        {
            Dispose();
            return false;
        }
        surface = new HwndSource(new HwndSourceParameters("Sora Desktop Wallpaper", bounds.Right, bounds.Bottom)
        {
            ParentWindow = hostWindow,
            WindowStyle = ChildWindow | Visible,
            ExtendedWindowStyle = NoActivate | Transparent
        });
        var video = new VideoDrawing
        {
            Player = player,
            Rect = new Rect(0, 0, player.NaturalVideoWidth, player.NaturalVideoHeight)
        };
        surface.RootVisual = new Border
        {
            Background = new DrawingBrush(video) { Stretch = Stretch.UniformToFill },
            IsHitTestVisible = false
        };
        if (Position()) return true;
        Dispose();
        return false;
    }

    private bool Position()
    {
        if (!GetClientRect(parent, out var bounds) || bounds.Right <= 0 || bounds.Bottom <= 0)
            return false;
        if (width == bounds.Right && height == bounds.Bottom &&
            (icons == IntPtr.Zero || GetWindow(icons, NextWindow) == hostWindow))
            return true;

        var flags = NoActivatePosition | ShowWindow;
        if (icons == IntPtr.Zero) flags |= NoZOrder;
        if (!SetWindowPos(hostWindow, icons, 0, 0, bounds.Right, bounds.Bottom, flags)) return false;
        if (!SetWindowPos(surface!.Handle, IntPtr.Zero, 0, 0, bounds.Right, bounds.Bottom, NoActivatePosition | NoZOrder)) return false;
        width = bounds.Right;
        height = bounds.Bottom;
        return true;
    }

    private bool FindLayer()
    {
        var progman = FindWindow("Progman", null);
        var shellThread = GetWindowThreadProcessId(GetShellWindow(), out var shellProcess);
        if (progman == IntPtr.Zero || shellThread == 0 ||
            GetWindowThreadProcessId(progman, out var progmanProcess) == 0 || progmanProcess != shellProcess)
            return false;

        // Explorer creates a wallpaper layer below the desktop icon view when it receives this message.
        SendMessageTimeout(progman, CreateWallpaperLayer, new IntPtr(0xD), new IntPtr(1), AbortIfHung, 250, out _);
        if ((GetWindowLong(progman, ExtendedStyleIndex) & NoRedirectionBitmap) != 0)
        {
            // Newer Explorer builds use sibling child windows inside Progman.
            icons = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (icons == IntPtr.Zero || FindWindowEx(progman, icons, "WorkerW", null) == IntPtr.Zero)
                return false;
            parent = progman;
            return true;
        }

        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var process);
            if (process != shellProcess || FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                return true;
            var worker = FindWindowEx(IntPtr.Zero, window, "WorkerW", null);
            if (worker != IntPtr.Zero && GetWindowThreadProcessId(worker, out process) != 0 && process == shellProcess &&
                FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                parent = worker;
            return false;
        }, IntPtr.Zero);
        return parent != IntPtr.Zero;
    }

    public void Dispose()
    {
        surface?.Dispose();
        surface = null;
        if (hostWindow != IntPtr.Zero) DestroyWindow(hostWindow);
        hostWindow = IntPtr.Zero;
        parent = icons = IntPtr.Zero;
        width = height = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool WindowCallback(IntPtr window, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
