# Gigabyte fan control in Sora 0.7.4

**Fan control** (**Ctrl+4**, or `Sora.exe --fans`) controls the six motherboard fan headers on the **Gigabyte Z890 GAMING X WIFI7 / ITE IT8696E**. This is a separate integration; RGB Fusion code and lighting preferences are unchanged.

Sora operates without GCC's executables, DLLs, drivers or services. Native register access uses the signed PawnIO module embedded in LibreHardwareMonitor. Firmware Zero-RPM uses Sora's own `root\WMI:Sora_GigabyteFanControl` definition, Microsoft's existing `WmiProv` provider and the motherboard's ACPI firmware. The independent definition was explicitly approved, registered and verified on this PC. Keep PawnIO installed when removing GCC.

All six headers passed brief 100% duty write/readback/restore tests. Eleven-point curve and stair-step programming, firmware stop/source/limit changes, and restoration were verified with GCC's fan service stopped. This establishes the recovered paths on this board, not compatibility with all GCC-supported hardware. Calibration has automated failure/cancellation coverage; its physical stop/start sweep has not been run on this PC's unidentified fan/pump connections.

## Use

Open Sora with its normal administrator prompt. GCC can be uninstalled; Sora does not recreate its service or change service startup settings. While GCC is still installed, exit its applications and other fan-control programs before applying settings. Sora refuses writes while EasyTuneEngineService runs. The optional **Pause GCC fan service for this session** button explicitly pauses that service and resumes it on exit, without changing its startup type; it disappears when the service is absent. Service ownership is saved so a subsequent Sora session can resume it after a crash.

Version 0.7.3 places the conflict warning and **Pause GCC fan service** action above the editor. Apply/Restore and speed tests are disabled while another fan controller is active. Pausing does not send the staged profile: click **Apply to fan** afterward. The footer distinguishes blocked writes from applied/read-back settings. RPM may take 10–15 seconds to settle. A blue selection outline distinguishes the current header from a hovered row.

After GCC and its fan service are removed, that service cannot block Sora. Some uninstall paths may leave components behind: check for EasyTuneEngineService rather than assuming that deleting GCC's folder removes a Windows service. Sora does not depend on that service or recreate it. Keep PawnIO installed.

Version 0.7.4 makes Apply reflect the selected header's pending fan edits. It shows **Applying…** while writing, then disabled **Applied** after success; unchanged initial state shows **No changes**. Edits enable Apply immediately, and reverting them returns to the clean state. Renaming a header uses its own Save name action. The page keeps normal colors, its existing row objects and scroll offset during writes while pointer/keyboard editing is locked.

Mode changes redraw the graph immediately: curves interpolate between points, steps hold duty until the next point. Fixed mode preserves the prior temperature points; returning to curve/step restores them. When a saved profile compiles to the observed native and firmware settings, the editor keeps its selected mode and points instead of replacing them with rounded or ambiguous readback. This matters because fixed duty and certain two-point steps have identical native representations. No hardware fan protocol or RGB implementation was changed for this editor update.

**Install standalone firmware fan support** explicitly registers the minimal Sora-owned WMI class if it is missing. Normal reading/applying never registers schemas. This setup adds no driver or service, and does not alter GCC's WMI classes. It is already installed on this PC. If Windows rebuilds its WMI repository, run this setup again. To remove only Sora's schema later, use Administrator Windows PowerShell after exiting Sora:

```powershell
$SoraFanSchema = [wmiclass]'\\.\root\WMI:Sora_GigabyteFanControl'
$SoraFanSchema.Delete()
```

Select a header, then choose a temperature curve, stair step, or fixed duty. Edit the table or drag graph dots. Quiet, Balanced and Performance are Sora presets, not claims of GCC's exact preset tables. Full speed requests 100%. A splitter may report the RPM of just one attached fan; 0 RPM alone does not establish that a header is unplugged.

**Header display name** lets you label each connection, for example “Radiator fans” or “AIO pump.” Click **Save name**; **Reset** returns to the physical header's original name. Names are saved in `data/fans.json`, appear in the header list and editor title, and survive restarting Sora. The physical header name stays in the detail line. Renaming sends no hardware commands and does not change fan curves or RGB settings.

In 0.7.2, **Connected device type** saves separately from the curve and does not require Apply. **Fixed-speed pump** displays live RPM and hides curve/sweep controls; saved startup curves are skipped for that connection. This PC is configured as CPU → **LD360 pump** (fixed-speed pump), System 4 / Pump → **Radiator fans** (Fan), according to the user's confirmed wiring. These are software labels and roles; wiring and controller registers are unchanged. Change the type/name when replacing or reconnecting the AIO.

