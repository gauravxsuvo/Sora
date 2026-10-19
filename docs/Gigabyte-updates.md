# GIGABYTE Control Center updates, reproduced in Sora

GCC's update page comes from `Lib\GBT_MB_Update` (`GBT_MB_Update.dll`, `MB_Update_Lib.dll`, `Search_MB_Device_Lib.dll`) and `Lib\Update_Center`. They were decompiled with ILSpy (`gcc-re/update/decompiled`, ignored investigation output); no GCC code runs in Sora.

## What GCC does

| Part | GCC source | Data |
| --- | --- | --- |
| BIOS | `Bios_Lib.Get_Bois_Update_ItemS_NewRule` | `https://mb.download.gigabyte.com/FileList/Swhttp/DriverUpd/BIOS/ver2.ini` (UTF-16 INI). Section = board part number (from the ACPI method `GsaGetFWTagString`), `BIOSVer0` = latest. File: `FileList/BIOS/mb_bios_<model-with-dashes>_<part>_<ver>.zip`, offered only if a HEAD request returns a size. |
| Drivers | `Driver_RAW_Table_Class.Get_Update_ListS` | `Drvdata/ver2.ini` gives the data folder (`[Info] path=…`), then `DriverDesp.csv`, `DriverTable.csv`, `IDtoDriver.csv`, `PCITable.csv` and `Package-<platform>.csv`. |
| Devices | `Search_MB_Device_Class` | Present devices (`SetupDiGetClassDevs(…, 6)`), first hardware ID: PCI/HDAUDIO/ACPI → `0xDDDDVVVV` + `SUBSYS`, USB/HID → VID/PID; installed version = the driver key's `DriverVersion`. |
| Matching | as above | First `PCITable` row whose ID (and subsystem when `ispeccase` ≠ 0) matches → `IDtoDriver` rows for this Windows (column `Win1164` on build ≥ 22000) → `DriverTable` row. Installed version from the device, or from the registry for package drivers (`Driver_Ver` 99 = key, 98 = Uninstall `DisplayName`). Newer when both versions have the same number of parts and the new one is greater (`Check_Version`). Link: `DriverUpd/` + path + `Num_Version_Exe`, upper case. |
| Platform | `GetMbPlatformId` | Z890/B860/H810/Q870/W880 → Intel 800 → `Package-Intel-800.csv`, etc. |

GCC's list also carries GCC's own modules (`LiveUpdate4/GCC/…`) and advertised software (Norton); Sora omits both.

## What Sora does differently

- Board identity comes from Windows' read-only SMBIOS values (`HKLM\HARDWARE\DESCRIPTION\System\BIOS`): the manufacturer must be Gigabyte, and the board name must match **exactly one** `BIOS/ver2.ini` entry once the `(rev…)` suffix is removed. If a name matches several revisions, no BIOS is offered.
- Drivers are offered only when the installed version is known (GCC also offers "N/A" items).
- BIOS versions compare as number + letter (F9a < F10 < F20 < F20a < F21); GCC's hex comparison misorders some.
- Links must be HTTPS on `mb.download.gigabyte.com`, with no redirects followed. Downloads must arrive at the size Gigabyte lists. `.exe`/`.msi` installers must pass Windows' Authenticode check (`WinVerifyTrust`), or they are deleted.
- Nothing is installed and the BIOS is never flashed: the user runs installers and uses Q-Flash.

## Verification (17 October 2026)

**18 October continuation:** a fresh comparison found index lag against the official board page. It also reproduced an incorrect fallback when registry/package rows were missing: Bluetooth/LE Audio inherited a Wi-Fi driver version, and LAN used its device version instead of its package version. Sora now skips missing/truncated package metadata or missing/incomparable installed versions, records an incomplete-check note, and labels versions as GCC list entries. Original incorrect results and raw metadata are preserved. See [maintenance findings](hardware-maintenance-findings.md). The earlier same-as-GCC comparison below does not establish that every classification was correct or that the index is current.

On the user's Z890 GAMING X WIFI7 (BIOS F20), Sora's check returned the same three driver updates, with the same versions and links, as the list GCC itself had computed (`C:\Program Files\GIGABYTE\Control Center\GCCUpdate.txt`): Intel NPU 32.0.100.3053 → 32.0.100.4404, Intel UHD Graphics 32.0.101.6129 → 32.0.101.8331, ITE PD IT8851 0.0.0.16 → 0.0.0.17. It also found BIOS F21 (entry `8AARL032`, `mb_bios_z890-gaming-x-wifi7_8aarl032_f21.zip`, 13.8 MB). A real download of the ITE PD installer arrived complete (182,080 bytes) and validated as signed by GIGA-BYTE TECHNOLOGY CO., LTD.; the same file with one byte changed failed validation; a foreign host and a wrong size were refused. `Sora.Checks` covers board matching, BIOS ordering, version rules, hardware-ID parsing, table matching, link safety and platform selection.
