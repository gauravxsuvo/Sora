# Sora component library (Phases 3–5)

## Source and usage

`src/Sora.App/Theme.xaml` is the shared WPF resource library, loaded once by `App.xaml`. Keep the approved shell dimensions, Inter font, monochromatic Lucide navigation, neutral surfaces and blue interaction accent. `brand.md` remains the visual contract.

Use standard WPF controls with these resources. Keep existing bindings, events, routed commands and automation peers. Device-specific keyboard caps, charts and artwork retain their specialized rendering. Do not wrap every existing control in a new class.

| Component | Shared API | Behaviour |
| --- | --- | --- |
| Buttons | implicit `Button`, `PrimaryButton`, `SmallButton`, `QuietButton` | Hover/pressed overlays, visible focus, disabled surface; native access keys/default/cancel/Invoke |
| Loading | `ControlState.IsBusy` + existing `IsEnabled` | Adds an indeterminate progress rail without changing the button's dimensions or label; caller disables repeated operations and owns cancellation |
| Dropdowns | implicit `ComboBox` / `ComboBoxItem` | Selected value, keyboard search/navigation, bounded popup, hover/pressed/focus/disabled and optional placeholder/invalid state; existing app uses noneditable dropdowns |
| Text inputs | implicit `TextBox` | Caret/selection, read-only and disabled surfaces, placeholder, focus and validation outline; preserve `PART_ContentHost`, wrapping/scrolling settings and bindings |
| Validation | `ControlState.SetValidation`, `ControlState.IsInvalid`, `ValidationMessage` | Synchronizes the error border, explanation and accessible help; hex inputs share `HexColourError`; correcting the value clears the state; WPF `Validation.HasError` also supported |
| Switches | implicit `CheckBox` | Native Toggle automation and Space interaction, on/off/indeterminate, hover/pressed/focus/disabled; preserve right-aligned `SettingsGrid` |
| Sliders | implicit `Slider`, `SliderThumb` / `SliderRepeat` | Existing horizontal range controls retain native keyboard, repeat, snap and RangeValue behaviour; subtle hover/drag/disabled states |
| Colour | existing `ColorWheel`, `Swatch` | Same HSV/palette logic; hex Value automation, keyboard instructions and focus; swatches preserve colour while disabled, readable selection marks and visible white borders |
| Segmented modes | `SegmentTrack`, `Segment` | Standard RadioButton grouping, selection, keyboard focus and disabled state |
| Tabs | implicit `TabControl` / `TabItem`, headerless `Pages` | Standard selection and arrow-key navigation; semibold is confined to the selected header so page content keeps its own typography |
| Choice chips | `ChoiceChip` (`ZoneChip` compatibility key) | Existing lighting zone toggles share one style; checked/hover/focus/disabled states |
| Lists/tables | implicit `ListBoxItem`, `DataGrid`, `DataGridCell`, `DataGridColumnHeader` | Shared typography, muted blue selection and focus; keep existing row templates, columns, bindings, virtualization and editing callbacks |
| Scrolling | implicit `ScrollBar`, `ScrollThumb` / `ScrollPage` | Vertical/horizontal native Track and page commands, slim neutral thumb |
| Tooltips | implicit `ToolTip` | Inter, wrapping, bounded 360 px width and restrained popup shadow |
| Progress | implicit `ProgressBar`, `MotionPreferences`, `ControlState.ReduceMotion` | Native range semantics; indeterminate pulses and dropdown fades respect live system preference and inherited local reduced-motion state |
| Notifications | `Notice`, `NoticeTone` | Neutral notices remain compact. Information/success/warning/error/loading use a distinct outline icon, explicit status label and dark message text, with polite UI Automation live-region events |
| Dialogs | `AppDialog.Confirm` / `ShowMessage` | Owned native Windows window, Sora icon, shared buttons/type, bounded scrolling body, safe default Cancel, Escape cancellation and explicit acceptance |
| Layout/status | `PageHeader`, `Card`, `SettingsGrid`, `StatusChip` / `StatusDot` | Approved page geometry and readable states; no new notification stack or decorative cards |