Background reads no longer disable controls or clear keyboard selection. Unsaved names survive polls, header switches and closing to the tray. Invalid numeric feedback and operation results remain visible. Actions wait for an in-flight read; rejected applies retain the last valid snapshot. Failed telemetry reads clear live values until recovery. The list uses an explicit dark template even while disabled for a real operation. Non-finite duty input cannot reach graph rendering.

Choose among System 1, PCH, CPU, PCIEX16, VRM MOS and System 2. Hysteresis is the controller's temperature debounce. The stop threshold must be below the first curve point; 0 disables that native threshold. End a curve at 100%, between 50 and 95°C. Fixed duty retains a full-speed temperature threshold. Electrical PWM/DC selection stays as configured in BIOS.

**Firmware Zero-RPM** is the separate BIOS fan-stop function recovered from GCC. Stop and restart temperatures apply with the selected profile and thermistor. Restart must be 1–15°C above stop. Fixed duty disables this option. The native **Stop below** threshold and firmware stop are distinct mechanisms; setting native stop to zero disables only the native threshold. Keep both disabled for pumps unless their manufacturer explicitly supports stopping.

**Apply to fan** changes only the selected header. Before writes, Sora saves original registers in `data/fans-original.json` and original firmware settings in `data/fans-firmware-original.json`. Every configuration byte and firmware value is read back. Failed native/firmware writes or profile saves trigger rollback to the settings immediately before the apply. **Restore original** restores the saved native and firmware settings and removes that header's startup profile. `data/fans.json` holds choices; `data/sora.log` records errors. Keep both original-setting backups.

**Restore previous fan settings when Sora exits** is on by default and restores settings from immediately before this session's first change. Closing to the tray keeps profiles active. Disabling exit restoration leaves the controller curve running until another program or a board reset changes it. No BIOS/flash save is issued. A crash cannot execute restoration; the programmed hardware curve continues, including its full-speed threshold. Keep the backup and use Restore original after restarting if needed.

**Measure RPM response** steps the selected spinning fan from 100% down to 40%, waits four seconds per step, records RPM and restores its previous bytes in `finally`, including on cancellation/error. It requires CPU temperature below 80°C to start and aborts at 85°C, missing temperature, or missing/stopped RPM. Results are saved as `data/fan-response-<header>-<time>.json`. This does not determine minimum starting duty or calibrate closed-loop RPM. Keep pumps above their manufacturer's minimum duty.

**Calibrate minimum fan duty** requires a header explicitly marked **Fan**. Unknown devices and pumps are excluded. It sweeps down in 5% steps, using GCC's recovered 2-second settling at/above 60%, 5 seconds below and 400 RPM floor. It then establishes a stopped fan and measures starting duty separately, resetting to stopped between unsuccessful attempts. The result is measured in 5% steps and should be used with extra margin. If a fan never fully stops, starting duty is reported as unknown. Calibration disables firmware stop temporarily, backs up both interfaces, checks temperature/RPM each second, supports cancellation and forces 100% with a spinning check before restoring the original settings. CPU must initially be below 80°C; 85°C or missing readings aborts the sweep. Results are `data/fan-calibration-<header>-<time>.json`; calibration does not automatically replace the profile.

Import/export stages one JSON profile for the selected header. Import does not write hardware. Startup applies only saved profiles when explicitly enabled. Polling runs every three seconds while the page is visible; hardware curves need no tray control loop. A competing program can overwrite the controller; Sora does not fight it continuously.

## Recovered protocol

GCC health ID `0xA0060607` selects the first IT8696E only; its map disables H2RAM and second-controller paths for this board.

| Channel | GCC title | PWM control | Automatic block | Tachometer low/high |
| --- | --- | --- | --- | --- |
| 0 | CPU | `15` | `60–66` | `0D/18` |
| 1 | System 1 | `16` | `68–6E` | `0E/19` |
| 2 | System 2 | `17` | `70–76` | `0F/1A` |
| 3 | System 3 | `7F` | `78–7E` | `80/81` |
| 4 | CPU OPT | `A7` | `A0–A6` | `82/83` |
| 5 | System 4 / Pump | `AF` | `A8–AE` | `4C/4D` |

Addresses are hexadecimal, bank 0. Block fields: off temperature, start temperature, full-speed temperature, starting PWM, integer slope, temperature hysteresis, target zone. PWM is 0–255. PWM-control bit 7 selects hardware automatic control; bits **5:3** select the thermistor. Other control bits are preserved. Temperatures are at `29–2E`. RPM is `1,350,000 / (2 × counter)`; `FFFF` reports stopped and invalid/unstable counters report unavailable.

