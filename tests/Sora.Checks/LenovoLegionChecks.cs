using System.Text.Json;
using Sora.Core;

static class LenovoLegionChecks
{
    public static void Run(Action<bool, string> require)
    {
        var legion = new LenovoSystemIdentity("LENOVO", "82JQ", "Legion 5 Pro 16ACH6H", "LENOVO_MT_82JQ", "GKCN00WW", HardwarePresence.Present);
        require(legion.IsLenovo && legion.IsLegionCandidate, "Machine-type identity recognizes Legion marketing name from SystemVersion");
        require(!(legion with { Manufacturer = "Other" }).IsLegionCandidate, "Legion product text from another manufacturer cannot qualify");
        require(!(legion with { Manufacturer = "NotLenovo" }).IsLenovo, "Manufacturer substring is insufficient");
        require(!(legion with { Version = "IdeaPad Gaming 3" }).IsLegionCandidate, "IdeaPad is not silently classified as Legion");
        require(!(legion with { Version = "", Sku = "" }).IsLegionCandidate, "Machine type alone does not guess Legion family or year");
        require((legion with { Version = "", Sku = "" }).IsLegionLaptop, "Pinned source model catalog recognizes a known laptop without marketing text");
        require(!(legion with { Product = "83E1", Version = "", Sku = "" }).IsLegionLaptop, "Legion Go machine type is excluded from the laptop source catalog");
        require(!(legion with { Product = "83ZZ", Version = "", Sku = "" }).IsLegionLaptop, "Unknown machine types do not inherit laptop controls");

        var inventory = HardwareDiscovery.Classify([], "", "") with { LenovoSystem = legion };
        require(inventory.Modules.All(module => !module.Available), "Legion identity cannot enable unrelated or unqualified hardware control modules");
        var schema = new LegionWmiSchema("LENOVO_GAMEZONE_DATA", LegionProbeStatus.Observed, null, "WmiProv", [],
            [new("SetSmartFanMode", [new("Data", "UInt32", false)], [])]);
        var report = new LegionProbeReport(DateTimeOffset.UtcNow, legion, [schema], [], LegionProbeStatus.Observed);
        require(report.Completed && !report.ControlsQualified && report.FirmwareMethodInvocations == 0,
            "Observed setter schema does not qualify controls or invoke firmware");
        foreach (var status in new[] { LegionProbeStatus.AccessDenied, LegionProbeStatus.TimedOut, LegionProbeStatus.Failed, LegionProbeStatus.UnexpectedSchema })
            require(!(report with { Interfaces = [schema with { Status = status }] }).Completed,
                "Incomplete WMI inspection remains distinguishable: " + status);
        require((report with { Interfaces = [schema with { Status = LegionProbeStatus.Absent }] }).Completed,
            "A successfully determined missing interface is not a probe failure");
        require(!(report with { HidStatus = LegionProbeStatus.AccessDenied }).Completed, "Unreadable HID descriptor cannot appear as successful absence");
        require(!(report with { Identity = legion with { Presence = HardwarePresence.DetectionFailed } }).Completed,
            "Failed identity read cannot produce successful completion");
        var desktop = report with { Identity = legion with { Manufacturer = "Gigabyte", Product = "Z890", Version = "" }, Interfaces = [], HidStatus = null };
        require(desktop.ProbeSkipped && desktop.Completed && !desktop.ControlsQualified, "Non-Lenovo desktop needs no firmware inspection or controls");

        var json = JsonSerializer.Serialize(report);
        require(!json.Contains("SerialNumber") && !json.Contains("InstanceName") && !json.Contains("DevicePath") && !json.Contains("UUID"),
            "Shareable report excludes device paths and unique identity fields");
    }
}
