using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Sora.Core;

public sealed record WifiAccessPoint(Guid AdapterId, string Ssid, string Bssid, int SignalDbm, uint SignalPercent,
    uint FrequencyKhz, string Band, int? Channel, string Radio, bool Secure, DateTimeOffset? LastSeen)
{
    public string Name => string.IsNullOrEmpty(Ssid) ? "Hidden network" : Ssid;
    public string ChannelLabel => Channel?.ToString() ?? "—";
    public string Security => Secure ? "Secured" : "Open";
}

public sealed record WifiConnection(string Ssid, string Bssid, uint SignalPercent, uint ReceiveKbps, uint TransmitKbps, int? SignalDbm);
public sealed record WifiAdapter(Guid Id, string Description, string State, WifiConnection? Connection, bool RadioOff = false);
public sealed record WifiSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<WifiAdapter> Adapters,
    IReadOnlyList<WifiAccessPoint> AccessPoints, IReadOnlyList<string> Notices, bool ScanCompleted);

/// <summary>Read-only WLAN client. Each request owns its native handle and native buffers.</summary>
/// <remarks>
/// Reads only what Windows already has, exactly the calls GIGABYTE's Wi-Fi Compass makes (MB.CPNT.WiFi.dll references only
/// ManagedNativeWifi's EnumerateConnectedNetworkSsids and EnumerateBssNetworks): the adapter list, the current connection and
/// the BSS list from Windows' own background scans. Sora never sends WlanScan and never queries the driver for radio state or RSSI.
/// Up to 0.8.6 Sora forced scans (every 3 s while the radio was off, in 0.8.2-0.8.5), and the user's Realtek 8922AE twice failed
/// its firmware reset right after (rtwlane711 "sw ser l2 reset", Device Manager code 10).
/// </remarks>
public static class WifiScanner
{
    public static Task<WifiSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private static WifiSnapshot Read(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Wi-Fi requires Windows.");
        Check(Native.WlanOpenHandle(2, IntPtr.Zero, out _, out var handle), "Open Wi-Fi service");
        try
        {
            var adapters = new List<WifiAdapter>();
            var points = new List<WifiAccessPoint>();
            var notices = new List<string>();
            Check(Native.WlanEnumInterfaces(handle, IntPtr.Zero, out var interfaces), "List Wi-Fi adapters");
            var infos = new List<Native.InterfaceInfo>();
            try
            {
                var count = Marshal.ReadInt32(interfaces);
                if (count is < 0 or > 1024) throw new IOException("Windows returned an invalid Wi-Fi adapter list.");
                var size = Marshal.SizeOf<Native.InterfaceInfo>();
                for (var index = 0; index < count; index++)
                    infos.Add(Marshal.PtrToStructure<Native.InterfaceInfo>(interfaces + 8 + index * size));
            }
            finally { Native.WlanFreeMemory(interfaces); }

            foreach (var info in infos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = info.Id;
                WifiConnection? connection = null;
                if (info.State == 1)
                {
                    var error = Native.WlanQueryInterface(handle, ref id, 7, IntPtr.Zero, out var size, out var value, out _);
                    if (error == 0)
                    {
                        try
                        {
                            if (size < Marshal.SizeOf<Native.ConnectionAttributes>()) throw new IOException("Windows returned truncated Wi-Fi connection details.");
                            var attributes = Marshal.PtrToStructure<Native.ConnectionAttributes>(value).Association;
                            // Signal comes from the connected BSS's entry in Windows' list below, as GCC reads it.
                            connection = new(DecodeSsid(attributes.Ssid.Bytes, attributes.Ssid.Length), FormatBssid(attributes.Bssid), attributes.Quality, attributes.RxRate, attributes.TxRate, null);
                        }
                        finally { Native.WlanFreeMemory(value); }
                    }
                    else notices.Add(info.Description + ": " + Explain(error, "Read connection"));
                }
                adapters.Add(new(id, info.Description, StateLabel(info.State), connection));
                // Windows answers "radio off" here when it is, so no separate radio query is needed.
                var bssError = Native.WlanGetNetworkBssList(handle, ref id, IntPtr.Zero, 3, false, IntPtr.Zero, out var list);
                if (bssError != 0) { adapters[^1] = adapters[^1] with { RadioOff = IsRadioOff(bssError) }; notices.Add(info.Description + ": " + Explain(bssError, "Read nearby networks")); continue; }
                try
                {
                    var total = Marshal.ReadInt32(list);
                    var count = Marshal.ReadInt32(list, 4);
                    var stride = Marshal.SizeOf<Native.BssEntry>();
                    if (total < 8 || count < 0 || count > (total - 8) / stride) throw new IOException("Windows returned an invalid Wi-Fi network list.");
                    for (var index = 0; index < count; index++)
                    {
                        var bss = Marshal.PtrToStructure<Native.BssEntry>(list + 8 + index * stride);
                        DateTimeOffset? seen = null;
                        if (bss.HostTimestamp > 0 && bss.HostTimestamp <= long.MaxValue)
                            try { seen = DateTimeOffset.FromFileTime((long)bss.HostTimestamp); } catch (ArgumentOutOfRangeException) { }
                        var frequency = WifiFrequency.Describe(bss.Frequency);
                        points.Add(new(id, DecodeSsid(bss.Ssid.Bytes, bss.Ssid.Length), FormatBssid(bss.Bssid), bss.Rssi, bss.Quality,
                            bss.Frequency, frequency.Band, frequency.Channel, RadioLabel(bss.PhyType), (bss.Capabilities & 0x10) != 0, seen));
                    }
                }
                finally { Native.WlanFreeMemory(list); }
            }
            return new(DateTimeOffset.UtcNow, adapters, points.OrderByDescending(p => p.SignalDbm).ToArray(), notices, false);
        }
        finally { Native.WlanCloseHandle(handle, IntPtr.Zero); }
    }

