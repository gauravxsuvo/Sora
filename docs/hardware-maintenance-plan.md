# Hardware update and maintenance requirements

Required by the user on 18 October 2026: Sora must retain useful driver, BIOS and device-firmware maintenance after daily vendor suites are removed. Vendor-app version updates do not satisfy this requirement. Other feature additions should serve reliable daily use.

## Current coverage and required work

**Latest continuation:** [hardware-maintenance-findings.md](hardware-maintenance-findings.md) records an applicable KIOXIA EVFATR.0 → EVFATR.1 advisory, fresh GCC/official-page differences, a corrected package-version fallback defect, expanded read-only inventory and the exact remaining qualification. Firmware was not applied. Eight official support entry points and explicit coverage limits are now implemented in the review candidate; they are not automatic firmware discovery.

| Hardware | Current Sora coverage | Required maintenance route / remaining gate |
| --- | --- | --- |
| Z890 GAMING X WIFI7 | Reads installed board/BIOS and present-device driver versions; matches GCC BIOS/driver lists and validates supported downloads | Verify exact board revision and compare the current official support page with GCC's index. Record BIOS release notes, prerequisites and a reviewed Q-Flash workflow. Separate firmware packages are not automatically covered by a BIOS/driver-only catalog. |
| Board chipset, LAN/Wi-Fi, audio, USB/PD | GCC table matching for detected devices with known installed versions | Verify required devices and installed packages. Check separate USB/PD firmware prerequisites. Preserve offline recovery network drivers and qualify official OEM/Windows routes without GCC. |
| GIGABYTE RTX 5070 Ti | Fan configuration/telemetry, not an update catalog | Confirm exact card SKU, revision, subsystem and VBIOS. Qualify official NVIDIA driver updates and model-specific GIGABYTE firmware tools when applicable. Driver availability is not VBIOS availability. |
| Logitech G502 HERO | Reads firmware (observed 174.0.5), preferences and onboard slots; no firmware catalog/flasher | Verify the exact device's supported official firmware route and availability. Preserve a maintenance-only vendor-tool workflow if needed. Generic Logitech instructions and other-device firmware do not qualify this G502. |
| Kreo Swarm 75 / receiver | Wired identity and configuration; receiver configuration; no qualified firmware updater | Confirm hardware/firmware revision, keyboard versus receiver targets, official package and recovery route. Software/web-control availability does not establish a firmware image or flash protocol. |
| DeepCool LD360 | Recovered display telemetry/settings; no firmware update check | Establish whether this exact revision has user-updatable firmware and an official tool. App version 1.2.14 is not a cooler firmware version. A no-update/not-user-updatable conclusion needs manufacturer evidence. |
| KIOXIA / ZEB NVMe | Reads identity, installed firmware, SMART, endurance, temperatures and TRIM | Check exact models against manufacturers' firmware support and recovery guidance. SMART health is not an update check; generic controller images are not qualified. |

## Official sources checked on 18 October 2026

