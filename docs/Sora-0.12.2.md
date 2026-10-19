# Sora 0.12.2

- Uses a versioned purple icon for Windows shortcuts and Installed apps, and asks Windows to refresh shell icons after setup.
- Adds a clearer setup welcome page, optional desktop shortcut and support links.
- Uninstall can remove saved profiles, settings, logs, hardware recovery backups and update cache across Windows user profiles. It removes only shortcuts and startup tasks owned by this installation. It refuses to traverse links in data folders and stops before removing installed files if cleanup fails.
- Keeps shared .NET and PawnIO installations. Hardware firmware, third-party software and files manually exported outside Sora's directories are outside the uninstaller's ownership.
- Website wording, mobile layout, download error handling and release details are improved. No em dashes are used in the updated website copy.

Chrome can flag a new executable as suspicious. This release is not Authenticode signed, and release metadata signatures are separate from Chrome's download reputation. Do not disable Safe Browsing to install it. A warning should be investigated using Chrome's details and local file scanning.

Candidate verification and installed-machine uninstall qualification are documented separately. Uninstall removes saved recovery records only after Sora has exited; it does not reset hardware firmware to factory defaults.
