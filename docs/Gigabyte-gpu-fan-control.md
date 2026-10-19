# Gigabyte GPU fan control

Sora adds independent NVIDIA fan monitoring, fixed duty, software temperature curves, and the recovered GCC **native three-point RPM curve / Auto Fan Stop** interface for the installed **GV-N507TWF3-16GD WINDFORCE RTX 5070 Ti**. It loads the NVIDIA driver’s system `nvapi64.dll`; it does not redistribute or load GCC libraries, install a service, or require GCC to remain installed.

Native programming is gated to NVIDIA PCI device `2C0510DE`, subsystem `41A81458`, three independently reported fan channels, successfully parsed firmware getters, available setter entrypoints, and the observed factory maximum of 3000 RPM. Other NVIDIA cards can expose the driver duty interface but do not inherit this card’s native RPM support. AMD and Intel GPU fan programming is unavailable.

## Use

Open **GPU fans** and read the driver. Each channel reports actual RPM, duty, control mode and allowed duty range. The native readout separately shows the current three RPM points and firmware Auto Fan Stop state for each of the three channels.

- **Automatic driver control** returns the duty controls to automatic and restores this session’s original native curve/stop settings when a session backup exists.
- **Native hardware RPM curve** edits exactly three RPM/temperature points, for one fan or all three. It preserves other channels’ native curves, fan-stop policies, and manual duty controls. These curves run on the GPU. The final point must request 3000 RPM at or below 85°C.
- **Native fixed RPM** requests the selected RPM through 84°C, with a hardware ramp to 3000 RPM at 85°C. It disables Auto Fan Stop for the selected channels. This explicit high-temperature ramp differs from GCC’s completely flat fixed curve.
- **Auto fan stop** uses the GPU’s existing firmware thresholds; Sora does not invent or change a separate stop/restart temperature. Disabled stop does not mean the driver’s minimum duty becomes zero.
- **Fixed duty** and **Software temperature curve** apply to all reported duty controllers. They require Sora to keep running, use the driver’s allowed 30–100% range on this card, request 100% at 85°C, and restore original control after missing temperature or a control failure. They do not emulate firmware zero-RPM by writing unsupported duty zero.

Switching from a software controller to a single-channel native controller requires restoration first, so other software-controlled channels cannot silently lose their temperature response.

**Stage factory curve** reads the actual driver factory curve into the editor; **Load current hardware curve** stages the selected channel’s observed curve. Neither button programs hardware. Apply is explicit. The graph uses RPM for native profiles and percent for software profiles. Invalid numeric edits prevent Apply and export instead of sending previous values.

Named profiles are stored per GPU in `data/gpu-fans-profiles.json`; saving, selecting and deleting profiles only changes the library/editor. Sora also imports/exports its JSON profiles. GCC `GFans` / `vfan.dat` JSON arrays can be imported by explicit `FanIndex`; current installed GCC profile order is 2, 1, 0. When a GCC file contains different per-channel curves, select an individual fan before importing. Sora rejects a partial or differing file when **All three fans** would lose the distinctions. Only single-channel and all-three native selections are represented. GCC export is not implemented.

## Backup and lifecycle

Before its first write, Sora atomically saves the original raw driver control, native RPM curve, and native Auto Fan Stop buffers to `data/gpu-fans-original.json`, keyed to PCI/bus identity. The native structure versions, complete channel count, channel IDs, RPM/temperature values and policy bits are validated. Writes are read back. Partial curve/stop/duty failures trigger rollback; restoration failures remain visible and retain the backup.

**Restore original** restores the complete original state, verifies it, and clears automatic reapplication. In 0.8.1, native RPM profiles stay programmed on normal exit; their original-state backup is retained. Software duty/temperature modes safely restore original control on exit while preserving the desired profile for the next launch. Closing the window keeps host-owned controllers running in the tray. Normal startup and resume recover the exact adapter's original backup first, then reapply the last applied profile. Named drafts still require Apply. Failures retain recovery files and log bounded retries. Process termination cannot guarantee immediate restoration.

Firmware Auto Fan Stop controls idle fan stopping with the card's existing firmware thresholds. Gigabyte describes the observed model's 3D Active Fan as semi-passive at low load; it does not specify a universal 50°C switching point. Sora reports zero RPM as a normal stopped state when firmware stop is enabled, rather than forcing an unsupported zero-duty request. Source: https://www.gigabyte.com/Graphics-Card/GV-N507TWF3-16GD

The 0.8.1 lifecycle suite uses an injected memory-only driver, with no hardware writes. It exercises native retention, software exit restoration, exact-identity startup recovery, suspend/driver reset, repeated resume, corruption, cancellation and rollback. These tests do not constitute a physical sleep/hibernate test of this PC. Evidence: `artifacts/gpu-fan-lifecycle-check.json`.

Sora blocks new writes while known competing controller applications such as GCC, MSI Afterburner, Precision X or FanControl run. It does not terminate them or change service startup settings. Detection is a known-process check, not proof that no other controller exists.

## Recovered implementation

The relevant screenshot UI is `ucVga.dll`, not just the older `Gigabyte.GraphicsCard.dll` fan wrapper. Its `FanControl.SettingApply` dispatches `FanApi.SetFanSetting` to `GvDisplayA.dll`. GCC AUTO/Performance and Customize all explicitly program the stored RPM curve and fan-stop policy. MANUAL produces a flat native RPM curve and disables fan stop. The UI can reset NVIDIA defaults before applying its stored AUTO profile. Importing GCC AUTO/Performance into Sora therefore stages its stored native curve instead of discarding it.