- The [GIGABYTE board support page](https://www.gigabyte.com/nz/Motherboard/Z890-GAMING-X-WIFI7/support) publishes BIOS, drivers and a separate ITE USB4 PD firmware tool with BIOS/driver prerequisites. This supports a separate maintenance gate; it does not establish that this PC needs an update.
- The [G502 HERO support page](https://support.logi.com/hc/en-au/articles/360024633314-G502-HERO-Gaming-Mouse) and [official G HUB release notes](https://marketplace.logi.com/releasenotes/ghub/en) are maintenance entry points. Generic/other-model notices do not establish a current G502 firmware update. Exact-device availability remains unverified.
- [Kreo's Swarm product page](https://kreo-tech.com/collections/shark-sale/products/swarm-black-purple-wireless-gaming-keyboard) links desktop and web software; [Kreo Kontrol](https://kreo-tech.com/pages/kontrol) lists Swarm 75/X. These establish configuration-tool entry points, not a qualified firmware replacement.
- [DeepCool's LD360 page](https://deepcool.com/products/Cooling/cpuliquidcoolers/LD360-Liquid-Cooler-with-a-Multi-Line-Display-1851-1700-AM5/2024/17717.shtml) and [customer support](https://www.deepcool.com/support/customersupport/) are official entry points. A model-specific user firmware update path remains unverified.

Sources can change. Revalidate model, revision, package and instructions at maintenance time. Finding a download does not establish that it is needed, installed or current.

## Qualification checklist

- [x] HWUP-01 — Make hardware maintenance a required vendor-removal gate.
- [x] HWUP-02 — Audit current coverage: `UpdateKind` contains BIOS and Driver; peripheral/GPU/SSD firmware discovery and flashing are not implemented.
- [x] HWUP-03 — Fix failed/skipped update checks being labeled up to date; add offline/unmatched-board regression cases. Current source reports incomplete coverage explicitly.
- [ ] HWUP-04 — Preserve exact device/revision/installed-firmware/driver inventory and machine-readable evidence.
  - [x] Expanded inventory includes present PnP descriptors and NVIDIA VBIOS 98.03.58.00.ec / driver 616.92 / subsystem 41A8:1458. Board/card PCB revision and remaining peripheral firmware identities remain unverified; USB REV descriptors are not qualified firmware versions.
  - [x] Captured Windows board/BIOS, disk firmware and 81 relevant driver entries in `artifacts/recheck-execution-20261018-171753/hardware-maintenance-inventory.json`: F20 BIOS; board version is only `x.x`; SSD firmware EVFATR.0 and V0926A0. Physical revisions and remaining device firmware fields still require verification.
- [ ] HWUP-05 — Reconcile fresh GCC-index results with official board files and prerequisites; identify gaps or index lag.
  - [x] Fresh BIOS F21 agrees; NPU index 32.0.100.4404 lags official 32.0.100.4778; PD driver/package versions differ and separate firmware prerequisites are recorded. Corrected missing package metadata falling back to an unrelated device driver. Exact PCB revision, package/recovery validation and ongoing catalog reconciliation remain open.
- [ ] HWUP-06 — Qualify official GPU driver and model-specific firmware routes separately.
- [ ] HWUP-07 — Qualify G502, Swarm/receiver and LD360 maintenance routes; distinguish no update from unknown availability.
  - [x] Preserve/hash the installed G HUB G502 DFU cache: 127.03.10, matching local manifest, versus previously observed device firmware 174.0.5. Matching USB IDs do not qualify another firmware family; current offer/recovery remain unknown. No vendor executable or DFU command launched.
- [ ] HWUP-08 — Verify exact SSD firmware support and recovery requirements.
  - [x] Collect actual physical disk/volume mapping, WinRE and status-only encryption evidence. Windows RE is on the target SSD; Windows backup catalog found none and the user confirms no verified backup. See [storage preparation](storage-maintenance-preflight.md); firmware remains HOLD.
  - [x] KIOXIA explicitly supports EXCERIA G3 in SSD Utility and recommends EVFATR.0 → EVFATR.1 in its dated advisory; installed system disk matches. Data/recovery preparation, exact offered image, utility qualification and ZEB route remain open. No firmware update performed.
- [ ] HWUP-09 — Preserve required installers, release notes, signatures/hashes and recovery material outside uninstall paths.
  - [x] Archive official SSD Utility 6.6.0.09 ZIP/installer, guide, release notes and normal download agreement. Installer Authenticode Valid, Kioxia Corporation; version/hash recorded. Installer never executed; exact firmware offer, recovery media and remaining installers still pending.
- [ ] HWUP-10 — Represent unsupported/unknown firmware checks honestly in Sora and provide verified maintenance entry points.
  - [x] Implement eight reviewed official HTTPS support links with exact URL validation, explicit automatic coverage limits/index lag, and a precise dated SSD-advisory match. Missing package versions now make the automatic check incomplete; the UI identifies GCC list versions. Complete per-device firmware availability and maintenance/recovery qualification remain open.
- [ ] HWUP-11 — Before an actual update, present the exact model/revision/package, changes, prerequisites, backup/recovery and restart plan for review. Flashing, BIOS changes and driver installation are separate maintenance actions from this recheck.
- [ ] HWUP-12 — After an approved hardware update, repeat affected protocol/configuration/restore/lifecycle acceptance tests.

## Essential daily-use priorities

1. Safe autonomous cooling, reliable recovery and no competing writers, including DeepCool finding F14. Controlled service isolation and failed-probe recovery now pass complete native/firmware preservation; remaining uninstall/lifecycle cases are tracked in [fan maintenance workflow](fan-maintenance-workflow.md).
2. Keyboard typing/remaps/lighting and mouse physical press/release/DPI profiles without stuck input or corrupted settings.
3. Clear startup/resume/reconnect behavior, diagnostics, backups and hardware health.
4. Hardware maintenance as defined above.

Vendor-format macro import, application auto-switching and onboard editing remain expansion candidates until a daily-use requirement or existing profile establishes the need. Their absence stays documented; firmware/driver maintenance is required now.
