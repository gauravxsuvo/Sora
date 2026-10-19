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
    private readonly Stream stream;
    private readonly int inputLength;
    private readonly Func<string?> conflictCheck = KreoKeyboard.FindConflict;
    private readonly SemaphoreSlim ioGate = new(1, 1);
    private const int MaxPayloadLength = 512;
    public DeviceInfo Device { get; }
    /// <summary>Optional bounded diagnostic evidence. Observer failures never interrupt hardware recovery.</summary>
    public event Action<ReceiverTrace>? DiagnosticTrace;

    private void Trace(string stage, byte command, byte selector, int expectedLength, ReceiverReply? reply = null,
        IReadOnlyList<string>? reports = null, string? error = null, int? attempt = null)
    {
        try { DiagnosticTrace?.Invoke(new(DateTimeOffset.UtcNow, stage, command, selector, expectedLength, reply, reports, error, attempt)); }
        catch (Exception observerError) { System.Diagnostics.Debug.WriteLine("Receiver trace observer: " + observerError); }
    }

    private static IOException ReadFailure(ReceiverReply reply, int expectedLength, string stage)
    {
        var detail = $"Receiver {stage}, command 0x{reply.Command:X2}: {reply.Error ?? "short reply"}; " +
            $"expected {expectedLength} bytes, received {reply.Data.Length}, packets {reply.PacketsReceived}/{reply.Packets}, bad checksums {reply.BadChecksums}.";
        return reply.Error == "timeout" && reply.PacketsReceived == 0
            ? new KreoReceiverTimeoutException(detail + " Press a key to wake the keyboard, then try again.")
            : new IOException(detail);
    }

    // Tests exercise the real request/reassembly code through a scripted byte transport.
    internal KreoReceiver(DeviceInfo device, Stream transport, int inputReportLength, Func<string?> conflictCheck)
    {
        Device = device;
        stream = transport;
        inputLength = inputReportLength;
        this.conflictCheck = conflictCheck;
    }

    public static DeviceInfo? Find() => KreoKeyboard.Descriptors().FirstOrDefault(d => d.VendorId == 0x3554 && d.ProductId == 0xfa09 && d.UsagePage == 0xff02 && d.Usage == 2 && d.OutputReportLength == ReportLength);

    public KreoReceiver(DeviceInfo device)
    {
        Device = device;
        var handle = HidDevice.Native.CreateFile(device.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the Kreo receiver's configuration channel."); }
        try
        {
            var attributes = new HidDevice.Native.HidAttributes { Size = Marshal.SizeOf<HidDevice.Native.HidAttributes>() };
            if (!HidDevice.Native.HidD_GetAttributes(handle, ref attributes) || attributes.VendorId != 0x3554 || attributes.ProductId != 0xfa09)
                throw new IOException("The selected device no longer matches the Kreo receiver identity.");
            var caps = HidDevice.GetCaps(handle);
            if (caps.UsagePage != 0xff02 || caps.Usage != 2 || caps.OutputReportByteLength != ReportLength || caps.InputReportByteLength is < ReportLength or > 64)
                throw new IOException("Unexpected Kreo receiver descriptor.");
            inputLength = caps.InputReportByteLength;
            // A 512-byte reply is 37 reports; the default queue of 32 drops the tail.
            if (!HidDevice.Native.HidD_SetNumInputBuffers(handle, 256))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot reserve the receiver report queue required for a complete settings read.");
            stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
        }
        catch { handle.Dispose(); throw; }
    }

    public static byte Checksum(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < ReportLength) throw new ArgumentException("A receiver report must contain 20 bytes.", nameof(packet));
        byte sum = 0;
        for (var i = 0; i < ReportLength - 1; i++) sum += packet[i];
        return sum;
    }

    public static byte[] Request(byte command, byte selector)
    {
        if (command is < 0x40 or > 0x7f || selector > 15) throw new ArgumentOutOfRangeException(nameof(command));
        var packet = new byte[ReportLength];
        packet[0] = ReportId; packet[1] = command; packet[2] = 1; packet[3] = 0; packet[4] = (byte)(selector << 4);
        packet[^1] = Checksum(packet);
        return packet;
    }

    /// <summary>Sends one read command and reassembles the reply. Read commands do not change keyboard settings.</summary>
    public async Task<ReceiverReply> ReadAsync(byte command, byte selector, int length, TimeSpan timeout, CancellationToken token = default)
    {
        if (length is < 1 or > MaxPayloadLength) throw new ArgumentOutOfRangeException(nameof(length));
        var request = Request(command, selector);
        await ioGate.WaitAsync(token);
        try
        {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        await stream.WriteAsync(request, deadline.Token);
        var data = new byte[MaxPayloadLength];
        var seen = new SortedSet<int>();
        var raw = new List<string>();
        var total = -1; var received = 0; var badChecksums = 0;
        var buffer = new byte[inputLength];
        var frames = 0;
        ReceiverReply Failure(string error) => new(command, false, error, [], total, seen.Count, badChecksums, raw);
        while (total < 0 || seen.Count < total)
        {
            int count;
            try { count = await stream.ReadAsync(buffer, deadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Failure("timeout"); }
            if (count == 0) return Failure("receiver transport closed");
            if (++frames > 512) return Failure("too many unrelated or duplicate receiver reports");
            if (count < ReportLength || buffer[0] != ReportId) continue;
            if (raw.Count < 128) raw.Add(Convert.ToHexString(buffer, 0, ReportLength));
            if (buffer[1] != command || buffer[4] >> 4 != selector) continue;
            if (Checksum(buffer) != buffer[ReportLength - 1]) { badChecksums++; return Failure("invalid receiver checksum"); }
            if ((buffer[2] & 0x80) != 0) return Failure("device reported an error");
            var packetCount = buffer[2];
            int index = buffer[3], size = buffer[4] & 0x0f;
            if (packetCount is < 1 or > (MaxPayloadLength + ChunkLength - 1) / ChunkLength || index >= packetCount || size is < 1 or > ChunkLength ||
                (index < packetCount - 1 && size != ChunkLength) || index * ChunkLength + size > MaxPayloadLength)
                return Failure("invalid receiver packet bounds");
            if (total != -1 && packetCount != total) return Failure("receiver packet count changed during a read");
            total = packetCount;
            var offset = index * ChunkLength;
            if (!seen.Add(index))
            {
                if (!buffer.AsSpan(5, size).SequenceEqual(data.AsSpan(offset, size)) || (index == total - 1 && offset + size != received))
                    return Failure("conflicting duplicate receiver packet");
                continue;
            }
            buffer.AsSpan(5, size).CopyTo(data.AsSpan(offset));
            received = Math.Max(received, offset + size);
        }
        return new(command, true, null, data[..received], total, seen.Count, badChecksums, raw);
        }
        finally { ioGate.Release(); }
    }

    /// <summary>
    /// A read that is sent again when it goes unanswered. The receiver drops requests while the keyboard's radio is idle or it is
    /// busy answering another program (replies reach every open handle), and then answers the next one within ~100 ms.
    /// </summary>
    public async Task<ReceiverReply> ReadWithRetryAsync(byte command, byte selector, int length, int attempts = 4, CancellationToken token = default)
    {
        if (attempts is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(attempts));
        ReceiverReply reply = new(command, false, "not sent", [], -1, 0, 0, []);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try { reply = await ReadAsync(command, selector, length, TimeSpan.FromMilliseconds(1200), token); }
            catch (Exception error)
            {
                Trace("read.exception", command, selector, length, error: error.ToString(), attempt: attempt + 1);
                throw;
            }
            Trace("read.reply", command, selector, length, reply, attempt: attempt + 1);
            if (reply.Complete && reply.Data.Length >= length) return reply;
            if (attempt + 1 < attempts) await Task.Delay(150, token);
        }
        return reply;
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
        var expectedLength = command switch { 0x01 => MatrixBytes, 0x04 => 128, 0x09 => PaletteBytes, _ => 0 };
        if (selector > 15 || data.Length != expectedLength) throw new ArgumentException("Unexpected receiver block size or selector.");
        if (conflictCheck() is { } conflict) throw new IOException(conflict);
        await ioGate.WaitAsync(token);
        var log = new List<string>();
        try
        {
        Trace("write.start", command, selector, data.Length);
        var packets = (data.Length + ChunkLength - 1) / ChunkLength;
        var buffer = new byte[inputLength];
        for (var index = 0; index < packets; index++)
        {
            if (conflictCheck() is { } currentConflict) throw new IOException(currentConflict);
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
                    var receivedReports = 0;
                    while (true)
                    {
                        var count = await stream.ReadAsync(buffer, deadline.Token);
                        if (count == 0) throw new IOException("The receiver transport closed during a write.");
                        if (++receivedReports > 512) throw new IOException("Too many unrelated receiver acknowledgements.");
                        if (count < ReportLength || buffer[0] != ReportId || buffer[1] != command) continue;
                        if (log.Count < 256) log.Add("< " + Convert.ToHexString(buffer, 0, ReportLength));
                        if (Checksum(buffer) != buffer[ReportLength - 1]) continue;
                        if ((buffer[2] & 0x80) != 0)
                        {
                            if ((buffer[2] & 0x7f) == packets && buffer[3] == index && buffer[4] >> 4 == selector)
                                throw new IOException($"The keyboard rejected packet {index} of command 0x{command:X2}.");
                            continue;
                        }
                        // Captured KB Hub acknowledgements echo the complete transmitted report.
                        if (buffer.AsSpan(0, ReportLength).SequenceEqual(packet)) { acknowledged = true; break; }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { log.Add($"! no acknowledgement for packet {index}, attempt {attempt + 1}"); }
            }
            if (!acknowledged) throw new IOException($"The keyboard did not acknowledge packet {index} of command 0x{command:X2}. Settings may be partly written; restore the backup.");
        }
        Trace("write.complete", command, selector, data.Length, reports: log);
        return log;
        }
        catch (Exception error)
        {
            Trace("write.failed", command, selector, data.Length, reports: log, error: error.ToString());
            throw;
        }
        finally { ioGate.Release(); }
    }

    /// <summary>
    /// Reads lighting, key map, effect palette and battery. Per-key colours cannot be read over the receiver
    /// (OemDrv sends command 6 without a reply), so Colors is a placeholder and ColorsKnown is false.
    /// </summary>
    public async Task<ReceiverSnapshot> ReadStateAsync(KreoModel model)
    {
        var lighting = await ReadWithRetryAsync(ReadLighting, 0, 128);
        if (!lighting.Complete || lighting.Data.Length < 128) throw ReadFailure(lighting, 128, "snapshot lighting");
        await Task.Delay(100);
        var matrix = await ReadWithRetryAsync(ReadMatrix, 0, 504);
        await Task.Delay(100);
        var palette = await ReadWithRetryAsync(ReadColors, 0, 420);
        await Task.Delay(100);
        var power = await ReadWithRetryAsync(ReadPower, 0, 2);
        if (!matrix.Complete || matrix.Data.Length < MatrixBytes) throw ReadFailure(matrix, MatrixBytes, "snapshot key map");
        if (!palette.Complete || palette.Data.Length < 420) throw ReadFailure(palette, 420, "snapshot palette");
        // The receiver omits the 8-byte block tails; they are completed locally and never written back over 2.4 GHz.
        var keys = new byte[512]; matrix.Data.AsSpan(0, 504).CopyTo(keys); KreoProtocol.CompleteBlock(keys);
        var effects = new byte[512]; palette.Data.AsSpan(0, Math.Min(palette.Data.Length, 504)).CopyTo(effects); KreoProtocol.CompleteBlock(effects);
        var state = new KreoState(model.Identities[0], lighting.Data[..128], keys, new byte[378], effects);
        state.Validate();
        int? battery = power.Complete && power.Data.Length > 0 && power.Data[0] <= 100 ? power.Data[0] : null;
        return new(state, battery);
    }

    // Receiver block sizes: the wired blocks without their 8-byte tails (keys) or 22-byte tail (palette).
    public const int MatrixBytes = 504, PaletteBytes = 490;

    /// <summary>
    /// Saves lighting (04), key map (01) and effect palette (09) through the receiver. The keyboard must still hold the
    /// expected blocks; the originals are backed up before the first write; every written block is read back, and on any
    /// mismatch the originals are written back before the error is reported. Per-key colours are not part of the receiver
    /// protocol (KB Hub's CDev3632 has no such command) and are never sent.
    /// </summary>
    public async Task<KreoState> ApplyAsync(KreoState expected, KreoState desired, KreoModel model, Action<ReceiverBackup> backup)
    {
        expected.Validate();
        desired.Validate();
        if (expected.Identity != desired.Identity || !model.Identities.Contains(expected.Identity))
            throw new InvalidDataException("This receiver profile belongs to another keyboard model.");
        if (!desired.Colors.SequenceEqual(expected.Colors)) throw new InvalidOperationException("Per-key colors can't be saved through the 2.4 GHz receiver.");
        async Task<byte[]> Read(byte command, int length, string stage)
        {
            Trace(stage, command, 0, length);
            var reply = await ReadWithRetryAsync(command, 0, length);
            if (!reply.Complete || reply.Data.Length < length) throw ReadFailure(reply, length, stage);
            return reply.Data[..length];
        }
        var lighting = await Read(ReadLighting, 128, "apply.original.lighting");
        var keys = await Read(ReadMatrix, MatrixBytes, "apply.original.keys");
        var palette = await Read(ReadColors, PaletteBytes, "apply.original.palette");
        if (!lighting.SequenceEqual(expected.Lighting) || !keys.SequenceEqual(expected.Keys.AsSpan(0, MatrixBytes).ToArray()) || !palette.AsSpan(0, 420).SequenceEqual(expected.EffectPalette!.AsSpan(0, 420)))
            throw new IOException("Keyboard settings changed since they were read. Refresh before applying edits.");
        foreach (var key in model.Keys.Where(k => k.VirtualKey == 0xfa))
            if (!keys.AsSpan(key.Index * 4, 4).SequenceEqual(desired.Keys.AsSpan(key.Index * 4, 4)))
                throw new IOException("These edits change the Fn key. Sora preserves its layer function.");
        backup(new ReceiverBackup(model.Name, DateTimeOffset.Now, Convert.ToHexString(lighting), Convert.ToHexString(keys), Convert.ToHexString(palette)));

        var newPalette = palette.ToArray(); desired.EffectPalette!.AsSpan(0, 420).CopyTo(newPalette);
        var newKeys = desired.Keys[..MatrixBytes];
        var plan = new List<(byte Write, byte Read, byte[] Original, byte[] Desired)>();
        if (!newPalette.SequenceEqual(palette)) plan.Add((0x09, ReadColors, palette, newPalette));
        if (!newKeys.SequenceEqual(keys)) plan.Add((0x01, ReadMatrix, keys, newKeys));
        if (!desired.Lighting.SequenceEqual(lighting)) plan.Add((0x04, ReadLighting, lighting, desired.Lighting));
        var written = new List<(byte Write, byte Read, byte[] Original, byte[] Desired)>();
        try
        {
            foreach (var step in plan)
            {
                written.Add(step);
                Trace("apply.write", step.Write, 0, step.Desired.Length);
                await WriteAsync(step.Write, 0, step.Desired);
                await Task.Delay(200);
                if (!(await Read(step.Read, step.Desired.Length, "apply.verify")).SequenceEqual(step.Desired))
                    throw new IOException($"The keyboard did not confirm command 0x{step.Write:X2}.");
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            Trace("apply.failed", 0, 0, 0, error: error.ToString());
            // Put back everything already sent, newest first, then report the original problem.
            var restored = true;
            foreach (var step in Enumerable.Reverse(written))
            {
                try
                {
                    Trace("rollback.write", step.Write, 0, step.Original.Length);
                    await WriteAsync(step.Write, 0, step.Original);
                    await Task.Delay(200);
                    restored &= (await Read(step.Read, step.Original.Length, "rollback.verify")).SequenceEqual(step.Original);
                }
                catch (Exception restoreError) when (restoreError is IOException or OperationCanceledException)
                {
                    restored = false;
                    Trace("rollback.failed", step.Write, 0, step.Original.Length, error: restoreError.ToString());
                }
            }
            Trace(restored ? "rollback.verified" : "rollback.unverified", 0, 0, 0);
            throw new IOException(error.Message + (restored ? " The previous settings were restored." : " Restoring the previous settings also failed; the backup is saved in data\\keyboard."), error);
        }
        var result = desired.Copy();
        newPalette.AsSpan(0, Math.Min(newPalette.Length, 504)).CopyTo(result.EffectPalette!);
        return result;
    }

    public void Dispose() => stream.Dispose();
}

public sealed record ReceiverSnapshot(KreoState State, int? BatteryPercent);
public sealed record ReceiverBackup(string Model, DateTimeOffset Saved, string Lighting, string Keys, string Palette);

/// <summary>The receiver is present but the keyboard did not answer (usually asleep or out of range).</summary>
public sealed class KreoReceiverTimeoutException(string message) : IOException(message);

public sealed record ReceiverReply(byte Command, bool Complete, string? Error, byte[] Data, int Packets, int PacketsReceived, int BadChecksums, IReadOnlyList<string> RawReports);

public sealed record ReceiverTrace(DateTimeOffset Time, string Stage, byte Command, byte Selector, int ExpectedLength,
    ReceiverReply? Reply, IReadOnlyList<string>? Reports, string? Error, int? Attempt);
