using System.Threading.Channels;
using System.Text.Json;
using Sora.Core;

internal static class KreoRecoveryChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static readonly DeviceInfo Receiver = new("scripted", "test", 0xff02, 2, 20, 0, 0x3554, 0xfa09);

    public static async Task CorruptRead()
    {
        using var transport = new ScriptedStream(request =>
        {
            var response = Reply(request[1], 1, 0, 0, [75, 1]); response[19] ^= 1;
            return [response];
        });
        using var receiver = new KreoReceiver(Receiver, transport, 20, () => null);
        var result = await receiver.ReadAsync(KreoReceiver.ReadPower, 0, 2, TimeSpan.FromMilliseconds(100));
        Require(!result.Complete && result.Data.Length == 0 && result.BadChecksums == 1,
            "A corrupt receiver payload was accepted as complete/trusted settings.");
    }

    public static async Task CorruptAcknowledgement()
    {
        using var transport = new ScriptedStream(request =>
        {
            var response = request.ToArray(); response[19] ^= 1; return [response];
        });
        using var receiver = new KreoReceiver(Receiver, transport, 20, () => null);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var accepted = false;
        try { await receiver.WriteAsync(0x04, 0, new byte[128], cancellation.Token); accepted = true; }
        catch (OperationCanceledException) { }
        Require(!accepted, "A corrupt acknowledgement advanced the receiver write.");
    }

    public static Task WiredRollback()
    {
        var before = State(); var desired = Changed(before); var actual = before.Copy(); var writes = 0; var saved = false;
        void Write(byte command, byte[] payload)
        {
            Require(saved, "A hardware write preceded the durable backup.");
            Assign(actual, command, payload);
            if (++writes == 2) throw new IOException("Injected partial USB write");
        }
        try
        {
            KreoKeyboardTransaction.Apply(before.Identity, KreoModel.All.Single(m => m.Name == "Swarm 75"), before,
                desired, () => actual.Copy(), Write, _ => saved = true, () => { });
            throw new Exception("Partial write was reported as successful.");
        }
        catch (IOException) { }
        Require(Same(actual, before), "A partial wired apply left previously written keyboard blocks changed.");
        return Task.CompletedTask;
    }

    public static async Task ReceiverFraming()
    {
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "kreo-receiver-capture.json")));
        foreach (var read in captured.RootElement.GetProperty("reads").EnumerateArray())
        {
            var command = Convert.ToByte(read.GetProperty("command").GetString()![2..], 16);
            var expected = Convert.FromHexString(read.GetProperty("hex").GetString()!);
            var reports = read.GetProperty("reports").EnumerateArray().Select(p => Convert.FromHexString(p.GetString()!)).ToArray();
            // Reorder and duplicate real reports; complete captured blocks must still reassemble exactly.
            using var stream = new ScriptedStream(_ => reports.Reverse().Take(1).Concat(reports.Reverse()).ToArray());
            using var receiver = new KreoReceiver(Receiver, stream, 20, () => null);
            var result = await receiver.ReadAsync(command, 0, expected.Length, TimeSpan.FromMilliseconds(200));
            Require(result.Complete && result.BadChecksums == 0 && result.Data.SequenceEqual(expected), "Captured receiver block did not reassemble: " + command);
        }

        var first = Reply(0x44, 2, 0, 0, new byte[14]); var last = Reply(0x44, 2, 1, 0, [1, 2]);
        var wrongCount = last.ToArray(); wrongCount[2] = 3; Rechecksum(wrongCount);
        var wrongIndex = last.ToArray(); wrongIndex[3] = 2; Rechecksum(wrongIndex);
        var oversize = last.ToArray(); oversize[4] = 15; Rechecksum(oversize);
        var shortMiddle = first.ToArray(); shortMiddle[4] = 13; Rechecksum(shortMiddle);
        var changedDuplicate = first.ToArray(); changedDuplicate[5] = 99; Rechecksum(changedDuplicate);
        var error = first.ToArray(); error[2] |= 0x80; Rechecksum(error);
        var tooMany = first.ToArray(); tooMany[2] = 127; Rechecksum(tooMany);
        foreach (var reports in new[]
        {
            new[] { first, wrongCount }, new[] { wrongIndex }, new[] { oversize }, new[] { shortMiddle },
            new[] { first, changedDuplicate }, new[] { error }, new[] { tooMany }, new[] { first }, new[] { first[..10] }
        })
        {
            using var stream = new ScriptedStream(_ => reports);
            using var receiver = new KreoReceiver(Receiver, stream, 20, () => null);
            var result = await receiver.ReadAsync(0x44, 0, 16, TimeSpan.FromMilliseconds(50));
            Require(!result.Complete && result.Data.Length == 0, "Malformed/incomplete receiver block exposed trusted data.");
        }
        using (var stream = new ScriptedStream(_ => [Reply(0x4a, 1, 0, 1, [5, 1]), Reply(0x41, 1, 0, 0, [9, 1]), Reply(0x4a, 1, 0, 0, [88, 1])]))
        using (var receiver = new KreoReceiver(Receiver, stream, 20, () => null))
        {
            var result = await receiver.ReadAsync(0x4a, 0, 2, TimeSpan.FromMilliseconds(100));
            Require(result.Complete && result.Data.SequenceEqual(new byte[] { 88, 1 }), "Wrong-command/selector reports satisfied a read.");
        }
        var attempts = 0;
        using (var stream = new ScriptedStream(_ =>
        {
            var report = Reply(0x4a, 1, 0, 0, [55, 1]); if (++attempts == 1) report[19] ^= 1; return [report];
        }))
        using (var receiver = new KreoReceiver(Receiver, stream, 20, () => null))
        {
            var result = await receiver.ReadWithRetryAsync(0x4a, 0, 2, 2);
            Require(result.Complete && attempts == 2 && result.Data[0] == 55, "Corrupt block was not retried as a new complete read.");
        }
    }

    public static async Task AcknowledgementCorrelation()
    {
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "kreo-receiver-capture.json")));
        var reports = captured.RootElement.GetProperty("writeLighting").EnumerateArray().Select(p => Convert.FromHexString(p.GetString()!)).ToArray();
        var original = reports.SelectMany(p => p.AsSpan(5, p[4] & 15).ToArray()).ToArray();
        using (var stream = new ScriptedStream(packet =>
        {
            Require(packet.SequenceEqual(reports[packet[3]]), "Lighting write bytes differ from the captured valid transmission.");
            var staleError = packet.ToArray(); staleError[2] |= 0x80; staleError[4] ^= 0x10; Rechecksum(staleError);
            return [staleError, packet];
        }))
        using (var receiver = new KreoReceiver(Receiver, stream, 20, () => null))
            await receiver.WriteAsync(4, 0, original);

        foreach (var mutate in new Action<byte[]>[]
        {
            p => p[2]++, p => p[3]++, p => p[4] ^= 0x10, p => p[4]--, p => p[5] ^= 1
        })
        {
            using var stream = new ScriptedStream(packet => { var stale = packet.ToArray(); mutate(stale); Rechecksum(stale); return [stale]; });
            using var receiver = new KreoReceiver(Receiver, stream, 20, () => null);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            try { await receiver.WriteAsync(4, 0, original, cancellation.Token); throw new Exception("Wrong echo accepted as an acknowledgement."); }
            catch (OperationCanceledException) { }
            Require(stream.Writes.Count == 1, "An invalid acknowledgement advanced to the next packet.");
        }
    }

    public static Task WiredFailureStages()
    {
        var model = KreoModel.All.Single(m => m.Name == "Swarm 75");
        foreach (var failAt in Enumerable.Range(1, 4))
        foreach (var afterMutation in new[] { false, true })
        {
            var before = State(); var actual = before.Copy(); var desired = Changed(before); var writes = 0; var saved = false;
            void Write(byte command, byte[] bytes)
            {
                Require(saved, "Write before backup"); var fail = ++writes == failAt;
                if (fail && !afterMutation) throw new IOException("Write failed before transfer");
                Assign(actual, command, bytes);
                if (fail) throw new System.ComponentModel.Win32Exception(31, "Transfer completed before error");
            }
            try { KreoKeyboardTransaction.Apply(before.Identity, model, before, desired, () => actual.Copy(), Write, _ => saved = true, () => { }); throw new Exception("Failed write reported success"); }
            catch (IOException error) { Require(error.Message.Contains("restored and read back"), "Successful rollback was not verified/reported."); }
            Require(Same(actual, before), "Wired failure stage did not restore originals: " + failAt);
            Require(Same(desired, Changed(before)), "Apply mutated the caller's draft.");
        }
        // A readback failure must restore all attempted blocks, including the last one.
        {
            var before = State(); var actual = before.Copy(); var reads = 0;
            KreoState Read() { if (++reads == 2) throw new IOException("Readback failed"); return actual.Copy(); }
            try { KreoKeyboardTransaction.Apply(before.Identity, model, before, Changed(before), Read, (c, b) => Assign(actual, c, b), _ => { }, () => { }); throw new Exception("Missing readback reported success"); }
            catch (IOException) { }
            Require(Same(actual, before), "Readback failure did not restore all blocks.");
        }
        // Backup failure prevents all writes; restoration failure remains explicit and retains the original error.
        {
            var before = State(); var writes = 0;
            try { KreoKeyboardTransaction.Apply(before.Identity, model, before, Changed(before), before.Copy, (_, _) => writes++, _ => throw new IOException("Disk full"), () => { }); }
            catch (IOException) { }
            Require(writes == 0, "Backup failure allowed hardware writes.");
            try
            {
                KreoKeyboardTransaction.Apply(before.Identity, model, before, Changed(before), before.Copy, (_, _) => throw new IOException("Disconnected"), _ => { }, () => { });
                throw new Exception("Restore failure ignored");
            }
            catch (IOException error) { Require(error.Message.Contains("could not be verified") && error.InnerException is AggregateException, "Restore failure lost its evidence."); }
        }
        return Task.CompletedTask;
    }

    public static async Task ReceiverApplyRecovery()
    {
        var model = KreoModel.All.Single(m => m.Name == "Swarm 75");
        foreach (var failedCommand in new byte[] { 0x09, 0x01, 0x04 })
        foreach (var failureKind in new[] { "checksum", "short", "device error" })
        {
            var before = State(); var desired = before.Copy();
            desired.Lighting[10] = 1; desired.EffectPalette![21] = 93;
            desired.Keys[model.Keys.First(k => k.VirtualKey == 0x41).Index * 4 + 3] = 5;
            var blocks = new Dictionary<byte, byte[]>
            {
                [0x44] = before.Lighting.ToArray(), [0x41] = before.Keys[..504], [0x49] = before.EffectPalette![..490]
            };
            var originals = blocks.ToDictionary(p => p.Key, p => p.Value.ToArray());
            var backupSaved = false; var failedWrites = 0;
            IEnumerable<byte[]> Respond(byte[] request)
            {
                var command = request[1];
                if (command < 0x40)
                {
                    Require(backupSaved, "Receiver hardware write preceded durable backup.");
                    var block = blocks[(byte)(command | 0x40)];
                    request.AsSpan(5, request[4] & 15).CopyTo(block.AsSpan(request[3] * 14));
                    if (command == failedCommand && request[3] == request[2] - 1) failedWrites++;
                    return [request];
                }
                var data = blocks[command];
                var reject = command == (failedCommand | 0x40) && failedWrites == 1;
                if (reject && failureKind == "short") data = data[..2];
                var count = (byte)((data.Length + 13) / 14);
                var reports = Enumerable.Range(0, count).Select(i => Reply(command, count, (byte)i, 0, data.Skip(i * 14).Take(14).ToArray())).ToArray();
                if (reject && failureKind == "checksum") reports[0][19] ^= 1;
                if (reject && failureKind == "device error") { reports[0][2] |= 0x80; Rechecksum(reports[0]); }
                return reports;
            }
            using var stream = new ScriptedStream(Respond);
            using var receiver = new KreoReceiver(Receiver, stream, 20, () => null);
            var trace = new List<ReceiverTrace>();
            receiver.DiagnosticTrace += trace.Add;
            // A broken diagnostic observer must not prevent any write or restoration.
            receiver.DiagnosticTrace += _ => throw new IOException("Trace disk unavailable");
            try
            {
                await receiver.ApplyAsync(before, desired, model, _ => backupSaved = true);
                throw new Exception("Receiver apply accepted rejected/short readback.");
            }
            catch (IOException error)
            {
                Require(error is not KreoReceiverTimeoutException && !error.Message.Contains("wake"), "A protocol error was mislabeled as sleeping.");
                Require(error.Message.Contains($"0x{failedCommand | 0x40:X2}") && error.Message.Contains("previous settings were restored"), "Receiver failure lost command or recovery result.");
            }
            Require(blocks.All(p => p.Value.SequenceEqual(originals[p.Key])), "Receiver rollback left a changed block.");
            Require(trace.Any(t => t.Stage == "apply.verify" && t.Command == (failedCommand | 0x40)), "Trace omitted failing readback step.");
            Require(trace.Any(t => t.Stage == "read.reply" && t.Reply is { } reply && (!reply.Complete || reply.Data.Length < t.ExpectedLength)), "Trace omitted failed/short reply evidence.");
            Require(trace.Any(t => t.Stage == "rollback.verified"), "Trace omitted verified rollback.");
        }
    }

    public static async Task ReceiverBackupFailure()
    {
        var before = State();
        var blocks = new Dictionary<byte, byte[]> { [0x44] = before.Lighting, [0x41] = before.Keys[..504], [0x49] = before.EffectPalette![..490] };
        using var stream = new ScriptedStream(request =>
        {
            Require(request[1] >= 0x40, "Failed backup allowed a receiver write.");
            var data = blocks[request[1]]; var count = (byte)((data.Length + 13) / 14);
            return Enumerable.Range(0, count).Select(i => Reply(request[1], count, (byte)i, 0, data.Skip(i * 14).Take(14).ToArray())).ToArray();
        });
        using var receiver = new KreoReceiver(Receiver, stream, 20, () => null);
        var desired = before.Copy(); desired.Lighting[10] = 1;
        try
        {
            await receiver.ApplyAsync(before, desired, KreoModel.All.Single(m => m.Name == "Swarm 75"), _ => throw new IOException("Backup disk full"));
            throw new Exception("Receiver ignored failed backup.");
        }
        catch (IOException error) { Require(error.Message.Contains("Backup disk full"), "Backup failure was hidden."); }
        Require(stream.Writes.All(p => p[1] >= 0x40), "Receiver wrote despite failed backup.");
    }

    private static void Rechecksum(byte[] report) => report[19] = KreoReceiver.Checksum(report);

    internal static KreoState State()
    {
        var model = KreoModel.All.Single(m => m.Name == "Swarm 75");
        var lighting = new byte[128]; lighting[126] = 0x5a; lighting[127] = 0xa5;
        var keys = new byte[512]; KreoProtocol.CompleteBlock(keys);
        var palette = new byte[512]; KreoProtocol.CompleteBlock(palette);
        return new(model.Identities[0], lighting, keys, new byte[378], palette);
    }
    internal static KreoState Changed(KreoState before)
    {
        var desired = before.Copy(); desired.Lighting[10] = 1; desired.Colors[0] = 42; desired.EffectPalette![21] = 93;
        var key = KreoModel.All.Single(m => m.Name == "Swarm 75").Keys.First(k => k.VirtualKey == 0x41);
        desired.Keys[key.Index * 4 + 3] = 5;
        return desired;
    }
    internal static void Assign(KreoState state, byte command, byte[] payload)
    {
        var target = command switch { 0x0a => state.EffectPalette!, 6 => state.Colors, 3 => state.Keys, 4 => state.Lighting, _ => throw new Exception("Unknown block") };
        payload.CopyTo(target, 0);
    }
    internal static bool Same(KreoState a, KreoState b) => a.Identity == b.Identity && a.Lighting.SequenceEqual(b.Lighting) &&
        a.Keys.SequenceEqual(b.Keys) && a.Colors.SequenceEqual(b.Colors) && a.EffectPalette!.SequenceEqual(b.EffectPalette!);

    internal static byte[] Reply(byte command, byte count, byte index, byte selector, byte[] data)
    {
        var report = new byte[20]; report[0] = 0x13; report[1] = command; report[2] = count; report[3] = index;
        report[4] = (byte)((selector << 4) | data.Length); data.CopyTo(report, 5); report[19] = KreoReceiver.Checksum(report);
        return report;
    }

    internal sealed class ScriptedStream(Func<byte[], IEnumerable<byte[]>> respond) : Stream
    {
        private readonly Channel<byte[]> incoming = Channel.CreateUnbounded<byte[]>();
        public List<byte[]> Writes { get; } = [];
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); var packet = buffer.ToArray(); Writes.Add(packet);
            foreach (var response in respond(packet)) incoming.Writer.TryWrite(response);
            return ValueTask.CompletedTask;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var response = await incoming.Reader.ReadAsync(cancellationToken); response.CopyTo(buffer); return response.Length;
        }
        public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
