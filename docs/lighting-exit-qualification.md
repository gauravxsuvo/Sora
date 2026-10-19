# Lighting worker ownership review — 18 October 2026

## F18: cleanup could finish before its worker

Source review found two premature completion paths in 0.10.7:

- `SyncEngine.Stop()` cancelled the worker, waited at most three seconds and discarded the result of `Wait(3000)`. `LightingService.StopEngine()` could then discard that engine while it still held HID/SMBus handles.
- `ExitLightingAsync()` awaited `Task.WhenAny(work, Task.Delay(6000))`, then released its gate. When the delay won, lighting writes could remain active after the caller considered cleanup complete.

The new deterministic group reproduces those timeout semantics using an unfinished task and a completed deadline. This establishes the control-flow defect; it does **not** establish that this PC previously hit a three/six-second stall.

The automatic animation permission was also never checked: `SoraHost` set `AutomaticChangesAllowed` false for diagnostics and shutdown, but `StartEngineIfNeeded()` did not read it. A finishing lighting operation could restart animation in that state.

## Correction in review candidate 0.10.8

`CleanupSequence.JoinAsync()` treats the deadline as a slow-cleanup warning and awaits the actual worker before returning, including when the warning callback throws. Worker failure/cancellation remains observable. Sync Stop uses the same helper through a synchronous bridge; its asynchronous continuations do not require the WPF synchronization context. Lighting Exit retains its gate until that join completes.

Automatic animation now requires permission, an unsuspended service, a configured animated sync effect and no running worker. Explicit normal-host configuration remains the same. The development build default is advanced to 0.10.8, but `build.ps1` was not run and stable deployment remains 0.10.5.

This does not make a stuck native driver call cancellable. A driver hang may keep shutdown waiting; the planned independent recovery supervisor and actual Windows/termination tests remain required. No hung native driver or normal installed host was exercised in this phase.

## Qualification

Evidence root: `artifacts/recheck-execution-20261018-171753/`.

| Check | Result / evidence |
| --- | --- |
| Application suite | **50 groups passed**, zero failed; `lighting-lifecycle-review/checks-final.txt`. Includes legacy timeout reproduction, pending ownership, success, late fault/cancellation, callback failure, synchronous join and later-controller cleanup. |
| Lighting editor/policy | **33 scenarios passed**, including **96 flag/effect combinations plus a missing-effect case**; zero hardware reads/writes in these synthetic checks. `lighting-lifecycle-hardware-ui/lighting-editor-check.json`. |
| Hardware/UI wrapper | **10 checks passed**: read-only hardware probe, synthetic lighting/GPU checks and seven screenshots. Three screenshots visually inspected: lighting, pump/fan page and GPU curve. Screenshots show saved intent/editor state, not physical lighting acceptance. |
| GPU / maintenance scenarios | **13 GPU** and **six maintenance UI** scenarios passed. These remain synthetic. |
| Package | New 0.10.8 executable setup, **19 guards passed**, 96 payload hashes verified. No 0.10.8 installation attempted. |
| Fresh hardware preparation | Six read-only probes passed at **20:54:04 IST**, using unchanged qualified 0.10.7 diagnostic binaries. This is a baseline, not an independent recovery supervisor. |
| Subsequent receiver sample | **FAIL retained:** five of six reads complete. First lighting read timed out after **4002 ms**, receiving **9/10** packets, zero bad checksums. Two later lighting reads each received 10/10. `lighting-lifecycle-final-state/receiver.json`. No configuration writes or retries to erase the failed result. |
| Final state/payload audit | **PASS at 20:58:36 IST**, `lighting-lifecycle-review-final-audit.json`. This pass covers preservation and artifacts; `qualificationComplete` remains false and receiver failure remains explicit. |

The final audit confirms all 258 fan native bytes, six firmware configurations, both DIMMs' identity/control/colors/brightness, original mouse lighting and 2400 DPI/mode/rate/map/indicator/preferences/five onboard slots, 26 data states, original task XML/security, services, two user shortcuts and stable 0.10.5. Five fresh CPU samples and the display handle read succeeded; no display output was started. Motherboard diagnostic replies agree, but they do not capture every effect/keep/initialization state, so exact motherboard effect rollback remains unqualified.

Two earlier collector failures are preserved separately: empty-array JSON serialization failed before any probe; a null-versus-empty error-list check then falsely reported a motherboard diagnostic error after three successful probes. Raw lighting replies contain no such diagnostic error. Both verifier bugs were corrected before the six-probe passing run. The later receiver timeout is an actual incomplete transaction and is retained as failed. Its wrapper process exit was zero; the collector correctly rejected the report content. Older `probes[].passed` fields describe command completion, not domain acceptance; the reusable collector now labels that field `commandExitedSuccessfully`.

## Package and next gates

Review setup: `installer-lighting-lifecycle/Sora-Setup-0.10.8-x64.exe`, **5,560,044 bytes**, unsigned.

SHA-256: `F3512AEBC1FD9DAF060E669DDD3DB6ED3757B463A7ED725ABD3BC9B557BD67DF`.

Prior 36 installed diagnostic checks and 43 real task checks belong to **0.10.7**. They were not repeated on 0.10.8. The 14 unchanged files within the previous 17-file routing/package audit still match; version/build default/test registration are the three intentional deltas. Do not transfer the old installed result to the new package as an actual installation pass.

- [x] Correct F18 source and test slow-worker ownership plus automatic sync policy.
- [x] Publish/package a separate review candidate and independently verify preserved state.
- [ ] Prepare and qualify independent hardware recovery before normal profile application. Resolve header 4, full board-lighting recovery, raw GPU backup and complete mouse recovery.
- [ ] Diagnose receiver loss with bounded read-only traces, including wake/idle/reconnect; preserve the new 9/10 failure alongside the earlier palette failures.
- [ ] Qualify normal installed 0.10.8 startup, tray Exit, delayed operations and actual Windows lifecycle. F18 integrated closure remains open.

Vendor removal and firmware installation remain **HOLD**.
