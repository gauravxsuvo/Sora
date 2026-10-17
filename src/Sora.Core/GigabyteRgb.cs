using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Sora.Core;

/// <summary>
/// Built-in effects of the Gigabyte RGB Fusion USB controller (GCC's RgbCommon.LMode_8297). Rainbow is GCC's rainbow wave
/// (LMode Wave17, captured from GCC): a whole-board pattern, see <see cref="GigabyteRgb.ApplyZones"/>. Wave (mode 6) left the
/// ARGB headers dark on the Z890 GAMING X WIFI7, so it is sent as Color cycle. Values are saved in lighting.json, so new effects go at the end.
/// </summary>
public enum BoardEffect { Off, Static, Pulse, Flash, DoubleFlash, ColorCycle, Wave, Rainbow }

/// <summary>One lighting zone of the board. Addressable zones are 5 V ARGB.</summary>
public sealed record BoardZone(int Number, string Name, bool Addressable);

/// <summary>One zone's built-in effect. Brightness 0-8 and speed 0-9 follow GCC's tables.</summary>
public sealed record BoardZoneEffect(int Zone, BoardEffect Effect, byte Red, byte Green, byte Blue, int Brightness, int Speed);

/// <summary>Reply to command 0x60 (IT8297_Info in GCC).</summary>
public sealed record BoardInfo(string Product, byte ProductId, byte DeviceNumber, byte StripDetect, uint Firmware, uint ChipId, byte SupportFlags, string Raw)
{
    public string FirmwareText => $"{Firmware >> 24 & 0xFF}.{Firmware >> 16 & 0xFF}.{Firmware >> 8 & 0xFF}.{Firmware & 0xFF}";
}

/// <summary>
/// ITE IT5711 RGB controller (USB 048D:5711), protocol recovered from GCC's RgbMotherboard.dll (MCU_8297) and confirmed
/// by capturing GCC's own HID traffic with Frida. 64-byte feature reports with ID 0xCC. Sora sends only what GCC sends in
/// normal use: 0x60 info, GCC's connect setup 0x34 00 00 (32-LED ARGB setting) and 0x32 00 (streaming off), per-zone effects 0x20-0x27 / 0x90-0x92 and 0x28 apply. 0x47 (keep LED parameters across a restart;
/// GCC sends 00 when it connects and 01 when it exits) is sent only by <see cref="KeepAfterRestart"/>, when the user asks. It never sends 0x5E (save to flash), calibration, pin or firmware commands.
/// </summary>
public sealed class GigabyteRgb : IDisposable
{
    public const ushort VendorId = 0x048D, ProductId = 0x5711;
    private const byte ReportId = 0xCC, ReadInfo = 0x60, EffectAllZones = 0x20, ApplyCommand = 0x28, KeepLedParameters = 0x47, DLedStripControl = 0x32, DLedOutputCount = 0x34;
    private const uint AllZones = 0x7FF;
    private static readonly byte[] BrightnessLevels = [26, 51, 77, 102, 128, 153, 179, 204, 255];
    private static readonly ushort[,] PulseTiming = { { 1600, 1600, 200 }, { 1400, 1400, 200 }, { 1200, 1200, 200 }, { 1000, 1000, 200 }, { 900, 900, 200 }, { 800, 800, 200 }, { 700, 700, 200 }, { 600, 600, 200 }, { 500, 500, 200 }, { 400, 400, 200 } };
    private static readonly ushort[,] FlashTiming = { { 100, 100, 2400 }, { 100, 100, 2200 }, { 100, 100, 2000 }, { 100, 100, 1800 }, { 100, 100, 1600 }, { 100, 100, 1400 }, { 100, 100, 1200 }, { 100, 100, 1000 }, { 100, 100, 800 }, { 100, 100, 600 } };
    private static readonly ushort[,] DoubleFlashTiming = { { 100, 100, 2600 }, { 100, 100, 2400 }, { 100, 100, 2200 }, { 100, 100, 2000 }, { 100, 100, 1800 }, { 100, 100, 1600 }, { 100, 100, 1400 }, { 100, 100, 1200 }, { 100, 100, 1000 }, { 100, 100, 800 } };
    private static readonly ushort[,] CycleTiming = { { 2400, 2200 }, { 1100, 900 }, { 1000, 800 }, { 900, 700 }, { 800, 600 }, { 700, 500 }, { 600, 400 }, { 500, 300 }, { 400, 200 }, { 300, 100 } };
    private static readonly ushort[] WaveTiming = [300, 250, 205, 165, 130, 100, 75, 55, 40, 30];

    private readonly SafeFileHandle handle;
    private readonly int reportLength;
    public DeviceInfo Device { get; }
    public BoardInfo Info { get; }

