using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Sora.Core;

internal static class StorageChecks
{
    public static void Run()
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var log = new byte[512];
        try { StorageSmart.DecodeNvme(log); throw new InvalidOperationException("Empty NVMe log reported healthy."); } catch (IOException) { }
        BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(1), 320);
        log[3] = 100; log[4] = 5; log[5] = 7;
        log[128] = 0x39; log[129] = 0x05;
        log[47] = 0x80; // Unsigned full-width 128-bit counter, exceeding Int64.
        var health = StorageSmart.DecodeNvme(log);
        Require(health.Status == "Good" && health.LifeRemaining == 93 && health.PowerOnHours == "1337" && Math.Abs(health.TemperatureC!.Value - 46.85) < 0.001, "NVMe temperature, wear or hours mismatch.");
        Require(BigInteger.Parse(health.Attributes.Single(a => a.Id == "06").Value) == BigInteger.One << 127, "NVMe counters truncated or signed.");
        log[0] = 4; Require(StorageSmart.DecodeNvme(log).Status == "Bad", "NVMe reliability warning ignored.");
        log[0] = 0; log[3] = 4; Require(StorageSmart.DecodeNvme(log).Status == "Caution", "Low NVMe spare ignored.");
        log[3] = 101;
        try { StorageSmart.DecodeNvme(log); throw new InvalidOperationException("Invalid NVMe spare accepted."); } catch (IOException) { }
        log[3] = 100;
        log[0] = 0; log[5] = 150; Require(StorageSmart.DecodeNvme(log).LifeRemaining == 0, "NVMe wear above 100 wrapped.");
        log[1] = log[2] = 0; Require(StorageSmart.DecodeNvme(log).TemperatureC == null, "Unreported temperature treated as absolute zero.");
        var descriptor = new byte[560];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, 48); BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(4), 48);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(8), 3); BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(24), 40); BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(28), 512);
        log.CopyTo(descriptor, 48); Require(StorageSmart.ExtractNvmeLog(descriptor).SequenceEqual(log), "NVMe protocol offset mismatch.");
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(24), uint.MaxValue);
        try { StorageSmart.ExtractNvmeLog(descriptor); throw new InvalidOperationException("Malformed NVMe offset accepted."); } catch (IOException) { }
        var identify = new byte[4096];
        Encoding.ASCII.GetBytes("ZFPKS2N1Z4S9        ").CopyTo(identify, 4);
        Encoding.ASCII.GetBytes("KIOXIA-EXCERIA G3 SSD                    ").CopyTo(identify, 24);
        Encoding.ASCII.GetBytes("EVFATR.0").CopyTo(identify, 64);
        var identity = StorageSmart.DecodeNvmeIdentify(identify);
        Require(identity == new StorageNvmeIdentity("KIOXIA-EXCERIA G3 SSD", "EVFATR.0", "ZFPKS2N1Z4S9"), "NVMe controller identity offsets or space trimming incorrect.");
        var identifyResponse = new byte[4144];
        BinaryPrimitives.WriteUInt32LittleEndian(identifyResponse, 48); BinaryPrimitives.WriteUInt32LittleEndian(identifyResponse.AsSpan(4), 48);
        BinaryPrimitives.WriteUInt32LittleEndian(identifyResponse.AsSpan(8), 3); BinaryPrimitives.WriteUInt32LittleEndian(identifyResponse.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(identifyResponse.AsSpan(24), 40); BinaryPrimitives.WriteUInt32LittleEndian(identifyResponse.AsSpan(28), 4096);
        identify.CopyTo(identifyResponse, 48);
        Require(StorageSmart.ExtractNvmeIdentify(identifyResponse).SequenceEqual(identify), "NVMe Identify protocol data offset mismatch.");
        BinaryPrimitives.WriteUInt32LittleEndian(identifyResponse.AsSpan(28), 512);
        try { StorageSmart.ExtractNvmeIdentify(identifyResponse); throw new InvalidOperationException("Truncated Identify accepted."); } catch (IOException) { }
        try { StorageSmart.DecodeNvmeIdentify(new byte[4096]); throw new InvalidOperationException("Empty Identify accepted."); } catch (IOException) { }
        var ata = new byte[512]; ata[0] = 1; ata[2] = 194; ata[5] = 100; ata[6] = 99; ata[7] = 47;
        ata[14] = 231; ata[17] = 99; ata[19] = 97;
        ata[511] = (byte)((256 - ata.Take(511).Sum(b => b) % 256) % 256);
        var ataHealth = StorageSmart.DecodeAta(ata);
        Require(ataHealth.TemperatureC == 47 && ataHealth.LifeRemaining == null && ataHealth.Status == "Unknown", "ATA vendor wear inferred without a model decoder.");
        var thresholds = new byte[512]; thresholds[0] = 1; thresholds[2] = 231; thresholds[3] = 100;
        thresholds[511] = (byte)((256 - thresholds.Take(511).Sum(b => b) % 256) % 256);
        Require(StorageSmart.DecodeAta(ata, thresholds).Status == "Bad", "ATA threshold failure ignored.");
        ata[0] ^= 1;
        try { StorageSmart.DecodeAta(ata); throw new InvalidOperationException("Corrupt ATA checksum accepted."); } catch (IOException) { }
        var drive = new StorageDrive(0, "=FORMULA,\"disk\"", "f", "s", 1, "NVMe", [], true, true, health);
        var csv = StorageSmart.ToCsv(drive);
        var whitespaceFormula = StorageSmart.ToCsv(drive with { Model = "  =1+1" });
        Require(whitespaceFormula.Contains("\"'  =1+1\""), "CSV formula protection must cover leading whitespace");
        Require(csv.Contains("\"'=FORMULA,\"\"disk\"\"\"") && csv.Contains("1337"), "CSV escaping or formula protection failed.");
    }
}
