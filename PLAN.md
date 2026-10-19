# Sora UI redesign plan

## UI/UX complete — 0.11.0

All five refinement phases are approved. Preserve the shared tokens/components, approved layouts, persisted preferences and hardware integrations. Future visual changes require a demonstrable bug, accessibility issue or specific usability problem. See [release notes](docs/Sora-0.11.0.md), [brand contract](brand.md) and [component library](docs/component-library.md).

The installed-machine checkpoints below are historical hardware qualification records, not a statement that the newly packaged release has been installed or that startup is currently disabled. Latest verified saved tray/display-startup values are both true, and Windows sign-in startup is enabled. Publishing this update does not modify them.

## Current checkpoint — installed 0.10.10; physical lighting fix confirmed

**Permanent installation complete:** `C:\Program Files\Sora\Sora.exe`, with Desktop and both Start menu shortcuts targeting it. Automatic startup is off and its task registration is absent. Sora remains open for user-requested regular use; this supersedes the older preview/closed-state entries below.

- [x] Close the preview, restore its temporary preference flags and preserve user edits.
- [x] Install 0.10.9 permanently, then upgrade to fixed **0.10.10**; verify all 96 installed payload files.
- [x] Fix synced startup replaying stale per-zone colours and falsely reporting applied sync (F21). Pass **51 application groups, 46 lighting scenarios and 19 package guards**.
- [x] User confirms RAM, case-fan RGB, AIO pump/radiator RGB and motherboard lights **all off**, window responsive.
- [x] Keep motherboard fan auto-apply off, remove unused channel-4 profile and label it unused. Case-fan motors use SATA power; ARGB does not provide speed control.
- [x] Stop only the CPU-bound post-launch audit helper; retain its partial evidence and verify installation independently. Normal Sora host was not stopped.
- [ ] Complete [regular-use checks](docs/regular-use-checklist.md): actual game/custom input, display/effects, reconnect, sleep and planned restart.
- [ ] Complete remaining backup/recovery, controller ownership, firmware and elapsed-use gates before vendor removal.

Evidence: `artifacts/recheck-execution-20261018-171753/lighting-sync-startup-20261018-233141/installed-0.10.10-checkpoint.json`. Details: [0.10.10 fix](docs/Sora-0.10.10.md), [installation](docs/program-files-installation.md). **Vendor removal and firmware installation remain HOLD.** Earlier records below are historical; their original failures and scope limits are retained.

## Historical manual Sora session — 23:16 IST (now closed)

Latest tested **0.10.9** is open from the separate reviewed candidate, PID **23452**, with a visible responding Sora window. This is the user-requested manual session. Automatic mouse control, motherboard fan startup and lighting application are temporarily paused; display startup and reviewed GPU Automatic remain available. No installation, stable-route change or startup enable was performed.

Five affected preference files are backed up under `artifacts/recheck-execution-20261018-171753/manual-run-20261018-231527/backup/`. A hidden session supervisor (PID 15776) waits for this Sora process to exit, then restores temporary startup fields while preserving other edits/new mouse lighting files. Use **tray → Exit Sora** when finished. Preferences are currently staged; recovery result will appear as `session-complete.json` in that directory. If another Sora instance is running at recovery, restoration is deferred and recorded.

Launch evidence: `artifacts/recheck-execution-20261018-171753/manual-run-20261018-231527/launch.json`; staging journal: `preferences-staged.json`. Prior closed/restored-state descriptions below are historical while this manual session is active. Vendor removal/firmware HOLD remains.


## Latest qualification — desktop repeats completed; removal on HOLD

Current checkpoint: `artifacts/recheck-execution-20261018-171753/remaining-audit-20261018-225308/checkpoint.json`. **Sora is closed, temporary installations removed, all 26 original data states restored, startup disabled.** Recorded scope: **five successful start/Exit cycles and ten window operations**, including the earlier physical Right/Middle/Back/Forward check. Original mouse/GPU independently matched after each successful cycle. One extra early user-selected Exit aborted an intervening sequence; user confirmed clicking Exit again when the icon reappeared. That failed sequence remains recorded. Future supervised tray invocations require one cycle at a time.

**Fresh checks:** 51 application groups, 19 package guards, 29 script parses and two no-staging refusal guards passed. Independent final audit passed at **23:02:30 IST**: original mouse 2400 DPI/mode 1/1000 Hz/all five slots, GPU, 26 data states, disabled task XML/security, shortcuts and 96 payload hashes. The 22:49 IST Windows reboot occurred while Sora was closed; active-host reboot acceptance remains open.

**Fans:** post-reboot 258 native bytes and six firmware settings match historical originals. Final test preserved its fresh fan/service baseline. Earlier 59-byte differences/failures remain retained; exact earlier cause and controller ownership/relaunch are open. EasyTune is delayed automatic, explaining its post-reboot instance.

**Receiver:** initial batch failed 8/24 reads (56 attempts, 15 first-attempt failures). After user confirmed 2.4 GHz mode and woke the keyboard, **24/24 first-attempt reads passed**. Both runs retained; sleep/wake and historical incomplete-packet reliability remain open. No keyboard configuration writes.

**Still open:** actual game/custom input, full saved profiles/physical output, Windows lifecycle/real failure recovery, verified backup/recovery, exact identities, hardware updates and vendor-disabled/post-removal observation. **Vendor removal and firmware installation HOLD.** Scoped repetitions across recorded runs do not qualify five consecutive full-profile cycles, resources or cleanup latency; observed Exit times include user wait.

