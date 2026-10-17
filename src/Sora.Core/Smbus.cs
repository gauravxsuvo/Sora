using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32.SafeHandles;
using RAMSPDToolkit.I2CSMBus;
using RAMSPDToolkit.I2CSMBus.Interop.PawnIO;
using RAMSPDToolkit.Windows.Driver;
using RAMSPDToolkit.Windows.Driver.Interfaces;

namespace Sora.Core;

/// <summary>
/// The Intel PCH SMBus, reached through PawnIO's signed SmbusI801 module (the copy inside LibreHardwareMonitorLib).
/// No Gigabyte or ENE kernel driver is loaded. Every transfer and every multi-transfer sequence holds
/// Global\Access_SMBUS.HTP.Method, the lock GCC, the ENE HAL, HWiNFO and LibreHardwareMonitor use, so no other
/// program can slip a transfer between a register-pointer write and the data that follows it.
/// </summary>
public sealed class Smbus : IDisposable
{
    public const string MutexName = @"Global\Access_SMBUS.HTP.Method";
    /// <summary>DDR5 RGB controllers live at SPD address + 0x20 (0x70-0x77). Sora never writes anywhere else.</summary>
    public const byte FirstLightingAddress = 0x70, LastLightingAddress = 0x77;

    private static readonly object gate = new();
    private readonly SMBusInterface bus;
    private readonly SoraPawnIoDriver driver;
    private readonly Mutex mutex;
    private bool disposed;

    public string Controller { get; }
    public int PciVendor { get; }
    public int PciDevice { get; }
    /// <summary>The PCH refuses writes to SPD EEPROM addresses (HOSTC.SPD_WD). A hardware backstop, reported for diagnostics.</summary>
    public bool SpdWriteProtected { get; }

    private Smbus(SMBusInterface bus, SoraPawnIoDriver driver, Mutex mutex)
    {
        this.bus = bus; this.driver = driver; this.mutex = mutex;
        Controller = bus.DeviceName ?? "SMBus";
        PciVendor = bus.PCIVendor; PciDevice = bus.PCIDevice;
        SpdWriteProtected = bus.HasSPDWriteProtection;
    }

    /// <summary>Opens the Intel SMBus. Needs administrator rights and PawnIO. Only one instance may be open per process.</summary>
    public static Smbus Open()
    {
        if (!OperatingSystem.IsWindows()) throw new SensorUnavailableException(SensorProblem.Unsupported, "SMBus access requires Windows.");
        if (!LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled) throw new SensorUnavailableException(SensorProblem.DriverMissing, "The PawnIO driver is not installed.");
        lock (gate)
        {
            if (SMBusManager.RegisteredSMBuses.Count > 0) throw new InvalidOperationException("The SMBus is already open in this process.");
            var driver = new SoraPawnIoDriver();
            DriverManager.Driver = driver;
            SMBusManager.UseWMI = false;
            if (!DriverManager.LoadDriver() || !SMBusPawnIO.SMBusDetect())
            {
                DriverManager.UnloadDriver();
                throw new SensorUnavailableException(driver.Denied ? SensorProblem.Permission : SensorProblem.Unsupported,
                    driver.Denied ? "SMBus access needs administrator rights." : "No supported Intel SMBus controller was found.");
            }
            var bus = SMBusManager.RegisteredSMBuses.OfType<SMBusPawnIO>().FirstOrDefault(b => b.PawnIOSMBusIdentifier == PawnIOSMBusIdentifier.I801);
            if (bus == null || bus.PCIVendor != 0x8086)
            {
                foreach (var other in SMBusManager.RegisteredSMBuses.ToArray()) SMBusManager.RemoveSMBus(other);
                DriverManager.UnloadDriver();
                throw new SensorUnavailableException(SensorProblem.Unsupported, "Only the Intel chipset SMBus is supported.");
            }
            // SMBusDetect has created the world-accessible mutex by now; open the same kernel object.
            var mutex = Mutex.TryOpenExisting(MutexName, out var existing) ? existing : new Mutex(false, MutexName);
            return new Smbus(bus, driver, mutex);
        }
    }

