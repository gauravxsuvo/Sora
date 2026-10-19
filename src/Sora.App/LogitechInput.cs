using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Sora.Core;

namespace Sora.App;

internal sealed class LogitechInput : ILogitechOutput
{
    private readonly object sync = new();
    private readonly Dictionary<int, int> keys = [];
    private readonly Dictionary<string, int> mouse = [];
    public void Key(int key, bool down)
    {
        lock (sync)
        {
            keys.TryGetValue(key, out var held);
            if (down) { if (held == 0) Send(new Input { Type = 1, Data = new Union { Key = new KeyInput { VirtualKey = (ushort)key, Flags = Extended(key) ? 1u : 0u } } }); keys[key] = held + 1; }
            else if (held > 0) { if (held == 1) { Send(new Input { Type = 1, Data = new Union { Key = new KeyInput { VirtualKey = (ushort)key, Flags = 2u | (Extended(key) ? 1u : 0u) } } }); keys.Remove(key); } else keys[key] = held - 1; }
        }
    }
    public void Mouse(string kind, bool down)
    {
        lock (sync)
        {
            mouse.TryGetValue(kind, out var held);
            if (down) { if (held == 0) SendMouse(kind, true); mouse[kind] = held + 1; }
            else if (held > 0) { if (held == 1) { SendMouse(kind, false); mouse.Remove(kind); } else mouse[kind] = held - 1; }
        }
    }
    private static void SendMouse(string kind, bool down)
    {
        var (flag, data) = kind switch { "MouseRight" => (down ? 8u : 16u, 0u), "MouseMiddle" => (down ? 32u : 64u, 0u), "MouseBack" => (down ? 128u : 256u, 1u), "MouseForward" => (down ? 128u : 256u, 2u), _ => throw new InvalidDataException("Unsupported mouse action.") };
        Send(new Input { Type = 0, Data = new Union { Mouse = new MouseInput { Flags = flag, Data = data } } });
    }
    public void Scroll(bool horizontal, int delta) => Send(new Input { Type = 0, Data = new Union { Mouse = new MouseInput { Flags = horizontal ? 0x1000u : 0x800u, Data = unchecked((uint)delta) } } });
    public void Launch(string target, bool url)
    {
        if (url) { if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new InvalidDataException("Only HTTP/HTTPS links can be launched."); }
        else if (!Path.IsPathFullyQualified(target) || !File.Exists(target) || !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("The assigned application was not found.", target);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
    public void ReleaseAll()
    {
        lock (sync)
        {
            foreach (var key in new List<int>(keys.Keys)) { keys[key] = 1; Key(key, false); }
            foreach (var kind in new List<string>(mouse.Keys)) { mouse[kind] = 1; Mouse(kind, false); }
        }
    }
    private static bool Extended(int key) => key is >= 0x21 and <= 0x28 or 0x2d or 0x2e or 0x5b or 0x5c or 0xa3 or 0xa5;
    private static void Send(Input input) { if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the assigned input."); }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
