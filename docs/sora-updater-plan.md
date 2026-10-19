# Sora downloads and updater plan

Planning baseline, 19 October 2026. A local 0.12.0 candidate now implements the updater and Portways website. See [the operating guide](sora-updater-operations.md) for actual completion/qualification status. The user's later free-only requirement supersedes the Authenticode purchase assumption below: this implementation authenticates releases using a free pinned P-256 key and signed installer hashes. The website is one Node application serving static assets and read-only streamed S3 downloads, without a separate API project. Hosting is not provisioned yet.

## Recommended approach

Use Portways for a small public download website, and its S3-compatible object storage for immutable release files and a signed update feed. Extend Sora with an application updater that downloads and verifies the existing Inno Setup installer, then coordinates a clean shutdown, installation and restart through a small trusted helper.

Keep the current machine-wide install identity and `C:\Program Files\Sora\Sora.exe` path. Start with Windows x64 and the stable channel. Check automatically, but require the user to choose installation. Add beta releases, automatic downloads and staged rollout after the basic upgrade path passes qualification.

This recommendation follows the existing deployment model. Velopack is a credible alternative with WPF integration, update packages and per-machine MSI support, but switching would require installer identity, startup routing, permissions and existing-install migration work. Reconsider it if maintaining the helper and installer lifecycle becomes more expensive than a packaging migration; do not combine that migration with the first update feature.

## Findings from this repository

- `src/Sora.App/Sora.App.csproj` targets .NET 8 WPF and declares version 0.11.0. Read the running binary's version for comparisons; historical installation documents describe older versions.
- `src/Sora.App/app.manifest` requires administrator privileges. Program Files installation also requires elevation.
- `installer/Sora.iss` already has a stable AppId, rejects downgrades, checks runtime prerequisites and refuses installation while Sora is running. Automatic application closing and restart are disabled.
- `scripts/Build-Installer.ps1` packages a qualified folder publish and records payload hashes. It validates the compiler's signature; this does not mean the resulting Sora installer is signed.
- Data lives separately under `%LOCALAPPDATA%\Sora\data`; upgrades must retain current profiles and original hardware backups.
- `App.ExitAsync()` runs window/hardware/tray cleanup, logs failures and then shuts down. Updating needs an explicit cleanup result and handoff protocol; process exit alone does not prove successful cleanup.
- The existing Updates page concerns motherboard drivers and BIOS. Sora application updates need their own clearly labelled section and controls.

## Architecture and Portways layout

```text
Windows release build / CI
  -> qualify payload -> build and sign installer -> sign release metadata
  -> upload immutable release files -> verify public downloads
  -> publish signed stable feed LAST

Portways website                         Portways object storage
  Home / Download / Release history       versioned installers and notes
  Reads the same release metadata         signed stable/beta feeds
             |                                  |
             +-------- Sora update client ------+
                               |
                   verified download -> user chooses install
                               |
                 trusted helper + successful hardware cleanup
                               |
                     Inno Setup -> verified restart
```

Suggested repository additions:

```text
web/sora-downloads/                 Portways project root; index.html + static assets
release/schema/                    versioned manifest schema and verification rules
scripts/Publish-SoraRelease.ps1     release upload, verification and promotion
src/Sora.Core/Updates/              metadata verification, policy and downloads
src/Sora.App/                       app update UI and lifecycle coordination
src/Sora.UpdateRunner/              minimal signed Windows update helper
.github/workflows/                  Windows release build and gated promotion
```

Use one static website project initially. A separate API is unnecessary for public downloads and a signed static feed. If a dynamic API or release dashboard is added later, give it its own Portways project/root directory as the supplied guide specifies. An eventual Node service must listen on `process.env.PORT`; a custom Docker image must expose its routed port.

Illustrative addresses, subject to the actual domain chosen:

- `https://sora.<your-domain>/` — product and download website.
- `https://downloads.<your-domain>/channels/stable.json` — signed feed envelope.
- `https://downloads.<your-domain>/releases/0.12.0/win-x64/Sora-Setup-0.12.0-x64.exe` — immutable artifact; version is an example.

Configure the bucket's public CDN domain. Store public files under `public/`; the guide maps those keys to the CDN domain without the `public/` prefix. Website and desktop clients receive no storage credentials. CI uses a separately revocable bucket access key, as the guide prescribes for clients outside Portways. Signing keys stay outside the web host and bucket.

Use long immutable caching for versioned files. Serve the channel feed with revalidation/no-cache and a short allowed freshness window; configure and test the actual Cloudflare rule so an old cached feed cannot delay withdrawal indefinitely. Publishing a website must never implicitly promote a desktop release. Preview sites must not have production publishing credentials.

### Hosting constraints from the supplied guide