Next: [remaining engineering/user acceptance](docs/remaining-acceptance.md), [user checklist](docs/user-action-checklist.md). Details: [installed lifecycle](docs/installed-lifecycle-qualification.md), [mouse incident](docs/mouse-native-routing-qualification.md). Earlier dated entries below describe their historical state.

## Hardware recheck checkpoint — 18 October 2026

**Resumed on 18 October at the user's request. Vendor removal remains on HOLD.** Full checklist: [recheck-plan.md](recheck-plan.md). Results and limitations: [recheck-results.md](recheck-results.md). Existing UI/Logitech history below is preserved. The pause state below describes the prior checkpoint; current continuation evidence is recorded in the results.

Evidence directory: `artifacts/recheck-execution-20261018-171753/`; pointer: `artifacts/recheck-active-run.txt`.

Current continuation checkpoint: `artifacts/recheck-execution-20261018-171753/remaining-audit-20261018-225308/checkpoint.json`. Five successful scoped Exit cycles/ten window operations; native/firmware fan settings match original after reboot. Actual game, full profiles, Windows lifecycle and controller ownership remain open.

**Latest review candidate:** `candidate-mouse-native-routing-final/` (0.10.9), setup `installer-mouse-native-routing/Sora-Setup-0.10.9-x64.exe` under the execution directory. Temporarily installed and tested in one scoped desktop cycle, then removed. Prior 0.10.8 normal-host attempts remain failed; prior 0.10.7 installed/task evidence remains historical. See [mouse routing/incident](docs/mouse-native-routing-qualification.md).

**Scope clarified by the user:** choose additions that support efficient daily use; **hardware drivers, BIOS and device firmware updates are required**. Track that expanded removal gate in [docs/hardware-maintenance-plan.md](docs/hardware-maintenance-plan.md). Vendor app updates are excluded from that requirement. Optional vendor features remain documented until a concrete daily-use need is established.

### Completed in this execution

- [x] Incident cleanup and 0.10.9 native routing source correction: original data/native mouse ownership recovered, temporary installation removed, Sora closed/startup left disabled. Pass **51 application groups, 19 package guards, six exit-policy simulations and idle-session refusal**. Subsequently pass one supervised 15-check installed cycle with four physical buttons and genuine tray Exit; game/repeated/Windows gates remain open. Preserve F19 historical physical failure and F20 unexplained 59-byte fan difference/reappeared EasyTune controller; live and removal gates remain open.

- [x] Preserve source/workspace baseline, vendor target binaries, startup task exports and Sora data; verify all 26 configuration backup hashes. Complete vendor profile exports/offline reinstallers remain pending.
- [x] Reproduce three defects: corrupt receiver payload acceptance, corrupt receiver ACK acceptance, and incomplete wired rollback. Implement receiver validation/serialization and shared wired transaction recovery, with captured-report and failure-injection checks.
- [x] Run deterministic suites: prior checkpoints **42/46/48/49/50 passed**, current **51 passed, 0 failed** (`mouse-native-routing-review/checks-final.txt`). Clean Release publishes; verifier scripts parse; preceding hardware/UI verifier and fan-stop refusal guard checks remain preserved. Remaining failure-path qualification stays open.
- [x] Verify wired Swarm 75 lighting, custom effect palettes, per-key colors and F12-to-F11 mapping through device readback; verify physical F12 delivers F11 in Windows. Restore and compare original settings.
- [x] Verify G502 bounded runtime apply/readback/restore, screen/audio lighting checks and preferences. Verify all five onboard profiles unchanged afterward. Physical G6 hold/release was subsequently completed below; full button coverage remains pending.
- [x] Complete elevated read-only CPU, fan, motherboard/DIMM lighting, GPU, storage and Wi-Fi probes. These establish device access with vendor services still present, not independence from those services.
- [x] Preserve/extract current DeepCool 1.2.14 ASAR and disassemble its changed main bytecode offline. Add current display/fan conflict process names. The subsequent protocol/lifecycle comparison is recorded below.
- [x] Complete six receiver read-only transactions without checksum failures. Execute combined receiver profile test and verify restoration of original lighting, 504-byte key map and complete 490-byte palette after its failure.

### Open failure and exact continuation

