# Operating Sora application updates

## Current state

Sora uses `https://sora.portways.app` for the website, installer delivery and signed stable feed. Version 0.12.4 adds setup, icon and removal improvements plus fixes found during disposable Windows qualification. Earlier published installers remain immutable. See [the stable release record](stable-release-0.12.4.md) for exact installer hashes, Windows lifecycle evidence and testing limits. The earlier 0.12.2 and 0.12.3 candidates were not promoted.

The user's free-only requirement supersedes the plan's paid publisher-certificate assumption. Sora verifies **its own signed metadata plus the installer hash**, rather than requiring an Authenticode publisher certificate. Windows may therefore display an unknown-publisher/SmartScreen prompt for initial downloads. There is no paid signing service, updater service subscription or telemetry requirement in this implementation; hosting/storage still use the user's Portways instance.

## Key ownership

A P-256 release key was generated locally at `E:\Sora\.release-keys\release-2026.pem`. Its directory disables inherited permissions and permits only the current user, Administrators and SYSTEM. `.release-keys/` is ignored by Git. The application, helper and website contain only its public key in `release/update-policy.json`.

**Back up this private key securely before distributing the bootstrap installer.** Do not upload it to Portways, include it in an issue, send it through chat, or commit it. Losing it means existing clients cannot authenticate a new signing key unless a still-trusted key first signs a bridge release containing both public keys. A compromised key requires a deliberate recovery release and trust rotation; this initial implementation does not claim online key revocation.

The free key authenticates the update's publisher to installed Sora. It does not create Windows publisher reputation. The initial installer is bootstrapped from the official site/repository; users must trust that initial delivery.

For a future key, use a NEW private directory and key ID. Keep existing public keys in the policy for the overlap:

```powershell
dotnet run --project scripts/Sora.ReleaseTool -- init-key .release-keys-next/release.pem release/update-policy.json sora-release-next
.\scripts\Sync-UpdatePolicy.ps1
```

Add any new private directory to `.gitignore` before creating it. The tool refuses an existing private directory so it cannot overwrite/reuse an unknown key directory.

Directories named `.release-keys*` are already ignored. The private key is intentionally not included in any release artifact; copying the repository to another machine does not copy this ignored key automatically.

## Build a candidate without changing the running install

Do not use `build.ps1` for release qualification: its development workflow updates the development junction. Publish to a fresh artifact directory directly:

```powershell
dotnet publish src/Sora.App/Sora.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:DebugType=embedded -p:UseSharedCompilation=false -o artifacts/RELEASE/publish
.\scripts\Write-ThirdPartyNotices.ps1 -ReleaseDirectory artifacts/RELEASE/publish
.\scripts\Build-Installer.ps1 -ReleaseDirectory artifacts/RELEASE/publish -OutputDirectory artifacts/RELEASE/setup -Compiler tools/inno-setup-6.7.3/ISCC.exe
```

The app's publish target also publishes `UpdateRunner/` with its four required runtime files. The installer builder checks their presence, includes its existing complete payload audit and uses the same AppId/default Program Files path. It does not install, run or promote the result.

This is framework-dependent: the .NET 8 **Windows Desktop Runtime x64** must be installed. Self-contained packaging is not enabled by this change. The installer currently validates that prerequisite instead of silently downloading it.

## Sign and publish

```powershell
dotnet run --project scripts/Sora.ReleaseTool -- prepare artifacts/RELEASE/setup/Sora-Setup-0.12.4-x64.exe 0.12.4 NEXT_SEQUENCE docs/Sora-0.12.4.md release/update-policy.json .release-keys/release-2026.pem artifacts/RELEASE/metadata
dotnet run --project scripts/Sora.ReleaseTool -- verify artifacts/RELEASE/metadata/stable.json release/update-policy.json artifacts/RELEASE/setup/Sora-Setup-0.12.4-x64.exe
```

Use a sequence greater than every previously promoted feed. Feed validity is 30 days; **refresh the signature and promotion with an increased sequence before expiry**, even if the installer version is unchanged. An expired feed produces a check error, not an automatic bypass. The payload is exact UTF-8 JSON encoded in base64; signatures use ECDSA P-256/SHA-256 with 64-byte IEEE P1363 encoding. The exact installer SHA-256 is calculated after all final packaging.

