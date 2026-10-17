<p align="center"><img src="assets/brand/sora-mark.svg" width="112" alt="Sora"></p>

<h1 align="center">Sora</h1>


A native Windows hardware hub for the **DeepCool LD360** display, **Kreo keyboards**, and **RGB memory and Gigabyte motherboard lighting**. Sora reads CPU temperature, utilization, and package power independently through **LibreHardwareMonitor 0.9.6 and PawnIO**, and talks to the keyboard directly over USB; Kreo KB Hub does not need to run.

## What's new in 0.6: Lighting

A **Lighting** page (sidebar, **Ctrl+3**) replaces GIGABYTE Control Center's RGB Fusion for RGB memory and the motherboard's RGB headers. It was reverse-engineered from GCC 26.08.11 and the ENE DRAM HAL it uses (`gcc-re/REPORT.md`, local only).

- **Memory** (ENE-based DDR5 RGB, such as XPG Lancer Blade RGB): Static, Breathing, Flashing, Color cycle, Rainbow or Off, with colour, brightness, speed and direction. The page opens showing what the RAM is doing now.
- **Motherboard** (Gigabyte RGB Fusion USB controller IT5711): Static, Pulse, Flash, Double flash, Color cycle, Wave or Off, for each light separately or together. On the Z890 GAMING X WIFI7 the lights are named: GAME ON logo, I/O cover, SSD heatsink, ARGB headers 1–3 and the RGB header. Sora leaves the board alone until you apply something there.
- **Apply when Sora starts** sets your lighting again at sign-in, because RAM lighting resets after a shutdown.
- **Restore** puts the RAM back exactly as it was before Sora first changed it.

How it protects your hardware:

