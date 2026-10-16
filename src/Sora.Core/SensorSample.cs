namespace Sora.Core;

public sealed record SensorSample(double TemperatureC, double PowerW, double UsagePercent, double ClockMhz, DateTimeOffset ReceivedAt)
{
    public bool IsFresh(DateTimeOffset now) => now - ReceivedAt < TimeSpan.FromSeconds(5) && now >= ReceivedAt;
    public void Validate()
    {
        if (!double.IsFinite(TemperatureC) || TemperatureC is < -100 or > 250 ||
            !double.IsFinite(PowerW) || PowerW is < 0 or > 65535 ||
            !double.IsFinite(UsagePercent) || UsagePercent is < 0 or > 100 || !double.IsFinite(ClockMhz))
            throw new InvalidDataException("The CPU sensors returned invalid readings.");
    }
}
