param([string]$OutputDirectory = 'dist\Sora-0.12.0', [switch]$NoRestore, [switch]$SkipShortcuts)
$ErrorActionPreference = 'Stop'
$TaskRoot = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $TaskRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $TaskRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$Release = [IO.Path]::GetFullPath((Join-Path $TaskRoot $OutputDirectory))
if (-not $Release.StartsWith(([IO.Path]::GetFullPath((Join-Path $TaskRoot 'dist')) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw 'Build output must be a subdirectory of this workspace dist directory.' }
# Windows x64 development release (needs the .NET 8 Desktop Runtime). Shared profiles live in LOCALAPPDATA\Sora\data.
if (Test-Path -LiteralPath $Release) { Get-ChildItem -LiteralPath $Release | Where-Object Name -ne 'data' | Remove-Item -Recurse -Force }
$PublishArguments = @('publish', (Join-Path $TaskRoot 'src\Sora.App\Sora.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-p:EnableRuntimePackDownload=false', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=embedded', '-o', $Release, '-p:UseSharedCompilation=false', '-v', 'minimal')
if ($NoRestore) { $PublishArguments += '--no-restore' }
& dotnet @PublishArguments
if ($LASTEXITCODE -ne 0) { throw 'Sora build failed' }
& (Join-Path $TaskRoot 'scripts\Write-ThirdPartyNotices.ps1') -ReleaseDirectory (Join-Path $TaskRoot $OutputDirectory)
$PriorDataDirectory = 'dist\Sora-0.10.4\data'
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.10.3\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.10.2\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.10.1\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.10.0\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.9.1\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.9.0\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.9\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.8\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.7\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.6\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.5\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.4\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.3\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.2\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.1\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.8.0\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.7.4\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.7.3\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.7.2\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.7.1\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.7.0\data' }
if (-not (Test-Path -LiteralPath (Join-Path $TaskRoot $PriorDataDirectory))) { $PriorDataDirectory = 'dist\Sora-0.6.11\data' }
$PriorSettings = Join-Path $TaskRoot (Join-Path $PriorDataDirectory 'settings.json')
$NewSettings = Join-Path $TaskRoot (Join-Path $OutputDirectory 'data\settings.json')
if ((Test-Path -LiteralPath $PriorSettings) -and -not (Test-Path -LiteralPath $NewSettings)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $NewSettings -Parent) | Out-Null
    Copy-Item -LiteralPath $PriorSettings -Destination $NewSettings
}
$PriorKeyboardData = Join-Path $TaskRoot (Join-Path $PriorDataDirectory 'keyboard')
$NewKeyboardData = Join-Path $TaskRoot (Join-Path $OutputDirectory 'data\keyboard')
if ((Test-Path -LiteralPath $PriorKeyboardData) -and -not (Test-Path -LiteralPath $NewKeyboardData)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $NewKeyboardData -Parent) | Out-Null
    Copy-Item -LiteralPath $PriorKeyboardData -Destination $NewKeyboardData -Recurse
}
# Lighting: the saved choices and, above all, the RAM's original-lighting backup.
foreach ($DeviceDirectory in 'logitech') {
    $PriorDeviceData = Join-Path $TaskRoot (Join-Path $PriorDataDirectory $DeviceDirectory)
    $NewDeviceData = Join-Path $Release (Join-Path 'data' $DeviceDirectory)
    if ((Test-Path -LiteralPath $PriorDeviceData) -and -not (Test-Path -LiteralPath $NewDeviceData)) {
        New-Item -ItemType Directory -Force -Path (Split-Path $NewDeviceData -Parent) | Out-Null
        Copy-Item -LiteralPath $PriorDeviceData -Destination $NewDeviceData -Recurse
    }
}
foreach ($Name in 'lighting.json', 'lighting-original.json', 'fans.json', 'fans-original.json', 'fans-firmware-original.json', 'fans-service-ownership.json', 'gpu-fans-original.json', 'gpu-fans-profile.json', 'gpu-fans-profiles.json') {
    $Prior = Join-Path $TaskRoot (Join-Path $PriorDataDirectory $Name)
    $New = Join-Path $TaskRoot (Join-Path $OutputDirectory (Join-Path 'data' $Name))
    if ((Test-Path -LiteralPath $Prior) -and -not (Test-Path -LiteralPath $New)) {
        New-Item -ItemType Directory -Force -Path (Split-Path $New -Parent) | Out-Null
        Copy-Item -LiteralPath $Prior -Destination $New
    }
}
# dist\Sora-current is a junction to the newest development release. Owned shortcuts and an opted-in development task use it, so an update changes neither
# (rewriting an elevated logon task from each new unsigned Sora.exe is what Defender flagged as persistence in 0.8.6).
$Current = Join-Path $TaskRoot 'dist\Sora-current'
if (Test-Path -LiteralPath $Current) {
    if (-not ((Get-Item -LiteralPath $Current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "$Current is a real folder, not a junction; move it away first." }
}
$NextCurrent = Join-Path $TaskRoot 'dist\Sora-current-next'
if (Test-Path -LiteralPath $NextCurrent) { throw "$NextCurrent already exists; inspect the interrupted build before proceeding." }
# Create first: if junction creation is denied, existing shortcuts must still work.
New-Item -ItemType Junction -Path $NextCurrent -Target $Release | Out-Null
$PreviousTarget = if (Test-Path -LiteralPath $Current) { (Get-Item -LiteralPath $Current -Force).Target } else { $null }
try {
    if (Test-Path -LiteralPath $Current) { [IO.Directory]::Delete($Current) } # Junction only; never traverse its target.
    [IO.Directory]::Move($NextCurrent, $Current)
} catch {
    if (-not (Test-Path -LiteralPath $Current) -and $PreviousTarget) { New-Item -ItemType Junction -Path $Current -Target $PreviousTarget | Out-Null }
    throw
}
Write-Host "Current: $Current -> $Release"
if (-not $SkipShortcuts) { & (Join-Path $TaskRoot 'scripts\Install-Shortcuts.ps1') -Target (Join-Path $Current 'Sora.exe') -UpdateOnly }
