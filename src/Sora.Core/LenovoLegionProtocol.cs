using System.Buffers.Binary;

namespace Sora.Core;

// Wire formats documented in docs/Lenovo-Legion-protocol-map.md. These pure codecs
// perform no device I/O and do not establish firmware/model compatibility.
public enum LegionPowerMode : uint { Quiet = 1, Balanced = 2, Performance = 3, Extreme = 224, Custom = 255 }
public enum LegionChargeMode { Normal, Conservation, RapidCharge }
public enum LegionFourZoneEffect : byte { Static = 1, Breathing = 3, WaveLeft = 4, WaveRight = 5, ColorCycle = 6 }

public static class LenovoLegionProtocol
{
    public const uint ChargeModeIoctl = 0x831020F8;
    public const uint ReadChargeModeRequest = 0xFF;
    public const int FourZoneReportLength = 33;
    public const int FanTableReportLength = 64;

    public static LegionPowerMode DecodePowerMode(uint value) => value switch
    {
        1 => LegionPowerMode.Quiet,
        2 => LegionPowerMode.Balanced,
        3 => LegionPowerMode.Performance,
        224 => LegionPowerMode.Extreme,
        255 => LegionPowerMode.Custom,
        _ => throw new InvalidDataException("Unknown Lenovo firmware power mode.")
    };

    public static uint EncodePowerMode(LegionPowerMode mode) => (uint)DecodePowerMode((uint)mode);

    public static LegionChargeMode DecodeChargeMode(uint flags)
    {
        // Conservation takes precedence if the driver returns both bits, as in the upstream decoder.
        if ((flags & 0x20) != 0) return LegionChargeMode.Conservation;
        return (flags & 0x04) != 0 ? LegionChargeMode.RapidCharge : LegionChargeMode.Normal;
    }

    /// <summary>Ordered DWORD inputs to the charging IOCTL, not a concatenated driver buffer.</summary>
    public static uint[] ChargeModeRequests(LegionChargeMode mode) => mode switch
    {
        LegionChargeMode.Normal => [0x05, 0x08],
        LegionChargeMode.Conservation => [0x08, 0x03],
        LegionChargeMode.RapidCharge => [0x05, 0x07],
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static byte[] FourZoneOffReport()
    {
        var report = new byte[FourZoneReportLength];
        report[0] = 0xCC;
        report[1] = 0x16;
        return report;
    }

    public static byte[] FourZoneReport(LegionFourZoneEffect effect, byte speed, byte brightness, ReadOnlySpan<byte> zoneRgb)
    {
        if (!Enum.IsDefined(effect)) throw new ArgumentOutOfRangeException(nameof(effect));
        if (speed is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(speed));
        if (brightness is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(brightness));
        if (zoneRgb.Length != 12) throw new ArgumentException("Four zones require twelve RGB bytes.", nameof(zoneRgb));
        var report = FourZoneOffReport();
        report[2] = effect is LegionFourZoneEffect.WaveLeft or LegionFourZoneEffect.WaveRight ? (byte)4 : (byte)effect;
        report[3] = speed;
        report[4] = brightness;
        if (effect is LegionFourZoneEffect.Static or LegionFourZoneEffect.Breathing) zoneRgb.CopyTo(report.AsSpan(5, 12));
        if (effect == LegionFourZoneEffect.WaveLeft) report[19] = 1;
        if (effect == LegionFourZoneEffect.WaveRight) report[18] = 1;
        return report;
    }

    /// <summary>Ten raw firmware levels; this is serialization, not a thermal-policy validator.</summary>
    public static byte[] FanTableReport(ReadOnlySpan<ushort> levels)
    {
        if (levels.Length != 10) throw new ArgumentException("The firmware table requires ten levels.", nameof(levels));
        var report = new byte[FanTableReportLength];
        report[0] = 1; // table mode
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(2, 4), 10);
        for (var index = 0; index < levels.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(6 + index * 2, 2), levels[index]);
        return report;
    }
}
