# Sora 0.12.5

- Adds a read-only hardware summary during setup.
- Shows device pages according to connected hardware, including navigation search and keyboard shortcuts.
- Hides LD360 settings and tray actions when the cooler is absent, and avoids unnecessary cooler startup activity.
- Detects wired G102 / G203 USB variants and reads their actual model names, DPI ranges, polling rates, and capabilities.
- Keeps G502 assignments, lighting services, and device preferences separate from other mouse models. Advanced G102 / G203 controls remain unavailable.
- Preserves shared application assemblies so newly connected devices can be recognized without installing a different executable.
- Makes build output independent of saved user profiles and local hardware data. Building no longer activates a development build or changes shortcuts.
- Joins in-flight hardware polling before shutdown and ignores stale reads after switching mouse devices.

Setup does not install hardware drivers, flash firmware, or change device settings. Normal upgrades preserve saved preferences and profiles.
