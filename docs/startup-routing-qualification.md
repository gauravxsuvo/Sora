# Startup and shortcut routing qualification

## Current continuation — 18 October 2026

Finding **F17** is fixed in the **0.10.7 review candidate**. Historical checkpoints called routing F15/F16; those identifiers already belong to hardware maintenance scope and incorrect installed-package comparison. Their evidence is preserved with this alias correction. Stable deployment still points to 0.10.5; daily-use installation and vendor removal remain open.

**Latest result, 20:40:15 IST:** actual startup enable/disable/selection, task policy, enabled/disabled installed-task uninstall refusal and exact rollback passed **43 checks**. A fresh independent hardware/state audit passed and the temporary installation was removed. Current checkpoint: `installed-startup-task-checkpoint.json`. Prior 36 installed diagnostic/repair checks, seven diagnostic entry points and six UI scenarios remain applicable to the unchanged package.

Evidence root: `artifacts/recheck-execution-20261018-171753/`.

## Implemented behavior

- A normal launch only reads the root `Sora` task. It never creates, updates, enables, disables or migrates the task, including tasks with an older description/layout. Sign-in remains disabled when the task is absent or disabled.
- Settings shows whether sign-in uses this copy, another copy, a disabled task or an unreadable registration. A failed read disables the checkbox and reports the failure. Unexpected multiple/non-executable actions are treated as unreadable.
- To select another deployment, explicitly turn **Start at sign-in** off, then on in the desired copy. The administrative CLI also requires exactly `on` or `off` as its action; missing/misspelled actions are rejected before registration changes.
- `--startup-task-inspect --output <new-file>` uses the same production inspection as normal launch, records the executable/target/registration, and exits before starting the hardware host. It refuses to overwrite prior output.
- Development `Install-Shortcuts.ps1 -UpdateOnly` refreshes only existing shortcuts whose target is a recognized `dist\Sora-x.y.z\Sora.exe` or `dist\Sora-current\Sora.exe` within that workspace. It verifies junction destinations stay inside `dist`; prefix lookalikes, manual paths, missing targets and other workspaces are preserved.
- Existing installed/manual shortcuts also survive an explicit shortcut creation call unless replacement is requested with `-ReplaceExisting`. Combining that switch with `-UpdateOnly` is refused. Missing development shortcuts stay missing during builds.
- Development `build.ps1` now defaults to `dist\Sora-0.10.7`. It still deploys the development junction; it was **not run** during this qualification. Review publishing and packaging use separate paths.

**Older binaries retain their old behavior.** The original 0.10.5 executable was not patched or overwritten. Before installed cutover, replace/archive obsolete launch shortcuts and qualify the fixed development deployment. Retain old releases for reviewed rollback, and avoid normal launches of those old binaries against installed startup registration. Fixing current source cannot change already-built executables.

## Completed checks

| Case | Result | Evidence |
| --- | --- | --- |
| Full deterministic application suite | **49 groups passed, 0 failed** | `checks-startup-routing.txt` |
| Shared production routing policy | **31 cases**, included in the 49 groups | Both installed/development directions; enabled/disabled/absent tasks; old/current descriptions; stable junction equivalence; four failed-read classes; explicit action validation |
| Actual Windows shortcut fixtures | **25 checks passed** | `shortcut-routing-checks/result.json`; WScript.Shell `.lnk` files and real directory junctions, entirely inside the evidence directory |
| Foreign shortcut preservation | Byte hashes and last-write timestamps unchanged | Installed/manual/other workspace/missing target/prefix spoof/escaping junction; explicit cutover then subsequent development refresh |
| Fresh candidate publish | 0.10.7, no stable deployment | `candidate-startup-routing/`, `candidate-startup-routing-publish.txt` |
| Setup payload and package guards | **96 payload files, 19 checks passed** | `installer-startup-routing/build-audit.json`, `installer-startup-routing-package-checks/result.json` |
| Temporary 0.10.7 installation/repair/uninstall | **36 checks passed** | `installer-startup-routing-live/qualification.json`; seven diagnostic entry points and six UI scenarios |
| Real installed production startup inspection | **Passed without task migration** | `installer-startup-routing-live/startup-routing-inspection.json`; installed executable/target reported; complete task XML unchanged |
| Fresh independent post-uninstall audit | **Passed, 20:23:44 IST** | `startup-routing-installed-final-audit.json`; 258 fan bytes, six firmware configurations, original 2400 DPI/all five mouse slots/preferences, 26 data states, 96 payload hashes, 17 source hashes, task/services/stable junction and both real shortcuts |

Review setup: `installer-startup-routing/Sora-Setup-0.10.7-x64.exe`.

SHA-256: `1B51849DD63A6A5C55BC4D36F57A8A86D06E0780E0997F2242CC91C5178A5C3D`.

The setup/application remain unsigned. The prior 0.10.6 installation qualification is historical evidence, not a substitute for qualification of changed binaries.

## Pre-installation audit and earlier elevation attempt

**20:18:17 IST independent state audit passed:** `startup-routing-final-audit-qualified.json`. All 96 source/staged hashes and the setup hash match; all 26 shared-data states, complete original task XML, stable 0.10.5 junction and three DeepCool service states/start modes are preserved. No Program Files installation, common installed shortcut, Apps entry or Sora/DeepCool/Kreo desktop process remains. Seventeen source files were copied/hash-verified to `startup-routing-source/`; seven PowerShell files parsed without errors. No hardware probe or application configuration/task/real-shortcut change was requested in this phase. Fan/mouse physical state was last freshly audited in the preceding installer phase; no fresh hardware pass is claimed here.

