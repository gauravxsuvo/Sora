using System.Diagnostics;
using System.Text;

namespace Sora.Core;

/// <summary>Effects built into the ENE DIMM controller. These are the modes GCC hands to the ENE HAL (AACDDR_SetMode clamps to 0-5).</summary>
public enum EneMode : byte { Off = 0, Static = 1, Breathing = 2, Flashing = 3, ColorCycle = 4, Rainbow = 5 }

/// <summary>How sync frames reach the colour registers: one SMBus block, the same bytes as an I2C block, or byte by byte.</summary>
public enum EneFrameWrite { Bytes, SmbusBlock, I2cBlock }

/// <summary>Byte order of one LED in the colour registers.</summary>
public enum EneColorOrder { Rbg, Rgb }

/// <summary>An SPD hub that answered on the SMBus. Slot n has its SPD hub at 0x50+n and its RGB controller (if any) at 0x70+n.</summary>
public sealed record SpdSlot(int Slot, byte Address, int DeviceType);

/// <summary>
/// Raw control and colour registers of one DIMM: the backup Sora restores from. Brightness is control +9 (0-9), which GCC's
/// ENE HAL sets on chip type 0xD5; null where it is not read or not written.
/// </summary>
public sealed record EneSnapshot(byte[] Control, byte[] Colors, byte? Brightness = null);

/// <summary>
/// A verified ENE lighting controller on a DDR5 DIMM.
/// Register map from GCC's ENE HAL (AacHal_x64.dll, AURACtrlDDR5): the 16-bit chip ID at 0xF01C selects
/// the control block (0x8020 or 0xE020) and the effect colour block (0x8160, 0x8010 or 0xE300).
/// </summary>
public sealed record EneDimm(int Slot, byte Address, string Name, ushort ChipId, byte[] Firmware, int LedCount, ushort ControlBase, ushort ColorBase, EneSnapshot Original)
{
    /// <summary>
    /// Chip type 0xD5 (firmware byte 1): GCC's HAL writes the brightness register after the colours (AacHal SetColors).
    /// </summary>
    public bool HasBrightness => Firmware.Length > 1 && Firmware[1] == 0xD5;

    /// <summary>
    /// The only registers Sora ever writes: direct flag, mode, speed and direction; brightness (+9) on 0xD5 chips, as GCC does;
    /// the apply byte (+0x0F); the effect colours.
    /// </summary>
    public bool CanWrite(ushort register) =>
        register >= ControlBase && register <= ControlBase + 3 ||
        register == ControlBase + 9 && HasBrightness ||
        register == ControlBase + 0x0F ||
        register >= ColorBase && register < ColorBase + 3 * LedCount;
}

public sealed record EneProbe(string Controller, int PciVendor, int PciDevice, bool SpdWriteProtected, IReadOnlyList<SpdSlot> Spd,
    IReadOnlyList<EneCandidate> Candidates, IReadOnlyList<EneDimm> Dimms);

/// <summary>What answered at 0x70+n and why it was or was not accepted. Kept for diagnostics.</summary>
public sealed record EneCandidate(int Slot, byte Address, bool Answered, string? Signature, string? Rejected, string? Name = null, string? ChipId = null,
    string? Id4000 = null, string? Id1030 = null, string? Firmware = null, string? Config = null, string? CapabilityB500 = null, string? Control = null, string? Colors = null, string? DirectColors = null);

public static class EneDram
{
    public const int MaxLeds = 15;