    /// <summary>The controller's configuration collection: vendor page, 0xCC feature reports. Read-only descriptor scan.</summary>
    public static IReadOnlyList<DeviceInfo> Find() => HidDevice.EnumerateMatching(path => path.Contains("vid_048d&pid_5711", StringComparison.OrdinalIgnoreCase))
        .Where(d => d.VendorId == VendorId && d.ProductId == ProductId && d.UsagePage >= 0xFF00 && d.FeatureReportLength >= 64).ToArray();

    /// <summary>Every 048D:5711 HID collection, for diagnostics.</summary>
    public static IReadOnlyList<DeviceInfo> Descriptors() => HidDevice.EnumerateMatching(path => path.Contains("vid_048d&pid_5711", StringComparison.OrdinalIgnoreCase));

    public GigabyteRgb()
    {
        var devices = Find();
        if (devices.Count != 1) throw new IOException(devices.Count == 0 ? "The Gigabyte RGB controller (USB 048D:5711) was not found." : "More than one Gigabyte RGB controller was found.");
        Device = devices[0];
        handle = HidDevice.Native.CreateFile(Device.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the Gigabyte RGB controller."); }
        try
        {
            var caps = HidDevice.GetCaps(handle);
            if (caps.UsagePage < 0xFF00 || caps.FeatureReportByteLength is < 64 or > 65) throw new IOException("Unexpected Gigabyte RGB HID descriptor.");
            reportLength = caps.FeatureReportByteLength;
            Info = ReadInfoReport();
        }
        catch { handle.Dispose(); throw; }
    }

    /// <summary>Reads GCC also performs on every connect: 0x61 strip calibration, 0x48 Dynamic Lighting switch, 0x3E/0x3F/0x3A/0x3B/0x2F Gen2 strip info.</summary>
    public static readonly byte[] DiagnosticReads = [0x61, 0x48, 0x3E, 0x3F, 0x3A, 0x3B, 0x2F];

    /// <summary>Select-then-read, as GCC's ReadDataFromDevice does. Only the commands in <see cref="DiagnosticReads"/> are accepted.</summary>
    public byte[] Query(byte command)
    {
        if (Array.IndexOf(DiagnosticReads, command) < 0) throw new InvalidOperationException($"0x{command:X2} is not a read command.");
        var request = new byte[reportLength];
        request[0] = ReportId; request[1] = command;
        if (!HidDevice.Native.HidD_SetFeature(handle, request, request.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Query 0x{command:X2} failed.");
        var reply = new byte[reportLength];
        reply[0] = ReportId;
        if (!HidDevice.Native.HidD_GetFeature(handle, reply, reply.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Reply to 0x{command:X2} failed.");
        return reply;
    }

    private BoardInfo ReadInfoReport()
    {
        Send([ReportId, ReadInfo]);
        var reply = new byte[reportLength];
        reply[0] = ReportId;
        if (!HidDevice.Native.HidD_GetFeature(handle, reply, reply.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The Gigabyte RGB controller did not answer.");
        if (reply[0] != ReportId) throw new IOException("Unexpected reply from the Gigabyte RGB controller.");
        var product = Encoding.ASCII.GetString(reply, 12, 28).TrimEnd('\0', ' ');
        return new BoardInfo(product, reply[1], reply[2], reply[3], BitConverter.ToUInt32(reply, 4), BitConverter.ToUInt32(reply, 56), reply[11], Convert.ToHexString(reply));
    }

    /// <summary>
    /// The DataFormat_8297 block GCC sends for one effect, covering all zones. Brightness 0-8 and speed 0 (slowest) to 9 (fastest)
    /// follow GCC's tables; Pulse always uses GCC's fixed peak of 100.
    /// </summary>
    public static byte[] BuildEffect(BoardEffect effect, byte red, byte green, byte blue, int brightness, int speed)
    {
        if (brightness is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(brightness));
        if (speed is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(speed));
        byte mode, maxBrightness = BrightnessLevels[brightness], ctrl0 = 0, ctrl1 = 0, oem0 = 0;
        ushort t0 = 0, t1 = 0, t2 = 0;
        var color = (uint)(red << 16 | green << 8 | blue);
        switch (effect)
        {
            case BoardEffect.Off: mode = 1; color = 0; maxBrightness = 255; break;
            case BoardEffect.Static: mode = 1; break;
            case BoardEffect.Pulse: mode = 2; (t0, t1, t2) = (PulseTiming[speed, 0], PulseTiming[speed, 1], PulseTiming[speed, 2]); maxBrightness = 100; ctrl1 = 1; break;
            case BoardEffect.Flash: mode = 3; (t0, t1, t2) = (FlashTiming[speed, 0], FlashTiming[speed, 1], FlashTiming[speed, 2]); ctrl1 = 1; oem0 = 1; break;
            case BoardEffect.DoubleFlash: mode = 3; (t0, t1, t2) = (DoubleFlashTiming[speed, 0], DoubleFlashTiming[speed, 1], DoubleFlashTiming[speed, 2]); ctrl1 = 1; oem0 = 2; break;
            case BoardEffect.ColorCycle or BoardEffect.Rainbow: mode = 4; (t0, t1) = (CycleTiming[speed, 0], CycleTiming[speed, 1]); ctrl0 = 7; break;
            case BoardEffect.Wave: mode = 6; t0 = WaveTiming[speed]; ctrl0 = 7; ctrl1 = 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(effect));
        }
        var data = new byte[32];
        BitConverter.GetBytes(AllZones).CopyTo(data, 0);
        data[9] = mode; data[10] = maxBrightness; data[11] = 0;
        BitConverter.GetBytes(color).CopyTo(data, 12);
        BitConverter.GetBytes(t0).CopyTo(data, 20);
        BitConverter.GetBytes(t1).CopyTo(data, 22);
        BitConverter.GetBytes(t2).CopyTo(data, 24);
        data[28] = ctrl0; data[29] = ctrl1; data[30] = oem0;
        return data;
    }

    /// <summary>
    /// The zones GCC drives on a board, from its divsmap.xml. The IT5711 ignores the zone mask of header 0x20 beyond zone 0,
    /// so every zone gets its own packet. Z890 GAMING X WIFI7 (MB_Id 31, layout 4) was mapped on this PC; other boards
    /// get GCC's eleven zone slots by number.
    /// </summary>
    public static IReadOnlyList<BoardZone> ZonesFor(string? boardModel) =>
        boardModel?.Trim().Equals("Z890 GAMING X WIFI7", StringComparison.OrdinalIgnoreCase) == true
            ? [new(0, "GAME ON logo", false), new(9, "I/O cover", false), new(2, "SSD heatsink", false), new(5, "ARGB header 1", true), new(6, "ARGB header 2", true), new(7, "ARGB header 3", true), new(4, "RGB header", false)]
            : Enumerable.Range(0, 11).Select(n => new BoardZone(n, $"Zone {n + 1}", n is >= 5 and <= 9)).ToArray();

    /// <summary>Motherboard product name from Windows (Win32_BaseBoard), or null.</summary>
    public static string? BoardModel()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
            foreach (var board in searcher.Get())
                if (board["Product"] is string product && !string.IsNullOrWhiteSpace(product)) return product.Trim();
            return null;
        }
        catch (Exception error) when (error is System.Management.ManagementException or UnauthorizedAccessException or COMException) { return null; }
    }

    /// <summary>
    /// The per-zone effect the controller is sent. Wave, saved by Sora 0.6/0.6.1, is sent as Color cycle, because mode 6 turned
    /// the ARGB headers off on this board. Rainbow is a whole-board pattern and never goes out per zone; if it ever did, it would be a colour cycle.
    /// </summary>
    public static BoardZoneEffect ForZone(BoardZoneEffect effect) =>
        effect.Effect is BoardEffect.Rainbow or BoardEffect.Wave ? effect with { Effect = BoardEffect.ColorCycle } : effect;

    /// <summary>
    /// GCC's rainbow wave (MCU_8297.SetLedEffect, LMode Wave17), byte for byte as captured: header 0x22 (GCC's division 2),
    /// empty zone masks, mode 9, brightness 100, time base 550, CtrlVal0 7. The controller runs it on the whole board:
    /// a rainbow flows along the ARGB headers and the plain RGB lights show white.
    /// </summary>
    public static byte[] RainbowWavePacket()
    {
        var data = new byte[32];
        data[9] = 9; data[10] = 100;
        BitConverter.GetBytes((ushort)550).CopyTo(data, 20);
        data[28] = 7;
        return [ReportId, 0x22, .. data];
    }

    /// <summary>Header byte and packet for one zone: 0x20+n below 8, 0x90+(n-8) above; zone mask 1&lt;&lt;n.</summary>
    public static byte[] BuildZonePacket(BoardZoneEffect zone)
    {
        if (zone.Zone is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(zone));
        var data = BuildEffect(zone.Effect, zone.Red, zone.Green, zone.Blue, zone.Brightness, zone.Speed);
        BitConverter.GetBytes(1u << zone.Zone).CopyTo(data, 0);
        return [ReportId, (byte)(zone.Zone < 8 ? 0x20 + zone.Zone : 0x90 + zone.Zone - 8), .. data];
    }

    /// <summary>
    /// GCC's apply sequence as captured with Frida (frida-gcc-*.jsonl, frida-ioctl-*.jsonl), with its timing: GCC's connect
    /// setup (0x34 00 00: ARGB headers at the 32-LED setting; 0x32 00: per-LED streaming off), clear all eleven zones and
    /// apply, wait (ClearIT8297Parameter), then either one packet per zone and apply, or, if any zone is Rainbow, GCC's
    /// rainbow wave for the whole board: all zones static black, apply, the Wave17 packet on division 2, apply that division.
    /// Without the waits the controller can drop packets. Each step is (packet, milliseconds to wait after it).
    /// </summary>
    public static IReadOnlyList<(byte[] Packet, int Wait)> Plan(IReadOnlyList<BoardZoneEffect> zones)
    {
        byte[] apply = [ReportId, ApplyCommand, 0xFF, 0x07];
        var plan = new List<(byte[], int)> { ([ReportId, DLedOutputCount, 0x00, 0x00], 0), ([ReportId, DLedStripControl, 0x00], 0), (apply, 0) };
        for (var zone = 0; zone <= 10; zone++) plan.Add(([ReportId, (byte)(zone < 8 ? 0x20 + zone : 0x90 + zone - 8)], 0));
        if (zones.Any(z => z.Effect == BoardEffect.Rainbow))
        {
            // Gaps as captured (frida-gcc-red-rainbow.jsonl): 140 ms after the clear, 60 ms before the wave, 110 ms before its apply.
            plan.Add((apply, 140));
            plan.Add(([ReportId, EffectAllZones, .. BuildEffect(BoardEffect.Static, 0, 0, 0, 8, 0)], 0));
            plan.Add((apply, 60));
            plan.Add((RainbowWavePacket(), 110));
            plan.Add(([ReportId, ApplyCommand, 0x04, 0x00], 25));
            return plan;
        }
        plan.Add((apply, 100));
        foreach (var zone in zones) plan.Add((BuildZonePacket(ForZone(zone)), 0));
        plan.Add((apply, 25));
        return plan;
    }

    /// <summary>
    /// One sync frame: Static colour on the given zones and apply, without the clear and wait, for zones that already hold a
    /// Static packet from <see cref="ApplyZones"/>. The colour carries the brightness; the controller runs at full level.
    /// </summary>
    public static IReadOnlyList<byte[]> FramePackets(IEnumerable<int> zones, byte red, byte green, byte blue) =>
        [.. zones.Select(zone => BuildZonePacket(new BoardZoneEffect(zone, BoardEffect.Static, red, green, blue, 8, 0))), [ReportId, ApplyCommand, 0xFF, 0x07]];

    public void SendFrame(IEnumerable<int> zones, byte red, byte green, byte blue)
    {
        foreach (var packet in FramePackets(zones, red, green, blue)) Send(packet);
    }

    /// <summary>Sends <see cref="Plan"/>. The board shows this only until the next restart, unless <see cref="KeepAfterRestart"/> follows.</summary>
    public void ApplyZones(IReadOnlyList<BoardZoneEffect> zones)
    {
        foreach (var (packet, wait) in Plan(zones))
        {
            Send(packet);
            if (wait > 0) Thread.Sleep(wait);
        }
    }

    /// <summary>
    /// GCC's SaveLedParameter, byte for byte: CC 47 01 (sent on GCC's exit) makes the board keep the lighting just applied and
    /// show it after a restart; CC 47 00 (sent when GCC connects) stops keeping it, so the board starts with its own built-in
    /// lighting. Only on the user's explicit request; never part of Apply.
    /// </summary>
    public void KeepAfterRestart(bool keep) => Send([ReportId, KeepLedParameters, keep ? (byte)1 : (byte)0], keep: true);

    /// <summary>
    /// Raw command, limited to zone effects 0x20-0x27 and 0x90-0x92 and apply 0x28. Never 0x47 or 0x5E.
    /// </summary>
    public void SendRaw(byte[] content) => Send(content);

    private void Send(byte[] content, bool keep = false)
    {
        var allowed = content[1] is ReadInfo or ApplyCommand or >= 0x20 and <= 0x27 or >= 0x90 and <= 0x92
            || content is [ReportId, DLedOutputCount, 0x00, 0x00] or [ReportId, DLedStripControl, 0x00]
            || keep && content is [ReportId, KeepLedParameters, 0x00 or 0x01];
        if (content[0] != ReportId || !allowed) throw new InvalidOperationException($"Refused RGB controller command 0x{content[1]:X2}.");
        var report = new byte[reportLength];
        content.CopyTo(report, 0);
        if (!HidDevice.Native.HidD_SetFeature(handle, report, report.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Writing to the Gigabyte RGB controller failed.");
    }

    public void Dispose() => handle.Dispose();
}
