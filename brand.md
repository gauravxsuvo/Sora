# Sora UI Brand and Design System

This document is the shared visual reference for Sora contributors. Use it alongside the existing WPF resources in `src/Sora.App/Theme.xaml`; the resource dictionary is the source of truth for application values.

## Visual direction

Sora uses a calm, compact desktop interface inspired by macOS System Settings. Keep the surfaces neutral, typography clear, and controls aligned to a consistent grid. Prefer useful structure and spacing over decorative effects. Avoid gradients, heavy shadows, excessive blur, and extra cards that do not clarify the content.

The supplied MangoDisk screenshot is a **sidebar layout reference only**: its compact rows, clear section groups, and pinned utility area are useful patterns. Do not copy its dark background, orange selection, logo, labels, or icon artwork. Sora keeps its existing light neutral palette, blue accent, wordmark, and device-specific navigation.

## Palette

The Sora logo is the circular flowing-cut mark reconstructed from the user's reference. Its master is `assets/brand/sora-mark-source.svg`; the transparent gaps are part of the silhouette. The default logo gradient runs from violet `#6030F4` to lilac `#9657FF`, with white, ink and `currentColor` variants. Gradients are reserved for the brand mark; existing interface accent colours retain their established roles. Regenerate the SVGs, transparent PNGs, WPF geometry, website icons and multi-size Windows ICO together with `scripts/Build-Brand.ps1`. Do not use the historical cloud or spiral artwork for new UI or exports.

Use the centralized brushes in `Theme.xaml`. Do not add one-off hex colours to controls when an existing token fits.

| Use | Token | Current value |
| --- | --- | --- |
| Main window | `WindowBrush` | `#F5F5F7` |
| Sidebar | `SidebarBrush` | `#ECECEF` |
| Main cards | `SurfaceBrush` | `#FFFFFF` |
| Raised surface | `SurfaceRaisedBrush` | `#F8F8FA` |
| Inset/control background | `InsetBrush` | `#F2F2F4` |
| Primary text | `TextBrush` | `#1D1D1F` |
| Secondary text | `SecondaryTextBrush` | `#626262` |
| Tertiary text and group labels | `TertiaryTextBrush` | `#6B6B6B` |
| Primary accent and selected icon | `AccentBrush` | `#0069D9` |
| Selected navigation surface | `NavSelectedBrush` | `#DDEAF9` |
| Separators | `SeparatorBrush` | `#26000000` |
| Subtle borders | `HairlineBrush` | `#18000000` |

Use status colours only to communicate meaningful good, warning, error, or idle states. Sidebar icons are neutral by default; reserve blue for the selected page and clear interaction states.

## Typography

- Use bundled Inter 4.1 (`UIFont`) throughout the desktop shell, page content and chart labels. Static Light, Regular, Medium, SemiBold and Bold faces are embedded as WPF resources; never rely on the font being installed on the user's computer. The native Windows caption follows the operating system font.
- Page titles use `TitleText` at 26 px and semibold weight.
- Section labels use `SectionText` or `Eyebrow`; keep them compact and clearly subordinate to page titles.
- Setting names use `SettingNameText` at 14 px and semibold weight.
- Body and explanatory text use `BodyText` or `SecondaryText`; keep secondary copy readable and use the shared line heights.
- Field labels use dark neutral `TextBrush` and Medium weight. Supporting copy uses neutral grey; blue identifies interactions and selection rather than ordinary descriptions.
- Small status and metadata labels use `CaptionText`, with enough contrast to remain legible on neutral surfaces.
- Numeric readings use `NumericText` for tabular numerals. Keep a value and its unit in one inline TextBlock, separated by a thin space and aligned on the text baseline.

## Window and page layout

- Embed `assets/Sora.ico` in the executable and as a WPF resource. Set the native window `Icon` explicitly for consistent title bar and taskbar branding.
- Keep the standard Windows title bar, minimize/maximize/restore/close controls, resize frame, and operating-system window behavior. The main window is resizable and declares minimum dimensions in `MainWindow.xaml`.
- Use the shared page insets and card tokens. Let groups size to their content when stretching would create unused space inside a card.
- Use `SettingsGrid` for settings sections: a flexible description column, a 16 px gap, and an automatic trailing control column at the card's right padding. Descriptions wrap within their column. Keep control dimensions and row spacing consistent within a group.
- Scroll long page content vertically. Do not clip controls or create horizontal scrolling at the supported minimum window size.
- Use thin neutral dividers, restrained corner radii, and subtle hover states. Shadows belong only on transient overlays where needed.

