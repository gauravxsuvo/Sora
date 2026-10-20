# Lenovo Legion support plan

Created: 20 October 2026

## Objective and initial scope

Add Lenovo Legion laptop controls to Sora on Windows 11, reusing existing open source implementations and documented hardware interfaces where practical. Keep support specific to the detected model, firmware and capabilities.

No Legion laptop is available. Development starts from the downloaded upstream repositories: trace their implementations, document protocols and model differences, build Sora's backend with simulated transports, and test source-reference vectors. Physical hardware is needed later for qualification, not to begin or continue implementation. The earlier 2023 purchase reference is not a target model or evidence of available test hardware.

Initial feature order: detection and monitoring, firmware power modes, battery conservation, keyboard lighting, GPU switching, then custom power limits and fan curves. Legion desktops, Legion Go and other Lenovo product families are later workstreams.

Detailed findings and source links: [Lenovo Legion research](docs/Lenovo-Legion-research.md).

## Source and software inventory

| Priority | Repository | Study/reuse target |
| --- | --- | --- |
| 1 | [Maintained Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit) | Windows WMI wrappers, capability detection, power/charging features, GPU switching, RGB and event listeners |
| 2 | [LenovoLegionLinux](https://github.com/johnfanv2/LenovoLegionLinux) | Firmware/EC research, fan curves, power limits and model differences |
| 3 | [L5P-Keyboard-RGB](https://github.com/4JX/L5P-Keyboard-RGB) | Four-zone keyboard identities and HID reports |
| 4 | [LenovoController](https://github.com/ViRb3/LenovoController) | Legacy Lenovo driver transport; archived MIT reference |
| 5 | [Original Legion Toolkit](https://github.com/BartoszCichecki/LenovoLegionToolkit) | Archived historical baseline for older behavior |
| 6 | [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) | Additional lighting implementations where the exact controller matches |

Inspect the source implementations first. Examine publicly available Legion Space/Zone, Vantage, Energy Management, Gaming Feature Driver, Hotkeys or Corsair iCUE packages only for gaps that the open source projects do not explain. Use LegionFanControl as a behavior reference where supported; an installer download is not reusable source code.

## Phase 1 — Establish reproducible references

- [x] Acquire the three primary source snapshots and record repository URL, commit, retrieval date and relevant paths.
- [ ] Check exact file licenses and dependencies before importing code.
- [ ] Record the reuse decision for each component: port with attribution, use as a dependency, protocol reference, or unnecessary.
- [x] Add GPL-3.0-only licensing for Sora's original source; preserve third-party terms separately.
- [ ] Preserve third-party notices, license texts and required corresponding source information.

Start reading Toolkit's `LenovoLegionToolkit.Lib/System/Management`, `Features`, `Controllers` and `Listeners`. Review `GodMode`, four-zone RGB and Spectrum implementations separately. Compare Linux firmware research when the Windows implementation leaves an ambiguity.

**Deliverable:** a pinned source inventory and feature-to-source map. No proprietary vendor binaries or artwork added to the release package.

## Phase 2 — Recover protocols and compatibility from source

- [x] Trace power, charging, four-zone RGB, Spectrum, GPU and fan entry points in the pinned source.
- [x] Document transports, request formats, state values and known generation differences in a protocol map.
- [x] Extract the 48 laptop machine-type entries from the pinned Toolkit model table; retain source provenance and separate hardware qualification.
- [ ] Build synthetic capability/schema fixtures for the protocol families.
- [x] Build a read-only probe for applicable WMI schemas and HID interface descriptors.
- [ ] Implement capability-data parsing using upstream definitions and synthetic fixtures.
- [ ] Distinguish unsupported hardware, missing drivers, permission errors and transient failures.
- [ ] Exclude serial numbers, UUIDs and other unnecessary identifiers from shareable reports.
- [ ] Compare overlapping implementations across Toolkit, LenovoLegionLinux and the keyboard project.

Investigate `LENOVO_GAMEZONE_DATA` for thermal profiles/events and `LENOVO_OTHER_METHOD` plus capability data for advanced controls. The examined Toolkit charging implementation uses EnergyDrv. A class name or VID/PID alone is insufficient evidence to enable writes.

**Deliverable:** [a source-backed protocol map](docs/Lenovo-Legion-protocol-map.md), compatibility data and offline fixtures. The read-only collector remains an optional tool for future testers.

## Phase 3 — Integrate the Lenovo backend into Sora

- [x] Inspect existing Sora discovery, settings, control ownership and diagnostic patterns before selecting new types or files.
- [x] Add Lenovo identity/catalog detection and independent feature capability states.
- [x] Separate WMI, driver and HID operations from the UI.
- [x] Implement pure codecs for power values, legacy charging requests, four-zone RGB and fan-table serialization.
- [x] Add injectable transports so feature logic can be developed and exercised without a Legion connected.
- [ ] Reuse existing sensors and GPU abstractions where they fit.
- [ ] Serialize operations against the same controller; enforce timeouts and handle cancellation/disconnection.
- [ ] Expose readable and qualified writable capabilities independently.
- [x] Save recovery evidence before changes; implement readback/rollback and session restoration where state is readable. Four-zone initial colors are explicitly unknown.
- [x] Handle competing controller ownership without silently uninstalling or disabling vendor software.
- [x] Refresh visible-page state every five seconds and when reopened; do not replay settings at resume/startup.

Suggested logical boundaries: device identity/capabilities, firmware transport, charging, thermal profiles, lighting, GPU mode and advanced tuning. Final class/file names should follow the repository's existing conventions.

**Deliverable:** Lenovo detection and honest read-only state in Sora; unsupported controls remain unavailable.

## Phase 4 — Power modes and battery controls

- [x] Read supported firmware power presets and current state.
- [x] Implement supported preset changes with readback and clear errors.
- [x] Refresh external mode changes, including Fn+Q, by polling while the page is visible.
- [x] Keep firmware presets distinct from Windows power plans/modes.
- [x] Implement source-defined conservation/normal/rapid charging through the EnergyDrv interface, with readback and recovery.
- [ ] Verify behavior on AC/battery and across sleep/resume and restart as applicable.

**Deliverable:** qualified power presets and charging features for the exact test model. Do not treat conservation mode as an arbitrary percentage threshold without supporting evidence.

## Phase 5 — Keyboard and chassis lighting

- [ ] Identify whether the machine uses white backlight, four-zone RGB, Spectrum/per-key RGB or a separate Corsair controller.
- [ ] Verify HID interface/usage, report lengths and controller-specific commands.
- [ ] Implement on/off, brightness and static color where applicable.
- [ ] Verify color/zone mapping and original-state restoration.
- [ ] Add supported firmware effects before streamed effects.
- [ ] Test Fn-key changes, reconnect/resume and controller ownership conflicts.
- [ ] Qualify logo, rear-port or chassis lighting independently if present.

**Deliverable:** controls matching the actual lighting hardware. Four-zone and per-key protocols remain separate implementations.

## Phase 6 — GPU modes, power limits and fan curves

- [ ] Establish firmware GPU working modes and their restart requirements.
- [ ] Keep these controls distinct from Advanced Optimus and ordinary Windows graphics preferences.
- [ ] Test external-display and active-GPU constraints before allowing transitions.
- [ ] Read custom-mode support, feature IDs, limits, units and increments from the applicable interfaces.
- [ ] Apply custom limits only in the required firmware profile and verify readback.
- [ ] Establish fan-curve format, allowed points, units and firmware bounds for this model.
- [ ] Validate automatic-control restoration, sensor failure, application exit and sleep/resume behavior.
- [ ] Record known BIOS quirks and keep unqualified advanced controls unavailable.

**Deliverable:** separately qualified GPU switching and tuning features. Do not add broad EC register writes based solely on another Legion model's layout.

## Protocol research workflow for remaining gaps

1. Record model, BIOS, driver/software version and initial feature state.
2. Change one setting through the OEM UI and record the resulting state.
3. Trace the relevant WMI, driver or HID path. Use USBPcap/Wireshark for applicable USB reports and targeted static analysis for uncovered call paths.
4. Document request/response structure, return semantics, bounds and restoration behavior. Process Monitor can locate components/device access but does not decode every payload.
5. Implement the smallest required operation, verify readback and compare behavior on the same machine.
6. Attach evidence to the model/feature entry before enabling the control in releases.

Prefer existing source and documented interfaces before repeating protocol discovery. Keep observations and unverified hypotheses distinct.

## Validation and release

- [ ] Add meaningful automated checks for parsing, bounds, capability selection and failure handling where hardware behavior can be represented accurately.
- [ ] Run appropriate existing build and test checks after implementation.
- [ ] When test hardware or community testers become available, exercise controls on physical Legion models; mocks establish software behavior, not hardware qualification.
- [ ] Record readback, external changes, AC/battery, resume, controller conflicts and restoration results for each implemented feature.
- [ ] Publish a matrix of exact model, BIOS, controller identity, driver requirements and qualified features.
- [ ] Update user documentation, release notes and third-party notices.
- [ ] Test packaged detection on a non-Lenovo PC to confirm Lenovo controls stay unavailable.

Implementation can progress across source-supported families without owning a laptop. A hardware-qualified release requires verified detection, power/charging controls and applicable lighting on identified models, with advanced features independently qualified. Track implemented/source-tested and hardware-qualified status separately.

## Current status and next action

- [x] Identify initial repositories, vendor software targets and communication families.
- [x] Save the detailed research document.
- [x] Create this implementation plan.
- [x] Establish that no Legion hardware is available and use source-led development as the active workflow.
- [x] Acquire pinned source snapshots and audit selected initial implementations.
- [x] Add identity discovery, WMI/HID metadata diagnostics and a standalone laptop collector.
- [x] Document the first source-backed protocol map and implement offline codecs.
- [x] Implement native transport adapters, source catalog selection and feature transactions with synthetic fixtures.
- [ ] Implement and validate support.

Current preview: power presets, legacy charging, default Gamezone GPU configuration, four-zone RGB and Spectrum profile/brightness controls are implemented. See [coverage](docs/Lenovo-Legion.md). Next source-led work is custom tuning and full per-key editing; physical model qualification remains a separate track and is not a prerequisite for that development.

### First implementation milestone — 20 October 2026

Source revisions and license hashes are in [the source inventory](docs/lenovo-legion-sources.json); selected findings are in [the initial audit](docs/Lenovo-Legion-source-audit.md). Sora's hardware discovery now carries non-unique system identity without enabling Legion controls. The explicit diagnostic inspects WMI schemas and HID descriptors before Sora's normal services start.

Future testers can collect identity and metadata using [Collect-LenovoLegion.ps1](scripts/Collect-LenovoLegion.ps1). See [diagnostic instructions](docs/Lenovo-Legion-diagnostics.md). Neither collector invokes firmware methods or changes hardware configuration. This collector is supplementary, not the current work's prerequisite.

Validation: application build passes with zero warnings; all 55 Sora checks pass, including new Legion isolation checks. The compiled diagnostic and standalone collector correctly skip firmware inspection on the connected Gigabyte desktop. Mocked collector checks cover Lenovo schemas, access denial, unexpected providers, privacy and preserving existing reports. Real Legion inspection and controls remain unqualified.

### Source-led protocol milestone — 20 October 2026

The repository audit now traces the actual Windows transports and packet layouts, including charging command ordering, power-mode enum offsets, four-zone versus Spectrum lighting, custom fan tables, GPU backend selection and BIOS workarounds. Findings are recorded in [the protocol map](docs/Lenovo-Legion-protocol-map.md). Pure packet/state codecs and source-reference tests are implemented without any hardware I/O. All 56 Sora checks pass. These tests establish format behavior and input validation; hardware qualification remains a later activity.

### Experimental Windows backend — 20 October 2026

Added native WMI, EnergyDrv and HID adapters; source-derived laptop identity selection; a dedicated Legion page; power, charging and GPU configuration transactions; write-only four-zone RGB; and Spectrum compatibility/profile/brightness handling. Recovery records are flushed before mutation. Transactions verify readable configuration, attempt rollback on failure and finish recovery before shutdown. The UI checks cover absent interfaces, competing controllers and read-only sessions with zero hardware access.

All 57 repository checks and the synthetic Legion UI checks pass. The application builds with zero warnings. This is a source-tested preview; physical compatibility is not claimed. Custom fan/power tuning, Corsair lighting and full per-key effect editing remain incomplete.
