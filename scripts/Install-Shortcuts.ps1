param([Parameter(Mandatory)][string]$Target, [switch]$UpdateOnly)
# Creates (or with -UpdateOnly, only refreshes) the Start menu and desktop shortcuts for Sora.
$ErrorActionPreference = 'Stop'
$Target = (Resolve-Path -LiteralPath $Target).Path
$Shell = New-Object -ComObject WScript.Shell
$Links = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Sora.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sora.lnk')
)
foreach ($Link in $Links) {
    if ($UpdateOnly -and -not (Test-Path -LiteralPath $Link)) { continue }
    $Shortcut = $Shell.CreateShortcut($Link)
    $Shortcut.TargetPath = $Target
    $Shortcut.WorkingDirectory = Split-Path $Target -Parent
    $Shortcut.IconLocation = "$Target,0"
    $Shortcut.Description = 'Sora - LD360 display and Kreo keyboard'
    $Shortcut.Save()
    Write-Host "Shortcut: $Link -> $Target"
}