## Sidebar

- Keep the sidebar fixed-width and visually distinct with the light `SidebarBrush`.
- Keep its width at 244 px. Consume `SidebarInset`, `NavigationItemPadding`, `NavigationItemSpacing`, `SidebarSectionSpacing`, and `SidebarFooterSpacing` rather than adding per-row offsets.
- Branding uses Inter 16 px SemiBold. Navigation uses Inter 14 px Medium, with SemiBold only on the selected row. Categories use 11 px Medium with a 16 px line height; search uses 13 px. Preserve the embedded font resources and verify that Medium and SemiBold resolve to their actual Inter font files.
- Preserve the Sora mark, search field, navigation actions, and pinned Settings entry.
- Organize device navigation under short, scannable section headings. The current groups are Cooling, Peripherals, and System.
- Use 36 px single-line navigation rows with 2 px between rows. Show only the icon and page name. Connection, availability, checking and error messages belong inside the relevant page, using its existing status chip, connection panel or status text. Do not add status subtitles or status dots to sidebar rows.
- Use one Lucide outline family through MahApps IconPacks. Put each icon in the shared 20 × 20 px box and use the shared 18 × 18 px icon style. Keep the visual weight neutral and optical sizing consistent.
- Use a 10 px gap after the icon box. At 100% scale, icon boxes start at x=22 and navigation labels at x=52, including Settings. Branding shares these centres and text alignment. Selection and focus must not change these positions.
- Show hover with a subtle neutral overlay. Show selection with the muted blue surface and narrow blue leading marker. Keep all navigation labels aligned to the same starting edge.
- Show keyboard focus with the shared navigation template's blue outline, independently of selection. Hover applies to inactive rows; it must not obscure selection. Draw states as overlays without layout shifts.
- Keep the sidebar scrollable when its entries exceed the available height; keep Settings and version information in the footer area.

## Keyboard editor states

- Keep connection guidance in the page notice and the Connect/Refresh action in the header. A disconnected layout preview is neutral, not an error. Hide the pending-change footer until a device snapshot exists.
- Tabs and key selection can be explored without hardware. Effects, assignments and profiles need a complete read snapshot. Keep editing and profile saving available from a retained USB snapshot offline; Apply always requires a connected device.
- Respect receiver limitations: per-key painting and complete profile operations require USB. Never infer unread per-key colours or add device support through UI changes.
- Use explicit disabled states and dropdown placeholders when no settings have been read. Keep the colour-mode value visible and label what it controls.
- Separate selected-key painting from whole-keyboard fill. Validate hex input before painting, show the selected key and stored colour, and retain a clear blue selection ring.
- The selected key's staged RGB bytes are the source of truth for its hex field, current/stored colour indicator, swatch and palette checkmark. Selecting another key loads that key's colour. A valid hex or palette edit stages the selected key immediately; only Apply writes hardware. With no key selected, the colour field is a brush for whole-keyboard fill. Keep invalid input from modifying staged RGB bytes.
- Hide brightness/speed sliders when no snapshot exists or the selected effect does not use them. Show “Not read yet” or “Not used by this effect” in the same reserved space. A retained snapshot keeps supported controls editable offline.
- Pending changes count state differences from the read baseline, not clicks: one per changed model colour slot, one per changed assignment, one lighting-settings group, and one palette group. Show the breakdown. Swarm 75 has 82 visible keys plus an auxiliary Mute colour slot in the vendor definition; preserve whole-keyboard fill semantics and disclose auxiliary slots separately.
- Use flat dark keycaps with one subtle lower edge, consistent spacing and readable legends. Keep actual RGB previews visible without decorative glows or deep bevels.
- Say “Selected key: A” rather than “A selected”. Selection uses a blue outline with a light outer separator, so it remains distinct from a blue or orange RGB fill. Keep the selected key's colour untouched.
- Hidden editor tabs must use normal body weight; selected-tab styling must not make all page content semibold.

