# Sora 0.12.4 stable release

Released on 19 October 2026 through the signed Portways stable feed, sequence 4. The feed signature expires on 18 November 2026; refresh it with a higher sequence before expiry if no newer release is published.

- Installer: `Sora-Setup-0.12.4-x64.exe`
- SHA-256: `0C6FF62FD7DE1997EF33C24A8595C7EEF3DA7C6A1F809E734A4576C9A6E74315`
- [Download website](https://sora.portways.app/#download)
- [GitHub release](https://github.com/gauravxsuvo/Sora/releases/tag/v0.12.4)
- [Windows qualification run](https://github.com/gauravxsuvo/Sora/actions/runs/39278754977)

## Verification

| Check | Result |
| --- | --- |
| Application logic | 53 check groups passed |
| Website verification and HTTP behavior | Six tests passed |
| Exact published installer on disposable Windows Server 2022 | All 46 lifecycle checks passed |
| Installed maintenance UI | All eight scenarios passed without configuration writes |
| Package build and audit | Passed; 70 payload files |
| Defender custom scan of this exact installer | No threats found |
| Public Portways download | Streamed bytes match signed size and SHA-256 |
| Stable promotion | Public feed returned exact signed sequence-4 metadata |
| Existing 0.12.1 client using isolated settings | Accepted live feed and offered 0.12.4 |
| 0.12.4 client using isolated settings | Accepted live feed and correctly reported no newer update |

The Windows checks install the previous public 0.12.1 package, seed synthetic data in registered Windows profiles, upgrade to the exact 0.12.4 installer, verify every installed manifest hash and check versioned shortcut icons. They cover same-version repair, downgrade refusal, running-process refusal, keeping data during uninstall, reinstalling retained data, refusal of junctions and foreign startup tasks, complete data removal, preservation of unrelated files and shortcuts, removal of the installation directory, clean reinstall and final uninstall.

The 0.12.2 preview exposed a reinstall failure when legacy installed data was retained. The 0.12.3 candidate fixed that but exposed an empty installation-directory remnant after a later full uninstall. Version 0.12.4 fixes both. A timing race in the test harness was also corrected: it now waits for Inno's temporary uninstaller to close its log before attempting reinstall. The final 46-check result matches the installer hash above.

GitHub's run-status and step responses lagged behind the available uploaded evidence. The final `qualification.json` was downloaded and checked for a passing result, matching run ID, version, hash and all 46 passing checks before promotion. Local evidence is retained under `artifacts/stable-0.12.4/qualification-final`.

## Limits

These tests do not establish a physical-device close/install/restart cycle, atomic recovery from power loss, reboot-required setup handling or every redirected user-data layout. The actual installed user's hardware controls and saved data were not used for destructive qualification. Read-only client probes run the real application update-check service with isolated settings and do not start the application or its hardware host.

An additional offline Windows Sandbox attempt was blocked by Smart App Control before installation and does not count as a passing qualification. Its Code Integrity event 3077 identifies policy `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`, documented by Microsoft as [VerifiedAndReputableDesktop](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/inbox-appcontrol-policies). Neither host nor guest security policies were changed. Both disposable local Sandbox sessions were stopped. The passing installed lifecycle evidence comes from the GitHub-hosted Windows runner.

The installer is not Authenticode signed. A clean Defender scan does not override Chrome's suspicious-download verdict or prove Smart App Control compatibility. Free update metadata signatures authenticate Sora's releases and installer hashes; they do not establish a publicly trusted Windows publisher identity. See [download warnings](download-warnings.md).
