# Storage maintenance preparation — 18 October 2026

**Firmware installation: HOLD. Vendor removal: HOLD.** The user confirmed that no verified backup exists. This phase collected status and preserved an official utility for review; it did not install or launch that utility, update firmware, create recovery media, reboot or uninstall anything.

## Observed recovery state

Evidence: `artifacts/recheck-execution-20261018-171753/recovery-readiness-final.json`, captured at 19:21 IST using the read-only `scripts/Collect-RecoveryReadiness.ps1`.

| Item | Observed result | Acceptance limit |
| --- | --- | --- |
| System SSD | Disk 0, KIOXIA-EXCERIA G3 SSD, EVFATR.0, approximately 1 TB; C: and D: | Re-enumerate before maintenance; disk numbers can change. |
| Other SSD | Disk 1, ZEB-MN52, V0926A0; E: | Physically separate from disk 0; not an independently verified backup. |
| Source use | C: about 373.5 GiB; D: about 23.6 GiB; combined about 397.0 GiB | Used-space estimate excludes image metadata, snapshots, growth and backup-tool requirements. |
| E: free space | About 418.1 GiB, roughly 21 GiB beyond current C:/D: use | Tight headroom; successful image creation and verification cannot be assumed. Do not fill the Sora workspace drive or count on compression. |
| Windows RE | Enabled, version 10.0.26100.9711, disk 0 / partition 4 | Located on the SSD being updated; not independent recovery after complete disk failure. No boot test performed. |
| BitLocker status | C:/D:/E: ProtectionStatus 0, ConversionStatus 0, EncryptionPercentage 0 | Reports fully decrypted with protection off at capture. No keys, passwords or protector IDs were queried. Recheck if encryption changes. |
| Windows backup catalog | `wbadmin get versions`: exit -2, no backup found | Other backup products were not enumerated; the user separately confirmed no verified backup. |

The collector deliberately reports `inventoryComplete:false` because the backup query returned nonzero. Section errors are empty; the native failure is explicitly recorded. This is not an all-clear or a successful backup. Earlier `recovery-readiness.json` and its original collector source remain preserved. Parser validation and existing-output refusal passed; the existing report's SHA-256 remained unchanged.

## Official utility preserved, without execution

