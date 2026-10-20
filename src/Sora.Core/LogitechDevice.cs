using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Sora.Core;

/// <summary>Independent wired HID++ transport. Both collections are read because replies may change report length.</summary>
public sealed class LogitechDevice : IAsyncDisposable
{
    private readonly FileStream shortStream, longStream;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<byte[]> incoming = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Task[] readers;
    private readonly DeviceInfo device;
    private readonly byte softwareId;
    private static readonly object clientLock = new();
    private static readonly bool[] clients = new bool[16];
    // The UI, button controller and lighting streamer can all open HID collections.
    // Serialize commands across those sessions, not just within one connection.
    private static readonly SemaphoreSlim wireGate = new(1, 1);
    private bool faulted;
    private readonly Channel<ushort> buttons = Channel.CreateBounded<ushort>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private byte spyFeature;
    public ChannelReader<ushort> ButtonReports => buttons.Reader;
    public bool Matches(DeviceInfo other) => device.ProductId == other.ProductId &&
        LogitechModels.PhysicalIdentity(device.Path).Equals(LogitechModels.PhysicalIdentity(other.Path), StringComparison.OrdinalIgnoreCase);

    public static DeviceInfo[] Enumerate() => LogitechModels.ConfigurationInterfaces(HidDevice.FindLogitech());
    public static DeviceInfo[] EnumerateG502() => Enumerate().Where(d => d.ProductId == 0xc08b).ToArray();

    public LogitechDevice(DeviceInfo device)
    {
        if (!LogitechModels.IsConfigurationInterface(device)) throw new NotSupportedException("This device does not expose a supported wired Logitech configuration interface.");
        this.device = device;
        var peer = HidDevice.FindLogitech().SingleOrDefault(d => d.ProductId == device.ProductId && d.SerialNumber == device.SerialNumber && d.UsagePage == 0xff00 && d.Usage == 1 && d.OutputReportLength == 7 && PhysicalPath(d.Path) == PhysicalPath(device.Path))
            ?? throw new IOException("The mouse's short HID++ interface is unavailable.");
        shortStream = Open(peer.Path);
        try { longStream = Open(device.Path); }
        catch { shortStream.Dispose(); throw; }
        try
        {
            lock (clientLock)
            {
                for (byte id = 1; id < 16; id++) if (!clients[id]) { clients[id] = true; softwareId = id; break; }
                if (softwareId == 0) throw new IOException("Too many concurrent Logitech sessions.");
            }
        }
        catch { shortStream.Dispose(); longStream.Dispose(); throw; }
        readers = [ReadLoop(shortStream, 7), ReadLoop(longStream, 20)];
    }

