# Native mouse routing and incident recovery — 18 October 2026

## Latest continuation — 23:02 IST, 18 October 2026

**Sora closed; startup disabled; temporary install removed; original mouse/GPU and 26 data states independently verified. Five individually successful scoped Exit cycles and ten window operations recorded.** Final separate cycle passed 14 checks. An intervening four-cycle sequence stopped during cycle 4 because the user selected Exit again before its prompt; three cycles passed beforehand, recovery/watchdog/uninstall passed, and the failed sequence is retained. Current test script refuses multi-cycle supervised tray invocations before staging, and records failure PID/Exit code. Two guard checks passed.

Fresh 51 application groups/19 package guards passed; 29 scripts parse. Final independent audit at 23:02:30 verifies original mouse 2400 DPI/mode 1/1000 Hz/five onboard slots, GPU, data/task/shortcuts/stable route, all 96 payload hashes and supported cleanup. Source application/package binaries unchanged. Exit timing includes user response; full-profile consecutive repeats/resources/Windows matrix remain open.

Post-reboot fan native/firmware settings match historical originals; final fresh pre/post baseline and services match. The earlier 59-byte failed comparison remains retained. EasyTune delayed auto start explains the post-reboot instance. An earlier application event confirms startup at 21:08:07, correlated with GCC log activity at 21:07:57–21:08:11; that does not prove the earlier caller/fan-change cause. No fan configuration writes or service changes requested.

Receiver initial batch: 8/24 failed, 56 attempts. After the user confirmed 2.4 GHz and woke the keyboard: 24/24 first-attempt reads passed. Both retained; sleep/wake and incomplete-packet reliability remain open. No keyboard configuration writes.

Evidence: `artifacts/recheck-execution-20261018-171753/remaining-audit-20261018-225308`; checkpoint: `checkpoint.json` there. [Remaining work and responsibilities](remaining-acceptance.md). **Actual game, physical display/lighting, full profiles, Windows/failure lifecycle, backup/recovery and vendor-removal gates remain open.**

### Earlier recorded phase — superseded current-state descriptions


## Current state

Sora is **closed**, the temporary Program Files installation is **removed**, and all **26 original shared-data states are restored**. Native mouse mapping, firmware ownership and 2400 DPI were verified during cleanup. Automatic Sora startup deliberately remains **disabled**. Its original action/path, triggers, policy and permissions are retained; the original enabled choice is not restored because the older deployment still contains the input issue. The stable junction and two user shortcuts remain unchanged at 0.10.5.

The supervised installed final audit passed at **22:21:01 IST**, verifying original mouse/GPU, 26 data states, task XML/security, shortcuts and 96 payload hashes after supported uninstall. This does **not** constitute a complete hardware preservation pass: 59 native motherboard fan bytes differ from the older snapshot, with no differences in the six firmware fan configurations. Those differences are retained as unresolved F20.

## F19 — native right-click was replaced with software input

The user reported broken right-click while gaming with the 0.10.8 qualification host active. The host was stopped at the user's request. An independent helper restored the native button map, disabled button spying, recovered original 2400 DPI/mode 1 and verified the original mouse/GPU state again. The incident remains a failed physical acceptance result.

Source inspection establishes a concrete weakness: the previous startup map was `01 00 00 00 00 …`; all nonprimary native buttons were disabled. Right, middle, back and forward clicks depended on HID notifications and Windows `SendInput`. The precise failure mechanism inside the user's game is **not proven**. Notification delivery, injection and focus/elevation effects were not captured sufficiently to distinguish them.

Review candidate **0.10.9** changes that policy:

- Unchanged standard buttons stay native when both effective layers retain their physical default assignment. The default map becomes `01 02 03 04 05 00 …`.
- Their notifications resolve to a native action; the runtime does not duplicate those clicks through Windows injection.
- A genuinely remapped button remains under software control. An alternate G-Shift remap also requires software routing for that physical button; this is not a guarantee of game compatibility for custom assignments.
- Startup, health checks and profile cycling use the same routing compiler. Profile cycling reads back the new route and refuses route changes while the affected physical button is held.
- Failed profile switching attempts routing, input-profile, runtime-stage and sensitivity recovery independently. Cancellation of the attempted switch does not cancel rollback.

This correction passed one supervised **real 0.10.9 installed desktop cycle** after explicit idle confirmation: four physical native button pairs and genuine user-selected tray Exit. **Actual game behavior remains untested.**

## Failed installed lifecycle runs retained

Evidence root: `artifacts/recheck-execution-20261018-171753/`.

