# DeepCool service lifecycle continuation

18 October 2026, Asia/Calcutta. **Temporary isolation is qualified for the cases below. F14 and vendor removal remain partially open.** No vendor app was removed. Hardware firmware maintenance still requires verified backup/recovery preparation.

## Changes

The real workflow now uses `scripts/DeepCoolIsolationLifecycle.ps1` for controller transitions and final recovery. Its hardware-free checks invoke those same functions.

- A Stop error is retained while the workflow attempts to stop the other helpers. Previously, the first error could skip them.
- Final cleanup makes at most two quiescence/recovery attempts. A late-finishing Stop error no longer prevents a separately guarded restore attempt. The restore adapter must confirm vendor absence before launching a possible write; the Sora command also checks for conflicts.
- Restart accepts an already running service, waits for StartPending, and settles StopPending before requesting Start. Unsupported states fail explicitly. A fresh status read must confirm the final state.
- Recovery must fully verify native/firmware settings before requesting any restart. A failed restart does not skip other originally running services or the final hardware audit. Every error remains visible and prevents a passing operation result.
- Originally stopped services are not requested to start. Service startup types, recovery policies and startup tasks are not changed.

These checks do not eliminate the interval between checking vendor absence and accessing hardware. Arbitrary competing-writer races remain open.

## Executed evidence

Evidence root: `artifacts/recheck-execution-20261018-171753/`. Candidate: unchanged `candidate-hardware-maintenance-final/` (0.10.6); stable deployment remains 0.10.5.

### Hardware-free checks: 35 passed

`deepcool-lifecycle-checks-final.json`: **22 controller cases and 13 recovery cases**, zero failures, no real hardware access or service operations.

Coverage includes Running/Stopped/StartPending/StopPending, unsupported pause states, Stop/Start/status-wait exceptions, status changing again after a wait, a late-completing Stop, transient/persistent competing writers, partial recovery errors, readback mismatch, failed restart of either original service, post-restart mismatch, originally stopped services, and preservation of an earlier operation error. The persistent-writer case makes zero simulated configuration writes and requests no restart. These simulations do not qualify an actual Windows service timeout or crash.

### Live controlled checks: four passed for recovery

Executed **19:32:53–19:34:14 IST**. Full report: `deepcool-lifecycle-hardware/qualification.json`. Exact scripts used are archived in that directory.

| Case | Operation result | Recovery qualification |
| --- | --- | --- |
| Normal Stop / independent sensor probe / restart | PASS | PASS |
| Inject exception after a completed first Stop | Expected FAIL, retained | PASS |
| Inject exception after a completed first restart | Expected FAIL, retained in service errors | PASS |
| Inject sensor-probe exception after verified recovery | Expected FAIL, retained | PASS |

Each case compared all **258 native bytes and six firmware configurations** against the prior qualified baseline, and compared every original service state/start mode afterward. All matched. Write journals recorded **zero native and zero firmware write attempts** in this continuation; DeepCool had already captured the corrected startup snapshot from the earlier recovery.

The exception switches throw in the service adapter **after an actual service operation has completed**. They do not force an SCM timeout, deny service permissions, break a service executable, or leave a deliberately failed service behind. `-InjectServiceFailure AfterFirstStop` and `AfterFirstRestart` must retain failed operation results; they are qualification switches, not normal maintenance options. The existing `-InjectProbeFailure` is also retained.

The later independent final read-only audit **passed at 19:35:24 IST**, `deepcool-lifecycle-final-audit.json`: native/firmware and historical native baseline, cooling, 26 shared-data states, 13 runtime files, 14 application source/test files, service states and stable deployment all matched. Pump ~3342 RPM, radiator ~1985 RPM, firmware stop disabled on both. Six scripts parsed with zero errors; existing workflow/test evidence was refused with hashes unchanged (`deepcool-lifecycle-safeguards.json`). The earlier 48-group application suite and UI scenarios were not rerun for these script-only changes.

## Observed relaunch policies

`deepcool-lifecycle-policy.json`: all **15 read-only service queries succeeded**.

| Installed service | Observed recovery/start policy |
| --- | --- |
| FanControl | Automatic start; no failure actions listed; no SCM start/stop triggers |
| JZFS Sensor Bridge Service | Automatic start; crash recovery restart delays 5 / 15 / 60 seconds; reset period 86,400 seconds; non-crash failure actions disabled; no SCM triggers |
| JZFS Display Service | Same configured recovery policy as sensor bridge; originally stopped, preserved stopped |

No dependent services were listed. No DeepCool/JZFS matches were found in the queried scheduled tasks or three Run registry locations. This is a bounded inventory: other launchers, startup folders, vendor watchdogs and actual restart behavior were not exhaustively established. No crash was induced and no recovery policy was edited.

## Remaining acceptance and exact continuation

- [x] Use the same transition/recovery implementation for simulation and real isolation.
- [x] Qualify controller transitions, bounded recovery, retained errors, no restart before verification and independent later service attempts in simulation.
- [x] Qualify real normal isolation and caught exceptions after completed Stop/restart, plus failed-probe recovery.
- [x] Record actual service recovery policies without editing them.
- [ ] Actual SCM timeout, failed Start before completion, service disappearing, dependency/access failure and delayed transition behavior on hardware. Simulation coverage above remains separate.
- [ ] Actual automatic restart, external desktop launch and competing writer during recovery. Include the observed 5 / 15 / 60 second recovery windows; do not assume a short absence check proves permanent quiescence.
- [ ] Independent supervisor and durable original service inventory for abrupt orchestrator termination. Define who recovers if the main PowerShell process dies; a `finally` block cannot cover forced termination.
- [ ] Reviewed aged-baseline recovery: reidentify machine/board/controller/firmware interface, inspect the saved digest/map and present cooling, establish absence of writers, and explicitly review why the backup is still applicable. Do not change timestamps or hashes to bypass the current one-hour limit.
- [ ] Reboot/power-loss recovery and exact supported uninstall integration. Preserve settings before uninstall and verify after reboot. No forced power interruption is authorized by these results.
- [ ] Sustained physical cooling/display checks, vendor-disabled rehearsal and post-uninstall observation periods.
- [ ] Program Files packaging, installed permissions, startup, upgrade/rollback and lifecycle qualification. The review candidate remains a folder publish.

The next independent phase can audit and prepare the installer while these hardware cases remain open. Actual firmware installation waits for verified C:/D: backup, separate recovery media and review of the exact offered firmware/instructions. The user's answer remains **no verified backup yet**.
