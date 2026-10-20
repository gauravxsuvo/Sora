# Sora 0.13.0-beta.1

This is an experimental Windows x64 installer release introducing Lenovo Legion laptop controls. It has passed automated protocol, controller and simulated UI checks. **No physical Legion laptop has been tested.** Device and firmware compatibility remains unverified; this is a beta for evaluation.

## Download and run

1. Install the .NET 8 Windows Desktop Runtime (x64) if it is missing.
2. Download `Sora-Setup-0.13.0-beta.1-x64.exe` from this release. Compare its SHA-256 with `SHA256SUMS.txt` if desired.
3. Exit an existing Sora from its tray menu and run the installer. It installs to Program Files and upgrades an existing Sora installation while retaining saved settings. Back up settings before trying the beta; the older stable installer will refuse a downgrade over this newer version. Returning to stable requires uninstalling the beta first.

The executable is not Authenticode signed. See the included download warning guidance; do not disable browser or antivirus protection. Beta updates are distributed manually through GitHub prereleases, separately from the signed stable update feed.

## New Legion controls

- Legion laptop identity checks, including a pinned catalog of 48 machine types; Legion Go and towers are excluded.
- Quiet, Balanced and Performance presets using the detected Lenovo Gamezone interface. Performance requires AC power.
- Normal, Conservation and Rapid charging modes through the installed Lenovo Energy Management driver.
- Supported firmware GPU mode configuration, with explicit restart and active-routing limitations.
- Four-zone lighting effects and colors on recognized ITE interfaces.
- Spectrum stored profiles and brightness on compatible interfaces.
- A dedicated Legion page, independent feature detection, known competing-controller checks, transaction backups, readback and recovery handling.
- Read-only Legion diagnostics and a standalone PowerShell collector for future testers.
- A website beta button that discovers published Windows prerelease assets independently of stable downloads.

No Legion settings are automatically applied at startup or on resume. Sora does not install Lenovo drivers. A missing interface disables its own controls. Configuration changes are explicit; close other device controllers before applying them.

## Limits

Custom fan curves, custom power limits, Spectrum per-key editing, Corsair/iCUE lighting, BIOS UMA switching and alternative GPU backends are not included. Firmware GPU configuration readback does not prove that display routing has changed. Four-zone lighting cannot read or restore the original colors before Sora's first command. Recovery files provide diagnostic evidence and are not automatically replayed after a crash or reboot.

See [Legion coverage](Lenovo-Legion.md), [diagnostics](Lenovo-Legion-diagnostics.md) and the pinned [source inventory](lenovo-legion-sources.json). Sora's original source is now provided under GPL-3.0-only; dependencies retain their own licenses.

## Report an issue

Include the exact Sora version, laptop model or machine type, Windows version, firmware version and steps to reproduce. A read-only report can be collected with:

```powershell
.\Sora.exe --lenovo-legion-diagnose --output legion-report.json
```

Review and redact personal information before attaching reports to [GitHub Issues](https://github.com/gauravxsuvo/Sora/issues).
