# GCC hardware tools in Sora 0.8

The requested scope is the SSD Tool, graphics-card FAN Control, and Wi-Fi Compass. The two supplied screenshots are feature references. The investigation reads shipped GCC artifacts and records REA evidence locally under `gcc-re/evidence/`; recovered vendor code and binaries are not shipped with Sora.

## Evidence and implementation boundaries

The installed application is GIGABYTE Control Center under `C:\Program Files\GIGABYTE\Control Center`. Existing motherboard fan and RGB investigations are preserved. REA's managed inspection and native-boundary workflows are combined with focused decompilation and native API checks. Static analysis establishes code paths; only separately recorded hardware probes establish behavior on this PC.

| Tool | Questions traced | Sora integration |
|---|---|---|
| SSD Tool | Enumeration, identity, volumes, temperature, endurance, SMART rows, CSV, erase eligibility and native vendor boundary | SSD tool page, independent Windows storage reads |
| GPU fans | Adapter/channel enumeration, automatic and customized modes, dynamic/fixed curves, profiles, auto-stop and native fan programming boundary | GPU fans page, independent NVIDIA duty control and recovered native RPM/Auto Fan Stop for the observed RTX 5070 Ti |
| Wi-Fi Compass | Module identity, native WLAN use, access-point scanning, signal sampling and antenna positioning | Wi-Fi antenna page, independent Windows WLAN calls |

The implementation reports unavailable data rather than synthesizing healthy readings. Opening a page performs reads; initial fan programming requires Apply. In 0.8.1, previously applied profiles automatically return at startup/resume. SSD erase has no executable command path in Sora.

## Host verification

The read-only host inventory is saved at `artifacts/hardware-tools-host-inventory.json`:

- NVIDIA GeForce RTX 5070 Ti and Intel integrated graphics.
- Disk 0: KIOXIA-EXCERIA G3 SSD, approximately 1 TB. Disk 1: ZEB-MN52, approximately 512 GB.
- Realtek 8922AE WiFi 7 PCI-E NIC, disconnected during initial inventory; Ethernet active; Windows WLAN AutoConfig running.

These identities establish the test host, not support for every device family. The prior 29 automated Sora checks passed before the new integration.

## Completed verification

- The expanded suite passes **32 check groups** with zero failures. New checks cover malformed storage responses, unsigned counters, identity and CSV, Wi-Fi freshness and target recovery, native fan-channel isolation, GCC profiles, readback and rollback at each native write stage.
- Independent NVMe health and Identify Controller reads succeeded for both disks. Controller serials are KIOXIA `ZFPKS2N1Z4S9` and ZEB `ZCI11FT00440`; the first matches the supplied GCC screenshot. Both report Good and 100% estimated endurance, with TRIM supported. Full probe: `artifacts/storage-readonly-probe.json`.
- Independent GPU getters expose three fan channels and current/factory native curves. A bounded elevated test requested native fixed 1,500 RPM with fan stop disabled. All target settings matched readback. Over seven seconds, reported RPM rose from zero to 1,058 / 1,033 / 1,019 at 46°C. Complete original control, curve and fan-stop buffers were restored and verified. This establishes a physical response and restoration; it does not establish settled 1,500 RPM accuracy or a thermal sweep. Evidence: `artifacts/gpu-native-write-verification.json` and its original-state backup.
- An initial unelevated setter attempt was denied by NVIDIA with `NVAPI_INVALID_USER_PRIVILEGE (-137)`. A separate read-only comparison verified original-state equality. The successful test used normal Windows administrator elevation; Sora already requests that level through its existing application manifest.
- WLAN enumeration detects the Realtek adapter. Windows reports radio powered down (`0x80342002`) for scan/BSS queries. Connected signal sampling and the physical four-position comparison remain unverified until Wi-Fi is enabled and connected in Windows. No radio, network profile or location setting was changed.
- Six packaged-app checks pass: the read-only integrated probe, GPU/SSD/Wi-Fi/motherboard-fan page renders at 1080×720, and the native GPU curve editor render. Hardware readings, readable adapter names, minimum-size SMART columns, navigation and the unchanged motherboard fan page were visually inspected. Results and the verified executable SHA-256 are in `artifacts/hardware-tools-ui-verification.json`.
- The packaged Sora executable launches through its normal Windows elevation. A raw DLL launch through the `dotnet` UI-test host was blocked by Windows Application Control; no security settings were changed. Release visual checks use the packaged executable.

## Coverage and remaining boundaries

| Tool | Report | Remaining boundaries |
|---|---|---|
| SSD | [SSD reconstruction](Gigabyte-ssd-tool.md) | Secure erase, firmware updates, proprietary controller/USB commands and vendor ATA endurance decoders are unavailable. The supplied GCC screenshot disables erase for the selected KIOXIA; no erase operation was tested on either drive. |
| GPU fans | [GPU reconstruction](Gigabyte-gpu-fan-control.md) | Native RPM/stop programming is restricted to the recovered GV-N507TWF3-16GD PCI identity and verified factory maximum. No AMD reconstruction, thermal sweep or graph dragging interface is claimed. Numeric curve editing is supported. |
| Wi-Fi Compass | [Wi-Fi reconstruction](Gigabyte-wifi-compass.md) | The original strength and direction workflows are reconstructed; this host's powered-down radio prevents successful RF and connected-measurement verification. |

Tool reports give recovered behavior, binary hashes, REA evidence IDs, implementation differences and tests. Completeness must be assessed against these boundaries; this work does not claim recovery of every GCC component or controller family. No GCC binary, proprietary driver or downloaded installer is included in the Sora release.
