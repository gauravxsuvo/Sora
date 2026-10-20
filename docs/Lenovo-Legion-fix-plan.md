# Legion page reliability fixes

Scope: the Lenovo Legion app page reported as unreliable after 0.13.0-beta.1. No physical Legion laptop is available in this workspace. Validate UI behavior through the actual WPF button handlers with simulated transports; physical firmware compatibility remains unverified.

## Plan

1. Reproduce interaction defects in the published page: selections lost or reset by polling, enabled buttons with no valid selection, hidden/overwritten action feedback, and lighting validation unrelated to the selected effect.
2. Correct selection synchronization: preserve pending edits, update untouched fields when live configuration changes, avoid rebuilding unchanged dropdowns, and synchronize selectors after successful Apply/Restore.
3. Make button state match the action: require valid selections, disable unavailable inputs, explain blocked controls, serialize operations, and preserve action results separately from discovery status.
4. Make errors actionable beside the relevant buttons, preserve requested values after a failed operation, and release busy state in finally blocks. Never claim an Apply succeeded if its follow-up state could not be read.
5. Exercise real WPF click handlers against an in-memory transport: power/charge/GPU Apply and Restore, Spectrum Apply/Restore, lighting Apply/Off, invalid color input, selection refresh, missing interfaces, conflicts, read-only sessions and write/recovery failures. No native device writes.
6. Build the app, run the core checks and interaction checks, render the updated page, and review the result. Record remaining hardware limitations. Prepare a new beta installer only after these checks pass; do not silently replace an existing release asset.

## Confirmed defects

- Apply buttons do not consistently check whether their required selections exist.
- Polling reconstructs picker collections, including while the user interacts with the page.
- Automatic refresh overwrites the last operation result after five seconds.
- Untouched power, charge and GPU selectors can retain old values after live configuration changes.
- Restore may leave the picker on the previously selected target rather than the restored live value.
- Wave and color-cycle effects unnecessarily parse zone color text.
- Editable fields remain active during an in-flight mutation.
- Original UI qualification checked enabled states but did not click Apply/Restore buttons.

## Acceptance

Every enabled action must execute or display an actionable local error. Unavailable actions must have an explanation. Background refresh must preserve edits and results. Successful Apply and Restore must show the resulting live configuration. All interaction scenarios must run without touching physical firmware or device interfaces.

## Completed in the local beta.2 candidate

Steps 1–5 are implemented. The finalized packaged app passed 42 WPF interaction checks, including actual automation invocations of Apply/Restore/Off, pending edits, local validation, recovery, retry and discovery failure. The fixture recorded 23 simulated configuration writes and zero physical configuration writes. All 57 core checks pass. The app builds without warnings or errors, and both page renders were inspected.

Step 6 has a local installer at `artifacts/legion-fixes-0.13.0-beta.2-final/setup/Sora-Setup-0.13.0-beta.2-x64.exe`, with SHA-256 `fe15cc085fbad17acf04d90c0d28190e55de8ad5f681679960d6256f15afacd5`. Installer compilation and payload audit passed. `scripts/Test-ReleaseLifecycle.ps1` now invokes the installed Legion interaction check for versions 0.13.0 and later, while retaining the earlier-version path.

Release qualification must additionally exercise the exact published beta.2 EXE on a disposable Windows VM using the installer qualification workflow. The prior beta.1 lifecycle result does not qualify this new installer. Physical Legion interface validation remains outstanding. See the GitHub prerelease and Actions run for publication and installed lifecycle results; the artifact hash above identifies the initial local candidate.
