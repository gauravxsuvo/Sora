using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using Sora.Core;

namespace Sora.App;

internal sealed class FanCalibrationSession : IFanCalibrationSession
{
    private readonly int header;
    private readonly FanRegisterValue[] original;
    private readonly FanStopConfig? originalFirmware;
    private bool firmwareDisabled, restored;
    public FanCalibrationSession(int header, Action<FanRegisterValue[], FanStopConfig?> saveBackup)
    {
        this.header = header;
        CheckConflict();
        using (var fans = new GigabyteFans()) original = FanTransactions.Capture(fans, header);
        try { using var firmware = new GigabyteFanFirmware(); originalFirmware = firmware.Read(header); }
        catch (NotSupportedException) { }
        catch (ManagementException e) when (e.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.NotFound) { }
        saveBackup(original, originalFirmware);
    }
    private static void CheckConflict() { if (GigabyteFans.FindConflict() is { } conflict) throw new IOException(conflict); }
    public FanCalibrationReading Read()
    {
        CheckConflict();
        using var fans = new GigabyteFans(); var state = fans.ReadSnapshot();
        return new(state.TemperaturesC[2], state.Headers[header].Rpm);
    }
    public void SetDuty(int dutyPercent)
    {
        CheckConflict();
        if (!firmwareDisabled && originalFirmware != null)
        {
            using var firmware = new GigabyteFanFirmware();
            FanFirmwareTransactions.Apply(firmware, header, originalFirmware with { Enabled = false });
            firmwareDisabled = true;
        }
        using var fans = new GigabyteFans();
        FanTransactions.Apply(fans, new FanProfile { Header = header, Mode = FanCurveMode.Fixed, FixedDutyPercent = dutyPercent, TemperatureSource = 2, FullSpeedC = 85 });
    }
    public void Restore()
    {
        if (restored) return;
        var errors = new List<Exception>();
        try { using var fans = new GigabyteFans(); FanTransactions.Restore(fans, header, original); } catch (Exception e) { errors.Add(e); }
        if (originalFirmware != null)
            try { using var firmware = new GigabyteFanFirmware(); FanFirmwareTransactions.Restore(firmware, header, originalFirmware); } catch (Exception e) { errors.Add(e); }
        if (errors.Count > 0) throw new AggregateException("Fan calibration restoration failed; use Restore original.", errors);
        restored = true;
    }
    public void Dispose() { }
}
