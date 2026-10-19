# Sora 0.12.0 updater candidate — local qualification

19 October 2026. Candidate only; no stable promotion or daily-use installation change.

## Verified

- Windows application/helper build and framework-dependent win-x64 folder publish completed with zero compiler warnings/errors.
- Existing application checks plus application-updater checks: **52 groups passed, 0 failed**. The new group exercises signatures/tampering/unknown keys, expiry/replay, versions/OS requirements, hostile URLs, download errors/cancellation, hash verification, staged-file modification and state persistence. Synthetic installer bytes are never executed.
- WPF maintenance UI checks: **8 checks passed** in `artifacts/updater-0.12.0/maintenance-ui-final.json`, including distinct application installation controls and separately labelled BIOS/driver actions. A historical SSD test asserted an obsolete empty-state label; it now checks that the prior drive identity is cleared, preserving the original intent without changing SSD behavior.
- Current-source isolated Updates screenshot: `artifacts/updater-0.12.0/sora-updates-final.png`, copied into the website. It uses the current light UI. No historical dark keyboard screenshot is used by the website.
- Website: **6 tests passed**, covering HTTP GET/HEAD/Range, static assets, honest missing-storage responses, error handling, caching, safe storage prefixes and signed-release verification.
- Headless Edge desktop/mobile visual checks passed at widths **1440 and 390**, with no horizontal overflow. Evidence: `artifacts/updater-0.12.0/website-layout-checks.json` and `website-*-verified.png`.
- Final candidate installer built from 69 audited payload files using the signature-verified Inno Setup 6.7.3 compiler. Evidence: `artifacts/updater-0.12.0/setup-final/build-audit.json`. The installer is 6,108,416 bytes. SHA-256: `720F0090B2628202028CC357679AC68F4BF8A320F71C0696B7453B0B36028E85`.
- Candidate feed signed with the local free release key and verified independently in .NET and Web Crypto against the exact installer bytes. Evidence: `artifacts/updater-0.12.0/release-signature-checks.json`. Local sequence 1 has not been uploaded or promoted; do not assume it is the next available sequence if another feed has since been published.
- The private-key directory and Node dependencies are ignored by Git. The browser receives only public trust policy. Local test server was stopped after qualification.

## Limits and required next checks

`sora.portways.app` and linked object storage are not provisioned. Live S3 multipart upload, conditional promotion, public download caching/range behavior and an actual desktop-to-host check have not been exercised. The website shows the unconfigured-service state instead of a fake download.

The privileged helper has been compiled and packaged, but its actual machine-wide update transaction has not been exercised. Test clean install, old-to-new install/restart, startup/other-session races, denied rights, failed cleanup, installer failure/reboot-required, restart failure and interrupted-install repair on an isolated Windows machine before stable promotion. Separately qualify bounded cleanup/reapplication on supported hardware with original-state records. The existing installer does not offer guaranteed atomic rollback.

The existing installer-qualification script intentionally installs in Program Files and requires no existing Sora installation. It was not run against this user's daily-use installation. No startup task, shortcut, normal install or hardware profile was changed during this work.

Private signing-key backup, Portways provisioning and public release promotion remain operator setup steps. See [the operating guide](sora-updater-operations.md) and [Portways website setup](../web/sora-downloads/README.md).
