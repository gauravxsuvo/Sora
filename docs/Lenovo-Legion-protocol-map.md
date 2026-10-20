# Lenovo Legion protocol map from upstream source

This work uses local repository source; no Legion laptop is available or required for the current development stage. Hardware qualification is a later stage. The entries below describe observed upstream implementations, not universal Lenovo support.

Pinned checkouts:

- `vendor/lenovo-legion/LenovoLegionToolkit`: `d0c57bfc7ecc26d4ccf15bb3835fed7ac28a259f`
- `vendor/lenovo-legion/LenovoLegionLinux`: `b055cec8e713cd62fc5aaa8085ec67c5ac57c31e`
- `vendor/lenovo-legion/L5P-Keyboard-RGB`: `6e54bd6bc3b91c6ad5ab0ad95887adec5e723bc7`

The local vendor directory is ignored by Git. URLs, revisions and license-file hashes are in [the source inventory](lenovo-legion-sources.json).

## What Sora can build now

Protocol constants and packet codecs, request planning, native transport adapters, feature detection, state machines, error handling and UI can be developed using source references and simulated transports. Physical testing determines which model/firmware combinations can be advertised as hardware-qualified; it does not block implementation.

The first pure codecs are implemented in `src/Sora.Core/LenovoLegionProtocol.cs`. They encode four-zone RGB and fan tables, map firmware power values and decode/plan legacy charging requests. They do not open devices or activate controls. Their tests use source-reference vectors, not hardware captures.

## Windows control architecture

| Feature | Upstream implementation | Communication | Sora direction |
| --- | --- | --- | --- |
| Power presets | `Features/PowerModeFeature.cs`, `System/Management/WMI.LenovoGameZoneData.cs` | WMI `root\WMI`, `LENOVO_GAMEZONE_DATA` | Typed support/read/set calls, AC rules and BIOS quirks |
| Conservation/rapid charge | `Features/BatteryFeature.cs`, `Features/AbstractDriverFeature.cs`, `System/Drivers.cs` | `DeviceIoControl` against `\\.\EnergyDrv` | Ordered DWORD commands with initial state and readback |
| Four-zone RGB | `Controllers/RGBKeyboardBacklightController.cs`, `System/Devices.cs`, `Native.cs`; L5P `driver/src/lib.rs` | HID feature report, 33 bytes | Exact report codec and descriptor-based controller selection |
| Spectrum RGB | `Controllers/SpectrumKeyboardBacklightController.cs`, `System/SpectrumDeviceFactory.cs`, `Native.cs` | Separate 960-byte HID feature request/response protocol | Compatibility exchange, key-page discovery, profiles and effects |
| GPU working mode | `Features/Hybrid/*.cs` | Gamezone WMI, capability WMI, legacy feature flags; BIOS path where applicable | Composite state machine with explicitly selected backend |
| Advanced power limits | `Controllers/GodMode/*`, `System/Management/WMI.LenovoOtherMethod.cs` | WMI feature IDs plus capability metadata | Range/step/default-aware controls, custom-mode dependency |
| Fan curves | `Structs.cs` (`FanTable`), GodMode controller, `WMI.LenovoFanMethod.cs` | WMI `LENOVO_FAN_METHOD`, packed table | Table codec, validation, transaction/readback and model-specific endpoints |
| Hotkey mode changes | `WMI.LenovoGameZoneThermalModeEvent.cs`, `Listeners/ThermalModeListener.cs` | WMI event subscription | Update state and synchronize Windows settings without feedback loops |

Paths in this table are relative to Toolkit's `LenovoLegionToolkit.Lib`, except the explicitly named L5P path.

## Firmware power modes

Toolkit's `PowerModeFeature` uses `AbstractWmiFeature` with an offset of one. Its internal UI enum is not the firmware value:

| Mode | Toolkit UI enum | Firmware input/output |
| --- | --- | --- |
| Quiet | 0 | 1 |
| Balanced | 1 | 2 |
| Performance | 2 | 3 |
| Extreme | 223 | 224 |
| Custom / GodMode | 254 | 255 |