- `installed-host-scoped/`: verifier failed on Windows PowerShell's missing `Path.GetRelativePath` before staging/install/startup. Corrected without overwriting the failure.
- `installed-host-scoped-qualified/`: actual 0.10.8 installed host applied 800 DPI/software mode/1000 Hz and passed startup duplicate, normal duplicate and window/tray checks. First automated menu Exit did not finish within 30 seconds. Recovery succeeded; uninstall completed. The initial result checked the directory before the uninstaller's final deletion finished, so it recorded a retained installation; later readback confirmed removal.
- `installed-host-scoped-repeat/`: another menu Exit timeout, an unavailable/disabled automation element, and failed watchdog mouse comparison were retained. A comparison observed a change from stage 1/2400 DPI to stage 0/1200 DPI between captures. Cause is unknown; this happened during overlapping PC/game use. Later guarded rescue and a two-second settled verification passed, with supported uninstall.
- `installed-host-native-menu/`: foreground protection refused native keyboard selection while Sora was not the foreground popup. No keyboard input was sent by that refused operation. Both normal recovery and the watchdog failed to close the host; the host remained active when the user requested a pause. That was unsafe for ordinary concurrent mouse use. The later right-click report led to immediate exact-PID termination and independent mouse restoration. This failed run is not converted to a pass by later cleanup.

These observations do not establish that Sora's internal `ExitAsync` itself hung; the menu dispatch/focus path was not reliably established. One later user-selected actual tray Exit passed on 0.10.9. Prior failures remain retained; repeated Exit, delayed cleanup and Windows lifecycle remain unqualified.

## Guarding future tests

- The installed-host script now refuses before any staging or process launch unless an **explicit idle input session** is confirmed. Broad permission to test is not evidence that the user has stopped gaming.
- Its recovery policy attempts graceful exit, then allows termination only for a uniquely recorded test PID with matching start time/path. No unrelated process may be terminated. Hardware recovery cannot start while a host remains.
- Six mocked exit-policy cases passed: normal exit, errors after exit, error/timeout while running, ownership refusal and ineffective termination. The new live fallback adapter has **not** been exercised.
- A force-stop or recovered cleanup never changes a failed graceful Exit into a passing lifecycle check.

## F20 — controller reappeared; fan state differs

Read-only cleanup found **59 differing native bytes**, zero firmware configuration differences. Pump/radiator remained automatic with firmware stop off; the recorded RPMs were approximately 3325/1467. No fan configuration write or vendor service stop/start was requested during the scoped host tests or this cleanup.

The user reports no manual setting changes, restart or vendor-app launch. The earlier inventory shows `EasyTuneEngineService` **Stopped**. It is now **Running**, PID 39004, with observed process creation **21:08:03 IST**. This proves another controller appeared; it does not prove which component caused every differing byte or which caller started the service. The existing Sora fan-conflict check already includes that process. Current fan settings were preserved. Do not overwrite them with the old snapshot while ownership and freshness are unresolved.

Files: `mouse-native-routing-review/controller-service-findings.json`, `fan-difference-report.json`, and `resume-corrected-cleanup-fans-readonly.json`. The cleanup result retains its failed fan-preservation comparison; the later passing audit is explicitly limited to artifacts and configuration cleanup.

## Review results and package

| Scope | Result |
| --- | --- |
| Application suite | **51 groups passed, zero failed**, including native routing, no duplicate injection, genuine remaps, layer resolution and held-route refusal. |
| Release publish | 0.10.9 compiled successfully into a separate candidate folder. |
| Installer package guards | **19 passed**, all 96 source/staged payload hashes verified. |
| Future idle-session guard | Refusal passed before creating a test directory or launching any host. |
| Exit fallback policy | **Six simulations passed**, zero real processes terminated by those checks. |
| Current mouse input | Physical four standard buttons passed at 800 DPI; original native map/2400 DPI/mode 1/1000 Hz and five onboard slots independently verified after Exit. |
| Configuration/deployment cleanup | 26 states restored, temporary install removed, no Sora running; startup intentionally disabled. |
| Physical 0.10.9 / installed lifecycle | **15 checks passed**, one scoped desktop cycle, two window cycles, physical Right/Middle/Back/Forward and real user-selected tray Exit. Game/repeats/Windows open. |
| Fan hardware preservation | **Failed comparison retained**, current settings preserved; F20 open. |

Review setup: `installer-mouse-native-routing/Sora-Setup-0.10.9-x64.exe`, **5,560,664 bytes**, unsigned. SHA-256: `1230BE8C087E812582CEFD7FE22F665BD15C0F1D2316538CDFBF3A5134F5103C`.

The exact 0.10.9 package was temporarily installed, launched and applied scoped mouse/GPU/display settings after idle confirmation, then exited normally and was removed. All 15 installed checks, runner recovery and watchdog verification passed. Motherboard fan/RAM/board/reactive startup was staged inactive; original data restored afterward. No fan configuration write, vendor service change, firmware update or vendor uninstall was requested. Three final fan reads match latest state; historical 59-byte difference persists. Exit wait of 34,747 ms includes human response time, not measured cleanup latency. Prior 33 lighting scenarios/ten hardware-UI checks belong to 0.10.8; prior installed diagnostics/task checks belong to 0.10.7. They are not new 0.10.9 physical passes.

Evidence in the root above: `installed-host-supervised/qualification.json`, `installed-host-supervised-final-audit.json`, `installed-host-supervised-checkpoint.json`.

Next: resolve fan-controller ownership; complete actual game behavior, remaining mouse actions, repeated Exit/window operations and Windows recovery/lifecycle in coordinated sessions. Vendor removal and firmware installation remain **HOLD**.
