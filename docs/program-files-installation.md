# Windows installation

Use the Windows x64 installer. It requires the .NET 8 Windows Desktop Runtime (x64) and administrator access. Setup does not install hardware drivers or change firmware.

Exit Sora from its tray menu before setup. Setup installs to Program Files and offers an optional desktop shortcut. It detects connected hardware and explains the device pages available on that computer. Hardware settings are filtered again at runtime so later connection changes are handled automatically.

Shared application assemblies remain installed for updates and newly connected devices. Device pages and background controllers are enabled according to detected hardware; setup does not produce a different executable for each computer.

Normal upgrades preserve user profiles and preferences. Uninstall offers to retain saved data or remove it, and keeps shared .NET and PawnIO installations.

## Build an installer

Publish the application as a Windows x64 framework-dependent folder, then generate dependency notices. Use an official Inno Setup compiler:

~~~powershell
./scripts/Build-Installer.ps1 -ReleaseDirectory artifacts/publish -OutputDirectory artifacts/setup -Compiler 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
~~~

Choose a new output directory for each build. The packager validates dependencies and hashes, rejects user-data folders and links, and does not install or launch the application. Building the installer does not give it a Windows publisher signature.

See [download warnings](download-warnings.md).
