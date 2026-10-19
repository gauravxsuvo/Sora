using Sora.Core;

internal static class WifiChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(WifiFrequency.Describe(2484000) == ("2.4 GHz", 14) && WifiFrequency.Describe(2412000) == ("2.4 GHz", 1), "Wi-Fi 2.4 GHz channel 14 special frequency");
        check(WifiFrequency.Describe(5935000) == ("6 GHz", 2) && WifiFrequency.Describe(5955000) == ("6 GHz", 1) && WifiFrequency.Describe(7115000) == ("6 GHz", 233), "Wi-Fi 6 GHz special channel 2 and band boundaries");
        check(WifiFrequency.Describe(5180000) == ("5 GHz", 36) && WifiFrequency.Describe(1234567).Channel == null, "Wi-Fi 5 GHz channel and unknown frequency");
        check(WifiScanner.DecodeSsid(new byte[] { 65, 0, 66, 67 }, 3) == "A\\u0000B", "Wi-Fi SSID uses byte length and escapes embedded control bytes");
        check(WifiAlignment.Quality(-50) == "Good" && WifiAlignment.Quality(-60) == "Fair" && WifiAlignment.Quality(-70) == "Weak" && WifiAlignment.Quality(-80) == "Poor", "Wi-Fi recovered quality boundary semantics");
        check(WifiScanner.Explain(0x80342002, "Scan").Contains("powered down") && WifiScanner.Explain(5, "Scan").Contains("Location"), "Wi-Fi explains actual powered-down and access-denied errors");
        // GCC's Wi-Fi Compass never forces a scan; neither may Sora (forced scans preceded two Realtek 8922AE firmware failures).
        var natives = typeof(WifiScanner).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)).Select(m => m.Name).ToArray();
        check(natives.Contains("WlanGetNetworkBssList") && !natives.Contains("WlanScan") && !natives.Contains("WlanRegisterNotification"), "Wi-Fi only reads Windows' results and never sends WlanScan");
        var firstAdapter = Guid.NewGuid(); var secondAdapter = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        const string bssid = "00:11:22:33:44:55";
        var alignment = new WifiAlignment(); alignment.Select(firstAdapter, bssid);
        WifiSnapshot Snapshot(DateTimeOffset at, int signal, bool live, DateTimeOffset? seen = null, Guid? adapter = null)
        {
            var id = adapter ?? firstAdapter;
            return new(at, new[] { new WifiAdapter(id, "Test radio", "Connected", live ? new("Same SSID", bssid, 80, 1000, 1000, null) : null) },
                new[] { new WifiAccessPoint(id, "Same SSID", bssid, signal, 80, 5180000, "5 GHz", 36, "802.11ax", true, seen ?? at) }, Array.Empty<string>(), false);
        }
        check(!alignment.Add(Snapshot(now, -20, true, adapter: secondAdapter)), "Wi-Fi never mixes identical SSIDs or BSSIDs across adapters");
        check(!alignment.Add(Snapshot(now, -45, false, now.AddSeconds(-30))), "Wi-Fi rejects stale cached measurements");
        check(alignment.Add(Snapshot(now, -60, false, now)) && !alignment.Add(Snapshot(now.AddMilliseconds(500), -60, false, now)), "Wi-Fi deduplicates cached beacon timestamps");
        var connectedCache = new WifiAlignment(); connectedCache.Select(firstAdapter, bssid);
        check(connectedCache.Add(Snapshot(now, -61, true, now)) && !connectedCache.Add(Snapshot(now.AddMilliseconds(500), -61, true, now)), "Connected BSS cache is not mistaken for a fresh RSSI measurement on every poll");
        alignment.BeginPosition("Front", now);
        check(!alignment.Add(Snapshot(now, -40, false, now.AddMilliseconds(-500))), "Wi-Fi position capture excludes beacons from before the antenna moved");
        alignment.BeginPosition("Front", now); alignment.Add(Snapshot(now, -70, true)); alignment.Add(Snapshot(now.AddMilliseconds(500), -50, true));
        var front = alignment.CompletePosition();
        alignment.BeginPosition("Left", now); alignment.Add(Snapshot(now, -55, true)); alignment.CompletePosition();
        check(front.Count == 2 && front.AverageDbm == -60 && front.MinimumDbm == -70 && front.MaximumDbm == -50 && alignment.Best?.Position == "Left", "Wi-Fi direction statistics choose strongest mean, not peak");
        var restored = Snapshot(now, -55, true).AccessPoints.Single();
        var competitor = restored with { Bssid = "00:11:22:33:44:66", SignalDbm = -30 };
        var absent = alignment.ResolveTarget(firstAdapter, new[] { competitor }, competitor.Bssid);
        var returned = alignment.ResolveTarget(firstAdapter, new[] { competitor, restored }, competitor.Bssid);
        check(absent == null && returned?.Bssid == bssid && alignment.Results.Count == 2, "Wi-Fi target disappearance and return preserve the original BSSID and comparison results");
        alignment.Select(secondAdapter, bssid);
        check(alignment.Results.Count == 0 && alignment.Samples.Count == 0, "Wi-Fi target change clears incompatible comparisons");
        var csv = WifiAlignment.ExportCsv(Snapshot(now, -55, true));
        check(csv.Contains("\"Same SSID\",\"00:11:22:33:44:55\"") && csv.Contains("Signal dBm") && csv.Contains("Last seen UTC"), "Wi-Fi CSV preserves radio identity and cache timestamp");
        var formula = Snapshot(now, -55, true);
        formula = formula with { AccessPoints = formula.AccessPoints.Select(p => p with { Ssid = "  =1+1" }).ToArray() };
        check(WifiAlignment.ExportCsv(formula).Contains("\"'  =1+1\""), "Wi-Fi CSV neutralizes formulas in nearby access-point names");
    }
}