- [x] Add bounded per-step receiver traces, including failed/short replies and partial-write evidence. Errors now distinguish timeouts, missing packets, corrupt reports and short blocks. New failure tests cover all three receiver apply stages and backup refusal.
- [x] Repeat combined receiver apply with traces: palette/key-map/lighting matched; original blocks restored. Six subsequent read-only cycles: **24/24 reads passed on first attempt**. Physical receiver F12 delivered F11; original settings restored. Evidence: `kreo-receiver-profile-traced.json`, `kreo-receiver-read-reliability.json`, `kreo-receiver-physical-input.json`.
- [ ] **Receiver reliability investigation remains open:** the original failed apply is retained as failed. A later successful traced run still observed incomplete palette replies (33/35 or 34/35 packets) before retries succeeded; exact original failure cause and transport lifecycle behavior are not established.
- [x] Recover current DeepCool LD function families offline and compare ten functions: eight packet/numeric helpers match after explicit lexical relocations; two lifecycle functions changed. Review the new bounded-retry/removal path. Evidence: `deepcool-1.2.14/ld-comparison/`.
- [x] Collect five fresh CPU samples with every DeepCool service stopped and no DeepCool process present, then restore all service states/start modes. Sensor independence passed; the larger fan-state preservation check FAILED and was recovered as described below.
- [x] Investigate and recover DeepCool stop-time fan drift: its stop handler restores a stale startup snapshot. Two StartPWM bytes changed (header 4: 66→64, header 5: 63→64). Restore exactly those two original bytes and verify all six headers against the pre-test native snapshot. Evidence: `fan-drift-restoration.json`; final independent re-read is part of unattended qualification.
- [x] Guard `Verify-IndependentSensors.ps1` against automatically stopping a running fan helper without a qualified hardware-state recovery workflow. Do not repeat service-stop/uninstall cases until finding F14 is addressed in that workflow.
- [x] Verify **three physical G502 G6 hold/release cycles** using the production runtime plus independent mouse DPI reads (800 regular / 400 held). Restore original 2400 DPI/mode/rate/map/lights/preferences and all five onboard slots. Evidence: `logitech-physical-shift.json`. Moving/clicking, reconnect/restart while held and other buttons remain open.
- [x] Isolate independent host shutdown cleanup steps; inject synchronous, asynchronous and cancellation failures at every cleanup position. **45 deterministic groups passed**, clean Release publish of candidate **0.10.6**. Current candidate: `candidate-recheck-final/`, with source/diagnostic changes included. Stable deployment/startup task remain unchanged.
- [x] Fix update-check status: failed/offline/skipped checks cannot report up to date. **46 groups passed** after this change. This stage used `candidate-update-maintenance/`; `candidate-recheck-final/` is the earlier build used for the initial ten unattended hardware/UI checks.
- [x] Unattended qualification: ten hardware/UI checks, 26 lighting-editor cases and 13 synthetic GPU lifecycle cases passed; final fan snapshot still matches all six pre-isolation native blocks. Five screenshots visually inspected. Evidence: `continuation-unattended-summary.json`, `hardware-tools-final/`.
- [x] Capture read-only maintenance inventory: BIOS F20, board version reported only as `x.x`, SSD firmware EVFATR.0 / V0926A0 and 81 relevant driver entries. Exact hardware revisions and peripheral firmware availability remain open.
- [x] Complete application exit isolation as well as host cleanup: window failure cannot skip hardware cleanup. This stage used `candidate-0.10.6-review/`, with notices; `candidate-update-maintenance/` is the preceding build. Full execution ledger: `artifacts/recheck-execution-20261018-171753/execution-ledger.md`.
- [x] Implement reusable DeepCool isolation with durably flushed, fingerprinted backup of all **258 native bytes + six firmware configurations**, pre-restart recovery, complete readback, cooling checks, original service-state restoration and a durable write-attempt journal. See [workflow and remaining limits](docs/fan-maintenance-workflow.md).
- [x] Test every native/firmware recovery write failure in simulation; later headers/domains still recover. **47 groups passed**. Nine real CLI checks reject stale/corrupt/wrong-machine backups, active writers and evidence overwrites without hardware writes or service changes.
- [x] Qualify normal real isolation and deliberately failed-probe recovery on the final candidate. All six native/firmware configurations match afterward; services/start modes restored. First integrated cycle recovered exactly the two known bytes; later stops retained the corrected vendor startup snapshot. Evidence: `fan-maintenance-qualification-final.json`; earlier write evidence preserved in `fan-maintenance-normal/`.
- [x] Final-candidate audit: **ten hardware/UI checks passed**, fan native/firmware settings match, native bytes also match the historical pre-isolation baseline, and all 26 shared-data file states match the prior checkpoint (24 retained files + two already-absent ownership records). Fan/GPU screenshots visually inspected. Evidence: `fan-maintenance-final-audit.json`.
- [x] Complete fresh maintenance source/inventory pass: NVIDIA VBIOS/driver/subsystem and present PnP descriptors preserved; BIOS F21 agrees, NPU official page is newer than GCC's entry, separate PD firmware prerequisites recorded. Exact PCB revisions and remaining peripheral firmware/recovery remain open.
- [x] Find an applicable KIOXIA EXCERIA G3 **EVFATR.0 → EVFATR.1** official advisory for the system disk; implement precise dated warning independent of SMART health. No firmware applied. Data/recovery preparation and reviewed update remain pending.
- [x] Reproduce and fix package-version fallback: missing GCC package registry metadata must not borrow an unrelated device driver's version. Corrected catalog reports two confirmed available entries and seven unknown package checks, rather than false same/newer results. Preserve original incorrect output and raw index 26100201.
- [x] Add eight reviewed official maintenance links, explicit coverage/index-lag limits and exact support URL validation. Verify **48 groups, ten hardware/UI checks, six UI scenarios**, actual SSD match and no configuration/service/deployment changes. Evidence: `hardware-maintenance-qualified/hardware-maintenance-qualification.json`.
- [x] Archive/review official KIOXIA SSD Utility 6.6.0.09, guide, release notes and agreement; installer signature Valid (Kioxia Corporation), version/hash preserved without execution. Read actual recovery/encryption/physical-disk state; WinRE is on target disk 0, Windows found no backup and user confirms none verified. Collector parse/overwrite refusal passed. Full checklist: [storage maintenance preflight](docs/storage-maintenance-preflight.md).
- [x] Preserve and hash G HUB's cached G502 DFU 127.03.10 and manifest. USB ID matches but observed mouse firmware is 174.0.5; cross-family compatibility, live availability and recovery remain unqualified. No vendor app/DFU launched.
- [x] Harden F14 transition/recovery handling: try remaining service stops after an error, retry guarded recovery after late Stop failures, settle pending restart states and retain every error. **35 simulation checks passed** using the same lifecycle functions as the real workflow.
- [x] Qualify four live controlled cycles: normal, exceptions after completed Stop/restart, and failed probe. All 258 native bytes, six firmware settings and original service states preserved; zero configuration write attempts. The three injected operations remain expected failures. Inventory 15 service policy queries; sensor/display crash recovery is configured at 5/15/60 seconds. Actual SCM failures, crash/relaunch races, termination and uninstall/reboot remain open.
- [x] Build an actual 0.10.6 setup executable from all 96 qualified files, with x64 Desktop runtime detection, protected Program Files location, Start menu/Apps registration, active-process refusal and profile-preserving uninstall. **19 package guard checks passed**; stable deployment/startup unchanged.
- [x] Qualify temporary Program Files install, all payload hashes, root ACL, six read-only installed probes, six maintenance UI scenarios, same-version repair and active-process uninstall refusal: **33 checks passed**. Remove temporary installation cleanly; preserve all 26 data states, fan native/firmware configuration and original startup/services. First ACL-verifier failure and preliminary build errors remain preserved separately.
- [x] Fix current-source launch routing (now F17; historical checkpoints called it F15/F16, colliding with existing findings): normal launch is read-only, task migration requires explicit action, and development shortcut refresh preserves other deployments. Pass **49 application groups including 31 routing cases, 25 Windows shortcut/junction fixture checks**, publish separate 0.10.7 and pass 19 package guards. Older binaries still require retirement from normal launch paths during cutover.
- [x] Audit 96 source/staged payload hashes, 26 data states, original task XML/services and stable 0.10.5 after canceled Windows elevation. No temporary install or application process remains. Preserve the first verifier regex failure and corrected passing result separately.
- [x] After user-confirmed Windows approval, relaunch the prepared temporary 0.10.7 installer qualification: **36 checks passed**, seven diagnostic entry points, six UI scenarios, complete startup XML and user shortcuts preserved. Supported uninstall removed its own temporary installation. Fresh independent audit verified all fan native/firmware settings, original 2400 DPI mouse/all five onboard slots, 26 data states, 96 payload files, 17 source files, services, task and both shortcuts.
- [x] Qualify actual startup selection/disable/rollback and installed-task uninstall refusal: **43 checks passed** after correcting verifier recovery to restore XML and permissions separately. Initial 40-check failed run, exact recovery/cleanup and passing rerun retained separately. Independent watchdog verified the restored task without another write; its parent-termination/deadline branches remain untested. Fresh fan/mouse/configuration/hash/service/shortcut audit passed; no normal hardware host or task Run was invoked.
- [x] Review normal-host preparation, reproduce premature timeout completion with pending workers, and fix F18 in 0.10.8: join lighting workers before ownership release and honor automatic-sync permission. Pass **50 application groups, 33 lighting scenarios including 97 policy cases, ten hardware/UI checks, 13 synthetic GPU cases, six maintenance UI cases and 19 package guards**. Build a separate review setup; no installation. Fresh baseline probes and final preservation audit passed. Retain two collector-only failures and the subsequent **real receiver 9/10 lighting reply timeout**; overall hardware qualification remains incomplete.
- [x] Complete one supervised 0.10.9 scoped installed desktop cycle: 15 checks, two window/tray cycles, four physical native button pairs, genuine user-selected Exit, independent original mouse/GPU verification, 26 data states restored and supported uninstall. Final audit passed; fan writes/full saved-profile application excluded.
- [x] Record five scoped successful start/Exit cycles and ten window operations; retain early user-selected Exit aborted sequence. Pass 51 application groups/19 package guards, 29 parses and two pre-launch refusals. Current post-reboot native/firmware fan settings match historical originals.
- [ ] **Next:** qualify actual game/custom input, full saved profiles/physical output and Windows/failure lifecycle with fresh guarded baselines and independent recovery. Use [installed lifecycle checklist](docs/installed-lifecycle-qualification.md); account for saved 800 DPI versus original 2400 DPI, unused fan-control channel 4 (exclude from connected-fan tests), GPU profile ownership and rainbow-on-exit. Complete actual F14 SCM failure/relaunch/termination, physical acceptance and F10 Windows lifecycle checks. Exact board/card/peripheral revisions and HWUP routes remain open. Firmware maintenance waits for verified C:/D: backup, separate recovery media/boot check and review of the utility's exact firmware offer. E: has tight, unqualified backup headroom.
- [ ] Complete rollback exports/installers, 24-hour vendor-disabled rehearsal, staged supported uninstalls and 48-hour post-uninstall observation before clearing removal gates.

