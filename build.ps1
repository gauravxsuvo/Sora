param([string]$OutputDirectory = 'dist\Sora-0.12.5', [switch]$NoRestore, [switch]$SkipShortcuts)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$releaseDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputDirectory))
$distDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist'))
if (-not $releaseDirectory.StartsWith($distDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build output must be inside the workspace dist directory.' }
if (Test-Path -LiteralPath $releaseDirectory) { throw 'Build output already exists. Choose a new output directory.' }
for ($ancestor = $releaseDirectory; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Build output must not traverse a link.' }
}
$publishArguments = @('publish', (Join-Path $PSScriptRoot 'src\Sora.App\Sora.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-m:1', '-p:EnableRuntimePackDownload=false', '-p:PublishSingleFile=false', '-p:DebugType=embedded', '-o', $releaseDirectory, '-p:UseSharedCompilation=false', '-v', 'minimal')
if ($NoRestore) { $publishArguments += '--no-restore' }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Sora build failed' }
& (Join-Path $PSScriptRoot 'scripts\Write-ThirdPartyNotices.ps1') -ReleaseDirectory $releaseDirectory
Write-Host "Built: $releaseDirectory"
