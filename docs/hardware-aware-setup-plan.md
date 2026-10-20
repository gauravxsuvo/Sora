# Hardware-aware setup

Sora uses a shared, read-only Windows hardware inventory for setup and runtime availability.

## Implemented

- Setup shows detected hardware pages before installation and does not launch hardware controls.
- Runtime navigation, search, and keyboard shortcuts hide unavailable hardware pages.
- A missing LD360 has no visible cooler settings or cooler startup activity.
- New device connections are detected automatically; saved profiles remain local.
- G102 / G203 variants are detected by their wired USB identities and displayed using their product strings.
- G502 runtime, lighting, and device preferences are isolated from other mouse models.
- Advanced mouse controls are exposed only for qualified models and features.
- Detection failures remain distinct from absent devices.

The shell and shared assemblies remain common to all installations. Device-specific pages and controller activity are selected locally; hardware changes do not require a different executable.

## Further qualification

- Additional model-specific control protocols and restoration behavior.
- Broader peripheral and adapter variants.
- Connection changes during in-flight editing and multiple identical devices.
- Installation and lifecycle checks on clean Windows machines.

Tests cover empty and mixed hardware inventories, exact board matching, Logitech variants, duplicate HID collections, and capability isolation. Physical hardware checks supplement these tests.