Calls use `IsSupportSmartFan`, `GetSmartFanMode` and `SetSmartFanMode`, with `Data` for the value. `GetThermalMode` is a separate observation. The `LENOVO_GAMEZONE_THERMAL_MODE_EVENT` event's `mode` field uses firmware-style values; it must not be decoded as the UI enum.

Toolkit discovers allowed modes through a supported-mode capability, with a fallback to `GetSupportThermalMode`. The capability bit positions are 0, 1, 2, 4 and 16 for Quiet, Balanced, Performance, Extreme and Custom. Smart-fan and Legion Zone versions participate in selecting custom-mode support. The presence of a numeric mode in an enum alone is insufficient to offer it.

The examined power implementation restricts high-performance/custom/extreme transitions on battery unless explicitly overridden. It also routes some transitions through an intermediate mode for BIOS families `J2CN` and `K1CN`, with delays and suppression of expected self-generated events. These are upstream workarounds to preserve in the implementation design, not evidence to apply to every model.

## Energy Management charging

In the examined Windows `BatteryFeature`, the transport is the Lenovo Energy Management device, not a ThinkPad charge-threshold API and not the Gamezone power method.

| Field | Value |
| --- | --- |
| Device | `\\.\EnergyDrv` |
| Charge-mode IOCTL | `0x831020F8` |
| State query input | DWORD `0x000000FF` |
| Conservation flag | response bit `0x20` |
| Rapid-charge flag | response bit `0x04` |

The response decoder checks conservation first. If neither flag is present, it reports normal charging. This decoder alone does not validate that the driver request succeeded; the transport must verify success and output size first.

| Desired mode | First DWORD request | Second DWORD request |
| --- | --- | --- |
| Normal | `0x05` | `0x08` |
| Conservation | `0x08` | `0x03` |
| Rapid charge | `0x05` | `0x07` |

These are separate ordered IOCTL calls, not a single two-DWORD buffer. Toolkit also reads back charging state and mirrors the actual mode into a Vantage registry preference. Sora should distinguish device state from optional vendor-preference synchronization rather than assume the registry value programs the hardware.

Night charging is a different feature/IOCTL (`0x83102150`) and must not be folded into conservation mode. Source files document query `0x11`, enable `0x80000012` and disable `0x12`; its response validity/state bits are separately interpreted.

## Four-zone RGB format

The L5P source identifies the relevant interface with vendor `048d`, a known product ID, usage page `ff89` and usage `00cc`. Toolkit's four-zone discovery also checks for a 33-byte feature report within the `c9xx` product family. Neither heuristic should become a brand-wide support rule.

| Byte offset | Meaning |
| --- | --- |
| 0–1 | `CC 16` header/report bytes |
| 2 | Effect: static `01`, breathing `03`, wave `04`, color cycle `06`; Toolkit's off state uses `00` |
| 3 | Speed, 1–4 for the L5P effects |
| 4 | Brightness, 1–2 for enabled L5P effects |
| 5–16 | Four RGB triples, used by static/breathing |
| 17 | Padding |
| 18 | Right-wave flag in the L5P implementation |
| 19 | Left-wave flag in the L5P implementation |
| 20–32 | Zero padding |

Both wave directions use wire effect `04`; direction comes from the flags. Enabled animated effects do not necessarily use the zone colors. The transport is a HID **feature report**, not a normal output report or keyboard key event.

The Rust project provides explicit device identities across several years. Toolkit uses broader masked matching and a descriptor-size check. Sora can keep a source-supported identity table and model/controller quirks instead of inventing a year from a purchase date.

## Spectrum / per-key RGB

This is a different protocol. Toolkit uses vendor `048d`, a 960-byte feature report and model-dependent masked product families (`c1xx`, `c9xx`, `c6xx`). A compatibility request/response exchange is part of initialization.

The common four-byte header consists of report ID `07`, operation, a size byte and trailing `03`. Fixed request structs inspected in `Native.cs` use size `C0` within a 960-byte report; do not infer that the header byte represents the entire buffer length.