`GvDisplayA.dll` has SHA-256 `156fe5f42737b26a8ca3544f6307ec031ebfc63d4dc4fb083fbce46210377163`. Export stubs dispatch through per-adapter C++ vtables. RTTI identifies `CNvDisplayAm`, with these recovered paths:

| Interface | Native entry / dispatch | NVIDIA query ID | Buffer |
|---|---|---|---|
| RPM curve capabilities | `1800032F0` | `52B76D12` | 76 bytes, version 2 |
| Get current/factory RPM curves | `GvGetFanCurve` → vtable +`180` / `CNvDisplayAm 1801DEA80` | `E543C540` | 220 bytes, version 2 |
| Set RPM curves | `GvSetFanCurve` → vtable +`188` / `1801DE530` | `C181947A` | 220 bytes, version 2 |
| Auto-stop capabilities | `1800037D0` | `DDDFDA38` | 1316 bytes, version 1 |
| Get Auto Fan Stop | `GvGetZeroFan` → vtable +`190` / `1801DE310` | `600F612E` | 292 bytes, version 1 |
| Set Auto Fan Stop | `GvSetZeroFan` → vtable +`198` / `1801DE110` | `44CD3014` | 292 bytes, version 1 |

Addresses are preferred-image VAs for that exact binary and are not used at runtime. Sora queries the driver by ID and invokes `(physicalGpuHandle, buffer)` with the recovered calling convention.

The 220-byte curve contains a full DWORD count at offset 4, four 52-byte slots starting at offset 8, a fan index at slot +12, and three 12-byte points at slot +16. A point contains temperature in Q8.8, duty fraction in Q16, and explicit RPM. The last DWORD’s bit 0 selects factory values on the getter. In a getter request, byte 4 is a requested count, not a fan selector; zero requests all channels. Sora preserves unrelated/reserved bytes and unused slots. GCC derives Q16 from an integer percent using C# `Math.Round` (ties to even); explicit RPM remains separate. The stop buffer contains count at offset 4 and `{index, flags}` pairs at offset 36, stride 8; bit 0 enables stop. Duty cooler IDs 1/2/3 map to native curve indices 0/1/2 on this exact card.

GCC profiles are plain JSON, not encrypted blobs: `GvProfile/41A80_GFans.dat` stores `Profiles` and `Select`, while `41A80_GFans_Profile 1.dat` stores `FanSetting[]`. The recovered `GvComApp.ProfileUtil` generates the first available `Profile N` name; its code declares no fixed three-profile limit. Each fan setting includes `FanIndex`, curve `Speed/Rpm/Temperature`, `Stop`, `Speed`, `Mode`, and `IsSelect`.

## Evidence and verification limits

REA 6.5.0 from the existing [morluto/rea](https://github.com/morluto/rea) checkout/package and ILSpy recovered managed semantics. GNU objdump and the existing RTTI parser recovered the native driver boundary without executing GCC code. Retained evidence:

- `gcc-re/evidence/rea-gpu-ucvga-fan.json`, `rea-gpu-ucvga-native-boundaries.json`, `rea-gpu-nvidia-fan.json`, `rea-gpu-gvdisplay-layout.json` bind managed/native observations to installed binary digests.
- `gcc-re/evidence/gpu-gvdisplay-*.txt` retain exports, RTTI/vtables and focused disassembly; `gpu-gcc-profile-util.cs` retains the profile storage reconstruction.
- `artifacts/gpu-nvapi-native-readonly.json` records actual independent driver getters: three cooler IDs, 30–100% allowed duty, automatic control, current/factory RPM curves and Auto Fan Stop. At capture, all three channels were stopped at approximately 46°C; factory points were 55°C/998 RPM, 76°C/2400 RPM and 84°C/3000 RPM.
- `tests/Sora.Checks/fixtures/gigabyte-5070ti-fans.json` contains the actual original native buffers. Tests cover decoding/encoding, selected-fan isolation, unrelated manual control preservation, count/version/policy validation, fixed thermal ramp, firmware stop, installed GCC profile import and rollback after each native write stage.

The elevated bounded physical test in `artifacts/gpu-native-write-verification.json` accepted a native 1500 RPM target for all three channels, read back the complete target curve/stop/control state, observed all three fans start, and restored the exact original raw configuration. Over the seven-second observation the fans rose from 0 RPM to approximately 1058 / 1033 / 1019 RPM while the GPU remained at 46°C. This short test did not establish that they settled at 1500 RPM. Original 55°C/998 RPM, 76°C/2400 RPM and 84°C/3000 RPM curves and enabled Auto Fan Stop were verified after restoration. The integrated suite passed 32 checks, including the GPU protocol/transaction fixtures.

The earlier medium-integrity attempt returned `NVAPI_INVALID_USER_PRIVILEGE (-137)`; its preserved evidence is `artifacts/gpu-native-permission-denied.json`, and a separate read-only comparison confirmed that the saved original configuration was unchanged. Normal Sora elevation supplied the required privilege for the successful test; no Windows security settings were changed.

Read-only driver probing establishes the getter ABI and this card’s reported capabilities. Automated tests add simulated partial-write, readback and restoration failures; the physical test establishes bounded target programming, fan response and exact restoration on this card. No full thermal sweep, prolonged stress test, native curve dragging UI, firmware update, GPU overclocking, AMD native fan reconstruction or cross-card compatibility claim is included here.
