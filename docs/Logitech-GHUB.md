# Logitech compatibility

Sora detects wired G502 HERO and supported G102 / G203 USB variants. USB identities for the G102 / G203 family are documented by [libratbag](https://github.com/libratbag/libratbag/blob/master/data/devices/logitech-g102-g203.device). The device's USB product string supplies its displayed model name when available.

## G502 HERO

Supported controls include DPI stages, report rate, hold-to-shift, button profiles, G-Shift assignments, chord macros, firmware lighting effects, screen sampling, speaker-level lighting, and selected device preferences. G1 remains native primary click. Exit G HUB before applying settings.

Stop Sora controls restores the saved original control state. Onboard profiles can be inspected; profile editing and firmware flashing are unavailable. Application-specific automatic profiles, vendor action integrations, Lua, and cloud features are unavailable.

## G102 / G203

Sora detects supported wired variants and reads their reported DPI, supported sensitivity range, polling rate, and protocol capabilities. G502-only preferences, assignments, memory inspection, and lighting editors are hidden for these models. Advanced G102 / G203 control paths require separate qualification.

## Multiple mice

The device picker distinguishes connected devices. Read operations use the selected configuration interface. G502 runtime and lighting services only open G502 devices. Ambiguous G502 control sessions fail rather than applying to an arbitrary mouse.

Profiles and backups must match their device. Refresh after connecting, removing, or changing the selected mouse.
