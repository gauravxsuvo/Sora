using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Sora.Core;

public enum HardwareModule { Cooler, Keyboard, Lighting, MotherboardFans, GpuFans, Storage, Wifi, Logitech }
public enum HardwarePresence { Unknown, Present, Absent, DetectionFailed }
public sealed record PresentHardware(string Name, string ClassName, string HardwareIds);
public sealed record HardwareAvailability(HardwareModule Module, HardwarePresence Presence)
{
    public bool Available => Presence == HardwarePresence.Present;
}
public sealed record HardwareInventory(HardwareAvailability[] Modules)
{
    public HardwarePresence Presence(HardwareModule module) => Modules.SingleOrDefault(m => m.Module == module)?.Presence ?? HardwarePresence.Unknown;
    public bool Available(HardwareModule module) => Presence(module) == HardwarePresence.Present;
    public static HardwareInventory Unknown { get; } = new(Enum.GetValues<HardwareModule>().Select(m => new HardwareAvailability(m, HardwarePresence.Unknown)).ToArray());
}

/// <summary>Present-device enumeration only. Never opens hardware configuration interfaces or loads a hardware driver.</summary>
public static class HardwareDiscovery
{
    public static HardwareInventory Classify(IEnumerable<PresentHardware> devices, string boardManufacturer, string boardProduct)
    {
        var all = devices.ToArray();
        bool Usb(string id) => all.Any(d => d.HardwareIds.Contains(id, StringComparison.OrdinalIgnoreCase));
        bool IsClass(PresentHardware d, string name) => d.ClassName.Equals(name, StringComparison.OrdinalIgnoreCase);
        var supportedBoard = boardManufacturer.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase)
            && boardProduct.Trim().Equals("Z890 GAMING X WIFI7", StringComparison.OrdinalIgnoreCase);
        bool Found(HardwareModule module) => module switch
        {
            HardwareModule.Cooler => Usb("VID_3633&PID_000A"),
            HardwareModule.Keyboard => Usb("VID_258A&PID_010C") || Usb("VID_3554&PID_FA09"),
            HardwareModule.Lighting => Usb("VID_048D&PID_5711") || supportedBoard,
            HardwareModule.MotherboardFans => supportedBoard,
            HardwareModule.GpuFans => all.Any(d => IsClass(d, "Display") && d.HardwareIds.Contains("VEN_10DE&", StringComparison.OrdinalIgnoreCase)),
            HardwareModule.Storage => all.Any(d => IsClass(d, "DiskDrive")),
            HardwareModule.Wifi => all.Any(d => IsClass(d, "Net") && new[] { "wi-fi", "wifi", "wireless", "802.11" }.Any(word => d.Name.Contains(word, StringComparison.OrdinalIgnoreCase))),
            // Include unqualified Logitech devices in discovery so the UI can explain their support status.
            HardwareModule.Logitech => new[] { "VID_046D&PID_C08B", "VID_046D&PID_C084", "VID_046D&PID_C092", "VID_046D&PID_C09D" }.Any(Usb)
                || all.Any(d => IsClass(d, "Mouse") && d.HardwareIds.Contains("VID_046D&", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
        return new(Enum.GetValues<HardwareModule>().Select(m => new HardwareAvailability(m, Found(m) ? HardwarePresence.Present : HardwarePresence.Absent)).ToArray());
    }

    [SupportedOSPlatform("windows")]
    public static HardwareInventory Capture()
    {
        try
        {
            var devices = ReadPresentDevices();
            try
            {
                using var bios = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (bios == null) return WithoutBoardIdentity(devices);
                return Classify(devices, bios.GetValue("BaseBoardManufacturer") as string ?? "", bios.GetValue("BaseBoardProduct") as string ?? "");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return WithoutBoardIdentity(devices);
            }
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(Enum.GetValues<HardwareModule>().Select(m => new HardwareAvailability(m, HardwarePresence.DetectionFailed)).ToArray());
        }
    }

    public static HardwareInventory WithoutBoardIdentity(IEnumerable<PresentHardware> devices)
    {
        var inventory = Classify(devices, "", "");
        return new(inventory.Modules.Select(m => m.Module == HardwareModule.MotherboardFans
            ? m with { Presence = HardwarePresence.DetectionFailed } : m).ToArray());
    }

    [SupportedOSPlatform("windows")]
    private static PresentHardware[] ReadPresentDevices()
    {
        var set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 6); // PRESENT | ALLCLASSES
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new List<PresentHardware>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var device = new DeviceData { Size = Marshal.SizeOf<DeviceData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref device))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                string Property(uint property)
                {
                    var buffer = new byte[4096];
                    uint type;
                    while (!SetupDiGetDeviceRegistryProperty(set, ref device, property, out type, buffer, (uint)buffer.Length, out var required))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == 13) return ""; // Optional property is not supplied by this device.
                        if (error == 122 && required > buffer.Length && required <= 65536) { buffer = new byte[required]; continue; }
                        throw new Win32Exception(error);
                    }
                    return type is 1 or 7 ? Encoding.Unicode.GetString(buffer).TrimEnd('\0').Replace('\0', ' ') : "";
                }
                var name = Property(12);
                if (name.Length == 0) name = Property(0);
                result.Add(new(name, Property(7), Property(1)));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceData { public int Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DeviceData data, uint property, out uint type, byte[] buffer, uint length, out uint required);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