Register `06`, bits `6:5`, selects the bank. Other bits and the original bank are restored. Bank 2 holds the main slope's 1/8 fractional part at `1C + 10h × channel`, bits `6:4`. Nine extra segments allow eleven visible curve points, plus the separate off threshold: A–C in bank 2, D–I in bank 3. Each has a starting temperature, integer/fractional slope, absolute starting PWM and enable bit. Bank 3 packs two fractional slopes per byte. Unused segments are explicitly disabled. Stair-step slopes are zero; fixed duty disables all extra segments.

Sora uses PawnIO's signed **LpcIO** module embedded in LibreHardwareMonitor 0.9.6. It checks the SMBIOS board identity, unlocks `2E/2F`, verifies chip ID `8696`, reads LDN4's base address, discovers allowed ports and verifies vendor `90` plus the controller identity bit. No vendor DLL/driver, SMBus lighting access or HID lighting command is used.

The firmware ACPI GUID is `{DEADBEEF-2001-0000-00A0-C90629100000}`. Capability method 7 returns `0x2068F0` here; bit `0x4000` advertises firmware stop. Method IDs 291/292 read/write stop status, 295/296 read/write temperature source and 297/298 read/write off/on temperatures. Void setters legitimately return no .NET output object. Sora's embedded MOF declares only these fan methods and capability query. It prefers its own schema and can use the original `GSA1_ACPIMethod` as a fallback while installed. GCC's uninstaller is not needed for normal operation and was not run during validation.

Operations hold `Global\Access_ISABUS.HTP.Method`, `MBPnPModeMutex`, `IT86XXECMutex4BankSelection` and `IT86XXECMutex` with bounded acquisition. Locks, handles and bank state are released in `finally`. RPM measurement releases hardware handles between waits. Writes are limited to the six known fan register sets and each transaction is confined to one header.

## Evidence and validation

