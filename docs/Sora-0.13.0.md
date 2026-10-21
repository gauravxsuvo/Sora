# Sora 0.13.0

This release updates Sora's desktop design and fan editing, fixes saved settings, and adds automatic handoff from supported fan helper services.

- AMOLED black dark mode with a saved appearance preference, neutral charcoal surfaces, clearer borders, consistent line icons, and the updated Sora logo. Purple accents, gradients, and the selected navigation rail are removed.
- Apple-style red, yellow, and green window controls with hover glyphs and keyboard focus. Native Windows dragging, resizing, caption double-click, and existing tray behavior are retained.
- Click empty motherboard fan-chart space to add a point, then drag or use arrow keys to adjust it. Delete removes the selected point. The table and chart share the same draft; fan-header switching retains edits. Curves keep unique temperatures, nondecreasing duties, at most eleven points, and a final full-speed point. Click Apply to save and send the curve.
- “Give Sora priority over fan helper services” is enabled by default. Applying a curve, including saved startup/resume profiles, can pause the supported DeepCool FanControl and Gigabyte EasyTuneEngineService helpers. Sora preserves and verifies all six headers and firmware settings across the handoff, checks for helper restarts while an applied profile is active, and resumes only previously running helpers on exit. Service startup types are unchanged. Standalone firmware fan support is required; its setup button is on the Fan control page. Other controller applications still require manual closure.
- Improved recovery and persistence for mouse lighting, saved fan/GPU profiles, device labels and preferences, including failed saves and malformed settings files.

Automated validation covers application logic, fan-service handoff and failure recovery with simulated services, saved preferences, chart interactions, window commands, and screenshot/layout checks. Live takeover of running vendor fan services has not been qualified on this PC. Hardware support remains restricted to the documented devices and interfaces.

Exit the older Sora instance through its tray menu, then run `Sora-Setup-0.13.0-x64.exe`. Normal upgrades retain saved preferences and profiles. Enable Settings → Start at sign-in to launch Sora automatically with Windows. Earlier prerelease users can install this stable version directly. Stable builds default to the Stable update channel.

Requires Windows x64 and the .NET 8 Windows Desktop Runtime (x64). The installer is not Windows Authenticode signed. Sora release-feed signatures and SHA-256 checksums are separate from Windows publisher signing. This release does not update device firmware or install hardware drivers.
