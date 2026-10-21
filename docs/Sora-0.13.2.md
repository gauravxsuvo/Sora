# Sora 0.13.2

Fixed Logitech fixed-color brightness returning to 100% in the editor after reopening or changing lighting zones. Sora now saves the chosen color and brightness separately from the mouse's dimmed RGB bytes, preventing repeated brightness changes from dimming the color twice.

- Retained color, brightness and effect timing across apply, reopen, zone switching and Discard. Linked zones retain matching choices, including brightness changes that produce identical black device output.
- Kept older saved lighting files compatible. Invalid editor metadata falls back to valid device state without discarding the saved lighting.
- Fixed Load original retaining a staged reactive lighting effect.
- Centered and strengthened all traffic-light glyphs, including maximize and restore.
- Added a transparent Swarm 75 product view and a Key map switch. Assignments automatically reveal the interactive map. Other keyboard models keep their own layouts. Updated keycaps and the keyboard chassis styling.

For older fixed-color saves, the original color/brightness pair cannot be reconstructed from the dimmed RGB value alone. Choose your original color and brightness once and click Apply lighting; future launches preserve both. This does not increase the mouse's existing brightness automatically. Reactive effects and software mappings still need Sora running.

Exit Sora through its tray menu before installing. Normal upgrades preserve saved profiles and preferences.
