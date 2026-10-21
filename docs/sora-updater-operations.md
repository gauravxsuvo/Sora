# Application updates

Sora supports Stable and Beta channels for application releases. Stable builds default to Stable; prerelease builds default to Beta. The Updates page lets the user select a channel. Signed metadata and file hashes are verified before installation. A newer version requires the user's explicit Install and restart action. Changing channels never downgrades the installed version.

The installer upgrades the application while preserving saved preferences and profiles. It does not perform BIOS, driver, or peripheral firmware updates. Exit through the tray menu when installing manually so hardware ownership can be released.

Release automation builds a folder publish, dependency notices, installer, and signed release metadata. Signing keys belong in the release environment's secret store. Only validated release artifacts should be published. Windows Authenticode signing is separate from Sora's release-feed signatures.

See [installation](program-files-installation.md).

## Stable publication

The Legion/beta release plan is cancelled. Publish stable versions only. The website refuses beta feed promotion and returns HTTP 410 for the retired beta feed and download paths. Existing prerelease users should manually install the latest stable release.

Prepare metadata with `Sora.ReleaseTool prepare INSTALLER VERSION SEQUENCE NOTES POLICY PRIVATE_KEY NEW_OUTPUT_DIRECTORY`. Verify with `verify SIGNED_FEED POLICY INSTALLER`. Use a strictly increasing stable-channel sequence and refresh signatures before their 30-day expiry.

After qualifying the exact installer, run `node web/sora-downloads/publish-release.mjs upload INSTALLER stable.json`, then `node web/sora-downloads/publish-release.mjs promote INSTALLER stable.json` with publisher credentials loaded. Both commands verify signatures and hashes; promotion verifies public bytes and performs a conditional feed write. Preserve immutable installer bytes.