The [official KIOXIA utility page](https://europe.kioxia.com/en-europe/personal/software/ssd-utility.html) lists SSD Utility **6.6.0.09**, explicitly supports EXCERIA G3 and requires administrator access and internet for firmware updates. The normal download agreement was retrieved with its parent-page Referer; direct access returned an Invalid access page. Both responses are preserved. The archive came from the exact download button's KIOXIA HTTPS URL, without using a mirror.

Archive directory: `artifacts/recheck-execution-20261018-171753/kioxia-preparation/`.

| Preserved file | Local SHA-256 |
| --- | --- |
| SSDUtility_6.6.0.09_Windows.zip | `1AACB3D8BF76556F93B551A0A07CA5C97560D572508AC4E977136A32AC7E03E5` |
| SSDUtility_6.6.0.09_Windows.exe | `C1EE2E1A0972E80B5A3599007E89D0934F2A32FBCD463542428F5064F443A3A1` |

The ZIP contains one installer, extracted by exact entry name using CreateNew. Its Windows Authenticode status was **Valid**, signer **Kioxia Corporation**, timestamp signer DigiCert; embedded file/product version **6.6.0.09**. The signing certificate's expiry is in the past, but Windows verified the timestamped signature as Valid. Full certificate metadata is in `package-signature.json`. These locally computed hashes establish the preserved bytes, not an independent manufacturer-published digest. A valid installer signature does not qualify an offered firmware image or establish rollback support.

The [Revision 20 guide](https://europe.kioxia.com/content/dam/kioxia/shared/personal/software/ssd-utility/asset/SSD_Utility_66r20_User_Guide_English.pdf) and [release notes](https://europe.kioxia.com/content/dam/kioxia/shared/personal/software/ssd-utility/asset/SSD_Utility_Release_Notes_v6.6.0.09_EN.pdf) are archived. The guide supports firmware updates with the Windows inbox storage driver; it restricts NVMe updates under Intel RST 18/19. Inventory reports Microsoft Standard NVM Express Controllers using `stornvme.inf`; actual utility recognition remains untested. Do not change storage mode/driver to force recognition. The guide's Bootable Media performs Secure Erase/Format NVMe; it is not Windows recovery media. The page also discontinues the old Bootable SSD Utility after 5.3.0004. No destructive media was created.

The dated [EXCERIA G3 advisory](https://europe.kioxia.com/en-europe/personal/news/2026/20260420-2.html) matches EVFATR.0 and recommends EVFATR.1. The utility's current exact offer, downloadable firmware bytes, restart instructions and recovery behavior have not been observed. No firmware-downgrade or rollback capability is established.

## Required backup and recovery checklist

- [x] STOR-01 — Identify physical source/destination mapping, volume use, driver environment, encryption status and current Windows RE location without configuration writes.
- [x] STOR-02 — Ask about existing verified recovery material; user reports no verified backup.
- [x] STOR-03 — Preserve official guide, release notes, agreement and utility package; record hashes, version and Windows signature result without running the installer.
- [ ] STOR-04 — Select and identify a backup destination with enough headroom for the chosen method, including snapshot/metadata/growth requirements. No external disk is currently enumerated. E: is a possible separate device with tight capacity, not a selected or validated target. Do not format, repurpose or delete existing data to make room automatically.
- [ ] STOR-05 — Create a recoverable backup of both C: and D:, including required system/EFI/recovery partitions for the chosen bare-metal restore route. Record exclusions and preserve application settings/vendor exports. A folder copy or restore point alone does not establish full system recovery.
- [ ] STOR-06 — Run the backup tool's integrity verification. Restore representative personal files, project/settings files and vendor profiles to a separate scratch location; compare their content/hashes. Document what was actually restored. Confirm the full system restore route, with a spare target or equivalent rehearsal where available; a sample restore alone is not proof of successful bare-metal recovery.
- [ ] STOR-07 — Prepare separate Windows recovery/installation media and any backup-tool recovery media required to access the backup. Identify the specific USB and preserve its data before its documented creation step. [Microsoft's recovery-drive instructions](https://support.microsoft.com/en-us/windows/experience/backup-recovery/recovery-drive) require an empty USB and separate personal-data backup. No media-creation command has been run.
- [ ] STOR-08 — Boot the intended recovery media during a reviewed restart window; confirm keyboard/mouse, source/destination storage visibility, backup catalog readability and required drivers. Exit without restoring, resetting or formatting the live machine. Record any separate full-restore rehearsal honestly.
- [ ] STOR-09 — Recheck encryption immediately before maintenance. If subsequently enabled, privately verify access to the required recovery material, without logging secrets. Current fully decrypted status does not call for a suspension command.
- [ ] STOR-10 — Revalidate exact drive identity, official support, archived package hash/signature, backup freshness and stable power. Install/launch the reviewed utility only as a separate maintenance step; identify the intended drive and record its exact offered firmware and instructions before any flash.
- [ ] STOR-11 — Present the exact firmware offer, changes, prerequisites, backup/recovery evidence, restart/power instructions and failure/escalation route for review. Stop if identity, compatibility, offer or restore route is uncertain. No automatic image substitution, forced update or speculative rollback.
- [ ] STOR-12 — After an authorized update, reread identity/firmware/SMART/volumes, verify boot and important files, then repeat affected Sora storage, resume/startup and hardware restoration checks. Preserve pre-update evidence and all failures.

Backup preparation is pending; independent Sora recheck work can continue. Next independent work remains F14 service-transition/relaunch/termination recovery, remaining physical peripheral checks, exact hardware revisions and installed-path qualification.

## G502 cached maintenance evidence from this continuation

Read-only review of `C:\ProgramData\LGHUB\depots\876190\g502_hero_dfu` found cached firmware **127.03.10**, USB IDs `046d_c08b` and `046d_aae6`, and `updateRequired:false`. Its binary SHA-256 matches its local manifest (`85FCFC43BA988E361AF5CCFB31C0B1F8238080CE699ADEC9D12F9CE4E381D268`). Selected files and an audit are preserved under `logitech-maintenance-cache/`; no Logitech program or DFU operation was launched.

The installed depot metadata identifies **2026.6.981214 / build 876190**. The actual mouse's previously read firmware is **174.0.5**. A USB-ID match and cached `updateRequired:false` do not establish compatibility across those firmware families, current online availability or recovery. This cached image is **not qualified for flashing this mouse**. G502 maintenance and G HUB removal gates remain open; no cross-family numeric comparison or claim of latest firmware is made.
