namespace Sora.Core;

public interface ILegionTransport
{
    LenovoSystemIdentity Identity { get; }
    bool IsAcConnected();
    string[] Conflicts();
    LegionPowerMode[] ReadSupportedPowerModes();
    uint ReadPowerMode();
    void WritePowerMode(uint value);
    uint ChargeMode(uint request);
    DeviceInfo[] FourZoneDevices();
    void WriteFourZone(DeviceInfo device, byte[] report);
}

public sealed record LegionState(LenovoSystemIdentity Identity, LegionPowerMode? PowerMode, LegionPowerMode[] PowerModes,
    LegionChargeMode? ChargeMode, DeviceInfo[] Keyboards, string[] Conflicts, string[] Problems)
{
    public LegionSpectrumState[] Spectrum { get; init; } = [];
    public LegionGpuState? Gpu { get; init; }
}
public sealed record LegionRecovery(string Feature, LenovoSystemIdentity Identity, uint? PowerMode = null,
    LegionChargeMode? ChargeMode = null, string? KeyboardModel = null, byte[]? PreviousLighting = null, LegionSpectrumSetting? Spectrum = null, LegionGpuMode? GpuMode = null);
public interface ILegionRecoveryStore { void Save(LegionRecovery recovery); }
public sealed class LegionChangeException(string message, Exception cause, Exception? recoveryError) : IOException(message, cause)
{
    public bool Restored => RecoveryError == null;
    public Exception? RecoveryError { get; } = recoveryError;
}