Create the Portways project/storage using `web/sora-downloads/README.md`. Then use its upload/promote commands. Publishing keys live only in the publisher's environment, not in frontend-prefixed env vars. The server reads linked credentials at runtime; environment changes require a restart/redeploy per the supplied guide.

The manual `.github/workflows/release-candidate.yml` workflow performs checks/build/package steps and saves an artifact. It signs only when a feed sequence and `SORA_RELEASE_PRIVATE_KEY` secret are configured; uploading/promoting also needs `SORA_RELEASE_S3_*` secrets. Local release commands can use the ignored publisher environment file instead. Before promotion, run `.github/workflows/qualify-installer.yml` with the candidate version and expected SHA-256. It downloads and exercises the exact published bytes on an administrator GitHub-hosted Windows runner. Its test script refuses personal PCs and self-hosted runners because it deliberately purges synthetic Sora data. Keep its passing qualification JSON and logs with the release. Never promote a candidate whose lifecycle qualification fails.

The website and desktop consume the same `/channels/stable.json` envelope. Website git pushes and Portways deploys do not promote installers. Client checks use no redirects, a compiled HTTPS host allowlist, compatibility checks, expiring metadata and a persisted highest accepted sequence. Corrupt replay settings disable checks until repaired; they are not silently reset. State file: `%LOCALAPPDATA%\Sora\data\app-updates.json`.

## Installation and repair

The updater must itself be installed in `C:\Program Files\Sora`. Development copies can inspect feeds but cannot install updates in place. Existing versions without this feature require one manual bootstrap installation.

Verified downloads and logs live in administrator-owned `%PROGRAMDATA%\Sora-Updates\<transaction-id>`. A trusted helper is copied there so it survives replacement of installed files. It verifies the feed/hash independently and holds the installer file open against replacement. An ACL-restricted same-user pipe carries READY/INSTALL; EOF, a timeout, another running Sora, cleanup failure or failed elevation never authorizes installation. The client rechecks the current promoted feed before handoff to catch withdrawn/replaced offers.

Sora blocks updating while device edits/operations are pending. Once cleanup starts, controls cannot be resumed in the same stopped host. If preparation fails, Sora explains the failure and closes; open it again from its normal shortcut. The installer retains its own process/mutex safeguards. The update helper never force-kills Sora, rewrites its startup task or reboots Windows automatically. It relaunches within the same user/session and requires a startup marker, which confirms application startup, not hardware health.

The existing installer replaces files in place. Power loss is **not guaranteed to roll back atomically**. Do not relaunch automatically after failed installation. Inspect `result.json` / `installer.log`, keep the staged installer, and run repair after checking the installed payload. Older-version installation is still refused by the existing downgrade guard. Prefer a fixed higher-version release to automatic downgrade. User data and original hardware backups remain separate and must not be overwritten by old test snapshots.

Staging retains candidates/logs for repair; automatic retention/cleanup and resuming partial downloads are deferred. Cancellation removes a partial download and retry starts from byte zero. Administrators may archive/delete completed staging transactions after all updater processes have exited; never delete an active transaction. Monitor disk space.

## Qualification and remaining coverage

- Automated Core checks: signed/tampered/unknown-key/expired/replayed releases, version/OS/updater checks, hostile URLs, HTTP errors/redirects, cancellation, corrupt/staged-replaced files and persisted preferences.
- Website tests: same signature format in Web Crypto, trust-policy sync, actual HTTP GET/HEAD/Range behavior using fake storage, errors, safe preview prefixes and cache headers.
- Candidate compile/publish/package and a fresh isolated screenshot verify current source/build output. An isolated UI capture does not establish physical device behavior.
- Disposable Windows lifecycle qualification covers the exact published package: 0.12.1 upgrade, same-version repair, downgrade and running-process refusals, installed UI, shortcut/task ownership, saved-data preservation, reinstall after keep-data removal, junction refusal, complete multi-profile removal and clean reinstall. Earlier lifecycle failures are retained as evidence rather than reported as passes.
- The release record distinguishes those installed-package checks from the hardware-dependent updater handoff. A full real-device close/install/restart cycle, forced power loss, helper crash, denied UAC, cross-session races and reboot-required setup responses remain separate coverage. Automated failure checks do not establish those physical scenarios. Do not run destructive or hardware qualification against the user's daily session.

No paid service is necessary for these checks. A Windows VM and the existing supported hardware are enough; actual Portways access is required for hosted verification.