Used the existing [morluto/rea](https://github.com/morluto/rea) 6.5.0 checkout/CLI for build-identified CIL evidence and ILSpy for decompilation. Local evidence under `gcc-re/evidence/`:

- `rea-fan-native-boundaries.json`: low-pin-count assembly identity and native boundaries.
- `rea-fan-it8689xx-members.json`: register layouts, bank switching, masks and segments.
- `rea-fan-z890-map.json`: board-specific placement and controller selection.
- `rea-fan-curve-converter.json`: point-to-register conversion and fixed/step semantics.
- `rea-fan-zero-rpm-acpi.json`: additional firmware fan-stop interface.
- `rea-fan-regulator-capabilities.json`: per-controller temperature-source and mode dispatch.
- `rea-fan-calibration-preferences.json`: calibration defaults (60% boundary, 2/5-second settling times, 400 RPM floor).
- `fan-sha256.txt`: analyzed assembly hashes.

Key types: `IT8689XXBase`, `IT8689`, `Intel890SeriesFanControlPlacementManagement`, `Intel890SeriesTemperatureInputPlacementManagement`, `FanIT86xxCoordinateSystemRegulator`, `SmartGuardianFanConfigConverter`, `ITEConverter`, `GAS1AutoFanStopManagement`. No proprietary source/binaries are shipped.

`artifacts/fan-probe-admin.json` identifies first controller `8696` and no second controller. `artifacts/fan-native-probe.json` records six register snapshots and tachometers. These probes made no fan-configuration changes; register selectors and bank state were restored. Channels 0 and 5 reported about 3342 and 1138 RPM; the others reported 0. The initial decoded source field was corrected during development: source is bits 5:3, not 2:0; retained raw registers are authoritative.

`tests/Sora.Checks/fixtures/gigabyte-z890-fans.json` contains captured configuration bytes. Tests exercise six maps, three modes, nine segments, known source/slope bytes, unrelated-bit preservation, shorter-curve cleanup, malformed profiles/backups, other-header isolation, readback mismatches and rollback after failure at every write position. Existing RGB, ENE, keyboard and display checks run unchanged.

Physical evidence:

- `artifacts/fan-native-verification-approved.json`: all six headers at 100%, readback and exact restoration of every captured configuration byte.
- `artifacts/fan-firmware-standalone-verification.json`: Sora-owned WMI schema, safe firmware enable/source/off/on readback and restore, eleven-point curve/step readback and restore, six-header firmware comparison and loaded assemblies without GCC libraries. The test used off/on limits of 1/2°C while the live source was above them, so it tested firmware programming without deliberately stopping a device.
- `artifacts/fan-admin-lifecycle.json`: GCC service stopped during the test and restored to Running afterward; startup type untouched.
- `artifacts/fan-service-verification.json`: app-level native/firmware apply, persistent backups, explicit restore and exit restore in an isolated diagnostic data directory. `fan-service-lifecycle.json` records service state around that test.

Automated checks include injected native failures at every write position, firmware partial failures, readback mismatch, minimum running versus starting duty, cancellation, temperature abort, missing RPM and restart failure. WPF editor checks cover source display, draft preservation across headers, fixed-mode native/firmware stop handling, firmware source selection, pump-calibration exclusion, point-count limits, invalid input preservation and rejection of hardware writes in screenshot mode. Packaging results and screenshots are retained in `artifacts`.

The original fan-control package passed 29 automated checks and 11 WPF editor checks. Version 0.7.2 expands the editor checks to 30, including reads held across two real timer ticks, focus/selection preservation, queued actions, failure/recovery, name persistence and separate fixed-speed-pump metadata. Existing desktop and Start menu shortcuts point to `dist/Sora-0.7.2/Sora.exe`. Display settings, RGB preferences and the original-lighting backup are migrated without modification. RGB Fusion implementation files are unchanged. Screenshot diagnostics suppress startup hardware output and exit-lighting commands.

The 0.7.2 build passed **29 automated checks, all 30 editor checks and three live refresh checks**. A 12-second read-only check observed at least three complete refresh cycles with stable controls/text selection and unchanged native/firmware fan configuration (`artifacts/fan-0.7.2-live-refresh-verification.json`). Live pump, radiator and smaller-window screenshots are `sora-0.7.2-pump-live.png`, `sora-0.7.2-radiator-live.png`, and `sora-0.7.2-pump-small.png`. No service pause or fan/RGB configuration write was used for this update's live checks. Preserved settings/lighting hashes are in `artifacts/fan-0.7.2-data-integrity.json`.

On the user's report that applies did nothing, the 0.7.2 log showed all four attempts blocked by EasyTuneEngineService before any fan writes. The explicitly approved radiator-only test (`artifacts/fan-radiator-physical-response.json`) then paused that service, set channel 5 to 100% with the existing native backend, sampled RPM for twelve seconds and restored the original registers. RPM rose from **1,264** through **1,671 at 4 seconds** to **2,177 at 11 seconds**. Exact native/firmware settings were restored on all six headers, CPU pump configuration was untouched, and the service was restored to Running (`fan-radiator-response-lifecycle.json`). No RGB writes were sent. This proves physical full-speed response on the radiator channel; it is not a physical sweep of every curve, step, temperature or calibration behavior.

Version 0.7.3 adds editor regression checks for the conflict banner/pause action, blocked controls, recovery without lost edits and keeping the selected header visible when the list scrolls (34 total). Existing shortcuts now target `dist/Sora-0.7.3/Sora.exe`.

Version 0.7.4 passes **45 editor checks**. New checks cover Apply state/reversion, row identity and scroll preservation on successful completion, input locking without disabled page styling, immediate chart mode updates, different rendered pixels for curve versus step, and restoring saved temperature points after fixed-duty readback/reopening. The successful-completion and readback UI cases use isolated recorded data; no physical speed tests were repeated for this UI-only update. Shortcuts target `dist/Sora-0.7.4/Sora.exe`.

## Scope and limits

The 18 October recheck found DeepCool 1.2.14 restores stale fan settings on service Stop. Complete native/firmware preservation, controlled isolation and failed-probe recovery are now qualified separately. See [maintenance workflow and remaining uninstall/lifecycle gates](fan-maintenance-workflow.md). The standalone sensor verifier still refuses unsafe fan-helper Stop; service-state restoration alone is insufficient.

- GCC's `ChangeTemperatureInputSelection4GraphicsCards` explicitly rejects IT8696E; its external GPU/memory-temperature route accepts IT8795E, IT8791E and IT5701. This PC has none of those secondary controllers. Six native thermistor inputs match the recovered route for this board.
- GCC XML profile import, closed-loop target-RPM mode and other board/controller families are not implemented. The common GCC enum includes liquid/tachometer modes, but its recovered point-to-physical converter does not implement those branches; their enum presence alone does not establish board support.
- No physical minimum-duty stop/start calibration was run on unidentified connections, and no controlled heating test was used to verify each curve's physical thermal transitions. Configuration readback is distinct from measuring fan response across an entire temperature range.
- Unknown boards/chips are rejected. Duty percentage is not target RPM. PWM/DC selection is preserved from BIOS.
