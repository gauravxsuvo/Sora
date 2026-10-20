# Sora 0.13.0-beta.2

This beta fixes Lenovo Legion page interactions reported after beta.1. Download `Sora-Setup-0.13.0-beta.2-x64.exe` from the GitHub prerelease. Requires Windows x64 and the .NET 8 Windows Desktop Runtime (x64).

- Apply buttons require complete, valid selections. Unavailable buttons explain why they are disabled.
- Dropdowns retain pending edits during refresh. Unedited selections follow live configuration changes.
- Apply and Restore synchronize their own selectors without discarding pending edits in other sections.
- Each section retains action results and errors beside its buttons. Background polling no longer overwrites these messages.
- Inputs and buttons are disabled during mutations, with progress feedback. Busy state clears after failure so the user can retry.
- Static/Breathing color errors highlight the relevant fields. Wave, Color cycle and Off do not depend on unused zone color text.
- UI qualification invokes the actual buttons through WPF automation, exercising transactions, restoration, failures and retries with an in-memory controller.

No physical Legion hardware has been tested. These fixes address reproducible app interaction defects; firmware and device compatibility remains experimental. See [Legion coverage](Lenovo-Legion.md) for the supported interfaces and remaining limitations.

The EXE installer upgrades the existing Program Files installation and preserves settings. Exit Sora from its tray menu before installing. Back up settings before using a beta. Returning to an older stable version requires uninstalling the beta first. The executable is not Authenticode signed; see the included download warning guidance.
