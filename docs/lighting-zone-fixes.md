# Preserve independent motherboard lighting zones

## 0.10.10: synced startup authority and applied status

With sync enabled and common Off saved, startup could replay older independent static colours and the UI could claim “Synced” before successful application. Startup now resolves the common sync effect across memory and all board zones; independent mode retains its separate settings. Applied status requires a successful current application, including the first completed frame for an animation. Saved but unapplied sync remains visible and retryable; suspension/failures invalidate applied status.

Fresh results: 51 application groups, 46 lighting scenarios and 19 package guards passed. Permanent installed startup applied Off to both DIMMs/all seven board zones. The user confirms all connected RAM/case/AIO/motherboard lighting off and the window responsive. This is Off acceptance, not physical qualification of every effect. [0.10.10 evidence and retained failures](Sora-0.10.10.md).

The motherboard planner previously promoted **any** selected Rainbow zone to GCC's global Wave17 sequence. It also cleared all eleven controller slots and changed global strip settings before every ordinary edit. Consequently, selecting the I/O-cover light could reset the other lights even when their saved settings were unchanged.

`GigabyteRgb.Plan(zones, completeBoardZones: null)` and `ApplyZones` default to a partial edit: only the supplied descriptor slots are cleared, applied and allowed to settle for 100 ms before their addressed effects and selected apply bits are sent. A partial edit sends no global clear, strip-output-count command, global streaming switch, or Wave17 command. Empty selections generate no traffic, and duplicate/out-of-range zones are rejected before any packet is sent. Static animation frames apply only their selected divisions without repeated resets.

### 0.8.3 GAME ON regression repair

The 0.8.1 isolation change removed the initialization previously performed before ordinary writes. On this board, the GAME ON logo stopped responding even though the correct zone-0 red packet was sent successfully. A zone-0-only reset/apply/write did **not** restore it (user confirmation). Restoring the earlier captured connection setup, complete descriptor clear, all-zone apply and 100 ms settling sequence, followed by every saved zone's desired effect, restored the red logo (user confirmation). After initialization, an isolated zone-0 blue edit worked while the I/O cover retained the rainbow the user selected in the running Sora. The logo was then returned to red using an isolated write.

The fix initializes once before native board edits when the complete desired board state is known, restoring every zone's saved setting after the controller reset. Later edits remain selected-zone operations. Resume, a failed write, exiting a sync animation, or leaving the global wave invalidates that initialized state. The global wave and sync-animation setup retain full initialization. Initialization is refused by the packet planner if the caller has only a partial/unknown board state; unrelated saved settings are never replaced with guessed colors.

This confirms that restoring the earlier initialization resolves this hardware regression. The exact internal firmware latch or individual setup command responsible has not been isolated. Evidence: `artifacts/game-on-logo-repair.json` (selected reset, ineffective), `artifacts/game-on-logo-baseline.json` (complete initialization, effective), `artifacts/game-on-logo-partial-blue.json` (isolated update, effective), and `artifacts/game-on-logo-red-restored.json`.

The global wave requires an explicit complete catalog. `CanUseGlobalRainbow` checks exact catalog coverage, unique valid zones and matching Rainbow brightness/speed settings. Recognized catalogs are Sora's seven Z890 GAMING X WIFI7 divisions (`0,2,4,5,6,7,9`) or its generic eleven-slot catalog. A selected one-zone subset passed as a purported catalog cannot authorize the global wave. The caller must additionally establish that its catalog describes the connected motherboard. Full-board uniform Rainbow retains the existing captured GCC sequence; a mixed state retains each zone's desired effect in settings.

## Evidence

Read-only inspection used the installed `RgbMotherboard.dll` from `C:\Program Files\GIGABYTE\Control Center\Lib\GBT_rgbMotherboard_UC`, SHA-256 `6e4d80a63878c73aa0cd5ff34ebe30ee52f6d9eb868b9eb2de8719bffb51e22e`. [REA](https://github.com/morluto/rea) `inspect-managed-members --type RgbMotherboard.IT8297_5702.MCU_8297` retains concrete managed metadata/string/CIL-anchor evidence in `gcc-re/evidence/rea-lighting-zone-mcu-members.json`. Existing ILSpy decompilation and previously captured GCC HID traffic supply the following observations:

- `MCU_8297.SetLedEffect`, case 24 (`Wave17`), explicitly zeroes `Zone_Sel0` and `Zone_Sel1`, sets mode 9, brightness 100, time base 550 and control value 7. `frida-gcc-red-rainbow.jsonl` and `frida-gcc-wave.jsonl` show the global packet `CC22…` followed by apply `CC280400`. This pattern was previously visually confirmed as a flowing rainbow on the ARGB headers, while plain RGB lights display white.
- `write_to_mcu(iDivis)` selects header `0x20 + division` for divisions below eight, and `0x90 + division - 8` above. `Apply(iApplyBit)` writes the division bitmask into the low/high bytes after `CC28`. These methods support addressed writes without `ClearIT8297Parameter`; the normal per-division effect path invokes them directly.
- `ClearIT8297Parameter` clears every division and applies all divisions. It is inappropriate for an independent-zone edit.
- The known per-zone mode 4 spectrum cycle is already supported by Sora's recovered GCC effect timing tables. `ForZone` sends that native cycle for an independent Rainbow selection, with its own brightness/speed. It does not transmit global mode 9 to an arbitrary division or claim mode 9 isolation has been verified.

For a plain RGB light such as the I/O cover, independent Rainbow means cycling that light through the spectrum. Spatial multicolor motion requires addressable LEDs and is currently supported only by the captured complete-board Wave17 effect. Per-header per-LED streaming commands `0x58–0x5C` and masked streaming control exist in GCC's source-derived implementation, but they have not been validated on this hardware and are not introduced by this fix.

The original 0.8.1 investigation used read-only analysis. The later 0.8.3 diagnosis above performed explicitly scoped lighting writes, with visual feedback from the user; no foreign-application injection or driver changes were performed. Independent spatial per-header streaming remains unverified.

## Verification

Regression checks cover the exact captured global sequence with explicit full-board authority, selected I/O-cover cycle (`CC91` then `CC280002`), mixed Rainbow/static scope, differing per-zone brightness, fake/incomplete catalogs, duplicate-zone rejection and empty selections. Sync-frame checks verify selected apply masks, including the upper division byte. The complete deterministic suite is run through `Sora.Checks`; no test claims visual or physical read-back of motherboard effects.

`Sora --lighting-editor-check --output <path>` additionally exercises the real WPF controls with synthetic, identity-bound DIMMs and a board catalog. It never scans or applies hardware. Scenarios cover single-click zone selection, one-zone Rainbow, dirty-target preservation when switching selections, refreshing memory controls while board edits are pending, independent module colors, RAM-sync staging, global-sync preference transitions, invalid color drafts hidden by a different selection, discard, and preference JSON reload. A diagnostic-only reflection fixture sets the service's private state; no production setter or hardware abstraction was added.

The production `LightingService.MergeAppliedSettings` function is also checked directly: successful partial updates preserve unedited module/zone settings, the first per-module edit migrates a legacy whole-RAM look for the other installed modules, failed/skipped sections retain their saved state, and a confirmed unified RAM update replaces module looks without altering board settings. These cases test the same merge function used after actual successful device operations.
