param([string]$ReleaseDirectory = 'dist\Sora-current', [string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
$TaskRoot = Split-Path $PSScriptRoot -Parent
$Executable = (Resolve-Path -LiteralPath (Join-Path $TaskRoot (Join-Path $ReleaseDirectory 'Sora.exe'))).Path
if (-not $ArtifactDirectory) { $ArtifactDirectory = Join-Path $TaskRoot ('artifacts\hardware-verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $ArtifactDirectory) { throw 'Use a new artifact directory to preserve earlier evidence.' }
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
$Results = [Collections.Generic.List[object]]::new()

function Invoke-SoraCheck([string]$Name, [string[]]$Arguments, [string]$OutputPath) {
    $Started = [DateTimeOffset]::Now
    $Process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
    if (-not $Process.WaitForExit(90000)) { $Process.Kill(); throw "Sora $Name exceeded its deadline." }
    if ($Process.ExitCode -ne 0) { throw "Sora $Name failed with exit code $($Process.ExitCode)." }
    if (-not (Test-Path -LiteralPath $OutputPath)) { throw "Sora $Name produced no output." }
    $File = Get-Item -LiteralPath $OutputPath
    if ($File.Length -eq 0 -or $File.LastWriteTimeUtc -lt $Started.UtcDateTime) { throw "Sora $Name did not produce fresh output." }
    $Results.Add([ordered]@{ name = $Name; exitCode = $Process.ExitCode; output = $OutputPath; bytes = $File.Length; durationSeconds = ([DateTimeOffset]::Now - $Started).TotalSeconds })
    } finally { $Process.Dispose() }
}

$ProbePath = Join-Path $ArtifactDirectory 'hardware-tools-integrated-probe.json'
Invoke-SoraCheck 'read-only probe' @('--hardware-tools-probe', '--output', ('"' + $ProbePath + '"')) $ProbePath
$Probe = Get-Content -LiteralPath $ProbePath -Raw | ConvertFrom-Json
if ($Probe.configurationWrites -ne 0 -or $Probe.activeWifiScanRequested -ne $false -or $Probe.storageError -or $Probe.gpuError -or $Probe.wifiError) { throw 'The read-only hardware probe reported an error; inspect the fresh result instead of treating exit code 0 as success.' }
foreach ($Check in @(@('lighting-editor-check','lighting-editor-check.json'), @('gpu-fan-lifecycle-check','gpu-fan-lifecycle-check.json'))) {
    $CheckPath = Join-Path $ArtifactDirectory $Check[1]
    Invoke-SoraCheck $Check[0] @(('--' + $Check[0]), '--output', ('"' + $CheckPath + '"')) $CheckPath
    if (-not (Get-Content -LiteralPath $CheckPath -Raw | ConvertFrom-Json).passed) { throw "$($Check[0]) reported failed scenarios." }
}
foreach ($Page in 'gpu', 'ssd', 'wifi', 'fans', 'lighting') {
    $ScreenshotPath = Join-Path $ArtifactDirectory ('sora-' + $Page + '.png')
    Invoke-SoraCheck ($Page + ' screenshot') @(('--' + $Page), '--screenshot', ('"' + $ScreenshotPath + '"'), '--size', '1080x720') $ScreenshotPath
}
$CurvePath = Join-Path $ArtifactDirectory 'sora-gpu-curve.png'
Invoke-SoraCheck 'native curve editor screenshot' @('--gpu', '--gpu-curve-preview', '--screenshot', ('"' + $CurvePath + '"')) $CurvePath
$WifiPath = Join-Path $ArtifactDirectory 'sora-wifi-comparison.png'
Invoke-SoraCheck 'Wi-Fi position comparison screenshot' @('--wifi', '--wifi-comparison-preview', '--screenshot', ('"' + $WifiPath + '"'), '--size', '1080x720') $WifiPath
[ordered]@{
    capturedAt = [DateTimeOffset]::Now.ToString('O')
    executable = $Executable
    sha256 = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
    configurationWrites = 0
    physicalHardwareQualified = $false
    checks = $Results
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'hardware-tools-ui-verification.json')
