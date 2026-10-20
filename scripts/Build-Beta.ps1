param(
    [string]$OutputDirectory = 'artifacts/beta',
    [Parameter(Mandatory)][string]$Compiler
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Sora.App/Sora.App.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ }
if ($version -notmatch '^\d+\.\d+\.\d+-beta\.\d+$') { throw 'Build-Beta requires a beta version in Sora.App.csproj.' }
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory to preserve prior release artifacts.' }
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:NUGET_PACKAGES = Join-Path $root '.nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$publish = Join-Path $output 'publish'
dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:DebugType=embedded -p:UseSharedCompilation=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Beta publish failed.' }
& (Join-Path $PSScriptRoot 'Write-ThirdPartyNotices.ps1') -ReleaseDirectory $publish
$instructions = @"
Sora $version - experimental Windows x64 beta

Requires .NET 8 Windows Desktop Runtime (x64). Exit other Sora instances
before installing. Setup upgrades the existing Program Files installation
and retains saved settings. Back up settings before trying the beta.

This build targets the supported hardware in your PC. Read ReleaseNotes.md
before changing settings.
Keep all DLLs and the UpdateRunner directory beside Sora.exe.

Redact personal information before sharing reports.
"@
[IO.File]::WriteAllText((Join-Path $publish 'BETA-README.txt'), $instructions, [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Build-Installer.ps1') -ReleaseDirectory $publish -OutputDirectory (Join-Path $output 'setup') -Compiler $Compiler
$installer = Join-Path $output "setup/Sora-Setup-$version-x64.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Beta installer: $installer"
Write-Host "SHA-256: $hash"