**Current handoff state:** no Sora or test runner, no temporary Program Files installation, original 26 data states restored, native mouse controls/2400 DPI read during cleanup. Startup remains disabled; original task structure/permissions and shortcuts/stable 0.10.5 retained. Current post-reboot fan settings match historical originals; earlier 59-byte difference/controller reappearance remains in history. Ownership/relaunch qualification remains open. No vendor uninstalled or firmware updated. Five scoped Exit cycles and ten window operations passed. Remaining game/full-profile/Windows checks require coordinated sessions.

---

**Active Logitech continuation plan:** [docs/logitech-plan.md](docs/logitech-plan.md). Tracks the six G502 screenshots, disabled Apply despite G HUB onboard-memory OFF, DPI stages/hold-to-shift, assignments/G-Shift/macros and missing LIGHTSYNC workflows. Update that checkpoint during implementation.

2026-10-17, 0.10.0: G502 software takeover fixes Apply without enabling onboard memory; DPI stages/held Shift, assignments/G-Shift, chord macros, front/side assets, screen/audio lighting and persistent device preferences implemented. 35 check groups, 15 release UI checks and bounded real apply/restore passed; five onboard profiles unchanged. Physical G6 timing/full G HUB parity remain unverified. See active continuation plan above.

2026-10-17, 0.9.0: Logitech G502 HERO integration from REA analysis of installed G HUB 39.1.2. Independent HID++ feature discovery, DPI/report rate, per-zone LIGHTSYNC, sensitivity presets, original backup/staging and CRC-checked onboard inspection. Native page at Ctrl+9. Live sensitivity and all four lighting modes passed apply/readback/restoration; 34 check groups and eight UI checks pass. Full scope and remaining phases: [Logitech plan](docs/Logitech-GHUB-plan.md), [coverage and evidence](docs/Logitech-GHUB.md). Full G HUB parity is not claimed.

