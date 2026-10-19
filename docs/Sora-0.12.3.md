# Sora 0.12.3

- Uses versioned purple icons for Windows shortcuts and Installed apps, refreshes existing Sora shortcuts and notifies Windows after setup.
- Adds clearer setup instructions, an optional desktop shortcut and support links.
- Fixes reinstall after choosing to keep legacy data. Setup preserves the retained data directory and continues to reject unexpected contents in an unregistered installation folder.
- Uninstall offers complete removal of saved profiles, preferences, logs, recovery backups and downloaded update cache, or keeping saved data for a later reinstall. Complete removal includes registered Windows user profiles.
- Removes startup tasks and shortcuts belonging to this installation. Refuses running Sora processes, links in data folders and startup tasks owned by another application.
- Keeps shared .NET and PawnIO installations, hardware firmware and files exported outside Sora's directories.
- Improves website wording, mobile layout, release details and download help.

Updates require your choice to install and restart. Normal upgrades keep saved profiles and preferences.

The installer remains unsigned for Windows Authenticode. Free Sora update signatures verify release metadata and downloaded bytes; they do not establish Chrome or Windows download reputation. Do not disable browser or antivirus protection to install it.
