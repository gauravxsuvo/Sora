# Third-party components in Sora

Lenovo Legion protocol research references LenovoLegionToolkit-Team/LenovoLegionToolkit (GPL-3.0), johnfanv2/LenovoLegionLinux (including GPL-2.0-or-later kernel source), and 4JX/L5P-Keyboard-RGB (GPL-3.0). The inspected revisions and license-file hashes are recorded in `docs/lenovo-legion-sources.json`; implementation notes are in `docs/Lenovo-Legion-protocol-map.md`. Sora's Legion backend uses those documented protocol facts and source-derived model identifiers. No upstream executable, Lenovo driver, Lenovo artwork, or vendor utility is bundled. Local source checkouts used for research are excluded from the repository and release package.

Logitech device artwork: Sora can import the G502 HERO PNG and zone metadata from the user's installed G HUB into `data/logitech/artwork`. These locally cached graphics belong to Logitech and are not embedded in the executable or source repository. Rendering uses the original PNG and metadata; Sora does not require the G HUB service to display the cached image.

Sora uses **LibreHardwareMonitorLib 0.9.6**, by the LibreHardwareMonitor contributors, under the **Mozilla Public License 2.0**. Its binaries and signed PawnIO modules are unmodified. The full corresponding source is available at:

https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6

Pinned source revision: `3d331e3370efb858411f19511373eff65a218701`.

The full MPL-2.0 license is included in `licenses/LibreHardwareMonitor-MPL-2.0.txt`.

The WPF interface uses **MahApps.Metro.IconPacks.Lucide 6.2.1** (MIT) to render consistent Lucide vector icons. Lucide icons are licensed under ISC, with icons derived from Feather retaining their MIT attribution. See https://github.com/MahApps/MahApps.Metro.IconPacks and https://lucide.dev/license.

The interface embeds unmodified static **Inter 4.1** font files, copyright the Inter Project Authors, under the **SIL Open Font License 1.1**. Source: https://github.com/rsms/inter/releases/tag/v4.1. The full license is retained at `assets/fonts/Inter/LICENSE.txt` and copied to `licenses/Inter-OFL.txt` in the application output.

NuGet dependency versions, license metadata, project links, and available packaged license/notice texts are included in `licenses/dependencies.md` and its sibling directories. Exact package hashes and versions are recorded in the project's `packages.lock.json` files.

PawnIO is an independently installed driver. Sora does not bundle, install, uninstall, or modify that driver. PawnIO project information: https://pawnio.eu/

Microsoft .NET and the Windows Desktop Runtime are installed separately. No DeepCool executables, HWiNFO SDK binaries, or DeepCool sensor configuration files are included in this release.

Kreo support includes independently implemented HID handling and factual model definitions. No Kreo executable, DLL, artwork, skin, or firmware is bundled.
