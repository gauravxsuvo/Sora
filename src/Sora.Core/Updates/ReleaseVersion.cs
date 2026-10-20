namespace Sora.Core.Updates;

// Stable versions keep the original strict numeric grammar. Beta ordering must
// retain the suffix: beta.10 is newer than beta.2, and stable follows every beta.
public sealed record ReleaseVersion : IComparable<ReleaseVersion>
{
    public Version Number { get; }
    public int? Beta { get; }
    public string Channel => Beta.HasValue ? "beta" : "stable";
    private ReleaseVersion(Version number, int? beta) { Number = number; Beta = beta; }
    public static ReleaseVersion Parse(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new InvalidDataException("Missing Sora release version.");
        var parts = value.Split("-beta.", StringSplitOptions.None);
        var number = ReleaseFeed.ParseVersion(parts[0]);
        if (parts.Length == 1) return new(number, null);
        if (parts.Length != 2 || parts[1].Length is < 1 or > 5 || parts[1][0] == '0' ||
            parts[1].Any(c => c is < '0' or > '9') || !int.TryParse(parts[1], out var beta) || beta > 65535)
            throw new InvalidDataException("Expected a major.minor.patch-beta.N Sora version.");
        return new(number, beta);
    }
    public int CompareTo(ReleaseVersion? other)
    {
        if (other == null) return 1;
        var numeric = Number.CompareTo(other.Number);
        if (numeric != 0) return numeric;
        if (Beta == other.Beta) return 0;
        if (Beta == null) return 1;
        if (other.Beta == null) return -1;
        return Beta.Value.CompareTo(other.Beta.Value);
    }
    public override string ToString() => Number.ToString(3) + (Beta is { } beta ? "-beta." + beta : "");
}
