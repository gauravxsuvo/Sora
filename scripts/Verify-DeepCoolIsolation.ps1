[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(1,10)][int]$Samples = 5,
    [switch]$InjectProbeFailure,
    [ValidateSet('None','AfterFirstStop','AfterFirstRestart')][string]$InjectServiceFailure = 'None'
)
# Qualification only. Does not uninstall anything or alter service startup types.
# Must run elevated with Sora and vendor desktops closed. Hardware handles are
# opened by short-lived probes and released BEFORE every service Stop/Start.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DeepCoolIsolationLifecycle.ps1')
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; earlier evidence must be preserved.' }
if (-not ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator PowerShell is required.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

function Save-NewJson([string]$Path, $Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 32))
    $file = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $file.Write($bytes,0,$bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
}
function Get-VendorServices {
    @(Get-CimInstance Win32_Service | Where-Object {
        $_.PathName -match '(?i)\\DeepCool\\' -or $_.Name -in @('FanControl','Deep Cool Helper Service','Deep Cool Display Service','JZFS Sensor Bridge Service','JZFS Display Service')
    } | Select-Object Name,State,StartMode,PathName,ProcessId)
}
function Assert-VendorAbsent {
    $active = @(Get-VendorServices | Where-Object State -ne 'Stopped')
    $processes = @(Get-CimInstance Win32_Process | Where-Object {
        $_.ExecutablePath -match '(?i)\\DeepCool\\' -or $_.Name -in @('DeepCool.exe','FanControlService.exe','SensorBridgeServer.exe','JZFSSensorBridgeServer.exe','JZFSDisplayService.exe','DeepCoolDisplayService.exe')
    })
    if ($active.Count -or $processes.Count) { throw ('A DeepCool writer is still present: ' + ($active.Name -join ',') + ' / ' + ($processes.Name -join ',')) }
}
function Invoke-FanTool([string]$Name, [switch]$Restore) {
    $path = Join-Path $OutputDirectory ($Name + '.json')
    $arguments = @('--fan-maintenance-capture','--output',('"' + $path + '"'))
    if ($Restore) {
        Assert-VendorAbsent
        $arguments = @('--fan-maintenance-restore','--baseline',('"' + $baselinePath + '"'),'--output',('"' + $path + '"'))
    }
    $started = [DateTimeOffset]::UtcNow
    $probe = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        # Do not kill a restore while it may be changing configuration; the controller
        # and firmware operations have their own bounded acquisition/call deadlines.
        if ($Restore) { $probe.WaitForExit() }
        elseif (-not $probe.WaitForExit(30000)) { $probe.Kill(); $probe.WaitForExit(5000) | Out-Null; throw 'Read-only fan capture exceeded its deadline.' }
        if (-not (Test-Path -LiteralPath $path)) { throw 'Fan maintenance tool produced no result.' }
        $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($probe.ExitCode -ne 0 -or $result.error -or ($Restore -and -not $result.passed)) { throw ('Fan maintenance command failed: ' + $result.error) }
        if ((Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $started.UtcDateTime) { throw 'Fan maintenance result is stale.' }
        return $result
    } finally { $probe.Dispose() }
}
function Assert-Cooling($Snapshot) {
    foreach ($header in @(0,5)) {
        $fan = @($Snapshot.Headers | Where-Object Header -eq $header)
        if ($fan.Count -ne 1 -or -not $fan[0].Automatic -or $fan[0].Rpm -lt 700 -or $null -eq $fan[0].Rpm -or $fan[0].FirmwareStop.Enabled) { throw "Cooling header $header failed automatic/RPM/firmware preflight." }
    }
    if (@($Snapshot.TemperaturesC).Count -ne 6 -or @($Snapshot.TemperaturesC | Where-Object { $null -eq $_ -or $_ -le 0 -or $_ -ge 75 }).Count) { throw 'Maintenance temperatures must be valid and below 75 degrees C.' }
}
function Assert-Baseline($Actual) {
    if ($Actual.Board -ne $baseline.Snapshot.Board -or $Actual.Controller -ne $baseline.Snapshot.Controller -or $Actual.FirmwareInterface -ne $baseline.Snapshot.FirmwareInterface -or @($Actual.Headers).Count -ne 6) { throw 'Fan hardware identity/interface differs from baseline.' }
    foreach ($header in $baseline.Snapshot.Headers) {
        $other = @($Actual.Headers | Where-Object Header -eq $header.Header)
        if ($other.Count -ne 1 -or
            ($header.Registers | ConvertTo-Json -Compress -Depth 6) -ne ($other[0].Registers | ConvertTo-Json -Compress -Depth 6) -or
            ($header.FirmwareStop | ConvertTo-Json -Compress) -ne ($other[0].FirmwareStop | ConvertTo-Json -Compress)) { throw "Native or firmware settings differ on header $($header.Header)." }
    }
    Assert-Cooling $Actual
}
function Stop-VendorServices {
    $failures = @()
    foreach ($service in Get-VendorServices) {
        if ($service.State -eq 'Stopped') { continue }
        # Record before requesting Stop: a timed-out request may finish afterward.
        $summary.stopRequested += $service.Name
        $controller = $null
        try {
            $controller = Get-Service -Name $service.Name
            Stop-DeepCoolServiceController $controller
            if ($InjectServiceFailure -eq 'AfterFirstStop' -and -not $faultState.triggered) {
                $faultState.triggered = $true
                throw 'Injected service-adapter failure after a completed Stop.'
            }
        }
        catch { $failures += ($service.Name + ': ' + $_.Exception.Message) }
        finally { if ($null -ne $controller) { $controller.Dispose() } }
    }
    try { Assert-VendorAbsent } catch { $failures += $_.Exception.Message }
    if ($failures.Count) { throw ($failures -join '; ') }
}

