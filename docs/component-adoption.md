# Phase 4: shared component adoption

The approved shell, typography, sidebar geometry, Cooler layout and Keyboard editor remain the visual baseline. This pass applies the existing library to production controls and removes overrides that hide its interaction states.

| Screen | Shared components and refinements |
| --- | --- |
| Cooler and Settings | Retain existing implicit controls, PageHeader, Card and right-aligned SettingsGrid; no structural redesign. |
| Keyboard | Retain approved controls, colour synchronization, selected-key styling and staged changes. Lighting, Key assignment and Profiles share section tokens; verify all three connected and unavailable states. |
| Fan control | Share list selection/focus and table header/cell templates; preserve per-page list padding, curve editing and header scrolling. Manual refresh uses shared loading feedback without locking the editor during background polls. |
| GPU fans | Remove duplicate table templates that suppressed cell focus. Share validation messages, refresh loading and section insets; retain specialized chart and existing curve logic. |
| Lighting | Use shared hex validation for memory, motherboard and sync, including accessible help and clearing errors on correction. Share refresh loading and section insets; preserve effect availability and automatic-apply rules. |
| SSD | Share table headers/cells, section insets and refresh loading. Preserve drive selection, read-only SMART data and native CSV file picker. |
| Wi-Fi | Share refresh loading and named selectors. Empty selectors show explanatory placeholders and are disabled; unavailable telemetry cannot start monitoring or measurements. Retain antenna view, charts and position workflow. |
| Updates | Continue using implicit buttons, progress, existing operation loading, file dialogs and section tokens. No update download/install action is triggered during visual verification. |
| Logitech | Share hex/RGB validation, refresh loading and saved-colour swatches. Use neutral assignment-label colours independently of markers on dark artwork. Explain empty macro choices and search/capture inputs; disable unavailable editors until a snapshot is read, retaining editing from a saved read snapshot. Multiline macro text starts at the top using the shared input style. Preserve DPI, assignment, macro and staged-apply logic. |

## Rules for later changes

- Use the application-level implicit control styles or derive from them. Keep local overrides only for geometry or device-specific rendering.
- Keep a table's outer frame neutral. Reserve blue for genuine focused cells and selection.
- Pair invalid input with explanatory text and accessible help, and clear all three together.
- Loading feedback follows existing operation ownership. It must not start an operation, change availability rules or enable a second concurrent action.
- Preserve bindings, events, automation peers, native dialogs and hardware support boundaries.
- Check production screens at the reference size and minimum window size. Overflow must remain reachable through the existing scroll containers.
- Run device editor checks against isolated settings. Do not use real saved preferences as a test fixture or apply staged edits to test their appearance.

## Verification artifacts

`artifacts/phase4-production-20261019` contains the final actual native-window captures at 1304 × 852 and 1064 × 712, including Keyboard and Logitech tabs. It also contains gallery interaction/navigation reports, recorded fan editor checks, real USB editor checks, isolated lighting checks and before/after keyboard snapshots. Reports distinguish synthetic fixtures from physical device reads. See that directory's README, screenshot report and verification JSON for results and limitations.

`--screen-ui-check` inspects each rendered production screen's input templates, accessible names, control heights and scroll reach. It restores scroll positions and invokes no device actions. These checks complement visual inspection; they do not constitute a complete screen-reader or Windows DPI certification. `--keyboard-section lighting|assignment|profiles` selects the actual editor tab for screenshot verification without changing an editor value.

Phase 5 has not been started. Changes to motion design, broader interaction behaviour and further accessibility polish require the next approval.

## Final Phase 4 quality pass

- The tray and cooler auto-start switches read the real `settings.json` values in normal runs. Earlier Phase 4 captures deliberately used new isolated test directories; those defaults were not a migration or preference reset. Captures in `artifacts/phase4-final-qa-20261019` labelled `persisted` use the actual preferences. `--settings-preference-check` exercises the real handlers, navigation and a new host/window against a copy, never against the user's original file. Start-at-sign-in also inspects the actual Windows task in screenshot mode rather than forcing an unchecked presentation.
- Logitech Assignments pins the complete illustration and view controls in a non-scrolling left pane. The right form scrolls independently and ends with the full, wrapping selectable assignment legend; the legend and existing selector update each other. The command chooser retains its legitimate list scrolling. The final label and editing controls remain above the footer.
- Logitech LIGHTSYNC pins the complete illustration while the right editor scrolls above the footer. Zone and effect selectors share a row, preserving the full-size wheel in the initial viewport at both 852px-high reference sizes. Shorter windows can scroll the entire wheel into view. Lighting and these Logitech editors use the shared ScrollContentInset bottom clearance.
- SSD scan failures explain permission/retry guidance inside the drive panel. Missing readings use explanatory empty states instead of large Unknown metrics or an empty table. A successful read restores normal metrics and attributes; losing data removes stale readings and disables export.
- Lighting keeps offline editing of local preferences. The page identifies unavailable controllers, distinguishes saved intent from device delivery, rejects unavailable Apply before a hardware operation and disables hardware-only persistence/restore actions. Offline automatic-save feedback never claims that lighting was sent. Discard restores the previous saved look. Existing available-device apply/readback logic is retained.

Final results and screenshots supersede the earlier isolated-profile screenshots for this review. Phase 5 remains pending approval.

The final pinned-layout acceptance captures and read-only checks are in `artifacts/phase4-pinned-layout-20261019`. These include both requested visible sizes, a shorter-height check, complete assignment lists and before/after production preference hashes across navigation and separate launches.
