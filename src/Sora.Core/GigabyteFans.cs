using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace Sora.Core;

public interface IFanRegisterAccess : IDisposable
{
    byte Read(FanRegister register);
    void Write(FanRegister register, byte value);
}

public sealed record FanRegisterValue(int Bank, int Address, byte Value)
{
    public FanRegister Register => new(Bank, Address);
}
public sealed record FanHeaderState(int Header, string Name, double? Rpm, bool Automatic, FanProfile Profile, FanRegisterValue[] Registers)
{
    public FanStopConfig? FirmwareStop { get; init; }
}
public sealed record FanSnapshot(string Board, string Controller, DateTimeOffset ReadAt, double?[] TemperaturesC, FanHeaderState[] Headers)
{
    public string? FirmwareInterface { get; init; }
}

/// <summary>Only the verified Z890 GAMING X WIFI7 / IT8696E map is enabled. No vendor DLL or driver is loaded.</summary>
public sealed class GigabyteFans : IFanRegisterAccess
{
    private readonly PawnIoModule module;
    private readonly List<Mutex> locks = [];
    private ushort addressPort, dataPort;
    private byte initialBank;
    private bool bankKnown, disposed;
    public const string SupportedBoard = "Z890 GAMING X WIFI7";
    public string Board { get; }

