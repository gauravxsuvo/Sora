using System.Globalization;
using System.Text;

namespace Sora.Core;

public sealed record WifiSignalSample(DateTimeOffset At, int SignalDbm);
public sealed record WifiPositionResult(string Position, int Count, double AverageDbm, int MinimumDbm, int MaximumDbm);

/// <summary>Antenna comparisons always use one adapter and BSSID. More negative dBm means weaker reception.</summary>
public sealed class WifiAlignment
{
    public static IReadOnlyList<string> Positions { get; } = new[] { "Front", "Left", "Back", "Right" };
    private readonly List<WifiSignalSample> samples = new();
    private readonly List<WifiPositionResult> results = new();
    private DateTimeOffset? lastBssSeen;
    public IReadOnlyList<WifiSignalSample> Samples => samples;
    public IReadOnlyList<WifiPositionResult> Results => results;
    public Guid AdapterId { get; private set; }
    public string Bssid { get; private set; } = "";
    public string? MeasuringPosition { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public WifiPositionResult? Best => results.OrderByDescending(r => r.AverageDbm).FirstOrDefault();

    public void Select(Guid adapterId, string bssid)
    {
        if (AdapterId == adapterId && Bssid == bssid) return;
        AdapterId = adapterId; Bssid = bssid; Reset();
    }
    public void Reset() { samples.Clear(); results.Clear(); lastBssSeen = null; MeasuringPosition = null; StartedAt = null; }
    public void BeginPosition(string position, DateTimeOffset now)
    {
        if (!Positions.Contains(position)) throw new ArgumentException("Choose Front, Left, Back or Right.", nameof(position));
        samples.Clear(); lastBssSeen = null; MeasuringPosition = position; StartedAt = now;
    }
    public void StopPosition() { MeasuringPosition = null; StartedAt = null; }

    public WifiAccessPoint? ResolveTarget(Guid adapterId, IEnumerable<WifiAccessPoint> accessPoints, string? connectedBssid)
    {
        var points = accessPoints.Where(p => p.AdapterId == adapterId).ToArray();
        // An absent pinned AP stays absent. A later refresh must recover its identity instead of changing comparisons.
        if (AdapterId == adapterId && Bssid.Length > 0) return points.FirstOrDefault(p => p.Bssid == Bssid);
        return points.FirstOrDefault(p => p.Bssid == connectedBssid) ?? points.FirstOrDefault();
    }

    public bool Add(WifiSnapshot snapshot)
    {
        // Windows' BSS list is cached, including for the connected AP. Do not count the same
        // host timestamp as a new sample just because the UI polled the list again.
        var point = snapshot.AccessPoints.FirstOrDefault(a => a.AdapterId == AdapterId && a.Bssid == Bssid);
        if (point?.LastSeen is not { } seen || snapshot.CapturedAt - seen > TimeSpan.FromSeconds(5) || seen > snapshot.CapturedAt + TimeSpan.FromSeconds(1) || lastBssSeen == seen || StartedAt is { } started && seen < started) return false;
        lastBssSeen = seen;
        var dbm = point.SignalDbm;
        if (dbm is < -127 or > 0) return false;
        samples.Add(new(snapshot.CapturedAt, dbm));
        if (samples.Count > 600) samples.RemoveAt(0); // Five minutes at the recovered 500 ms interval.
        return true;
    }

    public WifiPositionResult CompletePosition()
    {
        if (MeasuringPosition == null || samples.Count == 0) throw new InvalidOperationException("No fresh signal measurements were recorded for this position.");
        var result = new WifiPositionResult(MeasuringPosition, samples.Count, samples.Average(x => x.SignalDbm), samples.Min(x => x.SignalDbm), samples.Max(x => x.SignalDbm));
        results.RemoveAll(r => r.Position == MeasuringPosition); results.Add(result); StopPosition(); return result;
    }
    public static string Quality(int dbm) => dbm > -50 ? "Excellent" : dbm > -60 ? "Good" : dbm > -70 ? "Fair" : dbm > -80 ? "Weak" : "Poor";
    public static int MeterPercent(int dbm) => (int)((Math.Clamp(dbm, -90, -20) + 90) / 70d * 100);

    public static string ExportCsv(WifiSnapshot snapshot)
    {
        static string Q(string value)
        {
            // Nearby SSIDs and adapter names are untrusted spreadsheet cells, including leading whitespace.
            var first = value.AsSpan().TrimStart();
            if (value.Length > 0 && (value[0] is '\t' or '\r' or '\n' || first.Length > 0 && first[0] is '=' or '+' or '-' or '@')) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var text = new StringBuilder("Captured UTC,Adapter,SSID,BSSID,Signal dBm,Signal %,Frequency kHz,Band,Channel,Radio,Security,Last seen UTC\r\n");
        foreach (var p in snapshot.AccessPoints)
        {
            var adapter = snapshot.Adapters.FirstOrDefault(a => a.Id == p.AdapterId)?.Description ?? p.AdapterId.ToString();
            text.AppendLine(string.Join(",", Q(snapshot.CapturedAt.ToString("O", CultureInfo.InvariantCulture)), Q(adapter), Q(p.Ssid), Q(p.Bssid), p.SignalDbm, p.SignalPercent, p.FrequencyKhz, Q(p.Band), p.Channel?.ToString(CultureInfo.InvariantCulture) ?? "", Q(p.Radio), Q(p.Security), Q(p.LastSeen?.ToString("O", CultureInfo.InvariantCulture) ?? "")));
        }
        return text.ToString();
    }
}
