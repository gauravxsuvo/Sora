# Application updates

Sora supports Stable and Beta channels for application releases. Stable builds default to Stable; prerelease builds default to Beta. The Updates page lets the user select a channel. Signed metadata and file hashes are verified before installation. A newer version requires the user's explicit Install and restart action. Changing channels never downgrades the installed version.

The installer upgrades the application while preserving saved preferences and profiles. It does not perform BIOS, driver, or peripheral firmware updates. Exit through the tray menu when installing manually so hardware ownership can be released.

Release automation builds a folder publish, dependency notices, installer, and signed release metadata. Signing keys belong in the release environment's secret store. Only validated release artifacts should be published. Windows Authenticode signing is separate from Sora's release-feed signatures.

See [installation](program-files-installation.md).

## Beta channel operations

The beta feed is `https://sora.portways.app/channels/beta.json`. It uses the same pinned public keys and strict immutable installer path as Stable, with versions such as `0.13.0-beta.3`. Stable metadata accepts numeric versions only; beta metadata requires the `-beta.N` suffix. The website's beta download button independently discovers GitHub prerelease assets; publishing to GitHub alone does not promote an in-app update.

Prepare metadata with `Sora.ReleaseTool prepare-beta INSTALLER VERSION SEQUENCE NOTES POLICY PRIVATE_KEY NEW_OUTPUT_DIRECTORY`. Verify with `verify-beta SIGNED_FEED POLICY INSTALLER`. These commands do not print the private key. Use a strictly increasing sequence within the beta channel, independently of Stable, and refresh signatures before their 30-day expiry.

After qualifying the exact EXE, run `node web/sora-downloads/publish-release.mjs upload INSTALLER beta.json beta`, then `node web/sora-downloads/publish-release.mjs promote INSTALLER beta.json beta` with the separately scoped publisher credentials already loaded. Each command verifies signatures and file hashes; promotion verifies public bytes and performs a conditional feed write. Never rebuild a different EXE between qualification and promotion. The release-candidate workflow also accepts an explicit `channel` input; its default remains Stable.

Channel preference is stored separately in `app-update-channel.json`. Stable retains the original `app-updates.json`; Beta uses `app-updates-beta.json`. The beta channel inherits an existing automatic-check opt-out but never copies the stable replay counter, skipped version or last-check time. A corrupt channel/state file fails closed. Previously shipped beta.1 and beta.2 require one manual installer upgrade to gain beta feed support.