    public GigabyteFans()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Motherboard fan control requires Windows.");
        var bios = new SMBios();
        Board = bios.Board?.ProductName ?? "Unknown";
        if (!Board.Equals(SupportedBoard, StringComparison.OrdinalIgnoreCase) || !(bios.Board?.ManufacturerName?.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase) ?? false))
            throw new NotSupportedException($"Fan control has not been verified for {Board}.");
        using var binary = typeof(Computer).Assembly.GetManifestResourceStream("LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin") ?? throw new IOException("PawnIO LPC module is missing.");
        using var copy = new MemoryStream(); binary.CopyTo(copy);
        module = PawnIoModule.Load(copy.ToArray(), out bool denied) ?? throw new IOException(denied ? "Fan control needs administrator rights." : "Cannot load PawnIO LPC module. Check that PawnIO is installed.");
        try
        {
            foreach (string name in new[] { @"Global\Access_ISABUS.HTP.Method", "MBPnPModeMutex", "IT86XXECMutex4BankSelection", "IT86XXECMutex" })
            {
                var mutex = new Mutex(false, name);
                try { if (!mutex.WaitOne(2000)) { mutex.Dispose(); throw new TimeoutException("Another application is using the motherboard controller."); } }
                catch (AbandonedMutexException) { }
                locks.Add(mutex);
            }
            Execute("ioctl_select_slot", [0]);
            // GCC and LHM's ITE configuration unlock sequence. Only read identity and LDN4 base address.
            foreach (byte value in new byte[] { 0x87, 0x01, 0x55, 0x55 }) Out(0x2E, value);
            try
            {
                byte high = ConfigRead(0x20), low = ConfigRead(0x21);
                if ((high << 8 | low) != 0x8696) throw new NotSupportedException($"Expected IT8696E, found {high:X2}{low:X2}.");
                Out(0x2E, 7); Out(0x2F, 4);
                ushort address = (ushort)(ConfigRead(0x60) << 8 | ConfigRead(0x61));
                if (address == 0 || address == 0xFFFF || (address & 7) != 0) throw new IOException("Invalid fan controller I/O address.");
                addressPort = (ushort)(address + 5); dataPort = (ushort)(address + 6);
                Execute("ioctl_find_bars", []);
            }
            finally { Out(0x2E, 2); Out(0x2F, 2); }
            initialBank = RawRead(6); bankKnown = true;
            SelectBank(0);
            if (RawRead(0x58) != 0x90 || (RawRead(0) & 0x10) == 0) throw new IOException("The controller's ITE identity check failed.");
        }
        catch { Dispose(); throw; }
    }
    private long Execute(string function, long[] input, bool result = false)
    {
        long[] output = result ? new long[1] : [];
        int hr = module.Execute(function, input, (uint)input.Length, output, (uint)output.Length, out uint returned);
        if (hr != 0 || (result && returned != 1)) throw new IOException($"PawnIO {function} failed (0x{hr:X8}).");
        return result ? output[0] : 0;
    }
    private byte In(ushort port) => (byte)Execute("ioctl_pio_inb", [port], true);
    private void Out(ushort port, byte value) => Execute("ioctl_pio_outb", [port, value]);
    private byte ConfigRead(byte register) { Out(0x2E, register); return In(0x2F); }
    private byte RawRead(byte register) { Out(addressPort, register); return In(dataPort); }
    private void RawWrite(byte register, byte value) { Out(addressPort, register); Out(dataPort, value); }
    private void SelectBank(int bank)
    {
        byte current = RawRead(6);
        byte target = (byte)((current & 0x9F) | bank << 5);
        if (current != target) RawWrite(6, target);
    }
    public byte Read(FanRegister register)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (register.Bank is < 0 or > 3 || register.Address is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(register));
        SelectBank(register.Bank);
        return RawRead((byte)register.Address);
    }
    public void Write(FanRegister register, byte value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enumerable.Range(0, 6).Any(h => FanProtocol.CanWrite(h, register))) throw new ArgumentException("Register is outside the fan-control allow-list.");
        SelectBank(register.Bank);
        RawWrite((byte)register.Address, value);
    }
    public FanSnapshot ReadSnapshot()
    {
        int[] low = [0x0D, 0x0E, 0x0F, 0x80, 0x82, 0x4C], high = [0x18, 0x19, 0x1A, 0x81, 0x83, 0x4D];
        var temperatures = Enumerable.Range(0, 6).Select(i =>
        {
            int t = (sbyte)Read(new(0, 0x29 + i));
            return t is > 0 and < 127 ? (double?)t : null;
        }).ToArray();
        var headers = Enumerable.Range(0, 6).Select(h =>
        {
            // The 16-bit counter can roll over while read: retry when the high byte changes.
            int counter = 0; bool stable = false;
            for (int n = 0; n < 3; n++)
            {
                byte hi = Read(new(0, high[h])); int lo = Read(new(0, low[h]));
                if (hi == Read(new(0, high[h]))) { counter = hi << 8 | lo; stable = true; break; }
            }
            double? rpm = !stable || counter == 0 ? null : counter == 0xFFFF ? 0 : 1_350_000d / (counter * 2);
            var values = FanTransactions.Capture(this, h);
            var map = values.ToDictionary(v => v.Register, v => v.Value);
            return new FanHeaderState(h, FanProtocol.HeaderNames[h], rpm, (map[FanProtocol.ControlRegister(h)] & 0x80) != 0, FanProtocol.Decode(h, map), values);
        }).ToArray();
        return new(Board, "ITE IT8696E", DateTimeOffset.UtcNow, temperatures, headers);
    }
    public static string? FindConflict() => FindConflictProcess() is { } process ? $"Exit {process} before changing fan settings." : null;
    public static string? FindConflictProcess()
    {
        var processes = Process.GetProcesses();
        try
        {
            foreach (var p in processes)
            {
                try
                {
                    string n = p.ProcessName;
                    if (IsFanControllerProcess(n)) return n;
                }
                catch (InvalidOperationException) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return null;
    }
    internal static bool IsFanControllerProcess(string name) => new[]
    {
        "GCC", "GCCMbSensord", "EasyTuneEngineService", "EasyTune", "SIV", "FanControl", "FanControlService",
        "ArgusMonitor", "OpenHardwareMonitor", "LibreHardwareMonitor"
    }.Contains(name, StringComparer.OrdinalIgnoreCase);
    public void Dispose()
    {
        if (disposed) return;
        try { if (bankKnown) RawWrite(6, initialBank); }
        finally
        {
            disposed = true;
            module.Dispose();
            for (int i = locks.Count - 1; i >= 0; i--) { locks[i].ReleaseMutex(); locks[i].Dispose(); }
            locks.Clear();
        }
    }
}

public static class FanTransactions
{
    public static FanRegisterValue[] Capture(IFanRegisterAccess access, int header) => FanProtocol.Registers(header).Select(r => new FanRegisterValue(r.Bank, r.Address, access.Read(r))).ToArray();
    public static void Apply(IFanRegisterAccess access, FanProfile profile)
    {
        var writes = FanProtocol.Compile(profile);
        var before = Capture(access, profile.Header);
        var expected = before.ToDictionary(v => v.Register, v => v.Value);
        foreach (var w in writes) expected[w.Register] = (byte)((expected[w.Register] & ~w.Mask) | (w.Value & w.Mask));
        try
        {
            foreach (var w in writes)
            {
                byte current = access.Read(w.Register);
                byte target = (byte)((current & ~w.Mask) | (w.Value & w.Mask));
                if (current != target) access.Write(w.Register, target);
            }
            Verify(access, expected);
        }
        catch (Exception applyError)
        {
            try { Restore(access, profile.Header, before); }
            catch (Exception restoreError) { throw new AggregateException("Fan apply and restoration both failed. Original settings are saved; restore them before continuing.", applyError, restoreError); }
            throw new IOException("Fan apply failed; the original settings were restored and verified.", applyError);
        }
    }
    public static void Restore(IFanRegisterAccess access, int header, FanRegisterValue[] values)
    {
        var allowed = FanProtocol.Registers(header);
        if (values.Length != allowed.Length || values.Select(v => v.Register).Distinct().Count() != allowed.Length || values.Any(v => !allowed.Contains(v.Register)))
            throw new InvalidDataException("Fan backup does not match the selected header's register map.");
        // Restore the mode/source last, after all coefficients and vector enables.
        foreach (var v in values.OrderBy(v => v.Register == FanProtocol.ControlRegister(header) ? 1 : 0))
            if (access.Read(v.Register) != v.Value) access.Write(v.Register, v.Value);
        Verify(access, values.ToDictionary(v => v.Register, v => v.Value));
    }
    private static void Verify(IFanRegisterAccess access, IReadOnlyDictionary<FanRegister, byte> expected)
    {
        foreach (var (r, value) in expected)
        {
            byte actual = access.Read(r);
            if (actual != value) throw new IOException($"Fan readback mismatch at bank {r.Bank}, register {r.Address:X2}: expected {value:X2}, got {actual:X2}.");
        }
    }
}