2026-10-17, 0.8.9: Updates page = GCC update check (BIOS/ver2.ini exact single-entry board match; Drvdata tables + present PCI/HDAUDIO/ACPI/USB devices; GCC version rule; HEAD-verified Gigabyte links; download only, size + Authenticode checked). Verified against GCC's own computed list on this PC. Sign-in delays removed (task 15 s, recovery 10 s, lighting 8 s). RAM flash save declined again (no GCC precedent; OpenRGB issue 3753 hard reboot on XPG Lancer Blade).

2026-10-17, 0.8.8: Wi-Fi made read-only like GCC (RE: MB.CPNT.WiFi.dll uses only EnumerateConnectedNetworkSsids + EnumerateBssNetworks). WlanScan, scan notifications, radio-state and RSSI queries removed; reflection check guards against WlanScan returning. Second card failure at 18:45 was under 0.8.4 (3 s scan loop) after the card was re-enabled.

2026-10-17, 0.8.7: Defender removed 0.8.6 (Behavior:Win32/Persistence.A!ml): a failing schtasks /XML read made every launch re-create the elevated logon task. Task Scheduler COM read, no rewrite on read failure, stable dist\Sora-current junction for task and shortcuts.

2026-10-17, 0.8.6: Wi-Fi scan rationing (read-only radio-state query, no scans while off/not ready, 15 s settle and interval); radio re-check every 5 s without scanning.

2026-10-17, 0.8.5: Wi-Fi Compass names a Wi-Fi card that Windows could not start (WMI Win32_PnPEntity error code, read-only). User's Realtek 8922AE hit code 10 after an rtwlane711 'sw ser l2 reset' at 18:33:03, 50 s after Sora's last lighting write.

2026-10-17, 0.8.4: Per-module RAM Off sent as GCC's Static black (mode 0 woke up when the other stick was applied); all modules applied in one ENE write; sync frames interleaved across DIMMs (A2 led B2). Opt-in "Keep motherboard lighting after shutdown" (CC 47 01/00, re-sent after board Apply and before exit, sleep or sign-out while sync animates). Sync off re-applies built-in equivalents. Wi-Fi 3D view shows measured strength by side. 32 core check groups and 26 lighting editor scenarios pass; hardware behaviour of the fixes still needs the user's confirmation.

2026-10-17, 0.8.3: User-reproduced GAME ON logo regression after removal of controller initialization. A selected-zone-only reset was ineffective; restoring the captured complete initialization restored red. User then confirmed isolated blue edits preserve I/O rainbow; logo returned to red. Added explicit full-state initialization with saved-zone restoration, initialization lifecycle invalidation, scoped descriptor resets and packet regression checks. 0.8.2 Wi-Fi-off banner remains included.

2026-10-17, 0.8.1: Scoped motherboard lighting packets and explicit solo/Ctrl selection; independent RAM sync with identity-bound module settings, invalid-draft preservation and legacy migration. Interactive 3D ribbed antenna, strength gauge/history and measured direction comparisons. GPU last-applied persistence, native retention, safe software exit, exact-identity startup recovery and driver reconnection after suspend. Delayed elevated sign-in/wake task and cancellable host recovery. 32 core check groups, 26 lighting editor/persistence scenarios and 13 injected GPU lifecycle checks pass. Physical sleep/hibernate and powered-off Wi-Fi connected measurements remain untested.

2026-10-17, 0.8.0: REA-assisted GCC SSD Tool, RTX 5070 Ti native GPU curves/Auto Fan Stop, and Wi-Fi Compass reconstruction. Added independent Windows storage/WLAN/NVIDIA integrations and three pages, named/GCC fan profiles, backups, readback and rollback. Live reads verified both NVMe drives; bounded native fan test confirmed all three fans respond and complete original settings restore. Wi-Fi radio-off handling verified; connected antenna measurement remains untested. 32 check groups pass. Secure erase and unverified vendor/controller paths remain unavailable. See docs/GCC-hardware-tools.md.

