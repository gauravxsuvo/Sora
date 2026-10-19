using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Sora.Core;

public sealed record StorageAttribute(string Id, string Name, string Value, string Unit = "", string Normalized = "", string Worst = "", string Threshold = "", string RawHex = "");
public sealed record StorageHealth(string Protocol, string Status, double? TemperatureC, int? LifeRemaining,
    string? PowerOnHours, IReadOnlyList<StorageAttribute> Attributes, string Detail);
public sealed record StorageNvmeIdentity(string Model, string Firmware, string Serial);
public sealed record StorageDrive(int Number, string Model, string Firmware, string Serial, ulong CapacityBytes,
    string Bus, IReadOnlyList<string> Volumes, bool SystemDisk, bool? Trim, StorageHealth Health)
{
    public string Label => $"Disk {Number} · {Model}";
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string SecureEraseStatus => "Secure erase is unavailable for this drive in Sora.";
}

/// <summary>Read-only Windows storage inventory. No vendor DLLs, drivers, erase, format, or SMART-enable commands.</summary>
public static class StorageInventory
{
    public static IReadOnlyList<StorageDrive> Scan()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Storage inventory requires Windows.");
        var result = new List<StorageDrive>();
        // WMI association traversal needs the complete object's key/path metadata.
        using var search = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
        using var disks = search.Get();
        foreach (ManagementObject disk in disks)
        {
            using (disk)
            {
                var number = Convert.ToInt32(disk["Index"], CultureInfo.InvariantCulture);
                var model = Convert.ToString(disk["Model"], CultureInfo.InvariantCulture)?.Trim() ?? "Unknown drive";
                var firmware = Convert.ToString(disk["FirmwareRevision"], CultureInfo.InvariantCulture)?.Trim() ?? "";
                var serial = Convert.ToString(disk["SerialNumber"], CultureInfo.InvariantCulture)?.Trim() ?? "";
                var capacity = Convert.ToUInt64(disk["Size"] ?? 0, CultureInfo.InvariantCulture);
                var bus = Convert.ToString(disk["InterfaceType"], CultureInfo.InvariantCulture) ?? "Unknown";
                var volumes = new List<string>();
                string? volumeProblem = null;
                try
                {
                    using var partitions = disk.GetRelated("Win32_DiskPartition");
                    foreach (ManagementObject partition in partitions)
                    using (partition)
                    {
                        using var logical = partition.GetRelated("Win32_LogicalDisk");
                        foreach (ManagementObject volume in logical)
                        using (volume) volumes.Add(Convert.ToString(volume["DeviceID"], CultureInfo.InvariantCulture) ?? "");
                    }
                }
                catch (Exception error) when (Expected(error)) { volumeProblem = "Volume mapping unavailable: " + error.Message; }
                var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
                bool? trim = null;
                StorageHealth health;
                try
                {
                    using var handle = Native.CreateFile($@"\\.\PhysicalDrive{number}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var descriptor = Query(handle, 0, 1024);
                    if (descriptor.Length >= 36)
                    {
                        bus = BusName(BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(28)));
                        model = DescriptorString(descriptor, 16) is { Length: > 0 } m ? m : model;
                        firmware = DescriptorString(descriptor, 20) is { Length: > 0 } f ? f : firmware;
                        serial = DescriptorString(descriptor, 24) is { Length: > 0 } s ? s : serial;
                    }
                    try
                    {
                        // Windows' generic descriptor may expose a namespace identifier as the serial.
                        // Identify Controller supplies the manufacturer's actual model, SN and firmware.
                        var identity = StorageSmart.DecodeNvmeIdentify(ReadNvmeProperty(handle, 49, 1, 1, 4096));
                        model = identity.Model;
                        serial = identity.Serial;
                        if (identity.Firmware.Length > 0) firmware = identity.Firmware;
                    }
                    catch (Exception error) when (Expected(error)) { } // Preserve descriptor/WMI identity when unsupported.
                    try { var data = Query(handle, 8, 16); if (data.Length >= 9) trim = data[8] != 0; } catch (Exception error) when (Expected(error)) { }
                    try { health = StorageSmart.DecodeNvme(ReadNvmeLog(handle)); }
                    catch (Exception nvmeError) when (Expected(nvmeError))
                    {
                        try
                        {
                            // SMART_RCV_DRIVE_DATA is a legacy Windows read IOCTL requiring this access mask.
                            // Its only accepted commands here are SMART READ DATA and READ THRESHOLDS.
                            using var ata = Native.CreateFile($@"\\.\PhysicalDrive{number}", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                            if (ata.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                            var data = ReadAtaSmart(ata, number, 0xD0);
                            byte[]? thresholds = null;
                            try { thresholds = ReadAtaSmart(ata, number, 0xD1); } catch (Exception error) when (Expected(error)) { }
                            health = StorageSmart.DecodeAta(data, thresholds);
                        }
                        catch (Exception ataError) when (Expected(ataError))
                        {
                            health = new(bus, "Unknown", null, null, null, [], $"SMART unavailable. NVMe: {nvmeError.Message} ATA: {ataError.Message}");
                        }
                    }
                }
                catch (Exception error) when (Expected(error)) { health = new(bus, "Unknown", null, null, null, [], error.Message); }
                if (volumeProblem != null) health = health with { Detail = health.Detail + " " + volumeProblem };
                result.Add(new(number, model, firmware, serial, capacity, bus, volumes.Distinct().Order().ToArray(),
                    systemRoot != null && volumes.Contains(systemRoot, StringComparer.OrdinalIgnoreCase), trim, health));
            }
        }
        return result.OrderBy(d => d.Number).ToArray();
    }

    private static bool Expected(Exception error) => error is Win32Exception or IOException or UnauthorizedAccessException or ManagementException or COMException or ArgumentException or InvalidOperationException;
    private static string BusName(uint bus) => bus switch { 3 => "ATA", 7 => "USB", 8 => "RAID", 11 => "SATA", 17 => "NVMe", 1 => "SCSI", 15 => "Virtual", 16 => "Storage Spaces", _ => "Bus " + bus };
    private static string DescriptorString(byte[] data, int field)
    {
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(field));
        if (offset == 0 || offset >= data.Length) return "";
        var end = Array.IndexOf(data, (byte)0, (int)offset);
        return Encoding.ASCII.GetString(data, (int)offset, (end < 0 ? data.Length : end) - (int)offset).Trim();
    }
    private static byte[] Query(SafeFileHandle handle, uint property, int size)
    {
        var input = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(input, property);
        return Ioctl(handle, 0x2D1400, input, size);
    }
    private static byte[] ReadNvmeLog(SafeFileHandle handle)
        => ReadNvmeProperty(handle, 50, 2, 2, 512);

