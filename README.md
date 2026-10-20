<p align="center"><img src="assets/brand/sora-mark.svg" width="112" alt="Sora"></p>

# Sora

A native Windows hardware hub for this PC's DeepCool cooler display, Kreo keyboard, Logitech mouse, Gigabyte lighting and fans, NVIDIA GPU fans, storage health, and Wi-Fi measurements.

[Download for Windows](https://sora.portways.app/#download) · [Download beta](https://github.com/gauravxsuvo/Sora/releases/tag/v0.13.0-beta.3) · [User guide](docs/user-guide.md) · [Beta release notes](docs/Sora-0.13.0-beta.3.md)

## Installation

Use the Windows x64 installer from the download page. Sora requires the .NET 8 Windows Desktop Runtime (x64). Setup checks connected hardware; the application shows the pages and settings available for that computer. New hardware is detected automatically.

Sora does not install hardware drivers or update device firmware. CPU sensors and supported motherboard controls require a separately installed PawnIO driver. Exit other hardware controllers before applying changes to the same device.

Normal upgrades preserve preferences and profiles. Uninstall lets you keep saved data or remove it. See [installation](docs/program-files-installation.md) and [download warnings](docs/download-warnings.md).

## Supported hardware

| Hardware | Coverage |
| --- | --- |
| DeepCool LD360 | Display of live CPU temperature, utilization, and package power |
| Supported Kreo keyboards and receivers | Model-dependent key mapping and lighting |
| Logitech G502 HERO | DPI, button profiles, macros, lighting, and selected device preferences |
| Logitech G102 / G203 family | Wired detection and read-only DPI, polling-rate, and capability information; advanced controls are not yet qualified |
| Supported RGB memory / Gigabyte controllers | Model-dependent lighting with original-state backups |
| Gigabyte Z890 GAMING X WIFI7 | Supported motherboard fan controls |
| NVIDIA graphics | Driver-dependent fan monitoring and controls; native RPM programming is restricted to qualified card interfaces |
| Storage | Device-dependent identity, health, SMART, and endurance information |
| Wi-Fi | Windows network readings and manual antenna-position comparisons |

Support is specific to the device, interface, and available capabilities. Sora does not assume that every device from a supported brand accepts the same commands. See the [user guide](docs/user-guide.md) for limitations.

## Build and check

Install the .NET 8 SDK and build on Windows:

~~~powershell
dotnet restore src/Sora.App/Sora.App.csproj
dotnet build src/Sora.App/Sora.App.csproj -m:1
./test.ps1
~~~

Build a folder publish for packaging:

~~~powershell
dotnet publish src/Sora.App/Sora.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o artifacts/publish
./scripts/Write-ThirdPartyNotices.ps1 -ReleaseDirectory artifacts/publish
~~~

See [installer packaging](docs/program-files-installation.md) for the Inno Setup workflow. The installer and app do not require vendor suites as build dependencies.

## Troubleshooting

- Missing device: check its Windows device status and USB connection. Refresh the device page; Sora also checks for hardware changes automatically.
- Device detected without controls: that model or feature is not yet qualified. Detection does not imply support for every operation.
- Apply blocked: exit other controllers for that hardware and refresh before retrying.
- Missing sensor readings: check permissions and the required sensor driver. Sora does not substitute fabricated values.
- Download warnings: follow [download guidance](docs/download-warnings.md). Do not disable browser or antivirus protection.

Report the Sora version, device model, Windows version, and steps to reproduce in [Issues](https://github.com/gauravxsuvo/Sora/issues). Redact personal information from diagnostics before sharing.

Dependency licenses and attributions are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Sora's original source code is licensed under GPL-3.0-only; see [LICENSE](LICENSE). Third-party components and assets retain their own licenses and notices.
