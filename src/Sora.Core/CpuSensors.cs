using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

namespace Sora.Core;

public sealed record CpuSensorValue(string Name, SensorType Type, double? Value);

public enum SensorState { Starting, Live, Unavailable }
public enum SensorProblem { None, Permission, DriverMissing, Unsupported, ReadFailed }

/// <summary>Sensor availability, kept separate from the readings themselves. Message is short; Detail is the underlying error.</summary>
public sealed record SensorStatus(SensorState State, SensorProblem Problem, string Message, string? Detail = null)
{
    public static SensorStatus Starting { get; } = new(SensorState.Starting, SensorProblem.None, "Starting CPU sensors");
    public static SensorStatus Live { get; } = new(SensorState.Live, SensorProblem.None, "Live");
    public static SensorStatus From(Exception error) => error switch
    {
        SensorUnavailableException known => new(SensorState.Unavailable, known.Problem, known.Message),
        UnauthorizedAccessException => new(SensorState.Unavailable, SensorProblem.Permission, "CPU registers need administrator access.", error.Message),
        _ => new(SensorState.Unavailable, SensorProblem.ReadFailed, "The CPU sensors returned no usable reading.", error.Message),
    };
}

public sealed class SensorUnavailableException(SensorProblem problem, string message) : IOException(message)
{
    public SensorProblem Problem { get; } = problem;
}

public static class CpuSensorSelector
{
    public static SensorSample Select(IReadOnlyList<CpuSensorValue> sensors, DateTimeOffset receivedAt)
    {
        double Required(SensorType type, params string[] names)
        {
            foreach (var name in names)
            {
                var matches = sensors.Where(sensor => sensor.Type == type && sensor.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 1) throw new InvalidDataException($"Ambiguous CPU {type} sensor: {name}.");
                if (matches.Length == 1)
                {
                    if (matches[0].Value is not { } value || !double.IsFinite(value)) throw new InvalidDataException($"CPU {name} {type} reading is unavailable. Check PawnIO access.");
                    return value;
                }
            }
            throw new InvalidDataException($"CPU {type} sensor is unavailable: {string.Join(" / ", names)}.");
        }
        var clock = sensors.Where(sensor => sensor.Type == SensorType.Clock && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && sensor.Value is >= 0)
            .Select(sensor => sensor.Value!.Value).Where(double.IsFinite).DefaultIfEmpty(0).Max();
        var sample = new SensorSample(
            Required(SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)"),
            Required(SensorType.Power, "CPU Package", "Package"),
            Required(SensorType.Load, "CPU Total"), clock, receivedAt);
        sample.Validate();
        return sample;
    }
}

public interface ICpuSensorReader : IDisposable
{
    string CpuName { get; }
    IReadOnlyList<CpuSensorValue> Read();
}

public sealed class LibreCpuSensorReader : ICpuSensorReader
{
    private readonly Computer computer;
    private readonly IHardware cpu;
    public string CpuName => cpu.Name;

    public LibreCpuSensorReader()
    {
        if (!OperatingSystem.IsWindows()) throw new SensorUnavailableException(SensorProblem.Unsupported, "Sora hardware sensors require Windows.");
        if (!PawnIo.IsInstalled) throw new SensorUnavailableException(SensorProblem.DriverMissing, "The PawnIO driver is not installed.");
        computer = new Computer { IsCpuEnabled = true };
        try
        {
            computer.Open();
            var processors = computer.Hardware.Where(hardware => hardware.HardwareType == HardwareType.Cpu).ToArray();
            if (processors.Length != 1) throw new SensorUnavailableException(SensorProblem.Unsupported, "Sora requires one supported CPU package.");
            cpu = processors[0];
            // The upstream MSR wrapper can return zero-filled data when device
            // access fails. Reject that state before accepting any readings.
            if (cpu.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                var msr = new IntelMsr();
                try
                {
                    if (!msr.ReadMsr(0x1a2, out uint target, out _) || ((target >> 16) & 255) == 0 ||
                        !msr.ReadMsr(0x1b1, out uint thermal, out _) || (thermal & 0x80000000) == 0)
                        throw new SensorUnavailableException(SensorProblem.Permission, "CPU registers need administrator access.");
                }
                finally { msr.Close(); }
            }
        }
        catch { computer.Close(); throw; }
    }

    public IReadOnlyList<CpuSensorValue> Read()
    {
        cpu.Update();
        return cpu.Sensors.Select(sensor => new CpuSensorValue(sensor.Name, sensor.SensorType, sensor.Value is { } value ? value : null)).ToArray();
    }
    public void Dispose() => computer.Close();
}

public sealed class SensorClient(Func<ICpuSensorReader>? factory = null)
{
    public const string SourceName = "LibreHardwareMonitor · PawnIO";
    public event Action<SensorSample>? SampleReceived;
    public event Action<SensorStatus>? StatusChanged;
    public string? CpuName { get; private set; }
    public IReadOnlyList<CpuSensorValue> LastSensors { get; private set; } = [];
    public Task RunAsync(CancellationToken token) => Task.Run(async () =>
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Retries after a failure keep reporting that failure, so the status doesn't flicker through Starting.
                if (failures == 0) StatusChanged?.Invoke(SensorStatus.Starting);
                using var reader = (factory ?? (() => new LibreCpuSensorReader()))();
                CpuName = reader.CpuName;
                // Package power and CPU busy time are deltas. Prime once, then
                // allow a full interval before publishing the first reading.
                reader.Read();
                await Task.Delay(1000, token);
                while (!token.IsCancellationRequested)
                {
                    LastSensors = reader.Read();
                    var sample = CpuSensorSelector.Select(LastSensors, DateTimeOffset.UtcNow);
                    failures = 0;
                    StatusChanged?.Invoke(SensorStatus.Live);
                    SampleReceived?.Invoke(sample);
                    await Task.Delay(1000, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            { StatusChanged?.Invoke(SensorStatus.From(error)); }
            // Opening the sensor backend is expensive; back off 2, 4, 8, 16, then every 30 s while it keeps failing.
            failures++;
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 << Math.Min(failures - 1, 4))), token); } catch (OperationCanceledException) { break; }
        }
    }, CancellationToken.None);
}
