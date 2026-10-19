namespace Sora.Core;

public enum FanCurveMode { Curve, Step, Fixed }
public enum FanDeviceKind { Unknown, Fan, Pump, FixedSpeedPump }
public sealed record FanPoint(int TemperatureC, double DutyPercent);
public sealed record FanProfile
{
    public int Header { get; init; }
    public FanDeviceKind DeviceKind { get; init; }
    public FanCurveMode Mode { get; init; }
    public int TemperatureSource { get; init; } = 2;
    public int HysteresisC { get; init; } = 3;
    public int StopBelowC { get; init; }
    public double FixedDutyPercent { get; init; } = 100;
    public int FullSpeedC { get; init; } = 85;
    public FanStopConfig? FirmwareStop { get; init; }
    public FanPoint[] Points { get; init; } = [new(25, 35), new(45, 45), new(65, 70), new(85, 100)];

    public void Validate()
    {
        FirmwareStop?.Validate();
        if (Mode == FanCurveMode.Fixed && FirmwareStop?.Enabled == true)
            throw new ArgumentException("Use a temperature curve or stair step for firmware Zero-RPM.");
        if (Header is < 0 or > 5 || !Enum.IsDefined(Mode) || !Enum.IsDefined(DeviceKind) || TemperatureSource is < 0 or > 5)
            throw new ArgumentException("Unknown fan header, curve mode or temperature source.");
        if (HysteresisC is < 0 or > 15 || FullSpeedC is < 50 or > 95 || StopBelowC is < 0 or > 60)
            throw new ArgumentException("Use hysteresis 0–15°C, full speed 50–95°C and stop temperature 0–60°C.");
        if (!double.IsFinite(FixedDutyPercent) || FixedDutyPercent is < 0 or > 100)
            throw new ArgumentException("Fan duty must be between 0 and 100%.");
        if (Mode == FanCurveMode.Fixed)
        {
            if (StopBelowC != 0) throw new ArgumentException("Use a temperature curve for fan stop.");
            return;
        }
        if (Points == null || Points.Length is < 2 or > 11 || Points.Any(p => p == null))
            throw new ArgumentException("A curve needs 2–11 temperature points.");
        for (int i = 0; i < Points.Length; i++)
        {
            var p = Points[i];
            if (p.TemperatureC is < 1 or > 95 || !double.IsFinite(p.DutyPercent) || p.DutyPercent is < 0 or > 100)
                throw new ArgumentException("Use temperatures 1–95°C and duties 0–100%.");
            if (i > 0 && (p.TemperatureC <= Points[i - 1].TemperatureC || p.DutyPercent < Points[i - 1].DutyPercent))
                throw new ArgumentException("Temperatures must increase and fan duty cannot decrease.");
        }
        if (Points[^1].DutyPercent != 100 || Points[^1].TemperatureC < 50 || Points[^1].TemperatureC > 95)
            throw new ArgumentException("End the curve at 100%, between 50 and 95°C.");
        if (StopBelowC >= Points[0].TemperatureC)
            throw new ArgumentException("The stop temperature must be below the first curve point; 0 disables fan stop.");
    }

    public static FanProfile Preset(int header, string name) => new()
    {
        Header = header,
        Mode = name == "Full speed" ? FanCurveMode.Fixed : FanCurveMode.Curve,
        Points = name switch
        {
            "Quiet" => [new(25, 25), new(45, 30), new(65, 55), new(85, 100)],
            "Performance" => [new(25, 45), new(45, 60), new(65, 85), new(80, 100)],
            _ => [new(25, 35), new(45, 45), new(65, 70), new(85, 100)]
        }
    };
}

public readonly record struct FanRegister(int Bank, int Address);
public sealed record FanRegisterWrite(FanRegister Register, byte Value, byte Mask = 255);
public sealed record FanVector(int Bank, int Temperature, int SlopeInteger, int Fraction, int Shift, int Pwm, int Enable, byte EnableMask);

