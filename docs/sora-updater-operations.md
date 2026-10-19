# Operating Sora application updates

## Current state

The implementation targets `https://sora.portways.app`. Portways provisioning has not been verified from this workspace. The updater baseline was pushed to GitHub; the subsequent branding changes are local. No installed Sora, shortcut, startup task or hardware profile has been changed by this work. Current candidate: 0.12.1, with the reference-derived circular violet logo. The previously signed 0.12.0 candidate is retained unchanged.

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
dotnet run --project scripts/Sora.ReleaseTool -- prepare artifacts/RELEASE/setup/Sora-Setup-0.12.1-x64.exe 0.12.1 2 docs/Sora-0.12.1.md release/update-policy.json .release-keys/release-2026.pem artifacts/RELEASE/metadata
dotnet run --project scripts/Sora.ReleaseTool -- verify artifacts/RELEASE/metadata/stable.json release/update-policy.json artifacts/RELEASE/setup/Sora-Setup-0.12.1-x64.exe
```

Use a sequence greater than every previously promoted feed. Feed validity is 30 days; **refresh the signature and promotion with an increased sequence before expiry**, even if the installer version is unchanged. An expired feed produces a check error, not an automatic bypass. The payload is exact UTF-8 JSON encoded in base64; signatures use ECDSA P-256/SHA-256 with 64-byte IEEE P1363 encoding. The exact installer SHA-256 is calculated after all final packaging.

Create the Portways project/storage using `web/sora-downloads/README.md`. Then use its upload/promote commands. Publishing keys live only in the publisher's environment, not in frontend-prefixed env vars. The server reads linked credentials at runtime; environment changes require a restart/redeploy per the supplied guide.

An optional manual GitHub workflow, `.github/workflows/release-candidate.yml`, performs the same checks/build/package steps and saves an artifact. It signs only when a feed sequence and `SORA_RELEASE_PRIVATE_KEY` secret are configured; it uploads/promotes only when its explicit `promote` input is selected and the `SORA_RELEASE_S3_*` secrets are configured. Run release commands locally if you do not want hosted CI. No workflow or public promotion has been run by this change.

The website and desktop consume the same `/channels/stable.json` envelope. Website git pushes and Portways deploys do not promote installers. Client checks use no redirects, a compiled HTTPS host allowlist, compatibility checks, expiring metadata and a persisted highest accepted sequence. Corrupt replay settings disable checks until repaired; they are not silently reset. State file: `%LOCALAPPDATA%\Sora\data\app-updates.json`.

## Installation and repair

The updater must itself be installed in `C:\Program Files\Sora`. Development copies can inspect feeds but cannot install updates in place. Existing versions without this feature require one manual bootstrap installation.

Verified downloads and logs live in administrator-owned `%PROGRAMDATA%\Sora-Updates\<transaction-id>`. A trusted helper is copied there so it survives replacement of installed files. It verifies the feed/hash independently and holds the installer file open against replacement. An ACL-restricted same-user pipe carries READY/INSTALL; EOF, a timeout, another running Sora, cleanup failure or failed elevation never authorizes installation. The client rechecks the current promoted feed before handoff to catch withdrawn/replaced offers.

Sora blocks updating while device edits/operations are pending. Once cleanup starts, controls cannot be resumed in the same stopped host. If preparation fails, Sora explains the failure and closes; open it again from its normal shortcut. The installer retains its own process/mutex safeguards. The update helper never force-kills Sora, rewrites its startup task or reboots Windows automatically. It relaunches within the same user/session and requires a startup marker, which confirms application startup, not hardware health.

The existing installer replaces files in place. Power loss is **not guaranteed to roll back atomically**. Do not relaunch automatically after failed installation. Inspect `result.json` / `installer.log`, keep the staged installer, and run repair after checking the installed payload. Older-version installation is still refused by the existing downgrade guard. Prefer a fixed higher-version release to automatic downgrade. User data and original hardware backups remain separate and must not be overwritten by old test snapshots.

Staging retains candidates/logs for repair; automatic retention/cleanup and resuming partial downloads are deferred. Cancellation removes a partial download and retry starts from byte zero. Administrators may archive/delete completed staging transactions after all updater processes have exited; never delete an active transaction. Monitor disk space.

## Qualification before stable promotion

- Automated Core checks: signed/tampered/unknown-key/expired/replayed releases, version/OS/updater checks, hostile URLs, HTTP errors/redirects, cancellation, corrupt/staged-replaced files and persisted preferences.
- Website tests: same signature format in Web Crypto, trust-policy sync, actual HTTP GET/HEAD/Range behavior using fake storage, errors, safe preview prefixes and cache headers.
- Candidate compile/publish/package and a fresh isolated screenshot verify current source/build output. An isolated UI capture does not establish physical device behavior.
- Required before public stable: clean VM install; upgrade from installed 0.11.0; denied rights; cross-session/competing launch; helper crash and cleanup failure; real S3 upload/promotion/cache/range behavior; reboot-required setup result; failed restart; interrupted-install repair. The update helper's real privileged handoff has not yet been qualified on an installed machine.
- Required hardware qualification: bounded successful cleanup and reapplication on the actual supported devices, retaining pre/post original state and profiles. Do not run this silently against the user's daily hardware session.

No paid service is necessary for these checks. A Windows VM and the existing supported hardware are enough; actual Portways access is required for hosted verification.
