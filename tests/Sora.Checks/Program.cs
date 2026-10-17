using System.Buffers.Binary;
using LibreHardwareMonitor.Hardware;
using System.Text.Json;
using Sora.Core;

if (args.Contains("--keyboard-inspect-extended"))
{
    using var keyboard = new KreoKeyboard(KreoKeyboard.Enumerate().Single());
    Console.WriteLine(JsonSerializer.Serialize(new { keyboard.Identity, state = keyboard.ReadState(), fullKeys = keyboard.ReadKeyBank(), effectPalette = keyboard.ReadEffectPalette() }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Contains("--keyboard-wireless-verify-blocks"))
{
    // Reversible receiver writes: one effect-palette colour (cmd 9) and F12 -> F11 (cmd 1), each read back and restored.
    var outputIndex = Array.IndexOf(args, "--output");
    var output = Path.GetFullPath(outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : "kreo-wireless-blocks.json");
    using var receiver = new KreoReceiver(KreoReceiver.Find() ?? throw new IOException("No Kreo receiver found."));
    var timeout = TimeSpan.FromSeconds(4);
    var palette = await receiver.ReadAsync(KreoReceiver.ReadColors, 0, 490, timeout);
    var matrix = await receiver.ReadAsync(KreoReceiver.ReadMatrix, 0, 504, timeout);
    if (!palette.Complete || palette.Data.Length != 490 || !matrix.Complete || matrix.Data.Length != 504) throw new IOException("Could not read complete palette and key map; nothing was written.");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output + ".backup.json", JsonSerializer.Serialize(new { palette = Convert.ToHexString(palette.Data), matrix = Convert.ToHexString(matrix.Data) }));
    var swarm = KreoModel.All.Single(m => m.Name == "Swarm 75");
    var confirmed = new List<string>(); var problems = new List<string>(); var logs = new Dictionary<string, IReadOnlyList<string>>();
    async Task Trial(string name, byte write, byte read, byte[] original, byte[] changed)
    {
        try
        {
            logs[name] = await receiver.WriteAsync(write, 0, changed);
            await Task.Delay(300);
            var after = await receiver.ReadAsync(read, 0, original.Length, timeout);
            if (!after.Complete || !after.Data.SequenceEqual(changed)) throw new IOException(name + " readback mismatch: " + Convert.ToHexString(after.Data));
            confirmed.Add(name + ": readback matches all " + changed.Length + " bytes");
        }
        catch (Exception error) { problems.Add(name + ": " + error.Message); }
        finally
        {
            try
            {
                logs[name + " restore"] = await receiver.WriteAsync(write, 0, original);
                await Task.Delay(300);
                var restored = await receiver.ReadAsync(read, 0, original.Length, timeout);
                if (!restored.Complete || !restored.Data.SequenceEqual(original)) throw new IOException("restore readback mismatch");
                confirmed.Add(name + ": original restored and read back");
            }
            catch (Exception error) { problems.Add(name + " restore: " + error.Message); }
        }
    }
    var newPalette = palette.Data.ToArray();
    newPalette[21] ^= 0x10; // Static effect, first colour, red channel nudged.
    await Trial("Effect palette", 0x09, KreoReceiver.ReadColors, palette.Data, newPalette);
    var newMatrix = matrix.Data.ToArray();
    var f12 = swarm.Keys.Single(k => k.Label == "F12"); var f11 = swarm.Keys.Single(k => k.Label == "F11");
    matrix.Data.AsSpan(f11.Index * 4, 4).CopyTo(newMatrix.AsSpan(f12.Index * 4));
    await Trial("Key map F12 -> F11", 0x01, KreoReceiver.ReadMatrix, matrix.Data, newMatrix);
    File.WriteAllText(output, JsonSerializer.Serialize(new { receiver.Device, confirmed, failures = problems, logs }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new { confirmed, failures = problems }, new JsonSerializerOptions { WriteIndented = true }));
    return problems.Count == 0 ? 0 : 1;
}

if (args.Contains("--keyboard-wireless-verify-lighting"))
{
    // One reversible write over the 2.4 GHz receiver: dim the current effect by one step, read back, restore, read back.
    var outputIndex = Array.IndexOf(args, "--output");
    var output = Path.GetFullPath(outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : "kreo-wireless-lighting.json");
    using var receiver = new KreoReceiver(KreoReceiver.Find() ?? throw new IOException("No Kreo receiver found."));
    var timeout = TimeSpan.FromSeconds(4);
    var before = await receiver.ReadAsync(KreoReceiver.ReadLighting, 0, 128, timeout);
    if (!before.Complete || before.Data.Length != 128 || before.Data[126] != 0x5a || before.Data[127] != 0xa5) throw new IOException("Could not read a complete lighting block; nothing was written.");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output + ".backup.json", JsonSerializer.Serialize(new { lighting = Convert.ToHexString(before.Data) }));
    var effect = before.Data[10];
    if (effect is 0 or > 18) throw new IOException($"Current effect {effect} has no brightness slot; nothing was written.");
    var changed = before.Data.ToArray();
    var slot = 57 + 2 * effect;
    var brightness = changed[slot] >> 4;
    changed[slot] = (byte)(((brightness > 0 ? brightness - 1 : 1) << 4) | (changed[slot] & 15));
    string? failure = null, restoreFailure = null;
    var confirmed = new List<string>();
    IReadOnlyList<string> writeLog = [], restoreLog = [];
    try
    {
        writeLog = await receiver.WriteAsync(0x04, 0, changed);
        await Task.Delay(300);
        var after = await receiver.ReadAsync(KreoReceiver.ReadLighting, 0, 128, timeout);
        if (!after.Complete || !after.Data.SequenceEqual(changed)) throw new IOException("Readback did not match the written lighting block: " + Convert.ToHexString(after.Data));
        confirmed.Add($"Effect {effect} brightness {brightness} -> {changed[slot] >> 4}: readback matches all 128 bytes");
        await Task.Delay(800);
    }
    catch (Exception error) { failure = error.ToString(); }
    finally
    {
        try
        {
            restoreLog = await receiver.WriteAsync(0x04, 0, before.Data);
            await Task.Delay(300);
            var restored = await receiver.ReadAsync(KreoReceiver.ReadLighting, 0, 128, timeout);
            if (!restored.Complete || !restored.Data.SequenceEqual(before.Data)) throw new IOException("Restore readback mismatch: " + Convert.ToHexString(restored.Data));
            confirmed.Add("Original lighting restored; readback matches all 128 bytes");
        }
        catch (Exception error) { restoreFailure = error.ToString(); }
    }
    File.WriteAllText(output, JsonSerializer.Serialize(new { receiver.Device, effect, confirmed, failure, restoreFailure, writeLog, restoreLog }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new { confirmed, failure, restoreFailure }, new JsonSerializerOptions { WriteIndented = true }));
    return failure == null && restoreFailure == null ? 0 : 1;
}

