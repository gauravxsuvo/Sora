using System.Runtime.InteropServices;
using System.Text;

namespace Sora.Core;

public enum GpuFanMode { Automatic, Fixed, Curve, HardwareCurve, HardwareFixed }
public sealed record GpuFanPoint(double TemperatureC, double DutyPercent);
public sealed record GpuRpmPoint(double TemperatureC, uint Rpm);
public sealed record GpuHardwareFan(uint Index, GpuRpmPoint[] Points, bool ZeroRpmEnabled);
public sealed record GpuHardwareFanState(GpuHardwareFan[] Fans, uint MaximumRpm, bool CanProgram, string Notice);
public sealed record GpuFanProfile
{
    public string Name { get; init; } = "Balanced";
    public GpuFanMode Mode { get; init; } = GpuFanMode.Automatic;
    public double FixedDutyPercent { get; init; } = 50;
    public GpuFanPoint[] Points { get; init; } = [new(30, 30), new(50, 40), new(65, 60), new(75, 80), new(85, 100)];
    public GpuRpmPoint[] RpmPoints { get; init; } = [new(55, 1000), new(76, 2400), new(84, 3000)];
    public uint FixedRpm { get; init; } = 1500;
    public bool AutoFanStop { get; init; } = true;
    public uint[] FanIndices { get; init; } = [0, 1, 2];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 64 || Name.Any(char.IsControl) || !Enum.IsDefined(Mode)) throw new ArgumentException("Invalid GPU profile name or mode.");
        if (!double.IsFinite(FixedDutyPercent) || FixedDutyPercent is < 0 or > 100) throw new ArgumentException("GPU duty must be 0–100%.");
        if (Points == null || Points.Length is < 2 or > 16) throw new ArgumentException("Use 2–16 GPU curve points.");
        double previousTemperature = -1, previousDuty = -1;
        foreach (var point in Points)
        {
            if (point == null || !double.IsFinite(point.TemperatureC) || !double.IsFinite(point.DutyPercent) || point.TemperatureC is < 0 or > 100 || point.DutyPercent is < 0 or > 100 || point.TemperatureC <= previousTemperature || point.DutyPercent < previousDuty)
                throw new ArgumentException("Temperatures must increase; duty must not decrease. Use temperatures 0–100°C and duty 0–100%.");
            previousTemperature = point.TemperatureC; previousDuty = point.DutyPercent;
        }
        if (Points[^1].TemperatureC > 85 || Points[^1].DutyPercent != 100) throw new ArgumentException("The final curve point must reach 100% at or below 85°C.");
        if (RpmPoints == null || RpmPoints.Length != 3 || FixedRpm is < 998 or > 3000) throw new ArgumentException("Hardware RPM control uses exactly three points and 998–3000 RPM on the verified WINDFORCE card.");
        double previousC = -1; uint previousRpm = 0;
        foreach (var point in RpmPoints)
        {
            if (point == null || !double.IsFinite(point.TemperatureC) || point.TemperatureC is < 0 or > 85 || point.TemperatureC <= previousC || point.Rpm is < 998 or > 3000 || point.Rpm < previousRpm) throw new ArgumentException("Hardware curve temperatures must increase up to 85°C; RPM must not decrease and must be 998–3000.");
            previousC = point.TemperatureC; previousRpm = point.Rpm;
        }
        if (RpmPoints[^1].Rpm != 3000) throw new ArgumentException("The final hardware curve point must reach 3000 RPM at or below 85°C.");
        if (FanIndices == null || FanIndices.Length is not (1 or 3) || FanIndices.Any(i => i > 2) || FanIndices.Distinct().Count() != FanIndices.Length) throw new ArgumentException("Select one native fan channel (0–2) or all three channels.");
    }
    public double DutyAt(double temperatureC)
    {
        Validate();
        if (!double.IsFinite(temperatureC) || temperatureC >= 85) return 100;
        if (Mode == GpuFanMode.Fixed) return FixedDutyPercent;
        if (temperatureC <= Points[0].TemperatureC) return Points[0].DutyPercent;
        for (int i = 1; i < Points.Length; i++)
            if (temperatureC <= Points[i].TemperatureC)
            {
                var a = Points[i - 1]; var b = Points[i];
                return a.DutyPercent + (b.DutyPercent - a.DutyPercent) * (temperatureC - a.TemperatureC) / (b.TemperatureC - a.TemperatureC);
            }
        return 100;
    }
}