    private static byte[] ReadNvmeProperty(SafeFileHandle handle, uint property, uint dataType, uint requestValue, int length)
    {
        var input = new byte[48 + length];
        BinaryPrimitives.WriteUInt32LittleEndian(input, property); // Adapter = 49 for controller, device = 50 for SMART.
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(8), 3); // ProtocolTypeNvme
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(12), dataType); // Identify = 1, LogPage = 2.
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(16), requestValue); // CNS Controller = 1, SMART log = 2.
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(24), 40); // relative to protocol-specific data
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(28), (uint)length);
        var response = Ioctl(handle, 0x2D1400, input, input.Length);
        return dataType == 1 ? StorageSmart.ExtractNvmeIdentify(response) : StorageSmart.ExtractNvmeLog(response);
    }
    private static byte[] ReadAtaSmart(SafeFileHandle handle, int number, byte feature)
    {
        if (feature is not (0xD0 or 0xD1)) throw new ArgumentOutOfRangeException(nameof(feature));
        var input = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(input, 512);
        input[4] = feature; input[5] = 1; input[6] = 1; input[7] = 0x4F; input[8] = 0xC2;
        input[9] = (byte)(0xA0 | ((number & 1) << 4)); input[10] = 0xB0;
        input[12] = (byte)Math.Clamp(number, 0, 255);
        var output = Ioctl(handle, 0x7C088, input, 528);
        if (output.Length < 528 || output[4] != 0 || BinaryPrimitives.ReadUInt32LittleEndian(output) < 512)
            throw new IOException("Incomplete or rejected ATA SMART response.");
        return output.AsSpan(16, 512).ToArray();
    }
    private static byte[] Ioctl(SafeFileHandle handle, uint code, byte[] input, int size)
    {
        var output = new byte[size];
        if (!Native.DeviceIoControl(handle, code, input, input.Length, output, output.Length, out var returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (returned < 0 || returned > output.Length) throw new IOException("Invalid storage response length.");
        return output.AsSpan(0, returned).ToArray();
    }
    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
    }
}

