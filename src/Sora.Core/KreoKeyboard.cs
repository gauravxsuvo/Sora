using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sora.Core;

/// <summary>The keyboard is reachable only through its 2.4 GHz receiver, whose settings protocol Sora does not speak yet.</summary>
public sealed class KreoWirelessOnlyException() : IOException("The keyboard is connected through its 2.4 GHz receiver. Sora can change settings only over the USB cable for now: plug it in and switch the keyboard to wired mode.");

public sealed record KreoProbe(DeviceInfo Device, string? Model, string? Identity, string? LightingHex, string? Error, KreoState? State = null);

/// <summary>KB Hub Fw=24 configuration channel, recovered from OemDrv.exe. No keyboard input is opened.</summary>
public sealed class KreoKeyboard : IDisposable
{
    private readonly SafeFileHandle handle;
    public DeviceInfo Device { get; }
    public string Identity { get; }
    public string Model { get; }
    public KreoModel Definition { get; }

    public static IReadOnlyList<DeviceInfo> Descriptors() => HidDevice.EnumerateMatching(path =>
        path.Contains("vid_258a&pid_010c", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("vid_3554&pid_fa09", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<DeviceInfo> Enumerate() => Descriptors()
        .Where(d => d.VendorId == 0x258a && d.ProductId == 0x010c && d.UsagePage == 0xff00 && d.Usage == 1 && d.FeatureReportLength == 520).ToArray();

    /// <summary>True when a Kreo 2.4 GHz receiver is plugged in. Read-only descriptor scan.</summary>
    public static bool ReceiverPresent() => Descriptors().Any(d => d.VendorId == 0x3554 && d.ProductId == 0xfa09);

    public static string? FindConflict()
    {
        var processes = Process.GetProcessesByName("OemDrv");
        try { return processes.Length == 0 ? null : "Exit Kreo KB Hub before connecting in Sora."; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public KreoKeyboard(DeviceInfo device)
    {
        if (FindConflict() is { } conflict) throw new IOException(conflict);
        if (!Enumerate().Any(d => d == device)) throw new IOException("The selected Kreo configuration interface is no longer connected.");
        Device = device;
        handle = HidDevice.Native.CreateFile(device.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the Kreo configuration channel."); }
        try
        {
            var attributes = new HidDevice.Native.HidAttributes { Size = Marshal.SizeOf<HidDevice.Native.HidAttributes>() };
            if (!HidDevice.Native.HidD_GetAttributes(handle, ref attributes) || attributes.VendorId != 0x258a || attributes.ProductId != 0x010c)
                throw new IOException("The selected device no longer matches the Kreo keyboard identity.");
            var caps = HidDevice.GetCaps(handle);
            if (caps.UsagePage != 0xff00 || caps.Usage != 1 || caps.FeatureReportByteLength != 520) throw new IOException("Unexpected Kreo HID descriptor.");
            Identity = Convert.ToHexString(Read(0x82, 1, 6));
            Definition = KreoModel.All.SingleOrDefault(m => m.Identities.Contains(Identity)) ?? throw new IOException("Unrecognized keyboard identity " + Identity + ". This model is not supported yet.");
            Model = Definition.Name;
        }
        catch { handle.Dispose(); throw; }
    }

    internal byte[] Read(byte command, byte selector, int length)
    {
        var request = KreoProtocol.Request(command, selector, length);
        if (!HidDevice.Native.HidD_SetFeature(handle, request, request.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Kreo read request failed.");
        Thread.Sleep(20);
        var response = (byte[])request.Clone();
        if (!HidDevice.Native.HidD_GetFeature(handle, response, response.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Kreo read failed.");
        return KreoProtocol.Payload(request, response);
    }

    public byte[] ReadLighting() => Read(0x84, 0, 128);
    public byte[] ReadKeyBank() => Read(0x83, 0, 512);
    public byte[] ReadEffectPalette() => Read(0x8a, 0, 512);

    public KreoState ReadState()
    {
        var state = new KreoState(Identity, ReadLighting(), ReadKeyBank(), Read(0x86, 0, 378), ReadEffectPalette());
        state.Validate();
        return state;
    }

    public KreoState Apply(KreoState expected, KreoState desired, Action<KreoState> backup)
    {
        if (FindConflict() is { } conflict) throw new IOException(conflict);
        return KreoKeyboardTransaction.Apply(Identity, Definition, expected, desired, ReadState,
            (command, payload) => Write(command, 0, payload), backup, () => Thread.Sleep(120));
    }

    private void Write(byte command, byte selector, byte[] payload)
    {
        if (FindConflict() is { } conflict) throw new IOException(conflict);
        var report = KreoProtocol.Request(command, selector, payload.Length);
        payload.CopyTo(report, 8);
        if (!HidDevice.Native.HidD_SetFeature(handle, report, report.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Kreo settings write failed; the pre-change backup is saved.");
        Thread.Sleep(20);
    }

    public static IReadOnlyList<KreoProbe> Probe()
    {
        var result = new List<KreoProbe>();
        foreach (var device in Enumerate())
        {
            try { using var keyboard = new KreoKeyboard(device); var state = keyboard.ReadState(); result.Add(new(device, keyboard.Model, keyboard.Identity, Convert.ToHexString(state.Lighting), null, state)); }
            catch (Exception error) when (error is IOException or Win32Exception) { result.Add(new(device, null, null, null, error.Message)); }
        }
        return result;
    }

    public void Dispose() => handle.Dispose();
}
