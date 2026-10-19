using System.Buffers.Binary;

namespace Sora.Core;

public sealed record LogitechFeature(ushort Id, byte Index, byte Type, byte Version);
public sealed record LogitechSensor(byte Index, int Dpi, int DefaultDpi, int Minimum, int Maximum, int Step, int[] Values)
{
    public bool Accepts(int dpi) => Step > 0 ? dpi >= Minimum && dpi <= Maximum && (dpi - Minimum) % Step == 0 : Values.Contains(dpi);
}
public sealed record LogitechSnapshot(string Name, string Identity, ushort ProductId, string Protocol,
    LogitechFeature[] Features, LogitechSensor[] Sensors, int? ReportRate, int[] ReportRates, int? OnboardMode, string[] Warnings,
    LogitechLightZone[]? Lighting = null);
public sealed record LogitechLightZone(byte Index, string Name, int[] Effects, byte[] State);
public sealed record LogitechOnboardSlot(int Slot, bool Enabled, bool ChecksumValid, string Name, int[] Dpi, int ReportRate, string[] Bindings, string[] ShiftBindings, byte[] Raw);

public static class LogitechLighting
{
    // 8070 transmits the per-zone effect INDEX; onboard profiles store the effect ID.
    // G502 HERO advertises [0, 1, 10, 3], so breathing ID 10 is wire index 2.
    public static byte[] ToWire(byte[] state, int[] effects)
    {
        if (state.Length != 11) throw new InvalidDataException("Invalid Logitech light state.");
        var index = Array.IndexOf(effects, (int)state[0]);
        if (index is < 0 or > 255) throw new InvalidDataException("The lighting effect is not advertised by this zone.");
        var wire = state.ToArray(); wire[0] = (byte)index; return wire;
    }
    public static byte[] FromWire(ReadOnlySpan<byte> wire, int[] effects)
    {
        if (wire.Length != 11 || wire[0] >= effects.Length || effects[wire[0]] > 255) throw new InvalidDataException("The mouse returned an unknown light effect index.");
        var state = wire.ToArray(); state[0] = (byte)effects[wire[0]]; return state;
    }
    public static string EffectName(int mode) => mode switch { 0 => "Off", 1 => "Fixed", 3 => "Cycle", 10 => "Breathing", _ => $"Effect {mode}" };
    public static byte[] Encode(int mode, string hex, int period, int brightness)
    {
        if (mode is not (0 or 1 or 3 or 10) || period is < 1000 or > 20000 || brightness is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(mode));
        var color = Convert.FromHexString(hex.TrimStart('#'));
        if (color.Length != 3) throw new InvalidDataException("Enter a six-digit RGB color.");
        var state = new byte[11]; state[0] = (byte)mode;
        if (mode == 1) for (var i = 0; i < 3; i++) state[i + 1] = (byte)(color[i] * brightness / 100);
        if (mode == 10)
        {
            color.CopyTo(state, 1); BinaryPrimitives.WriteUInt16BigEndian(state.AsSpan(4), (ushort)period);
            state[6] = 0; state[7] = (byte)(brightness == 100 ? 0 : brightness);
        }
        if (mode == 3)
        {
            BinaryPrimitives.WriteUInt16BigEndian(state.AsSpan(6), (ushort)period);
            state[8] = (byte)(brightness == 100 ? 0 : brightness);
        }
        return state;
    }
}

public static class LogitechProtocol
{
    public static byte[] Request(byte device, byte feature, byte function, byte softwareId, ReadOnlySpan<byte> data)
    {
        if (function > 15 || softwareId is 0 or > 15 || data.Length > 16) throw new ArgumentOutOfRangeException(nameof(function));
        var report = new byte[data.Length <= 3 ? 7 : 20];
        report[0] = report.Length == 7 ? (byte)0x10 : (byte)0x11;
        report[1] = device; report[2] = feature; report[3] = (byte)((function << 4) | softwareId);
        data.CopyTo(report.AsSpan(4));
        return report;
    }

