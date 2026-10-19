# Sora Program Files installation

## Current permanent installation — 0.10.10, 18 October 2026

**Installed and open for regular use:** `C:\Program Files\Sora\Sora.exe`. Permanent 0.10.9 installation completed at 23:26 IST; the 0.10.10 lighting fix was installed at 23:37 IST. All 96 installed payload files match the package. Desktop, user Start menu and common Start menu shortcuts target Program Files. The older development junction is retained as an archive, not the normal launch route.

Automatic sign-in startup is **off**: the old disabled task was first retargeted, then explicitly removed through Sora's startup-off command. Motherboard fan auto-apply is off; unused channel-4 saved profile removed and labelled unused. User data is preserved under `%LOCALAPPDATA%\Sora\data`; current daily-use changes must not be overwritten by historical test snapshots.

The user physically confirms RAM, case-fan RGB, AIO pump/radiator RGB and motherboard lighting **all off**, and a responsive window after the 0.10.10 fix. Fresh checks: **51 application groups, 46 lighting scenarios, 19 package guards**. [Release/evidence details](Sora-0.10.10.md). [Regular-use checks](regular-use-checklist.md) remain open; vendor removal/firmware HOLD.

Current checkpoint: `artifacts/recheck-execution-20261018-171753/lighting-sync-startup-20261018-233141/installed-0.10.10-checkpoint.json`. Earlier closed/temporary-install descriptions below are historical. Retained failures include the first ACL account-name audit error, missing-notices package refusal and stalled post-launch evidence collector; separate completion audit passed without stopping the normal host.

## Historical qualification — 23:02 IST, 18 October 2026

**Sora closed; startup disabled; temporary install removed; original mouse/GPU and 26 data states independently verified. Five individually successful scoped Exit cycles and ten window operations recorded.** Final separate cycle passed 14 checks. An intervening four-cycle sequence stopped during cycle 4 because the user selected Exit again before its prompt; three cycles passed beforehand, recovery/watchdog/uninstall passed, and the failed sequence is retained. Current test script refuses multi-cycle supervised tray invocations before staging, and records failure PID/Exit code. Two guard checks passed.

Fresh 51 application groups/19 package guards passed; 29 scripts parse. Final independent audit at 23:02:30 verifies original mouse 2400 DPI/mode 1/1000 Hz/five onboard slots, GPU, data/task/shortcuts/stable route, all 96 payload hashes and supported cleanup. Source application/package binaries unchanged. Exit timing includes user response; full-profile consecutive repeats/resources/Windows matrix remain open.

Post-reboot fan native/firmware settings match historical originals; final fresh pre/post baseline and services match. The earlier 59-byte failed comparison remains retained. EasyTune delayed auto start explains the post-reboot instance. An earlier application event confirms startup at 21:08:07, correlated with GCC log activity at 21:07:57–21:08:11; that does not prove the earlier caller/fan-change cause. No fan configuration writes or service changes requested.

Receiver initial batch: 8/24 failed, 56 attempts. After the user confirmed 2.4 GHz and woke the keyboard: 24/24 first-attempt reads passed. Both retained; sleep/wake and incomplete-packet reliability remain open. No keyboard configuration writes.

Evidence: `artifacts/recheck-execution-20261018-171753/remaining-audit-20261018-225308`; checkpoint: `checkpoint.json` there. [Remaining work and responsibilities](remaining-acceptance.md). **Actual game, physical display/lighting, full profiles, Windows/failure lifecycle, backup/recovery and vendor-removal gates remain open.**

### Earlier recorded phase — superseded current-state descriptions


## Current result — 18 October 2026

**Latest review candidate: 0.10.9.** It preserves unchanged standard mouse buttons natively after the user reported right-click failure during gaming with the 0.10.8 test host. **51 application groups and 19 package guards passed**, with all 96 source/staged payload hashes verified. The exact 0.10.9 package was temporarily installed and passed **15 actual checks in one supervised scoped desktop cycle**, including four physical native buttons and user-selected tray Exit, then was removed. Game, repeated-cycle and full saved-profile acceptance remain open; see [incident and correction](mouse-native-routing-qualification.md).