    public static string DecodeSsid(byte[] bytes, uint length)
    {
        var count = checked((int)Math.Min(length, Math.Min(32u, (uint)bytes.Length)));
        // An SSID is arbitrary bytes, not a nul-terminated string. Escape control characters for display/CSV.
        return string.Concat(Encoding.UTF8.GetString(bytes, 0, count).Select(c => char.IsControl(c) ? "\\u" + ((int)c).ToString("x4") : c.ToString()));
    }
    public static string FormatBssid(byte[] bytes) => string.Join(":", bytes.Select(b => b.ToString("X2")));
    private static bool IsRadioOff(uint error) => error is 5023 or 0x80342002;
    public static string Explain(uint error, string action) => error switch
    {
        5 => action + " was denied. Windows may require Location access for Wi-Fi details; check Settings → Privacy & security → Location and allow desktop apps.",
        1062 => "The Windows WLAN AutoConfig service is stopped.",
        1168 => "The Wi-Fi adapter is unavailable or was removed.",
        5023 or 0x80342002 => "The Wi-Fi radio is off or the adapter is powered down. Turn Wi-Fi on in Windows.",
        _ => action + ": " + new Win32Exception(unchecked((int)error)).Message + " (" + error + ")."
    };
    private static void Check(uint error, string action) { if (error != 0) throw new Win32Exception(unchecked((int)error), Explain(error, action)); }
    private static string StateLabel(uint state) => state switch { 1 => "Connected", 2 => "Ad hoc network", 3 => "Disconnecting", 4 => "Disconnected", 5 => "Associating", 6 => "Discovering", 7 => "Authenticating", _ => "Not ready" };
    private static string RadioLabel(uint phy) => phy switch { 1 => "FHSS", 2 => "DSSS", 3 => "IR", 4 => "802.11a", 5 => "802.11b", 6 => "802.11g", 7 => "802.11n", 8 => "802.11ac", 9 => "802.11ad", 10 => "802.11ax", 11 => "802.11be", _ => "PHY " + phy };

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct InterfaceInfo { public Guid Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description; public uint State; }
        [StructLayout(LayoutKind.Sequential)] internal struct Ssid { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes; }
        [StructLayout(LayoutKind.Sequential)] internal struct Association { public Ssid Ssid; public uint BssType; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid; public uint Phy, PhyIndex, Quality, RxRate, TxRate; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ConnectionAttributes { public uint State, Mode; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile; public Association Association; public uint SecurityEnabled, OneXEnabled, Authentication, Cipher; }
        [StructLayout(LayoutKind.Sequential)] internal struct BssEntry
        {
            public Ssid Ssid; public uint PhyId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
            public uint BssType, PhyType; public int Rssi; public uint Quality; public byte InRegDomain;
            public ushort BeaconPeriod; public ulong Timestamp, HostTimestamp; public ushort Capabilities; public uint Frequency, RateCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)] public ushort[] Rates;
            public uint IeOffset, IeSize;
        }
        [DllImport("wlanapi.dll")] internal static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);
        [DllImport("wlanapi.dll")] internal static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] internal static extern void WlanFreeMemory(IntPtr memory);
        [DllImport("wlanapi.dll")] internal static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] internal static extern uint WlanGetNetworkBssList(IntPtr handle, ref Guid id, IntPtr ssid, uint bssType, [MarshalAs(UnmanagedType.Bool)] bool security, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] internal static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, uint opcode, IntPtr reserved, out uint size, out IntPtr data, out uint valueType);
    }
}

public static class WifiFrequency
{
    public static (string Band, int? Channel) Describe(uint khz)
    {
        var mhz = khz / 1000d;
        if (mhz == 2484) return ("2.4 GHz", 14);
        if (mhz is >= 2412 and <= 2472 && (mhz - 2407) % 5 == 0) return ("2.4 GHz", (int)(mhz - 2407) / 5);
        if (mhz == 5935) return ("6 GHz", 2);
        if (mhz is >= 5955 and <= 7115 && (mhz - 5950) % 5 == 0) return ("6 GHz", (int)(mhz - 5950) / 5);
        if (mhz is >= 4910 and <= 4980 && (mhz - 4000) % 5 == 0) return ("4.9 GHz", (int)(mhz - 4000) / 5);
        if (mhz is >= 5000 and <= 5895 && (mhz - 5000) % 5 == 0) return ("5 GHz", (int)(mhz - 5000) / 5);
        if (mhz is >= 58320 and <= 70200 && (mhz - 56160) % 2160 == 0) return ("60 GHz", (int)(mhz - 56160) / 2160);
        return ("Unknown", null);
    }
}
