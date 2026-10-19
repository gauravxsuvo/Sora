# Sora 0.10.10 — saved lighting sync at startup

Installed permanently on 18 October 2026 at `C:\Program Files\Sora\Sora.exe`. The user confirms RAM, case-fan RGB, AIO pump/radiator RGB and motherboard lights are now off and the window responds normally.

## Fixed

With sync enabled and its saved effect set to Off, startup previously replayed older per-zone settings. Three board zones retained static colours even though the editor showed the saved common Off effect and reported “Synced”.

Startup now resolves the common saved sync effect before applying both RAM and motherboard settings. Independent lighting keeps its separate settings. A sync request is reported as applied only after successful application; animations wait for the first complete frame. Saved but unapplied sync is labelled accordingly and can be applied again. Suspension and failed operations invalidate applied status.

“Synced” describes successful application, not continuous optical monitoring. Software cannot detect whether an unconnected header emits visible light, and another controller can subsequently change the hardware.

## Evidence

Under `artifacts/recheck-execution-20261018-171753/lighting-sync-startup-20261018-233141/`:

| Check | Result | File |
| --- | --- | --- |
| Application checks | 51 passed, 0 failed | `application-checks.txt` |
| Lighting scenarios | 46 passed, zero hardware reads/writes | `lighting-editor-checks.json` |
| Package guards | 19 passed | `package-guards/result.json` |
| Permanent installed payload/version | 96 files verified; 0.10.10 | `upgrade-completion.json` |
| Startup log/settings | Both DIMMs and all seven board zones applied Off | `upgrade-completion.json` |
| Physical result | Connected lighting off; window responsive | `physical-confirmation.json` |

Setup: `installer/Sora-Setup-0.10.10-x64.exe`, SHA-256 `AE38DBD1E5D7A21669272C66D42FFD1F254E80593F3665EFB33E14CD78E87B69`. The package is unsigned and requires the x64 .NET 8 Windows Desktop Runtime.

## Installation and limits

The original permanent 0.10.9 install and subsequent 0.10.10 upgrade preserve user data. Desktop and both Start menu shortcuts target Program Files. Automatic sign-in startup is off, with no registered Sora startup task. Motherboard fan auto-apply remains off while controller ownership is unresolved; unused channel 4 is excluded from saved profiles. SATA-powered case-fan motors have no established software speed-control connection.

Preserved failures: the first permanent-install audit encountered account-name ACL translation failure; the first package attempt refused missing legal notices; the post-launch upgrade evidence collector became CPU-bound. A separate completion script stopped only that identified collector and verified the installed payload/settings/host. Sora itself remained running and was physically confirmed responsive. Original failed/partial reports and scripts are retained.

This is a startup Off acceptance result. Other effects, full profiles, actual games, sleep/reconnect/restart, vendor independence and elapsed observation remain open. Follow [regular-use checks](regular-use-checklist.md); vendor removal and firmware installation remain HOLD.