2026-10-17, 0.7.4: Fan Apply tracks per-header pending edits, shows Applying/Applied and disables until another fan edit. Preserve row objects/scroll and lock input without greying the page. Restore temperature points when leaving fixed duty and retain the saved mode when compiled settings match readback. Mode explanations and pixel-level curve/step differentiation added; 45 editor checks pass. Fan programming and RGB implementation unchanged.

2026-10-17, 0.7.3: User applies were blocked before writes by EasyTuneEngineService. Approved radiator-only 100% test physically increased tachometer speed from 1264 to 2177 RPM; all native/firmware configuration and service state restored, CPU pump untouched. Promoted GCC conflict/pause controls above the editor, disable writes while blocked, distinguish selected/hovered headers, and explain RPM settling. Added three conflict UI checks; RGB implementation unchanged.

2026-10-17, 0.7.2: Fixed the three-second fan polling flash by separating reads from mutating operations and providing an explicit dark list template. Preserve name drafts, cursor selection, numeric feedback and operation results; queue user actions behind reads. Device roles save without touching curves. User-confirmed wiring: CPU = LD360 fixed-speed pump; System 4/Pump = radiator fans. Fixed-speed pumps have RPM monitoring with curve/sweep controls hidden. No fan settings or RGB implementation changes were made for this update. Added polling/failure/recovery and role-persistence regression checks.

2026-10-17, 0.7.1 fan-control work: REA/CIL and ILSpy recovered the Z890 `A0060607` IT8696E map, eleven-point curves, source selection, fixed/step modes and ACPI Zero-RPM. Added a separate fan backend/page, backups, readback/rollback, profiles, response measurement and minimum running/starting-duty calibration. All six native full-speed tests and the curve/step/firmware configuration tests passed with settings restored. The explicitly approved `Sora_GigabyteFanControl` WMI interface is registered and verified, removing GCC's WMI-definition dependency as well as its runtime dependency. Calibration excludes pumps and has automated failure/cancellation coverage; actual minimum-duty stopping tests were not run on unidentified connections. Details and evidence are in `docs/Gigabyte-fan-control.md`. RGB implementation files were left unchanged.

Working plan for the 0.5 redesign. Each phase ends with real screenshots and waits for approval before the next one starts.

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | Audit of code and 0.4 screenshots | Done, approved |
| 2 | Design tokens + app shell (sidebar, shared page header) | Done, approved |
| 3 | Sensor / display state model + Cooler Display page | Done, approved |
| 4 | Keyboard: geometry, keycaps, editing workflow | Done, awaiting approval |
| 5 | State logic and action feedback (Apply/Discard, connection) | Not started |
| 6 | Motion system | Not started |
| 7 | Accessibility, resizing, final verification, README | Not started |

## Ground rules

- Keep every existing feature: Windows startup task, tray behaviour, Fahrenheit, leading zeros, auto-start display, profiles, backups, hex validation, palette, per-key paint, remaps, key test field.
- No mock data. Readings, connection state and success messages must come from real state.
- Don't rewrite the backend; only add state where the UI needs to tell different conditions apart.
- No new dependencies unless clearly worth it. Motion uses WPF Storyboards.
- The public repo excludes `kreo-re/`, `deepcool-re/`, `artifacts/`, `dist/`, `vendor/`, `rea/`, `v8*`, `tools/` (vendor code, device evidence, oversized files).

## Audit findings to fix

1. Text as small as 10 px; footer and hint text fail AA contrast. Fonts are set on TextBlock only; no tabular numerals.
2. Eight near-identical navy surfaces; two different `Card` styles.
3. Keyboard renders X at 1.45× and Y at 0.73× (squashed); widths come from legend hit-boxes, not key pitch; `MaxHeight=185`.
4. Vendor hotspot data jitters by 1–2 px and rows sit at mismatched Y values. Normalise to a 0.25u grid in code and check that key indices and labels are preserved for Hive 75, Swarm 75 and Hive 98.
5. Selection replaces the key's real colour; colours are dimmed to 34%; no hover, focus or pressed states.
6. Per-key panel shows a hex value with no key selected; single-key and whole-keyboard actions aren't separated; Fn is selectable but silently not editable.
7. Disabled Apply still looks active (0.4 opacity over bright blue). "Connected over USB" means "read once", not "connected now".
8. Cooler page shows the readings twice; preview card mostly empty; display settings, app settings and diagnostics share one card.
9. Sensor status and readings update independently (stale readings can sit next to "unavailable"); the "Live sensor readings are still available" message is hard-coded; "Ready to connect" without detecting the LD360; error causes collapsed into one string; sensor failures not logged.
10. Header layouts differ between pages; browser-style tabs; MessageBox dialogs; turning on startup silently enables auto-display; model picker looks like a device selector.

## Design system (phase 2)

