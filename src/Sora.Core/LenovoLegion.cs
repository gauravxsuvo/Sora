using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Sora.Core;

/// <summary>Non-unique SMBIOS fields only; identity is not evidence of writable feature support.</summary>
public sealed record LenovoSystemIdentity(string Manufacturer, string Product, string Version, string Sku, string BiosVersion,
    HardwarePresence Presence)
{
    public bool IsLenovo => Manufacturer.Trim().Equals("LENOVO", StringComparison.OrdinalIgnoreCase);
    public bool IsLegionCandidate => IsLenovo && new[] { Product, Version, Sku }
        .Any(value => Regex.IsMatch(value, @"\blegion\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    private static readonly Lazy<HashSet<string>> SourceLaptopTypes = new(() =>
    {
        using var resource = typeof(LenovoSystemIdentity).Assembly.GetManifestResourceStream("Sora.LenovoLegionModels")
            ?? throw new IOException("The Legion source model catalog is missing.");
        using var json = JsonDocument.Parse(resource);
        return json.RootElement.GetProperty("models").EnumerateArray().Select(model => model.GetProperty("machineType").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });
    public bool IsLegionLaptop => Presence == HardwarePresence.Present && IsLenovo &&
        (IsLegionCandidate || (Product.Trim().Length >= 4 && SourceLaptopTypes.Value.Contains(Product.Trim()[..4]))) &&
        !new[] { Product, Version, Sku }.Any(value => Regex.IsMatch(value, @"\bLegion\s+(Go|T[3579]\b|Tower\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    [SupportedOSPlatform("windows")]
    public static LenovoSystemIdentity Capture()
    {
        try
        {
            using var bios = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (bios == null) return new("", "", "", "", "", HardwarePresence.DetectionFailed);
            string Read(string name) => bios.GetValue(name) as string ?? "";
            var manufacturer = Read("SystemManufacturer");
            var product = Read("SystemProductName");
            return new(manufacturer, product, Read("SystemVersion"), Read("SystemSKU"), Read("BIOSVersion"),
                string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(product)
                    ? HardwarePresence.DetectionFailed : HardwarePresence.Present);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new("", "", "", "", "", HardwarePresence.DetectionFailed);
        }
    }
}

public enum LegionProbeStatus { Observed, Absent, AccessDenied, TimedOut, Failed, UnexpectedSchema }
public sealed record LegionWmiParameter(string Name, string Type, bool IsArray);
public sealed record LegionWmiMethod(string Name, LegionWmiParameter[] Inputs, LegionWmiParameter[] Outputs);
public sealed record LegionWmiSchema(string ClassName, LegionProbeStatus Status, string? Guid, string? Provider,
    LegionWmiParameter[] Properties, LegionWmiMethod[] Methods);
public sealed record LegionHidDescriptor(ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    ushort OutputReportLength, ushort FeatureReportLength);
public sealed record LegionProbeReport(DateTimeOffset CapturedAt, LenovoSystemIdentity Identity,
    LegionWmiSchema[] Interfaces, LegionHidDescriptor[] HidDescriptors, LegionProbeStatus? HidStatus)
{
    public int ConfigurationWrites => 0;
    public int FirmwareMethodInvocations => 0;
    public bool ControlsQualified => false;
    public bool ProbeSkipped => Identity.Presence == HardwarePresence.Present && !Identity.IsLenovo;
    public bool Completed => Identity.Presence == HardwarePresence.Present && (ProbeSkipped ||
        (HidStatus == LegionProbeStatus.Observed && Interfaces.Length > 0 &&
         Interfaces.All(item => item.Status is LegionProbeStatus.Observed or LegionProbeStatus.Absent)));
}
