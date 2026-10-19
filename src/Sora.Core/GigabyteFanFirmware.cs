using System.Management;
using System.Runtime.Versioning;
using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace Sora.Core;

public sealed record FanStopConfig(bool Enabled, int TemperatureSource, int OffC, int OnC)
{
    public void Validate()
    {
        if (TemperatureSource is < 0 or > 5 || OffC is < 0 or > 60 || OnC is < 0 or > 95 ||
            (Enabled && (OffC < 1 || OnC <= OffC || OnC - OffC > 15)))
            throw new ArgumentException("Firmware Zero-RPM needs a live source, stop temperature 1–60°C and restart 1–15°C above stop, at most 95°C.");
    }
}

public interface IFanFirmwareAccess : IDisposable
{
    FanStopConfig Read(int header);
    void Write(int header, FanStopConfig config);
}

/// <summary>GCC's GAS1 firmware Zero-RPM interface. Only the six recovered fan methods can be invoked.</summary>
[SupportedOSPlatform("windows")]
public sealed class GigabyteFanFirmware : IFanFirmwareAccess
{
    public const string WmiClassName = FanMaintenance.FirmwareInterface;
    private const string FirmwareGuid = "{DEADBEEF-2001-0000-00A0-C90629100000}";
    private readonly ManagementObject instance;
    public uint Capability { get; }
    public string InterfaceClass { get; }
    public GigabyteFanFirmware()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Firmware fan control requires Windows.");
        var board = new SMBios().Board;
        if (board?.ProductName != GigabyteFans.SupportedBoard || !(board.ManufacturerName?.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase) ?? false))
            throw new NotSupportedException("This motherboard's firmware fan interface has not been verified.");
        // Registration is an explicit setup action. Reads and applies never install or change a WMI schema.
        InterfaceClass = StandaloneInterfaceInstalled ? WmiClassName : "GSA1_ACPIMethod";
        using var search = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM " + InterfaceClass);
        using var objects = search.Get();
        using var iterator = objects.GetEnumerator();
        if (!iterator.MoveNext()) throw new NotSupportedException("The BIOS firmware fan interface is unavailable.");
        using var found = (ManagementObject)iterator.Current;
        if (!Convert.ToBoolean(found["Active"])) throw new NotSupportedException("The BIOS firmware fan interface is inactive.");
        instance = new ManagementObject(@"root\WMI", found.Path.RelativePath, null);
        try
        {
            using var result = Invoke("GsaGetCapabilityD0");
            Capability = Convert.ToUInt32(result["Value"]);
            if ((Capability & 0x4000) == 0) throw new NotSupportedException("This BIOS does not advertise firmware Zero-RPM.");
        }
        catch { instance.Dispose(); throw; }
    }
    public static bool StandaloneInterfaceInstalled
    {
        get
        {
            try
            {
                using var schema = new ManagementClass(@"root\WMI", WmiClassName, null); schema.Get();
                if (!string.Equals(Convert.ToString(schema.Qualifiers["guid"].Value), FirmwareGuid, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Convert.ToString(schema.Qualifiers["provider"].Value), "WmiProv", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Sora's firmware class has an unexpected GUID or provider.");
                return true;
            }
            catch (ManagementException e) when (e.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.NotFound) { return false; }
        }
    }
    public static void InstallStandaloneInterface()
    {
        var board = new SMBios().Board;
        if (board?.ProductName != GigabyteFans.SupportedBoard || !(board.ManufacturerName?.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase) ?? false))
            throw new NotSupportedException("This board's firmware interface has not been verified.");
        if (StandaloneInterfaceInstalled) return;
        using var mutex = new Mutex(false, @"Local\Sora.GigabyteFanSchema");
        try { if (!mutex.WaitOne(30000)) throw new TimeoutException("Another Sora instance is registering the firmware fan interface."); }
        catch (AbandonedMutexException) { }
        try
        {
            if (StandaloneInterfaceInstalled) return;
            using var resource = typeof(GigabyteFanFirmware).Assembly.GetManifestResourceStream("Sora.Core.GigabyteFan.mof") ?? throw new IOException("Sora fan interface definition is missing.");
            string path = Path.Combine(Path.GetTempPath(), "Sora-fan-" + Guid.NewGuid().ToString("N") + ".mof");
            try
            {
                using (var file = File.Create(path)) resource.CopyTo(file);
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wbem", "mofcomp.exe"))
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(path);
                using var process = Process.Start(start) ?? throw new IOException("Cannot start Windows' WMI compiler.");
                if (!process.WaitForExit(20000)) { process.Kill(); throw new TimeoutException("Firmware fan interface registration timed out."); }
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (process.ExitCode != 0 || !StandaloneInterfaceInstalled) throw new IOException("Cannot register Sora's firmware fan interface: " + output.Trim());
            }
            finally { File.Delete(path); }
        }
        finally { mutex.ReleaseMutex(); }
    }
    private ManagementBaseObject Invoke(string method, params (string Name, object Value)[] arguments) =>
        Call(method, arguments) ?? throw new IOException("No result from firmware " + method + ".");
    private void Set(string method, params (string Name, object Value)[] arguments) { using var result = Call(method, arguments); }
    private ManagementBaseObject? Call(string method, params (string Name, object Value)[] arguments)
    {
        using var input = instance.GetMethodParameters(method);
        foreach (var (name, value) in arguments) input[name] = value;
        var result = instance.InvokeMethod(method, input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(3) });
        // GCC setters are WMI void methods: null is their normal successful response.
        if (result == null) return null;
        var returnValue = result.Properties.Cast<PropertyData>().FirstOrDefault(p => p.Name.Equals("ReturnValue", StringComparison.OrdinalIgnoreCase));
        if (method != "GsaGetCapabilityD0" && returnValue?.Value != null && !Convert.ToBoolean(returnValue.Value))
        { result.Dispose(); throw new IOException("Firmware rejected " + method + "."); }
        return result;
    }
    private static void CheckHeader(int header) { if (header is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(header)); }
    public FanStopConfig Read(int header)
    {
        CheckHeader(header);
        using var status = Invoke("ZFCGetFanStopStatus", ("id", (uint)header));
        using var limits = Invoke("ZFCGetFanTempLimit", ("id", (uint)header));
        using var source = Invoke("ZFCGetFanTargetTemp", ("id", (uint)header));
        var config = new FanStopConfig(Convert.ToUInt32(status["ison"]) == 1, Convert.ToInt32(source["Tempid"]), Convert.ToInt32(limits["off"]), Convert.ToInt32(limits["on"]));
        config.Validate(); return config;
    }
    public void Write(int header, FanStopConfig config)
    {
        CheckHeader(header); config.Validate();
        var before = Read(header);
        if (before == config) return;
        // Disable first so a source/limit update cannot temporarily stop the wrong fan.
        if (before.Enabled) Set("ZFCSetFanStopStatus", ("id", (uint)header), ("ison", 0u));
        if (before.TemperatureSource != config.TemperatureSource)
            Set("ZFCSetFanTargetTemp", ("id", (uint)header), ("Tempid", (uint)config.TemperatureSource));
        if (before.OffC != config.OffC || before.OnC != config.OnC)
            Set("ZFCSetFanTempLimit", ("id", (uint)header), ("off", (uint)config.OffC), ("on", (uint)config.OnC));
        if (config.Enabled) Set("ZFCSetFanStopStatus", ("id", (uint)header), ("ison", 1u));
    }
    public void Dispose() => instance.Dispose();
    public static FanSnapshot Enrich(FanSnapshot snapshot)
    {
        using var firmware = new GigabyteFanFirmware();
        return snapshot with { FirmwareInterface = firmware.InterfaceClass, Headers = snapshot.Headers.Select(h => h with { FirmwareStop = firmware.Read(h.Header) }).ToArray() };
    }
}

