using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sora.Core;

public sealed record DeviceInfo(string Path, string SerialNumber, ushort UsagePage, ushort Usage, ushort OutputReportLength, ushort FeatureReportLength = 0, ushort VendorId = 0, ushort ProductId = 0, string ProductName = "");

public interface IDisplayTransport : IAsyncDisposable
{
    Task WriteAsync(byte[] report, CancellationToken token);
}

public sealed class HidDevice : IDisplayTransport
{
    private readonly FileStream stream;
    private readonly int outputLength;
    private HidDevice(FileStream stream, int outputLength) { this.stream = stream; this.outputLength = outputLength; }

    public static IReadOnlyList<DeviceInfo> Enumerate() => EnumerateMatching(path => path.Contains("vid_3633&pid_000a", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<DeviceInfo> FindLogitech() => EnumerateMatching(path => path.Contains("vid_046d", StringComparison.OrdinalIgnoreCase));

    internal static IReadOnlyList<DeviceInfo> EnumerateMatching(Func<string, bool> matches, bool includeDeviceStrings = true,
        bool throwOnMatchedDeviceFailure = false)
    {
        Native.HidD_GetHidGuid(out var guid);
        var set = Native.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var results = new List<DeviceInfo>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var entry = new Native.DeviceInterfaceData { Size = Marshal.SizeOf<Native.DeviceInterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref entry))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                Native.SetupDiGetDeviceInterfaceDetail(set, ref entry, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                if (required is < 8 or > 65536) continue;
                var detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref entry, detail, required, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4)!;
                    if (!matches(path)) continue;
                    using var handle = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid)
                    {
                        if (throwOnMatchedDeviceFailure) throw new Win32Exception(Marshal.GetLastWin32Error());
                        continue;
                    }
                    var attributes = new Native.HidAttributes { Size = Marshal.SizeOf<Native.HidAttributes>() };
                    if (!Native.HidD_GetAttributes(handle, ref attributes))
                    {
                        if (throwOnMatchedDeviceFailure) throw new Win32Exception(Marshal.GetLastWin32Error());
                        continue;
                    }
                    var caps = GetCaps(handle);
                    if (!includeDeviceStrings)
                    {
                        results.Add(new DeviceInfo(path, "", caps.UsagePage, caps.Usage, caps.OutputReportByteLength,
                            caps.FeatureReportByteLength, attributes.VendorId, attributes.ProductId));
                        continue;
                    }
                    var serialBuffer = Marshal.AllocHGlobal(512);
                    string serial;
                    try { serial = Native.HidD_GetSerialNumberString(handle, serialBuffer, 512) ? Marshal.PtrToStringUni(serialBuffer) ?? "" : ""; }
                    finally { Marshal.FreeHGlobal(serialBuffer); }
                    var productBuffer = Marshal.AllocHGlobal(512);
                    string product;
                    try { product = Native.HidD_GetProductString(handle, productBuffer, 512) ? Marshal.PtrToStringUni(productBuffer) ?? "" : ""; }
                    finally { Marshal.FreeHGlobal(productBuffer); }
                    results.Add(new DeviceInfo(path, serial, caps.UsagePage, caps.Usage, caps.OutputReportByteLength, caps.FeatureReportByteLength, attributes.VendorId, attributes.ProductId, product));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return results;
    }

    /// <summary>LD-series display interfaces that are plugged in. Read-only: nothing is written and no handle is kept.</summary>
    public static IReadOnlyList<DeviceInfo> FindDisplays() => Enumerate().Where(device => device.VendorId == LdProtocol.VendorId && device.ProductId == LdProtocol.ProductId && device.UsagePage is 0x00ff or 0xff00 && device.Usage == 1 && device.OutputReportLength >= 18).ToArray();

    public static IDisplayTransport Open()
    {
        var devices = FindDisplays();
        if (devices.Count != 1) throw new IOException(devices.Count == 0 ? "LD360 not found. Check its USB connection." : "Multiple LD-series displays found. Connect one to use Sora.");
        var device = devices[0];
        var handle = Native.CreateFile(device.Path, 0x40000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the LD360. Exit DeepCool and try again."); }
        try
        {
            var caps = GetCaps(handle);
            if (caps.OutputReportByteLength is < 18 or > 4096) throw new IOException("Unexpected LD360 report length.");
            return new HidDevice(new FileStream(handle, FileAccess.Write, caps.OutputReportByteLength, true), caps.OutputReportByteLength);
        }
        catch { handle.Dispose(); throw; }
    }

    public async Task WriteAsync(byte[] report, CancellationToken token)
    {
        if (report.Length > outputLength || report.Length is not (9 or 18) || report[0] != 0x10) throw new InvalidDataException("Unexpected LD-series report.");
        var padded = new byte[outputLength];
        report.CopyTo(padded, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(2000);
        await stream.WriteAsync(padded, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
    public ValueTask DisposeAsync() => stream.DisposeAsync();

    internal static Native.HidCaps GetCaps(SafeFileHandle handle)
    {
        if (!Native.HidD_GetPreparsedData(handle, out var preparsed)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (Native.HidP_GetCaps(preparsed, out var caps) != 0x00110000) throw new IOException("Cannot read HID report capabilities.");
            return caps;
        }
        finally { Native.HidD_FreePreparsedData(preparsed); }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceInterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct HidAttributes { public int Size; public ushort VendorId, ProductId, Version; }
        [StructLayout(LayoutKind.Sequential)] internal struct HidCaps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort LinkCollectionNodes, InputButtonCaps, InputValueCaps, InputDataIndices, OutputButtonCaps, OutputValueCaps, OutputDataIndices, FeatureButtonCaps, FeatureValueCaps, FeatureDataIndices;
        }
        [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetSerialNumberString(SafeFileHandle handle, IntPtr buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetProductString(SafeFileHandle handle, IntPtr buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_SetNumInputBuffers(SafeFileHandle handle, int count);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref DeviceInterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, uint length, out uint required, IntPtr device);
        [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    }
}
