<p align="center"><img src="assets/brand/sora-mark.svg" width="112" alt="Sora"></p>

> **Sora 0.11.0 — UI/UX refinement complete.** All five phases are approved. [Download the Windows x64 installer](https://github.com/gauravxsuvo/Sora/releases/latest) · [Release notes](docs/Sora-0.11.0.md) · [Design contract](brand.md) · [Shared components](docs/component-library.md). Saved preferences, profiles and hardware integrations are preserved. Requires the .NET 8 Windows Desktop Runtime (x64). Historical installed-machine and hardware qualification records remain in PLAN.md and the hardware guides.

<h1 align="center">Sora</h1>


A native Windows hardware hub for the **DeepCool LD360** display, **Kreo keyboards**, **Logitech G502 HERO**, **RGB memory and Gigabyte motherboard lighting**, **Gigabyte motherboard fans**, **NVIDIA GPU fans**, **SSD health**, and **Wi-Fi antenna measurements**. Sora reads CPU temperature, utilization, and package power independently through **LibreHardwareMonitor 0.9.6 and PawnIO**, and talks to the keyboard directly over USB; Kreo KB Hub does not need to run.

## 0.10.0: G502 software controls and lighting

Open **Logitech** with **Ctrl+9**. Edit DPI stages and a separate hold-to-shift speed, remap G2–G11 on default/G-Shift layers, create chord macros, and choose independent Primary/Logo lighting. The editor uses G HUB's real locally imported front/side images and marker coordinates. LIGHTSYNC adds color/RGB/palettes, screen sampling and speaker-level audio lighting. Device settings expose firmware, DPI Always On and startup effects.

Exit G HUB from its tray menu, then choose **Apply profile**. Sora acquires software control without enabling onboard memory or writing onboard profile slots. The mouse's temporary firmware mode after G HUB exits no longer disables Apply. Lighting and device settings apply separately. **Stop Sora controls** restores the original mouse control state; tray/reconnect/suspend cleanup is handled by the persistent service.

**35 check groups pass.** A live bounded test applied1600 DPI/500Hz, both device preferences and both reactive lighting modes, then restored originals. Physical G6 timing and complete G HUB parity are not claimed: G1 remains native, G9 defaults to DPI Cycle, audio follows output level, and automatic application profiles/onboard editing/vendor integrations remain unavailable. See [evidence and coverage](docs/Logitech-GHUB.md) and [the continuation plan](docs/logitech-plan.md).

## 0.8.9: Updates page, lighting straight at sign-in

- **Updates** (new page, Ctrl+8): GCC's BIOS and driver check, reverse-engineered from `GBT_MB_Update`/`MB_Update_Lib` and reproduced read-only. On the user's PC it reports exactly GCC's driver list (Intel NPU, Intel UHD Graphics, ITE PD) plus BIOS F21 for the Z890 GAMING X WIFI7. Sora downloads to `Downloads\Sora updates`, checks the size and the installer's digital signature, and never installs drivers or flashes the BIOS. See [the investigation](docs/Gigabyte-updates.md).
- **Lighting at sign-in without waiting:** the sign-in task no longer waits 15 s, and Sora no longer waits another 18 s before applying; retries still cover hardware that is not ready.
- The RAM's own memory is still never written: GCC never does it, and OpenRGB reports hard reboots on this exact RAM (XPG Lancer Blade).

## 0.8.8: Wi-Fi reads only, exactly like GCC

GCC's Wi-Fi Compass (`MB.CPNT.WiFi.dll`) never forces a scan: it only reads the connected network and the network list Windows already has. Sora now does the same and no longer contains `WlanScan` at all, nor radio-state or RSSI driver queries. **Scan networks** is now **Refresh**. Forced scans preceded both Realtek 8922AE firmware failures on 17 October 2026.

## 0.8.7: no more start-up task rewrites (Defender false positive)

Microsoft Defender flagged Sora 0.8.6 as `Behavior:Win32/Persistence.A!ml` and removed it. Cause: Sora read its sign-in task with `schtasks /XML`, the read failed on this PC, and Sora re-created the elevated task on every launch. Sora now reads the task with the Task Scheduler API, never rewrites it because a read failed, and touches it only when you turn **Start with Windows** on or off, or once if it points at a different Sora. Releases are reached through `dist\Sora-current`, a junction that `build.ps1` moves to each new build, so updates change neither the task nor the shortcuts.

## 0.8.6: gentler Wi-Fi scanning

While Wi-Fi was off, 0.8.2-0.8.5 re-checked the radio by forcing a scan every 3 seconds and kept doing it while the radio powered up; on the user's Realtek 8922AE that coincided with a failed firmware reset (Device Manager code 10). Sora now reads the radio state with a read-only query, never scans an adapter that is off or not ready, waits 15 s after the radio turns on, and scans each adapter at most every 15 s.

## 0.8.5: failed Wi-Fi card reported clearly

When Windows lists no Wi-Fi adapter because the card's driver failed to start (for example Device Manager code 10 after a Realtek firmware reset), Wi-Fi Compass names the card and says how to recover, instead of "No Wi-Fi adapter was found".

## 0.8.4: per-module RAM fix, motherboard lighting kept after shutdown, Wi-Fi direction view

- **RAM Off per module:** Off is now written as Static black, which is how GCC does it. Before, a module left in the controller's own Off mode lit up again whenever the other module was applied.
- **RAM in step:** modules are written in one pass, with the apply bytes back to back, so their built-in effects start together. Sync frames are now written register by register across both sticks. Before, DDR5_A2 got its whole colour block before DDR5_B2 and ran visibly ahead.
- **Keep motherboard lighting after shutdown** (Lighting, options): saves the applied look in the board's lighting controller with GCC's own exit command (`CC 47 01`), so the board starts with it before Windows loads. Animated sync is kept as its closest built-in effect, never as one frame of the animation. Turning it off sends GCC's connect command (`CC 47 00`). The RAM is still set at sign-in: its only save rewrites the ENE controller's flash, which GCC never does.
- **Sync off no longer freezes an animation:** the devices switch to their closest built-in effects.
- **Wi-Fi Compass:** the 3D antenna view shows a petal for each measured position (length = strength, colour = quality), with an arrow and beacon on the strongest side.

## 0.8.3: GAME ON logo regression

Restored the controller initialization omitted by the zone-isolation change. Sora resets and restores the complete saved board state once before isolated edits, and again after resume or leaving a global effect. Later color changes target only selected zones. The user confirmed that initialization restored GAME ON, then an isolated blue test changed only the logo while the I/O cover retained its own rainbow. The logo was restored to red. See [the investigation](docs/lighting-zone-fixes.md).

0.8.2 also adds the prominent Wi-Fi-off warning and an **Open Wi-Fi settings** button, with automatic rechecking while the page is visible.

## What's new in 0.8.1: lighting isolation, recovery and Wi-Fi visuals

Wi-Fi Compass now has an interactive 3D ribbed antenna based on the supplied reference, a signal gauge, labelled history and four-position comparisons. The model is a manual reference; it does not claim to sense antenna orientation.

Lighting edits target the selected motherboard zones only, including Rainbow. Click a light to select it alone; Ctrl+click adds/removes lights. Independent Rainbow cycles the selected light through the spectrum; selecting all board lights enables GCC's spatial wave. **Sync RAM modules** independently controls whether sticks share a theme. Global memory + motherboard sync applies to both sticks and the board. Invalid hex input stays blocked across selection changes.

The elevated startup task starts in the tray 15 seconds after sign-in, with hardware recovery deferred another 10 seconds. Sleep/hibernate resume pauses/reopens hardware handles and restores applied profiles after a settling delay. A wake task also starts Sora if it was not running; an existing instance stays in the tray. Lighting and motherboard fan startup preferences remain available. Cooler output starts only with fresh sensor readings.

Applied GPU native profiles remain programmed after Exit and reapply after launch/resume. Software fan modes restore original control on Exit and reapply next launch. **Restore original** clears automatic GPU reapplication. Original backups remain available. Firmware Auto Fan Stop uses the card's existing thresholds; 0 RPM at low load is expected, and 50°C is not hardcoded.

## GPU fans, SSD tool and Wi-Fi antenna (0.8.0)

Three additional pages extend Sora's independent hardware tools. Open **GPU fans** with **Ctrl+5**, **SSD tool** with **Ctrl+6**, or **Wi-Fi antenna** with **Ctrl+7**. The existing cooler, keyboard, lighting and motherboard fan controls remain available at Ctrl+1–4.

The investigation uses the local [REA](https://github.com/morluto/rea) workflow and recovered GCC modules. Hardware readings and unsupported operations are reported explicitly; source analysis and live verification are documented separately in the tool reports.

**SSD tool:** physical disks, firmware/serial/volumes, TRIM support, Celsius/Fahrenheit temperature, NVMe endurance and complete SMART counters, ATA raw attributes and CSV export. No GCC storage DLL is required. Secure erase is unavailable; the installed GCC uses proprietary controller commands for eligible drives. See [the SSD report](docs/Gigabyte-ssd-tool.md).

**GPU fans:** automatic control, software duty curves, native three-point RPM curves, native fixed RPM, per-fan selection, Auto Fan Stop, named profiles, Sora/GCC profile import, current/factory curve reads and verified restoration. Native RPM programming is restricted to the recovered **GV-N507TWF3-16GD / RTX 5070 Ti** interface; other NVIDIA cards can use the independent driver duty interface when detected. Sora saves original settings before taking control. Named drafts require Apply; the last applied profile automatically returns at startup/resume. See [the GPU report](docs/Gigabyte-gpu-fan-control.md).

**Wi-Fi Compass:** scan per adapter and select a specific access point, inspect band/channel/radio/link rates, monitor signal for five minutes, or compare Front/Left/Back/Right antenna positions over 15 seconds each. The implementation uses Windows WLAN without GCC and keeps stale cached signals separate from fresh measurements. This PC's Wi-Fi radio was off during verification; adapter detection and the radio-off response were verified, while connected measurements require enabling and connecting Wi-Fi through Windows. See [the Wi-Fi report](docs/Gigabyte-wifi-compass.md).

Validation: **32 check groups pass**. Both installed NVMe drives return live SMART and manufacturer identity. A bounded native GPU test spun all three fans up and verified complete restoration of the original settings. It did not establish settled target RPM or a full thermal sweep. See [the integration report](docs/GCC-hardware-tools.md) for evidence and the remaining limits.

## What's new in 0.7.4: Apply feedback and mode switching

The fan Apply button shows **Applying…**, then disables as **Applied** after successful readback. Editing a fan setting enables it again; reverting the edit disables it. Display-name edits remain separate. Applying locks input without greying the page, rebuilding curve rows or resetting the scroll position.

Curve and stair-step selections update the graph immediately. Switching back from fixed duty restores the previous temperature points instead of retaining a flat readback line. Saved modes/points are retained when their compiled values match hardware readback, including after reopening the page. The mode explanation distinguishes smooth ramps, stair steps and fixed duty. This update changes editor behavior only; fan programming and RGB implementation are unchanged.

GCC is not required. When uninstalling GCC, make sure its EasyTuneEngineService component is removed too; merely deleting the GCC folder can leave a registered service. Sora handles the service being absent. Keep PawnIO installed.

## 0.7.3: Clear GCC takeover and verified RPM response

Fan changes blocked by another controller now show a warning above the editor. If **EasyTuneEngineService** is running, **Pause GCC fan service** is visible there; after pausing, click **Apply to fan**. Apply/Restore and speed tests remain disabled while a competing controller is active. GCC's previous service state is restored when Sora exits. The selected header has a blue outline distinct from hover.

A radiator-only 100% physical test on this PC increased reported RPM from **1,264 to about 2,177**, settling over roughly ten seconds. Original native/firmware settings were verified after restoration, the CPU pump configuration was untouched, and GCC's service returned to its previous running state. The user's earlier unsuccessful applies were logged as blocked before any hardware write. This verifies the full-speed response; it does not establish physical behavior over every curve/temperature.

## 0.7.2: Stable refresh and clear fan connections

The fan page updates live readings without disabling the header list or editor every three seconds. It keeps unsaved names, cursor selection and validation messages, queues Apply/Restore behind an active read, and retains live readings when a write is rejected. The list stays dark during actual operations too.

Connection types save independently of fan profiles and send no hardware commands. **Fixed-speed pump** shows RPM monitoring without irrelevant curve/sweep controls. For this PC's current wiring, CPU is labelled **LD360 pump** (fixed speed) and System 4 / Pump is labelled **Radiator fans** (Fan). Physical header names remain visible; labels and types can be changed when the AIO or wiring changes. This update changes no fan speeds, curves or RGB settings.

## 0.7.1: Independent motherboard fan control

**Fan control** (sidebar, **Ctrl+4**) adds an independent IT8696E integration for the **Z890 GAMING X WIFI7**: six headers, live RPM and temperatures, eleven-point hardware curves, stair steps, fixed duty with a full-speed temperature threshold, presets, temperature-source selection, hysteresis, native stop thresholds, firmware Zero-RPM, profile import/export, startup apply, original-setting backups and verified restore. Minimum-duty calibration measures running and starting duty separately; headers must be marked Fan, and pumps are excluded. **Measure RPM response** measures 100–40% duty and restores the previous setup. Existing RGB Fusion implementation files and saved lighting choices are unchanged.

**GCC removal is on HOLD pending the current recheck gates. Keep PawnIO installed.** Native control uses the signed PawnIO module; firmware Zero-RPM uses Sora's own fan-only WMI definition and Microsoft's existing provider. That interface has been registered and verified on this PC. No GCC DLL, driver or service is required. All six headers passed brief full-speed write/readback/restore tests; eleven-point curve/step programming and firmware stop configuration also passed with GCC's fan service stopped. Calibration has automated failure/cancellation checks but has not been physically run on this PC's unidentified fan/pump connections. Closed-loop target RPM and other board/controller families are outside this implementation; the recovered GPU/memory-temperature route does not support this board's IT8696E. See [the protocol and verification report](docs/Gigabyte-fan-control.md).

Each header also has a saved **Header display name**. Select a header, enter a label such as “Radiator fans,” and click **Save name**; **Reset** restores the original label. This changes only Sora's display name; the physical header remains identified in the detail line.

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
- Overclocking, undervolting, voltages and timings are not part of Sora. Fan control is a separate page introduced in 0.7.

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

The cooler-display integration controls only the LD360 segmented display and issues no pump-speed or firmware commands over its USB interface. Lighting and motherboard fan control have separate pages and transports. When readings become stale, Sora stops refreshing; the cooler's handling of its last displayed value depends on its firmware.

Sora is launched with `Sora.exe`; a folder publish also needs its accompanying libraries. Settings, keyboard backups and a bounded log are shared across releases in `%LOCALAPPDATA%\Sora\data`. Program Files packaging and its completed/pending checks are documented in [Program Files installation](docs/program-files-installation.md). The installer retains profiles and does not enable startup or launch hardware controls.

For development releases, `build.ps1` points `dist\Sora-current` at each new build, and `scripts\Install-Shortcuts.ps1 -Target <path to Sora.exe>` creates shortcuts. Builds refresh only shortcuts owned by that workspace; replacing a shortcut pointing elsewhere requires explicit `-ReplaceExisting`. **Start at sign-in** registers an elevated task for sign-in and wake recovery. From 0.10.7, normal launch only inspects that task. To select another copy, turn the setting off, then on in that copy. Actual task migration/rollback passed 43 checks on 0.10.7; normal installed host and Windows lifecycle remain open. Review 0.10.8 fixes lighting cleanup ownership and has a separate setup; it is not deployed. Older binaries retain their previous routing behavior. See [lighting lifecycle results](docs/lighting-exit-qualification.md). See [routing qualification](docs/startup-routing-qualification.md).

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
