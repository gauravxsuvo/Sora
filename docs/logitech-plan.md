# G502 HERO implementation plan and continuation record

## Latest continuation — 23:02 IST, 18 October 2026

**Sora closed; startup disabled; temporary install removed; original mouse/GPU and 26 data states independently verified. Five individually successful scoped Exit cycles and ten window operations recorded.** Final separate cycle passed 14 checks. An intervening four-cycle sequence stopped during cycle 4 because the user selected Exit again before its prompt; three cycles passed beforehand, recovery/watchdog/uninstall passed, and the failed sequence is retained. Current test script refuses multi-cycle supervised tray invocations before staging, and records failure PID/Exit code. Two guard checks passed.

Fresh 51 application groups/19 package guards passed; 29 scripts parse. Final independent audit at 23:02:30 verifies original mouse 2400 DPI/mode 1/1000 Hz/five onboard slots, GPU, data/task/shortcuts/stable route, all 96 payload hashes and supported cleanup. Source application/package binaries unchanged. Exit timing includes user response; full-profile consecutive repeats/resources/Windows matrix remain open.

Post-reboot fan native/firmware settings match historical originals; final fresh pre/post baseline and services match. The earlier 59-byte failed comparison remains retained. EasyTune delayed auto start explains the post-reboot instance. An earlier application event confirms startup at 21:08:07, correlated with GCC log activity at 21:07:57–21:08:11; that does not prove the earlier caller/fan-change cause. No fan configuration writes or service changes requested.

Receiver initial batch: 8/24 failed, 56 attempts. After the user confirmed 2.4 GHz and woke the keyboard: 24/24 first-attempt reads passed. Both retained; sleep/wake and incomplete-packet reliability remain open. No keyboard configuration writes.

Evidence: `artifacts/recheck-execution-20261018-171753/remaining-audit-20261018-225308`; checkpoint: `checkpoint.json` there. [Remaining work and responsibilities](remaining-acceptance.md). **Actual game, physical display/lighting, full profiles, Windows/failure lifecycle, backup/recovery and vendor-removal gates remain open.**

### Earlier recorded phase — superseded current-state descriptions


## 18 October incident continuation — review 0.10.9

The user reported broken right-click while gaming with the 0.10.8 test host active. Sora was stopped and native mouse controls restored. The temporary installation is removed and startup remains disabled. Source review found unchanged standard buttons had been disabled natively and recreated through Windows input. Review 0.10.9 preserves matching standard assignments in both layers natively, suppresses duplicate injected clicks, updates routing during profile changes and refuses changes to held routes. **51 application groups and 19 package guards passed. One supervised 0.10.9 installed desktop cycle passed 15 checks, including physical right/middle/back/forward and genuine user-selected tray Exit; original mouse/GPU state independently matched afterward. Actual game, repeats, custom assignments and Windows lifecycle remain open.**

Prior checked implementation items below are historical, not closure of this new physical failure. Only run live input tests in an explicitly confirmed idle session. See [mouse incident/routing qualification](mouse-native-routing-qualification.md) and [user-action checklist](user-action-checklist.md).

Updated 18 October 2026. This file is the continuation checkpoint for the user's G502 implementation request. Latest release: 0.10.1; see `docs/Sora-0.10.1.md` for the live DPI outline, reselect/apply correction, physical button capture, 1300 indicator commands and hardware validation. Do not infer full G HUB parity.

## User-visible problem

Screenshot 1 shows Sora reporting Onboard memory mode with a staged 800 DPI edit, live sidebar 2400 DPI, and a disabled Apply button with no explanation. **User correction: G HUB settings explicitly show On-board memory OFF (firmware 174.0.5). Do not assume the user's mode is wrong.** Check stale Sora snapshots and G HUB's runtime transitions; fix mode/status refresh and feedback so the user can actually apply. Preserve original mouse state, competing-controller checks and readback. Never silently rewrite onboard flash just to change live settings.

## Screenshot requirements