- Keep release files in object storage, not the website container filesystem; redeploys replace containers.
- Default single upload request limit is 25 MB, and the public proxy has a 100 MB request-body cap. Use S3 multipart uploads with parts below both configured limits, such as 8 MiB. The request-body cap is not a 100 MB download-file limit.
- Default internet bandwidth is 10 Mbit/s shared across the storage add-on. Verify/tune limits with the operator, use cacheable immutable URLs and test real installer download times. Do not assume all EXE sizes are automatically edge-cached.
- Default bucket allowance is 5 GB. Keep the current and previous known-good releases available, define retention and monitor capacity before adding large self-contained bundles.
- Support/test GET, HEAD and Range requests, including interrupted downloads and 429 Retry-After handling. Resume only if the artifact identity/ETag still matches, and hash the complete file afterward.
- Storage backups in the guide are local to the Portways machine. Retain release artifacts and signed metadata independently in CI or an offsite archive.

## User experience

1. Website shows the latest stable Windows x64 installer, version, release date, size, requirements, release notes and publisher. Include installation help and release history. Download buttons use the promoted release metadata rather than manually maintained version strings.
2. Sora's Updates page gains a prominent **Sora application** card with installed/latest versions, last successful check, release notes, **Check now**, **Download update**, and **Install and restart**. Hardware driver/BIOS actions retain separate labels and behavior.
3. Check shortly after launch without delaying hardware startup, then approximately daily while running, with jitter, timeouts, cancellation and backoff. Checks send app/channel/platform versions only; no hardware inventory is needed. Offer an automatic-check setting and manual checking.
4. Show one unobtrusive update notification per version. Offer Remind me later and Skip this version for ordinary releases. Critical releases may be marked prominently; installation still requires the user's action.
5. Downloads display bytes, progress, cancel/retry and verification state. Initially downloading is user initiated; optional background downloading can follow later.
6. Before installation, resolve unsaved edits and explain the brief pause in software hardware controls. If a safe pause is unavailable, postpone the update. Never force-kill Sora to continue.
7. Restart into the same user's session, preserve the prior window/tray preference, and confirm the new version. Do not turn on previously disabled startup or hardware auto-apply options.
8. Offline/failed checks show **Could not check** with the last successful check, never a misleading **Up to date**. Sora remains usable.

## Release trust and feed contract

Use a signed metadata envelope containing the exact payload bytes, signature, algorithm and key ID. For example, base64-encoded UTF-8 JSON signed with ECDSA P-256/SHA-256 through .NET cryptography. Specify the signature format and byte encoding once; verify the original bytes before parsing policy fields. Bundle trusted public keys with Sora; reject unknown keys/algorithms. Bound envelope, decoded-payload and release-note sizes.

Signed payload fields should include schema version, product ID, monotonically increasing feed sequence, channel, version, publication/expiry times, platform/architecture, minimum OS/updater version, release-note text or URL, immutable installer URL, byte length and SHA-256. Later add rollout percentage and an update-withdrawal mechanism. Validate required fields and supported schema, channel, product and platform. Use proper version comparisons, never string ordering.

Verify HTTPS origin and redirects against an explicit release-host allowlist. Verify metadata signature, installer size/hash and an Authenticode signature from the approved Sora publisher before execution. A matching hash alone cannot authenticate a release if an attacker can replace both artifact and feed. Define certificate renewal and public-key rotation with overlapping trust before launch.

Persist the highest accepted feed sequence and reject older/expired feeds. Explain that first contact and machines with incorrect clocks have weaker freshness guarantees; fail with a clear check error rather than executing unverifiable files. Withdrawing a release uses a newer signed feed sequence, not replaying an older feed. Installed-version comparisons continue to block automatic downgrades.

Public unattended update installation requires a signing identity. If signing is not yet available, website downloads/manual installation can be an interim milestone, but label that status accurately and do not enable the production automatic-install path.

## Installation handoff and recovery

Keep the runner small: accept a validated release descriptor and supported operation, not arbitrary commands or executable paths from remote JSON. Install it with the signed Sora package in Program Files. Run the update operation with the rights required for the existing machine-wide installation; handle denied elevation without losing the running app where possible.

Stage executable content and the runner's working copy in an administrator-owned directory with restrictive ACLs outside the files being replaced. Reject links/reparse points and unsafe paths. The privileged runner must independently verify metadata, publisher, artifact bytes and target install identity immediately before execution. Do not execute a file from an ordinary writable Downloads/temp location after an earlier verification; prevent replacement between validation and launch.

Handoff sequence:

1. Verify and stage the installer while Sora continues to run. Preflight disk space, install identity, prerequisites and permission requirements. Acquire a machine-wide update lock.
2. Prepare the trusted runner and establish an authenticated, ACL-restricted local handshake tied to this update and process. Block competing launches/sign-in starts during the transaction through a shared machine-wide coordination mechanism. Existing session-local mutex/WMI checks alone do not close this race.
3. Save user edits, stop hardware activity through the existing lifecycle, and report cleanup success explicitly. Surface failures and abort installation. Do not infer success from a PID disappearing. Refactor lifecycle results before connecting this to installation.
4. At a quiescent point, make a versioned backup of settings that may migrate, without replacing original hardware recovery records. Pass a successful handoff to the runner, then exit. Runner waits for the expected process and handles to close with a bounded timeout.
5. Invoke the qualified Inno installer with progress/logging, no forced application close, and no automatic Windows reboot. Keep the installer's existing running-app safeguards. Interpret documented exit codes and reboot-required status.
6. Verify the installed package/version before relaunch. Coordinate startup with the update lock so exactly one host starts. Preserve the initiating user's data directory; elevation under another account must not redirect profiles or relaunch into the wrong session.
7. New Sora reports a bounded application-health acknowledgement. A successful process start does not establish fan/lighting/hardware qualification. If restart fails, retain logs, stop automatic retries and give a repair path.