## Reusable implementation rules

- The Phase 3 component inventory, APIs, token values and verification workflow are documented in `docs/component-library.md`. Extend this library before adding page-specific templates. Preserve standard WPF interaction and automation behaviour.
- Use `ControlState.IsBusy` for shared loading feedback while the operation owner controls `IsEnabled`. Pair invalid inputs with `ValidationMessage` and a useful accessible explanation. Use `Notice` for persistent inline messages and `AppDialog` for owned application confirmations; keep Windows file pickers native.
- Use `ControlState.SetValidation` to keep an input's invalid border, visible explanation and accessible help synchronized. Hex colour fields share `ControlState.HexColourError` in the gallery and Keyboard editor.
- Non-neutral notices use a restrained Lucide icon, a visible status label and dark message text. Success, information, warning, error and loading must remain understandable without colour. Neutral notices stay compact; do not add status icons to ordinary connection guidance solely for decoration.
- Use `CompactCard`, `SectionContentInset`, `SectionHeadingInset`, `SectionSpacing`, and `ControlLabelInset` for repeated control sections. Popup and tooltip shadows use the shared effect tokens; cards remain flat.
- Respect the live system animation preference through `MotionPreferences`. `ControlState.ReduceMotion` can further restrict a view; it never overrides the system's request for less motion.
- Components is an opt-in development preview, with no permanent navigation item and no selected production row. Navigation must remove the gallery and select the matching production page. In the isolated preview process, those pages are read-only with a clear explanation. Normal Sora pages retain their existing functionality.
- Give the development gallery initial focus before window activation; never let startup focus select Cooler. Tables use neutral outer frames and blue outlines for genuinely focused cells. Production adoption is documented in `docs/component-adoption.md`.
- Distinguish unavailable telemetry from healthy data with a concise empty state. Never let an empty table or large Unknown metric dominate a page. Keep retry actions available and disable data-specific actions until readings exist.
- Label offline lighting edits as local preferences or unsent changes. A saved setting is not proof of device availability or successful hardware delivery.
- Keep Logitech illustrations fully contained in a fixed, non-scrolling pane while the adjoining long form scrolls. Use the shared bottom content inset to clear persistent footers. Short artwork identifiers may link to full, wrapping assignment labels; never truncate the only accessible name of an assignment.

### Motion and keyboard interaction

- Use the shared 90/120/140ms feedback, selection and reveal tokens with gentle ease-out. Animate opacity or switch-knob transforms; keep layout, input and hardware feedback immediate.
- Respect live Windows animation preferences and inherited reduced motion, including cancelling in-flight transitions. Avoid motion on live telemetry or continuous colour edits.
- Preserve visible keyboard focus on navigation, tabs, inputs, palettes and actions. Modal dialogs use safe defaults, cycle keyboard focus inside their content, support Escape and restore the invoking control.
- Pair status colour with readable labels/icons. Announce meaningful changes politely, suppress repeated identical notices, and preserve explanations until the user can read them.
- Development gallery sessions use a unique `component-preview-data` directory beside their captures. Never migrate or write real application data from preview controls. Examples operate on in-memory sample state, and every enabled action must have a real local behaviour.

- Add or adjust shared visual values in `Theme.xaml`, then consume them through `StaticResource` styles and tokens.
- Reuse existing styles such as `Card`, `NavItem`, `NavigationIconBox`, `NavIcon`, `TitleText`, `SecondaryText`, `CaptionText`, and `SettingNameText` before creating another variant.
- Preserve control names, event handlers, keyboard shortcuts, accessibility names, and device functionality when changing layout.
- Review both the Cooler display and Settings pages at a 1304 × 821 client size after shell changes. Check resized behavior at the declared minimum window size as well.
- Treat screenshots as layout evidence. Fix the shared layout or component that causes a mismatch instead of compensating with isolated margins.
- `--screenshot` verification can record actual navigation geometry and resolved font files. `--layout-scale 1.25` or `1.5` simulates client layout scaling only; do not present these as tests of Windows monitor DPI changes or native-caption scaling.
