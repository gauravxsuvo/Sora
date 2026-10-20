using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sora.Core;

/// <summary>Explicit diagnostic: class metadata and HID descriptors only. No firmware methods, IOCTLs or HID reports.</summary>
[SupportedOSPlatform("windows")]
public static class LenovoLegionProbe
{
    private static readonly (string Name, string? ExpectedGuid)[] Targets =
    [
        ("LENOVO_GAMEZONE_DATA", "887B54E3-DDDC-4B2C-8B88-68A26A8835D0"),
        ("LENOVO_OTHER_METHOD", "DC2A8805-3A8C-41BA-A6F7-092E0089CD3B"),
        ("LENOVO_CAPABILITY_DATA_00", "362A3AFE-3D96-4665-8530-96DAD5BB300E"),
        ("LENOVO_CAPABILITY_DATA_01", "7A8F5407-CB67-4D6E-B547-39B3BE018154"),
        ("LENOVO_FAN_METHOD", null),
        ("LENOVO_FAN_TABLE_DATA", null),
        ("LENOVO_FAN_TEST_DATA", "B642801B-3D21-45DE-90AE-6E86F164FB21"),
        ("LENOVO_LIGHTING_METHOD", null),
        ("LENOVO_UTILITY_DATA", null)
    ];

    public static LegionProbeReport Capture()
    {
        var identity = LenovoSystemIdentity.Capture();
        if (identity.Presence != HardwarePresence.Present || !identity.IsLenovo)
            return new(DateTimeOffset.UtcNow, identity, [], [], null);

        // Lenovo machine-type product names may not say Legion. Keep schema inspection available
        // for these machines without assuming their product family or enabling any controls.
        var schemas = Targets.Select(target => ReadSchema(target.Name, target.ExpectedGuid)).ToArray();
        LegionHidDescriptor[] descriptors = [];
        var hidStatus = LegionProbeStatus.Observed;
        try
        {
            descriptors = HidDevice.EnumerateMatching(path => path.Contains("vid_048d&", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("vid_17ef&", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("vid_1b1c&", StringComparison.OrdinalIgnoreCase),
                    includeDeviceStrings: false, throwOnMatchedDeviceFailure: true)
                .Select(device => new LegionHidDescriptor(device.VendorId, device.ProductId, device.UsagePage,
                    device.Usage, device.OutputReportLength, device.FeatureReportLength)).ToArray();
        }
        catch (Exception error) when (IsProbeError(error)) { hidStatus = Status(error); }
        return new(DateTimeOffset.UtcNow, identity, schemas, descriptors, hidStatus);
    }

    private static LegionWmiSchema ReadSchema(string name, string? expectedGuid)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI", new ConnectionOptions { Timeout = TimeSpan.FromSeconds(3) });
            using var schema = new ManagementClass(scope, new ManagementPath(name),
                new ObjectGetOptions { Timeout = TimeSpan.FromSeconds(3) });
            schema.Get();
            string? Qualifier(string key) => schema.Qualifiers.Cast<QualifierData>()
                .FirstOrDefault(item => item.Name.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value as string;
            var guid = Qualifier("guid");
            var provider = Qualifier("provider");
            var expectedMatches = expectedGuid == null || (System.Guid.TryParse(guid, out var actual)
                && actual == System.Guid.Parse(expectedGuid));
            var status = expectedMatches && string.Equals(provider, "WmiProv", StringComparison.OrdinalIgnoreCase)
                ? LegionProbeStatus.Observed : LegionProbeStatus.UnexpectedSchema;
            var methods = schema.Methods.Cast<MethodData>().Select(method =>
            {
                using var inputs = method.InParameters;
                using var outputs = method.OutParameters;
                return new LegionWmiMethod(method.Name, Parameters(inputs?.Properties), Parameters(outputs?.Properties));
            }).OrderBy(method => method.Name, StringComparer.Ordinal).ToArray();
            return new(name, status, guid, provider, Parameters(schema.Properties), methods);
        }
        catch (Exception error) when (IsProbeError(error))
        {
            return new(name, Status(error), null, null, [], []);
        }
    }

    private static LegionWmiParameter[] Parameters(PropertyDataCollection? properties) => properties == null ? [] :
        properties.Cast<PropertyData>().Select(property => new LegionWmiParameter(property.Name,
            property.Type.ToString(), property.IsArray)).OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();

    private static bool IsProbeError(Exception error) => error is ManagementException or COMException
        or UnauthorizedAccessException or System.Security.SecurityException or IOException or Win32Exception or TimeoutException;

    private static LegionProbeStatus Status(Exception error) => error switch
    {
        ManagementException { ErrorCode: ManagementStatus.InvalidClass or ManagementStatus.NotFound or ManagementStatus.InvalidNamespace }
            => LegionProbeStatus.Absent,
        ManagementException { ErrorCode: ManagementStatus.AccessDenied } => LegionProbeStatus.AccessDenied,
        ManagementException { ErrorCode: ManagementStatus.Timedout } => LegionProbeStatus.TimedOut,
        UnauthorizedAccessException or System.Security.SecurityException => LegionProbeStatus.AccessDenied,
        COMException { HResult: unchecked((int)0x80070005) or unchecked((int)0x80041003) } => LegionProbeStatus.AccessDenied,
        Win32Exception { NativeErrorCode: 5 } => LegionProbeStatus.AccessDenied,
        TimeoutException => LegionProbeStatus.TimedOut,
        _ => LegionProbeStatus.Failed
    };
}
