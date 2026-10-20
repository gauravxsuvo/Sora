using Sora.Core;

static class LenovoLegionControllerChecks
{
    public static async Task Run(Action<bool, string> require)
    {
        var fake = new FakeLegion(); var store = new MemoryRecovery(); var controller = new LenovoLegionController(fake, store);
        var initial = await controller.ReadAsync();
        require(initial.PowerMode == LegionPowerMode.Balanced && initial.ChargeMode == LegionChargeMode.Normal && fake.Writes == 0,
            "Legion discovery reads independent controls without changing configuration");
        await controller.SetPowerAsync(LegionPowerMode.Quiet);
        require(fake.Power == 1 && store.Items.Count == 1 && store.Items[0].PowerMode == 2, "Power change has original-state evidence and verified state");
        await controller.SetPowerAsync(LegionPowerMode.Performance);
        await controller.RestorePowerAsync();
        require(fake.Power == 2 && !controller.CanRestorePower, "Restore retains the session's initial mode across multiple edits");
        await controller.SetChargeAsync(LegionChargeMode.Conservation);
        require(fake.ChargeFlags == 0x20 && fake.ChargeRequests.TakeLast(3).SequenceEqual(new uint[] { 8, 3, 255 }), "Charging uses ordered disable/enable calls and independent readback");
        await controller.RestoreChargeAsync();
        require(fake.ChargeFlags == 0 && !controller.CanRestoreCharge, "Charging restores and verifies the session baseline");

        var deniedStore = new MemoryRecovery { Fail = true }; var deniedDevice = new FakeLegion();
        await Fails(() => new LenovoLegionController(deniedDevice, deniedStore).SetChargeAsync(LegionChargeMode.Conservation));
        require(deniedDevice.Writes == 0, "Failed durable backup blocks all charging commands");
        await Fails(() => new LenovoLegionController(deniedDevice, deniedStore).SetPowerAsync(LegionPowerMode.Quiet));
        require(deniedDevice.Writes == 0, "Failed durable backup blocks all power commands");

        foreach (var identity in new[] { fake.Identity with { Manufacturer = "Other" }, fake.Identity with { Version = "Legion Go" }, fake.Identity with { Version = "Legion T5 Tower" } })
        {
            var wrong = new FakeLegion { Identity = identity };
            var wrongController = new LenovoLegionController(wrong, new MemoryRecovery());
            await Fails(() => wrongController.ReadAsync()); await Fails(() => wrongController.SetPowerAsync(LegionPowerMode.Quiet));
            require(wrong.Calls == 0, "Unrelated PCs, handhelds and towers never reach the laptop transport");
        }
        var noAc = new FakeLegion { Ac = false }; var noAcStore = new MemoryRecovery();
        await Fails(() => new LenovoLegionController(noAc, noAcStore).SetPowerAsync(LegionPowerMode.Performance));
        require(noAc.Writes == 0 && noAcStore.Items.Count == 0, "Performance on battery is rejected before backup or mutation");
        var conflicts = new FakeLegion(); var conflictController = new LenovoLegionController(conflicts, new MemoryRecovery());
        await conflictController.ReadAsync(); conflicts.Competing = ["LenovoLegionToolkit"];
        await Fails(() => conflictController.SetPowerAsync(LegionPowerMode.Quiet));
        require(conflicts.Writes == 0, "A conflict appearing after refresh blocks Apply");
        var partial = new FakeLegion { MissingCharging = true };
        var partialState = await new LenovoLegionController(partial, new MemoryRecovery()).ReadAsync();
        require(partialState.PowerMode != null && partialState.ChargeMode == null && partialState.Problems.Length == 1, "Missing charging driver does not hide a working power interface");
        var unsupported = new FakeLegion { Modes = [LegionPowerMode.Quiet] };
        await Fails(() => new LenovoLegionController(unsupported, new MemoryRecovery()).SetPowerAsync(LegionPowerMode.Performance));
        require(unsupported.Writes == 0, "Unadvertised power mode cannot be set");

        // Every stage can fail after the device has already accepted the change.
        foreach (var failedWrite in new[] { 1, 2 })
        {
            var device = new FakeLegion { FailWrites = [failedWrite] };
            var error = await ChangeFails(() => new LenovoLegionController(device, new MemoryRecovery()).SetChargeAsync(LegionChargeMode.Conservation));
            require(error.Restored && device.ChargeFlags == 0, "Partial charging transaction restores after failed stage " + failedWrite);
        }
        var powerFailure = new FakeLegion { FailWrites = [1] };
        var powerError = await ChangeFails(() => new LenovoLegionController(powerFailure, new MemoryRecovery()).SetPowerAsync(LegionPowerMode.Quiet));
        require(powerError.Restored && powerFailure.Power == 2, "Power write failure after mutation restores original firmware state");
        var mismatch = new FakeLegion { IgnoreNextPower = true };
        var mismatchError = await ChangeFails(() => new LenovoLegionController(mismatch, new MemoryRecovery()).SetPowerAsync(LegionPowerMode.Quiet));
        require(mismatchError.Restored && mismatch.Power == 2, "A successful native call with wrong readback is not reported as applied");
        var failedRecovery = new FakeLegion { FailWrites = [1, 2] };
        var recoveryError = await ChangeFails(() => new LenovoLegionController(failedRecovery, new MemoryRecovery()).SetPowerAsync(LegionPowerMode.Quiet));
        require(!recoveryError.Restored && recoveryError.RecoveryError != null, "Failed rollback explicitly leaves state unknown");

        var keyboard = FakeLegion.Keyboard;
        var report = LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.Static, 1, 2, new byte[12]);
        var lightDevice = new FakeLegion(); var lightStore = new MemoryRecovery(); var lights = new LenovoLegionController(lightDevice, lightStore);
        await lights.SetLightingAsync(keyboard, report);
        require(lightStore.Items[0].PreviousLighting == null && lightDevice.Lighting!.SequenceEqual(report), "First write-only lighting edit does not invent an original state");
        lightDevice.FailWrites = [2];
        var lightError = await ChangeFails(() => lights.SetLightingAsync(keyboard, LenovoLegionProtocol.FourZoneOffReport()));
        require(lightError.Restored && lightDevice.Lighting!.SequenceEqual(report), "A failed later lighting edit can recover the previous known session command");
        foreach (var wrongKeyboard in new[] { keyboard with { VendorId = 0x1234 }, keyboard with { UsagePage = 1 }, keyboard with { ProductId = 0xc900 }, keyboard with { FeatureReportLength = 960 }, keyboard with { Path = "unplugged" } })
        {
            var device = new FakeLegion();
            await Fails(() => new LenovoLegionController(device, new MemoryRecovery()).SetLightingAsync(wrongKeyboard, report));
            require(device.Writes == 0, "Lighting rejects foreign interfaces, Spectrum, unknown IDs and unplugged paths");
        }
        var corrupt = report.ToArray(); corrupt[32] = 1;
        await Fails(() => lights.SetLightingAsync(keyboard, corrupt));

