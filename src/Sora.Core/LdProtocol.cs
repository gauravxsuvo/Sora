using System.Buffers.Binary;

namespace Sora.Core;

public static class LdProtocol
{
    public const ushort VendorId = 0x3633, ProductId = 0x000a;
    public static byte[] Initialize() => Frame([0x68, 1, 1, 2, 3, 1]);
    public static byte[] LeadingZeros(bool enabled) => Frame([0x68, 1, 1, 2, 2, (byte)(enabled ? 1 : 0)]);
    public static byte[] Close() => [0x10, 0x68, 1, 1, 0x0b, 1, 0, 8, 0, 100, 0, 0x42, 0x70, 0, 0, 40, 0xbc, 0x16];

    public static byte[] Telemetry(SensorSample sample, bool fahrenheit)
    {
        sample.Validate();
        var report = new byte[18];
        byte[] header = [0x10, 0x68, 1, 1, 0x0b, 1, 2, 5];
        header.CopyTo(report, 0);
        BinaryPrimitives.WriteUInt16BigEndian(report.AsSpan(8), (ushort)Math.Truncate(sample.PowerW));
        report[10] = (byte)(fahrenheit ? 1 : 0);
        var temperature = fahrenheit ? 32 + 1.8 * sample.TemperatureC : sample.TemperatureC;
        BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(11), VendorFloatBits(temperature));
        report[15] = (byte)Math.Truncate(sample.UsagePercent);
        report[16] = Checksum(report.AsSpan(1, 15));
        report[17] = 0x16;
        return report;
    }

    // Match the recovered vendor encoder's truncation, rather than float rounding.
    public static uint VendorFloatBits(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == 0) return 0;
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7ff) - 1023 + 127;
        if (exponent is <= 0 or >= 255) throw new ArgumentOutOfRangeException(nameof(value));
        return (uint)(((bits >> 63) << 31) | ((ulong)exponent << 23) | ((bits & 0xfffffffffffff) >> 29));
    }

    private static byte[] Frame(byte[] body) => [0x10, .. body, Checksum(body), 0x16];
    private static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var value in bytes) sum += value;
        return (byte)(sum & 255);
    }
}
