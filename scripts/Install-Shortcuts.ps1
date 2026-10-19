param([Parameter(Mandatory)][string]$Target, [switch]$UpdateOnly, [switch]$ReplaceExisting)
# Creates (or with -UpdateOnly, only refreshes) the Start menu and desktop shortcuts for Sora.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ShortcutRouting.ps1')
$Links = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Sora.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sora.lnk')
)
Update-SoraShortcuts -Target $Target -Links $Links -DevelopmentRoot (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist') -UpdateOnly:$UpdateOnly -ReplaceExisting:$ReplaceExisting