if (args.Contains("--keyboard-wireless-probe"))
{
    // Read-only: sends only read commands (0x4x) over the 2.4 GHz receiver and records every reply.
    var outputIndex = Array.IndexOf(args, "--output");
    var output = Path.GetFullPath(outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : "kreo-wireless-probe.json");
    var device = KreoReceiver.Find();
    object result;
    if (device == null) result = new { error = "No Kreo receiver configuration channel found.", descriptors = KreoKeyboard.Descriptors() };
    else
    {
        using var receiver = new KreoReceiver(device);
        var timeout = TimeSpan.FromSeconds(4);
        var power = await receiver.ReadAsync(KreoReceiver.ReadPower, 0, 2, timeout);
        await Task.Delay(150);
        var lighting = await receiver.ReadAsync(KreoReceiver.ReadLighting, 0, 128, timeout);
        await Task.Delay(150);
        var matrix = await receiver.ReadAsync(KreoReceiver.ReadMatrix, 0, 512, timeout);
        await Task.Delay(150);
        var colors = await receiver.ReadAsync(KreoReceiver.ReadColors, 0, 378, timeout);
        if (!colors.Complete) { await Task.Delay(500); colors = await receiver.ReadAsync(KreoReceiver.ReadColors, 0, 378, timeout); }
        await Task.Delay(150);
        var perKey = await receiver.ReadAsync(0x46, 0, 378, timeout);
        static bool Marker(byte[] data, int at) => data.Length > at + 1 && data[at] == 0x5a && data[at + 1] == 0xa5;
        var wired = SwarmFixture();
        object Summary(ReceiverReply reply) => new { command = "0x" + reply.Command.ToString("X2"), reply.Complete, reply.Error, length = reply.Data.Length, reply.Packets, reply.PacketsReceived, reply.BadChecksums, hex = Convert.ToHexString(reply.Data), reply.RawReports };
        result = new
        {
            device,
            writesSent = "read requests only",
            power = Summary(power), lighting = Summary(lighting), matrix = Summary(matrix), colors = Summary(colors), perKey = Summary(perKey),
            checks = new
            {
                lightingMarkerAt126 = Marker(lighting.Data, 126),
                matrixMarkerAt510 = Marker(matrix.Data, 510),
                matrixMatchesWiredFixturePrefix = matrix.Data.Length >= 64 && matrix.Data.AsSpan(0, 64).SequenceEqual(wired.Keys.AsSpan(0, 64)),
                lightingEffectId = lighting.Data.Length > 10 ? lighting.Data[10] : -1,
                paletteMatchesWiredFixture = colors.Data.Length >= 420 && colors.Data.AsSpan(0, 420).SequenceEqual(wired.EffectPalette!.AsSpan(0, 420)),
                perKeyMatchesWiredFixture = perKey.Data.Length >= 378 && perKey.Data.AsSpan(0, 378).SequenceEqual(wired.Colors),
            },
        };
    }
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(File.ReadAllText(output));
    return 0;
}