- Exit GCC first (tray icon → Exit). Sora will not write while GCC or another RGB program (Armoury Crate, OpenRGB, SignalRGB, iCUE, XPG PRIME) is running.
- Applying lighting never makes the board or the RAM remember it: no flash saves, and not the board's "keep LED settings" command either. After a shutdown the hardware shows its own stored lighting until Sora sets yours again at sign-in. Two one-off maintenance commands send GCC's own keep command, only when you run them: `Sora.exe --board-default-rainbow` (GCC's rainbow, kept after restarts, as GCC does on exit) and `Sora.exe --board-builtin-default` (stop keeping, as GCC does when it starts, so the board uses its built-in lighting).
- Sora never writes to the RAM's SPD chip. It writes only to verified ENE lighting controllers (`0x70–0x77`), and only to their effect, colour and apply registers. Nothing is saved to the RAM's or the board controller's flash.
- Every RAM change is read back from both sticks; a mismatch is reported, not ignored.
- It uses the signed PawnIO SMBus module that ships with LibreHardwareMonitor and the system-wide SMBus lock other tools use. GCC's kernel drivers are not needed.
- Overclocking, undervolting, voltages, timings and fan control are not part of Sora.

Verified on this PC: two ADATA AX5U6000C3616G-SLABR sticks (Static and Breathing written, read back and restored byte-for-byte), and every motherboard light (GAME ON logo, I/O cover, SSD heatsink, case fans and radiator fans on ARGB headers 1 and 2) switched between colours repeatedly.

0.6.9: In sync mode, Color cycle, Breathing and Flashing are driven the way GCC drives them: Sora computes one colour per frame and writes it as Static to both sticks and every board light, so everything moves in lockstep (the sticks are also started together for Rainbow). The engine re-checks the RAM controllers every 10 s and stops if GCC or another RGB app starts. Sync no longer switches itself off after Apply. **Rainbow when Sora closes** (on by default): on exit, like GCC, the RAM returns to its own rainbow and the board to its rainbow wave.

0.6.5: **Sync memory and motherboard.** One switch at the top of the Lighting page replaces the two cards with a single "All lighting" editor: one effect, colour wheel, brightness and speed for the RAM and every board light. The effects are the ones both can run (Rainbow, Static, Breathing, Flashing, Color cycle, Off); each device gets its closest built-in effect (Breathing is the board's Pulse, Flashing its Flash, Rainbow the RAM's rainbow and GCC's board wave). Sign-in re-applies the synced look.

0.6.4: **Rainbow** on the motherboard is GCC's flowing rainbow wave (the spectrum runs around every ARGB fan), sent byte for byte as GCC sends it (Sora 0.6.2–0.6.3 sent a colour cycle by mistake). RAM colours are no longer washed out: on these ENE chips GCC also sets a brightness register from the colour ((R+G+B)/84, so pure red runs at 3 of 9), and Sora now does the same, with backup, restore and readback.

0.6.2 adds a colour wheel with a saturation/brightness square to both cards, and **Rainbow** (GCC's default colour cycle) as the first effect for the memory and the board. The controller's own Wave mode is gone: it turned the ARGB fans off on this board (GCC draws its flowing fan rainbow in software). If an apply fails, that part is no longer saved and re-sent at sign-in.

0.6.1 fixes motherboard lighting: 0.6 sent one all-lights command that the controller applies only to the GAME ON logo. Sora now addresses every light the way GCC does, with GCC's timing.

## What's new in 0.4

- **Starts with Windows.** Tick **Start Sora when I sign in to Windows** (Preferences). Sora then starts in the tray at sign-in with administrator rights and no UAC prompt, through a Task Scheduler task named *Sora*. **Turn on the cooler display when Sora starts** sends temperatures to the LD360 without opening the window.
- **Much lighter in the tray.** The window now exists only while it is open; closing it to the tray releases it. Measured on this PC with live sensors: about 15–19 MB of RAM in the tray (0.3.1: 173 MB), 129 MB with the window open. See `artifacts\sora-0.4-memory-comparison.json`.
- **One program file.** Sora is now a single `Sora.exe` (about 5 MB) with Start menu and desktop shortcuts; the `Start Sora.cmd` launcher is gone.
- **Redesigned window.** A sidebar replaces the top tabs. Each device shows a live status line (for example *LD360 · showing 54°C* or *Swarm 75 · USB*). Switch pages with **Ctrl+1/2**. The window is shorter (860 px) and fits a 1080p screen with the taskbar. The cooler page puts **Start display** in the header and groups the preview with its status. Sliders, scrollbars and dropdowns share one dark style.

Screenshots: `artifacts\sora-0.4-display.png`, `sora-0.4-keyboard.png`.

## Kreo keyboard controls

1. Connect the keyboard with a USB cable and select wired mode. Exit Kreo KB Hub from its tray menu.
2. Open **Sora** from the Start menu or desktop and select **Keyboard**. It connects automatically; use **Connect / refresh** after reconnecting.
3. In **Lighting**, choose an effect and edit its hex color directly, or select a key and use **Per-key RGB** to paint it. Fixed-color effects disable custom color controls.
4. In **Key assignment**, select a source key, choose its target action, and click **Use this assignment**. The preview shows the staged source → target mapping. Targets remain standard actions even if a key was previously remapped.
5. Click the persistent **Apply to keyboard** button. Sora saves a backup, writes complete configuration blocks, and checks device readback. The focused key-test field shows the actual action Windows receives.

**Profiles** exports/imports editor state; **Load last backup** stages the previous setup. Click Apply to send a loaded profile. Backups are under `dist\Sora-0.4\data\keyboard`. Existing 0.3 profiles are upgraded during import. A profile must match the keyboard's six-byte model identity. If keyboard settings change externally, refresh before applying edits.

Model definitions cover **Swarm 75, Hive 75, and Hive 98** from the installed KB Hub. The **Swarm 75** was verified on this PC: identity, lighting, per-key colors, and a temporary F12-to-F11 remap all passed readback, followed by successful restoration of the original settings. Hive models are source-derived and have not been tested on hardware here.

This release supports **wired USB configuration and base-layer remapping**, custom hex colors for supported effects, and per-key RGB. Wireless/Bluetooth configuration, macro authoring, Fn-layer editing, debounce/sleep controls, audio-reactive streaming, firmware updates, and factory reset are not included. Existing settings outside edited fields are preserved.

Version 0.3.1 fixes incorrect effect-name lookup and duplicate Off labels, and fixes key assignments by writing the full 512-byte key-map block with its completion marker. The physical F12→F11 test passed on this PC, with the original setup restored. The UI now has separate lighting/assignment/profile tabs, a larger keyboard, named actions, hex validation/swatches, pending-change feedback, and a persistent Apply bar. Screenshots: `artifacts\sora-ui-after-lighting.png` and `artifacts\sora-ui-after-assignment.png`.

See [the reverse-engineering report](kreo-re/REPORT.md) for REA evidence, recovered protocol, tests, and limitations. No Kreo executable, DLL, artwork, or firmware is bundled.

## Switch to the independent version

1. Exit the older Sora (0.3.1 or earlier) through its tray menu: **Stop display and exit**. Closing its window normally leaves it running.
2. Exit the original DeepCool app and stop **Deep Cool Display Service**, if still installed.
3. Open **Sora** from the Start menu or the desktop shortcut. Both point to `dist\Sora-0.4\Sora.exe`, a single program file that uses the installed .NET 8 Desktop Runtime.
4. Accept the Windows administrator prompt. CPU register access through PawnIO requires elevation.
5. Confirm live readings, then click **Start display**.
6. You can uninstall DeepCool through Windows Settings > Apps. **Keep PawnIO installed**; it is Sora's independent hardware access driver.

If needed, stop both original services from an Administrator PowerShell:

```powershell
Stop-Service -Name 'Deep Cool Display Service','Deep Cool Helper Service'
```

The helper is `SensorBridgeServer.exe`; the display service is `DeepCoolDisplayService.exe`. Sora 0.2 needs neither. Sora does not automatically uninstall software or change service startup settings. The verification script temporarily stopped and restored the helper during testing.

Previous release folders are retained as fallbacks. The shortcuts open 0.4; display preferences and keyboard backups are copied from 0.3.1 when available.

## Features

- Live temperature, utilization, and watts, with a preview of the selected display format.
- Celsius/Fahrenheit and leading-zero preferences.
- Tray operation: left-click the tray icon to open Sora; right-click for **Start/Stop display** and **Exit Sora**. Opening Sora again while it runs brings up its window.
- Retries independent sensor access and reconnects to the LD360 after interruptions.
- Rejects missing or invalid CPU readings and stops sending readings older than five seconds.
- Checks for competing DeepCool display processes and releases its USB handle when they appear.
- Opens only supported LD-series HID interfaces with VID/PID `3633:000A`.
- Starts USB output only when you click **Start display**.

Sora controls the segmented display. It issues no pump-speed, fan-speed, ARGB, or firmware commands. When readings become stale, Sora stops refreshing; the cooler's handling of its last displayed value depends on its firmware.

Sora is one file, `Sora.exe`. Settings, keyboard backups and a bounded log are stored in the `data` folder next to it. `build.ps1` re-points existing shortcuts to each new build; `scripts\Install-Shortcuts.ps1 -Target <path to Sora.exe>` creates them. No automatic startup is installed.

## Build and verify

Requires the .NET 8 SDK to build, or the .NET 8 Windows Desktop Runtime to run. Both are installed on this PC. The first restore downloads the pinned NuGet dependencies.

```powershell
.\build.ps1
.\test.ps1

# With dependencies already restored:
.\build.ps1 -NoRestore
.\test.ps1 -NoRestore

# Run from an Administrator PowerShell. No display reports are transmitted.
.\dist\Sora-0.4\Sora.exe --diagnose --samples 5 --output E:\Sora\artifacts\live-diagnostics-0.4.json

# Read Kreo descriptors and settings without changing the keyboard configuration:
.\dist\Sora-0.4\Sora.exe --keyboard-diagnose --output E:\Sora\artifacts\keyboard-diagnostics.json
```

`--diagnose` enumerates HID descriptors, opens and releases an output handle without writing, collects CPU samples, and reports conflicting processes. It exits after completing or timing out.

From an Administrator PowerShell, `scripts\Verify-IndependentSensors.ps1 -WithoutDeepCool` additionally tests with the DeepCool helper stopped and restores its previous state in a `finally` block.

Implementation:

- `src\Sora.Core`: CPU sensors, LD protocol/lifecycle, Windows HID transport, Kreo protocol, model definitions, profile validation, and keyboard settings read/write.
- `src\Sora.App`: WPF window shell, display and keyboard pages, diagnostics, and tray application.
- `tests\Sora.Checks`: display lifecycle checks plus Kreo packet, profile, lighting, color, and remapping checks using a real device snapshot.
- `deepcool-re`: reverse-engineering reports and evidence.
- `kreo-re`: REA-assisted static analysis, definition extraction, focused assembly, and live keyboard evidence.

## Validation on this PC

The 0.4 release builds with warnings treated as errors and passes **18 checks**. The display and keyboard code paths are unchanged from 0.3.1; 0.4 changes only the window design.

Earlier 0.3.1 results: `artifacts\kreo-physical-input-verification.json` confirms actual F12→F11 delivery and restoration. `artifacts\kreo-effect-hex-verification.json` confirms custom Static/Breathing effect colors and restoration. `artifacts\sora-ui-after-lighting.png.checks.json` records the actual WPF effect/assignment/paint/discard interaction checks, with zero configuration writes.

The earlier 0.2 sensor validation on this **Intel Core Ultra 9 285K** received five fresh temperature, package-power, and utilization samples with **Deep Cool Helper Service stopped** and no DeepCool sensor or desktop process running. The helper was restored afterward. The independent driver was the already installed **PawnIO 2.2.0**. Sensor acquisition has not changed in 0.3.

During that earlier validation, the real LD360 was enumerated with a 64-byte output descriptor and its output handle opened successfully. Those display diagnostics transmitted zero USB reports. Actual pump-screen output still requires confirmation using **Start display**. The new Kreo hardware verification intentionally sent keyboard settings reports and restored the original state.

Evidence: `artifacts\sensor-independence-verification.json` and `artifacts\independent-without-deepcool.json`. See [the sensor report](deepcool-re/SENSOR-REPORT.md) and [the recovered display protocol](deepcool-re/REPORT.md).

## Dependencies and licenses

The release includes third-party notices and license information under `licenses`. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). LibreHardwareMonitor is pinned to version 0.9.6; exact resolved dependencies and content hashes are recorded in the projects' `packages.lock.json` files. No DeepCool or HWiNFO binaries are bundled.