$baselinePath = Join-Path $OutputDirectory 'baseline.json'
$baseline = $null; $stopAttempted = $false; $operationError = $null
$faultState = @{triggered=$false}
$summary = [ordered]@{
    startedAt=[DateTimeOffset]::Now.ToString('o'); executable=$Executable; sha256=(Get-FileHash -LiteralPath $Executable).Hash
    injectedProbeFailure=[bool]$InjectProbeFailure; servicesBefore=@(); servicesAfter=@(); stopRequested=@()
    injectedServiceFailure=$InjectServiceFailure; serviceFailureInjected=$false
    recoveredBeforeRestart=$false; recoveryErrors=@(); restarted=@(); serviceRestoreErrors=@()
    probePassed=$false; baselineMatchedAfterRestart=$false; passed=$false; error=$null
}
try {
    if (Get-Process Sora,DeepCool,OemDrv -ErrorAction SilentlyContinue) { throw 'Exit Sora, DeepCool desktop and Kreo KB Hub before qualification.' }
    $summary.servicesBefore = @(Get-VendorServices)
    if (@($summary.servicesBefore | Where-Object { $_.State -notin @('Running','Stopped') }).Count) { throw 'A vendor service is transitioning; nothing was stopped.' }
    if (@($summary.servicesBefore | Where-Object { $_.Name -eq 'FanControl' -and $_.PathName -match '(?i)\\DeepCool\\.*FanControlService\.exe' }).Count -ne 1) { throw 'This workflow requires the identified DeepCool FanControl service.' }
    Save-NewJson (Join-Path $OutputDirectory 'services-before.json') $summary.servicesBefore
    $baseline = Invoke-FanTool 'baseline'
    Assert-Cooling $baseline.Snapshot
    # At this point the backup is durably flushed and every native/firmware header validated.
    $stopAttempted = $true
    Stop-VendorServices
    $stopped = Invoke-FanTool 'after-stop'
    # Recover the CURRENT baseline while the vendor is absent, before any restart can
    # capture the stale settings that its Stop handler just restored.
    $recovered = Invoke-FanTool 'restore-before-probe' -Restore
    $restored = Invoke-FanTool 'verified-before-probe'
    Assert-Baseline $restored.Snapshot
    if ($InjectProbeFailure) { throw 'Injected probe failure after stop and verified recovery.' }
    & (Join-Path $PSScriptRoot 'Verify-IndependentSensors.ps1') -Executable $Executable -OutputDirectory (Join-Path $OutputDirectory 'sensors') -Samples $Samples -WithoutDeepCool
    $sensorResult = Get-Content -LiteralPath (Join-Path $OutputDirectory 'sensors\summary.json') -Raw | ConvertFrom-Json
    if (-not $sensorResult.passed) { throw 'Independent sensor qualification failed.' }
    $summary.probePassed = $true
}
catch { $operationError = $_; $summary.error = $_.ToString() }
finally {
    if ($stopAttempted -and $null -ne $baseline) {
        # This also handles a partially completed Stop or a helper that relaunched.
        # Always restore BEFORE restart after caught stop/probe/recovery failures.
        Complete-DeepCoolIsolationRecovery -Summary $summary -StopVendors { Stop-VendorServices } -RestoreAndVerify {
                param($attempt)
                $recovery = Invoke-FanTool ('finally-restore-' + $attempt) -Restore
                $verified = Invoke-FanTool ('finally-verify-' + $attempt)
                Assert-Baseline $verified.Snapshot
            } -RestartService {
                param($service)
                $controller = Get-Service -Name $service.Name
                try { Start-DeepCoolServiceController $controller }
                finally { $controller.Dispose() }
                if ($InjectServiceFailure -eq 'AfterFirstRestart' -and -not $faultState.triggered) {
                    $faultState.triggered = $true
                    throw 'Injected service-adapter failure after a completed restart.'
                }
            } -VerifyAfterRestart {
                Start-Sleep -Seconds 3
                $after = Invoke-FanTool 'after-restart'
                Assert-Baseline $after.Snapshot
            }
    }
    try {
        $summary.servicesAfter = @(Get-VendorServices)
        foreach ($service in $summary.servicesBefore) {
            $afterService = @($summary.servicesAfter | Where-Object Name -eq $service.Name)
            if ($afterService.Count -ne 1 -or $afterService[0].State -ne $service.State -or $afterService[0].StartMode -ne $service.StartMode) { $summary.serviceRestoreErrors += ($service.Name + ': state/startup mode differs from baseline') }
        }
    } catch { $summary.serviceRestoreErrors += $_.ToString() }
    $summary.serviceFailureInjected = $faultState.triggered
    $summary.passed = $summary.probePassed -and $summary.recoveredBeforeRestart -and $summary.baselineMatchedAfterRestart -and
        -not $summary.error -and $summary.recoveryErrors.Count -eq 0 -and $summary.serviceRestoreErrors.Count -eq 0
    $summary.finishedAt = [DateTimeOffset]::Now.ToString('o')
    Save-NewJson (Join-Path $OutputDirectory 'summary.json') $summary
}
if (-not $summary.passed) {
    if ($operationError) { throw $operationError }
    throw 'DeepCool isolation did not pass. Inspect summary.json and recovery evidence.'
}
Write-Host "DeepCool isolation and native/firmware restoration passed: $OutputDirectory"