- Tokens live in `src/Sora.App/Theme.xaml`, merged by `App.xaml`.
- Surfaces: window `#0E0F11`, sidebar `#141518`, content group `#1A1B1F`, raised `#222327`, control `#2B2C31`, inset input `#111215`. Hover/press are white/black overlays so they work on any fill.
- Text: primary `#F5F5F7`, secondary `#AEAEB2`, tertiary `#8A8A93` (5:1 on the content surface), disabled `#5E5E66`.
- Accent: `#0A84FF` (Apple dark-mode blue) for primary action, selection, focus ring only.
- States: success `#30D158`, warning `#FFD60A`, error `#FF453A`, idle `#636366`.
- Type: Segoe UI (WPF can't select Segoe UI Variable's named instances, so weights would be synthesised). Scale 26 title / 16 section / 14 body / 13 secondary / 12 caption; numbers use tabular figures; nothing below 12 px.
- Spacing on a 4 px grid (4, 8, 12, 16, 24, 32). Control height 32. Radii: 6 for controls, 10 for surfaces.
- Disabled = muted fill and muted text, never opacity over an accent.

## Verification loop

1. Preview build (non-elevated): copy `app.manifest` with `asInvoker` into the scratchpad, then
   `dotnet build src/Sora.App -p:ApplicationManifest=<copy> -p:IntermediateOutputPath=obj/preview/ -o <scratch>/preview`
2. `set SORA_INSTANCE=preview` and run `Sora.exe --screenshot X.png` (add `--keyboard` for the keyboard page).
3. Screenshot, critique, fix, screenshot again. Run `test.ps1` before each commit.
4. Non-elevated previews always show "sensors unavailable". Live readings need an elevated build on the real PC.
5. Bump `build.ps1`'s output folder before any release build (the running release locks its folder).

## Decisions log

