using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

internal sealed record LogitechReactiveZone(byte Zone, string Mode, string Hex, int Brightness, string ScreenArea)
{
    internal void Validate()
    {
        if (Zone > 1 || Mode is not ("Screen sampler" or "Audio visualizer") || Brightness is < 1 or > 100 || ScreenArea is not ("Whole screen" or "Left half" or "Right half")) throw new InvalidDataException("Invalid reactive lighting configuration.");
        if (string.IsNullOrWhiteSpace(Hex) || Convert.FromHexString(Hex.TrimStart('#')).Length != 3) throw new InvalidDataException("Enter a six-digit lighting color.");
    }
}

internal sealed class LogitechReactiveLighting
{
    private readonly SoraHost host;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;
    private string OwnershipPath => path + ".ownership";
    private string FirmwarePath => Path.Combine(Path.GetDirectoryName(path)!, "firmware-lighting.json");
    private string LinkPath => Path.Combine(Path.GetDirectoryName(path)!, "lighting-linked.json");
    public bool LinkedZones { get; private set; }
    public void SaveLinkedZones(bool linked)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LinkPath)!);
        File.WriteAllText(LinkPath + ".tmp", JsonSerializer.Serialize(linked));
        File.Move(LinkPath + ".tmp", LinkPath, true); LinkedZones = linked;
    }
    private sealed record FirmwareLighting(string Identity, System.Collections.Generic.Dictionary<byte, byte[]> States,
        System.Collections.Generic.Dictionary<byte, LogitechLightingChoice>? Choices = null);
    private FirmwareLighting? firmware;
    internal byte[]? SavedFirmwareState(string identity, byte zone) => firmware?.Identity == identity && !Zones.Any(z => z.Zone == zone) && firmware.States.TryGetValue(zone, out var state) ? state.ToArray() : null;
    internal LogitechLightingChoice? SavedChoice(string identity, byte zone) => SavedFirmwareState(identity, zone) is { } state
        ? firmware!.Choices?.GetValueOrDefault(zone) ?? LogitechLightingChoice.FromState(state) : null;
    private bool firmwareApplied;
    private sealed record Ownership(string Identity, System.Collections.Generic.Dictionary<byte, byte[]> States);
    private CancellationTokenSource? lifetime;
    private Task task = Task.CompletedTask;
    private DateTime retryAt;
    private DateTime verifyAt;
    private bool suspended, stopping;
    public LogitechReactiveZone[] Zones { get; private set; } = [];
    public string Status { get; private set; } = "";
    public bool Running => lifetime is { IsCancellationRequested: false } && !task.IsCompleted;
    public LogitechReactiveLighting(SoraHost host)
    {
        this.host = host; path = Path.Combine(host.DataDirectory, "logitech", "reactive-lighting.json");
        try { if (File.Exists(LinkPath)) LinkedZones = JsonSerializer.Deserialize<bool>(File.ReadAllText(LinkPath)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { host.Log("Mouse lighting link: " + e.Message); }
        try { if (File.Exists(path) && new FileInfo(path).Length < 8192) { Zones = JsonSerializer.Deserialize<LogitechReactiveZone[]>(File.ReadAllText(path)) ?? []; Validate(Zones); } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or FormatException) { host.Log("Reactive lighting: " + e.Message); Zones = []; }
        try
        {
            if (File.Exists(FirmwarePath))
            {
                firmware = JsonSerializer.Deserialize<FirmwareLighting>(File.ReadAllText(FirmwarePath));
                if (firmware == null || string.IsNullOrWhiteSpace(firmware.Identity) || firmware.States == null || firmware.States.Count is < 1 or > 2 || firmware.States.Any(z => z.Key > 1 || z.Value == null || z.Value.Length != 11))
                    throw new InvalidDataException("Invalid saved mouse lighting.");
                // Bad editor metadata must not discard a valid saved hardware state.
                if (firmware.Choices != null)
                    foreach (var entry in firmware.Choices.ToArray())
                    {
                        try { if (entry.Value == null || !firmware.States.TryGetValue(entry.Key, out var state) || !entry.Value.Encode().SequenceEqual(state)) firmware.Choices.Remove(entry.Key); }
                        catch (Exception e) when (e is ArgumentException or FormatException or InvalidDataException) { firmware.Choices.Remove(entry.Key); }
                    }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        { firmware = null; host.Log("Mouse lighting settings: " + e.Message); }
    }
    private static void Validate(LogitechReactiveZone[] zones) { if (zones.Length > 2 || zones.Any(z => z == null) || zones.Select(z => z.Zone).Distinct().Count() != zones.Length) throw new InvalidDataException("The G502 has two lighting zones."); foreach (var zone in zones) zone.Validate(); }
    public async Task SetAsync(LogitechReactiveZone zone)
    {
        zone.Validate(); if (LogitechDevice.IsGHubRunning()) throw new InvalidOperationException("Exit G HUB before applying reactive lighting.");
        await gate.WaitAsync();
        var previous = Zones;
        try
        {
            await StopCoreAsync(); Zones = Zones.Where(z => z.Zone != zone.Zone).Append(zone).ToArray();
            try { await StartCoreAsync(); Save(); }
            catch { await StopCoreAsync(); Zones = previous; throw; }
        }
        finally { gate.Release(); }
    }
    public async Task ApplyFirmwareAsync(byte zone, byte[] state, LogitechLightingChoice? choice = null)
    {
        if (zone > 1 || state.Length != 11 || choice != null && !choice.Encode().SequenceEqual(state)) throw new InvalidDataException("Lighting choice does not match the device state.");
        if (LogitechDevice.IsGHubRunning()) throw new InvalidOperationException("Exit G HUB before applying lighting.");
        await gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            var fresh = await host.Logitech.UseDeviceAsync(async device =>
            {
                var current = await device.ReadAsync();
                await device.SetLightingAsync(current, new System.Collections.Generic.Dictionary<byte, byte[]> { [zone] = state });
                return current;
            });
            RememberFirmware(fresh.Identity, zone, state, choice);
            firmwareApplied = true;
            Zones = Zones.Where(z => z.Zone != zone).ToArray(); Save(); if (Zones.Length > 0) await StartCoreAsync();
        }
        finally { gate.Release(); }
    }
    internal void RememberFirmware(string identity, byte zone, byte[] state, LogitechLightingChoice? choice)
    {
        if (string.IsNullOrWhiteSpace(identity) || zone > 1 || state.Length != 11 || choice != null && !choice.Encode().SequenceEqual(state))
            throw new InvalidDataException("Lighting choice does not match the device state.");
        var states = firmware?.Identity == identity ? new Dictionary<byte, byte[]>(firmware.States) : new();
        states[zone] = state.ToArray();
        var choices = firmware?.Identity == identity && firmware.Choices != null ? new Dictionary<byte, LogitechLightingChoice>(firmware.Choices) : new();
        choices[zone] = choice ?? LogitechLightingChoice.FromState(state);
        var saved = new FirmwareLighting(identity, states, choices);
        Directory.CreateDirectory(Path.GetDirectoryName(FirmwarePath)!);
        File.WriteAllText(FirmwarePath + ".tmp", JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FirmwarePath + ".tmp", FirmwarePath, true);
        firmware = saved;
    }
    private void Save() { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Zones, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true); }
    private async Task StartCoreAsync()
    {
        await RestoreOwnershipAsync();
        lifetime = new CancellationTokenSource(); var selected = Zones.ToArray(); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        task = Task.Run(() => StreamAsync(selected, ready, lifetime.Token)); await ready.Task;
    }
    private async Task StreamAsync(LogitechReactiveZone[] selected, TaskCompletionSource ready, CancellationToken token)
    {
        LogitechDevice? device = null; LogitechSnapshot? original = null; using var meter = new LogitechOutputMeter();
        try
        {
            device = new LogitechDevice(LogitechDevice.EnumerateG502().Single()); original = await device.ReadAsync(token);
            var ownership = new Ownership(original.Identity, selected.ToDictionary(z => z.Zone, z => original.Lighting!.Single(o => o.Index == z.Zone).State));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(OwnershipPath + ".tmp", JsonSerializer.Serialize(ownership)); File.Move(OwnershipPath + ".tmp", OwnershipPath, true);
            var feature = original.Features.Single(f => f.Id == 0x8070).Index;
            var last = new System.Collections.Generic.Dictionary<byte, string>();
            var firstFrame = true;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (LogitechDevice.IsGHubRunning()) break;
                foreach (var zone in selected)
                {
                    var hex = zone.Mode == "Screen sampler" ? LogitechScreenSampler.Sample(zone.ScreenArea) : zone.Hex.TrimStart('#');
                    var brightness = zone.Mode == "Audio visualizer" ? Math.Max(1, (int)(Math.Sqrt(meter.Peak()) * zone.Brightness)) : zone.Brightness;
                    if (zone.Mode == "Audio visualizer" && meter.Peak() < 0.0001f) hex = "000000";
                    var state = LogitechLighting.Encode(1, hex, 5000, brightness);
                    var wire = LogitechLighting.ToWire(state, original.Lighting!.Single(z => z.Index == zone.Zone).Effects);
                    var identity = Convert.ToHexString(wire); if (last.TryGetValue(zone.Zone, out var old) && old == identity) continue;
                    var data = new byte[16]; data[0] = zone.Zone; wire.CopyTo(data, 1); // persistence byte 12 remains zero
                    await device.RequestAsync(feature, 3, data, token); last[zone.Zone] = identity;
                    if (firstFrame)
                    {
                        var read = await device.RequestAsync(feature, 14, [zone.Zone], token);
                        if (read.Length < 12 || read[0] != zone.Zone || !read.AsSpan(1, 11).SequenceEqual(wire)) throw new IOException("Reactive lighting first-frame readback differs.");
                    }
                }
                firstFrame = false;
                Status = "Reactive lighting streaming · Sora must stay running";
                ready.TrySetResult();
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) { ready.TrySetCanceled(); }
        catch (Exception e) when (e is not OutOfMemoryException) { ready.TrySetException(e); Status = e.Message; host.Log("Reactive lighting: " + e.Message); }
        finally
        {
            ready.TrySetException(new IOException("Reactive lighting stopped before its first frame; check the device and competing controllers."));
            if (device != null)
            {
                await device.DisposeAsync();
                try { await RestoreOwnershipAsync(); }
                catch (Exception error) when (error is not OutOfMemoryException) { Status = "Lighting restoration pending · reconnect the mouse"; host.RecordCleanupFailure("Reactive lighting restoration: " + error.Message); }
            }
        }
    }
    private async Task RestoreOwnershipAsync()
    {
        if (!File.Exists(OwnershipPath)) return;
        // A new owner supersedes our RAM state; do not overwrite G HUB's profile.
        if (LogitechDevice.IsGHubRunning()) { File.Delete(OwnershipPath); return; }
        if (new FileInfo(OwnershipPath).Length > 8192) throw new InvalidDataException("Invalid lighting ownership backup.");
        var backup = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(OwnershipPath)) ?? throw new InvalidDataException("Invalid lighting ownership backup.");
        await using var device = new LogitechDevice(LogitechDevice.EnumerateG502().Single()); var fresh = await device.ReadAsync();
        if (fresh.Identity != backup.Identity || backup.States == null || backup.States.Count is < 1 or > 2 || backup.States.Any(z => z.Key > 1 || z.Value == null || z.Value.Length != 11)) throw new InvalidDataException("Lighting backup does not match this mouse.");
        await device.SetLightingAsync(fresh, backup.States); File.Delete(OwnershipPath);
    }
    public async Task TickAsync(bool allowHardware)
    {
        if (!allowHardware || suspended || stopping || gate.CurrentCount == 0) return;
        if (LogitechDevice.IsGHubRunning()) { firmwareApplied = false; return; }
        // RAM lighting is lost on USB reconnect, even while Sora stays open.
        // Verify through the shared mouse connection so control polling cannot consume our replies.
        if (firmware != null && firmwareApplied && DateTime.UtcNow >= verifyAt)
        {
            verifyAt = DateTime.UtcNow.AddSeconds(10);
            await gate.WaitAsync();
            try
            {
                var fresh = await host.Logitech.UseDeviceAsync(d => d.ReadAsync());
                firmwareApplied = FirmwareMatches(firmware.Identity, firmware.States, Zones, fresh);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { firmwareApplied = false; Status = "Saved mouse lighting waiting for reconnection: " + error.Message; }
            finally { gate.Release(); }
        }
        if (Zones.Length == 0 && !File.Exists(OwnershipPath) && (firmware == null || firmwareApplied)) return;
        if ((!Running || (firmware != null && !firmwareApplied)) && !LogitechDevice.IsGHubRunning() && DateTime.UtcNow >= retryAt)
        {
            retryAt = DateTime.UtcNow.AddSeconds(5); await gate.WaitAsync();
            try
            {
                await StopCoreAsync();
                await RestoreOwnershipAsync();
                if (firmware != null && !firmwareApplied)
                {
                    await host.Logitech.UseDeviceAsync(async device =>
                    {
                        var fresh = await device.ReadAsync();
                        if (fresh.Identity != firmware.Identity) throw new InvalidDataException("Saved lighting belongs to a different mouse.");
                        await device.SetLightingAsync(fresh, firmware.States); return true;
                    });
                    firmwareApplied = true;
                    host.Log("Mouse lighting: saved firmware effects restored.");
                }
                if (Zones.Length > 0) await StartCoreAsync();
            }
            catch (Exception e) when (e is not OutOfMemoryException) { Status = e.Message; host.Log("Reactive lighting: " + e.Message); }
            finally { gate.Release(); }
        }
    }
    internal static bool FirmwareMatches(string identity, System.Collections.Generic.IReadOnlyDictionary<byte, byte[]> states, LogitechReactiveZone[] zones, LogitechSnapshot fresh) =>
        fresh.Identity == identity && states.Where(s => !zones.Any(z => z.Zone == s.Key)).All(s =>
            fresh.Lighting?.SingleOrDefault(z => z.Index == s.Key) is { } actual &&
            LogitechLighting.ToWire(s.Value, actual.Effects).SequenceEqual(LogitechLighting.ToWire(actual.State, actual.Effects)));
    public async Task SuspendAsync(bool value) { suspended = value; if (value) { firmwareApplied = false; await gate.WaitAsync(); try { await StopCoreAsync(); } finally { gate.Release(); } } }
    public async Task StopAsync() { stopping = true; await gate.WaitAsync(); try { await StopCoreAsync(); } finally { gate.Release(); } }
    private async Task StopCoreAsync() { lifetime?.Cancel(); await task; lifetime?.Dispose(); lifetime = null; }
}
