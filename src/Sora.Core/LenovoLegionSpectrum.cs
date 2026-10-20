namespace Sora.Core;

public sealed record LegionSpectrumSetting(byte Brightness, byte Profile);
public sealed record LegionSpectrumState(DeviceInfo Device, LegionSpectrumSetting Setting);
public interface ILegionSpectrumTransport
{
    DeviceInfo[] SpectrumDevices();
    LegionSpectrumSetting ReadSpectrum(DeviceInfo device);
    void WriteSpectrum(DeviceInfo device, LegionSpectrumSetting setting);
}
public static class LenovoLegionSpectrum
{
    public const int ReportLength = 960;
    public static bool IsCandidate(DeviceInfo device) => device.VendorId == 0x048d && device.FeatureReportLength == ReportLength
        && (device.ProductId & 0xff00) is 0xc100 or 0xc900 or 0xc600;
    public static byte[] Query(byte operation)
    {
        if (operation is not (0xd1 or 0xcd or 0xca)) throw new ArgumentOutOfRangeException(nameof(operation));
        return Header(operation);
    }
    public static byte[] Brightness(byte brightness)
    {
        if (brightness > 9) throw new ArgumentOutOfRangeException(nameof(brightness));
        var report = Header(0xce); report[4] = brightness; return report;
    }
    public static byte[] Profile(byte profile)
    {
        if (profile > 6) throw new ArgumentOutOfRangeException(nameof(profile));
        var report = Header(0xc8); report[4] = profile; return report;
    }
    public static byte ReadResponse(byte operation, ReadOnlySpan<byte> response)
    {
        _ = Query(operation);
        if (response.Length != ReportLength || response[0] != 7 || response[1] != operation)
            throw new InvalidDataException("Unrelated or incomplete Spectrum response.");
        var value = response[4];
        if ((operation == 0xd1 && value != 0) || (operation == 0xcd && value > 9) || (operation == 0xca && value > 6))
            throw new InvalidDataException("Unsupported Spectrum compatibility, brightness or profile value.");
        return value;
    }
    private static byte[] Header(byte operation)
    {
        var report = new byte[ReportLength];
        report[0] = 7; report[1] = operation; report[2] = 0xc0; report[3] = 3;
        return report;
    }
}