/// <summary>Explicit user transactions only. No startup replay or background writes.</summary>
public sealed class LenovoLegionController(ILegionTransport transport, ILegionRecoveryStore recovery)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private LegionRecovery? originalPower, originalCharge;
    private LegionGpuMode? originalGpu;
    private readonly Dictionary<string, byte[]> lastLighting = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LegionSpectrumSetting> originalSpectrum = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool stopping;
    public bool CanRestorePower => originalPower != null;
    public bool CanRestoreCharge => originalCharge != null;
    public bool CanRestoreGpu => originalGpu != null;
    public bool CanRestoreSpectrum(DeviceInfo keyboard) { lock (originalSpectrum) return originalSpectrum.ContainsKey(keyboard.Path); }
    public async Task StopAsync()
    {
        stopping = true;
        await Gate.WaitAsync().ConfigureAwait(false);
        Gate.Release(); // Every active transaction, including recovery, has finished.
    }

    public Task<LegionState> ReadAsync(CancellationToken token = default) => Run(() =>
    {
        EnsureLaptop();
        LegionPowerMode? mode = null;
        LegionPowerMode[] modes = [];
        LegionChargeMode? charging = null;
        DeviceInfo[] keyboards = [];
        var spectrum = new List<LegionSpectrumState>();
        LegionGpuState? gpu = null;
        var problems = new List<string>();
        try { modes = transport.ReadSupportedPowerModes(); mode = LenovoLegionProtocol.DecodePowerMode(transport.ReadPowerMode()); }
        catch (Exception error) when (error is not OutOfMemoryException) { problems.Add("Power: " + error.Message); modes = []; }
        try { charging = LenovoLegionProtocol.DecodeChargeMode(transport.ChargeMode(LenovoLegionProtocol.ReadChargeModeRequest)); }
        catch (Exception error) when (error is not OutOfMemoryException) { problems.Add("Charging: " + error.Message); }
        try { keyboards = transport.FourZoneDevices().Where(IsFourZone).ToArray(); }
        catch (Exception error) when (error is not OutOfMemoryException) { problems.Add("Keyboard: " + error.Message); }
        if (transport is ILegionGpuTransport gpuTransport)
        {
            try { gpu = gpuTransport.ReadGpu(); }
            catch (Exception error) when (error is not OutOfMemoryException) { problems.Add("GPU mode: " + error.Message); }
        }
        if (transport is ILegionSpectrumTransport spectrumTransport)
        {
            try
            {
                foreach (var device in spectrumTransport.SpectrumDevices().Where(LenovoLegionSpectrum.IsCandidate))
                {
                    try { spectrum.Add(new(device, spectrumTransport.ReadSpectrum(device))); }
                    catch (Exception error) when (error is not OutOfMemoryException) { problems.Add("Spectrum: " + error.Message); }
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { problems.Add("Spectrum discovery: " + error.Message); }
        }
        return new LegionState(transport.Identity, mode, modes, charging, keyboards, transport.Conflicts(), problems.ToArray()) { Spectrum = spectrum.ToArray(), Gpu = gpu };
    }, token);

    public Task SetPowerAsync(LegionPowerMode target, CancellationToken token = default) => Run(() =>
    {
        EnsureWritable();
        if (!transport.ReadSupportedPowerModes().Contains(target)) throw new NotSupportedException("This firmware does not advertise the selected power mode.");
        if (target is LegionPowerMode.Custom or LegionPowerMode.Extreme) throw new NotSupportedException("Advanced power modes need a separate tuning backend.");
        if (target == LegionPowerMode.Performance && !transport.IsAcConnected()) throw new InvalidOperationException("Connect the AC adapter before selecting Performance.");
        var before = LenovoLegionProtocol.DecodePowerMode(transport.ReadPowerMode());
        if (before == target) return true;
        var backup = new LegionRecovery("power", transport.Identity, (uint)before);
        recovery.Save(backup); // Durable evidence must precede the first mutation.
        originalPower ??= backup;
        try { ChangePower(before, target); VerifyPower(target); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw Recover("Power change failed", error, () => { ChangePower(null, before); VerifyPower(before); });
        }
        return true;
    }, token);

    public Task SetChargeAsync(LegionChargeMode target, CancellationToken token = default) => Run(() =>
    {
        EnsureWritable();
        _ = LenovoLegionProtocol.ChargeModeRequests(target);
        var before = LenovoLegionProtocol.DecodeChargeMode(transport.ChargeMode(LenovoLegionProtocol.ReadChargeModeRequest));
        if (before == target) return true;
        var backup = new LegionRecovery("charging", transport.Identity, ChargeMode: before);
        recovery.Save(backup);
        originalCharge ??= backup;
        try { ChangeCharge(target); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw Recover("Charging change failed", error, () => ChangeCharge(before));
        }
        return true;
    }, token);

    public Task RestorePowerAsync(CancellationToken token = default) => Run(() =>
    {
        EnsureWritable();
        var backup = originalPower ?? throw new InvalidOperationException("There is no original power mode for this session.");
        var target = LenovoLegionProtocol.DecodePowerMode(backup.PowerMode!.Value);
        if (target == LegionPowerMode.Performance && !transport.IsAcConnected()) throw new InvalidOperationException("Connect AC before restoring Performance.");
        var before = LenovoLegionProtocol.DecodePowerMode(transport.ReadPowerMode());
        recovery.Save(new("power-before-restore", transport.Identity, (uint)before));
        try { ChangePower(before, target); VerifyPower(target); originalPower = null; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw Recover("Power restoration failed", error, () => { ChangePower(null, before); VerifyPower(before); }); }
        return true;
    }, token);

    public Task RestoreChargeAsync(CancellationToken token = default) => Run(() =>
    {
        EnsureWritable();
        var backup = originalCharge ?? throw new InvalidOperationException("There is no original charging mode for this session.");
        var before = LenovoLegionProtocol.DecodeChargeMode(transport.ChargeMode(LenovoLegionProtocol.ReadChargeModeRequest));
        recovery.Save(new("charging-before-restore", transport.Identity, ChargeMode: before));
        try { ChangeCharge(backup.ChargeMode!.Value); originalCharge = null; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw Recover("Charging restoration failed", error, () => ChangeCharge(before)); }
        return true;
    }, token);

    public Task SetLightingAsync(DeviceInfo keyboard, byte[] report, CancellationToken token = default)
    {
        var copy = report.ToArray();
        ValidateLighting(copy);
        return Run(() =>
        {
            EnsureWritable();
            if (!IsFourZone(keyboard) || !transport.FourZoneDevices().Any(device => SameKeyboard(device, keyboard)))
                throw new NotSupportedException("The selected four-zone keyboard is no longer available.");
            lastLighting.TryGetValue(keyboard.Path, out var previous);
            recovery.Save(new("four-zone-lighting", transport.Identity,
                KeyboardModel: $"{keyboard.VendorId:X4}:{keyboard.ProductId:X4}", PreviousLighting: previous?.ToArray()));
            try { transport.WriteFourZone(keyboard, copy); lastLighting[keyboard.Path] = copy; }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (previous == null) throw new IOException("Lighting command failed. The controller does not expose its original colors; its current state is unknown.", error);
                throw Recover("Lighting command failed", error, () => transport.WriteFourZone(keyboard, previous));
            }
            return true;
        }, token);
    }

    public static bool IsFourZone(DeviceInfo device) => device.VendorId == 0x048d &&
        new ushort[] { 0xc955, 0xc965, 0xc975, 0xc984, 0xc985, 0xc994, 0xc995 }.Contains(device.ProductId) &&
        device.UsagePage == 0xff89 && device.Usage == 0x00cc && device.FeatureReportLength == 33;

    public Task SetSpectrumAsync(DeviceInfo keyboard, LegionSpectrumSetting setting, CancellationToken token = default) => Run(() =>
    {
        EnsureWritable();
        _ = LenovoLegionSpectrum.Brightness(setting.Brightness); _ = LenovoLegionSpectrum.Profile(setting.Profile);
        var access = SpectrumAccess(keyboard);
        var before = access.ReadSpectrum(keyboard);
        if (before == setting) return true;
        recovery.Save(new("spectrum", transport.Identity, KeyboardModel: $"{keyboard.VendorId:X4}:{keyboard.ProductId:X4}", Spectrum: before));
        lock (originalSpectrum) originalSpectrum.TryAdd(keyboard.Path, before);
        try { ChangeSpectrum(access, keyboard, setting); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw Recover("Spectrum change failed", error, () => ChangeSpectrum(access, keyboard, before)); }
        return true;
    }, token);
    public Task RestoreSpectrumAsync(DeviceInfo keyboard, CancellationToken token = default) => Run(() =>
    {
        EnsureWritable();
        LegionSpectrumSetting target;
        lock (originalSpectrum)
            if (!originalSpectrum.TryGetValue(keyboard.Path, out target!)) throw new InvalidOperationException("No original Spectrum state exists for this session.");
        var access = SpectrumAccess(keyboard);
        var before = access.ReadSpectrum(keyboard);
        recovery.Save(new("spectrum-before-restore", transport.Identity, KeyboardModel: $"{keyboard.VendorId:X4}:{keyboard.ProductId:X4}", Spectrum: before));
        try { ChangeSpectrum(access, keyboard, target); lock (originalSpectrum) originalSpectrum.Remove(keyboard.Path); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw Recover("Spectrum restoration failed", error, () => ChangeSpectrum(access, keyboard, before)); }
        return true;
    }, token);
    private ILegionSpectrumTransport SpectrumAccess(DeviceInfo keyboard)
    {
        if (transport is not ILegionSpectrumTransport access || !LenovoLegionSpectrum.IsCandidate(keyboard) ||
            !access.SpectrumDevices().Any(device => SameKeyboard(device, keyboard))) throw new NotSupportedException("The Spectrum keyboard is no longer available.");
        return access;
    }
    private static void ChangeSpectrum(ILegionSpectrumTransport access, DeviceInfo keyboard, LegionSpectrumSetting setting)
    {
        access.WriteSpectrum(keyboard, setting);
        if (access.ReadSpectrum(keyboard) != setting) throw new IOException("Spectrum readback did not match the requested profile and brightness.");
    }
    public Task SetGpuAsync(LegionGpuMode mode, CancellationToken token = default) => Run(() =>
    {
        EnsureWritable();
        if (transport is not ILegionGpuTransport access) throw new NotSupportedException("GPU working-mode support is unavailable.");
        var before = access.ReadGpu();
        if (!before.AvailableModes.Contains(mode)) throw new NotSupportedException("This GPU working mode is not supported by the firmware.");
        if (mode == before.ConfiguredMode) return true;
        recovery.Save(new("gpu-mode", transport.Identity, GpuMode: before.ConfiguredMode));
        originalGpu ??= before.ConfiguredMode;
        try { ChangeGpu(access, mode); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw Recover("GPU configuration failed", error, () => ChangeGpu(access, before.ConfiguredMode)); }
        return true;
    }, token);
    public Task RestoreGpuAsync(CancellationToken token = default) => Run(() =>
    {
        EnsureWritable();
        if (transport is not ILegionGpuTransport access || originalGpu is not { } target) throw new NotSupportedException("No original GPU configuration exists for this session.");
        var before = access.ReadGpu();
        recovery.Save(new("gpu-mode-before-restore", transport.Identity, GpuMode: before.ConfiguredMode));
        try { ChangeGpu(access, target); originalGpu = null; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw Recover("GPU restoration failed", error, () => ChangeGpu(access, before.ConfiguredMode)); }
        return true;
    }, token);
    private static void ChangeGpu(ILegionGpuTransport access, LegionGpuMode target)
    {
        if (!access.ReadGpu().AvailableModes.Contains(target)) throw new NotSupportedException("GPU capability changed; refresh before applying.");
        access.WriteGpu(target);
        if (access.ReadGpu().ConfiguredMode != target) throw new IOException("GPU firmware configuration readback did not match; active routing is unverified.");
    }

    private static bool SameKeyboard(DeviceInfo a, DeviceInfo b) => a.Path.Equals(b.Path, StringComparison.OrdinalIgnoreCase)
        && a.VendorId == b.VendorId && a.ProductId == b.ProductId && a.UsagePage == b.UsagePage && a.Usage == b.Usage
        && a.FeatureReportLength == b.FeatureReportLength;
    private void EnsureLaptop()
    {
        if (!transport.Identity.IsLegionLaptop) throw new NotSupportedException("A supported Lenovo Legion laptop identity was not detected.");
    }
    private void EnsureWritable()
    {
        if (stopping) throw new InvalidOperationException("Sora is stopping; no new Legion changes can start.");
        EnsureLaptop();
        var conflicts = transport.Conflicts();
        if (conflicts.Length != 0) throw new InvalidOperationException("Exit competing Lenovo/RGB controllers before applying: " + string.Join(", ", conflicts));
    }
    private void ChangePower(LegionPowerMode? before, LegionPowerMode target)
    {
        // Read again for recovery/restoration; a previous call may have failed after changing state.
        before ??= LenovoLegionProtocol.DecodePowerMode(transport.ReadPowerMode());
        if (transport.Identity.BiosVersion.StartsWith("J2CN", StringComparison.OrdinalIgnoreCase)
            && before == LegionPowerMode.Quiet && target == LegionPowerMode.Performance)
        { transport.WritePowerMode(2); Thread.Sleep(500); }
        if (transport.Identity.BiosVersion.StartsWith("K1CN", StringComparison.OrdinalIgnoreCase) && before == LegionPowerMode.Custom)
        {
            var intermediate = target switch { LegionPowerMode.Quiet => 3u, LegionPowerMode.Balanced => 1u, LegionPowerMode.Performance => 2u, _ => (uint)target };
            transport.WritePowerMode(intermediate); Thread.Sleep(500);
        }
        transport.WritePowerMode(LenovoLegionProtocol.EncodePowerMode(target));
    }
    private void VerifyPower(LegionPowerMode expected)
    {
        if (LenovoLegionProtocol.DecodePowerMode(transport.ReadPowerMode()) != expected) throw new IOException("Firmware power-mode readback did not match the request.");
    }
    private void ChangeCharge(LegionChargeMode mode)
    {
        foreach (var request in LenovoLegionProtocol.ChargeModeRequests(mode)) transport.ChargeMode(request);
        if (LenovoLegionProtocol.DecodeChargeMode(transport.ChargeMode(LenovoLegionProtocol.ReadChargeModeRequest)) != mode)
            throw new IOException("Driver charging-mode readback did not match the request.");
    }
    private static LegionChangeException Recover(string message, Exception cause, Action restore)
    {
        Exception? recoveryError = null;
        try { restore(); } catch (Exception error) when (error is not OutOfMemoryException) { recoveryError = error; }
        return new(message + (recoveryError == null ? "; the previous state was restored." : "; restoration failed and device state is unknown."), cause, recoveryError);
    }
    private static void ValidateLighting(byte[] report)
    {
        if (report.Length != 33 || report[0] != 0xcc || report[1] != 0x16 || report[17] != 0 || report.AsSpan(20).ToArray().Any(b => b != 0))
            throw new ArgumentException("Invalid four-zone lighting packet.");
        if (report[2] == 0)
        {
            if (report.AsSpan(2).ToArray().Any(b => b != 0)) throw new ArgumentException("Invalid off packet.");
            return;
        }
        if (report[2] is not (1 or 3 or 4 or 6) || report[3] is < 1 or > 4 || report[4] is < 1 or > 2 ||
            (report[2] != 4 && (report[18] != 0 || report[19] != 0)) ||
            (report[2] == 4 && !((report[18] == 1 && report[19] == 0) || (report[18] == 0 && report[19] == 1))) ||
            (report[2] is 4 or 6 && report.AsSpan(5, 12).ToArray().Any(b => b != 0)))
            throw new ArgumentException("Unsupported four-zone effect packet.");
    }
    private async Task<T> Run<T>(Func<T> operation, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (stopping) throw new InvalidOperationException("The Legion controller has stopped.");
            // Once a transaction starts, cancellation cannot interrupt its recovery sequence.
            return await Task.Run(operation, CancellationToken.None).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}