The existing installer is an in-place upgrade, so this proposal does not promise atomic replacement or automatic rollback after power loss. Qualify interrupted-install repair using the retained installer. Its current downgrade guard also means restoring an older installer is not an automatic recovery mechanism. Any future rollback must explicitly support installer downgrade, compatible settings restoration and recovery when the app cannot start. A side-by-side version layout and stable launcher are a separate possible follow-up if crash-safe automatic rollback becomes a requirement.

## Release process

1. Tag or explicitly select a release commit; derive binary, installer and feed versions from one source. Build on Windows, run existing checks and updater-specific checks, and preserve third-party notices.
2. Build using `scripts/Build-Installer.ps1`, extended to package/sign the runner and Sora executables and sign the final installer. Verify all final signatures; compute artifact hashes only after signing.
3. Test clean installation and upgrade on an isolated Windows machine/VM. Perform bounded hardware lifecycle qualification separately on supported hardware before promotion.
4. Upload versioned installer/notes/metadata with multipart support. Never overwrite an existing version with different bytes. Download via the public URL and independently verify bytes/signatures.
5. Publish a signed beta/test feed; test an actual installed client. Only then update the stable feed as the final promotion step. Serialize promotions to prevent concurrent release races.
6. Website and desktop now see the same promoted stable version. A git push alone updates neither existing desktop installations nor the stable feed.
7. Monitor availability and failures. Keep diagnostic logs local and provide export; any client telemetry is a separate opt-in feature. Withdraw bad releases via a new signed feed and ship a higher-version fix for machines already updated.

## Implementation order and acceptance gates

| Phase | Work | Completion evidence |
|---|---|---|
| 1: Release foundation | Fix feed schema/trust contract; establish signing identity; extend versioned packaging/publishing; provision Portways website and bucket | Signed example release downloaded and verified publicly; preview cannot publish stable; credentials absent from client assets |
| 2: Website and discovery | Download page/history; signed-feed reader; Sora application update card, check scheduling and notifications | Site/client agree on promoted version; bad/expired feed rejected; offline checks leave Sora usable |
| 3: Verified downloads | Streaming download, staging, cancellation, resume and verification | Corrupt/truncated/wrong-publisher files rejected; disk-full and network interruptions recover predictably |
| 4: Install and restart | Cleanup result, trusted runner, machine lock, installer integration and health acknowledgement | Installed old-to-new update preserves data/startup routing, cleans up hardware, installs once and restarts once |
| 5: Release qualification | Failure matrix, operational instructions, initial stable promotion | VM fault tests and bounded live hardware qualification pass; retained artifacts support repair; release/withdrawal procedure rehearsed |
| Later | Beta opt-in, automatic downloading, staged rollout, optional delta packages and transactional rollback evaluation | Each feature gets separate compatibility/failure qualification |

Essential tests: version/channel/platform rules; unsupported schema; metadata tampering/replay/expiry; signer renewal; malicious redirect; staged-file replacement/reparse attempts; resume integrity; low disk space; denied elevation; cleanup failure; helper failure; duplicate launches across sessions; scheduled startup during update; installer failure/reboot-required; power interruption and repair; failed relaunch; profile/schema compatibility; preserved shortcuts/startup preference; runtime missing on a clean machine. Use fake transports/hardware for automated tests; actual fan/display/mouse cleanup needs bounded live qualification.

Existing 0.11.0 clients cannot discover this feature until installed once with an updater-capable build. Publish that bootstrap installer on the site and existing release channel. Later releases can use in-app updates. Actual domain, Portways project/storage access, Windows release CI access and publisher signing credentials are setup inputs, not reasons to block local implementation of the contract/client/helper.

## References

- User-supplied `C:\Users\gaura\Downloads\portways-hosting-guide (3).md`: hosting contract, static sites, monorepo roots, public bucket prefix/CDN, multipart limits, bandwidth and backup behavior. Treat these as supplied platform constraints; confirm configured limits during deployment.
- [Inno Setup command-line parameters](https://jrsoftware.org/ishelp/topic_setupcmdline.htm): logging, restart and application-closing controls.
- [Inno Setup application closing](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm): automatic close behavior to avoid in Sora's update flow.
- [Velopack WPF integration](https://docs.velopack.io/getting-started/wpf) and [installer modes](https://docs.velopack.io/packaging/installer): alternative framework and migration considerations.