Review setup: `installer-mouse-native-routing/Sora-Setup-0.10.9-x64.exe` under `artifacts/recheck-execution-20261018-171753/`, **5,560,664 bytes**, unsigned. SHA-256: `1230BE8C087E812582CEFD7FE22F665BD15C0F1D2316538CDFBF3A5134F5103C`.

**Current PC state:** Sora closed, temporary 0.10.9 installation removed, all 26 original shared-data states restored. Automatic startup remains **disabled intentionally**; the original task structure/permissions, two user shortcuts and stable 0.10.5 junction are retained. Do not launch the older copy or install the review package before coordinated acceptance. Independent final audit passed at 22:21:01 IST: original mouse/GPU, 26 data states, 96 payload hashes, disabled task XML/security and both shortcuts verified. Three fan reads match the latest configuration. Fan preservation comparison failed separately: 59 native differences and a reappeared GCC controller remain unresolved; current fan settings were preserved.

**Historical qualification boundaries:** 0.10.7 passed 36 installed diagnostic checks and 43 task checks. 0.10.8 passed 33 lighting scenarios/ten hardware-UI checks, but subsequent scoped installed normal-host runs failed Exit acceptance and overlapped gaming. Their failures and recovery evidence are retained. None of these is a 0.10.9 physical acceptance result.

Vendor removal and firmware installation remain **HOLD**. [User involvement checklist](user-action-checklist.md).

### Preserved 0.10.6 artifact and qualification

Historical review artifact:

`artifacts/recheck-execution-20261018-171753/installer-build-qualified/Sora-Setup-0.10.6-x64.exe`

SHA-256: `2BF95F2E02C49578F354F7B4CA6EE168A52D9B6542F2778923B12F33A831B363`.

The installer contains the **same 96 files, byte for byte**, as `candidate-hardware-maintenance-final/`, plus a package manifest. Application code was not rebuilt. Payload is about 17.8 MB before compression. This is a conventional folder installation with `Sora.exe` as its launcher, requiring the **Microsoft .NET 8 Windows Desktop Runtime, x64**. Copying only the small executable would omit its libraries. The current machine has Desktop/Core runtime 8.0.31.

