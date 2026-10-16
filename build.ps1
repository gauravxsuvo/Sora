param([string]$OutputDirectory = 'dist\Sora-0.5', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$TaskRoot = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $TaskRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $TaskRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$Release = Join-Path $TaskRoot $OutputDirectory
# Single Sora.exe for Windows x64 (needs the .NET 8 Desktop Runtime). Settings in data\ are kept across rebuilds.
if (Test-Path -LiteralPath $Release) { Get-ChildItem -LiteralPath $Release | Where-Object Name -ne 'data' | Remove-Item -Recurse -Force }
$PublishArguments = @('publish', (Join-Path $TaskRoot 'src\Sora.App\Sora.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=embedded', '-o', $Release, '-p:UseSharedCompilation=false', '-v', 'minimal')
if ($NoRestore) { $PublishArguments += '--no-restore' }
& dotnet @PublishArguments
if ($LASTEXITCODE -ne 0) { throw 'Sora build failed' }
& (Join-Path $TaskRoot 'scripts\Write-ThirdPartyNotices.ps1') -ReleaseDirectory (Join-Path $TaskRoot $OutputDirectory)
$PriorSettings = Join-Path $TaskRoot 'dist\Sora-0.4\data\settings.json'
$NewSettings = Join-Path $TaskRoot (Join-Path $OutputDirectory 'data\settings.json')
if ((Test-Path -LiteralPath $PriorSettings) -and -not (Test-Path -LiteralPath $NewSettings)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $NewSettings -Parent) | Out-Null
    Copy-Item -LiteralPath $PriorSettings -Destination $NewSettings
}
$PriorKeyboardData = Join-Path $TaskRoot 'dist\Sora-0.4\data\keyboard'
$NewKeyboardData = Join-Path $TaskRoot (Join-Path $OutputDirectory 'data\keyboard')
if ((Test-Path -LiteralPath $PriorKeyboardData) -and -not (Test-Path -LiteralPath $NewKeyboardData)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $NewKeyboardData -Parent) | Out-Null
    Copy-Item -LiteralPath $PriorKeyboardData -Destination $NewKeyboardData -Recurse
}
# Re-point existing Sora shortcuts at this build; never creates new ones.
& (Join-Path $TaskRoot 'scripts\Install-Shortcuts.ps1') -Target (Join-Path $Release 'Sora.exe') -UpdateOnly