1. Sensitivity: editable DPI stages (up to five), current/default stage, separate yellow DPI Shift marker, report rates, reset to defaults. G6 held = temporary shift DPI; release = previous current DPI. G7/G8 move through configured stages. Do not confuse G-Shift with DPI Shift.
2. Assignments: front and side mouse views using actual G HUB assets/marker metadata; all eleven buttons; default and G-Shift layers; search; Commands, Keys, Actions, Macros, System categories. Primary click must remain recoverable. Distinguish supported local actions from vendor integrations.
3. LIGHTSYNC: independent Primary and Logo; Off, Fixed, Cycle, Breathing, Screen Sampler, Audio Visualizer; hue/saturation/color/RGB/hex editing, palettes, sync options; Windows Dynamic Lighting entry/control where available. Existing four firmware effects already passed readback tests; screen/audio require a software streaming engine.
4. Working application behavior: apply, dirty states, errors, controller takeover and release, tray operation, restart/reconnect/resume, input releases on cancellation, profile persistence and original restoration. Expose source-derived/unverified functionality explicitly.

## Implementation sequence

- [x] Recover native mode/host-button/event behavior from the installed build and primary HID++ references. Preserve evidence and exact byte layouts in `logitech-re` and the report.
- [x] Fix Apply in onboard mode using an explicit and verified software-control transition, with visible explanation and rollback.
- [x] Implement typed stages and hold-to-shift state machine, persistent runtime independent of whether the page is open, and bounded event processing.
- [x] Implement host button filtering/diversion, assignment execution and G-Shift; preserve/restore original report masks, mode, sensitivity and lights; release injected input on shutdown/disconnect.
- [x] Add front/side artwork and actual marker hit targets; assignment catalog/editor and profile import/export.
- [x] Add macro models/editor/executor with bounded timing, cancellation and held-input cleanup; do not pretend unsupported integrations are working.
- [x] Add missing software lighting modes and color/palette/sync UX, validated against recovered streaming commands.
- [x] Add meaningful protocol/runtime tests, render UI and check real hardware. Preserve existing Sora changes and settings. Build a new release and update `Sora-current` after checks.

## Evidence already available

`docs/Logitech-GHUB.md`, `docs/Logitech-GHUB-plan.md`; REA 6.5.0 ASAR evidence in `logitech-re/rea-application.json`; installed G HUB app build 39.1.2 / depot 876190; G502 046D:C08B HID++ 4.2 with features 2201, 8060, 8070, 8100 and 8110. Original settings and five CRC-verified onboard slots are retained locally. G HUB assets/metadata are locally cached under release `data/logitech/artwork`. Release 0.10.0 is packaged; Sora-current targets dist/Sora-0.10.0.

## Work log / next action

- Initial diagnosis: `LogitechPanel.SetEnabled` disables Apply in mode 1, and `LogitechDevice.SetSensitivityAsync` refuses mode 1. No independent mode transition or running button handler exists yet. The user's issue is real, not an invalid DPI value.
- Research in progress: 8110 mouse button filter/spy, 8100 mode commands, distinction between onboard assignments and host actions. No new hardware mutations have been performed for this request yet.
- Seventh screenshot adds: firmware 174.0.5, DPI indicator Always On, device startup effect Enable, onboard toggle OFF and five slots. Include these device-settings controls in the feature matrix; initial mode diagnosis is provisional pending live reads.

### Current implementation checkpoint — 0.10.0