    private static string PhysicalPath(string path) => System.Text.RegularExpressions.Regex.Replace(path, @"&col\d\d|&000[0-9](?=#)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static FileStream Open(string path)
    {
        var handle = HidDevice.Native.CreateFile(path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Cannot open the Logitech configuration interface."); }
        try { return new FileStream(handle, FileAccess.ReadWrite, 1, true); }
        catch { handle.Dispose(); throw; }
    }

    private async Task ReadLoop(FileStream stream, int length)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var report = new byte[length];
                var read = await stream.ReadAsync(report, lifetime.Token);
                if (read == 0) throw new IOException("Logitech mouse disconnected.");
                if (read >= 7)
                {
                    if (spyFeature != 0 && report[0] == 0x11 && report[1] == 0xff && report[2] == spyFeature && report[3] == 0)
                    {
                        if (!buttons.Writer.TryWrite((ushort)(report[4] << 8 | report[5]))) { buttons.Writer.TryComplete(new IOException("Logitech input queue overflowed; control was stopped to release held input.")); lifetime.Cancel(); }
                    }
                    // HID++ errors use report[2] == 0xFF and carry the original feature
                    // index in report[3], so its low nibble is not a software ID.
                    else if (LogitechProtocol.IsResponseCandidate(report.AsSpan(0, read))) incoming.Writer.TryWrite(report[..read]);
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { incoming.Writer.TryComplete(e); buttons.Writer.TryComplete(e); }
    }

    public async Task<byte[]> RequestAsync(byte feature, byte function, byte[]? parameters = null, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        var wireHeld = false;
        try
        {
            await wireGate.WaitAsync(token); wireHeld = true;
            if (faulted) throw new IOException("Reconnect the mouse session after a failed HID++ transaction.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            timeout.CancelAfter(1800);
            while (incoming.Reader.TryRead(out _)) { }
            var request = LogitechProtocol.Request(0xff, feature, function, softwareId, parameters ?? []);
            await (request.Length == 7 ? shortStream : longStream).WriteAsync(request, timeout.Token);
            while (true)
            {
                var reply = await incoming.Reader.ReadAsync(timeout.Token);
                if (!LogitechProtocol.Matches(reply, request)) continue;
                LogitechProtocol.CheckReply(reply, request);
                return reply[4..];
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ChannelClosedException)
        {
            faulted = true;
            throw new IOException("The Logitech mouse did not respond. Reconnect it and refresh; another controller may be using it.", error);
        }
        finally { if (wireHeld) wireGate.Release(); gate.Release(); }
    }

    public async Task<LogitechSnapshot> ReadAsync(CancellationToken token = default)
    {
        var version = await RequestAsync(0, 1, [0, 0, 0x6b], token);
        if (version.Length < 3 || version[0] < 2 || version[2] != 0x6b) throw new IOException("The mouse did not confirm HID++ 2.0.");
        var set = await RequestAsync(0, 0, [0, 1], token);
        if (set[0] == 0) throw new IOException("The mouse has no discoverable HID++ feature set.");
        var count = (await RequestAsync(set[0], 0, token: token))[0];
        var features = new List<LogitechFeature>();
        for (var i = 1; i <= count; i++)
        {
            var f = await RequestAsync(set[0], 1, [(byte)i], token);
            features.Add(new((ushort)(f[0] << 8 | f[1]), (byte)i, f[2], f.Length > 3 ? f[3] : (byte)0));
        }
        var sensors = new List<LogitechSensor>(); var warnings = new List<string>();
        var dpi = features.Find(f => f.Id == 0x2201);
        if (dpi != null)
        {
            var sensorCount = (await RequestAsync(dpi.Index, 0, token: token))[0];
            if (sensorCount > 16) throw new InvalidDataException("Unexpected Logitech sensor count.");
            for (byte i = 0; i < sensorCount; i++)
            {
                var list = await RequestAsync(dpi.Index, 1, [i], token);
                var current = await RequestAsync(dpi.Index, 2, [i], token);
                sensors.Add(LogitechProtocol.ParseSensor(i, list, current));
            }
        }
        int? rate = null, mode = null; int[] rates = [];
        var report = features.Find(f => f.Id == 0x8060);
        if (report != null)
        {
            rates = LogitechProtocol.ParseReportRates((await RequestAsync(report.Index, 0, token: token))[0]);
            var period = (await RequestAsync(report.Index, 1, token: token))[0];
            rate = period > 0 ? 1000 / period : null;
        }
        var onboard = features.Find(f => f.Id == 0x8100);
        if (onboard != null) mode = (await RequestAsync(onboard.Index, 2, token: token))[0];
        var zones = new List<LogitechLightZone>();
        var lighting = features.Find(f => f.Id == 0x8070);
        if (lighting != null)
        {
            try
            {
                var info = await RequestAsync(lighting.Index, 0, token: token);
                if (info[0] > 16) throw new InvalidDataException("Unexpected Logitech light zone count.");
                for (byte zone = 0; zone < info[0]; zone++)
                {
                    var z = await RequestAsync(lighting.Index, 1, [zone], token);
                    if (z[3] > 32) throw new InvalidDataException("Unexpected Logitech effect count.");
                    var effects = new List<int>();
                    for (byte effect = 0; effect < z[3]; effect++)
                    {
                        var e = await RequestAsync(lighting.Index, 2, [zone, effect], token);
                        effects.Add(e[2] << 8 | e[3]);
                    }
                    var current = await RequestAsync(lighting.Index, 14, [zone], token);
                    if (current.Length < 12 || current[0] != zone) throw new InvalidDataException("Invalid Logitech lighting state.");
                    zones.Add(new(zone, (z[1] << 8 | z[2]) == 2 ? "Logo" : "DPI indicator", effects.ToArray(), LogitechLighting.FromWire(current.AsSpan(1, 11), effects.ToArray())));
                }
            }
            catch (IOException error) { warnings.Add("Lighting read unavailable: " + error.Message); }
        }
        if (IsGHubRunning()) warnings.Add("G HUB agent is running. Exit G HUB before applying Sora settings to avoid competing changes.");
        return new(LogitechModels.Name(device), string.IsNullOrWhiteSpace(device.SerialNumber) ? device.Path : device.SerialNumber, device.ProductId,
            $"{version[0]}.{version[1]}", features.ToArray(), sensors.ToArray(), rate, rates, mode, warnings.ToArray(), zones.ToArray());
    }

    public static bool IsGHubRunning()
    {
        var processes = Process.GetProcessesByName("lghub_agent");
        try { return processes.Length != 0; } finally { foreach (var process in processes) process.Dispose(); }
    }

    public async Task SetModeAsync(LogitechSnapshot snapshot, int mode, CancellationToken token = default)
    {
        if (mode is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(mode));
        var index = snapshot.Features.Single(f => f.Id == 0x8100).Index;
        await RequestAsync(index, 1, [(byte)mode], token);
        if ((await RequestAsync(index, 2, token: token))[0] != mode) throw new IOException("The mouse did not accept the control mode.");
    }

    public async Task<byte[]> ReadButtonMapAsync(LogitechSnapshot snapshot, CancellationToken token = default)
    {
        if (!LogitechModels.HasG502Controls(snapshot)) throw new NotSupportedException("Button controls are unavailable for this model.");
        var index = snapshot.Features.Single(f => f.Id == 0x8110).Index;
        var count = await RequestAsync(index, 0, token: token);
        if (count[0] != 11) throw new NotSupportedException("This device's button layout is not the G502 HERO layout.");
        var map = await RequestAsync(index, 3, token: token);
        if (map.Length != 16) throw new IOException("Incomplete Logitech button map."); return map;
    }
    public async Task WriteButtonMapAsync(LogitechSnapshot snapshot, byte[] map, CancellationToken token = default)
    {
        if (!LogitechModels.HasG502Controls(snapshot)) throw new NotSupportedException("Button controls are unavailable for this model.");
        if (map.Length != 16 || map[0] != 1 || map.Any(b => b > 16)) throw new InvalidDataException("Invalid G502 button forwarding map; native primary click must be preserved.");
        var index = snapshot.Features.Single(f => f.Id == 0x8110).Index;
        await RequestAsync(index, 4, map, token);
        if (!(await RequestAsync(index, 3, token: token)).SequenceEqual(map)) throw new IOException("Logitech button forwarding readback failed.");
    }
    public async Task SetButtonSpyAsync(LogitechSnapshot snapshot, bool enable, CancellationToken token = default)
    {
        var index = snapshot.Features.Single(f => f.Id == 0x8110).Index;
        spyFeature = enable ? index : (byte)0;
        await RequestAsync(index, enable ? (byte)1 : (byte)2, token: token);
    }
    public async Task SetLiveDpiAsync(LogitechSnapshot snapshot, int dpi, CancellationToken token = default)
    {
        var sensor = snapshot.Sensors.Single();
        if (!sensor.Accepts(dpi)) throw new InvalidDataException("Unsupported DPI.");
        var feature = snapshot.Features.Single(f => f.Id == 0x2201).Index;
        await RequestAsync(feature, 3, [sensor.Index, (byte)(dpi >> 8), (byte)dpi], token);
        if (await ReadLiveDpiAsync(snapshot, token) != dpi)
            throw new IOException("The mouse did not retain the requested DPI.");
    }

    public async Task<int> ReadLiveDpiAsync(LogitechSnapshot snapshot, CancellationToken token = default)
    {
        var sensor = snapshot.Sensors.Single();
        var feature = snapshot.Features.Single(f => f.Id == 0x2201).Index;
        var readback = await RequestAsync(feature, 2, [sensor.Index], token);
        if (readback.Length < 5 || readback[0] != sensor.Index) throw new IOException("Incomplete live DPI readback.");
        var dpi = readback[1] << 8 | readback[2];
        if (!sensor.Accepts(dpi)) throw new IOException("The mouse returned an invalid live DPI.");
        return dpi;
    }

    public async Task SetSensitivityAsync(LogitechSnapshot expected, int dpi, int rate, CancellationToken token = default)
    {
        if (IsGHubRunning()) throw new InvalidOperationException("Exit G HUB from its tray menu before applying Sora settings.");
        var fresh = await ReadAsync(token);
        if (fresh.Identity != expected.Identity || fresh.Sensors.Length != 1 || !fresh.Sensors[0].Accepts(dpi) || !fresh.ReportRates.Contains(rate)) throw new InvalidOperationException("These settings do not match the connected mouse's capabilities.");
        if (expected.Sensors.Length != 1 || fresh.Sensors[0].Dpi != expected.Sensors[0].Dpi || fresh.ReportRate != expected.ReportRate) throw new InvalidOperationException("Sensitivity changed outside Sora. Discard edits and refresh before applying.");
        if (fresh.OnboardMode == 1) throw new InvalidOperationException("The mouse is using firmware control. Apply a Sora control profile to acquire software control before changing live sensitivity.");
        var sensor = fresh.Sensors[0]; var dpiFeature = fresh.Features.Single(f => f.Id == 0x2201); var rateFeature = fresh.Features.Single(f => f.Id == 0x8060);
        // A failed second command must restore both values. Caller retains an on-disk original first.
        try
        {
            await RequestAsync(dpiFeature.Index, 3, [sensor.Index, (byte)(dpi >> 8), (byte)dpi], token);
            await RequestAsync(rateFeature.Index, 2, [(byte)(1000 / rate)], token);
            var current = await RequestAsync(dpiFeature.Index, 2, [sensor.Index], token);
            var period = await RequestAsync(rateFeature.Index, 1, token: token);
            if ((current[1] << 8 | current[2]) != dpi || period[0] != 1000 / rate) throw new IOException("The mouse did not retain the requested sensitivity.");
        }
        catch (Exception applyError)
        {
            try
            {
                await RequestAsync(dpiFeature.Index, 3, [sensor.Index, (byte)(sensor.Dpi >> 8), (byte)sensor.Dpi]);
                if (fresh.ReportRate.HasValue) await RequestAsync(rateFeature.Index, 2, [(byte)(1000 / fresh.ReportRate.Value)]);
            }
            catch (Exception restoreError) { throw new IOException("Apply failed and automatic restoration failed. Reconnect and restore the saved original. " + restoreError.Message, applyError); }
            throw;
        }
    }

    public async Task SetLightingAsync(LogitechSnapshot expected, IReadOnlyDictionary<byte, byte[]> states, CancellationToken token = default)
    {
        if (IsGHubRunning()) throw new InvalidOperationException("Exit G HUB from its tray menu before applying Sora settings.");
        var fresh = await ReadAsync(token);
        if (fresh.Identity != expected.Identity || states.Count == 0) throw new InvalidOperationException("The Logitech device changed. Refresh before applying.");
        var feature = fresh.Features.Single(f => f.Id == 0x8070);
        foreach (var (index, state) in states)
        {
            var zone = fresh.Lighting?.SingleOrDefault(z => z.Index == index);
            if (zone == null || state.Length != 11 || !zone.Effects.Contains(state[0]) || state[0] is not (0 or 1 or 3 or 10)) throw new InvalidOperationException("This lighting effect is unavailable for the selected zone.");
            var oldZone = expected.Lighting?.SingleOrDefault(z => z.Index == index);
            if (oldZone == null || !oldZone.State.SequenceEqual(zone.State)) throw new InvalidOperationException("Lighting changed outside Sora. Discard edits and refresh before applying.");
        }
        async Task Write(byte index, byte[] state, CancellationToken cancellation)
        {
            var wire = LogitechLighting.ToWire(state, fresh.Lighting!.Single(z => z.Index == index).Effects);
            var data = new byte[16]; data[0] = index; wire.CopyTo(data, 1);
            data[12] = 0; // RAM only. Never use the flash-persistence flag here.
            await RequestAsync(feature.Index, 3, data, cancellation);
            var read = await RequestAsync(feature.Index, 14, [index], cancellation);
            if (read[0] != index || !read.AsSpan(1, 11).SequenceEqual(wire)) throw new IOException("Lighting readback did not match the requested effect.");
        }
        try { foreach (var (index, state) in states) await Write(index, state, token); }
        catch (Exception error)
        {
            try { foreach (var index in states.Keys) await Write(index, fresh.Lighting!.Single(z => z.Index == index).State, CancellationToken.None); }
            catch (Exception restore) { throw new IOException("Lighting failed and restoration needs attention: " + restore.Message, error); }
            throw;
        }
    }

    public async Task<LogitechOnboardSlot[]> ReadOnboardAsync(LogitechSnapshot snapshot, CancellationToken token = default)
    {
        if (snapshot.ProductId != 0xc08b) throw new NotSupportedException("Onboard profile inspection is unavailable for this model.");
        var feature = snapshot.Features.Single(f => f.Id == 0x8100).Index;
        var info = await RequestAsync(feature, 0, token: token);
        var size = info[7] << 8 | info[8]; var count = info[3]; var buttons = info[5];
        if (info[0] != 1 || info[1] != 2 || count > 5 || buttons > 16 || size is < 240 or > 4096) throw new NotSupportedException("This onboard profile format has not been validated.");
        async Task<byte[]> Sector(ushort sector)
        {
            var data = new byte[size];
            for (var offset = 0; offset < size; offset += 16)
            {
                var start = Math.Min(offset, size - 16);
                var read = await RequestAsync(feature, 5, [(byte)(sector >> 8), (byte)sector, (byte)(start >> 8), (byte)start], token);
                if (read.Length < 16) throw new IOException("Incomplete onboard memory read.");
                read.AsSpan(0, 16).CopyTo(data.AsSpan(start));
            }
            return data;
        }
        var directory = await Sector(0);
        if (!LogitechProtocol.ValidChecksum(directory)) throw new IOException("The onboard directory checksum is invalid. No profile data was interpreted.");
        var slots = new List<LogitechOnboardSlot>();
        for (var i = 0; i < count; i++)
        {
            var address = directory[i * 4] << 8 | directory[i * 4 + 1];
            if (address == 0xffff) break;
            if (address != i + 1) throw new IOException("The onboard profile directory uses an unexpected address.");
            var data = await Sector((ushort)address); var valid = LogitechProtocol.ValidChecksum(data);
            var values = new int[5]; var bindings = new string[buttons]; var shifted = new string[buttons];
            if (valid)
            {
                for (var j = 0; j < 5; j++) values[j] = data[3 + j * 2] | data[4 + j * 2] << 8;
                for (var j = 0; j < buttons; j++) { bindings[j] = LogitechProtocol.DescribeBinding(data.AsSpan(32 + j * 4, 4)); shifted[j] = LogitechProtocol.DescribeBinding(data.AsSpan(96 + j * 4, 4)); }
            }
            var name = valid ? Encoding.Unicode.GetString(data, 160, 48).Split('\0')[0].Trim('\uffff', ' ', '\0') : "Invalid checksum";
            slots.Add(new(i + 1, directory[i * 4 + 2] != 0, valid, string.IsNullOrWhiteSpace(name) ? $"Slot {i + 1}" : name, values, valid && data[0] > 0 ? 1000 / data[0] : 0, bindings, shifted, data));
        }
        return slots.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); shortStream.Dispose(); longStream.Dispose();
        await Task.WhenAll(readers);
        lock (clientLock) clients[softwareId] = false;
        lifetime.Dispose(); gate.Dispose();
    }
}