`KeyboardSwatch` and `KeyboardValidation` are compatibility keys that inherit the shared styles. New callers should use `Swatch` and `ValidationMessage`.

Multiline inputs (`AcceptsReturn=True`) align content to the top through the shared input style. Single-line inputs retain their approved centred alignment. Dynamically created saved-colour controls must also use `Swatch`; using a generic button can clip small colour samples and lose their intended focus/accessible-name treatment.

## Tokens

Palette, typography, page/sidebar geometry and component dimensions live in `Theme.xaml`:

- `ControlHeight` 32, `CompactControlHeight` 28; `ControlFontSize` 13, `InputFontSize` 14, `CaptionFontSize` 12.
- `ControlRadius` 6, `SwatchRadius` 5, `PopoverRadius` 8, existing `GroupCornerRadius` 10.
- `ButtonPadding`, `InputPadding`, `NoticePadding`, `SegmentPadding` centralize internal spacing.
- `SwitchWidth` 40, `SwitchHeight` 24, `SwitchKnobSize` 20; `SliderHeight` 24, `SliderThumbSize` 18, `ScrollBarSize` 8.
- Existing `AccentBrush`, `NavSelectedBrush`, `HoverOverlayBrush`, `PressedOverlayBrush`, `ControlDisabledBrush`, `DisabledTextBrush`, `ErrorTextBrush` and status brushes provide interaction colours.
- Supporting text is neutral grey (`SecondaryTextBrush` #626262, `TertiaryTextBrush` #6B6B6B). `FieldLabel` uses dark Medium text. Semantic colour is confined to feedback icons; labels and explanations stay readable neutrals.
- `CompactCard`, `SectionContentInset`, `SectionHeadingInset`, `SectionSpacing`, `ControlLabelInset`, `SectionColumnGap`, `PopupShadow` and `TooltipShadow` cover repeated grouping and overlays. The gallery status region uses `FeedbackRegionHeight` to avoid moving sections when feedback changes.

Use tokens/styles before adding local values. Specialized geometry (keycaps, charts, miniature palette dots) can retain its established size.

## Integration examples

```xml
<Button Content="Apply" Style="{StaticResource PrimaryButton}" />
<TextBox local:ControlState.Placeholder="Profile name"
         AutomationProperties.Name="Profile name" />
<TextBlock Text="Use six hex digits." Style="{StaticResource ValidationMessage}" />
<local:Notice Tone="Warning" Content="Connect by USB to read per-key colours." />
```

```csharp
ControlState.SetIsBusy(button, true);
button.IsEnabled = false; // Operation owner, not the presentation state, controls availability.
try { await ExistingOperationAsync(); }
finally { ControlState.SetIsBusy(button, false); button.IsEnabled = true; }

if (!AppDialog.Confirm(Window.GetWindow(this), "Refresh keyboard?",
    "Refreshing discards staged edits.", "Refresh keyboard", "Keep edits")) return;
```

Pair an invalid outline with an explanation and `AutomationProperties.HelpText`; colour alone must not explain an error. Give icon-only controls an accessible name. Preserve natural keyboard order and reserve whole-page live announcements for meaningful changes. Notifications do not auto-dismiss.

Native Windows open/save dialogs, system tray menus, UAC and early startup/single-instance messages remain native. Do not recreate those operating-system surfaces merely to match the theme. Native file pickers retain their accessibility and file-system behaviour.

## Preview and verification

The opt-in component preview is hosted in the actual application, without a new sidebar entry:

```powershell
dotnet src/Sora.App/bin/Debug/net8.0-windows/Sora.dll --screenshot artifacts/components.png --components --component-ui-check --size 1320x860
```

Components is explicitly labelled as a development preview and clears all production navigation selections. Choosing a production page removes the gallery and selects the correct page; in that preview process the page is read-only, with a banner explaining how to open normal Sora. No developer item is added to normal navigation.

Create the gallery before initial window activation and give it initial focus. WPF navigation radio buttons can select themselves when focus first lands on them; clearing `IsChecked` after loading alone does not cover startup. Verify the rendered selection marker and focus outline as well as the checked value, including captures made without the interaction-check flag.

Tables use a neutral hairline outer frame. The shared cell template draws an accent outline only for a cell containing keyboard focus, and an error outline for invalid values. Preserve this template when applying page-specific cell padding; do not remove keyboard focus feedback with a local template.

Gallery sessions set `SoraData` to a unique `component-preview-data/<session>` directory before constructing the host or panels, skip migration and disable automatic hardware application. Sample actions, validation, saving, progress, reset and help operate only on local state. `.checks.json` records WPF/UI Automation checks, `.route-checks.json` records navigation/isolation checks, and font/navigation diagnostics are also written. This preview is not a substitute for checking real production pages.

Production screen verification can add `--isolated-ui-check` to a `--screenshot` command. This uses a new `screen-check-data/<session>` directory and skips legacy migration while retaining production interaction behaviour. It does not enable automatic hardware application. Explicit hardware actions still have their normal meaning: verification must not click Apply, Restore or calibration controls. Use `--fans`, `--gpu`, `--lighting`, `--ssd`, `--wifi`, `--updates`, `--logitech`, `--settings` or `--keyboard` to choose a screen.

The `.ready` file is written after page reads, preview setup and checks finish. Capture the native window after that signal; a fixed delay can otherwise capture an incomplete receiver read or loading state.

Use the shared `ScrollContentInset` thickness (0, 0, 8, 24) on scroll content above persistent action bars. Keep the scroll viewport in its own layout row above the footer; padding complements that separation. Logitech device illustrations remain outside their adjoining form's scroll container.

Use `--components-lower-preview` to capture the lower feedback/list/table examples. Component checks also save the actual confirmation dialog's client content as `.dialog.png`; that image excludes native Windows chrome.

Check Cooler, Settings and connected/disconnected Keyboard at 1304 × 852 visible pixels, plus minimum window size. Keep pending edits local during verification and compare USB snapshots before/after. Do not click hardware Apply just to test visual states. Display-scale simulations remain distinct from actual monitor-DPI testing.

## Phase 5: motion, focus and announcements

`InteractionMotion` owns presentation only. `FeedbackMotionMilliseconds` (90), `SelectionMotionMilliseconds` (120) and `RevealMotionMilliseconds` (140) are shared tokens. Use quadratic ease-out with opacity or a switch-knob render transform; never animate layout dimensions, device commands, sensor values or the time at which a control becomes usable. Interrupting motion replaces its previous clock, without queuing transitions.

- Navigation selection and button feedback use dedicated opacity surfaces. Their final opacity is a local value so template reevaluation during resizing cannot reveal inactive highlights.
- Navigation pages, selected tab content, dialog content and expanded sensor information receive a small opacity reveal. Their layout and hit testing are available immediately. Colour values and dragging remain immediate.
- Windows `ClientAreaAnimation` is read live; inherited `ControlState.ReduceMotion` further restricts motion. Reducing motion cancels current presentation animations at the correct final state. Existing popup/progress triggers use the same policy. No user settings are added or rewritten.
- Dialogs retain safe Cancel defaults, Escape handling and native chrome. Initial focus is established after rendering to handle pending popup focus restoration. Tab traversal cycles within the dialog, and closing restores the invoking control when it remains available.
- Tab headers have an explicit focus outline without changing their dimensions. Native ComboBox, Slider, CheckBox and selection keyboard behaviour is retained.
- Colour pickers publish their actual hexadecimal value through UI Automation Value changes. Disabled pickers reject automation edits. Notices coalesce same-turn changes and suppress repeated identical live announcements; meaningful status text persists.

Verification flags: `--interaction-check` exercises real shared controls in an owned, local fixture window; `--interaction-audit` records production accessible names and text-token contrast; `--desktop-check` resizes the actual window through both acceptance sizes, checks scroll reach, and verifies sidebar selection surfaces. `--interaction-focus-preview` focuses the selected tab or an available dropdown without changing the selected page. These flags invoke no hardware actions or production preference writes.
