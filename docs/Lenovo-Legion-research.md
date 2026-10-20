# Lenovo Legion integration research

Research date: 20 October 2026. Initial target: Legion laptops running Windows 11. No Legion laptop is available. The active workflow uses upstream source to implement and test protocol logic offline; physical qualification is a separate later stage. The earlier 2023 purchase reference does not identify a test device. Legion desktops and Legion Go require separate qualification.

This research now supports an experimental native backend for power, charging, GPU configuration, four-zone RGB and Spectrum brightness/profiles. Implementations are source-tested and not physically qualified. See [current coverage](Lenovo-Legion.md), the [source audit](Lenovo-Legion-source-audit.md), [source inventory](lenovo-legion-sources.json) and [diagnostic instructions](Lenovo-Legion-diagnostics.md).

## Recommended approach

Study existing source and documented firmware interfaces first. Reuse compatible implementations with attribution, and investigate vendor software only for gaps. Sora still needs its own integration, capability detection and hardware validation. An upstream supported-device list is evidence to investigate, not proof that Sora supports those devices.

Sora currently targets Windows/.NET 8. Its README already limits controls by device and interface; Lenovo should follow that pattern. Existing sensor support should be reused rather than replaced.

## Repository shortlist, in research order

| Priority | Project | What to inspect | License/status observed | Intended use |
| --- | --- | --- | --- | --- |
| 1 | [LenovoLegionToolkit-Team/LenovoLegionToolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit) | Windows power/charging controls, GPU modes, lighting, events and capability checks | GPL-3.0; maintained community continuation | Main Windows reference; evaluate a licensed port of the required backend components |
| 2 | [johnfanv2/LenovoLegionLinux](https://github.com/johnfanv2/LenovoLegionLinux) | Fan curves, power limits, firmware/EC research, supported-model differences; start in `kernel_module` and `doc` | GPL-2.0 repository label; inspect exact file terms before reuse | Cross-check protocol assumptions; Linux backend reference for later |
| 3 | [4JX/L5P-Keyboard-RGB](https://github.com/4JX/L5P-Keyboard-RGB) | Four-zone keyboard device IDs, reports and effects; start in `driver` | GPL-3.0; Windows and Linux | Focused four-zone lighting reference, separate from Spectrum per-key lighting |
| 4 | [ViRb3/LenovoController](https://github.com/ViRb3/LenovoController) | Legacy Energy Management driver transport; start in `source/Providers/DriverProvider.cs` and `source/Features` | MIT; archived in August 2024 | Small reference for older driver-based controls, only if applicable to the test model |
| 5 | [BartoszCichecki/LenovoLegionToolkit](https://github.com/BartoszCichecki/LenovoLegionToolkit) | Historical implementation and behavior before the community continuation | Archived original project | Compare historical behavior when investigating older firmware; avoid duplicate implementation work |
| 6 | [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) / [GitHub mirror](https://github.com/CalcProgrammer1/OpenRGB) | Device-specific lighting detectors, controller implementations and SDK protocol | GPL-2.0 repository label; file terms need checking | Secondary lighting reference; confirm an exact controller match before selecting it |

The Toolkit README reports support spanning multiple Legion generations, but individual capabilities vary. It also distinguishes Lenovo firmware power modes from Windows power plans, and firmware GPU working modes from Advanced Optimus. Preserve these distinctions in Sora. [Source](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit)

LenovoLegionLinux describes reverse engineering of ACPI firmware and the embedded controller. Its Linux kernel module is not a Windows component that can simply be dropped into Sora. [Source](https://github.com/johnfanv2/LenovoLegionLinux)

The keyboard project lists four-zone devices across 2020–2024. Examples include vendor `048d` and 2023 product IDs `c985` (Pro) and `c984` (regular/Slim). These are candidates to compare with enumeration, not a complete or universal Lenovo keyboard identifier. [Source](https://github.com/4JX/L5P-Keyboard-RGB)

## First source-reading targets

In the maintained Toolkit, inspect:

- [`LenovoLegionToolkit.Lib/System/Management`](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/tree/master/LenovoLegionToolkit.Lib/System/Management): WMI wrappers and firmware interface handling.
- [`LenovoLegionToolkit.Lib/Features`](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/tree/master/LenovoLegionToolkit.Lib/Features): per-feature support and control behavior.
- [`LenovoLegionToolkit.Lib/Controllers`](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/tree/master/LenovoLegionToolkit.Lib/Controllers): `GodMode`, `RGBKeyboardBacklightController.cs`, `SpectrumKeyboardBacklightController.cs`, GPU and Windows power controllers.
- [`LenovoLegionToolkit.Lib/Listeners`](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/tree/master/LenovoLegionToolkit.Lib/Listeners): state changes and event handling.

These are reading targets, not a completed audit of their implementations. Pin a source revision before importing anything; record the original path, modifications and applicable license in the third-party notices.

## Software to inspect when source leaves a gap

| Software/package | Research purpose | When to prioritize |
| --- | --- | --- |
| Lenovo Vantage and its installed hardware-control components | Observe the settings actually exposed on the test laptop; trace calls for uncovered features | First OEM baseline for the 2023-purchase laptop if it uses Vantage |
| Legion Space for laptops/desktops | Compare newer hardware settings and interfaces | Later generations, or if already installed on the target |
| Legion Zone | Compare region-specific controls | Only if the target model/region uses it |
| Lenovo Energy Management driver | Investigate driver-backed controls | When the target exposes that driver/interface |
| Lenovo Vantage Gaming Feature Driver | Check model-specific dependency and interface definitions | When the model support page or observed behavior requires it |
| Lenovo Hotkeys | Understand event delivery and OSD ownership | When reconciling Fn-key changes with Sora state |
| Corsair iCUE | Research older Corsair-controlled Legion lighting | Only after identifying a Corsair lighting controller |
| LegionFanControl | Compare fan behavior, curve limits and restoration | Secondary behavior reference for an explicitly supported model |

Lenovo publishes a distinct [Legion Space package for laptops and desktops](https://support.lenovo.com/na/en/downloads/ds572220); do not assume every Legion Space package targets Legion Go. Lenovo also provides [model-specific Gaming Feature Driver packages](https://support.lenovo.com/pr/en/downloads/DS556754), so select packages from the exact machine's support page.

Toolkit documents that some generations need Lenovo drivers even though the Vantage UI is unnecessary. It also excludes lighting that requires iCUE. Removing a vendor UI does not establish that every underlying driver can be removed. [Source](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit)

[LegionFanControl's site](https://www.legionfancontrol.com/) describes custom curves and restoring stock curves on exit. The [similarly named GitHub repository inspected](https://github.com/HellOwhatAs/LegionFanControl) contains an installer archive and README, not an inspectable implementation. Do not classify that repository as reusable open source.

## Communication map

| Area | Established reference | What Sora must determine on the actual model |
| --- | --- | --- |
| Performance profiles and thermal-mode events | `LENOVO_GAMEZONE_DATA` WMI interface | Class availability, method schema, supported modes, return semantics and event delivery |
| Custom power limits and some fan capabilities | `LENOVO_OTHER_METHOD` and capability data blocks | Available feature IDs, bounds, units, increments, dependencies and model quirks |
| Legacy driver transport | LenovoController opens the `EnergyDrv` device | Whether the installed driver supports the required operation; inspect each IOCTL separately |
| Four-zone RGB | L5P-Keyboard-RGB's device table and driver source | VID/PID, interface/usage, report sizes and exact report protocol |
| Spectrum/per-key RGB | Toolkit's Spectrum controller | Controller identity, layout and supported effects; separate protocol from four-zone lighting |
| GPU switching | Toolkit's GPU feature/controller implementation | Firmware mode versus driver/display mode, restart requirements and external-display behavior |
| Battery conservation and rapid charge | Toolkit's feature implementations | Exact transport and allowed combinations on this machine; do not substitute ThinkPad threshold commands |

The upstream Linux kernel documents Gamezone GUID `887B54E3-DDDC-4B2C-8B88-68A26A8835D0` and thermal event GUID `D320289E-8FEA-41E0-86F9-911D83151B5F`. Custom tuning depends on the active firmware profile. Some firmware has mode-specific bugs requiring quirks. [Gamezone documentation](https://docs.kernel.org/wmi/devices/lenovo-wmi-gamezone.html)

The Other Mode GUID is `DC2A8805-3A8C-41BA-A6F7-092E0089CD3B`. Its documented feature operations and capability blocks are useful protocol references, including attribute bounds and increments. Linux sysfs paths are Linux interfaces; Windows should inspect the corresponding WMI schemas rather than assume those paths exist. [Other Mode documentation](https://docs.kernel.org/wmi/devices/lenovo-wmi-other.html)

The legacy `EnergyDrv` handle is directly visible in [LenovoController's provider source](https://github.com/ViRb3/LenovoController/blob/master/source/Providers/DriverProvider.cs). This establishes a transport, not universal support for any particular command.

## Proposed implementation sequence

1. **Identify and inspect.** Collect manufacturer, product name, SMBIOS model/version, BIOS version, CPU/GPU identities, keyboard HID interfaces and relevant installed driver versions. Enumerate applicable WMI class schemas and read-only capability data. Exclude serial numbers and UUIDs from shared diagnostics.
2. **Build a capability report.** Record each feature as unavailable, readable, experimentally writable or qualified. A Lenovo brand match alone must not enable controls. Separate absent interfaces, missing drivers, access errors and unsupported firmware.
3. **Add basic controls.** Start with firmware power presets and battery conservation if supported. Read initial state, change one setting and verify readback and visible behavior. Reconcile changes made through Fn+Q and the OEM interface.
4. **Add keyboard lighting.** Select the backend from the observed controller identity. Test on/off, brightness, static colors and original-state restoration before effects.
5. **Add advanced controls.** Qualify GPU switching, custom limits and fan curves independently. Use firmware-reported limits, preserve automatic fan control as a recovery path and verify sleep/resume and application-exit behavior.
6. **Expand coverage.** Publish an exact model/BIOS/feature matrix. Add another generation only with corresponding evidence. Legion towers, LOQ, ThinkPad and Legion Go get separate qualification.

For uncovered behavior, record one OEM UI action at a time and inspect its WMI, driver or HID exchange. Use WMI tracing for WMI calls, USBPcap/Wireshark for USB reports where applicable, and targeted static analysis for service/driver call paths. Process Monitor can help locate components and device access but is not sufficient to decode every payload. Record request, response, feature state, firmware version and recovery behavior in a protocol note before implementing it.

## Open source preparation

Sora's original source is now licensed under GPL-3.0-only in the root LICENSE. Preserve upstream notices, license texts and applicable source obligations when reusing third-party implementations. Verify exact file terms and dependency licenses at the pinned revisions; repository labels alone are insufficient. Do not presume that separating code into a plugin automatically resolves compatibility.

Keep proprietary Lenovo/iCUE binaries and artwork out of Sora's source and release package unless separately authorized for redistribution. User-installed drivers may remain runtime prerequisites for particular models. Record those prerequisites explicitly.

## Completion criteria for the first Legion model

- Exact model and BIOS identified; read-only report reviewed.
- Every exposed control backed by an observed capability and a documented implementation source.
- Power/charging and applicable keyboard controls verified on hardware, including readback and restoration.
- Advanced fan/power/GPU controls enabled only after separate validation.
- Competing-controller ownership handled and state refreshed after resume or external changes.
- Source revisions, reuse decisions and notices recorded; compatibility matrix updated.

Current result: source inventory, metadata diagnostics, codecs, native adapters, transactions and the Legion UI are implemented for the features listed in [coverage](Lenovo-Legion.md). Custom tuning and full per-key editing remain future work. The [protocol map](Lenovo-Legion-protocol-map.md) traces the source implementations. Physical validation is still pending, independently of source-led development.