- 2026-10-17: repo initialised and pushed to github.com/gauravxsuvo/Sora (public). RE folders kept local.
- 2026-10-17: `Kreo-keyboard.md` moved to `docs/` so a clean clone builds.
- 2026-10-17: phase 2 shell done. Known carry-over: at 1080×720 both page bodies scroll internally (same as 0.4); fixed in phases 3–4. Screenshot mode accepts `--size WxH`.
- 2026-10-17: phase 3. Sensors report Starting/Live/Unavailable with a cause; readings are cleared on failure; sensor changes are logged. LD360 presence is a read-only USB scan every 3 s while the window is open. Turning on start-at-sign-in no longer silently turns on the display (a hint explains instead). About sensors is inline, not a MessageBox.
- 2026-10-17: phase 4. KreoLayout snaps vendor hotspots to a 0.25u grid (checked for all three models: no lost or overlapping keys). Keycaps glow in their real colour; selection is an outer ring; remaps show "→ X" and a dot; Fn dims while assigning. Mode switch lives on the board toolbar. Editors scroll on short windows so the board keeps ~300 px. Preview flags: --keyboard-paint-preview, --keyboard-assignment-preview (staged only, never applied).
- 2026-10-17: 2.4 GHz receiver (3554:FA09) is detected and shown as "settings need USB" instead of a red error; Apply is disabled while only the receiver is present. Real wireless configuration needs the receiver protocol (FF02/2 20-byte output, FF04/2 8-byte feature), not yet reverse-engineered. Release 0.5.1.
- 2026-10-17: 2.4 GHz receiver support (release 0.5.2). Protocol recovered from OemDrv CDev3632 (docs/Kreo-receiver-protocol.md). Over the receiver Sora reads lighting, key map, effect palette and battery, and writes lighting (verified with readback and restore). Key map (01) and palette (09) writes are implemented as a reversible hardware check but not run yet, so they stay disabled in the app. Per-key colours cannot be read over the receiver.
- 2026-10-17: brand. The cloud mark from soro_logo_light.svg is the Sora logo: assets/brand has transparent SVGs (sky gradient, white, ink) generated by scripts/Build-Brand.ps1, which also builds the multi-size assets/Sora.ico (window, taskbar, tray, shortcuts). The sidebar uses the vector mark.
- 2026-10-17: 0.5.3. Receiver writes for key map (01) and palette (09) enabled in Apply with backup, readback and automatic rollback. Command 06 over the receiver is ResetDevice and 88/8E are SetRealData (live frames), so per-key colours cannot be stored over 2.4 GHz; Sora remembers them from the last USB read and labels them as such.
- 2026-10-17: 0.6, Lighting. GCC reverse-engineered (gcc-re/REPORT.md, local): RAM via ENE DDR5 controllers on the SMBus (PawnIO SmbusI801 from LHM, global SMBus mutex, read-only signature before any write, register allow-list, readback), motherboard via IT5711 USB feature reports (commands 0x60/0x20/0x28 only; never 0x5E flash save). OC/undervolt/fans excluded by the user. Verified on the ADATA sticks with restore; board effects untested visually (nothing on its headers). Lighting page = sidebar item 3.
- 2026-10-18: 0.6.1 board fix. Frida captures of GCC's own HidD_SetFeature calls showed per-zone packets (0x20+n / 0x90+n-8, mask 1<<n) with a 100 ms wait after the clear; header 0x20 only drives zone 0. Zones on this board (GCC divsmap MB_Id 31, layout 4, mapped by the user): 0 GAME ON, 2 SSD heatsink, 9 I/O cover, 4 RGB header, 5/6/7 ARGB headers. Sora no longer sends 0x47 01 (it made the board remember Sora's colours across restarts); the user restores GCC's stored lighting by opening and exiting GCC once.
- 2026-10-17: 0.6.2. HSV colour wheel (ColorWheel.cs) for memory and board. Board "Rainbow" = GCC's set_all_rainbow (InitAllRainbowEffect: PatternType 9 → LMode ColorCycle, brightness 8, speed 8); GCC's ARGB rainbow wave is software (SWave 100). Hardware Wave (mode 6) left ARGB headers 5/6 dark on this board, so Wave is removed from the UI and Rainbow/saved Wave are sent as ColorCycle. At the user's request the board stored the rainbow (`--board-default-rainbow`: GCC's rainbow + 0x47 01, exactly GCC's exit). The user wants the boot-time rainbow *wave*; next step is `--board-builtin-default` (0x47 00, GCC's connect command) plus a restart, to see whether the firmware's built-in lighting is that wave. Apply now saves only sections that reached the hardware.
- 2026-10-17: 0.6.3/0.6.4. Pale colours: GCC's ENE HAL writes control +9 (0x8029, brightness 0-9) = (R+G+B)/84 on chip type 0xD5 (disassembly + Frida capture of AACDDR_SetMode(1, 0xCC,0,0, 2, 2)); Sora now writes it (allow-listed only for 0xD5), backs it up and reads it back. Board rainbow: GCC's flowing fan rainbow is hardware Wave17 (mode 9, header 0x22, apply mask 0x0004), confirmed by ending GCC (flow continues) and by NtDeviceIoControlFile captures of GCC and Sora (gcc-re/hook-ioctl.js). GBT_DL_LIB (Windows Dynamic Lighting LampArray helper) only sets one colour on RGB sync. 0.6.2-0.6.3 bug: LightingService converted Rainbow to ColorCycle before ApplyZones; GigabyteRgb.Plan is now checked. The board stores the wave at the user's request (--board-default-rainbow = wave + 47 01).
- 2026-10-17: 0.6.5 sync mode. LightingSettings.Sync + SyncLighting (SyncEffect Rainbow/Static/Breathing/Flashing/ColorCycle/Off, colour, brightness 10-100 %, speed 1-5). Apply in sync writes SyncLighting.ToMemory() and ToBoard(all zones) through the normal apply path and stores them as Memory/BoardZones, so sign-in needs no special case. Board brightness = nearest GCC level, speed 1-5 -> 0,2,5,7,9. The toggle is saved at once; the hardware changes only on Apply.
- 2026-10-17: 0.6.6-0.6.9. Sync engine (SyncEngine.cs) for animated sync effects, modelled on GCC's EneMemoryDeviceV5 threads (EneModeLedEffAutoThd etc. stream AACDDR_SetMode(1, rgb) per frame): ~14 fps here, RAM by byte writes because PawnIO's i801 module rejects SMBus and I2C block writes (tested once per start with readback; Sora falls back automatically). ENE writes now stage every DIMM and apply them back to back, so the sticks start in phase. GCC aligns sticks with AACDDR_Synchronize -> HAL +0x28 writing frame register 0x80F2 (not used by Sora). Bug: ApplyAsync saved new LightingSettings(...) and dropped Sync; now uses with. Exit lighting (RainbowOnExit): RAM original Rainbow from the backup, board Wave17; nothing saved to the devices.
- 2026-10-17: 0.6.10. Sidebar and status chip: sync shows "Synced · <effect>" from the settings (right before the first scan), otherwise "RAM <mode> · Board <effect>"; no more "RAM · Not checked". Startup with an animated sync effect scans first, then starts the engine; the window reads lighting once on open on any page. Refresh while the engine runs rescans the RAM alongside it and keeps the last board reading (an engine frame could split the board's info request and reply), so Check again no longer blips the lights.
- 2026-10-17: 0.6.11 keyboard receiver fix. --receiver-diagnose (read-only) showed the receiver drops a request while the keyboard radio is idle or it is answering another reader (replies are broadcast to every open handle), then answers the next within ~100 ms. Reads now retry (4 x 1.2 s, KreoReceiver.ReadWithRetryAsync) instead of one 4 s wait; the Keyboard page retries quietly every 8 s while the keyboard does not answer.

## User-confirmed lighting connections — 18 October 2026

ARGB header 1 (lighting zone 5) = case-fan RGB. ARGB header 2 (zone 6) = AIO pump and radiator-fan RGB. ARGB header 3 (zone 7) and RGB header (zone 4) are unconnected. Mark empty-output visual checks not applicable; no physical effect acceptance is implied. Separate fan-control header 4 remains unidentified. No application settings or hardware writes. See docs/hardware-connections.md and artifacts/recheck-execution-20261018-171753/user-confirmed-lighting-connections.json.

## User-confirmed fan power correction — 18 October 2026

Case fans are non-PWM and SATA-powered; RGB connects to motherboard ARGB header 1. This explicit correction supersedes the earlier uncertain/3-pin answer. No motherboard speed-control path is established: qualify their RGB, exclude case-fan motherboard speed curves pending a supported separate control connection. User also reports fan-control channel 4 unused; exclude it from connected-fan physical tests and staged automatic acceptance profiles. Existing saved channel-4 profile was not modified/applied; historical observations are retained. Exact hardware revisions and remaining lifecycle/maintenance gates remain open. No application settings or hardware state changed. See docs/hardware-connections.md and artifacts/recheck-execution-20261018-171753/user-confirmed-fan-power-connections.json.