    /// <summary>
    /// Finds ENE controllers. Every step is a read until a candidate has passed all read-only checks:
    /// an SPD hub answers at 0x50+n, the controller answers a receive-byte at 0x70+n, and command codes 0xA0-0xAF
    /// return 0x00-0x0F (ENE's read-only signature). Only then is the register pointer written to read its name and state.
    /// </summary>
    public static EneProbe Scan(Smbus bus)
    {
        var spd = new List<SpdSlot>();
        var candidates = new List<EneCandidate>();
        var dimms = new List<EneDimm>();
        for (var slot = 0; slot < 8; slot++)
        {
            using var held = bus.Lock();
            var spdAddress = (byte)(0x50 + slot);
            var type = bus.ReadByte(spdAddress, 0x00);
            if (type < 0) continue;
            spd.Add(new SpdSlot(slot, spdAddress, type));
            var address = (byte)(Smbus.FirstLightingAddress + slot);
            if (bus.ReceiveByte(address) < 0) { candidates.Add(new EneCandidate(slot, address, false, null, "No device answered.")); continue; }
            var signature = new byte[16];
            var valid = true;
            for (var i = 0; i < 16; i++)
            {
                var value = bus.ReadByte(address, (byte)(0xA0 + i));
                signature[i] = (byte)Math.Max(0, value);
                valid &= value == i;
            }
            if (!valid) { candidates.Add(new EneCandidate(slot, address, true, Convert.ToHexString(signature), "Not an ENE controller (signature mismatch); left untouched.")); continue; }
            var candidate = Identify(bus, slot, address, signature, out var dimm);
            candidates.Add(candidate);
            if (dimm != null) dimms.Add(dimm);
        }
        return new EneProbe(bus.Controller, bus.PciVendor, bus.PciDevice, bus.SpdWriteProtected, spd, candidates, dimms);
    }

    private static EneCandidate Identify(Smbus bus, int slot, byte address, byte[] signature, out EneDimm? dimm)
    {
        dimm = null;
        var sig = Convert.ToHexString(signature);
        var name = ReadBlock(bus, address, 0x1000, 16);
        var chip = ReadId(bus, address, 0xF01C);
        var id4000 = ReadId(bus, address, 0x4000);
        var id1030 = ReadId(bus, address, 0x1030);
        var firmware = ReadBlock(bus, address, 0x87FB, 5);
        var config = ReadBlock(bus, address, 0x1C00, 16);
        var capability = ReadBlock(bus, address, 0xB500, 5);
        if (name == null || chip == null || config == null)
            return new EneCandidate(slot, address, true, sig, "Register reads failed.");
        var text = Encoding.ASCII.GetString(name.TakeWhile(b => b is >= 0x20 and < 0x7F).ToArray());
        var chipId = chip.Value;
        var (control, color) = chipId switch
        {
            0x7742 or 0x7730 => ((ushort)0xE020, (ushort)0xE300),
            0x3570 => ((ushort)0x8020, (ushort)0x8010),
            _ => ((ushort)0x8020, (ushort)0x8160),
        };
        var leds = config[2];
        var direct = ReadBlock(bus, address, (ushort)(color == 0x8160 ? 0x8100 : color == 0x8010 ? 0x8000 : 0xE100), Math.Clamp((int)leds, 1, MaxLeds) * 3);
        var controlBytes = ReadBlock(bus, address, control, 16);
        var colorBytes = leds is >= 1 and <= MaxLeds ? ReadBlock(bus, address, color, leds * 3) : null;
        var result = new EneCandidate(slot, address, true, sig, null, text, chipId.ToString("X4"), id4000?.ToString("X4"), id1030?.ToString("X4"),
            firmware == null ? null : Convert.ToHexString(firmware), Convert.ToHexString(config), capability == null ? null : Convert.ToHexString(capability),
            controlBytes == null ? null : Convert.ToHexString(controlBytes), colorBytes == null ? null : Convert.ToHexString(colorBytes), direct == null ? null : Convert.ToHexString(direct));
        if (text.Length < 4) return result with { Rejected = "The controller name is not readable text, so the register byte order is unconfirmed." };
        if (leds is < 1 or > MaxLeds) return result with { Rejected = $"Unexpected LED count {leds}." };
        if (controlBytes == null || colorBytes == null) return result with { Rejected = "Could not read the current lighting state." };
        var hasBrightness = firmware is { Length: > 1 } && firmware[1] == 0xD5;
        dimm = new EneDimm(slot, address, text, chipId, firmware ?? [], leds, control, color, new EneSnapshot(controlBytes[..4], colorBytes, hasBrightness ? controlBytes[9] : null));
        return result;
    }

