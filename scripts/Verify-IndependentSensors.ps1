param([switch]$WithoutDeepCool)
$ErrorActionPreference = 'Stop'
$TaskRoot = Split-Path $PSScriptRoot -Parent
$Executable = Join-Path $TaskRoot 'dist\Sora-0.2\Sora.exe'
$Artifacts = Join-Path $TaskRoot 'artifacts'
New-Item -ItemType Directory -Force $Artifacts | Out-Null
$IsAdministrator = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $IsAdministrator) { throw 'Run this verification script from Administrator PowerShell.' }
function Invoke-SensorProbe([string]$OutputName) {
    $Output = Join-Path $Artifacts $OutputName
    $Process = Start-Process -FilePath $Executable -ArgumentList @('--diagnose','--samples','5','--output',$Output) -PassThru -WindowStyle Hidden
    if (-not $Process.WaitForExit(25000)) { throw 'Sora sensor verification exceeded its deadline.' }
    $Result = Get-Content -LiteralPath $Output -Raw | ConvertFrom-Json
    if ($Process.ExitCode -ne 0 -or $Result.sensorError -or $Result.readings.Count -lt 5) { throw "Independent sensor verification failed: $($Result.sensorError)" }
    return $Result
}
$Summary = [ordered]@{ isAdministrator=$IsAdministrator; before=$null; withoutDeepCool=$null; helperRestored=$false; error=$null }
$OriginalHelperState = $null
try {
    $Summary.before = Invoke-SensorProbe 'independent-sensors-admin.json'
    if ($WithoutDeepCool) {
        $Helper = Get-Service -Name 'Deep Cool Helper Service' -ErrorAction SilentlyContinue
        if ($Helper) {
            $OriginalHelperState = $Helper.Status
            if ($Helper.Status -eq 'Running') {
                Stop-Service -InputObject $Helper
                $Helper.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10))
            }
        }
        if (Get-Process SensorBridgeServer,DeepCool -ErrorAction SilentlyContinue) { throw 'A DeepCool sensor/app process remains running; cannot establish independence.' }
        $Summary.withoutDeepCool = Invoke-SensorProbe 'independent-without-deepcool.json'
        $Summary.helperDuringTest = if ($Helper) { (Get-Service -Name $Helper.Name).Status.ToString() } else { 'Not installed' }
    }
} catch { $Summary.error = $_.ToString(); throw }
finally {
    if ($OriginalHelperState -eq 'Running') {
        Start-Service -Name 'Deep Cool Helper Service'
        (Get-Service -Name 'Deep Cool Helper Service').WaitForStatus('Running', [TimeSpan]::FromSeconds(10))
        $Summary.helperRestored = $true
    }
    $Summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Artifacts 'sensor-independence-verification.json')
}