    // Notifications and replies to another program must never complete our command.
    public static bool Matches(ReadOnlySpan<byte> reply, ReadOnlySpan<byte> request)
    {
        if (reply.Length < 7 || request.Length < 7 || reply[0] is not (0x10 or 0x11) || reply[1] != request[1]) return false;
        return reply[2] == 0xff ? reply[3] == request[2] && reply[4] == request[3]
            : reply[2] == request[2] && reply[3] == request[3];
    }

    public static bool IsResponseCandidate(ReadOnlySpan<byte> report) => report.Length >= 7 && (report[2] == 0xff || (report[3] & 15) != 0);

    public static void CheckReply(ReadOnlySpan<byte> reply, ReadOnlySpan<byte> request)
    {
        if (!Matches(reply, request)) throw new InvalidDataException("The HID++ reply does not match this request.");
        if (reply[2] == 0xff) throw new IOException($"Logitech rejected feature {request[2]:X2}, function {request[3] >> 4:X1}: HID++ error {reply[5]:X2}.");
    }

    public static LogitechSensor ParseSensor(byte index, ReadOnlySpan<byte> list, ReadOnlySpan<byte> current)
    {
        if (list.Length < 3 || current.Length < 5 || list[0] != index || current[0] != index) throw new InvalidDataException("Invalid Logitech sensor response.");
        var values = new List<int>(); var step = 0;
        for (var offset = 1; offset + 1 < list.Length; offset += 2)
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(list[offset..]);
            if (value == 0) break;
            if (value > 0xe000) step = value - 0xe000; else values.Add(value);
        }
        if (values.Count == 0 || (step > 0 && values.Count != 2)) throw new InvalidDataException("The mouse returned an unsupported DPI range.");
        return new(index, BinaryPrimitives.ReadUInt16BigEndian(current[1..]), BinaryPrimitives.ReadUInt16BigEndian(current[3..]), values.Min(), values.Max(), step, values.ToArray());
    }

    public static int[] ParseReportRates(byte mask) => Enumerable.Range(1, 8).Where(period => (mask & (1 << (period - 1))) != 0).Select(period => 1000 / period).OrderDescending().ToArray();

    public static bool ValidChecksum(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3) return false;
        ushort crc = 0xffff;
        foreach (var value in data[..^2])
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc == BinaryPrimitives.ReadUInt16BigEndian(data[^2..]);
    }

    public static string DescribeBinding(ReadOnlySpan<byte> b)
    {
        if (b.Length != 4) throw new InvalidDataException("A Logitech button binding must be four bytes.");
        if (b[0] == 0xff) return "Disabled";
        if (b[0] == 0x90) return b[1] switch { 1 => "Scroll left", 2 => "Scroll right", 3 => "DPI up", 4 => "DPI down", 5 => "Cycle DPI", 6 => "Default DPI", 7 => "DPI shift", 8 => "Next profile", 9 => "Previous profile", 10 => "Cycle profile", 11 => "G-Shift", _ => $"Special {b[1]:X2}" };
        if (b[0] == 0x80) return b[1] switch { 0 => "No action", 1 => BinaryPrimitives.ReadUInt16BigEndian(b[2..]) switch { 1 => "Left click", 2 => "Right click", 4 => "Middle click", 8 => "Back", 16 => "Forward", var mask => $"Mouse buttons 0x{mask:X4}" }, 2 => $"Keyboard HID {b[3]:X2} · modifiers {b[2]:X2}", 3 => $"Media HID {BinaryPrimitives.ReadUInt16BigEndian(b[2..]):X4}", _ => "HID " + Convert.ToHexString(b) };
        if (b[0] == 0) return $"Macro at {b[1]:X2}:{b[3]:X2}";
        return "Unknown " + Convert.ToHexString(b);
    }
}
