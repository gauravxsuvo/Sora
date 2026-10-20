# Sora downloads on Portways

One Node service serves the static public website and read-only release downloads. This is one application with one entry point; no independently built frontend/API pair is required. Windows builds and release signing happen on Windows, outside Portways.

## Create the project

1. Create the Portways project from this repo with root directory `web/sora-downloads`, production branch of your choice, subdomain `sora`. The expected address is `https://sora.portways.app`.
2. Link an Object Storage (S3) add-on. The app uses injected `S3_INTERNAL_ENDPOINT` when available, otherwise `S3_ENDPOINT`; the SDK receives the injected bucket, region and scoped credentials. No storage keys reach the browser.
3. Portways detects `package.json`, installs dependencies and runs `npm start`. The server binds `0.0.0.0` on `process.env.PORT`. Health check: `/healthz`. The project has no database and persists no state in its container.
4. Upload releases using a separately issued bucket access key from the storage add-on's Access keys panel. The runtime only exposes GET/HEAD. Do not put release-signing private keys in Portways environment variables.
5. Keep `/channels/*` out of Cloudflare cache/challenges; ensure the desktop can GET it without interactive login. Release files return immutable caching headers. Test actual cache eligibility for EXEs and large files before assuming the edge absorbs download traffic.

Storage keys are `public/releases/VERSION/win-x64/Sora-Setup-VERSION-x64.exe` and `public/channels/stable.json`. The Node service streams these using authenticated server-side S3 requests, so a separate public bucket/CDN domain is optional. When using the internal endpoint, the add-on's public speed limiter is bypassed; size the website project's capacity and host bandwidth for installer traffic. Do not route uploads through the website. The release uploader uses 8 MiB multipart parts and two concurrent parts, below the guide's default 25 MB upload-request limit.

The site shows an honest unavailable state and links existing GitHub releases until a valid signed feed is available. It never presents a placeholder installer as downloadable. Static presentation and `/healthz` work without S3 credentials.

## Local checks

```powershell
cd web/sora-downloads
npm ci --ignore-scripts
npm test
npm start
```

The server defaults to port 3000 locally. `npm test` covers public routes, HTTP Range/HEAD, cache headers, errors, safe storage prefixes and browser signature verification. Test files use in-memory storage; they need no real credentials. The public key policy must match `release/update-policy.json`; run `scripts/Sync-UpdatePolicy.ps1` from the repo after rotation.

## Releases

The service also exposes `/channels/beta.json` and immutable `-beta.N` installer paths from the same storage. Beta publishing requires the explicit final `beta` argument to `publish-release.mjs`; omitting it retains Stable. Both channels verify signed metadata before upload or promotion and maintain independent monotonic feed sequences. See the operating guide for signing commands. The website's stable download continues to verify only Stable metadata.

### Beta download button

The separate **Download beta** button discovers published GitHub prereleases in `gauravxsuvo/Sora`. Publish a prerelease with a tag such as `v0.13.0-beta.1` and attach `Sora-Setup-0.13.0-beta.1-x64.exe` (installer) or `Sora-0.13.0-beta.1-win-x64.zip` (portable). The optional `v` prefix and the `alpha`, `beta`, `rc`, and `legion` prerelease labels are supported. The newest published matching release wins; installers are preferred when both formats exist. Draft releases and releases without a matching uploaded Windows asset never enable the button.

The server checks GitHub's public releases API, caches successful checks for five minutes, and retries failed checks after one minute. No GitHub token or new environment configuration is needed. If GitHub is unreachable or rate limited, the button stays disabled and offers retry plus a GitHub releases link. This download path does not modify the signed stable feed or desktop updater. Deploy the website changes to Portways for the button to appear on the live site.

To build the beta installer on Windows, run `./scripts/Build-Beta.ps1 -OutputDirectory artifacts/beta -Compiler tools/inno-setup-6.7.3/ISCC.exe` from the repository root with an official signed Inno Setup compiler. Publish the resulting setup EXE and `SHA256SUMS.txt` as GitHub prerelease assets after qualification. Beta setup upgrades the existing Program Files installation and preserves settings; it does not promote the stable update feed.

See [the operating guide](../../docs/sora-updater-operations.md). With publisher access-key values loaded into this shell's environment (never command arguments or tracked files):

```powershell
node publish-release.mjs upload ../../artifacts/RELEASE/Sora-Setup-0.12.5-x64.exe ../../artifacts/METADATA/stable.json
# Only after qualification, with the same exact artifacts:
node publish-release.mjs promote ../../artifacts/RELEASE/Sora-Setup-0.12.5-x64.exe ../../artifacts/METADATA/stable.json
```

`upload` never changes the stable feed. Both commands verify the signed metadata, local artifact and public download bytes. `promote` additionally requires an increasing sequence and performs a conditional feed write against the old ETag. Concurrent promotions fail rather than overwriting each other. Existing immutable artifact keys are verified, never replaced. A failed post-write public verification means promotion may already have happened: inspect the live feed/cache before retrying.

Monitor bucket capacity (5 GB default in the supplied guide), actual host bandwidth, feed freshness and request errors. Keep offsite release backups; Portways's object-storage backups described in the supplied guide remain on the host machine.