if (args.Contains("--keyboard-probe"))
{
    Console.WriteLine(JsonSerializer.Serialize(new { descriptors = KreoKeyboard.Descriptors(), probes = KreoKeyboard.Probe() }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Contains("--keyboard-verify-lighting") || args.Contains("--keyboard-verify-keymap") || args.Contains("--keyboard-verify-effect-hex"))
{
    var outputIndex = Array.IndexOf(args, "--output");
    if (outputIndex < 0 || outputIndex + 1 >= args.Length) throw new ArgumentException("Supply --output PATH.");
    var output = Path.GetFullPath(args[outputIndex + 1]);
    using var keyboard = new KreoKeyboard(KreoKeyboard.Enumerate().Single());
    var before = keyboard.ReadState();
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output + ".backup.json", JsonSerializer.Serialize(before));
    string? failure = null, restoreFailure = null;
    var confirmed = new List<string>();
    var keymapTest = args.Contains("--keyboard-verify-keymap");
    try
    {
        if (args.Contains("--keyboard-verify-effect-hex"))
        {
            var changed = before.Copy();
            KreoProtocol.SetLighting(changed, keyboard.Definition, 1, 2, 0, 0);
            KreoProtocol.SetEffectColor(changed, keyboard.Definition, 1, 0, KreoProtocol.ParseHex("#12ABEF"));
            var actual = keyboard.Apply(before, changed, _ => { });
            if (!actual.EffectPalette!.AsSpan(21, 3).SequenceEqual(Convert.FromHexString("12ABEF")) || actual.Lighting[10] != 1) throw new IOException("Effect hex readback did not match.");
            confirmed.Add("Custom hex #12ABEF stored in Static effect palette; effect retained");
            var animated = actual.Copy();
            KreoProtocol.SetLighting(animated, keyboard.Definition, 2, 2, 2, 0);
            KreoProtocol.SetEffectColor(animated, keyboard.Definition, 2, 0, KreoProtocol.ParseHex("#EF32AB"));
            keyboard.Apply(actual, animated, _ => { });
            confirmed.Add("Custom hex #EF32AB stored in Breathing effect palette; animation retained");
        }
        else if (keymapTest)
        {
            var changed = before.Copy();
            var f12 = keyboard.Definition.Keys.Single(k => k.Label == "F12");
            var f11 = keyboard.Definition.Keys.Single(k => k.Label == "F11");
            KreoProtocol.Remap(changed, f12.Index, before.Keys.AsSpan(f11.Index * 4, 4).ToArray());
            keyboard.Apply(before, changed, _ => { });
            confirmed.Add("F12 temporarily assigned the F11 action, with matching keymap readback");
        }
        else
        {
            var preset = before.Copy();
            KreoProtocol.SetLighting(preset, keyboard.Definition, 1, 1, 0, 7);
            keyboard.Apply(before, preset, _ => { });
            confirmed.Add("Static effect, brightness and speed readback");
            await Task.Delay(600);
            var painted = preset.Copy();
            foreach (var key in keyboard.Definition.Keys) KreoProtocol.Paint(painted, key.Index, 118, 155, 255);
            KreoProtocol.SetLighting(painted, keyboard.Definition, 21, 4, 0, 0);
            keyboard.Apply(preset, painted, _ => { });
            confirmed.Add("Custom per-key color readback");
            await Task.Delay(600);
        }
    }
    catch (Exception error) { failure = error.ToString(); }
    finally
    {
        try { keyboard.Apply(keyboard.ReadState(), before, _ => { }); confirmed.Add("Original settings restored with matching readback"); }
        catch (Exception error) { restoreFailure = error.ToString(); }
        File.WriteAllText(output, JsonSerializer.Serialize(new { keyboard.Model, keyboard.Identity, confirmed, failure, restoreFailure, keyAssignmentsChangedDuringTest = keymapTest }, new JsonSerializerOptions { WriteIndented = true }));
    }
    Console.WriteLine(File.ReadAllText(output));
    return failure == null && restoreFailure == null ? 0 : 1;
}

var failures = 0;
var passed = 0;
void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
async Task Check(string title, Func<Task> run)
{
    try { await run(); Console.WriteLine("PASS " + title); passed++; }
    catch (Exception error) { Console.WriteLine("FAIL " + title + ": " + error); failures++; }
}
async Task Throws(Func<Task> run)
{
    try { await run(); } catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or JsonException or FormatException) { return; }
    throw new Exception("Expected rejection");
}
SensorSample Fresh() => new(42, 125, 37, 5400, DateTimeOffset.UtcNow);
KreoState SwarmFixture() => JsonSerializer.Deserialize<KreoState>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "kreo-swarm75.json")))!;
await Check("Kreo packets match recovered headers and reject unrelated commands", async () =>
{
    var request = KreoProtocol.Request(0x82, 1, 6);
    Require(request.Length == 520 && Convert.ToHexString(request.AsSpan(0, 8)) == "0682010001000600");
    var response = (byte[])request.Clone();
    Convert.FromHexString("0300000001BE").CopyTo(response, 8);
    Require(Convert.ToHexString(KreoProtocol.Payload(request, response)) == "0300000001BE");
    response[2] = 2;
    await Throws(() => { KreoProtocol.Payload(request, response); return Task.CompletedTask; });
    await Throws(() => { KreoProtocol.Request(0x0f, 0, 1); return Task.CompletedTask; });
    await Throws(() => { KreoProtocol.Request(4, 0, 513); return Task.CompletedTask; });
});
await Check("Kreo lighting edits preserve all unrelated live configuration bytes", () =>
{
    var state = SwarmFixture(); state.Validate();
    var original = state.Copy();
    var model = KreoModel.All.Single(m => m.Identities.Contains(state.Identity));
    KreoProtocol.SetLighting(state, model, 1, 2, 3, 7);
    Require(state.Lighting[9] == 0 && state.Lighting[10] == 1 && state.Lighting[58] == 3 && state.Lighting[59] == 0x27);
    Require(state.Lighting.Where((b, i) => i is not (9 or 10 or 58 or 59)).SequenceEqual(original.Lighting.Where((b, i) => i is not (9 or 10 or 58 or 59))));
    Require(state.Keys.SequenceEqual(original.Keys) && state.Colors.SequenceEqual(original.Colors));
    KreoProtocol.SetLighting(state, model, 0, 4, 4, 7);
    Require(state.Lighting[9] == 0 && state.Lighting[10] == 0);
    KreoProtocol.SetLighting(state, model, 21, 4, 4, 7);
    Require(state.Lighting[9] == 1 && state.Lighting[10] == 21);
    return Task.CompletedTask;
});
await Check("Kreo planar per-key colors and action slots match live Swarm data", () =>
{
    var state = SwarmFixture(); var original = state.Copy();
    KreoProtocol.Paint(state, 9, 12, 34, 56);
    Require(state.Colors[9] == 12 && state.Colors[135] == 34 && state.Colors[261] == 56);
    var action = state.Keys.AsSpan(9 * 4, 4).ToArray();
    Require(Convert.ToHexString(action) == "00000004"); // A at hardware position 9.
    KreoProtocol.Remap(state, 0, action);
    Require(Convert.ToHexString(state.Keys.AsSpan(0, 4)) == "00000004");
    Require(state.Keys.AsSpan(4, 504).SequenceEqual(original.Keys.AsSpan(4, 504)) && state.Lighting.SequenceEqual(original.Lighting));
    Require(state.Keys.Length == 512 && Convert.ToHexString(state.Keys.AsSpan(508)) == "00005AA5");
    return Task.CompletedTask;
});
await Check("Kreo effect names are complete and never label a preset Off", () =>
{
    foreach (var model in KreoModel.All)
    {
        Require(model.Effects.Length == 18 && model.Effects.Select(e => e.Name).Distinct().Count() == 18);
        Require(!model.Effects.Any(e => string.IsNullOrWhiteSpace(e.Name) || e.Name == "Off"));
        Require(model.Effects.Single(e => e.Id == 2).Name == "Breathing" && model.Effects.Single(e => e.Id == 3).Name == "Color Cycle");
        Require(model.ToString() == model.Name && model.Effects[0].ToString() == model.Effects[0].Name);
    }
    return Task.CompletedTask;
});
await Check("Kreo effect hex edits preserve the animation and other palette colors", async () =>
{
    var state = SwarmFixture(); var original = state.Copy(); var model = KreoModel.All.Single(m => m.Identities.Contains(state.Identity));
    KreoProtocol.SetLighting(state, model, 2, 3, 2, 7);
    KreoProtocol.SetEffectColor(state, model, 2, 3, KreoProtocol.ParseHex(" #12aBeF "));
    Require(state.Lighting[10] == 2 && state.Lighting[61] == 0x33);
    Require(Convert.ToHexString(state.EffectPalette!.AsSpan(51, 3)) == "12ABEF");
    Require(state.EffectPalette.AsSpan(0, 51).SequenceEqual(original.EffectPalette!.AsSpan(0, 51)) && state.EffectPalette.AsSpan(54, 454).SequenceEqual(original.EffectPalette.AsSpan(54, 454)));
    foreach (var hex in new[] { "##123456", "#XYZABC", "#FFFF", "1234567" }) await Throws(() => { KreoProtocol.ParseHex(hex); return Task.CompletedTask; });
    await Throws(() => { KreoProtocol.SetEffectColor(state, model, 3, 0, new byte[3]); return Task.CompletedTask; });
});
await Check("Kreo action targets stay independent of already remapped source keys", () =>
{
    foreach (var supported in KreoModel.All) Require(KreoActions.ForModel(supported).Count > 0);
    var state = SwarmFixture(); var model = KreoModel.All.Single(m => m.Identities.Contains(state.Identity));
    foreach (var key in model.Keys) Require(KreoActions.ForVirtualKey(key.VirtualKey).Length == 4);
    var a = model.Keys.Single(k => k.Label == "A");
    KreoProtocol.Remap(state, a.Index, KreoActions.ForVirtualKey(0x42));
    Require(Convert.ToHexString(KreoActions.ForModel(model).Single(a => a.Label == "A").Bytes) == "00000004");
    Require(Convert.ToHexString(state.Keys.AsSpan(a.Index * 4, 4)) == "00000005");
    return Task.CompletedTask;
});
await Check("Kreo legacy profile import preserves full block tails and effect palettes", () =>
{
    var state = SwarmFixture(); var legacy = state with { Keys = state.Keys.AsSpan(0, 504).ToArray(), EffectPalette = null };
    var upgraded = KreoProtocol.Import(legacy, state); upgraded.Validate();
    Require(upgraded.Keys.SequenceEqual(state.Keys) && upgraded.EffectPalette!.SequenceEqual(state.EffectPalette!));
    return Task.CompletedTask;
});
await Check("Kreo profile import rejects foreign models, missing arrays and invalid markers", async () =>
{
    var state = SwarmFixture();
    foreach (var invalid in new[] { state with { Identity = "000000000000" }, state with { Lighting = new byte[128] }, state with { Colors = new byte[377] }, state with { Keys = null! } })
        await Throws(() => { invalid.Validate(); return Task.CompletedTask; });
    var roundtrip = JsonSerializer.Deserialize<KreoState>(JsonSerializer.Serialize(state))!;
    roundtrip.Validate();
    Require(roundtrip.Lighting.SequenceEqual(state.Lighting) && roundtrip.Keys.SequenceEqual(state.Keys) && roundtrip.Colors.SequenceEqual(state.Colors));
});
await Check("HID protocol vectors and truncation", () =>
{
    Require(Convert.ToHexString(LdProtocol.Telemetry(Fresh(), false)) == "106801010B010205007D0042280000258916");
    Require(Convert.ToHexString(LdProtocol.Initialize()) == "106801010203017016");
    Require(Convert.ToHexString(LdProtocol.LeadingZeros(false)) == "106801010202006E16");
    Require(Convert.ToHexString(LdProtocol.LeadingZeros(true)) == "106801010202016F16");
    Require(Convert.ToHexString(LdProtocol.Close()) == "106801010B0100080064004270000028BC16");
    Require(LdProtocol.VendorFloatBits(0.1) == 0x3dcccccc);
    Require(LdProtocol.VendorFloatBits(-0.0) == 0);
    var report = LdProtocol.Telemetry(Fresh() with { TemperatureC = 25 }, true);
    Require(report[10] == 1 && BinaryPrimitives.ReadUInt32BigEndian(report.AsSpan(11)) == 0x429a0000);
    return Task.CompletedTask;
});
await Check("invalid telemetry never becomes a report", async () =>
{
    foreach (var sample in new[] { Fresh() with { PowerW = -1 }, Fresh() with { PowerW = 65536 }, Fresh() with { UsagePercent = 101 }, Fresh() with { TemperatureC = double.NaN } })
        await Throws(() => { LdProtocol.Telemetry(sample, false); return Task.CompletedTask; });
});
await Check("CPU selection keeps package temperature and power separate from cores", () =>
{
    CpuSensorValue[] sensors = [new("CPU Package", SensorType.Temperature, 55), new("Core Max", SensorType.Temperature, 80), new("CPU Package", SensorType.Power, 63.5), new("CPU Cores", SensorType.Power, 30), new("CPU Total", SensorType.Load, 24), new("CPU Core #1", SensorType.Clock, 5400)];
    var sample = CpuSensorSelector.Select(sensors, DateTimeOffset.UtcNow);
    Require(sample.TemperatureC == 55 && sample.PowerW == 63.5 && sample.UsagePercent == 24 && sample.ClockMhz == 5400);
    Require(sample.IsFresh(DateTimeOffset.UtcNow));
    Require(!(sample with { ReceivedAt = DateTimeOffset.UtcNow.AddSeconds(-6) }).IsFresh(DateTimeOffset.UtcNow));
    return Task.CompletedTask;
});
await Check("missing, null, ambiguous, and non-finite sensor values are rejected", async () =>
{
    CpuSensorValue[] valid = [new("CPU Package", SensorType.Temperature, 55), new("CPU Package", SensorType.Power, 63.5), new("CPU Total", SensorType.Load, 24)];
    foreach (var invalid in new[] { valid.Skip(1).ToArray(), new[] { valid[0] with { Value = null }, valid[1], valid[2] }, new[] { valid[0], valid[1] with { Value = double.NaN }, valid[2] }, valid.Append(valid[0]).ToArray(), new[] { valid[0], valid[1], valid[2] with { Value = 101 } } })
        await Throws(() => { CpuSensorSelector.Select(invalid, DateTimeOffset.UtcNow); return Task.CompletedTask; });
});
await Check("sensor polling warms up before publishing and releases on cancellation", async () =>
{
    var reader = new FakeCpuReader();
    var client = new SensorClient(() => reader);
    var first = new TaskCompletionSource<SensorSample>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    client.SampleReceived += sample => first.TrySetResult(sample);
    var polling = client.RunAsync(cancellation.Token);
    Require(!first.Task.IsCompleted);
    var sample = await first.Task.WaitAsync(cancellation.Token);
    Require(reader.Reads >= 2 && sample.TemperatureC == 55 && client.CpuName == "Test CPU");
    cancellation.Cancel();
    await polling;
    Require(reader.Disposed);
});
await Check("a broken CPU reader retries without publishing fabricated samples", async () =>
{
    var attempts = 0;
    var client = new SensorClient(() => { if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Backend not ready"); return new FakeCpuReader(); });
    var first = new TaskCompletionSource<SensorSample>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    client.SampleReceived += sample => first.TrySetResult(sample);
    var polling = client.RunAsync(cancellation.Token);
    await first.Task.WaitAsync(cancellation.Token);
    cancellation.Cancel();
    await polling;
    Require(attempts == 2);
});
await Check("idle, conflicts, and stale samples cause zero writes", async () =>
{
    var opens = 0;
    await using var controller = new DisplayController(() => { opens++; return new FakeTransport(); });
    await controller.TickAsync(Fresh(), new(), false, null);
    await controller.TickAsync(Fresh(), new(), true, "DeepCool is running");
    await controller.TickAsync(Fresh() with { ReceivedAt = DateTimeOffset.UtcNow.AddSeconds(-6) }, new(), true, null);
    Require(opens == 0 && !controller.Status.Sending);
});
await Check("start, preference change, stop, and reconnect ordering", async () =>
{
    var transports = new List<FakeTransport>();
    await using var controller = new DisplayController(() => { var value = new FakeTransport(); transports.Add(value); return value; });
    await controller.TickAsync(Fresh(), new(), true, null);
    Require(transports[0].Reports.Count == 3 && controller.Status.Sending);
    Require(transports[0].Reports[0].SequenceEqual(LdProtocol.Initialize()));
    await controller.TickAsync(Fresh(), new(true, true), true, null);
    Require(transports[0].Reports.Count == 5 && transports[0].Reports[^1][10] == 1);
    await controller.TickAsync(null, new(), false, null);
    Require(transports[0].Reports[^1].SequenceEqual(LdProtocol.Close()) && transports[0].Disposed);
    await controller.TickAsync(Fresh(), new(), true, null);
    Require(transports.Count == 2 && transports[1].Reports.Count == 3);
    await controller.TickAsync(Fresh(), new(), true, "DeepCool restarted");
    Require(transports[1].Disposed && transports[1].Reports.Count == 3 && !controller.Status.Sending);
});
await Check("failed USB write releases handle and retries with initialization", async () =>
{
    var attempts = new List<FakeTransport>();
    await using var controller = new DisplayController(() => { var transport = new FakeTransport { FailAt = attempts.Count == 0 ? 3 : 0 }; attempts.Add(transport); return transport; });
    await controller.TickAsync(Fresh(), new(), true, null);
    Require(attempts[0].Disposed && !controller.Status.Sending);
    await controller.TickAsync(Fresh(), new(), true, null);
    Require(attempts.Count == 2 && attempts[1].Reports.Count == 3 && controller.Status.Sending);
    await controller.TickAsync(Fresh() with { ReceivedAt = DateTimeOffset.UtcNow.AddSeconds(-6) }, new(), true, null);
    Require(attempts[1].Disposed && !controller.Status.Sending);
});
await Check("stop waits for an in-flight write before closing", async () =>
{
    var transport = new FakeTransport { BlockFirst = true };
    await using var controller = new DisplayController(() => transport);
    var start = controller.TickAsync(Fresh(), new(), true, null);
    await transport.FirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var stop = controller.TickAsync(null, new(), false, null);
    Require(!stop.IsCompleted);
    transport.Continue.TrySetResult();
    await Task.WhenAll(start, stop);
    Require(transport.Disposed && transport.Reports[^1].SequenceEqual(LdProtocol.Close()));
});
await Check("receiver packets match captured 2.4 GHz traffic", () =>
{
    // Replies captured from a Swarm 75 through its receiver (lighting read, battery read, lighting write acknowledgement).
    foreach (var hex in new[] { "13440A000E0003030000000404070006200100AB", "134A0100026401000000000000000000000000C5", "13040A010E010000000100040200FF060000003D" })
    {
        var packet = Convert.FromHexString(hex);
        Require(KreoReceiver.Checksum(packet) == packet[^1], "checksum " + hex);
    }
    var request = KreoReceiver.Request(KreoReceiver.ReadLighting, 0);
    Require(request.Length == 20 && request[0] == 0x13 && request[1] == 0x44 && request[2] == 1 && request[3] == 0 && request[4] == 0 && request[^1] == KreoReceiver.Checksum(request));
    Require(KreoReceiver.Request(KreoReceiver.ReadMatrix, 2)[4] == 0x20, "selector goes in the high nibble");
    return Task.CompletedTask;
});
await Check("keyboard layouts snap to a key-unit grid without losing or overlapping keys", () =>
{
    foreach (var model in KreoModel.All)
    {
        var layout = KreoLayout.Build(model);
        var visible = model.Keys.Where(k => k.Width > 0 && k.Height > 0).ToArray();
        Require(layout.Count == visible.Length && layout.Select(p => p.Key).SequenceEqual(visible), model.Name + ": keys changed");
        Require(layout.All(p => p.X * 4 % 1 == 0 && p.Y * 4 % 1 == 0 && p.Width >= 1 && p.Height >= 1), model.Name + ": off-grid key");
        for (var i = 0; i < layout.Count; i++)
            for (var j = i + 1; j < layout.Count; j++)
            {
                var (a, b) = (layout[i], layout[j]);
                Require(!(a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height), $"{model.Name}: {a.Key.Label} overlaps {b.Key.Label}");
            }
    }
    var swarm = KreoLayout.Build(KreoModel.All.Single(m => m.Name == "Swarm 75")).ToDictionary(p => p.Key.Label);
    Require(swarm["Backspace"].Width == 2 && swarm["Space"].Width == 6.25 && swarm["LShift"].Width == 2.25 && swarm["Enter"].Width == 2.25 && swarm["Tab"].Width == 1.5 && swarm["CapsLock"].Width == 1.75);
    Require(swarm["Up"].Y == swarm["RShift"].Y + 0.25 && swarm["Left"].Y == swarm["Space"].Y + 0.25, "Swarm 75 arrow cluster offset lost");
    Require(KreoLayout.Size(KreoLayout.Build(KreoModel.All.Single(m => m.Name == "Swarm 75"))) == (16.5, 6.5));
    return Task.CompletedTask;
});
await Check("Gigabyte RGB effect packets match GCC's DataFormat_8297 layout", () =>
{
    Require(Convert.ToHexString(GigabyteRgb.BuildEffect(BoardEffect.Static, 0x12, 0x34, 0x56, 8, 0)) == "FF070000000000000001FF005634120000000000000000000000000000000000", "static");
    var wave = GigabyteRgb.BuildEffect(BoardEffect.Wave, 0, 0, 0, 4, 9);
    Require(wave[9] == 6 && wave[10] == 128 && BitConverter.ToUInt16(wave, 20) == 30 && wave[28] == 7 && wave[29] == 1, "wave");
    var doubleFlash = GigabyteRgb.BuildEffect(BoardEffect.DoubleFlash, 255, 0, 0, 0, 0);
    Require(doubleFlash[9] == 3 && BitConverter.ToUInt16(doubleFlash, 24) == 2600 && doubleFlash[29] == 1 && doubleFlash[30] == 2, "double flash");
    Require(GigabyteRgb.BuildEffect(BoardEffect.Pulse, 1, 2, 3, 0, 5)[10] == 100, "pulse peaks at GCC's fixed 100");
    var off = GigabyteRgb.BuildEffect(BoardEffect.Off, 9, 9, 9, 3, 3);
    Require(off[9] == 1 && BitConverter.ToUInt32(off, 12) == 0, "off is static black");
    var threw = false;
    try { GigabyteRgb.BuildEffect(BoardEffect.Static, 0, 0, 0, 9, 0); } catch (ArgumentOutOfRangeException) { threw = true; }
    Require(threw, "brightness above GCC's table is refused");
    return Task.CompletedTask;
});

await Check("Gigabyte RGB zone packets match GCC's captured exit sequence", () =>
{
    // From artifacts: Frida capture of GCC.exe HidD_SetFeature on exit (Static red, brightness level 6 = 0xB3).
    Require(Convert.ToHexString(GigabyteRgb.BuildZonePacket(new BoardZoneEffect(0, BoardEffect.Static, 0xFF, 0, 0, 6, 0))).StartsWith("CC2001000000000000000001B3000000FF00"), "zone 0");
    Require(Convert.ToHexString(GigabyteRgb.BuildZonePacket(new BoardZoneEffect(6, BoardEffect.Static, 0xFF, 0, 0, 6, 0))).StartsWith("CC2640000000000000000001B3000000FF00"), "zone 6");
    Require(Convert.ToHexString(GigabyteRgb.BuildZonePacket(new BoardZoneEffect(9, BoardEffect.Static, 0xFF, 0, 0, 6, 0))).StartsWith("CC9100020000000000000001B3000000FF00"), "zone 9 uses header 0x91");
    var zones = GigabyteRgb.ZonesFor("Z890 GAMING X WIFI7").Select(z => z.Number).Order().ToArray();
    Require(zones.SequenceEqual(new[] { 0, 2, 4, 5, 6, 7, 9 }), "GCC's divsmap zones for MB_Id 31 layout 4");
    return Task.CompletedTask;
});

await Check("Board rainbow wave is GCC's captured Wave17 packet; saved Wave is sent as colour cycle", () =>
{
    Require(Convert.ToHexString(GigabyteRgb.RainbowWavePacket()) == "CC220000000000000000000964000000000000000000260200000000000007000000", "Wave17 packet from frida-gcc-red-rainbow.jsonl");
    foreach (var effect in new[] { BoardEffect.Rainbow, BoardEffect.Wave })
        foreach (var zone in new[] { 0, 2, 4, 5, 6, 7, 9 })
        {
            var sent = GigabyteRgb.ForZone(new BoardZoneEffect(zone, effect, 0, 0, 0, 8, 8));
            Require(sent.Effect == BoardEffect.ColorCycle && GigabyteRgb.BuildZonePacket(sent)[11] == 4, $"{effect} on zone {zone}");
        }
    Require((int)BoardEffect.Wave == 6 && (int)BoardEffect.Rainbow == 7, "saved effect numbers are stable");
    return Task.CompletedTask;
});

await Check("Board Rainbow sends GCC's captured wave sequence, not per-zone colour cycles", () =>
{
    // Sora 0.6.2-0.6.3 converted Rainbow to Color cycle before applying, so the wave never reached the board.
    var zones = GigabyteRgb.ZonesFor("Z890 GAMING X WIFI7").Select(z => new BoardZoneEffect(z.Number, BoardEffect.Rainbow, 10, 132, 255, 8, 8)).ToArray();
    var sent = GigabyteRgb.Plan(zones).Select(step => Convert.ToHexString(step.Packet)).ToArray();
    // frida-gcc-wave.jsonl / frida-ioctl.jsonl: what GCC sends when the fans flow.
    var tail = new[] { "CC28FF07", "CC20FF070000000000000001FF000000000000000000000000000000000000000000", "CC28FF07", Convert.ToHexString(GigabyteRgb.RainbowWavePacket()), "CC280400" };
    Require(sent.TakeLast(5).SequenceEqual(tail), "wave sequence: " + string.Join(" ", sent.TakeLast(5)));
    Require(!sent.Any(p => p.Length > 30 && p.Substring(22, 2) == "04"), "no colour cycle packets");
    Require(sent.Take(2).SequenceEqual(new[] { "CC340000", "CC3200" }), "GCC's connect setup first");
    var cycle = GigabyteRgb.Plan(zones.Select(z => z with { Effect = BoardEffect.ColorCycle }).ToArray()).Select(step => Convert.ToHexString(step.Packet)).ToArray();
    Require(!cycle.Contains(Convert.ToHexString(GigabyteRgb.RainbowWavePacket())) && cycle.Count(p => p.Length > 30 && p.Substring(22, 2) == "04") == zones.Length, "Color cycle stays per zone");
    return Task.CompletedTask;
});

await Check("Sync frames are one Static packet per board light plus apply", () =>
{
    var zones = new[] { 0, 2, 4, 5, 6, 7, 9 };
    var frame = GigabyteRgb.FramePackets(zones, 0x12, 0x34, 0x56).Select(Convert.ToHexString).ToArray();
    Require(frame.Length == zones.Length + 1 && frame[^1] == "CC28FF07", "apply last");
    Require(frame[0].StartsWith("CC2001000000000000000001FF0056341200"), "zone 0 Static, full level, colour 0x123456: " + frame[0][..40]);
    Require(frame[^2].StartsWith("CC910002"), "zone 9 on header 0x91");
    return Task.CompletedTask;
});

await Check("ENE writes are limited to the control block, apply byte and effect colours", () =>
{
    var dimm = new EneDimm(1, 0x71, "AUDA0-E6K5-0101", 0x6802, [0xA1, 0xD5, 0x08, 0x03, 0x00], 8, 0x8020, 0x8160, new EneSnapshot(new byte[4], new byte[24]));
    foreach (var allowed in new ushort[] { 0x8020, 0x8021, 0x8022, 0x8023, 0x8029, 0x802F, 0x8160, 0x8177 }) Require(dimm.CanWrite(allowed), $"0x{allowed:X4} should be writable");
    // 0x80A0 is apply/save-to-flash, 0x80F8/0x80F9 remap the I2C address, 0x1000 the name. 0x8029 (brightness) only on 0xD5 chips, as GCC.
    Require(!(dimm with { Firmware = [0xA1, 0x00, 0x08, 0x03, 0x00] }).CanWrite(0x8029), "brightness is written only on 0xD5 chips");
    Require(EneDram.GccBrightness(EneMode.Static, 0xCC, 0, 0) == 2, "GCC's captured red #CC0000 runs at brightness 2");
    Require(EneDram.GccBrightness(EneMode.Static, 0xFF, 0, 0) == 3 && EneDram.GccBrightness(EneMode.Static, 0xFF, 0xFF, 0xFF) == 9, "(R+G+B)/84");
    Require(EneDram.GccBrightness(EneMode.Rainbow, 0, 0, 0) == 9 && EneDram.GccBrightness(EneMode.Static, 1, 0, 0) == 1 && EneDram.GccBrightness(EneMode.Off, 0, 0, 0) == null, "rainbow 9, at least 1, Off untouched");
    foreach (var refused in new ushort[] { 0x8024, 0x8028, 0x802A, 0x802E, 0x80A0, 0x80F8, 0x80F9, 0x8100, 0x815F, 0x8178, 0x1000, 0x1C00, 0xF01C, 0xE020 }) Require(!dimm.CanWrite(refused), $"0x{refused:X4} must be refused");
    Require(EneDram.Encode(0x01, 0x02, 0x03, EneColorOrder.Rbg).SequenceEqual(new byte[] { 0x01, 0x03, 0x02 }), "ENE colour order is R, B, G");
    return Task.CompletedTask;
});

Console.WriteLine($"{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;

sealed class FakeTransport : IDisplayTransport
{
    public List<byte[]> Reports { get; } = [];
    public bool Disposed { get; private set; }
    public int FailAt { get; init; }
    public bool BlockFirst { get; init; }
    public TaskCompletionSource FirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int count;
    public async Task WriteAsync(byte[] report, CancellationToken token)
    {
        if (Disposed) throw new Exception("Write after disposal");
        count++;
        if (count == FailAt) throw new IOException("Device disconnected");
        if (BlockFirst && count == 1) { FirstWrite.TrySetResult(); await Continue.Task.WaitAsync(token); }
        Reports.Add(report.ToArray());
    }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
sealed class FakeCpuReader : ICpuSensorReader
{
    public string CpuName => "Test CPU";
    public int Reads { get; private set; }
    public bool Disposed { get; private set; }
    public IReadOnlyList<CpuSensorValue> Read()
    {
        if (Disposed) throw new Exception("Read after disposal");
        Reads++;
        return [new("CPU Package", SensorType.Temperature, 55), new("CPU Package", SensorType.Power, 63.5), new("CPU Total", SensorType.Load, 24)];
    }
    public void Dispose() => Disposed = true;
}
