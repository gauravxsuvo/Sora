param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$Compiler
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $root) { throw 'Preserve earlier installer package checks.' }
New-Item -ItemType Directory -Path $root | Out-Null
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
$builder = Join-Path $PSScriptRoot 'Build-Installer.ps1'
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
}
function Fixture([string]$Name) {
    $path = Join-Path $root $Name
    New-Item -ItemType Directory -Path $path | Out-Null
    Get-ChildItem -LiteralPath $release -Force | Copy-Item -Destination $path -Recurse
    return $path
}
function Refuses([string]$Name, [string]$Source, [string]$Output, [string]$Pattern) {
    $errorText = $null
    try { & $builder -ReleaseDirectory $Source -OutputDirectory $Output -Compiler $Compiler | Out-Null }
    catch { $errorText = $_.ToString() }
    Check $Name ($null -ne $errorText -and $errorText -match $Pattern) $errorText
}
$audit = Get-Content (Join-Path $build 'build-audit.json') -Raw | ConvertFrom-Json
$manifest = Get-Content (Join-Path $build 'payload\sora-package.json') -Raw | ConvertFrom-Json
Check 'Installer hash matches build audit' ((Get-FileHash -LiteralPath $audit.installer).Hash -eq $audit.installerSha256)
Check 'All source files match staged payload hashes' (@($manifest.files).Count -eq $audit.payloadFiles -and $audit.payloadFiles -gt 0 -and @($manifest.files | Where-Object {
    (Get-FileHash -LiteralPath (Join-Path $release $_.relative)).Hash -ne $_.sha256 -or
    (Get-FileHash -LiteralPath (Join-Path (Join-Path $build 'payload') $_.relative)).Hash -ne $_.sha256
}).Count -eq 0)
$before = @(Get-ChildItem -LiteralPath $build -Recurse -File | Get-FileHash | Select-Object Path,Hash)
Refuses 'Existing output refuses overwrite' $release $build 'already exists'
$after = @(Get-ChildItem -LiteralPath $build -Recurse -File | Get-FileHash | Select-Object Path,Hash)
Check 'Existing installer evidence unchanged after refusal' (($before | ConvertTo-Json -Compress) -eq ($after | ConvertTo-Json -Compress))
Refuses 'Output inside source refused' $release (Join-Path $release 'nested-setup') 'inside the source'
Check 'No nested output created' (-not (Test-Path -LiteralPath (Join-Path $release 'nested-setup')))
foreach ($name in @('Sora.dll','Sora.Core.dll','Sora.runtimeconfig.json','licenses\LibreHardwareMonitor-MPL-2.0.txt','runtimes\win-x64\lib\net8.0\LibreHardwareMonitorLib.dll','BlackSharp.Core.dll')) {
    $fixture = Fixture ('missing-' + [IO.Path]::GetFileName($name))
    # Delete only this copied fixture file, after verifying its resolved parent stays inside the fixture.
    $target = [IO.Path]::GetFullPath((Join-Path $fixture $name))
    if (-not $target.StartsWith($fixture + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture path.' }
    Remove-Item -LiteralPath $target
    Refuses ('Missing asset refused: ' + $name) $fixture (Join-Path $root ('out-' + [IO.Path]::GetFileName($name))) 'Incomplete folder|Dependency manifest asset'
}
$fixture = Fixture 'user-data'
New-Item -ItemType Directory -Path (Join-Path $fixture 'data') | Out-Null
Refuses 'User data directory refused' $fixture (Join-Path $root 'out-user-data') 'user data'
$fixture = Fixture 'wrong-runtime'
$config = Join-Path $fixture 'Sora.runtimeconfig.json'
(Get-Content -LiteralPath $config -Raw).Replace('8.0.0','9.0.0') | Set-Content -LiteralPath $config
Refuses 'Different major runtime refused' $fixture (Join-Path $root 'out-runtime') 'framework-dependent .NET 8'
$fixture = Fixture 'corrupt-exe'
[IO.File]::WriteAllBytes((Join-Path $fixture 'Sora.exe'), [byte[]]@(0,0,0,0))
Refuses 'Non-PE executable refused' $fixture (Join-Path $root 'out-exe') 'not a PE'
$linkedSource = Join-Path $root 'linked-source'
New-Item -ItemType Junction -Path $linkedSource -Target $release | Out-Null
Refuses 'Linked release source refused' $linkedSource (Join-Path $root 'out-source-link') 'Reparse point'
$fixture = Fixture 'nested-link'
New-Item -ItemType Junction -Path (Join-Path $fixture 'linked-runtime') -Target (Join-Path $release 'runtimes') | Out-Null
Refuses 'Nested payload junction refused before traversal' $fixture (Join-Path $root 'out-nested-link') 'reparse point'
$realOutput = Join-Path $root 'real-output'
New-Item -ItemType Directory -Path $realOutput | Out-Null
$linkedOutput = Join-Path $root 'linked-output'
New-Item -ItemType Junction -Path $linkedOutput -Target $realOutput | Out-Null
Refuses 'Linked output ancestor refused' $release (Join-Path $linkedOutput 'setup') 'Reparse point'
Check 'Linked output target untouched' (@(Get-ChildItem -LiteralPath $realOutput -Force).Count -eq 0)
$result = [ordered]@{
    recordedAt = [DateTimeOffset]::Now.ToString('o'); passed = @($checks | Where-Object {-not $_.passed}).Count -eq 0
    count = $checks.Count; checks = @($checks); hardwareAccess = $false; installerExecuted = $false
    builderSha256 = (Get-FileHash -LiteralPath $builder).Hash
    templateSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot '..\installer\Sora.iss')).Hash
}
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(($result | ConvertTo-Json -Depth 8))
$stream = [IO.FileStream]::new((Join-Path $root 'result.json'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
$result | Select-Object passed,count | ConvertTo-Json
if (-not $result.passed) { throw 'Installer package guard checks failed.' }