- User correction confirmed by G HUB API: `onboardMode:false`, firmware174.0.5. Hardware mode2 while agent runs; mode1 can be restored when it exits. Panel labels now say live firmware/software control, poll even with staged edits, and permit explicit Sora software takeover from mode1. Exit G HUB before Apply. No onboard profile writes.
- Native agent RTTI/vtable inspection recovered 8110 count/start/stop/read-map/write-map (fn0/1/2/3/4), sixteen-byte forwarding map, physical big-endian eleven-button bitmap. Evidence: `spy-vtable.json`, `spy-disassembly.json`. Sora leaves G1 native, hosts G2–G11 and uses Windows SendInput with reference-counted held keys/buttons. Exact request correlation and software IDs isolate concurrent in-process HID clients.
- Persistent `LogitechService` handles staged DPI speeds, separate held DPI Shift, G-Shift, assignments, macro cancellation, tray/reconnect/resume, G HUB handoff and original mode/DPI/rate/map restoration. Ownership backup survives process interruptions. G9 cycles saved Sora profiles by default after at least two named profiles are applied; DPI Cycle remains assignable and no onboard profile sectors are written.
- Front/side images and marker positions are imported from installed G HUB/cache. Assignment catalog and shortcut capture support commands, keys, local app/HTTP(S) actions, macros and system controls. Drag to marker or select/use assignment. G1 stays native. Full control profiles import/export as bounded Sora JSON.
- Macro modes implemented: no-repeat, repeat-held, toggle and sequence. Editor captures/edits chord taps with delays; it is not the full G HUB raw key-down/key-up event recorder. Stop/disconnect releases injected input. No vendor Actions integrations or application auto-switching.
- LIGHTSYNC adds screen-average sampling (primary screen whole/left/right), speaker peak-level audio lighting at10fps, color wheel/RGB/hex/palette, same-settings for both zones and Windows lighting settings link. These are Sora algorithms, not proven exact G HUB parity. Original-state ownership persists; restore opens a fresh connection after faults. Failed startup reverts the in-memory reactive draft. First frame is read back. Native Off/Fixed/Cycle/Breathing remain supported.
- Recovered actual G HUB toggle traffic for DPI Always On and startup effect. Source `LogitechDeviceSettings.cs`: 1300 fn6/7 NV bits2/4; after writing the flag G HUB sends captured 1300 fn5 state `00 00 02 00 02` to refresh the indicator. 8070 fn4/5 uses capability0001 and byte2 enabled1/disabled2. Getter1300fn4 is status and cannot be blindly replayed through fn5. Independent rollback/readback prevents one failed preference restore skipping the other.
- Device screenshot preferences audited: AlwaysOn false, startup enabled, firmware174.0.5 restored. Bounded live test passed software takeover1600DPI/500Hz, both device preference toggles/restoration, screen/audio streaming and restoration of original mode/DPI/rate/map/two lighting states. See `runtime-full-verification.json` (including first-frame reactive readback).
- 35 core check groups pass; Debug build zero warnings/errors. 15 UI checks passed with G HUB active and15 without; firmware mode1 explicitly permits Apply. UI checks send zero hardware writes. Layout rendered at1080x720; assignment artwork needs a scrollable minimum size to keep labels readable.
- Still unverified: actual user physical G6 press/release delivery and injected action timing, disconnect/resume under every device fault, exact visual animation timing and G HUB screen/audio algorithms. Five profile CRCs were read previously; final unchanged-profile audit/release packaging pending.

### Release checkpoint

- Completed: `dist/Sora-0.10.0/Sora.exe` packaged; `dist/Sora-current` junction targets0.10.0. Prior0.9.1 settings/artwork preserved. Published release passed all15 read-only UI checks; software-mode Apply check also passed in Debug with G HUB closed. 35 check groups pass; zero build warnings/errors.
- Live runtime test passed after first-frame lighting readback was added. Final read-only audit: all five decoded onboard profiles match the pre-test JSON exactly, AlwaysOn remainsfalse, startup remainstrue, firmware174.0.5, G HUB agent running. Originals restored after all bounded mutations.
- Screenshots: `artifacts/sora-010-release-assignments.png`, `logitech-010-software-ui.png`, `logitech-010-{side,lighting,details,macros}.png`. Minimum1080x720 layout uses separate scrolling for assignment artwork and catalog to retain readable markers.
- Next work requires exercising physical G6 hold/release and assignments on this release; do not claim that synthetic state tests prove button delivery. Remaining feature limitations are recorded in Logitech-GHUB.md. Do not repeat completed REA/native capture/runtime work unless new evidence requires it.