| Operation | Code |
| --- | --- |
| Compatibility | `D1` |
| Key count / key page | `C4` / `C5` |
| Set profile / reset profile / get profile | `C8` / `C9` / `CA` |
| Set effects / get effects | `CB` / `CC` |
| Get brightness / set brightness | `CD` / `CE` |
| Aurora start/stop / bitmap | `D0` / `A1` |
| Get logo / set logo | `A5` / `A6` |

Key pages carry key codes; Aurora items associate a 16-bit key code with RGB. Build layout discovery and response validation before per-key effects. Toolkit's cached-device responsiveness check itself sends a feature request, so it should not be copied into Sora's passive device enumeration path.

## GPU working modes

Toolkit combines GSync and iGPU state into Hybrid On, iGPU-only, Auto and Off. Here, the Gamezone GSync state is used in the firmware switching implementation; it should not be exposed as an ordinary display G-SYNC toggle in Sora.

The default iGPU backend uses `GetIGPUModeStatus` / `SetIGPUModeStatus` after `IsSupportIGPUMode`. Alternative paths use `LENOVO_OTHER_METHOD` capabilities or feature flags. The selected code does not simply choose an arbitrary fallback on every failure: experimental selection and the detected feature source matter.

Capability `IGPUMode` is `0x00010000`; `IGPUModeChangeStatus` is `0x000F0000`. Values are Default `0`, iGPU-only `1`, Auto `2`. The capability path checks change status after setting. The feature-flags path uses a different operation/return contract. Deferred dGPU notification and external-device behavior make this a state machine, not a single register write.

There is also a BIOS `GraphicsDevice` route for applicable machines and UMA support. It is not a universal Legion requirement. Keep BIOS changes separate from runtime mode switching.

## Custom limits and fan tables

Toolkit's OtherMethod wrapper uses `GetFeatureValue(IDs)` and `SetFeatureValue(IDs, value)`. Capability metadata supplies supported IDs and ranges/defaults/increments. The GodMode platform configuration also applies capability-ID masks, including `0xFFFF00FF`, before some operations. Do not substitute a constant power limit for the capability metadata.

Toolkit has a legacy custom-mode platform and a newer Legion platform. Compatibility combines allowed modes, smart-fan/Legion Zone versions, model information and BIOS thresholds. The Linux driver independently models access methods per feature: WMI, ACPI and EC are not interchangeable transports across every machine.

The serialized `FanTable` request in the inspected Toolkit is 64 bytes:

| Offset | Encoding |
| --- | --- |
| 0 | Table mode `01` |
| 1 | Table selector `00` |
| 2–5 | Little-endian 32-bit count `10` |
| 6–25 | Ten little-endian 16-bit raw fan levels |
| 26–63 | Zero padding |

`Fan_Set_Table` takes that byte array. `Fan_Get_Table(FanID, SensorID)` returns a table and size. These raw levels are not a temperature/RPM-pair format and must not be relabeled as arbitrary percentages. Sensor/fan mappings and mode filtering vary by platform.

Toolkit's examined verifier writes then reads twice, with a short delay between reads, retrying a limited number of times. Its use of `(FanID=1, SensorID=1)` in that verifier is an upstream assumption to assess, not a universal endpoint definition. Some full-speed controls use a capability while others use `Fan_Get_FullSpeed` / `Fan_Set_FullSpeed`.

## Implementation and verification boundaries

| Area | Can proceed without hardware | Later physical evidence |
| --- | --- | --- |
| Packets / enums | Source-reference vectors, bounds and endian tests | Actual accepted exchanges |
| Transport | Native API adapter, fake adapter, failure/cancellation tests | Driver/provider behavior on models |
| Feature logic | Capability parsing, request ordering, state/readback handling | Firmware state changes and recovery |
| Compatibility | Source-derived tables, schema/descriptor fixtures | Exact supported model/BIOS matrix |
| UI | Capability fixtures and preview states | Correct runtime state and ownership behavior |

Next development step: implement injectable WMI/driver/HID adapters and feature logic using this map. Keep physical qualification separate from implementation status. A request for a laptop report is not a prerequisite for continuing source-led work.
