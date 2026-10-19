namespace Sora.Core;

/// <summary>Preserves actual hardware settings across a vendor service's stop-time restoration.</summary>
public static class FanMaintenance
{
    public const string Controller = "ITE IT8696E";
    public const string FirmwareInterface = "Sora_GigabyteFanControl";

    public static void Validate(FanSnapshot snapshot)
    {
        if (snapshot.Board != GigabyteFans.SupportedBoard || snapshot.Controller != Controller ||
            snapshot.FirmwareInterface != FirmwareInterface || snapshot.Headers is not { Length: 6 } ||
            !snapshot.Headers.Select(h => h.Header).Order().SequenceEqual(Enumerable.Range(0, 6)))
            throw new InvalidDataException("Maintenance requires the verified board/controller, six distinct headers and Sora's standalone firmware interface.");
        foreach (var header in snapshot.Headers)
        {
            var allowed = FanProtocol.Registers(header.Header);
            if (header.Registers == null || header.Registers.Length != allowed.Length ||
                !header.Registers.Select(r => r.Register).OrderBy(r => r.Bank).ThenBy(r => r.Address).SequenceEqual(allowed))
                throw new InvalidDataException($"Incomplete or invalid native backup for fan header {header.Header}.");
            if (header.FirmwareStop == null) throw new InvalidDataException("Firmware settings must be captured for every header.");
            header.FirmwareStop.Validate();
            var control = header.Registers.Single(r => r.Register == FanProtocol.ControlRegister(header.Header)).Value;
            if (header.Automatic != ((control & 0x80) != 0)) throw new InvalidDataException("Backup mode flag and native control byte disagree.");
        }
    }

    public static bool Matches(FanSnapshot expected, FanSnapshot actual)
    {
        Validate(expected); Validate(actual);
        return expected.Headers.All(h =>
        {
            var other = actual.Headers.Single(x => x.Header == h.Header);
            return h.FirmwareStop == other.FirmwareStop && h.Registers.All(r =>
                other.Registers.Single(v => v.Register == r.Register).Value == r.Value);
        });
    }

    public static void RequireCooling(FanSnapshot snapshot)
    {
        Validate(snapshot);
        foreach (var header in new[] { 0, 5 })
        {
            var fan = snapshot.Headers.Single(h => h.Header == header);
            if (!fan.Automatic || fan.Rpm is not { } rpm || !double.IsFinite(rpm) || rpm < 700 || fan.FirmwareStop!.Enabled)
                throw new IOException($"Cooling header {header} must be running automatically above 700 RPM with firmware stop disabled.");
        }
        if (snapshot.TemperaturesC is not { Length: 6 } || snapshot.TemperaturesC.Any(t => !t.HasValue || !double.IsFinite(t.Value) || t <= 0 || t >= 75))
            throw new IOException("Maintenance needs six valid motherboard temperatures below 75°C.");
    }

    /// <summary>
    /// Call only while all competing writers are stopped. The caller durably saves the complete
    /// baseline before stopping anything, and releases hardware mutexes before service operations.
    /// Recovery keeps trying independent headers and firmware after an individual failure. It never
    /// rolls back toward the stale vendor state; any failure prevents a successful qualification.
    /// </summary>
    public static void Restore(IFanRegisterAccess native, IFanFirmwareAccess firmware, FanSnapshot baseline)
    {
        Validate(baseline); // Validate every header before the first possible write.
        var failures = new List<Exception>();
        void Attempt(string name, Action action)
        {
            try { action(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { failures.Add(new IOException(name, error)); }
        }
        foreach (var h in baseline.Headers.OrderBy(h => h.Header))
        {
            Attempt($"Restore native header {h.Header}", () => FanTransactions.Restore(native, h.Header, h.Registers));
            Attempt($"Restore firmware header {h.Header}", () => FanFirmwareTransactions.Restore(firmware, h.Header, h.FirmwareStop!));
        }
        // Firmware methods may also affect native registers. Compare both domains after all writes.
        foreach (var h in baseline.Headers)
        {
            Attempt($"Verify native header {h.Header}", () =>
            {
                if (h.Registers.Any(r => native.Read(r.Register) != r.Value)) throw new IOException("Native settings differ from baseline.");
            });
            Attempt($"Verify firmware header {h.Header}", () =>
            {
                if (firmware.Read(h.Header) != h.FirmwareStop) throw new IOException("Firmware settings differ from baseline.");
            });
        }
        if (failures.Count != 0) throw new AggregateException("Fan maintenance recovery was not fully verified. Preserve the baseline and retry recovery before restarting the vendor fan service.", failures);
    }
}
