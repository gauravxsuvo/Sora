namespace Sora.Core;

public sealed record MaintenanceRoute(string Name, string Coverage, string Url);
public sealed record FirmwareNotice(string Title, string Detail, string Url);

/// <summary>Reviewed support entry points, not a live firmware catalog or an installer.</summary>
public static class HardwareMaintenance
{
    public const string KioxiaAdvisory = "https://europe.kioxia.com/en-europe/personal/news/2026/20260420-2.html";
    public const string KioxiaUtility = "https://europe.kioxia.com/en-europe/personal/software/ssd-utility.html";
    public const string Coverage = "The automatic check covers matched Gigabyte motherboard BIOS and drivers. GCC's lists can lag official product pages. GPU, SSD, mouse, keyboard, receiver and cooler firmware availability is not checked automatically. These support links do not mean an update is available or that a device is installed.";
    public static IReadOnlyList<MaintenanceRoute> Routes { get; } = Array.AsReadOnly(new[]
    {
        new MaintenanceRoute("Gigabyte motherboard / USB PD", "Confirm board revision and package prerequisites. Separate USB PD firmware tools may require a BIOS update and PD driver first.", "https://www.gigabyte.com/Motherboard/Z890-GAMING-X-WIFI7/support"),
        new MaintenanceRoute("NVIDIA graphics drivers", "Select the exact GPU and Windows version. Graphics drivers and card VBIOS are separate updates.", "https://www.nvidia.com/en-us/drivers/"),
        new MaintenanceRoute("Gigabyte graphics firmware", "Select the exact card SKU and revision from its label before considering firmware. No VBIOS availability check is performed by Sora.", "https://www.gigabyte.com/Support/Consumer/Download"),
        new MaintenanceRoute("Logitech G502 HERO", "Exact-device firmware availability is unknown in Sora. Preserve an official maintenance route before removing G HUB; older standalone updates are not qualified for this mouse.", "https://support.logi.com/hc/en-au/articles/360024633314-G502-HERO-Gaming-Mouse"),
        new MaintenanceRoute("Kreo keyboard / receiver", "Kontrol is a configuration entry point. Keyboard and receiver firmware packages and recovery remain unverified; request exact-model guidance from Kreo.", "https://kreo-tech.com/pages/kontrol"),
        new MaintenanceRoute("DeepCool cooler", "User-updatable LD360 firmware remains unverified. Ask DeepCool about the exact revision; the desktop app version is not cooler firmware.", "https://www.deepcool.com/support/customersupport/"),
        new MaintenanceRoute("KIOXIA SSD Utility", "Official SSD maintenance route. Confirm exact-model compatibility and make a recoverable data backup before firmware maintenance.", KioxiaUtility),
        new MaintenanceRoute("Zebronics storage support", "Exact ZEB-MN52 firmware availability and recovery remain unverified. Use manufacturer guidance for the exact drive; Sora has no firmware catalog.", "https://support.zebronics.com/")
    });

    // One dated advisory match, deliberately not a numerical version comparison.
    // EXCERIA PLUS G3, unknown firmware and other strings cannot trigger it.
    public static FirmwareNotice? StorageNotice(string? model, string? firmware)
    {
        if (!string.Equals(model?.Trim(), "KIOXIA-EXCERIA G3 SSD", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(firmware?.Trim(), "EVFATR.0", StringComparison.OrdinalIgnoreCase)) return null;
        return new("Firmware advisory matches this drive", "KIOXIA's 20 April 2026 advisory recommends EVFATR.0 → EVFATR.1 for EXCERIA G3 reliability fixes. Back up the drive, then review the current official SSD Utility instructions. Sora has not downloaded or applied firmware; this dated advisory is not a complete firmware check.", KioxiaAdvisory);
    }

    // Shell links are limited to the exact reviewed pages; never execute a discovered package.
    public static bool IsSupportUrl(string? value) => value == KioxiaAdvisory || Routes.Any(route => route.Url == value);
}
