# GCC SSD Tool reconstruction

The Sora SSD page reads physical drive identity, mounted drive letters, capacity, firmware, serial number, bus, TRIM support, NVMe SMART health and legacy ATA SMART attributes. It refreshes every five seconds while visible and exports the selected drive's SMART report as CSV. It loads no GIGABYTE storage DLL or vendor kernel driver.

## Evidence and recovered behavior

Inspected the installed `C:\Program Files\GIGABYTE\Control Center\Lib\MBStorage` artifacts using the locally installed [REA](https://github.com/morluto/rea) 6.5.0 CLI and ILSpy 8.2. REA operations were `inspect-managed-artifact` and `inspect-managed-native-boundaries`; neither executes the target. Local retained evidence is `gcc-re/evidence/rea-storage-managed.json` and `rea-storage-boundaries.json`; focused decompilation is in `gcc-re/decompiled/Gigabyte.ComputerSystemHardware.Storage` and `gcc-re/decompiled/MBStorage.Internal`. These ignored investigation artifacts remain in the workspace.

| Artifact | SHA-256 |
| --- | --- |
| `Gigabyte.ComputerSystemHardware.Storage.dll` | `7b0ea195fc64544148e720aa05205384aa98132633c632b4de48e581cc853886` |
| `MB.CPNT.Internal.dll` from MBStorage | `e84ed9a3aecf3a2d760842313a7238405a11708b207367cb62bdd6eee5c68fa3` |

Managed observations:

- `StorageGroupPanel` starts a delayed 200 ms discovery and then a 5,000 ms hardware-monitor timer. Rescan stops the monitor and disables the content while discovery runs. Selecting a physical disk populates summary, SMART, status and erase panels.
- `StorageSummaryPanel` exposes model, firmware, serial, logical drive letters, power-on time and feature information. `StorageStatusPanel` shows Celsius/Fahrenheit, endurance and Good/Caution/Bad. `StorageEnduranceRule.ToLevel` maps zero remaining endurance to Bad and positive values at or below 10 to Caution.
- `StorageSmartPanel` enables export when SMART rows exist, suggests `SSD{physicalAddress}-{yyyyMMddTHHmmss}.csv`, and writes identifier, name and raw values. Its displayed "Normalized" label also covers decoded NVMe values, which are not ATA normalization scores.
- `SmartAttributeStructConverter` recovers the standard NVMe warning, temperature, spare, spare threshold, percentage used, data units, host commands, busy time, power cycles/hours, unsafe shutdown, media-error and error-log counters. The additional converter covers thermal timing and eight temperature sensors. The managed implementation adds two 64-bit halves of a 128-bit counter; Sora instead decodes the complete unsigned 128-bit number.
- `GenericPCIeCommand.ReadSmartAttribute` delegates to `GbtNvme.dll`. REA confirms managed declarations into `ToolBoxDLL.dll`, `pcie_bridge.dll`, `RtsAta.dll`, `RtsNvme.dll`, `igcliXdll.dll`, `GbtNvme.dll`, plus Windows `kernel32.dll`. This proves the managed/native boundary, not the internal native command implementation.
- Generic-drive `SecureErase()` returns false. Controller-specific classes use Phison/RealTek/Innogrit implementations. For example, the Phison path calls SATA `Security_Erase` or native `Vuc_Format_Nvm_5012`/`Format_Nvm`, depending on identified controller. `StorageGroupPanel` enables erase only when feature flag `SecureErase = 8` is present and the selected drive is not the Windows system disk. It asks Yes/No before launching a worker and stops health polling during the operation. The text says it cannot operate on boot drives with a partition; the inspected UI guard actually compares selected logical drive letters with the Windows system-directory root.

Sora presents secure erase as unavailable. No erase, format, sanitize, firmware-update or proprietary controller command is implemented or tested. Firmware updates visible in the storage plugin's internal host integration are outside the requested SSD health page. Native erase routines, proprietary USB bridges and vendor wear decoders have not been completely reconstructed; no claim of complete GCC parity is made.

## Independent Windows implementation

`StorageInventory.Scan()` enumerates `Win32_DiskDrive`, joins physical partitions to logical volumes, and reads `STORAGE_DEVICE_DESCRIPTOR` and `StorageDeviceTrimProperty`. NVMe identity comes from a read-only Identify Controller query: `IOCTL_STORAGE_QUERY_PROPERTY`, `StorageAdapterProtocolSpecificProperty` (`49`), protocol NVMe (`3`), data type Identify (`1`), CNS Controller (`1`), 4,096-byte payload. Serial/model/firmware are fixed ASCII fields at offsets 4/24/64 with lengths 20/40/8. Generic Windows storage descriptors can expose a namespace identifier as the serial; the controller's actual manufacturer serial takes precedence. Unsupported Identify requests retain descriptor/WMI identity.

NVMe health uses the same query IOCTL with `StorageDeviceProtocolSpecificProperty`, `ProtocolTypeNvme`, `NVMeDataTypeLogPage`, log page `02h`. Returned protocol kind, descriptor sizes, offsets and full payload bounds are checked before parsing both response types. Empty/malformed logs fail closed, and missing/implausible temperatures remain unknown. Microsoft's [Identify Controller layout](https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_identify_controller_data) defines the identity fields.

NVMe life remaining is `max(0, 100 - PercentageUsed)`, distinct from available spare. Health also considers the device's critical-warning flags and spare threshold. Counters retain all 128 unsigned bits; data units explicitly show their 512,000-byte multiplier. Remaining life is an endurance estimate, not a prediction of time to failure. Microsoft documents the [NVMe query mechanism](https://learn.microsoft.com/en-us/windows/win32/fileio/working-with-nvme-devices) and [health-log fields](https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_health_info_log).

The ATA fallback permits only `SMART READ DATA` (`B0h/D0h`) and `SMART READ THRESHOLDS` (`B0h/D1h`) through `SMART_RCV_DRIVE_DATA`. The legacy IOCTL requires opening the disk with read/write access; Sora sends only these two read commands and never enables SMART or initiates a device self-test. Complete SMART sectors must pass their checksum. Attribute values, worst values, thresholds and all six raw bytes are retained. Without a model-specific decoder, vendor wear IDs and power-on units remain uninterpreted; Sora never invents an ATA life percentage or hours. A crossed returned threshold reports Bad; otherwise ATA overall health remains Unknown. Unsupported RAID, USB, policy restrictions or missing privileges appear as an explicit per-drive SMART failure while identity remains visible.

CSV captures the actual sample timestamp, quotes all fields, preserves large counters and protects identity strings from spreadsheet formula interpretation. Failed refreshes clear prior readings. Hidden pages stop polling; discovery runs off the UI thread and never overlaps another scan.

## Verification

`StorageChecks.Run()` covers empty logs, protocol offsets and malformed bounds, unsigned 128-bit counters, Kelvin conversion, warning flags, wear over 100%, unavailable temperature, ATA checksum validation and unknown vendor wear, plus CSV quoting/formula handling. This is a deterministic protocol/parser check; it does not establish successful ATA or proprietary USB bridge access on untested hardware. Actual host probe results are reported separately in the hardware-tools integration evidence.

The standalone probe was built and run on the user's Windows host on 2026-10-17. Default sandbox CIM access was denied; the same read-only probe succeeded outside the sandbox. Evidence: `artifacts/storage-readonly-probe.json`, generated by `tools/storage-probe`. The first integrated attempt exposed missing WMI path metadata in a projected query; changing to `SELECT * FROM Win32_DiskDrive` fixed association traversal and recovered all mounted volumes.

| Host drive | Read-back result |
| --- | --- |
| Disk 0, KIOXIA-EXCERIA G3 SSD, 1,000,202,273,280 bytes | NVMe; firmware EVFATR.0; C:, D:; Windows system disk; TRIM supported; 45.9°C; 100% estimated endurance; 1,336 hours; 22 SMART rows |
| Disk 1, ZEB-MN52, 512,105,932,800 bytes | NVMe; firmware V0926A0; E:; TRIM supported; 40.9°C; 100% estimated endurance; 15,147 hours; 21 SMART rows |

Both drives returned critical-warning `0`, available spare `100`, percentage used `0`, and media-error count `0`. Disk 0's model, firmware, volume mapping and power-on hours agree with the supplied GCC screenshot; temperature is a fresh sample and differs with time. No storage configuration or destructive commands were sent. The deterministic suite completed with **30 passed, 0 failed**. Native ATA transport, proprietary bridge paths and secure erase remain unverified/unsupported as described above.

Visual review caught the generic descriptor serial representing the NVMe namespace rather than the controller serial. The Identify Controller query was then added and verified on both physical drives: Disk 0 returns `ZFPKS2N1Z4S9`, matching GCC exactly, and Disk 1 returns `ZCI11FT00440`. Model/firmware/SMART/volume behavior remains unchanged. The standalone probe JSON was refreshed with these manufacturer serials; parser checks additionally cover Identify payload bounds, fixed-field offsets, trimming and empty data.
