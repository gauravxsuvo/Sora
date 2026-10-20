using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Sora.Core;

[SupportedOSPlatform("windows")]
public sealed class LenovoLegionWindows : ILegionTransport, ILegionSpectrumTransport, ILegionGpuTransport
{
    public LenovoSystemIdentity Identity => LenovoSystemIdentity.Capture();
    public bool IsAcConnected()
    {
        if (!GetSystemPowerStatus(out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return status.AcLineStatus == 1; // Unknown is not permission to request Performance.
    }

    public string[] Conflicts()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LenovoVantageService", "Lenovo.Vantage", "LenovoVantage", "Lenovo.Modern.ImController",
            "LZService", "LegionZone", "LegionSpace", "LegionSpaceService", "LenovoLegionToolkit", "iCUE", "OpenRGB"
        };
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (names.Contains(process.ProcessName)) found.Add(process.ProcessName); }
                catch (InvalidOperationException) { } // Process exited between enumeration and inspection.
            }
        }
        return found.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public LegionPowerMode[] ReadSupportedPowerModes()
    {
        var version = Gamezone("IsSupportSmartFan");
        if (version == 0) throw new NotSupportedException("The Lenovo smart-fan/power interface is unavailable.");
        uint mask;
        try { mask = Other("GetFeatureValue", "Value", "IDs", 0x00070000); }
        catch (Exception error) when (MissingInterface(error))
        {
            try { mask = Other("GetSupportThermalMode", "mode"); }
            catch (Exception fallback) when (MissingInterface(fallback))
            {
                // The legacy Toolkit power feature advertises the three ordinary presets when smart-fan support exists.
                mask = 7;
            }
        }
        var modes = new List<LegionPowerMode>();
        if ((mask & 1) != 0) modes.Add(LegionPowerMode.Quiet);
        if ((mask & 2) != 0) modes.Add(LegionPowerMode.Balanced);
        if ((mask & 4) != 0) modes.Add(LegionPowerMode.Performance);
        return modes.ToArray(); // Custom and Extreme require their own capability/tuning paths.
    }
    public uint ReadPowerMode() => Gamezone("GetSmartFanMode");
    public LegionGpuState ReadGpu()
    {
        var toggle = OptionalSupport("IsSupportGSync");
        var workingMode = OptionalSupport("IsSupportIGPUMode");
        if (!toggle && !workingMode) throw new NotSupportedException("Firmware GPU switching is unavailable.");
        var discrete = toggle ? Gamezone("GetGSyncStatus") : 0;
        if (discrete > 1) throw new InvalidDataException("Unknown firmware graphics-switch state.");
        var igpu = workingMode ? Gamezone("GetIGPUModeStatus") : 0;
        if (igpu > 2) throw new InvalidDataException("Unknown firmware iGPU working mode.");
        var modes = new List<LegionGpuMode> { LegionGpuMode.Hybrid };
        if (workingMode) modes.AddRange([LegionGpuMode.IntegratedOnly, LegionGpuMode.Auto]);
        if (toggle) modes.Add(LegionGpuMode.Discrete);
        var current = discrete == 1 ? LegionGpuMode.Discrete : igpu switch
        { 0 => LegionGpuMode.Hybrid, 1 => LegionGpuMode.IntegratedOnly, 2 => LegionGpuMode.Auto, _ => throw new InvalidDataException() };
        return new(current, modes.ToArray());
    }
    public void WriteGpu(LegionGpuMode mode)
    {
        if (!ReadGpu().AvailableModes.Contains(mode)) throw new NotSupportedException("Unsupported firmware GPU mode.");
        if (OptionalSupport("IsSupportGSync"))
        {
            var value = mode == LegionGpuMode.Discrete ? 1u : 0u;
            if (Gamezone("GetGSyncStatus") != value)
                Invoke("LENOVO_GAMEZONE_DATA", "887B54E3-DDDC-4B2C-8B88-68A26A8835D0", "SetGSyncStatus", null, "Data", value);
        }
        if (OptionalSupport("IsSupportIGPUMode"))
        {
            var value = mode switch { LegionGpuMode.IntegratedOnly => 1u, LegionGpuMode.Auto => 2u, _ => 0u };
            if (Gamezone("GetIGPUModeStatus") != value)
                Invoke("LENOVO_GAMEZONE_DATA", "887B54E3-DDDC-4B2C-8B88-68A26A8835D0", "SetIGPUModeStatus", null, "mode", value);
        }
    }
    private bool OptionalSupport(string method)
    {
        try { return Gamezone(method) != 0; }
        catch (NotSupportedException) { return false; }
    }
    private static bool MissingInterface(Exception error) => error is NotSupportedException or
        ManagementException { ErrorCode: ManagementStatus.InvalidClass or ManagementStatus.NotFound or ManagementStatus.InvalidNamespace };
    public void WritePowerMode(uint value)
    {
        _ = LenovoLegionProtocol.DecodePowerMode(value);
        Invoke("LENOVO_GAMEZONE_DATA", "887B54E3-DDDC-4B2C-8B88-68A26A8835D0", "SetSmartFanMode", null, "Data", value);
    }
    private uint Gamezone(string method) => Invoke("LENOVO_GAMEZONE_DATA", "887B54E3-DDDC-4B2C-8B88-68A26A8835D0", method, "Data");
    private uint Other(string method, string output, string? input = null, uint value = 0) =>
        Invoke("LENOVO_OTHER_METHOD", "DC2A8805-3A8C-41BA-A6F7-092E0089CD3B", method, output, input, value);

    private uint Invoke(string className, string expectedGuid, string method, string? output, string? inputName = null, uint inputValue = 0)
    {
        EnsureLaptop();
        var scope = new ManagementScope(@"\\.\root\WMI", new ConnectionOptions { Timeout = TimeSpan.FromSeconds(3) });
        using var schema = new ManagementClass(scope, new ManagementPath(className), new ObjectGetOptions { Timeout = TimeSpan.FromSeconds(3) });
        schema.Get();
        var qualifiers = schema.Qualifiers.Cast<QualifierData>().ToArray();
        var guid = qualifiers.FirstOrDefault(q => q.Name.Equals("guid", StringComparison.OrdinalIgnoreCase))?.Value as string;
        var provider = qualifiers.FirstOrDefault(q => q.Name.Equals("provider", StringComparison.OrdinalIgnoreCase))?.Value as string;
        if (!Guid.TryParse(guid, out var actual) || actual != Guid.Parse(expectedGuid) || !string.Equals(provider, "WmiProv", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("The Lenovo firmware interface has an unexpected GUID or provider.");
        var definition = schema.Methods.Cast<MethodData>().FirstOrDefault(m => m.Name.Equals(method, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException("Firmware method unavailable: " + method);
        using var input = definition.InParameters;
        var fields = input?.Properties.Cast<PropertyData>().ToArray() ?? [];
        if (fields.Length != (inputName == null ? 0 : 1) || (inputName != null &&
            !fields.Any(field => field.Name.Equals(inputName, StringComparison.OrdinalIgnoreCase) && field.Type == CimType.UInt32 && !field.IsArray)))
            throw new NotSupportedException("The Lenovo method has an unexpected input schema: " + method);
        if (inputName != null) input![inputName] = inputValue;
        using var outputSchema = definition.OutParameters;
        if (output != null && !(outputSchema?.Properties.Cast<PropertyData>().Any(field => field.Name.Equals(output, StringComparison.OrdinalIgnoreCase)
            && field.Type == CimType.UInt32 && !field.IsArray) ?? false))
            throw new NotSupportedException("The Lenovo method has an unexpected output schema: " + method);
        using var search = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM " + className),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = false });
        using var objects = search.Get();
        using var iterator = objects.GetEnumerator();
        if (!iterator.MoveNext()) throw new NotSupportedException("The Lenovo firmware interface has no active instance.");
        using var instance = (ManagementObject)iterator.Current;
        var active = instance.Properties.Cast<PropertyData>().FirstOrDefault(property => property.Name.Equals("Active", StringComparison.OrdinalIgnoreCase));
        if (active?.Value is bool isActive && !isActive) throw new NotSupportedException("The Lenovo firmware interface is inactive.");
        if (iterator.MoveNext()) throw new NotSupportedException("Multiple Lenovo firmware instances are ambiguous.");
        using var result = instance.InvokeMethod(method, input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(3) });
        if (output == null) return 0; // Lenovo setters have a void contract, including a null result object.
        var value = result?.Properties.Cast<PropertyData>().FirstOrDefault(property => property.Name.Equals(output, StringComparison.OrdinalIgnoreCase))?.Value;
        return value is uint number ? number : throw new InvalidDataException("The Lenovo firmware response is missing a UInt32 value.");
    }

    public uint ChargeMode(uint request)
    {
        EnsureLaptop();
        if (request is not (0xff or 0x05 or 0x08 or 0x03 or 0x07)) throw new ArgumentOutOfRangeException(nameof(request));
        using var handle = HidDevice.Native.CreateFile(@"\\.\EnergyDrv", 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Lenovo Energy Management driver is unavailable or inaccessible.");
        if (!DeviceIoControl(handle, LenovoLegionProtocol.ChargeModeIoctl, ref request, 4, out var response, 4, out var returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The Lenovo charging driver rejected the request.");
        if (returned != 4) throw new InvalidDataException("The Lenovo charging driver returned an incomplete response.");
        return response;
    }

    public DeviceInfo[] FourZoneDevices()
    {
        EnsureLaptop();
        return HidDevice.EnumerateMatching(path => path.Contains("vid_048d&", StringComparison.OrdinalIgnoreCase),
            includeDeviceStrings: false, throwOnMatchedDeviceFailure: true).Where(LenovoLegionController.IsFourZone).ToArray();
    }
    public void WriteFourZone(DeviceInfo device, byte[] report)
    {
        EnsureLaptop();
        if (!LenovoLegionController.IsFourZone(device) || report.Length != 33 || report[0] != 0xcc || report[1] != 0x16)
            throw new ArgumentException("Invalid four-zone keyboard or report.");
        using var handle = HidDevice.Native.CreateFile(device.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var attributes = new HidDevice.Native.HidAttributes { Size = Marshal.SizeOf<HidDevice.Native.HidAttributes>() };
        if (!HidDevice.Native.HidD_GetAttributes(handle, ref attributes)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var caps = HidDevice.GetCaps(handle);
        if (attributes.VendorId != device.VendorId || attributes.ProductId != device.ProductId || caps.UsagePage != 0xff89 || caps.Usage != 0xcc || caps.FeatureReportByteLength != 33)
            throw new IOException("The keyboard identity changed; refresh before applying.");
        if (!HidDevice.Native.HidD_SetFeature(handle, report, report.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private void EnsureLaptop()
    {
        if (!Identity.IsLegionLaptop) throw new NotSupportedException("Lenovo Legion laptop hardware was not detected.");
    }
    public DeviceInfo[] SpectrumDevices()
    {
        EnsureLaptop();
        return HidDevice.EnumerateMatching(path => path.Contains("vid_048d&", StringComparison.OrdinalIgnoreCase),
            includeDeviceStrings: false, throwOnMatchedDeviceFailure: true).Where(LenovoLegionSpectrum.IsCandidate).ToArray();
    }
    public LegionSpectrumSetting ReadSpectrum(DeviceInfo device)
    {
        using var handle = OpenSpectrum(device);
        _ = SpectrumQuery(handle, 0xd1);
        return new(SpectrumQuery(handle, 0xcd), SpectrumQuery(handle, 0xca));
    }
    public void WriteSpectrum(DeviceInfo device, LegionSpectrumSetting setting)
    {
        var profile = LenovoLegionSpectrum.Profile(setting.Profile);
        var brightness = LenovoLegionSpectrum.Brightness(setting.Brightness);
        using var handle = OpenSpectrum(device);
        _ = SpectrumQuery(handle, 0xd1);
        if (!HidDevice.Native.HidD_SetFeature(handle, profile, profile.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!HidDevice.Native.HidD_SetFeature(handle, brightness, brightness.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private SafeFileHandle OpenSpectrum(DeviceInfo device)
    {
        EnsureLaptop();
        if (!LenovoLegionSpectrum.IsCandidate(device)) throw new NotSupportedException("Unsupported Spectrum descriptor.");
        var handle = HidDevice.Native.CreateFile(device.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var attributes = new HidDevice.Native.HidAttributes { Size = Marshal.SizeOf<HidDevice.Native.HidAttributes>() };
            if (!HidDevice.Native.HidD_GetAttributes(handle, ref attributes)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var caps = HidDevice.GetCaps(handle);
            if (attributes.VendorId != device.VendorId || attributes.ProductId != device.ProductId || caps.Usage != device.Usage ||
                caps.UsagePage != device.UsagePage || caps.FeatureReportByteLength != 960) throw new IOException("Spectrum interface changed; refresh before applying.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    private static byte SpectrumQuery(SafeFileHandle handle, byte operation)
    {
        var query = LenovoLegionSpectrum.Query(operation);
        if (!HidDevice.Native.HidD_SetFeature(handle, query, query.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var reply = new byte[960]; reply[0] = 7;
        if (!HidDevice.Native.HidD_GetFeature(handle, reply, reply.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return LenovoLegionSpectrum.ReadResponse(operation, reply);
    }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatus; public uint LifeTime, FullLifeTime; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle device,
        uint code, ref uint input, uint inputSize, out uint output, uint outputSize, out uint returned, IntPtr overlapped);
}

public sealed class LegionFileRecoveryStore(string directory) : ILegionRecoveryStore
{
    public void Save(LegionRecovery recovery)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, new { schemaVersion = 1, capturedAt = DateTimeOffset.UtcNow, recovery }, new JsonSerializerOptions { WriteIndented = true });
        stream.Flush(true);
    }
}
