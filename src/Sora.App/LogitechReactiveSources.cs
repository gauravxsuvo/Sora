using System;
using System.Runtime.InteropServices;

namespace Sora.App;

internal static class LogitechScreenSampler
{
    internal static string Sample(string area)
    {
        var screen = GetDC(IntPtr.Zero); if (screen == IntPtr.Zero) throw new InvalidOperationException("The desktop image is unavailable.");
        var memory = IntPtr.Zero; var bitmap = IntPtr.Zero; var old = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen); var info = new BitmapInfo { Size = 40, Width = 16, Height = -9, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out var pixels, IntPtr.Zero, 0);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero || pixels == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate the screen sampler.");
            old = SelectObject(memory, bitmap); var width = GetSystemMetrics(0); var height = GetSystemMetrics(1); var x = area == "Right half" ? width / 2 : 0;
            if (area != "Whole screen") width /= 2;
            SetStretchBltMode(memory, 4);
            if (!StretchBlt(memory, 0, 0, 16, 9, screen, x, 0, width, height, 0x00cc0020)) throw new InvalidOperationException("Screen sampling failed. Protected content may not be available.");
            var data = new byte[16 * 9 * 4]; Marshal.Copy(pixels, data, 0, data.Length); int r = 0, g = 0, b = 0;
            for (var i = 0; i < data.Length; i += 4) { b += data[i]; g += data[i + 1]; r += data[i + 2]; }
            return $"{r / 144:X2}{g / 144:X2}{b / 144:X2}";
        }
        finally { if (old != IntPtr.Zero) SelectObject(memory, old); if (bitmap != IntPtr.Zero) DeleteObject(bitmap); if (memory != IntPtr.Zero) DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int Xppm, Yppm; public uint Used, Important; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StretchBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint operation);
}

internal sealed class LogitechOutputMeter : IDisposable
{
    private IAudioMeter? meter;
    private string? endpoint;
    private DateTime checkedAt;
    public float Peak()
    {
        if (meter == null || DateTime.UtcNow - checkedAt > TimeSpan.FromSeconds(2))
        {
            var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator(); IMMDevice? device = null;
            try
            {
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 0, out device)); Marshal.ThrowExceptionForHR(device.GetId(out var id));
                if (id != endpoint || meter == null)
                {
                    Dispose(); var guid = typeof(IAudioMeter).GUID; Marshal.ThrowExceptionForHR(device.Activate(ref guid, 23, IntPtr.Zero, out var result));
                    meter = (IAudioMeter)result; endpoint = id;
                }
                checkedAt = DateTime.UtcNow;
            }
            finally { if (device != null) Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator); }
        }
        Marshal.ThrowExceptionForHR(meter!.GetPeakValue(out var value)); return Math.Clamp(value, 0, 1);
    }
    public void Dispose() { if (meter != null) Marshal.ReleaseComObject(meter); meter = null; endpoint = null; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int state, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioMeter
    {
        [PreserveSig] int GetPeakValue(out float value);
    }
}
