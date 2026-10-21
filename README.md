<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="assets/brand/sora-mark-white.svg"><img src="assets/brand/sora-mark-ink.svg" width="112" alt="Sora"></picture></p>

# Sora

A native Windows PC controller for cooling, lighting, peripherals and hardware monitoring.

[Download for Windows](https://sora.portways.app/#download) · [User guide](docs/user-guide.md)

## Installation

Use the Windows x64 installer from the download page. Sora requires the .NET 8 Windows Desktop Runtime (x64). Setup checks connected hardware; the application shows the pages and settings available for that computer. New hardware is detected automatically.

Sora does not install hardware drivers or update device firmware. CPU sensors and supported motherboard controls require a separately installed PawnIO driver. Exit other hardware controllers before applying changes to the same device.

Normal upgrades preserve preferences and profiles. Uninstall lets you keep saved data or remove it. See [installation](docs/program-files-installation.md) and [download warnings](docs/download-warnings.md).

## Features

- Cooling: temperatures, supported fan curves and cooler displays.
- Lighting: memory, motherboard and peripheral lighting with saved preferences.
- Peripherals: device-dependent mouse sensitivity, button profiles and keyboard controls.
- Monitoring: storage health, Wi-Fi readings and hardware maintenance information.

Available controls depend on detected hardware, drivers and qualified device interfaces. Unsupported controls remain hidden or read-only. See the [user guide](docs/user-guide.md) for capabilities and limitations.

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
