# Lenovo Legion initial source audit

Audit date: 20 October 2026. Exact revisions and license-file hashes are recorded in [lenovo-legion-sources.json](lenovo-legion-sources.json). Local shallow checkouts are under ignored `vendor/lenovo-legion/`; upstream code has not been linked, compiled or imported into Sora.

## Examined references

| Project | Revision | Findings and next reading target |
| --- | --- | --- |
| LenovoLegionToolkit-Team/LenovoLegionToolkit | `d0c57bfc7ecc26d4ccf15bb3835fed7ac28a259f` | WMI wrappers separate query operations from firmware methods. Gamezone exposes thermal/smart-fan mode operations. OtherMethod exposes feature-ID operations; capability data wrappers enumerate supported IDs and ranges. Charging in the examined `BatteryFeature.cs` uses EnergyDrv rather than the Gamezone interface. Continue auditing generation-specific selection and recovery behavior before implementing writes. |
| johnfanv2/LenovoLegionLinux | `b055cec8e713cd62fc5aaa8085ec67c5ac57c31e` | The kernel source has a GPL-2.0-or-later header and model/access-method configuration. Its AGENTS.md emphasizes model/BIOS allowlists and limiting fan-unlock support to validated combinations. No kernel module was built or loaded. Continue with the model table and relevant firmware research after identifying the laptop. |
| 4JX/L5P-Keyboard-RGB | `6e54bd6bc3b91c6ad5ab0ad95887adec5e723bc7` | `driver/src/lib.rs` identifies devices by VID/PID plus HID usage page and usage. Four-zone packets and effect logic are controller-specific. Sora's first diagnostic gathers those descriptors without sending packets; it does not assume that all ITE devices use this protocol. |

Pinned source entry points:

- [Toolkit WMI](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/d0c57bfc7ecc26d4ccf15bb3835fed7ac28a259f/LenovoLegionToolkit.Lib/System/Management/WMI.cs)
- [Toolkit Gamezone wrapper](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/d0c57bfc7ecc26d4ccf15bb3835fed7ac28a259f/LenovoLegionToolkit.Lib/System/Management/WMI.LenovoGameZoneData.cs)
- [Toolkit OtherMethod wrapper](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/d0c57bfc7ecc26d4ccf15bb3835fed7ac28a259f/LenovoLegionToolkit.Lib/System/Management/WMI.LenovoOtherMethod.cs)
- [Toolkit battery feature](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/d0c57bfc7ecc26d4ccf15bb3835fed7ac28a259f/LenovoLegionToolkit.Lib/Features/BatteryFeature.cs)
- [Linux kernel module](https://github.com/johnfanv2/LenovoLegionLinux/blob/b055cec8e713cd62fc5aaa8085ec67c5ac57c31e/kernel_module/legion-laptop.c)
- [Four-zone keyboard driver](https://github.com/4JX/L5P-Keyboard-RGB/blob/6e54bd6bc3b91c6ad5ab0ad95887adec5e723bc7/driver/src/lib.rs)

This is an initial audit of selected files, not a complete review of any repository. License files show GPL version 3 for Toolkit and the keyboard project, and version 2 for LenovoLegionLinux; preserve exact file notices and dependency obligations if code is imported later. Selecting Sora's project license remains pending.

## Implemented first slice

- Registry-based system identity in Sora's existing hardware inventory. Identity does not create a control module.
- Explicit WMI metadata inspection with an allowlist of class names, known GUID checks, provider checks and distinct absent/access/failure states.
- Descriptor-only HID enumeration without serial/product string queries for this diagnostic.
- Early `--lenovo-legion-diagnose --output PATH` startup route, before normal hardware services start.
- Standalone PowerShell collector for obtaining the laptop identity and metadata without a Sora installation.

## Source-led continuation

No Legion laptop is available. The [protocol map](Lenovo-Legion-protocol-map.md) now follows the relevant source beyond the initial metadata inspection. Pure packet/state codecs are implemented in `LenovoLegionProtocol.cs`, with source-reference tests. Continue capability parsing, transport adapters and feature state machines using offline fixtures. Physical reports can qualify models later; they are not a prerequisite for source analysis or implementation.
