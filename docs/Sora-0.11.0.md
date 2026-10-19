# Sora 0.11.0 — completed desktop UI refinement

All five UI/UX phases are approved. This release preserves the current hardware tools and applies the shared desktop design system across the production application.

## Highlights

- Refined native Windows window branding, compact grouped sidebar, consistent monochrome Lucide icons and bundled Inter typography.
- Shared buttons, dropdowns, switches, sliders, text/colour inputs, tabs, dialogs, notices and design tokens across Cooler display, Keyboard, Lighting, Fan control, GPU fans, Logitech, SSD, Wi-Fi and Settings/Updates.
- Clear sensor/device-unavailable states and offline editing feedback, with existing hardware operations, profiles and staged Apply/Discard behaviour preserved.
- Fixed Logitech illustrations and predictable editor scrolling above persistent action bars. LIGHTSYNC opens at the top; sync controls, the full-size colour wheel and complete RGB fields fit at 1304 × 852 and 1064 × 852.
- Restrained transitions respecting reduced motion, visible keyboard focus, safe modal defaults/focus restoration and improved UI Automation colour/status feedback.

## Verification

The approved Phase 5 pass included 99 accessibility/layout reports, 16 motion checks, 55 shared-component checks, 31 Logitech checks, 55 offline Lighting checks and connected keyboard editor checks. The final LIGHTSYNC correction passed 26 RGB/positioning checks and all 31 Logitech editor checks for each initial/focused capture at both reference sizes. Actual running Windows application screenshots were reviewed.

Tray and display-startup preferences remained true across navigation and independent launches. Windows sign-in startup remained enabled; the stored preferences file hash and task registration were unchanged. Release packaging excludes user preferences, profiles, logs and recovery records and does not modify the installed application or startup registration.

## Windows download

Use **Sora-Setup-0.11.0-x64.exe** from the GitHub release. Requires Windows x64 and the **.NET 8 Windows Desktop Runtime (x64)**. The application and installer are unsigned. SHA256SUMS.txt accompanies the installer.

Exit Sora through its tray menu before installing an update. The installer preserves `%LOCALAPPDATA%\Sora\data`, does not launch hardware controls automatically, and does not enable or rewrite Windows sign-in startup.

## Scope and remaining limitations

The component preview remains development-only. This release does not claim new device support, firmware qualification or universal hardware compatibility. No hardware Apply/Restore/firmware actions were used to demonstrate UI states. Unavailable Lighting controller scenarios use isolated fixtures; full screen-reader, high-contrast, DPI and physical lifecycle qualification remain separate work. Existing hardware ownership and qualification limits remain documented in the hardware guides.

The design is complete. Further changes should address specific bugs, accessibility issues or demonstrated usability problems without a broad redesign.