        foreach (var stage in new[] { 1, 2 })
        {
            var spectrumDevice = new FakeLegion { SpectrumAttached = true, FailWrites = [stage] };
            var spectrumController = new LenovoLegionController(spectrumDevice, new MemoryRecovery());
            var spectrumError = await ChangeFails(() => spectrumController.SetSpectrumAsync(FakeLegion.SpectrumKeyboard, new(9, 3)));
            require(spectrumError.Restored && spectrumDevice.SpectrumSetting == new LegionSpectrumSetting(6, 1),
                "Spectrum restores profile and brightness after partial stage " + stage);
        }
        var spectrumHardware = new FakeLegion { SpectrumAttached = true };
        var spectrumStore = new MemoryRecovery(); var spectrum = new LenovoLegionController(spectrumHardware, spectrumStore);
        require((await spectrum.ReadAsync()).Spectrum.Single().Setting == new LegionSpectrumSetting(6, 1), "Spectrum queries remain distinct from four-zone descriptors");
        await spectrum.SetSpectrumAsync(FakeLegion.SpectrumKeyboard, new(0, 4));
        await spectrum.RestoreSpectrumAsync(FakeLegion.SpectrumKeyboard);
        require(spectrumHardware.SpectrumSetting == new LegionSpectrumSetting(6, 1), "Spectrum baseline is preserved independently of requested brightness/profile");
        var spectrumWrites = spectrumHardware.Writes;
        await Fails(() => spectrum.SetSpectrumAsync(FakeLegion.SpectrumKeyboard, new(10, 4)));
        require(spectrumHardware.Writes == spectrumWrites, "Invalid Spectrum brightness cannot reach hardware");
        var gpuHardware = new FakeLegion(); var gpu = new LenovoLegionController(gpuHardware, new MemoryRecovery());
        await gpu.SetGpuAsync(LegionGpuMode.Discrete); await gpu.RestoreGpuAsync();
        require(gpuHardware.GpuMode == LegionGpuMode.Hybrid && !gpu.CanRestoreGpu, "GPU firmware configuration has independent original-state restoration");
        var brokenGpu = new FakeLegion { FailWrites = [1] };
        var gpuError = await ChangeFails(() => new LenovoLegionController(brokenGpu, new MemoryRecovery()).SetGpuAsync(LegionGpuMode.IntegratedOnly));
        require(gpuError.Restored && brokenGpu.GpuMode == LegionGpuMode.Hybrid, "GPU failure after mutation restores the previous configured mode");
        var restrictedGpu = new FakeLegion { GpuModes = [LegionGpuMode.Hybrid] };
        await Fails(() => new LenovoLegionController(restrictedGpu, new MemoryRecovery()).SetGpuAsync(LegionGpuMode.Discrete));
        require(restrictedGpu.Writes == 0, "Unsupported GPU modes do not reach the transport");

