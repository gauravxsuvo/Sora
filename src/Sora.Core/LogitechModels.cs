namespace Sora.Core;

public static class LogitechModels
{
    // USB identities: https://github.com/libratbag/libratbag/blob/master/data/devices/logitech-g102-g203.device
    public static bool IsKnownMouse(ushort productId) => productId is 0xc08b or 0xc084 or 0xc092 or 0xc09d;

    public static string Name(DeviceInfo device) => !string.IsNullOrWhiteSpace(device.ProductName)
        ? device.ProductName.Trim()
        : device.ProductId switch
        {
            0xc08b => "G502 HERO",
            0xc084 => "G102 / G203 Prodigy",
            0xc092 or 0xc09d => "G102 / G203 LIGHTSYNC",
            _ => $"Logitech USB {device.ProductId:X4}"
        };

    public static bool IsConfigurationInterface(DeviceInfo device) => device.VendorId == 0x046d
        && IsKnownMouse(device.ProductId) && device.UsagePage == 0xff00 && device.Usage == 2 && device.OutputReportLength == 20;

    public static DeviceInfo[] ConfigurationInterfaces(IEnumerable<DeviceInfo> descriptors) => descriptors
        .Where(IsConfigurationInterface).GroupBy(d => PhysicalIdentity(d.Path), StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First()).OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase).ToArray();

    // HID collections belonging to one device share the remainder of their interface path.
    // Do not group by serial: many wired mice have no serial, or expose the same placeholder.
    public static string PhysicalIdentity(string path) => System.Text.RegularExpressions.Regex.Replace(path,
        @"&col\d\d|&000[0-9](?=#)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool HasG502Controls(LogitechSnapshot? snapshot) => snapshot is { ProductId: 0xc08b, Sensors.Length: 1 }
        && new ushort[] { 0x2201, 0x8060, 0x8100, 0x8110, 0x1300 }.All(id => snapshot.Features.Any(f => f.Id == id));

    public static bool HasG502Preferences(LogitechSnapshot? snapshot) => snapshot?.ProductId == 0xc08b
        && new ushort[] { 3, 0x1300, 0x8070 }.All(id => snapshot.Features.Any(f => f.Id == id));
}