/// <summary>IT8696E layout recovered from GCC's IT8689XXBase and SmartGuardianFanConfigConverter.</summary>
public static class FanProtocol
{
    public static readonly string[] HeaderNames = ["CPU", "System 1", "System 2", "System 3", "CPU OPT", "System 4 / Pump"];
    public static readonly string[] TemperatureNames = ["System 1", "PCH", "CPU", "PCIEX16", "VRM MOS", "System 2"];
    private static readonly int[] Main = [0x60, 0x68, 0x70, 0x78, 0xA0, 0xA8];
    private static readonly int[] Control = [0x15, 0x16, 0x17, 0x7F, 0xA7, 0xAF];
    private static readonly int[] VectorPwm = [0x70, 0x76, 0x80, 0x86, 0x90, 0x96];
    public static int MainAddress(int header) => Main[CheckHeader(header)];
    public static FanRegister ControlRegister(int header) => new(0, Control[CheckHeader(header)]);
    private static int CheckHeader(int header) => header is >= 0 and < 6 ? header : throw new ArgumentOutOfRangeException(nameof(header));
    public static FanVector[] Vectors(int header)
    {
        CheckHeader(header);
        int b = 0x10 + 16 * header;
        var result = new List<FanVector>();
        for (int i = 0; i < 3; i++) result.Add(new(2, b + 4 * i, b + 4 * i + 1, b + 4 * i + 2, 0, b + 4 * i + 3, b + 4 * i + 2, 0x80));
        for (int i = 0; i < 6; i++) result.Add(new(3, b + 2 * i, b + 2 * i + 1, b + 13 + i / 2, (i % 2) * 4, VectorPwm[header] + i, b + 12, (byte)(1 << i)));
        return result.ToArray();
    }
    public static FanRegister[] Registers(int header)
    {
        var r = new HashSet<FanRegister> { ControlRegister(header), new(2, 0x1C + 16 * header) };
        for (int i = 0; i < 7; i++) r.Add(new(0, MainAddress(header) + i));
        foreach (var v in Vectors(header)) foreach (int a in new[] { v.Temperature, v.SlopeInteger, v.Fraction, v.Pwm, v.Enable }) r.Add(new(v.Bank, a));
        return r.OrderBy(x => x.Bank).ThenBy(x => x.Address).ToArray();
    }
    public static bool CanWrite(int header, FanRegister r) => Registers(header).Contains(r);
    public static byte Duty(double percent) => checked((byte)Math.Round(percent * 255 / 100, MidpointRounding.AwayFromZero));
    private static int Slope(FanPoint a, FanPoint b)
    {
        int slope = (int)Math.Floor((Duty(b.DutyPercent) - Duty(a.DutyPercent)) * 8d / (b.TemperatureC - a.TemperatureC));
        if (slope is < 0 or > 1023) throw new ArgumentException("Curve segment is too steep for this controller.");
        return slope;
    }
    public static FanRegisterWrite[] Compile(FanProfile profile)
    {
        profile.Validate();
        int h = profile.Header, m = MainAddress(h);
        var writes = new List<FanRegisterWrite>();
        void W(int bank, int address, int value, int mask = 255) => writes.Add(new(new(bank, address), (byte)value, (byte)mask));
        var points = profile.Mode == FanCurveMode.Fixed ? new[] { new FanPoint(20, profile.FixedDutyPercent), new FanPoint(profile.FullSpeedC, 100) } : profile.Points;
        int slope = profile.Mode == FanCurveMode.Curve ? Slope(points[0], points[1]) : 0;
        var vectors = Vectors(h);
        // Disable all nine old segments before populating a new curve, including unused trailing segments.
        foreach (var v in vectors) W(v.Bank, v.Enable, 0, v.EnableMask);
        int count = profile.Mode == FanCurveMode.Fixed ? 0 : points.Length - 2;
        for (int i = 0; i < count; i++)
        {
            var v = vectors[i];
            int s = profile.Mode == FanCurveMode.Curve ? Slope(points[i + 1], points[i + 2]) : 0;
            W(v.Bank, v.Temperature, points[i + 1].TemperatureC);
            W(v.Bank, v.SlopeInteger, s >> 3);
            W(v.Bank, v.Fraction, (s & 7) << v.Shift, 15 << v.Shift);
            W(v.Bank, v.Pwm, Duty(points[i + 1].DutyPercent));
        }
        W(2, 0x1C + h * 16, (slope & 7) << 4, 0x70);
        W(0, m, profile.StopBelowC);
        W(0, m + 1, points[0].TemperatureC);
        W(0, m + 2, points[^1].TemperatureC);
        W(0, m + 3, Duty(points[0].DutyPercent));
        W(0, m + 4, slope >> 3);
        W(0, m + 5, profile.HysteresisC);
        W(0, m + 6, 0);
        for (int i = 0; i < count; i++) W(vectors[i].Bank, vectors[i].Enable, vectors[i].EnableMask, vectors[i].EnableMask);
        // Hardware automatic mode and the hardware thermistor source. Preserve electrical PWM/DC selection.
        W(0, Control[h], 0x80 | (profile.TemperatureSource << 3), 0xB8);
        return writes.ToArray();
    }
    public static FanProfile Decode(int header, IReadOnlyDictionary<FanRegister, byte> registers)
    {
        byte R(int b, int a) => registers[new(b, a)];
        int m = MainAddress(header);
        double duty = R(0, m + 3) * 100d / 255;
        double slope = (R(0, m + 4) & 0x7F) + ((R(2, 0x1C + header * 16) >> 4) & 7) / 8d;
        var points = new List<FanPoint> { new(R(0, m + 1), duty) };
        bool any = false, step = slope == 0;
        foreach (var v in Vectors(header))
        {
            if ((R(v.Bank, v.Enable) & v.EnableMask) == 0) break;
            any = true;
            int t = R(v.Bank, v.Temperature);
            double s = R(v.Bank, v.SlopeInteger) + ((R(v.Bank, v.Fraction) >> v.Shift) & 7) / 8d;
            if ((R(v.Bank, v.Fraction) & (8 << v.Shift)) != 0) s = -s;
            step &= s == 0;
            double y = Math.Clamp(points[^1].DutyPercent + slope * (t - points[^1].TemperatureC) * 100 / 255, 0, 100);
            points.Add(new(t, step ? R(v.Bank, v.Pwm) * 100d / 255 : y));
            slope = s;
        }
        points.Add(new(R(0, m + 2), 100));
        return new() { Header = header, Mode = !any && slope == 0 ? FanCurveMode.Fixed : step ? FanCurveMode.Step : FanCurveMode.Curve,
            TemperatureSource = (R(0, Control[header]) >> 3) & 7, StopBelowC = R(0, m), HysteresisC = R(0, m + 5), FixedDutyPercent = duty,
            FullSpeedC = R(0, m + 2), Points = points.ToArray() };
    }
}
