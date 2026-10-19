# Setup and website improvements, 19 October 2026

## Findings

The installed `C:\Program Files\Sora\Sora.exe` reports 0.12.1 and its extracted executable icon is the purple circular mark. The common Start menu shortcut and user's OneDrive desktop shortcut point to that installed executable. The Start menu's old blue Recent icon is therefore consistent with Windows shell caching, not an old installed binary. The new installer supplies `Assets/sora-brand-VERSION.ico`, sets explicit shortcut icons and AppUserModelID, repairs owned existing shortcuts and notifies the shell. It does not delete Windows' global icon database or restart Explorer.

An immediate repair script was prepared at `scripts/Repair-SoraIcons.ps1`. Direct Program Files access was denied; the requested Windows UAC launch was cancelled. Consequently the current installed shortcuts were not changed by this task. The user may run that script in an Administrator PowerShell, or use the new installer. A cached pinned/Recent item may still need re-pinning or signing out and back in.

Chrome's reported wording is "suspicious download blocked". No claim is made that this is only a reputation warning or that a new package resolves it. Non-remediating Windows Defender scans of the exact 0.12.1 published installer and initial 0.12.2 candidate reported no threats. A clean scan does not override Chrome's verdict or guarantee safety. The files remain unsigned for Authenticode. See `docs/download-warnings.md`.

## Changes and checks

- WPF application/helper build passed with warnings treated as errors.
- 53 application check groups passed, including bounded cleanup-directory removal, preservation of neighboring data, unsafe path rejection and substituted-file refusal. The removal group was rerun after tightening permission-error handling. A separate synthetic NTFS junction check passed and preserved the target's unrelated data.
- Six website tests passed, including HTTP routes, range behavior, release signature verification and no em dashes in the HTML copy.
- The application maintenance UI checks passed in an isolated process. The website uses a fresh 0.12.2 app capture.
- Browser layout checks covered 320, 390, 640, 768, 1440 and 2560 px, with no horizontal overflow. A read-only preview of the live signed feed confirmed the enabled download button and correct installer URL at all six widths. An initial QA proxy incorrectly treated a missing Content-Length as zero; the proxy was corrected and these checks then passed.
- Installer compilation and payload manifest audit passed. Final package SHA-256 is `FBBD0C72603BBFF653CD9CFD49CED4BD5DE6386930770260CBB69082A112BE5A`. A non-remediating Windows Defender scan of that final package reported no threats. Current artifacts, screenshots and scan records are under `artifacts/setup-improvements-0.12.2`.

## Removal scope and limits

Interactive uninstall offers complete data removal or keeping data. Silent uninstall keeps data unless `/PURGEDATA=1` is supplied. Both modes remove this installation's startup task and shortcuts. Complete removal deletes Sora's Local/Roaming application directories, standard Sora update-download folders, legacy `.net/Sora` extraction cache, machine-wide `Sora-Updates` staging, machine-wide Sora data and legacy installed `data` directory. It uses Windows' registered user profiles rather than guessing arbitrary drive paths. Versioned Sora shortcut icon files are also removed. Inno Setup removes tracked installed payload and its registration.

The helper first validates known data roots, rejects reparse points, refuses a running Sora or competing updater and validates startup-task ownership. It does not follow links or delete files from arbitrary locations. Shared dependencies, vendor drivers, firmware and manually exported files outside Sora-owned directories are not removed. Custom redirected profiles/data paths require separate inspection if validation refuses them. Deletion failure may leave a partially removed data set; installed program files are retained if preparation fails. No filesystem transaction or undo is promised for deliberate data removal.

Actual privileged uninstall and an old-to-new installation have not been exercised against this user's active installation. Destructive tests must use an isolated Windows installation with synthetic profiles, task/shortcut ownership cases and reparse-point fixtures before making broader claims. No real saved profiles were deleted, no shared driver was removed and no running hardware controller was terminated during this task.

The final 0.12.2 installer was uploaded with conditional overwrite protection and its public download hash verified. It is available directly as a setup preview. The stable feed remains on 0.12.1; the 0.12.2 feed has been signed locally but not promoted. The website copy distinguishes that preview from the stable download.