        var blocked = new FakeLegion { BlockWrite = true };
        var stopping = new LenovoLegionController(blocked, new MemoryRecovery());
        var active = stopping.SetChargeAsync(LegionChargeMode.Conservation);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancel = new CancellationTokenSource();
        var queued = stopping.SetPowerAsync(LegionPowerMode.Quiet, cancel.Token); cancel.Cancel();
        await Fails(() => queued);
        var stop = stopping.StopAsync();
        require(!stop.IsCompleted, "Shutdown waits for the in-flight Legion transaction");
        blocked.Release.Set(); await active; await stop;
        var writesAtStop = blocked.Writes;
        await Fails(() => stopping.SetPowerAsync(LegionPowerMode.Quiet));
        require(blocked.Writes == writesAtStop && blocked.ChargeFlags == 0x20, "Queued cancellation and shutdown cannot interrupt charging completion or start new writes");

        var quirky = new FakeLegion { Identity = fake.Identity with { BiosVersion = "J2CN00WW" }, Power = 1 };
        await new LenovoLegionController(quirky, new MemoryRecovery()).SetPowerAsync(LegionPowerMode.Performance);
        require(quirky.PowerRequests.SequenceEqual(new uint[] { 2, 3 }), "J2CN Quiet to Performance uses the source-defined Balanced intermediate mode");
    }
    private static async Task Fails(Func<Task> action)
    {
        try { await action(); } catch (Exception error) when (error is not OutOfMemoryException) { return; }
        throw new Exception("Expected the Legion operation to fail.");
    }
    private static async Task<LegionChangeException> ChangeFails(Func<Task> action)
    {
        try { await action(); } catch (LegionChangeException error) { return error; }
        throw new Exception("Expected a Legion transaction/recovery result.");
    }
    private sealed class MemoryRecovery : ILegionRecoveryStore
    {
        public bool Fail;
        public readonly List<LegionRecovery> Items = [];
        public void Save(LegionRecovery value) { if (Fail) throw new IOException("Backup failed."); Items.Add(value); }
    }
    private sealed class FakeLegion : ILegionTransport, ILegionSpectrumTransport, ILegionGpuTransport
    {
        public static DeviceInfo Keyboard => new("fixture-keyboard", "", 0xff89, 0xcc, 0, 33, 0x048d, 0xc985);
        public static DeviceInfo SpectrumKeyboard => new("fixture-spectrum", "", 0xff89, 0x7, 0, 960, 0x048d, 0xc987);
        public bool SpectrumAttached;
        public LegionSpectrumSetting SpectrumSetting = new(6, 1);
        public LegionGpuMode GpuMode = LegionGpuMode.Hybrid;
        public LegionGpuMode[] GpuModes = Enum.GetValues<LegionGpuMode>();
        public LenovoSystemIdentity Identity { get; set; } = new("LENOVO", "82JQ", "Legion 5 Pro 16ACH6H", "", "GKCN00WW", HardwarePresence.Present);
        public bool Ac = true, MissingCharging, IgnoreNextPower, BlockWrite;
        public uint Power = 2, ChargeFlags;
        public int Calls, Writes;
        public int[] FailWrites = [];
        public string[] Competing = [];
        public LegionPowerMode[] Modes = [LegionPowerMode.Quiet, LegionPowerMode.Balanced, LegionPowerMode.Performance];
        public readonly List<uint> ChargeRequests = [], PowerRequests = [];
        public byte[]? Lighting;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public bool IsAcConnected() { Calls++; return Ac; }
        public string[] Conflicts() { Calls++; return Competing; }
        public LegionPowerMode[] ReadSupportedPowerModes() { Calls++; return Modes; }
        public uint ReadPowerMode() { Calls++; return Power; }
        public void WritePowerMode(uint value)
        {
            Calls++; PowerRequests.Add(value);
            if (IgnoreNextPower) IgnoreNextPower = false; else Power = value;
            AfterWrite();
        }
        public uint ChargeMode(uint request)
        {
            Calls++;
            if (MissingCharging) throw new NotSupportedException("Energy Management missing.");
            ChargeRequests.Add(request);
            if (request == 255) return ChargeFlags;
            ChargeFlags = request switch { 5 => ChargeFlags & ~0x20u, 8 => ChargeFlags & ~4u, 3 => ChargeFlags | 0x20u, 7 => ChargeFlags | 4u, _ => throw new Exception("Unexpected driver command.") };
            AfterWrite(); return 0;
        }
        public DeviceInfo[] FourZoneDevices() { Calls++; return [Keyboard]; }
        public void WriteFourZone(DeviceInfo device, byte[] report) { Calls++; Lighting = report.ToArray(); AfterWrite(); }
        public DeviceInfo[] SpectrumDevices() { Calls++; return SpectrumAttached ? [SpectrumKeyboard] : []; }
        public LegionSpectrumSetting ReadSpectrum(DeviceInfo device) { Calls++; return SpectrumSetting; }
        public void WriteSpectrum(DeviceInfo device, LegionSpectrumSetting setting)
        {
            Calls++; SpectrumSetting = SpectrumSetting with { Profile = setting.Profile }; AfterWrite();
            SpectrumSetting = SpectrumSetting with { Brightness = setting.Brightness }; AfterWrite();
        }
        public LegionGpuState ReadGpu() { Calls++; return new(GpuMode, GpuModes); }
        public void WriteGpu(LegionGpuMode mode) { Calls++; GpuMode = mode; AfterWrite(); }
        private void AfterWrite()
        {
            Writes++;
            if (BlockWrite && Writes == 1) { Entered.SetResult(); if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fixture timed out."); }
            if (FailWrites.Contains(Writes)) throw new IOException("Simulated failure after mutation.");
        }
    }
}