The compiler installer and ISCC executable have valid **Pyrsys B.V.** Authenticode signatures; they came from the [official Inno Setup download](https://jrsoftware.org/isdl.php), version 6.7.3. **The resulting Sora setup/application are unsigned**; verifying the compiler does not sign Sora. Evidence: `installer-toolchain/download-verification.json`, `installer-build-qualified/build-audit.json`. The compiler was installed under `E:\Sora\tools\inno-setup-6.7.3`, with its own user-level installation entry.

## Installation procedure — permanent installation completed

1. Exit every Sora instance through **tray menu → Exit Sora**, and let hardware cleanup finish.
2. The installed release is **0.10.10**, setup `installer/Sora-Setup-0.10.10-x64.exe` under the current fix evidence directory. It installs to `C:\Program Files\Sora` and creates **Start menu → Sora → Sora** and a **Settings → Apps** entry. Permanent installation is complete; do not reinstall an older review package.
3. Settings, keyboard backups and recovery records remain in `%LOCALAPPDATA%\Sora\data` for the Windows account running Sora. The installer does not import, replace or delete that directory. Use the same Windows account; elevation under a different account changes the profile path.
4. Normal launch requires elevation because Sora accesses protected hardware. The installer does not launch it or create/repoint a startup task. **From 0.10.7, normal launch only inspects registration.** To select installed sign-in explicitly, turn **Start at sign-in** off, then on from the installed copy. Actual administrative selection/rollback passed 43 checks; normal host/Settings checkbox interaction and Windows sign-in/wake remain unqualified. Older binaries, including the historical 0.10.6 setup below, retain automatic retargeting and must not be used for daily-use cutover.
5. Both existing user shortcuts were updated to Program Files, as was the common installer shortcut. The historical `E:\Sora\dist\Sora-current` junction remains available for development history; use the installed shortcuts for normal operation.

Normal startup may apply saved hardware preferences. The saved Sora mouse stage is 800 DPI while the restored hardware baseline is 2400 DPI. A successful read-only probe does not establish that startup applies and relinquishes all preferences correctly.

The installer retains normal Program Files permissions. It does not grant ordinary users write access, install PawnIO, provision the independent Gigabyte WMI schema, change vendor services, install drivers, flash firmware, or remove vendor applications. Installed probes passed using the machine's existing driver/schema setup; clean-machine prerequisite provisioning is still unqualified.

## Upgrade, repair and removal

- Close Sora through its tray menu before running a newer setup or the same setup for repair. The executable path stays `Program Files\Sora\Sora.exe`.
- Setup checks running `Sora.exe` processes across sessions and the normal-instance mutex; it refuses active copies instead of terminating them. It checks again immediately before installation. Concurrent launch/race behavior during file replacement still needs stress qualification.
- A same-version repair passed historically. The permanent 0.10.9 → 0.10.10 upgrade now also passed payload/version and user-data checks. Downgrades are refused by version comparison; interrupted replacement, obsolete-file handling and rollback remain unqualified.
- To uninstall after enabling installed startup, first turn off **Start with Windows** in installed Sora, then exit through its tray menu. The uninstaller refuses when the root `Sora` task still targets its executable, or when task inspection fails. Actual refusal passed for both enabled and disabled installed registrations, preserving all payload hashes and task XML; supported uninstall passed after original workspace-task recovery. Task-read failure injection remains unqualified.
- Uninstall through **Settings → Apps → Sora**. It removes installer-owned program files and the common Start menu shortcut. Shared profiles/backups and shared drivers are retained. No recursive user-data purge is included.
- Preserve the prior release, task XML, original shortcuts, shared-data backup and the exact installer before cutover. A rollback requires a reviewed data compatibility check; do not blindly restore old ownership records. The already-absent GPU/mouse ownership files must stay absent unless a current recovery operation establishes otherwise.

## Qualification evidence

| Check | Result | Evidence under the execution directory |
| --- | --- | --- |
| Package and refusal checks | **19 passed**, hardware-free | `installer-package-checks-final/result.json` |
| Temporary installation / repair / uninstall | **33 checks passed** | `installer-live-qualified/qualification.json` |
| Payload integrity | All 96 source files and package manifest match after installation, repair and refused uninstall | Build audit, manifest and qualification |
| Actual running-process refusal | Setup and uninstall rejected a benign test process named `Sora.exe`; that fixture has no hardware code | Running-refusal logs |
| Destination refusal | `/DIR` outside the default Program Files location rejected before payload copy | `directory-refusal.log` |
| Registration / shortcut / ACL | Correct 64-bit Apps entry, installed executable shortcut, no broad Users/Everyone/Authenticated Users write ACE at installation root | Qualification + `installation-acl.txt` |
| Installed read-only probes | CPU/LD handle, motherboard/DIMM lighting, storage/GPU/Wi-Fi, Logitech, fan capture; all six passed | Probe JSONs in `installer-live-qualified/` |
| Installed synthetic maintenance UI | All six scenarios passed, no browser launched | `maintenance-ui.json` |
| Preservation | 258 native fan bytes + six firmware configurations, original 2400 DPI/mode 1/1000 Hz, all 26 shared-data states, service states/start modes, stable junction, startup task XML | Qualification |
| Cleanup | Temporary Program Files directory, common shortcut and Apps entry removed, no reboot requested, no Sora process left | Qualification + `uninstall.log` |

The first live run hit an ACL-verifier account-name translation error after successful installation; its automatic uninstall succeeded and settings/task/service state were preserved. The verifier now asks Windows for SID-based access rules directly; the complete rerun passed. Preserve the failed run as `installer-live-qualification/`, not as an installer or hardware pass. An earlier restricted package run could not create junction fixtures; the unrestricted final run passed all 19 cases. Earlier dependency-path/COM compilation errors remain in preliminary build outputs and were corrected before qualification.

**Independent post-removal audit, 19:58:29 IST:** `installer-final-audit.json` passed a fresh fan/mouse read, full payload/source/staging hashes, 26 data states, startup XML, stable junction, service states and absence of the temporary install/registration/shortcut. All 258 native bytes and six firmware configurations match the prior checkpoint; all five mouse onboard slots, button mapping, indicator and persistent preferences match the original read-only baseline. Evidence: `installer-final-fans-readonly.json`, `installer-final-mouse-readonly.json`. No application configuration or firmware writes were requested.

**Deployment finding F17** (historically mislabeled F15/F16, both already assigned): fixed in current 0.10.7 source/candidate, with 31 policy cases, 25 shortcut fixtures, 36 installed diagnostics/repair checks and 43 actual task checks. Builds preserve foreign shortcuts. Actual installed/development selection, off, original XML/permissions/enabled restoration and enabled/disabled installed-task uninstall refusal passed. A fresh hardware/state audit passed at 20:40:15 IST. Initial verifier recovery serialization failure and its correction are preserved separately. Old binaries retain their original behavior; normal installed host, Windows lifecycle and retirement of obsolete launch shortcuts remain required. See [task evidence](startup-routing-qualification.md) and [normal lifecycle checklist](installed-lifecycle-qualification.md).

### Remaining installation checklist

- [x] Produce a setup executable from the qualified candidate without changing stable deployment/startup.
- [x] Validate required runtime/native assets, executable architecture, notices and copied file hashes.
- [x] Refuse existing build output, source/output junctions, nested links, embedded user data, missing libraries and unsupported runtime declarations.
- [x] Install, repair and uninstall from actual Program Files with read-only hardware access and preservation evidence.
- [ ] Exercise missing/broken .NET runtime detection on an isolated machine; do not remove the live machine's shared runtime just to test this gate.
- [x] Test installed-task uninstall refusal and explicit startup migration/disable/rollback (43 historical task checks); preserve original XML/security. Permanent cutover explicitly removed the old registration and leaves automatic startup off.
- [ ] Qualify normal first launch, saved-profile apply, single-instance behavior, tray exit and independent hardware restoration from the installed path.
- [ ] Qualify sign-in, sleep/wake, device reconnect, restart and Windows shutdown/sign-out from installed deployment; include pending F10/F14 failure cases.
- [ ] Test cross-version upgrade, obsolete-file handling, interrupted installation, disk-full/access failure and reviewed downgrade/rollback in isolation.
- [ ] Inspect ACLs on all installed files and perform an actual ordinary-user denied-write check; root ACE inspection is only the completed scope.
- [ ] Qualify protection/antivirus behavior and decide on code signing for distribution. Do not disable protections or add broad exclusions.
- [x] Complete user-authorized permanent installation, retarget the three normal shortcuts, retire the old startup registration and open installed 0.10.10. Keep backups and pending lifecycle gates separate from this completed installation.

## Rebuild without deployment

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1 `
  -ReleaseDirectory <qualified-folder-publish> `
  -OutputDirectory <new-empty-output-path> `
  -Compiler E:\Sora\tools\inno-setup-6.7.3\ISCC.exe
```

Do not use `build.ps1` for isolated installer review: it moves the development deployment junction. `Build-Installer.ps1` only packages the given folder and refuses existing output. Verification entry points are `Test-InstallerPackage.ps1` (no hardware) and `Test-SoraInstaller.ps1 -InstallForQualification` (temporary real installation; requires elevation and no pre-existing Program Files Sora installation).

References: [Microsoft deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [official .NET installation registration specification](https://github.com/dotnet/sdk/blob/main/documentation/specs/cli-installation-scenarios.md), [Inno installation privileges](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), [Inno application closing behavior](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm), [Inno verification instructions](https://jrsoftware.org/isdl-verify.php).
