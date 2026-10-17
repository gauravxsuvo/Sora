# Sora UI redesign plan

Working plan for the 0.5 redesign. Each phase ends with real screenshots and waits for approval before the next one starts.

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | Audit of code and 0.4 screenshots | Done, approved |
| 2 | Design tokens + app shell (sidebar, shared page header) | Done, approved |
| 3 | Sensor / display state model + Cooler Display page | Done, approved |
| 4 | Keyboard: geometry, keycaps, editing workflow | Done, awaiting approval |
| 5 | State logic and action feedback (Apply/Discard, connection) | Not started |
| 6 | Motion system | Not started |
| 7 | Accessibility, resizing, final verification, README | Not started |

## Ground rules

- Keep every existing feature: Windows startup task, tray behaviour, Fahrenheit, leading zeros, auto-start display, profiles, backups, hex validation, palette, per-key paint, remaps, key test field.
- No mock data. Readings, connection state and success messages must come from real state.
- Don't rewrite the backend; only add state where the UI needs to tell different conditions apart.
- No new dependencies unless clearly worth it. Motion uses WPF Storyboards.
- The public repo excludes `kreo-re/`, `deepcool-re/`, `artifacts/`, `dist/`, `vendor/`, `rea/`, `v8*`, `tools/` (vendor code, device evidence, oversized files).

## Audit findings to fix

1. Text as small as 10 px; footer and hint text fail AA contrast. Fonts are set on TextBlock only; no tabular numerals.
2. Eight near-identical navy surfaces; two different `Card` styles.
3. Keyboard renders X at 1.45× and Y at 0.73× (squashed); widths come from legend hit-boxes, not key pitch; `MaxHeight=185`.
4. Vendor hotspot data jitters by 1–2 px and rows sit at mismatched Y values. Normalise to a 0.25u grid in code and check that key indices and labels are preserved for Hive 75, Swarm 75 and Hive 98.
5. Selection replaces the key's real colour; colours are dimmed to 34%; no hover, focus or pressed states.
6. Per-key panel shows a hex value with no key selected; single-key and whole-keyboard actions aren't separated; Fn is selectable but silently not editable.
7. Disabled Apply still looks active (0.4 opacity over bright blue). "Connected over USB" means "read once", not "connected now".
8. Cooler page shows the readings twice; preview card mostly empty; display settings, app settings and diagnostics share one card.
9. Sensor status and readings update independently (stale readings can sit next to "unavailable"); the "Live sensor readings are still available" message is hard-coded; "Ready to connect" without detecting the LD360; error causes collapsed into one string; sensor failures not logged.
10. Header layouts differ between pages; browser-style tabs; MessageBox dialogs; turning on startup silently enables auto-display; model picker looks like a device selector.

## Design system (phase 2)

- Tokens live in `src/Sora.App/Theme.xaml`, merged by `App.xaml`.
- Surfaces: window `#0E0F11`, sidebar `#141518`, content group `#1A1B1F`, raised `#222327`, control `#2B2C31`, inset input `#111215`. Hover/press are white/black overlays so they work on any fill.
- Text: primary `#F5F5F7`, secondary `#AEAEB2`, tertiary `#8A8A93` (5:1 on the content surface), disabled `#5E5E66`.
- Accent: `#0A84FF` (Apple dark-mode blue) for primary action, selection, focus ring only.
- States: success `#30D158`, warning `#FFD60A`, error `#FF453A`, idle `#636366`.
- Type: Segoe UI (WPF can't select Segoe UI Variable's named instances, so weights would be synthesised). Scale 26 title / 16 section / 14 body / 13 secondary / 12 caption; numbers use tabular figures; nothing below 12 px.
- Spacing on a 4 px grid (4, 8, 12, 16, 24, 32). Control height 32. Radii: 6 for controls, 10 for surfaces.
- Disabled = muted fill and muted text, never opacity over an accent.

## Verification loop

1. Preview build (non-elevated): copy `app.manifest` with `asInvoker` into the scratchpad, then
   `dotnet build src/Sora.App -p:ApplicationManifest=<copy> -p:IntermediateOutputPath=obj/preview/ -o <scratch>/preview`