    /// <summary>
    /// GCC's brightness for 0xD5 chips (AacHal SetColors, recovered from the disassembly and confirmed by capturing GCC):
    /// (R+G+B)/84 for the single-colour effects, so a pure red runs at 3 of 9 and white at 9; 9 for Color cycle and Rainbow.
    /// Sora keeps at least 1 so a dim colour is not switched off. Null for Off, which GCC sends without colours.
    /// </summary>
    public static byte? GccBrightness(EneMode mode, byte red, byte green, byte blue) => mode switch
    {
        EneMode.Static or EneMode.Breathing or EneMode.Flashing => (byte)Math.Clamp((red + green + blue) / 84, 1, 9),
        EneMode.ColorCycle or EneMode.Rainbow => 9,
        _ => null,
    };

    /// <summary>Encodes one colour for the controller. GCC's ENE path passes red first; the other two depend on the byte order.</summary>
    public static byte[] Encode(byte red, byte green, byte blue, EneColorOrder order) => order == EneColorOrder.Rbg ? [red, blue, green] : [red, green, blue];

    /// <summary>
    /// Writes one effect to every DIMM, then reads it back. Holds the SMBus lock per DIMM, re-checks the controller's
    /// identity first, and writes only registers allowed by <see cref="EneDimm.CanWrite"/>. Nothing is saved to the controller's flash.
    /// </summary>
    public static void Apply(Smbus bus, IReadOnlyList<EneDimm> dimms, EneMode mode, byte red, byte green, byte blue, byte speed, bool reverse, EneColorOrder order)
    {
        if (speed > 4) throw new ArgumentOutOfRangeException(nameof(speed));
        Write(bus, dimms.Select(dimm =>
        {
            var colors = new byte[dimm.LedCount * 3];
            var one = Encode(red, green, blue, order);
            for (var i = 0; i < dimm.LedCount; i++) one.CopyTo(colors, i * 3);
            return (dimm, new EneSnapshot([0x00, (byte)mode, speed, (byte)(reverse ? 1 : 0)], colors, dimm.HasBrightness ? GccBrightness(mode, red, green, blue) : null));
        }).ToArray());
    }

    /// <summary>Puts back the registers captured during the scan.</summary>
    public static void Restore(Smbus bus, IReadOnlyList<EneDimm> dimms) => Write(bus, dimms.Select(dimm => (dimm, dimm.Original)).ToArray());

    /// <summary>
    /// One sync frame: a Static colour on every LED of every DIMM, as GCC's sync threads write it (AACDDR_SetMode(1, r, g, b) per
    /// frame, brightness from the colour on 0xD5 chips). Only the colour block, brightness and apply byte are written, all DIMMs
    /// under one SMBus lock so they change together. The DIMMs must already be in Static (set by <see cref="Apply"/>, which
    /// verified each controller and read it back); the caller re-verifies them with <see cref="Read"/> every few seconds.
    /// </summary>
    /// <remarks>
    /// With <paramref name="block"/>, the colours go out as one SMBus block write (command 0x03 after the register pointer), as
    /// GCC's ENE HAL writes them; the caller confirms this path once with a readback before using it.
    /// </remarks>
    public static void WriteFrame(Smbus bus, IReadOnlyList<EneDimm> dimms, byte red, byte green, byte blue, EneColorOrder order, EneFrameWrite method = EneFrameWrite.Bytes, byte? lastBrightness = null)
    {
        var one = Encode(red, green, blue, order);
        var brightness = GccBrightness(EneMode.Static, red, green, blue)!.Value;
        using var held = bus.Lock();
        foreach (var dimm in dimms)
        {
            if (method != EneFrameWrite.Bytes)
            {
                var colors = new byte[dimm.LedCount * 3];
                for (var led = 0; led < dimm.LedCount; led++) one.CopyTo(colors, led * 3);
                WriteColorBlock(bus, dimm, colors, method);
            }
            else
                for (var led = 0; led < dimm.LedCount; led++)
                    for (var c = 0; c < 3; c++) WriteRegister(bus, dimm, (ushort)(dimm.ColorBase + led * 3 + c), one[c]);
            if (dimm.HasBrightness && brightness != lastBrightness) WriteRegister(bus, dimm, (ushort)(dimm.ControlBase + 9), brightness);
        }
        // Apply last and back to back, so every stick shows the frame at the same moment.
        foreach (var dimm in dimms) WriteRegister(bus, dimm, (ushort)(dimm.ControlBase + 0x0F), 0x01);
    }

