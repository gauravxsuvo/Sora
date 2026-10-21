# Sora 0.13.1

Applied fan curves and lighting now retain their saved configuration across closing and reopening Sora. This release also updates the tray menu, window buttons, logo and website presentation.

- Fixed the fan editor replacing a saved curve with a different live controller curve. Saved temperatures, duty values, mode and the selected header remain available while hardware is reconnecting or settings are being reapplied.
- Migrated older fan defaults once: saved curves reapply at startup and remain in hardware after exit. Original option files are backed up before migration. Explicit changes made afterward are preserved.
- Competing fan helpers stop automatically. Sora waits for the stopped process to exit before recovering its baseline, retries interrupted startup applies, and keeps helpers stopped while retaining its curve. Opting into restoration on exit returns the original fan configuration and service ownership. Other active controller applications still require closure.
- Migrated older RGB settings away from returning to rainbow on exit, preserving the chosen look and startup reapplication. Saved mouse lighting is loaded into the editor even when the device has temporarily reverted. Mouse DPI profiles, the last selected DPI stage, colors and brightness retain their saved values. GPU software curves still require Sora to run and safely release manual control on exit; their profiles remain saved for reopening.
- Replaced the tray's default Windows menu with a themed, keyboard-accessible menu, consistent icons, clearer spacing and a Settings shortcut.
- Removed the extra circle around focused traffic-light window buttons. Action glyphs remain visible for keyboard focus.
- Removed the dark backing tile from the app/tray icon and website logo. The website download button is black.
- Presented Sora as a general Windows PC controller on the website and GitHub, without publishing the developer's PC specifications. Available controls remain device-dependent.

Normal upgrades keep saved profiles and preferences. Exit the running Sora instance through the tray menu before installing. To retain fan curves and lighting, leave their startup reapplication enabled and the optional reset-on-exit settings disabled.

Requires Windows x64 and the .NET 8 Windows Desktop Runtime (x64). Hardware support depends on detected devices and qualified interfaces. Animated mouse lighting, software button mappings and GPU software curves need Sora running. Configuration persistence does not imply that unsupported devices gain controls or that software effects can run without their controller. No device firmware is flashed and no hardware drivers are installed.
