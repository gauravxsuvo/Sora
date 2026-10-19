[CmdletBinding()]
param(
    [string]$Executable = 'dist\Sora-current\Sora.exe',
    [string]$OutputDirectory,
    [ValidateRange(1,20)][int]$Samples = 5,
    [switch]$WithoutDeepCool
)
# -WithoutDeepCool temporarily stops installed display/sensor services.
# A running fan helper requires a separate hardware-state-preserving workflow:
# DeepCool 1.2.14 restores its startup fan snapshot on Stop, which can overwrite
# settings changed since it started. Desktop processes are never killed.
# For controlled isolation with complete native/firmware backup and recovery, use
# Verify-DeepCoolIsolation.ps1 with an explicitly built maintenance-capable candidate.
$ErrorActionPreference = 'Stop'
$TaskRoot = Split-Path $PSScriptRoot -Parent
$Executable = (Resolve-Path -LiteralPath $(if ([IO.Path]::IsPathRooted($Executable)) { $Executable } else { Join-Path $TaskRoot $Executable })).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $TaskRoot ('artifacts\sensor-independence-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory to preserve prior verification evidence.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$IsAdministrator = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $IsAdministrator) { throw 'Run this verification script from Administrator PowerShell.' }

function Get-DeepCoolServices {
    @(Get-CimInstance Win32_Service | Where-Object {
        $_.PathName -match '(?i)\\DeepCool\\' -or
        $_.Name -in @('Deep Cool Helper Service','Deep Cool Display Service','JZFS Sensor Bridge Service','JZFS Display Service')
    } | Select-Object Name,State,StartMode,PathName,ProcessId)
}
function Get-DeepCoolProcesses {
    @(Get-CimInstance Win32_Process | Where-Object {
        $_.ExecutablePath -match '(?i)\\DeepCool\\' -or
        $_.Name -in @('DeepCool.exe','SensorBridgeServer.exe','DeepCoolDisplayService.exe','JZFSSensorBridgeServer.exe','JZFSDisplayService.exe','FanControlService.exe')
    } | Select-Object Name,ProcessId,ExecutablePath)
}
function Assert-DeepCoolAbsent {
    $activeServices = @(Get-DeepCoolServices | Where-Object State -ne 'Stopped')
    $activeProcesses = @(Get-DeepCoolProcesses)
    if ($activeServices.Count -or $activeProcesses.Count) {
        throw ('DeepCool isolation failed. Services: ' + ($activeServices.Name -join ', ') + '; processes: ' + ($activeProcesses.Name -join ', ') + '. Exit the vendor app and inspect auto-relaunch before repeating.')
    }
}
function Invoke-SensorProbe([string]$Name, [switch]$RequireIsolation) {
    $output = Join-Path $OutputDirectory ($Name + '.json')
    $started = [DateTimeOffset]::UtcNow
    if ($RequireIsolation) { Assert-DeepCoolAbsent }
    $modules = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $process = Start-Process -FilePath $Executable -ArgumentList @('--diagnose','--samples',"$Samples",'--output',('"' + $output + '"')) -PassThru -WindowStyle Hidden
    try {
        while (-not $process.WaitForExit(500)) {
            if (([DateTimeOffset]::UtcNow - $started).TotalSeconds -gt (25 + $Samples)) { throw 'Sora sensor verification exceeded its deadline.' }
            if ($RequireIsolation) { Assert-DeepCoolAbsent }
            try { foreach ($module in $process.Modules) { $modules.Add($module.FileName) | Out-Null } } catch { }
        }
        if ($RequireIsolation) { Assert-DeepCoolAbsent }
        if (-not (Test-Path -LiteralPath $output)) { throw 'The sensor probe produced no output.' }
        $file = Get-Item -LiteralPath $output
        if ($file.Length -eq 0 -or $file.LastWriteTimeUtc -lt $started.UtcDateTime) { throw 'The sensor result is empty or stale.' }
        $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        if ($process.ExitCode -ne 0 -or $result.sensorError -or @($result.readings).Count -lt $Samples -or
            $result.sensorSource -notmatch 'LibreHardwareMonitor.*PawnIO' -or $result.hardwareWrites -ne 0) {
            throw "Independent sensor verification failed: $($result.sensorError)"
        }
        $timestamps = @($result.readings | ForEach-Object { [DateTimeOffset]::Parse($_.ReceivedAt).UtcDateTime })
        if (@($timestamps | Where-Object { $_ -lt $started.UtcDateTime }).Count -or @($timestamps | Sort-Object -Unique).Count -lt $Samples) { throw 'Sensor readings are stale or duplicated.' }
        $observed = @($modules | Sort-Object)
        $observed | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory ($Name + '-observed-modules.json')) -Encoding utf8
        if ($RequireIsolation -and @($observed | Where-Object { $_ -match '(?i)\\DeepCool\\' }).Count) { throw 'A DeepCool module was observed in the isolated sensor probe.' }
        return $result
    }
    finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
        $process.Dispose()
    }
}

