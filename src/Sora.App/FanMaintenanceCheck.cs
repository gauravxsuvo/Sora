using System;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using Sora.Core;

namespace Sora.App;

internal static class FanMaintenanceCheck
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Capture(int Format, string Machine, FanSnapshot Snapshot, int ConfigurationWrites, string SnapshotSha256);
    private static string Fingerprint(FanSnapshot snapshot) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, Json)));
    private static Capture Backup(FanSnapshot snapshot) => new(2, Environment.MachineName, snapshot, 0, Fingerprint(snapshot));

    internal static void SaveNew(string path, object value)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, value, Json); file.Flush(flushToDisk: true);
    }

    public static bool Run(string output, string? baselinePath)
    {
        output = Path.GetFullPath(output);
        // Do not truncate either an earlier result or its recovery evidence.
        if (File.Exists(output) || File.Exists(output + ".before.json") || File.Exists(output + ".writes.jsonl")) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        int nativeWrites = 0, firmwareWrites = 0;
        FanSnapshot? before = null, after = null; string? error = null;
        try
        {
            if (baselinePath == null)
            {
                using var fans = new GigabyteFans();
                var snapshot = GigabyteFanFirmware.Enrich(fans.ReadSnapshot());
                FanMaintenance.Validate(snapshot);
                SaveNew(output, Backup(snapshot));
                return true;
            }
            var saved = JsonSerializer.Deserialize<Capture>(File.ReadAllText(baselinePath), Json) ?? throw new InvalidDataException("Missing maintenance backup.");
            if (saved.Format != 2 || saved.Machine != Environment.MachineName || saved.ConfigurationWrites != 0 ||
                saved.Snapshot.ReadAt > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - saved.Snapshot.ReadAt > TimeSpan.FromHours(1))
                throw new InvalidDataException("Use this machine's fresh read-only maintenance backup (at most one hour old).");
            if (saved.SnapshotSha256 != Fingerprint(saved.Snapshot)) throw new InvalidDataException("Maintenance backup integrity check failed.");
            FanMaintenance.Validate(saved.Snapshot);
            var conflict = GigabyteFans.FindConflict();
            if (conflict != null) throw new IOException(conflict);
            using var native = new GigabyteFans();
            using var firmware = new GigabyteFanFirmware();
            before = GigabyteFanFirmware.Enrich(native.ReadSnapshot());
            FanMaintenance.Validate(before);
            // Persist the pre-recovery state before touching any configuration register.
            SaveNew(output + ".before.json", Backup(before));
            using var journalFile = new FileStream(output + ".writes.jsonl", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            void Journal(object entry)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
                journalFile.Write(bytes); journalFile.Flush(flushToDisk: true);
            }
            var countedNative = new CountedNative(native, (register, value) =>
            {
                Journal(new { domain = "native", register.Bank, register.Address, value, at = DateTimeOffset.UtcNow }); nativeWrites++;
            });
            var countedFirmware = new CountedFirmware(firmware, (header, value) =>
            {
                Journal(new { domain = "firmware", header, value, at = DateTimeOffset.UtcNow }); firmwareWrites++;
            });
            FanMaintenance.Restore(countedNative, countedFirmware, saved.Snapshot);
            after = GigabyteFanFirmware.Enrich(native.ReadSnapshot());
            if (!FanMaintenance.Matches(saved.Snapshot, after)) throw new IOException("Final native/firmware settings differ from maintenance baseline.");
            FanMaintenance.RequireCooling(after);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) { error = failure.ToString(); }
        SaveNew(output, new { passed = error == null, nativeWriteAttempts = nativeWrites, firmwareWriteAttempts = firmwareWrites,
            before, after, error, servicesChanged = false });
        return error == null;
    }

    private sealed class CountedNative(IFanRegisterAccess inner, Action<FanRegister, byte> writing) : IFanRegisterAccess
    {
        public byte Read(FanRegister register) => inner.Read(register);
        public void Write(FanRegister register, byte value) { writing(register, value); inner.Write(register, value); }
        public void Dispose() { } // Outer scope owns the hardware and mutexes.
    }
    private sealed class CountedFirmware(IFanFirmwareAccess inner, Action<int, FanStopConfig> writing) : IFanFirmwareAccess
    {
        public FanStopConfig Read(int header) => inner.Read(header);
        public void Write(int header, FanStopConfig value)
        {
            if (inner.Read(header) == value) return;
            writing(header, value); inner.Write(header, value);
        }
        public void Dispose() { }
    }
}
