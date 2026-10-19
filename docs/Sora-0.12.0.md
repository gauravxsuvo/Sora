# Sora 0.12.0 — application updater (release candidate)

Source and candidate installer only. This build has not been installed into the daily-use Sora or promoted on a public update feed.

- Updates has a dedicated Sora application section with installed/latest versions, release notes, manual checks, automatic daily checks, download/cancel, skip-version and install/restart controls.
- Downloads use a pinned P-256 public key, signed expiring release metadata, replay protection and exact installer size/SHA-256 verification. This is a free release-signing scheme; the installer does not carry a paid Windows Authenticode certificate.
- A separate helper stages in an administrator-owned directory, rechecks the release, blocks competing launches, waits for a successful cleanup handshake and process exit, runs the existing Inno installer without forced closing/reboot, verifies the new version and waits for startup acknowledgement.
- Reported hardware cleanup failures stop installation. Saved profiles, existing original hardware backups, the installer AppId, Program Files path and startup preference are retained.
- The Portways-ready website and streaming download server live in `web/sora-downloads`. The host is planned as `sora.portways.app`; it must be created and connected to object storage before checks or downloads can succeed.

See [setup, release operations and qualification](sora-updater-operations.md). Actual machine-wide upgrade/restart, interrupted-install repair and hardware lifecycle qualification remain required before public stable promotion. This in-place installer does not promise transactional rollback.