$summary = [ordered]@{
    capturedAt=[DateTimeOffset]::Now.ToString('o'); executable=$Executable
    sha256=(Get-FileHash -LiteralPath $Executable).Hash; samplesRequested=$Samples
    isAdministrator=$IsAdministrator; isolationRequested=[bool]$WithoutDeepCool
    servicesBefore=@(); processesBefore=@(); servicesDuringTest=@(); processesDuringTest=@()
    before=$null; withoutDeepCool=$null; restoredServices=@(); restoredStoppedServices=@(); servicesAfter=@()
    restoreFailures=@(); passed=$false; error=$null
}
$changed = [Collections.Generic.List[string]]::new()
$restoreFailures = [Collections.Generic.List[string]]::new()
$failure = $null
$isolationStarted = $false
try {
    $summary.servicesBefore = @(Get-DeepCoolServices)
    $summary.processesBefore = @(Get-DeepCoolProcesses)
    if ($WithoutDeepCool -and @($summary.servicesBefore | Where-Object { $_.State -ne 'Stopped' -and ($_.PathName -match '(?i)FanControlService\.exe' -or $_.Name -eq 'FanControl') }).Count) {
        throw 'The running DeepCool fan helper restores a potentially stale hardware snapshot when stopped. This standalone verifier will not stop it. Use a qualified fan-state backup/recovery workflow first; no services were changed.'
    }
    $summary.before = Invoke-SensorProbe 'before'
    if ($WithoutDeepCool) {
        if (@($summary.servicesBefore | Where-Object { $_.State -notin @('Running','Stopped') }).Count) { throw 'A DeepCool service is in a transitional or paused state; isolation was not started.' }
        $isolationStarted = $true
        foreach ($record in $summary.servicesBefore | Where-Object State -eq 'Running') {
            # Record before requesting Stop: a timed-out stop can still complete later.
            $changed.Add($record.Name)
            Stop-Service -Name $record.Name
            (Get-Service -Name $record.Name).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10))
        }
        Assert-DeepCoolAbsent
        $summary.servicesDuringTest = @(Get-DeepCoolServices)
        $summary.processesDuringTest = @(Get-DeepCoolProcesses)
        $summary.withoutDeepCool = Invoke-SensorProbe 'without-deepcool' -RequireIsolation
    }
    $summary.passed = $true
}
catch { $failure = $_; $summary.error = $_.ToString(); $summary.passed = $false }
finally {
    # A previously stopped helper may relaunch during the probe. Restore that state too.
    if ($isolationStarted) {
        foreach ($record in $summary.servicesBefore | Where-Object State -eq 'Stopped') {
            try {
                $service = Get-Service -Name $record.Name
                if ($service.Status -ne 'Stopped') {
                    Stop-Service -Name $record.Name
                    (Get-Service -Name $record.Name).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10))
                    $summary.restoredStoppedServices += $record.Name
                }
            }
            catch { $restoreFailures.Add($record.Name + ': ' + $_.Exception.Message) }
        }
    }
    $restoreOrder = $changed.ToArray()
    [array]::Reverse($restoreOrder)
    foreach ($serviceName in $restoreOrder) {
        try {
            Start-Service -Name $serviceName
            (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(10))
            $summary.restoredServices += $serviceName
        }
        catch { $restoreFailures.Add($serviceName + ': ' + $_.Exception.Message) }
    }
    # No startup types or scheduled tasks are altered by this script.
    try {
        $summary.servicesAfter = @(Get-DeepCoolServices)
        if ($isolationStarted) {
            foreach ($record in $summary.servicesBefore) {
                $after = @($summary.servicesAfter | Where-Object Name -eq $record.Name)
                if ($after.Count -ne 1 -or $after[0].State -ne $record.State -or $after[0].StartMode -ne $record.StartMode) {
                    $restoreFailures.Add($record.Name + ': final service state/start mode differs from baseline')
                }
            }
        }
    }
    catch { $restoreFailures.Add('Final service inventory: ' + $_.Exception.Message) }
    $summary.restoreFailures = @($restoreFailures.ToArray())
    if ($restoreFailures.Count) { $summary.passed = $false }
    $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
}
if ($failure) { throw $failure }
if ($restoreFailures.Count) { throw ('Service restoration failed: ' + ($restoreFailures -join '; ')) }
Write-Host "Sensor verification completed: $OutputDirectory"
