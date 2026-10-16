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
