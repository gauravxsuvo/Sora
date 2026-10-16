using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sora.Core;

/// <summary>
/// Kreo 2.4 GHz receiver (3554:FA09) configuration channel, recovered from OemDrv.exe class CDev3632
/// (0x496100 write, 0x496490 read, 0x496d40 checksum). Usage page FF02, usage 2, 20-byte reports with ID 0x13:
/// [0x13, command, packet count, packet index, (selector &lt;&lt; 4) | length, 14 data bytes, checksum = sum of bytes 0-18].
/// Reads are the write command | 0x40 and are answered with packets in the same layout.
/// </summary>
public sealed class KreoReceiver : IDisposable
{
    public const byte ReportId = 0x13;
    public const int ReportLength = 20, ChunkLength = 14;
    public const byte ReadMatrix = 0x41, ReadLighting = 0x44, ReadColors = 0x49, ReadPower = 0x4a;
    private readonly FileStream stream;
    private readonly int inputLength;
    public DeviceInfo Device { get; }

    public static DeviceInfo? Find() => KreoKeyboard.Descriptors().FirstOrDefault(d => d.VendorId == 0x3554 && d.ProductId == 0xfa09 && d.UsagePage == 0xff02 && d.Usage == 2 && d.OutputReportLength == ReportLength);

    public KreoReceiver(DeviceInfo device)
    {
        Device = device;
        var handle = HidDevice.Native.CreateFile(device.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the Kreo receiver's configuration channel."); }
        try
        {
            var caps = HidDevice.GetCaps(handle);
            if (caps.UsagePage != 0xff02 || caps.Usage != 2 || caps.OutputReportByteLength != ReportLength || caps.InputReportByteLength is < ReportLength or > 64)
                throw new IOException("Unexpected Kreo receiver descriptor.");
            inputLength = caps.InputReportByteLength;
            // A 512-byte reply is 37 reports; the default queue of 32 drops the tail.
            HidDevice.Native.HidD_SetNumInputBuffers(handle, 256);
            stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
        }
        catch { handle.Dispose(); throw; }
    }

    public static byte Checksum(ReadOnlySpan<byte> packet)
    {
        byte sum = 0;
        for (var i = 0; i < ReportLength - 1; i++) sum += packet[i];
        return sum;
    }

    public static byte[] Request(byte command, byte selector)
    {
        var packet = new byte[ReportLength];
        packet[0] = ReportId; packet[1] = command; packet[2] = 1; packet[3] = 0; packet[4] = (byte)(selector << 4);
        packet[^1] = Checksum(packet);
        return packet;
    }

    /// <summary>Sends one read command and reassembles the reply. Read commands do not change keyboard settings.</summary>
    public async Task<ReceiverReply> ReadAsync(byte command, byte selector, int length, TimeSpan timeout, CancellationToken token = default)
    {
        if ((command & 0x40) == 0) throw new ArgumentException("Only read commands are allowed here.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        await stream.WriteAsync(Request(command, selector), deadline.Token);
        var data = new byte[Math.Max(length, ChunkLength)];
        var seen = new SortedSet<int>();
        var raw = new List<string>();
        var total = -1; var received = 0; var badChecksums = 0;
        var buffer = new byte[inputLength];
        while (total < 0 || seen.Count < total)
        {
            int count;
            try { count = await stream.ReadAsync(buffer, deadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(command, false, "timeout", data[..Math.Min(received, length)], total, seen.Count, badChecksums, raw); }
            if (count < ReportLength || buffer[0] != ReportId) continue;
            raw.Add(Convert.ToHexString(buffer, 0, ReportLength));
            if ((buffer[1] & 0x7f) != command) continue;
            if ((buffer[2] & 0x80) != 0) return new(command, false, "device reported an error", [], total, seen.Count, badChecksums, raw);
            if (Checksum(buffer) != buffer[ReportLength - 1]) badChecksums++;
            total = buffer[2] & 0x7f;
            int index = buffer[3], size = Math.Min(buffer[4] & 0x0f, ChunkLength);
            if (total == 0 || index >= total || !seen.Add(index)) continue;
            var offset = index * ChunkLength;
            if (offset + size > data.Length) Array.Resize(ref data, offset + size);
            buffer.AsSpan(5, size).CopyTo(data.AsSpan(offset));
            received = Math.Max(received, offset + size);
        }
        return new(command, true, null, data[..Math.Min(received, Math.Max(length, received))], total, seen.Count, badChecksums, raw);
    }

    // Writes Sora understands and can read back to verify: key map (1), lighting (4), effect palette (9).
    private static readonly byte[] VerifiedWrites = [0x01, 0x04, 0x09];

    /// <summary>
    /// Sends a block in 14-byte packets; each packet must be acknowledged with its own index (OemDrv 0x496100 / 0x495c80).
    /// Returns a log of every packet sent and acknowledgement received.
    /// </summary>
    public async Task<IReadOnlyList<string>> WriteAsync(byte command, byte selector, byte[] data, CancellationToken token = default)
    {
        if (!VerifiedWrites.Contains(command)) throw new ArgumentException("Sora does not send this receiver command.");
        if (KreoKeyboard.FindConflict() is { } conflict) throw new IOException(conflict);
        var log = new List<string>();
        var packets = (data.Length + ChunkLength - 1) / ChunkLength;
        var buffer = new byte[inputLength];
        for (var index = 0; index < packets; index++)
        {
            var size = Math.Min(ChunkLength, data.Length - index * ChunkLength);
            var packet = new byte[ReportLength];
            packet[0] = ReportId; packet[1] = command; packet[2] = (byte)packets; packet[3] = (byte)index; packet[4] = (byte)((selector << 4) | size);
            data.AsSpan(index * ChunkLength, size).CopyTo(packet.AsSpan(5));
            packet[^1] = Checksum(packet);
            var acknowledged = false;
            for (var attempt = 0; attempt < 5 && !acknowledged; attempt++)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                await stream.WriteAsync(packet, deadline.Token);
                log.Add("> " + Convert.ToHexString(packet));
                try
                {
                    while (true)
                    {
                        var count = await stream.ReadAsync(buffer, deadline.Token);
                        if (count < ReportLength || buffer[0] != ReportId || (buffer[1] & 0x7f) != command) continue;
                        log.Add("< " + Convert.ToHexString(buffer, 0, ReportLength));
                        if ((buffer[2] & 0x80) != 0) throw new IOException($"The keyboard rejected packet {index} of command 0x{command:X2}.");
                        if (buffer[3] == index) { acknowledged = true; break; }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { log.Add($"! no acknowledgement for packet {index}, attempt {attempt + 1}"); }
            }
            if (!acknowledged) throw new IOException($"The keyboard did not acknowledge packet {index} of command 0x{command:X2}. Settings may be partly written; restore the backup.");
        }
        return log;
    }

    /// <summary>
    /// Reads lighting, key map, effect palette and battery. Per-key colours cannot be read over the receiver
    /// (OemDrv sends command 6 without a reply), so Colors is a placeholder and ColorsKnown is false.
    /// </summary>
    public async Task<ReceiverSnapshot> ReadStateAsync(KreoModel model)
    {
        const string asleep = "The keyboard didn't answer through its 2.4 GHz receiver. Press a key to wake it, then refresh.";
        var timeout = TimeSpan.FromSeconds(4);
        var lighting = await ReadAsync(ReadLighting, 0, 128, timeout);
        if (!lighting.Complete || lighting.Data.Length < 128) throw new KreoReceiverTimeoutException(asleep);
        await Task.Delay(100);
        var matrix = await ReadAsync(ReadMatrix, 0, 504, timeout);
        await Task.Delay(100);
        var palette = await ReadAsync(ReadColors, 0, 490, timeout);
        await Task.Delay(100);
        var power = await ReadAsync(ReadPower, 0, 2, timeout);
        if (!matrix.Complete || matrix.Data.Length < 504 || !palette.Complete || palette.Data.Length < 420) throw new KreoReceiverTimeoutException(asleep);
        // The receiver omits the 8-byte block tails; they are completed locally and never written back over 2.4 GHz.
        var keys = new byte[512]; matrix.Data.AsSpan(0, 504).CopyTo(keys); KreoProtocol.CompleteBlock(keys);
        var effects = new byte[512]; palette.Data.AsSpan(0, Math.Min(palette.Data.Length, 504)).CopyTo(effects); KreoProtocol.CompleteBlock(effects);
        var state = new KreoState(model.Identities[0], lighting.Data[..128], keys, new byte[378], effects);
        state.Validate();
        int? battery = power.Complete && power.Data.Length > 0 && power.Data[0] <= 100 ? power.Data[0] : null;
        return new(state, battery);
    }

    /// <summary>Writes a lighting block after checking the keyboard still has the expected one, then confirms it by readback.</summary>
    public async Task<byte[]> ApplyLightingAsync(byte[] expected, byte[] desired, Action<byte[]> backup)
    {
        if (desired.Length != 128 || desired[126] != 0x5a || desired[127] != 0xa5) throw new InvalidDataException("Invalid lighting block.");
        var timeout = TimeSpan.FromSeconds(4);
        var current = await ReadAsync(ReadLighting, 0, 128, timeout);
        if (!current.Complete || current.Data.Length < 128) throw new IOException("The keyboard didn't answer through its 2.4 GHz receiver. Press a key to wake it, then try again.");
        if (!current.Data.AsSpan(0, 128).SequenceEqual(expected)) throw new IOException("Keyboard lighting changed since it was read. Refresh before applying edits.");
        backup(current.Data[..128]); // The backup must reach disk before the first write.
        await WriteAsync(0x04, 0, desired);
        await Task.Delay(200);
        var after = await ReadAsync(ReadLighting, 0, 128, timeout);
        if (!after.Complete || !after.Data.AsSpan(0, 128).SequenceEqual(desired)) throw new IOException("The keyboard did not confirm the new lighting. Refresh to see its current state; the backup is saved.");
        return after.Data[..128];
    }

    public void Dispose() => stream.Dispose();
}

public sealed record ReceiverSnapshot(KreoState State, int? BatteryPercent);

/// <summary>The receiver is present but the keyboard did not answer (usually asleep or out of range).</summary>
public sealed class KreoReceiverTimeoutException(string message) : IOException(message);

public sealed record ReceiverReply(byte Command, bool Complete, string? Error, byte[] Data, int Packets, int PacketsReceived, int BadChecksums, IReadOnlyList<string> RawReports);
