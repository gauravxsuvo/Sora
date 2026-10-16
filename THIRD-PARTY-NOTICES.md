# Third-party components in Sora 0.3.1

Sora uses **LibreHardwareMonitorLib 0.9.6**, by the LibreHardwareMonitor contributors, under the **Mozilla Public License 2.0**. Its binaries and signed PawnIO modules are unmodified. The full corresponding source is available at:

https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6

Pinned source revision: `3d331e3370efb858411f19511373eff65a218701`.

The full MPL-2.0 license is included in `licenses/LibreHardwareMonitor-MPL-2.0.txt`. A local source checkout is retained at `E:\Sora\vendor\LibreHardwareMonitor`.

NuGet dependency versions, license metadata, project links, and available packaged license/notice texts are included in `licenses/dependencies.md` and its sibling directories. Exact package hashes and versions are recorded in the project's `packages.lock.json` files.

PawnIO is an independently installed driver on this PC. Sora does not bundle, install, uninstall, or modify that driver. PawnIO project information: https://pawnio.eu/

Microsoft .NET and the Windows Desktop Runtime are installed separately. No DeepCool executables, HWiNFO SDK binaries, or DeepCool sensor configuration files are included in this release.

Kreo support was implemented using static inspection of the locally installed Kreo KB Hub and device readback. The release includes independently implemented HID handling and factual model definitions (USB identifiers, key positions, and effect identifiers). It does not include Kreo executables, DLLs, artwork, skins, or firmware. REA (MIT licensed) is an analysis tool in the development workspace, not a runtime dependency or part of the release.