The first state-audit attempt had a verifier regex-escaping error after its initial nine checks. Its failed JSON and script are preserved as `startup-routing-final-audit.json` and `Audit-StartupRoutingQualification-initial.ps1`. The corrected, complete audit is the separately named qualified result above.

Windows **canceled the administrator elevation request before the temporary 0.10.7 runner started**. The runner result and `installer-startup-routing-live/` directory do not exist. The launch command's trailing status object incorrectly printed `started: true` despite the Windows cancellation; it is not execution evidence. Use `$ErrorActionPreference = 'Stop'` in the next elevation launcher and require an actual runner result before accepting success. There is no pending installer or background recovery operation.

At the earlier checkpoint the in-app Windows prompt question was unanswered. The user subsequently confirmed approval. The previous elevation attempt had already expired, so a new launcher with `$ErrorActionPreference='Stop'` was used. It returned an actual PID; the durable runner and qualification JSON now both exist and pass. The older cancellation/failure records remain unchanged. No background installer or recovery remains.

## Remaining qualification

- [x] Complete the temporary install/repair/uninstall rerun, including the production startup inspector and complete task XML preservation.
- [x] Independently audit source/staged payload/setup hashes, 26 data states, original task/services/stable junction and absence of an installation after canceled elevation.
- [x] After actual installation qualification, independently recheck native/firmware fan settings, original mouse state, user shortcuts and all preserved state. Completed with fresh reads, not inferred from the pre-install audit.
- [x] Qualify explicit installed startup enable/disable/rollback with durable original task XML/security descriptor and guarded recovery. Verify action, arguments, working directory, current interactive user/highest privilege, enabled sign-in/wake triggers, eight-second delay, concurrency/recovery policy and enabled choice.
- [x] Exercise installed-task uninstall refusal for enabled and disabled installed registrations. Verify payload and full XML preservation, recovery after an injected operation failure, exact original XML/security/enabled choice and successful uninstall after restoration.
- [ ] Qualify watchdog recovery after actual parent termination/deadline and denied task restoration. The completed watchdog verified the runner's successful recovery; it did not perform fallback recovery in the passing run.
- [ ] Qualify normal installed startup/profile application, single-instance behavior and tray Exit, including independent restoration and the saved 800 DPI versus restored 2400 DPI distinction.
- [ ] Complete sign-in, sleep/wake, reconnect, reboot/sign-out/shutdown, physical acceptance and the remaining F10/F14 lifecycle cases.
- [ ] Update/retire obsolete real shortcuts and switch daily-use deployment only after the installed candidate passes its lifecycle checks.

The completed task writes are registration checks; the task was never run. Actual sign-in/wake/crash restart, Settings checkbox interaction and protection/antivirus acceptance over daily use remain open. No firmware or vendor software changes are included.

## Actual task qualification and preserved recovery failure

`installed-startup-task-qualified/qualification.json`: **43 PASS**, 20:40:13 IST; runner and independent final audit also PASS. Checks cover missing/misspelled administrative arguments with unchanged task XML, absent/off idempotency, explicit selection in both deployment directions, read-only production inspectors, exact task policy, uninstall refusal in both enabled states, and original task restoration. Every command has a durable pre-launch evidence record. Normal hardware host, task Run, Windows sign-in and wake were not invoked.

Original task XML and owner/group/DACL are flushed before changes; an independent elevated process watches the runner identity/deadline. Recovery uses the saved XML with registration triggers suppressed, then restores the security descriptor separately if required. The passing run retained the original permissions without needing the conditional permission write. The independent watchdog confirmed original state and exited without registering again. Its termination/deadline and conditional permission-write branches still need separate qualification.

Initial `installed-startup-task-live/qualification.json` remains **FAIL**: 38 checks passed and two exact-recovery checks failed. Passing the saved SDDL directly to `RegisterTask` restored effective permissions/path/enabled state but added a `RegistrationInfo/SecurityDescriptor` element to exported XML. The corrected helper restores XML without that argument and checks permissions separately, consistent with Microsoft's separate [registration](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/nf-taskschd-itaskfolder-registertask) and [registered-task permissions](https://learn.microsoft.com/en-us/windows/win32/taskschd/registeredtask-setsecuritydescriptor) APIs. Fresh local comparison establishes exact recovery; the API documentation alone does not establish the observed serialization behavior.

`installed-startup-task-live/corrected-task-recovery-elevated.json` records exact recovery and supported cleanup before the new full rerun. The initial scripts, failed results and a separate access-denied attempt without an administrator token are preserved. Tool escalation did not itself create an elevated Windows token; subsequent mutation used an approved `RunAs` process. `installed-startup-task-first-elevation.json` preserves an expired UAC request that never started the runner.

`installed-startup-task-final-audit.json`: **PASS**, 20:40:15 IST. Fresh reads verified 258 native fan bytes, six firmware configurations, automatic cooling, original mouse 2400 DPI/mode 1/1000 Hz and all five onboard slots/buttons/preferences. All 26 data states, 96 payload/source files, 17 qualified source files, three service states/start modes, task XML/permissions, stable 0.10.5 and both user shortcut hashes/timestamps are preserved. No temporary installation, common installed shortcut, Apps entry or Sora/DeepCool/Kreo desktop process remains.