    /// <summary>Reads the current control and colour registers.</summary>
    public static EneSnapshot Read(Smbus bus, EneDimm dimm)
    {
        using var held = bus.Lock();
        Verify(bus, dimm);
        var control = ReadBlock(bus, dimm.Address, dimm.ControlBase, 10) ?? throw new IOException($"Could not read DIMM {dimm.Slot} lighting mode.");
        return new EneSnapshot(control[..4],
            ReadBlock(bus, dimm.Address, dimm.ColorBase, dimm.LedCount * 3) ?? throw new IOException($"Could not read DIMM {dimm.Slot} colours."),
            dimm.HasBrightness ? control[9] : null);
    }

    /// <summary>
    /// Writes every DIMM's registers first and their apply bytes last, back to back, so the effects start together and the
    /// sticks stay in step; then reads each DIMM back.
    /// </summary>
    private static void Write(Smbus bus, IReadOnlyList<(EneDimm Dimm, EneSnapshot Target)> writes)
    {
        foreach (var (dimm, target) in writes)
        {
            if (target.Control.Length != 4 || target.Colors.Length != dimm.LedCount * 3) throw new ArgumentException("Lighting data does not match this DIMM.");
            if (target.Brightness > 9) throw new ArgumentOutOfRangeException(nameof(writes), "ENE brightness is 0-9.");
            var builtIn = target.Control is [0x00, <= (byte)EneMode.Rainbow, <= 4, <= 1];
            if (!builtIn && !target.Control.SequenceEqual(dimm.Original.Control))
                throw new InvalidOperationException("Sora writes only the built-in effects 0-5 (or the controller's own original state).");
        }
        using (var held = bus.Lock())
        {
            foreach (var (dimm, target) in writes)
            {
                Verify(bus, dimm);
                for (var i = 0; i < target.Colors.Length; i++) WriteRegister(bus, dimm, (ushort)(dimm.ColorBase + i), target.Colors[i]);
                for (var i = 0; i < 4; i++) WriteRegister(bus, dimm, (ushort)(dimm.ControlBase + i), target.Control[i]);
                if (target.Brightness is { } brightness) WriteRegister(bus, dimm, (ushort)(dimm.ControlBase + 9), brightness);
            }
            foreach (var (dimm, _) in writes) WriteRegister(bus, dimm, (ushort)(dimm.ControlBase + 0x0F), 0x01);
        }
        Thread.Sleep(20);
        foreach (var (dimm, target) in writes)
        {
            var actual = Read(bus, dimm);
            if (!actual.Control.SequenceEqual(target.Control) || !actual.Colors.SequenceEqual(target.Colors) || target.Brightness != null && actual.Brightness != target.Brightness)
                throw new IOException($"DIMM {dimm.Slot} did not read back what was written (mode {Convert.ToHexString(actual.Control)}, colours {Convert.ToHexString(actual.Colors)}, brightness {actual.Brightness}).");
        }
    }