2. `set SORA_INSTANCE=preview` and run `Sora.exe --screenshot X.png` (add `--keyboard` for the keyboard page).
3. Screenshot, critique, fix, screenshot again. Run `test.ps1` before each commit.
4. Non-elevated previews always show "sensors unavailable". Live readings need an elevated build on the real PC.
5. Bump `build.ps1`'s output folder before any release build (the running release locks its folder).

## Decisions log

- 2026-10-17: repo initialised and pushed to github.com/gauravxsuvo/Sora (public). RE folders kept local.
- 2026-10-17: `Kreo-keyboard.md` moved to `docs/` so a clean clone builds.
- 2026-10-17: phase 2 shell done. Known carry-over: at 1080×720 both page bodies scroll internally (same as 0.4); fixed in phases 3–4. Screenshot mode accepts `--size WxH`.
- 2026-10-17: phase 3. Sensors report Starting/Live/Unavailable with a cause; readings are cleared on failure; sensor changes are logged. LD360 presence is a read-only USB scan every 3 s while the window is open. Turning on start-at-sign-in no longer silently turns on the display (a hint explains instead). About sensors is inline, not a MessageBox.
- 2026-10-17: phase 4. KreoLayout snaps vendor hotspots to a 0.25u grid (checked for all three models: no lost or overlapping keys). Keycaps glow in their real colour; selection is an outer ring; remaps show "→ X" and a dot; Fn dims while assigning. Mode switch lives on the board toolbar. Editors scroll on short windows so the board keeps ~300 px. Preview flags: --keyboard-paint-preview, --keyboard-assignment-preview (staged only, never applied).
- 2026-10-17: 2.4 GHz receiver (3554:FA09) is detected and shown as "settings need USB" instead of a red error; Apply is disabled while only the receiver is present. Real wireless configuration needs the receiver protocol (FF02/2 20-byte output, FF04/2 8-byte feature), not yet reverse-engineered. Release 0.5.1.
- 2026-10-17: 2.4 GHz receiver support (release 0.5.2). Protocol recovered from OemDrv CDev3632 (docs/Kreo-receiver-protocol.md). Over the receiver Sora reads lighting, key map, effect palette and battery, and writes lighting (verified with readback and restore). Key map (01) and palette (09) writes are implemented as a reversible hardware check but not run yet, so they stay disabled in the app. Per-key colours cannot be read over the receiver.
- 2026-10-17: brand. The cloud mark from soro_logo_light.svg is the Sora logo: assets/brand has transparent SVGs (sky gradient, white, ink) generated by scripts/Build-Brand.ps1, which also builds the multi-size assets/Sora.ico (window, taskbar, tray, shortcuts). The sidebar uses the vector mark.
- 2026-10-17: 0.5.3. Receiver writes for key map (01) and palette (09) enabled in Apply with backup, readback and automatic rollback. Command 06 over the receiver is ResetDevice and 88/8E are SetRealData (live frames), so per-key colours cannot be stored over 2.4 GHz; Sora remembers them from the last USB read and labels them as such.
- 2026-10-17: 0.6, Lighting. GCC reverse-engineered (gcc-re/REPORT.md, local): RAM via ENE DDR5 controllers on the SMBus (PawnIO SmbusI801 from LHM, global SMBus mutex, read-only signature before any write, register allow-list, readback), motherboard via IT5711 USB feature reports (commands 0x60/0x20/0x28 only; never 0x5E flash save). OC/undervolt/fans excluded by the user. Verified on the ADATA sticks with restore; board effects untested visually (nothing on its headers). Lighting page = sidebar item 3.
- 2026-10-18: 0.6.1 board fix. Frida captures of GCC's own HidD_SetFeature calls showed per-zone packets (0x20+n / 0x90+n-8, mask 1<<n) with a 100 ms wait after the clear; header 0x20 only drives zone 0. Zones on this board (GCC divsmap MB_Id 31, layout 4, mapped by the user): 0 GAME ON, 2 SSD heatsink, 9 I/O cover, 4 RGB header, 5/6/7 ARGB headers. Sora no longer sends 0x47 01 (it made the board remember Sora's colours across restarts); the user restores GCC's stored lighting by opening and exiting GCC once.
- 2026-10-17: 0.6.2. HSV colour wheel (ColorWheel.cs) for memory and board. Board "Rainbow" = GCC's set_all_rainbow (InitAllRainbowEffect: PatternType 9 → LMode ColorCycle, brightness 8, speed 8); GCC's ARGB rainbow wave is software (SWave 100). Hardware Wave (mode 6) left ARGB headers 5/6 dark on this board, so Wave is removed from the UI and Rainbow/saved Wave are sent as ColorCycle. At the user's request the board stored the rainbow (`--board-default-rainbow`: GCC's rainbow + 0x47 01, exactly GCC's exit). The user wants the boot-time rainbow *wave*; next step is `--board-builtin-default` (0x47 00, GCC's connect command) plus a restart, to see whether the firmware's built-in lighting is that wave. Apply now saves only sections that reached the hardware.
- 2026-10-17: 0.6.3/0.6.4. Pale colours: GCC's ENE HAL writes control +9 (0x8029, brightness 0-9) = (R+G+B)/84 on chip type 0xD5 (disassembly + Frida capture of AACDDR_SetMode(1, 0xCC,0,0, 2, 2)); Sora now writes it (allow-listed only for 0xD5), backs it up and reads it back. Board rainbow: GCC's flowing fan rainbow is hardware Wave17 (mode 9, header 0x22, apply mask 0x0004), confirmed by ending GCC (flow continues) and by NtDeviceIoControlFile captures of GCC and Sora (gcc-re/hook-ioctl.js). GBT_DL_LIB (Windows Dynamic Lighting LampArray helper) only sets one colour on RGB sync. 0.6.2-0.6.3 bug: LightingService converted Rainbow to ColorCycle before ApplyZones; GigabyteRgb.Plan is now checked. The board stores the wave at the user's request (--board-default-rainbow = wave + 47 01).
- 2026-10-17: 0.6.5 sync mode. LightingSettings.Sync + SyncLighting (SyncEffect Rainbow/Static/Breathing/Flashing/ColorCycle/Off, colour, brightness 10-100 %, speed 1-5). Apply in sync writes SyncLighting.ToMemory() and ToBoard(all zones) through the normal apply path and stores them as Memory/BoardZones, so sign-in needs no special case. Board brightness = nearest GCC level, speed 1-5 -> 0,2,5,7,9. The toggle is saved at once; the hardware changes only on Apply.
- 2026-10-17: 0.6.6-0.6.9. Sync engine (SyncEngine.cs) for animated sync effects, modelled on GCC's EneMemoryDeviceV5 threads (EneModeLedEffAutoThd etc. stream AACDDR_SetMode(1, rgb) per frame): ~14 fps here, RAM by byte writes because PawnIO's i801 module rejects SMBus and I2C block writes (tested once per start with readback; Sora falls back automatically). ENE writes now stage every DIMM and apply them back to back, so the sticks start in phase. GCC aligns sticks with AACDDR_Synchronize -> HAL +0x28 writing frame register 0x80F2 (not used by Sora). Bug: ApplyAsync saved new LightingSettings(...) and dropped Sync; now uses with. Exit lighting (RainbowOnExit): RAM original Rainbow from the backup, board Wave17; nothing saved to the devices.
- 2026-10-17: 0.6.10. Sidebar and status chip: sync shows "Synced · <effect>" from the settings (right before the first scan), otherwise "RAM <mode> · Board <effect>"; no more "RAM · Not checked". Startup with an animated sync effect scans first, then starts the engine; the window reads lighting once on open on any page. Refresh while the engine runs rescans the RAM alongside it and keeps the last board reading (an engine frame could split the board's info request and reply), so Check again no longer blips the lights.
- 2026-10-17: 0.6.11 keyboard receiver fix. --receiver-diagnose (read-only) showed the receiver drops a request while the keyboard radio is idle or it is answering another reader (replies are broadcast to every open handle), then answers the next within ~100 ms. Reads now retry (4 x 1.2 s, KreoReceiver.ReadWithRetryAsync) instead of one 4 s wait; the Keyboard page retries quietly every 8 s while the keyboard does not answer.