public static class FanFirmwareTransactions
{
    public static void Apply(IFanFirmwareAccess access, int header, FanStopConfig target)
    {
        target.Validate();
        var before = access.Read(header);
        try { access.Write(header, target); Verify(access, header, target); }
        catch (Exception error)
        {
            try { Restore(access, header, before); }
            catch (Exception restore) { throw new AggregateException("Firmware Zero-RPM apply and rollback both failed.", error, restore); }
            throw new IOException("Firmware Zero-RPM apply failed; previous settings were restored and verified.", error);
        }
    }
    public static void Restore(IFanFirmwareAccess access, int header, FanStopConfig original)
    { original.Validate(); access.Write(header, original); Verify(access, header, original); }
    private static void Verify(IFanFirmwareAccess access, int header, FanStopConfig expected)
    { if (access.Read(header) != expected) throw new IOException("Firmware Zero-RPM readback does not match the requested settings."); }
}

/// <summary>Session takeover only. Never changes the service's startup type or any RGB service.</summary>
[SupportedOSPlatform("windows")]
public static class GigabyteFanServiceControl
{
    private const string ServicePath = "Win32_Service.Name='EasyTuneEngineService'";
    public static bool IsInstalled
    {
        get
        {
            try { using var service = new ManagementObject(ServicePath); service.Get(); return true; }
            catch (ManagementException e) when (e.ErrorCode == ManagementStatus.NotFound) { return false; }
        }
    }
    public static bool IsRunning
    {
        get { if (!IsInstalled) return false; using var service = new ManagementObject(ServicePath); service.Get(); return Convert.ToString(service["State"]) == "Running"; }
    }
    public static void Pause() => Change("StopService", "Stopped");
    public static void Resume() => Change("StartService", "Running");
    private static void Change(string method, string expected)
    {
        // GCC can be completely uninstalled. There is no service to stop, restart or recreate in that case.
        if (!IsInstalled) return;
        using var service = new ManagementObject(ServicePath);
        service.Get();
        if (Convert.ToString(service["State"]) == expected) return;
        uint result = Convert.ToUInt32(service.InvokeMethod(method, null));
        if (result != 0) throw new IOException($"GCC fan service {method} failed ({result}).");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do { service.Get(); if (Convert.ToString(service["State"]) == expected) return; Thread.Sleep(100); } while (DateTime.UtcNow < deadline);
        throw new TimeoutException("GCC fan service did not reach " + expected + ".");
    }
}
