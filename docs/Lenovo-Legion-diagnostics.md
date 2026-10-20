# Lenovo Legion diagnostics

The first Lenovo milestone adds read-only discovery. It does not expose Legion controls or establish hardware support. Both collectors avoid firmware method calls, IOCTLs and HID reports. Their JSON reports exclude serial numbers, UUIDs and device instance paths.

## Run on the Legion laptop

Copy `scripts/Collect-LenovoLegion.ps1` to the laptop. In Windows PowerShell, from the directory containing that script, run:

```powershell
.\Collect-LenovoLegion.ps1 -Output "$env:USERPROFILE\Desktop\legion-report.json"
```

The script requires Windows and built-in CIM support. It does not require Sora, .NET SDK, Vantage or additional PowerShell modules. It preserves existing reports: choose another filename for a subsequent capture. Review the report before sharing it. If access fails, the report records the affected section instead of treating it as missing hardware.

The collector records selected SMBIOS fields, WMI class/method/property metadata, Windows/CPU/GPU information, Lenovo driver versions and selected present HID hardware IDs. Driver inventory can include disconnected devices. The HID section contains model/interface IDs, not USB packet captures or full report descriptors.

## Diagnostic in a new Sora build

```powershell
Start-Process -FilePath '.\Sora.exe' -ArgumentList '--lenovo-legion-diagnose --output "C:\Users\Public\legion-report.json"' -Wait -WindowStyle Hidden
```

Use a new output path. Sora's existing application manifest requests administrator access. This command runs before the normal hardware host starts, so saved profiles and controllers are not activated. Exit status is 0 for a completed inspection or a successful non-Lenovo skip, 1 for an incomplete inspection/output error, and 2 when `--output` is missing.

The compiled diagnostic records WMI schema metadata and selected HID usage/report-length descriptors. It omits the standalone script's CPU/GPU/driver inventory. It does not read firmware settings or capability instance values. Class absence and inaccessible metadata are distinct results. A schema containing setter methods does not qualify those operations for use.

The HID candidate vendors include ITE (`048d`), Lenovo (`17ef`) and Corsair (`1b1c`). Their inclusion is for inspection, not a claim that every device from these vendors is a Legion keyboard. USB serial and product string requests are disabled for this diagnostic.

## Current limitations

- Only the non-Lenovo runtime path has been exercised on the connected Gigabyte desktop.
- Lenovo WMI and HID behavior still require a capture on the actual Legion.
- Provider timeouts are requested; Windows provider behavior can affect actual duration.
- A Lenovo machine-type name may omit the Legion marketing name. The explicit diagnostic can inspect Lenovo machines without guessing their family.
- No compatibility claim follows from a 2023 purchase date or a matching class/USB identifier.

Use the resulting model, BIOS, schemas and controller identities to select the next protocol implementation. Do not enable power, charging, lighting or fan writes from this report alone.