public sealed record GpuFanChannel(uint Id, uint Rpm, uint DutyPercent, uint MinimumDutyPercent, uint MaximumDutyPercent, uint? ControlMode);
public sealed record GpuFanSnapshot(string Identity, string Name, uint BusId, uint DeviceId, uint SubsystemId, double? TemperatureC, GpuFanChannel[] Fans, bool CanControl, string Notice, DateTimeOffset ReadAt)
{
    public GpuHardwareFanState? Hardware { get; init; }
    public GpuHardwareFanState? FactoryHardware { get; init; }
}
public sealed record GpuFanBackup(string Identity, byte[] Control)
{
    public byte[]? FirmwareCurve { get; init; }
    public byte[]? FirmwareStop { get; init; }
}

public interface IGpuFanAccess : IDisposable
{
    GpuFanSnapshot[] Read();
    GpuFanBackup Capture(string identity);
    void Write(GpuFanBackup control);
}

/// <summary>The NVIDIA driver's client-fan interface. Loads only the system driver DLL, never GCC libraries.</summary>
public sealed class NvidiaGpuFans : IGpuFanAccess
{
    public const int ControlSize = 1452, StatusSize = 1704;
    private const int HeaderSize = 44, ItemSize = 44;
    private readonly IntPtr library;
    private readonly Query query;
    private readonly Enumerate enumerate;
    private readonly GetText getName;
    private readonly GetUInt getBus;
    private readonly GetPci getPci;
    private readonly BufferCall? getStatus, getControl, setControl;
    private readonly Dictionary<string, IntPtr> handles = new();
    private bool disposed;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate([Out] IntPtr[] handles, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int GetText(IntPtr gpu, StringBuilder value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUInt(IntPtr gpu, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetPci(IntPtr gpu, out uint device, out uint subsystem, out uint revision, out uint extended);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BufferCall(IntPtr gpu, IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ThermalCall(IntPtr gpu, uint sensor, IntPtr buffer);
    private readonly ThermalCall? thermal;

    public NvidiaGpuFans()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("GPU fan control requires Windows and an NVIDIA driver.");
        library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll"));
        try
        {
            query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
            Check(Required<Initialize>(0x0150E828)(), "initialize");
            enumerate = Required<Enumerate>(0xE5AC921F); getName = Required<GetText>(0xCEEE8E9F);
            getBus = Required<GetUInt>(0x1BE0B8E5); getPci = Required<GetPci>(0x2DDFB66E);
            getStatus = Optional<BufferCall>(0x35AED5E8); getControl = Optional<BufferCall>(0x814B209F); setControl = Optional<BufferCall>(0xA58971A5);
            thermal = Optional<ThermalCall>(0xE3640A56);
        }
        catch { NativeLibrary.Free(library); throw; }
    }
    private T Required<T>(uint id) where T : Delegate => Optional<T>(id) ?? throw new NotSupportedException($"NVIDIA interface {id:X8} is unavailable.");
    private T? Optional<T>(uint id) where T : Delegate { IntPtr entry = query(id); return entry == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(entry); }
    private static void Check(int status, string operation) { if (status != 0) throw new IOException($"NVIDIA {operation} failed (NVAPI {status})."); }
    private static uint U32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
    private static void Put(byte[] data, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);
    private static byte[] Empty(int size) { byte[] data = new byte[size]; Put(data, 0, (uint)(0x10000 | size)); return data; }
    private static byte[] Call(BufferCall? api, IntPtr gpu, int size, string operation, byte[]? input = null)
    {
        if (api == null) throw new NotSupportedException("The NVIDIA driver does not expose " + operation + ".");
        var data = input == null ? Empty(size) : (byte[])input.Clone();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try { Marshal.Copy(data, 0, buffer, size); Check(api(gpu, buffer), operation); Marshal.Copy(buffer, data, 0, size); return data; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private double? Temperature(IntPtr gpu)
    {
        if (thermal == null) return null;
        var data = Empty(68); IntPtr buffer = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, buffer, data.Length);
            if (thermal(gpu, 15, buffer) != 0) return null;
            Marshal.Copy(buffer, data, 0, data.Length);
            uint count = U32(data, 4); if (count > 3) return null;
            for (int i = 0; i < count; i++) if (U32(data, 8 + i * 20 + 16) == 1) { int value = BitConverter.ToInt32(data, 8 + i * 20 + 12); return value is >= 0 and <= 125 ? value : null; }
            return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public GpuFanSnapshot[] Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var native = new IntPtr[64]; Check(enumerate(native, out uint count), "enumerate GPUs");
        if (count > native.Length) throw new IOException("Invalid NVIDIA GPU count.");
        handles.Clear(); var result = new List<GpuFanSnapshot>();
        for (int i = 0; i < count; i++)
        {
            IntPtr gpu = native[i]; var name = new StringBuilder(64); Check(getName(gpu, name), "GPU name"); Check(getBus(gpu, out uint bus), "bus identity");
            Check(getPci(gpu, out uint device, out uint subsystem, out _, out _), "PCI identity");
            string identity = $"nvidia:{bus:X8}:{device:X8}:{subsystem:X8}";
            if (!handles.TryAdd(identity, gpu)) throw new IOException("Ambiguous NVIDIA adapter identity; fan writes disabled.");
            byte[]? control = null; string notice = "Independent NVIDIA driver control. Curves run in Sora; vendor firmware curves and zero-RPM programming are unavailable.";
            try { control = Call(getControl, gpu, ControlSize, "read fan control"); ValidateControl(control); }
            catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException) { notice = e.Message; }
            var fans = new List<GpuFanChannel>();
            try
            {
                var status = Call(getStatus, gpu, StatusSize, "read fan status"); uint n = U32(status, 4);
                if (n > 32) throw new IOException("Invalid NVIDIA fan count.");
                for (int j = 0; j < n; j++)
                {
                    int offset = 40 + j * 52; uint id = U32(status, offset), level = U32(status, offset + 16), min = U32(status, offset + 8), max = U32(status, offset + 12);
                    if (min > max || max > 100 || level > 100 || fans.Any(f => f.Id == id)) throw new IOException("Invalid NVIDIA fan telemetry.");
                    uint? mode = null;
                    if (control != null) for (int k = 0; k < U32(control, 8); k++) if (U32(control, HeaderSize + k * ItemSize) == id) mode = U32(control, HeaderSize + k * ItemSize + 8);
                    fans.Add(new(id, U32(status, offset + 4), level, min, max, mode));
                }
            }
            catch (Exception e) when (e is IOException or NotSupportedException) { notice = e.Message; fans.Clear(); }
            bool supported = control != null && setControl != null && fans.Count > 0 && fans.All(f => f.ControlMode != null) && U32(control, 8) == fans.Count;
            GpuHardwareFanState? hardware = null, factoryHardware = null;
            if (device == 0x2C0510DE && subsystem == 0x41A81458)
            {
                try
                {
                    var nativeState = CaptureFirmware(gpu); hardware = GpuNativeFanProtocol.Decode(nativeState.Curve, nativeState.Stop);
                    byte[] factory = new byte[220]; Put(factory, 0, 0x200DC); Put(factory, 216, 1);
                    factoryHardware = GpuNativeFanProtocol.Decode(Call(Optional<BufferCall>(0xE543C540), gpu, 220, "read factory fan curve", factory), nativeState.Stop);
                    if (factoryHardware.Fans.Any(f => f.Points[^1].Rpm != 3000)) throw new NotSupportedException("This card's factory maximum differs from the verified 3000 RPM map.");
                    notice = "Native three-point RPM curves and firmware auto fan stop recovered for this WINDFORCE RTX 5070 Ti. Driver writes require original-state backup and readback.";
                }
                catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException) { hardware = null; factoryHardware = null; notice += " Native curve route unavailable: " + e.Message; }
            }
            result.Add(new(identity, name.ToString(), bus, device, subsystem, Temperature(gpu), fans.ToArray(), supported, notice, DateTimeOffset.Now) { Hardware = hardware, FactoryHardware = factoryHardware });
        }
        return result.ToArray();
    }
    public GpuFanBackup Capture(string identity)
    {
        var snapshot = Read().SingleOrDefault(s => s.Identity == identity); if (snapshot == null || !handles.TryGetValue(identity, out IntPtr gpu)) throw new IOException("The selected NVIDIA GPU is no longer connected.");
        var control = Call(getControl, gpu, ControlSize, "capture original fan control"); ValidateControl(control);
        if (snapshot.Hardware?.CanProgram == true) { var firmware = CaptureFirmware(gpu); return new(identity, control) { FirmwareCurve = firmware.Curve, FirmwareStop = firmware.Stop }; }
        return new(identity, control);
    }
    private (byte[] Curve, byte[] Stop) CaptureFirmware(IntPtr gpu)
    {
        byte[] Read(uint id, int size, int version, string name) { byte[] data = new byte[size]; Put(data, 0, (uint)(version << 16 | size)); return Call(Optional<BufferCall>(id), gpu, size, name, data); }
        var info = Read(0x52B76D12, 76, 2, "hardware fan curve capabilities");
        if ((U32(info, 4) & 1) == 0 || U32(info, 8) != 3) throw new NotSupportedException("Expected three hardware RPM controllers.");
        var curve = Read(0xE543C540, 220, 2, "read hardware fan curves");
        var stopInfo = Read(0xDDDFDA38, 1316, 1, "hardware fan stop capabilities");
        var stop = Read(0x600F612E, 292, 1, "read hardware fan stop");
        GpuNativeFanProtocol.Validate(curve, stop);
        if (stopInfo[4] != 3 || Enumerable.Range(0, 3).Any(i => U32(stopInfo, 36 + i * 40) != i + 1)) throw new NotSupportedException("Unknown hardware fan stop topology.");
        if (Optional<BufferCall>(0xC181947A) == null || Optional<BufferCall>(0x44CD3014) == null) throw new NotSupportedException("Hardware fan setters are unavailable.");
        return (curve, stop);
    }
    /// <summary>Read-only private NVAPI probes recovered from the installed GCC GvDisplayA binary. No setters are queried or invoked.</summary>
    public Dictionary<string, string> ReadRecoveredFirmwareEvidence(string identity)
    {
        Read(); if (!handles.TryGetValue(identity, out IntPtr gpu)) throw new IOException("GPU disconnected.");
        var result = new Dictionary<string, string>();
        void Probe(string name, uint id, int size, int version, byte? fan = null)
        {
            byte[] data = new byte[size]; Put(data, 0, (uint)(version << 16 | size)); if (fan != null) data[4] = fan.Value;
            try { result[name] = Convert.ToBase64String(Call(Optional<BufferCall>(id), gpu, size, name, data)); }
            catch (Exception error) when (error is IOException or NotSupportedException) { result[name] = error.Message; }
        }
        Probe("curve-info-52B76D12-v2", 0x52B76D12, 76, 2);
        for (byte count = 0; count < 3; count++) Probe($"curve-E543C540-v2-count-request-{count}", 0xE543C540, 220, 2, count);
        Probe("zero-fan-info-DDDFDA38-v1", 0xDDDFDA38, 1316, 1);
        Probe("zero-fan-get-600F612E-v1", 0x600F612E, 292, 1);
        return result;
    }
    public static void ValidateControl(byte[] data)
    {
        if (data == null || data.Length != ControlSize || U32(data, 0) != (0x10000 | ControlSize) || U32(data, 8) is 0 or > 32) throw new ArgumentException("Invalid NVIDIA fan-control backup.");
        var ids = new HashSet<uint>();
        for (int i = 0; i < U32(data, 8); i++)
        {
            int offset = HeaderSize + i * ItemSize;
            if (!ids.Add(U32(data, offset)) || U32(data, offset + 4) > 100 || U32(data, offset + 8) > 1) throw new ArgumentException("Unsupported NVIDIA fan-control entry.");
        }
    }
    public static void ValidateBackup(GpuFanBackup backup)
    {
        if (backup == null || string.IsNullOrWhiteSpace(backup.Identity)) throw new ArgumentException("Missing GPU backup identity.");
        ValidateControl(backup.Control);
        if (backup.FirmwareCurve != null || backup.FirmwareStop != null)
        {
            if (backup.FirmwareCurve == null || backup.FirmwareStop == null) throw new ArgumentException("Incomplete native GPU fan backup.");
            GpuNativeFanProtocol.Validate(backup.FirmwareCurve, backup.FirmwareStop);
        }
    }
    public static GpuFanBackup WithDuty(GpuFanBackup original, GpuFanSnapshot snapshot, double? duty, IReadOnlySet<uint>? selectedCoolerIds = null)
    {
        ValidateControl(original.Control);
        if (original.Identity != snapshot.Identity || !snapshot.CanControl || snapshot.Fans.Length != U32(original.Control, 8) || duty is double value && (!double.IsFinite(value) || value is < 0 or > 100)) throw new ArgumentException("Invalid GPU control request.");
        var data = (byte[])original.Control.Clone();
        for (int i = 0; i < U32(data, 8); i++)
        {
            int offset = HeaderSize + i * ItemSize; uint id = U32(data, offset);
            var fan = snapshot.Fans.SingleOrDefault(f => f.Id == id) ?? throw new ArgumentException("The GPU fan map changed.");
            if (selectedCoolerIds != null && !selectedCoolerIds.Contains(id)) continue;
            uint level = duty == null ? 0 : (uint)Math.Round(duty.Value);
            if (duty != null && (level < fan.MinimumDutyPercent || level > fan.MaximumDutyPercent)) throw new ArgumentException($"Fan {id} allows {fan.MinimumDutyPercent}–{fan.MaximumDutyPercent}% duty; firmware zero-RPM is not independently supported.");
            Put(data, offset + 4, level); Put(data, offset + 8, duty == null ? 0u : 1u);
        }
        return original with { Control = data };
    }
    public static bool Equivalent(GpuFanBackup expected, GpuFanBackup actual)
    {
        ValidateControl(expected.Control); ValidateControl(actual.Control);
        if (expected.Identity != actual.Identity || U32(expected.Control, 8) != U32(actual.Control, 8)) return false;
        for (int i = 0; i < U32(expected.Control, 8); i++)
        {
            int offset = HeaderSize + i * ItemSize; uint mode = U32(expected.Control, offset + 8);
            if (U32(expected.Control, offset) != U32(actual.Control, offset) || mode != U32(actual.Control, offset + 8) || mode == 1 && U32(expected.Control, offset + 4) != U32(actual.Control, offset + 4)) return false;
        }
        if (expected.FirmwareCurve != null && (actual.FirmwareCurve == null || !expected.FirmwareCurve.SequenceEqual(actual.FirmwareCurve))) return false;
        if (expected.FirmwareStop != null && (actual.FirmwareStop == null || !expected.FirmwareStop.SequenceEqual(actual.FirmwareStop))) return false;
        return true;
    }
    public void Write(GpuFanBackup control)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ValidateBackup(control);
        var snapshot = Read().SingleOrDefault(s => s.Identity == control.Identity) ?? throw new IOException("The selected NVIDIA GPU is disconnected.");
        if (!snapshot.CanControl) throw new NotSupportedException("This driver's fan control is unavailable.");
        var current = Capture(control.Identity);
        if (!SameFanIds(current.Control, control.Control)) throw new IOException("GPU fan mapping changed; restore refused.");
        if (control.FirmwareCurve != null || control.FirmwareStop != null)
        {
            if (snapshot.DeviceId != 0x2C0510DE || snapshot.SubsystemId != 0x41A81458 || control.FirmwareCurve == null || control.FirmwareStop == null) throw new NotSupportedException("Native GPU fan state is incomplete or the card identity is unverified; write refused.");
            GpuNativeFanProtocol.Validate(control.FirmwareCurve, control.FirmwareStop);
            if (current.FirmwareCurve == null || !control.FirmwareCurve.SequenceEqual(current.FirmwareCurve)) Call(Optional<BufferCall>(0xC181947A), handles[control.Identity], 220, "set hardware fan curve", control.FirmwareCurve);
            if (current.FirmwareStop == null || !control.FirmwareStop.SequenceEqual(current.FirmwareStop)) Call(Optional<BufferCall>(0x44CD3014), handles[control.Identity], 292, "set hardware auto fan stop", control.FirmwareStop);
        }
        // Native curve setters can operate while duty control is already automatic. Do not require a privileged duty setter for a no-op.
        var latestControl = Call(getControl, handles[control.Identity], ControlSize, "read duty control after native programming"); ValidateControl(latestControl);
        if (!Equivalent(new GpuFanBackup(control.Identity, control.Control), new GpuFanBackup(control.Identity, latestControl))) Call(setControl, handles[control.Identity], ControlSize, "set fan control", control.Control);
    }
    private static bool SameFanIds(byte[] a, byte[] b) => U32(a, 8) == U32(b, 8) && Enumerable.Range(0, (int)U32(a, 8)).All(i => U32(a, HeaderSize + i * ItemSize) == U32(b, HeaderSize + i * ItemSize));
    public void Dispose() { if (disposed) return; disposed = true; NativeLibrary.Free(library); }
}

public static class GpuFanTransactions
{
    public static void Apply(IGpuFanAccess access, GpuFanBackup target)
    {
        NvidiaGpuFans.ValidateBackup(target); var before = access.Capture(target.Identity);
        if (NvidiaGpuFans.Equivalent(target, before)) return;
        try { access.Write(target); if (!NvidiaGpuFans.Equivalent(target, access.Capture(target.Identity))) throw new IOException("NVIDIA fan-control readback did not match."); }
        catch (Exception applyError)
        {
            try
            {
                bool unchanged = false;
                // A permission-denied setter can leave everything unchanged. Confirm that state before issuing another write.
                try { unchanged = NvidiaGpuFans.Equivalent(before, access.Capture(before.Identity)); } catch (Exception) { }
                if (!unchanged) { access.Write(before); if (!NvidiaGpuFans.Equivalent(before, access.Capture(before.Identity))) throw new IOException("NVIDIA rollback readback did not match."); }
            }
            catch (Exception restoreError) { throw new AggregateException("GPU write and restoration failed. Original backup retained.", applyError, restoreError); }
            throw;
        }
    }
}
