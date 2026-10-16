param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget\packages'
$env:MSBuildEnableWorkloadResolver = 'false'
$TestArguments = @('run', '--project', (Join-Path $PSScriptRoot 'tests\Sora.Checks\Sora.Checks.csproj'), '-p:UseSharedCompilation=false')
if ($NoRestore) { $TestArguments += '--no-restore' }
& dotnet @TestArguments
if ($LASTEXITCODE -ne 0) { throw 'Sora checks failed' }
