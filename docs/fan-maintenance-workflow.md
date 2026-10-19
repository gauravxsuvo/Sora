# Preserving cooling through DeepCool service isolation

Qualified on 18 October 2026 for this PC: Z890 GAMING X WIFI7 / IT8696E, DeepCool 1.2.14 `FanControlService.exe`, six fan headers and Sora's standalone firmware interface. This is a temporary service-isolation workflow. Supported uninstall, reboot, power-loss recovery and other controllers remain separate acceptance cases.

## Why the order matters

DeepCool captures native fan settings at service startup and restores that snapshot when stopped. The initial recheck reproduced two overwritten StartPWM bytes: header 4 `0:A3` (66→64), header 5 `0:AB` (63→64). Restarting the service before recovering those bytes would let it capture the overwritten state again.

The workflow in `scripts/Verify-DeepCoolIsolation.ps1` follows this order:

1. Require administrator access, closed Sora/vendor desktops, stable service states and the identified DeepCool fan service. Preserve every service state/start mode before changes.
2. Capture **all 258 native configuration bytes and all six firmware Zero-RPM configurations**, board/controller/interface, machine identity and timestamp. Flush the new backup to disk before stopping services. A SHA-256 fingerprint detects accidental snapshot changes.
3. Require the known pump (header 0) and radiator (header 5) to be in firmware automatic mode, at least 700 RPM, with firmware stop disabled. Require six valid board temperatures below 75°C. This wiring precondition applies to this PC.
4. Release every hardware handle/mutex, then request service Stop with bounded status waits. Confirm no DeepCool process or service remains active. Capture the actual post-stop state.
5. Restore the current pre-operation baseline while the vendor is absent. Native registers use the existing header allow-lists and restore control/source last. Firmware settings use verified getter/setter transactions. Persist the pre-recovery snapshot and flush an entry **before each attempted native or firmware write**.
6. Verify every header in both domains. Run fresh independent CPU sensor checks, checking vendor absence and observed modules. No fan curve, firmware setting or display mode is deliberately changed for the test.
7. On success or a caught operation failure, make at most two quiescence/recovery attempts, then restart only services that were originally running. A Stop error is retained while other helpers and guarded recovery are still attempted; active writers prohibit recovery writes. Their new startup snapshot now contains the recovered settings. See the [lifecycle continuation](deepcool-lifecycle-qualification.md).
8. Wait three seconds; re-read all native/firmware settings, pump/radiator RPM and temperatures; compare service state and startup mode with the original inventory. Preserve errors even when later recovery succeeds.

If hardware recovery cannot be verified, the workflow requests no vendor restart. A service that failed to stop may still be active; inspect the evidence rather than assuming all services are stopped. It retains the baseline, attempted-write journal and errors for recovery review. It does not silently accept the stale vendor settings. Startup types/tasks are never edited.

## Commands and backup rules

Use an explicitly built maintenance-capable candidate from administrator PowerShell, with a new output directory each time:

```powershell
& E:\Sora\scripts\Verify-DeepCoolIsolation.ps1 `
  -Executable E:\Sora\artifacts\recheck-execution-20261018-171753\candidate-fan-maintenance-final\Sora.exe `
  -OutputDirectory E:\Sora\artifacts\deepcool-isolation-NEW `
  -Samples 5
```

`-InjectProbeFailure` deliberately reports a failed operation after stopping/recovering hardware, and exercises the normal recovery/restart path. Its summary must remain `passed:false`; qualification checks the independently recorded successful restoration. It does not inject a hardware write failure.

The isolated app commands are `--fan-maintenance-capture --output PATH` and `--fan-maintenance-restore --baseline CAPTURE --output NEW_PATH`. Restore requires format 2, this machine, an unchanged snapshot fingerprint, a read-only capture at most one hour old, complete native/firmware maps, the standalone Sora WMI interface, and no detected competing writer. It saves the pre-recovery state first. They do not start the normal Sora host or access daily profile/settings files.

Older baseline formats and aged captures require a separate reviewed recovery procedure; do not rewrite their timestamps, fingerprints or format to bypass checks. A fingerprint is an integrity check, not cryptographic authentication. The standalone `Verify-IndependentSensors.ps1` continues refusing to stop a running fan helper; use the integrated workflow for this case.

## Evidence and precise coverage

Evidence root: `artifacts/recheck-execution-20261018-171753/`.

| Case | Result / evidence |
| --- | --- |
| Full native/firmware recovery, malformed late-header rejection, header isolation, mode-last ordering, no-op writes, readback mismatch | PASS; `FanMaintenanceChecks.cs`, `checks-fan-maintenance-final.txt` |
| Transient failure at each of 258 native writes and each of six firmware writes | PASS; other headers/domains still attempted; a second recovery reaches the baseline; simulated transports |
| First integrated real Stop/recovery/restart | PASS; `fan-maintenance-normal/`; journal records only the two observed native bytes, zero firmware writes |
| First real failed-probe recovery | PASS restoration; operation intentionally FAIL; `fan-maintenance-injected/` |
| Final format-2 candidate: normal isolation | PASS; `fan-maintenance-normal-final/`, five isolated CPU samples 52–55°C, complete native/firmware and service restoration |
| Final candidate: failed-probe recovery | PASS restoration; operation intentionally FAIL; `fan-maintenance-injected-final/` |
| Nine CLI refusal/preservation checks | PASS; `fan-maintenance-refusals-final.json`: wrong machine, stale/future capture, old format, non-read-only capture, changed digest, changed register, active writer, existing output preservation; zero hardware writes/service changes |

The subsequent stop cycles needed no native writes: DeepCool had captured the corrected startup snapshot. No persistent fan/firmware change was requested. The [later lifecycle phase](deepcool-lifecycle-qualification.md) adds 35 simulation checks and four live controlled recovery cases. These results do not establish complete thermal behavior, arbitrary competing-writer races, actual SCM timeout/restart failures, process kill/power loss, or survival of an actual uninstall/reboot. Those cases keep F14 and the broader removal gate partially open.

## Remaining acceptance checklist

- [x] Reusable durable complete backup, native/firmware recovery and post-restart comparison.
- [x] Simulated write failures at every native position and firmware header; independent later recovery attempts continue.
- [x] Real normal isolation and deliberately failed-probe recovery, including corrected vendor startup snapshot.
- [x] Reject corrupt/stale/wrong-machine captures, active writers and evidence overwrites.
- [x] Simulate transition/Stop/restart failures, persistent/transient writers and readback failures; qualify live caught exceptions after completed Stop/restart and failed-probe recovery. See the lifecycle continuation for exact scope.
- [ ] Qualify interrupted/transitional Stop and failed service restart without losing the original hardware/service state.
- [ ] Qualify automatic relaunch and a competing writer appearing during recovery; no forced termination of hardware writers has been performed.
- [ ] Define and qualify reviewed recovery for a terminated process, aged backup, reboot or power loss.
- [ ] Integrate current-state preservation with the exact supported DeepCool uninstall; verify all headers/firmware after uninstall and reboot.
- [ ] Complete sustained physical thermal/cooling qualification and the vendor-disabled observation periods.