    /// <summary>Holds the system-wide SMBus lock for a whole sequence. Waits at most two seconds.</summary>
    public IDisposable Lock()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try { if (!mutex.WaitOne(2000)) throw new TimeoutException("Another program is holding the SMBus. Try again in a moment."); }
        catch (AbandonedMutexException) { /* The previous owner exited mid-sequence; the bus itself is idle again and we now own the lock. */ }
        return new Releaser(mutex);
    }

    /// <summary>SMBus "receive byte": addresses the device for reading only. Negative when nothing answers.</summary>
    public int ReceiveByte(byte address) { Check(); return bus.i2c_smbus_read_byte(address); }

    /// <summary>SMBus "read byte data": a command code then one byte back. Negative on error.</summary>
    public int ReadByte(byte address, byte command) { Check(); return bus.i2c_smbus_read_byte_data(address, command); }

    /// <summary>SMBus "read word data", low byte first. Negative on error.</summary>
    public int ReadWord(byte address, byte command) { Check(); return bus.i2c_smbus_read_word_data(address, command); }

    /// <summary>Write word data. Refuses every address outside the DDR5 lighting range.</summary>
    internal int WriteWord(byte address, byte command, ushort value) { CheckWrite(address); return bus.i2c_smbus_write_word_data(address, command, value); }

    /// <summary>Write byte data. Refuses every address outside the DDR5 lighting range.</summary>
    internal int WriteByte(byte address, byte command, byte value) { CheckWrite(address); return bus.i2c_smbus_write_byte_data(address, command, value); }

    /// <summary>SMBus block write (1-32 bytes), only to the lighting addresses like every other write.</summary>
    internal int WriteBlock(byte address, byte command, byte[] values)
    {
        CheckWrite(address);
        if (values.Length is 0 or > 32) throw new ArgumentOutOfRangeException(nameof(values), "SMBus blocks are 1-32 bytes.");
        return bus.i2c_smbus_write_block_data(address, command, (byte)values.Length, values);
    }

    /// <summary>
    /// The same bytes on the wire as <see cref="WriteBlock"/> (command, count, data), sent as an I2C block write for controllers
    /// whose driver does not do SMBus block writes. Lighting addresses only.
    /// </summary>
    internal int WriteBlockAsI2c(byte address, byte command, byte[] values)
    {
        CheckWrite(address);
        if (values.Length is 0 or > 31) throw new ArgumentOutOfRangeException(nameof(values), "Blocks sent this way are 1-31 bytes.");
        byte[] payload = [(byte)values.Length, .. values];
        return bus.i2c_smbus_write_i2c_block_data(address, command, (byte)payload.Length, payload);
    }

    private void Check() => ObjectDisposedException.ThrowIf(disposed, this);

    private void CheckWrite(byte address)
    {
        Check();
        if (address is < FirstLightingAddress or > LastLightingAddress) throw new InvalidOperationException($"Refused SMBus write to 0x{address:X2}: only DDR5 lighting controllers (0x70-0x77) may be written.");
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            SMBusManager.RemoveSMBus(bus);
            DriverManager.UnloadDriver();
            mutex.Dispose();
        }
    }

    private sealed class Releaser(Mutex mutex) : IDisposable
    {
        private bool released;
        public void Dispose() { if (released) return; released = true; mutex.ReleaseMutex(); }
    }
}

/// <summary>Loads PawnIO modules for RAMSPDToolkit. Only SmbusI801 is offered; its signed blob comes from LibreHardwareMonitorLib.</summary>
internal sealed class SoraPawnIoDriver : IPawnIODriver
{
    private readonly List<PawnIoModule> modules = [];
    public bool Denied { get; private set; }
    public bool IsOpen => true;
    public bool Load() => true;

    public IPawnIOModule? LoadModule(PawnIOSMBusIdentifier identifier)
    {
        if (identifier != PawnIOSMBusIdentifier.I801) return null;
        using var stream = typeof(Computer).Assembly.GetManifestResourceStream("LibreHardwareMonitor.Resources.PawnIo.SmbusI801.bin");
        if (stream == null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var module = PawnIoModule.Load(copy.ToArray(), out var denied);
        Denied |= denied;
        if (module != null) modules.Add(module);
        return module;
    }

    public void Unload() { foreach (var module in modules) module.Dispose(); modules.Clear(); }
}

/// <summary>One loaded PawnIO module: \Device\PawnIO with a signed binary loaded into it.</summary>
internal sealed class PawnIoModule : IPawnIOModule, IDisposable
{
    private const uint LoadBinary = 0xA1B2_2084, ExecuteFunction = 0xA1B2_2104;
    private readonly SafeFileHandle handle;
    private PawnIoModule(SafeFileHandle handle) => this.handle = handle;

    public static PawnIoModule? Load(byte[] binary, out bool denied)
    {
        denied = false;
        var handle = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 3, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle.IsInvalid) { denied = Marshal.GetLastWin32Error() == 5; handle.Dispose(); return null; }
        if (!DeviceIoControl(handle, LoadBinary, binary, binary.Length, null, 0, out _, IntPtr.Zero)) { handle.Dispose(); return null; }
        return new PawnIoModule(handle);
    }

    public int Execute(string name, long[] inBuffer, uint inSize, long[] outBuffer, uint outSize, out uint returnSize)
    {
        returnSize = 0;
        if (handle.IsClosed || inSize > inBuffer.Length || outSize > outBuffer.Length) return unchecked((int)0x80070057);
        var input = new byte[32 + inSize * 8];
        Encoding.ASCII.GetBytes(name, 0, Math.Min(31, name.Length), input, 0);
        Buffer.BlockCopy(inBuffer, 0, input, 32, (int)inSize * 8);
        var output = new byte[outSize * 8];
        if (!DeviceIoControl(handle, ExecuteFunction, input, input.Length, output, output.Length, out var written, IntPtr.Zero))
            return Marshal.GetHRForLastWin32Error();
        Buffer.BlockCopy(output, 0, outBuffer, 0, Math.Min(written, output.Length));
        returnSize = (uint)written / 8;
        return 0;
    }

    public void Dispose() => handle.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputLength, byte[]? output, int outputLength, out int returned, IntPtr overlapped);
}
