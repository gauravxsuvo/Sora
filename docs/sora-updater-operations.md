# Application updates

Sora checks its configured stable feed for application releases. Signed metadata and file hashes are verified before installation. A newer version requires the user's explicit Install and restart action.

The installer upgrades the application while preserving saved preferences and profiles. It does not perform BIOS, driver, or peripheral firmware updates. Exit through the tray menu when installing manually so hardware ownership can be released.

Release automation builds a folder publish, dependency notices, installer, and signed release metadata. Signing keys belong in the release environment's secret store. Only validated release artifacts should be published. Windows Authenticode signing is separate from Sora's release-feed signatures.

See [installation](program-files-installation.md).