    /// <summary>Same device as at scan time: the signature still matches and the name and chip ID are unchanged.</summary>
    private static void Verify(Smbus bus, EneDimm dimm)
    {
        for (var i = 0; i < 16; i++)
            if (bus.ReadByte(dimm.Address, (byte)(0xA0 + i)) != i) throw new IOException($"The controller at 0x{dimm.Address:X2} no longer identifies as ENE. Nothing was written.");
        var name = ReadBlock(bus, dimm.Address, 0x1000, 16);
        var text = name == null ? "" : Encoding.ASCII.GetString(name.TakeWhile(b => b is >= 0x20 and < 0x7F).ToArray());
        if (text != dimm.Name || ReadId(bus, dimm.Address, 0xF01C) != dimm.ChipId) throw new IOException($"The controller at 0x{dimm.Address:X2} changed identity. Nothing was written.");
    }

    /// <summary>The whole effect colour block in one SMBus block write; every register it covers must be writable.</summary>
    private static void WriteColorBlock(Smbus bus, EneDimm dimm, byte[] colors, EneFrameWrite method)
    {
        if (colors.Length != dimm.LedCount * 3) throw new ArgumentException("Colour data does not match this DIMM.");
        for (var i = 0; i < colors.Length; i++)
            if (!dimm.CanWrite((ushort)(dimm.ColorBase + i))) throw new InvalidOperationException($"Refused ENE block write over register 0x{dimm.ColorBase + i:X4}.");
        if (bus.WriteWord(dimm.Address, 0x00, Swap(dimm.ColorBase)) < 0) throw new IOException($"SMBus write to DIMM {dimm.Slot} failed.");
        var result = method == EneFrameWrite.SmbusBlock ? bus.WriteBlock(dimm.Address, 0x03, colors) : bus.WriteBlockAsI2c(dimm.Address, 0x03, colors);
        if (result < 0) throw new IOException($"{method} write to DIMM {dimm.Slot} failed.");
    }

    private static void WriteRegister(Smbus bus, EneDimm dimm, ushort register, byte value)
    {
        if (!dimm.CanWrite(register)) throw new InvalidOperationException($"Refused ENE write to register 0x{register:X4}.");
        if (bus.WriteWord(dimm.Address, 0x00, Swap(register)) < 0 || bus.WriteByte(dimm.Address, 0x01, value) < 0)
            throw new IOException($"SMBus write to DIMM {dimm.Slot} failed at register 0x{register:X4}.");
    }

    /// <summary>ENE takes the register address high byte first: write it as a word to command 0x00, then read with command 0x81.</summary>
    private static byte[]? ReadBlock(Smbus bus, byte address, ushort register, int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            if (bus.WriteWord(address, 0x00, Swap((ushort)(register + i))) < 0) return null;
            var value = bus.ReadByte(address, 0x81);
            if (value < 0) return null;
            data[i] = (byte)value;
        }
        return data;
    }

    /// <summary>16-bit ID registers, as GCC reads them (word read with command 0x82), in the ENE HAL's byte order.</summary>
    private static ushort? ReadId(Smbus bus, byte address, ushort register)
    {
        if (bus.WriteWord(address, 0x00, Swap(register)) < 0) return null;
        var value = bus.ReadWord(address, 0x82);
        return value < 0 ? null : Swap((ushort)value);
    }

    private static ushort Swap(ushort value) => (ushort)(value << 8 | value >> 8);

    /// <summary>Programs that also drive DIMM or board lighting. Sora will not write while one runs.</summary>
    public static string? FindConflict()
    {
        foreach (var (process, product) in new[] { ("GCC", "GIGABYTE Control Center"), ("GBT_DL_LIB", "GIGABYTE Control Center"), ("ArmouryCrate", "Armoury Crate"), ("LightingService", "Armoury Crate"), ("OpenRGB", "OpenRGB"), ("SignalRgb", "SignalRGB"), ("iCUE", "Corsair iCUE"), ("XPG PRIME", "XPG PRIME") })
        {
            var running = Process.GetProcessesByName(process);
            try { if (running.Length > 0) return $"{product} is running and also controls this lighting. Exit it first."; }
            finally { foreach (var p in running) p.Dispose(); }
        }
        return null;
    }
}