public static class StorageSmart
{
    public static byte[] ExtractNvmeLog(ReadOnlySpan<byte> response) => ExtractNvmeData(response, 2, 512);
    public static byte[] ExtractNvmeIdentify(ReadOnlySpan<byte> response) => ExtractNvmeData(response, 1, 4096);

    private static byte[] ExtractNvmeData(ReadOnlySpan<byte> response, uint dataType, int requiredLength)
    {
        if (response.Length < 48 || U32(response, 0) != 48 || U32(response, 4) < 48 || U32(response, 8) != 3 || U32(response, 12) != dataType)
            throw new IOException("Invalid NVMe protocol descriptor.");
        var offset = U32(response, 24); var length = U32(response, 28);
        if (offset < 40 || length < requiredLength || (ulong)offset + 8 + length > (ulong)response.Length)
            throw new IOException("Truncated NVMe protocol data.");
        return response.Slice(checked(8 + (int)offset), requiredLength).ToArray();
    }

    public static StorageNvmeIdentity DecodeNvmeIdentify(ReadOnlySpan<byte> data)
    {
        if (data.Length != 4096) throw new IOException("A complete 4096-byte NVMe Identify Controller response is required.");
        static string Text(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes) if (b > 126 || b is > 0 and < 32) throw new IOException("Invalid NVMe controller identity text.");
            var value = Encoding.ASCII.GetString(bytes).Trim(' ', '\0');
            if (value.Any(c => c is < ' ' or > '~')) throw new IOException("Invalid NVMe controller identity text.");
            return value;
        }
        var serial = Text(data.Slice(4, 20));
        var model = Text(data.Slice(24, 40));
        var firmware = Text(data.Slice(64, 8));
        if (model.Length == 0 || serial.Length == 0) throw new IOException("NVMe Identify Controller contains no model or serial number.");
        return new(model, firmware, serial);
    }

    public static StorageHealth DecodeNvme(ReadOnlySpan<byte> data)
    {
        if (data.Length != 512) throw new ArgumentException("A complete 512-byte NVMe health log is required.", nameof(data));
        if (data.IndexOfAnyExcept((byte)0) < 0 || data[3] > 100 || data[4] > 100)
            throw new IOException("Empty or malformed NVMe health log.");
        var attributes = new List<StorageAttribute>();
        void Add(int id, string name, string value, string unit = "") => attributes.Add(new(id.ToString("X2"), name, value, unit));
        var kelvin = BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
        double? temperature = kelvin is < 173 or > 473 ? null : kelvin - 273.15;
        var remaining = Math.Clamp(100 - data[5], 0, 100);
        Add(1, "Critical warning", $"0x{data[0]:X2}");
        Add(2, "Composite temperature", temperature?.ToString("F1", CultureInfo.InvariantCulture) ?? "Unknown", "°C");
        Add(3, "Available spare", data[3].ToString(), "%"); Add(4, "Available spare threshold", data[4].ToString(), "%"); Add(5, "Percentage used", data[5].ToString(), "%");
        string[] names = ["Data units read", "Data units written", "Host read commands", "Host write commands", "Controller busy time", "Power cycles", "Power-on hours", "Unsafe shutdowns", "Media and data integrity errors", "Error information log entries"];
        string? hours = null;
        for (var i = 0; i < names.Length; i++)
        {
            var value = new BigInteger(data.Slice(32 + 16 * i, 16), isUnsigned: true, isBigEndian: false).ToString(CultureInfo.InvariantCulture);
            Add(6 + i, names[i], value, i is 0 or 1 ? "× 512,000 bytes" : i == 4 ? "minutes" : i == 6 ? "hours" : "");
            if (i == 6) hours = value;
        }
        Add(16, "Warning temperature time", U32(data, 192).ToString(), "minutes");
        Add(17, "Critical temperature time", U32(data, 196).ToString(), "minutes");
        for (var i = 0; i < 8; i++)
        {
            var t = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(200 + i * 2));
            if (t is >= 173 and <= 473) Add(18 + i, $"Temperature sensor {i + 1}", (t - 273.15).ToString("F1", CultureInfo.InvariantCulture), "°C");
        }
        Add(26, "Thermal management temperature 1 transitions", U32(data, 216).ToString());
        Add(27, "Thermal management temperature 2 transitions", U32(data, 220).ToString());
        Add(28, "Thermal management temperature 1 time", U32(data, 224).ToString(), "seconds");
        Add(29, "Thermal management temperature 2 time", U32(data, 228).ToString(), "seconds");
        var critical = data[0];
        var status = (critical & 0x2C) != 0 || remaining == 0 ? "Bad" : critical != 0 || remaining <= 10 || data[3] < data[4] ? "Caution" : "Good";
        return new("NVMe", status, temperature, remaining, hours, attributes,
            "NVMe SMART / Health log 02h. Remaining life is 100 minus Percentage Used, clamped to 0–100%; it is an endurance estimate. Critical warning flags also affect health.");
    }

    public static StorageHealth DecodeAta(ReadOnlySpan<byte> data, byte[]? thresholds = null)
    {
        if (data.Length != 512 || data.ToArray().Aggregate(0, (sum, b) => (sum + b) & 255) != 0)
            throw new IOException("Incomplete ATA SMART data or invalid checksum.");
        if (thresholds != null && (thresholds.Length != 512 || thresholds.Aggregate(0, (sum, b) => (sum + b) & 255) != 0)) thresholds = null;
        var limits = new Dictionary<byte, byte>();
        if (thresholds != null) for (var i = 2; i + 12 <= 362; i += 12) if (thresholds[i] != 0) limits[thresholds[i]] = thresholds[i + 1];
        var attrs = new List<StorageAttribute>(); double? temperature = null; string? hours = null; var failing = false;
        for (var i = 2; i + 12 <= 362; i += 12)
        {
            var id = data[i]; if (id == 0) continue;
            ulong raw = 0; for (var b = 0; b < 6; b++) raw |= (ulong)data[i + 5 + b] << (8 * b);
            var name = id switch { 5 => "Reallocated sectors", 9 => "Power-on time (vendor units)", 12 => "Power cycles", 187 => "Reported uncorrectable errors", 188 => "Command timeouts", 190 => "Airflow temperature (vendor format)", 194 => "Temperature (vendor format)", 197 => "Current pending sectors", 198 => "Offline uncorrectable sectors", 199 => "UDMA CRC errors", 241 => "Total host writes (vendor units)", 242 => "Total host reads (vendor units)", _ => $"Vendor attribute {id:X2}" };
            var hasLimit = limits.TryGetValue(id, out var limit);
            if (hasLimit && limit > 0 && data[i + 3] <= limit) failing = true;
            // C2 low byte is the conventional ATA temperature; never interpret arbitrary wear IDs as percentages.
            if (id == 194 && data[i + 5] is > 0 and < 127) temperature = data[i + 5];
            // Power-on units vary by controller; expose the raw value in the table, not invented hours.
            attrs.Add(new(id.ToString("X2"), name, raw.ToString(CultureInfo.InvariantCulture), "", data[i + 3].ToString(), data[i + 4].ToString(), hasLimit ? limit.ToString() : "", Convert.ToHexString(data.Slice(i + 5, 6))));
        }
        if (attrs.Count == 0) throw new IOException("ATA SMART log contains no attributes.");
        return new("ATA", failing ? "Bad" : "Unknown", temperature, null, hours, attrs,
            "ATA SMART read data. Raw units and wear attributes depend on the controller. No endurance percentage is inferred. Health remains unknown unless a returned threshold is crossed.");
    }

    public static string ToCsv(StorageDrive drive)
    {
        var csv = new StringBuilder();
        void Row(params string[] fields) => csv.AppendLine(string.Join(",", fields.Select(Escape)));
        Row("Disk", drive.Number.ToString(), "Model", drive.Model, "Serial", drive.Serial, "Firmware", drive.Firmware);
        Row("Captured UTC", drive.CapturedAt.ToString("O"), "Protocol", drive.Health.Protocol);
        Row("ID", "Attribute Name", "Value", "Unit", "Normalized", "Worst", "Threshold", "Raw bytes (hex)");
        foreach (var a in drive.Health.Attributes) Row(a.Id, a.Name, a.Value, a.Unit, a.Normalized, a.Worst, a.Threshold, a.RawHex);
        return csv.ToString();
    }
    private static string Escape(string value)
    {
        // CSV is commonly opened in Excel; hardware identity strings must never become formulas.
        var first = value.AsSpan().TrimStart();
        if (value.Length > 0 && (value[0] is '\t' or '\r' or '\n' || first.Length > 0 && first[0] is '=' or '+' or '-' or '@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
