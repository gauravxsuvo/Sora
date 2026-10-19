# Hardware maintenance findings — 18 October 2026

Vendor removal remains **HOLD**. This is a source/inventory audit and a review candidate change. No BIOS, device firmware or driver was installed, and no vendor suite was uninstalled.

## 1. KIOXIA EXCERIA G3: an applicable firmware advisory

This PC's Windows system disk (disk 0, volumes C: and D:) reports **KIOXIA-EXCERIA G3 SSD, EVFATR.0**. The [manufacturer's 20 April 2026 advisory](https://europe.kioxia.com/en-europe/personal/news/2026/20260420-2.html) specifically identifies EVFATR.0 and recommends EVFATR.1 for reliability fixes. This establishes an advisory match, not permission to flash or proof that this is the latest possible firmware.

The [official SSD Utility page](https://europe.kioxia.com/en-europe/personal/software/ssd-utility.html) explicitly supports EXCERIA G3 and lists utility 6.6.0.09. It requires administrator access and internet for firmware updates; recognition depends on the storage/driver configuration. Preserve a recoverable data backup and follow current official instructions.

Sora now matches this one dated advisory by the complete observed model and firmware string. The SSD page displays it even when SMART health is Good. Another model, unknown firmware, or EVFATR.1 does not inherit the warning; absence of a warning never means firmware is current. No live SSD firmware catalog or flasher is implemented.

### Maintenance preparation before any update

**Continuation:** [storage-maintenance-preflight.md](storage-maintenance-preflight.md) records the official signed utility archive, guide review and actual recovery inventory. User confirms no verified backup. WinRE is enabled on the target SSD; no independent recovery boot has been qualified. Firmware remains HOLD.

- [ ] Verify a current full backup of C:/D: on another physical device and a usable restore route. E: being another volume alone is insufficient; verify physical-device separation and backup integrity.
- [ ] Prepare recovery media and verify that any required disk-encryption recovery material is available privately; do not put recovery secrets in project logs.
- [ ] Recheck the exact drive model, installed firmware, current advisory and utility compatibility.
- [ ] Preserve the official utility package, release notes, instructions, SHA-256 and signature result outside vendor uninstall paths.
- [ ] Review the utility's exact offered firmware and prerequisites before authorizing an update. Installing or launching a tool that might update firmware requires a separate maintenance step.
- [ ] Follow the current manufacturer restart/power instructions; afterward reread firmware/SMART and repeat storage and affected lifecycle checks.

## 2. Motherboard: index agreement and index lag

Installed board **Z890 GAMING X WIFI7**, BIOS **F20**; SMBIOS PCB version remains only **x.x**. PCI silicon revision does not establish PCB revision.

The [official board support page](https://www.gigabyte.com/Motherboard/Z890-GAMING-X-WIFI7/support) lists F21, consistent with the fresh GCC index. Its NPU entry is **32.0.100.4778**, while GCC's fresh metadata still offers **32.0.100.4404**; installed is **32.0.100.3053**. The separate PD firmware tool is version **20250109**, with BIOS and PD-driver prerequisites; the page's PD driver is **0.0.0.16**, while GCC offers **0.0.0.17**. These mismatches require review, not automatic replacement of a URL or a claim of complete coverage.

The index also offers Intel graphics 32.0.101.8331. A board-name match still requires physical revision verification before BIOS maintenance. No PD firmware version has been read, so an available tool is not evidence the PD controller needs flashing.

### Reproduced Sora defect and correction

The original fresh check classified Bluetooth, LE Audio and LAN packages using unrelated device-driver versions. GCC marks these as registry/package version checks (98/99), but omits the necessary rows from Package-Intel-800.csv. Sora's switch fell through to the device version when the package row was missing. In particular Bluetooth/LE Audio inherited the Wi-Fi version **6102.24.143.0**.

The matcher now refuses missing/truncated package metadata, missing package versions and incomparable version shapes, and emits an incomplete-check note. It never substitutes the associated PCI device's version for an unknown package version. Original incorrect output is preserved as `hardware-maintenance-gcc-fresh.json`; raw index version **26100201** is preserved in `gcc-metadata-maintenance/`. Corrected results are separate evidence. UI labels now say **GCC list**, avoiding an unsupported latest-version claim.

The corrected live result has **two confirmed available entries** (BIOS and Intel graphics) and **seven explicit unknown package checks**, including NPU and PD where installed *package* versions cannot be established. Their observed device-driver versions remain useful inventory; they are not substituted for missing registry evidence.

## 3. GPU driver and VBIOS are separate

The read-only NVIDIA query reports device **2C05:10DE**, subsystem **41A8:1458**, VBIOS **98.03.58.00.ec**, driver **616.92**, bus 02:00.0. Windows reports build **26300**, Windows 11 Pro x64. Exact card SKU/PCB revision remains unconfirmed from a physical label; the subsystem alone is insufficient for selecting a firmware image.

[NVIDIA's official driver page](https://www.nvidia.com/en-us/drivers/) is a driver maintenance route. [GIGABYTE's download center](https://www.gigabyte.com/Support/Consumer/Download) requires exact product selection. The [WINDFORCE OC support page](https://www.gigabyte.com/Graphics-Card/GV-N507TWF3OC-16GD/support) is only a candidate until SKU is confirmed; its generic BIOS warning is not a downloadable matching update. Current/latest driver and VBIOS availability are not qualified by this audit.

## 4. Peripheral and second SSD routes

| Target | Evidence / maintenance route | Remaining qualification |
| --- | --- | --- |
| G502 HERO | Prior HID++ read: 174.0.5. Present USB 046D:C08B, REV_7400. [Exact support](https://support.logi.com/hc/en-au/articles/360024633314-G502-HERO-Gaming-Mouse). | The [old standalone updater article](https://support.logi.com/hc/en-za/articles/360025289793-G502-HERO-Gaming-Mouse-Firmware-Update) describes 127.1.7 and profile erasure. It is not qualified for this 174.x device. Generic Options+ notices are not a G502 maintenance route. Current exact-device availability/recovery remains unknown. |
| Kreo Swarm 75 / receiver | Present receiver 3554:FA09, REV_0102. [Kontrol](https://kreo-tech.com/pages/kontrol) is a configuration entry point. | Keyboard versus receiver firmware identity, official exact-revision package, supported flash/recovery route and paired-model binding remain unknown. No undocumented firmware commands were sent. |
| DeepCool LD360 | [Customer support](https://www.deepcool.com/support/customersupport/) and [software downloads](https://www.deepcool.com/downloadpage/). | DeepCreative 1.2.14 is an application version. User-updatable LD firmware and recovery need exact-revision manufacturer evidence. No-update/not-user-updatable is not established. |
| ZEB-MN52 | Installed V0926A0. [Official support](https://support.zebronics.com/) and [driver index](https://support.zebronics.com/drivers). | No exact-model firmware package or recovery route qualified. An unsuccessful public search does not prove that firmware updates do not exist. |

USB REV/bcdDevice strings are inventory descriptors, not independently verified firmware versions. Vendor daily-control independence does not establish that their maintenance tools can be discarded permanently.

## 5. Sora coverage and evidence

Updates now provides eight reviewed HTTPS support entry points with explicit coverage descriptions. These open web pages only, through an exact URL allow-list. They do not execute installers, launch vendor apps or claim that the displayed hardware is present. The automatic motherboard check remains GCC-index based and can lag official product pages.

Evidence root: `artifacts/recheck-execution-20261018-171753/`:

- `hardware-maintenance-inventory-expanded.json`: Windows, board, BIOS, disks, present PnP descriptors, driver records and read-only NVIDIA VBIOS/driver inventory; zero configuration writes, section errors recorded.
- `gcc-metadata-maintenance/`: public source metadata used to reproduce missing package rows; originals retained.
- `hardware-maintenance-gcc-fresh.json`: pre-fix result, including the incorrect classifications.
- Later build/test/UI/read-only and corrected catalog evidence is recorded in `hardware-maintenance-checkpoint.json` and the execution ledger.

HWUP-04/05/06/07/08/09 remain partial where exact revisions, installed firmware, current availability or recovery material are unknown. HWUP-10 has a qualified partial implementation (explicit limits, support routes, one dated SSD advisory), not complete firmware-update coverage. HWUP-11/12 remain open before an authorized update and its post-update acceptance checks.

## Qualification outcome

Final review candidate: `candidate-hardware-maintenance-final/` (0.10.6), separate from stable 0.10.5. **48 automated groups passed**, including missing/truncated/unknown package metadata, exact advisory identity and URL refusal cases. **Ten hardware/UI checks and six synthetic maintenance UI scenarios passed.** The fresh read-only probe matched the real system SSD advisory; SSD/advisory and support-route views were visually inspected.

`hardware-maintenance-qualified/hardware-maintenance-qualification.json` passed at **19:07:56 IST**: two confirmed catalog entries/seven unknown notes, six fan native/firmware configurations preserved, all 26 prior shared-data file states unchanged, original vendor services unchanged, Sora/KB Hub closed, stable deployment 0.10.5. No hardware configuration writes, update, uninstall or deployment change occurred.

The initial orchestration result remains failed in `hardware-maintenance-qualification.json`: its assertion expected the old four-entry count after the corrected matcher intentionally refused more unknown package versions. That verifier expectation was corrected; the entire read-only qualification was repeated in a separate directory. The initial failure and its source are retained, not overwritten.
