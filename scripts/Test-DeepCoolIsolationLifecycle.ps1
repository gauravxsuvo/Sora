[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Output)
# Hardware-free checks of the SAME controller/recovery functions used by the real workflow.
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Choose a new evidence file.' }
. (Join-Path $PSScriptRoot 'DeepCoolIsolationLifecycle.ps1')

class FakeDeepCoolController {
    [string]$Status
    [string]$Failure
    [string]$AfterWaitStatus
    [System.Collections.Generic.List[string]]$Calls
    FakeDeepCoolController([string]$status, [string]$failure, [string]$afterWait) {
        $this.Status = $status; $this.Failure = $failure; $this.AfterWaitStatus = $afterWait
        $this.Calls = [System.Collections.Generic.List[string]]::new()
    }
    [void] Refresh() { $this.Calls.Add('Refresh') }
    [void] Stop() {
        $this.Calls.Add('Stop')
        if ($this.Failure -eq 'Stop') { throw 'Injected Stop failure.' }
        $this.Status = 'StopPending'
    }
    [void] Start() {
        $this.Calls.Add('Start')
        if ($this.Failure -eq 'Start') { throw 'Injected Start failure.' }
        $this.Status = 'StartPending'
    }
    [void] WaitForStatus([string]$target, [TimeSpan]$timeout) {
        $this.Calls.Add('Wait:' + $target)
        if ($timeout.TotalSeconds -ne 15) { throw 'Unexpected wait deadline.' }
        if ($this.Failure -eq ('Wait:' + $target)) { throw 'Injected status timeout.' }
        $this.Status = $target
        if ($this.AfterWaitStatus) { $this.Status = $this.AfterWaitStatus }
    }
}
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$Name, [scriptblock]$Body) {
    $failure = $null
    try { & $Body } catch { $failure = $_.ToString() }
    $results.Add([ordered]@{name=$Name;passed=($null -eq $failure);error=$failure})
}

$controllerCases = @(
    @{op='Stop';state='Running';calls='Stop,Wait:Stopped';error=$false},
    @{op='Stop';state='Stopped';calls='';error=$false},
    @{op='Stop';state='StopPending';calls='Wait:Stopped';error=$false},
    @{op='Stop';state='StartPending';calls='Wait:Running,Stop,Wait:Stopped';error=$false},
    @{op='Stop';state='Paused';calls='';error=$true},
    @{op='Stop';state='PausePending';calls='';error=$true},
    @{op='Stop';state='ContinuePending';calls='';error=$true},
    @{op='Stop';state='Running';failure='Stop';calls='Stop';error=$true},
    @{op='Stop';state='Running';failure='Wait:Stopped';calls='Stop,Wait:Stopped';error=$true},
    @{op='Stop';state='StartPending';failure='Wait:Running';calls='Wait:Running';error=$true},
    @{op='Stop';state='Running';afterWait='Running';calls='Stop,Wait:Stopped';error=$true},
    @{op='Start';state='Stopped';calls='Start,Wait:Running';error=$false},
    @{op='Start';state='Running';calls='';error=$false},
    @{op='Start';state='StartPending';calls='Wait:Running';error=$false},
    @{op='Start';state='StopPending';calls='Wait:Stopped,Start,Wait:Running';error=$false},
    @{op='Start';state='Paused';calls='';error=$true},
    @{op='Start';state='PausePending';calls='';error=$true},
    @{op='Start';state='ContinuePending';calls='';error=$true},
    @{op='Start';state='Stopped';failure='Start';calls='Start';error=$true},
    @{op='Start';state='Stopped';failure='Wait:Running';calls='Start,Wait:Running';error=$true},
    @{op='Start';state='StopPending';failure='Wait:Stopped';calls='Wait:Stopped';error=$true},
    @{op='Start';state='Stopped';afterWait='Stopped';calls='Start,Wait:Running';error=$true}
)
foreach ($case in $controllerCases) {
    Check ("controller/$($case.op)/$($case.state)/$($case.failure)/$($case.afterWait)") {
        $controller = [FakeDeepCoolController]::new($case.state, $case.failure, $case.afterWait)
        $threw = $false
        try {
            if ($case.op -eq 'Stop') { Stop-DeepCoolServiceController $controller }
            else { Start-DeepCoolServiceController $controller }
        } catch { $threw = $true }
        Require ($threw -eq $case.error) 'Incorrect success/failure outcome.'
        $calls = @($controller.Calls | Where-Object { $_ -ne 'Refresh' }) -join ','
        Require ($calls -eq $case.calls) "Incorrect service calls: $calls"
    }
}

$recoveryCases = @(
    @{name='normal';stops=1;restores=1;recovered=$true;errors=0;startErrors=0;audit=$true},
    @{name='late-stop-timeout-completes';lateStop=$true;stops=1;restores=1;recovered=$true;errors=1;startErrors=0;audit=$true},
    @{name='stop-timeout-then-settles';stopOnce=$true;stops=2;restores=2;recovered=$true;errors=2;startErrors=0;audit=$true},
    @{name='writer-relaunch-before-restore';relaunch=$true;stops=2;restores=2;recovered=$true;errors=1;startErrors=0;audit=$true},
    @{name='writer-never-quiesces';writerAlways=$true;stops=2;restores=2;recovered=$false;errors=4;startErrors=1;audit=$false},
    @{name='transient-partial-restore-failure';restoreOnce=$true;stops=2;restores=2;recovered=$true;errors=1;startErrors=0;audit=$true},
    @{name='persistent-restore-failure';restoreAlways=$true;stops=2;restores=2;recovered=$false;errors=2;startErrors=1;audit=$false},
    @{name='readback-mismatch-then-recovers';verifyOnce=$true;stops=2;restores=2;recovered=$true;errors=1;startErrors=0;audit=$true},
    @{name='fan-restart-fails-sensor-still-attempted';restartFailure='FanControl';stops=1;restores=1;recovered=$true;errors=0;startErrors=1;audit=$true},
    @{name='sensor-restart-fails-final-audit-still-attempted';restartFailure='Sensor';stops=1;restores=1;recovered=$true;errors=0;startErrors=1;audit=$true},
    @{name='post-restart-hardware-mismatch';auditFailure=$true;stops=1;restores=1;recovered=$true;errors=1;startErrors=0;audit=$false},
    @{name='originally-stopped-remains-unrequested';allStopped=$true;stops=1;restores=1;recovered=$true;errors=0;startErrors=0;audit=$true},
    @{name='previous-operation-error-is-preserved';previousError='Injected earlier probe failure';stops=1;restores=1;recovered=$true;errors=0;startErrors=0;audit=$true}
)
foreach ($case in $recoveryCases) {
    Check ('recovery/' + $case.name) {
        $state = @{stops=0;restores=0;writer=$true;verified=$false;writes=0;audits=0;starts=[System.Collections.Generic.List[string]]::new();events=[System.Collections.Generic.List[string]]::new()}
        $summary = [ordered]@{
            servicesBefore=@([pscustomobject]@{Name='FanControl';State='Running'},[pscustomobject]@{Name='Sensor';State='Running'},[pscustomobject]@{Name='Display';State='Stopped'})
            recoveredBeforeRestart=$false;recoveryErrors=@();restarted=@();serviceRestoreErrors=@();baselineMatchedAfterRestart=$false;error=$case.previousError
        }
        if ($case.allStopped) { foreach ($service in $summary.servicesBefore) { $service.State='Stopped' } }
        Complete-DeepCoolIsolationRecovery -Summary $summary -StopVendors {
            $state.stops++; $state.events.Add('stop')
            if ($case.writerAlways -or ($case.stopOnce -and $state.stops -eq 1)) { throw 'Injected Stop timeout with writer active.' }
            $state.writer=$false
            if ($case.lateStop) { throw 'Injected timeout completing before guarded recovery.' }
        } -RestoreAndVerify {
            param($attempt)
            $state.restores++; $state.events.Add('restore:' + $attempt)
            if ($case.relaunch -and $attempt -eq 1) { $state.writer=$true }
            if ($state.writer) { throw 'Competing writer present: no configuration write.' }
            $state.writes++
            if ($case.restoreAlways -or ($case.restoreOnce -and $attempt -eq 1)) { throw 'Injected recovery write failure.' }
            if ($case.verifyOnce -and $attempt -eq 1) { throw 'Injected full readback mismatch.' }
            $state.verified=$true; $state.events.Add('verified')
        } -RestartService {
            param($service)
            Require $state.verified 'Restart attempted before complete hardware verification.'
            $state.starts.Add($service.Name); $state.events.Add('start:' + $service.Name)
            if ($service.Name -eq $case.restartFailure) { throw 'Injected restart failure.' }
        } -VerifyAfterRestart {
            $state.audits++; $state.events.Add('audit')
            if ($case.auditFailure) { throw 'Injected post-restart hardware mismatch.' }
        }
        Require ($state.stops -eq $case.stops -and $state.restores -eq $case.restores) 'Incorrect bounded recovery attempts.'
        Require ($summary.recoveredBeforeRestart -eq $case.recovered) 'Incorrect recovery outcome.'
        Require ($summary.recoveryErrors.Count -eq $case.errors) 'A recovery error was lost or invented.'
        Require ($summary.serviceRestoreErrors.Count -eq $case.startErrors) 'A service error was lost or invented.'
        Require ($summary.baselineMatchedAfterRestart -eq $case.audit) 'Incorrect audit outcome.'
        Require ($summary.error -eq $case.previousError) 'Prior operation error was changed.'
        $expectedStarts = if ($case.recovered -and -not $case.allStopped) { 'FanControl,Sensor' } else { '' }
        Require (($state.starts -join ',') -eq $expectedStarts) 'Wrong service restart set/order.'
        Require ($state.audits -eq [int]$case.recovered) 'Final audit skipped or run before recovery.'
        Require (-not $case.writerAlways -or $state.writes -eq 0) 'Configuration write attempted with an active writer.'
        # The real qualification must remain failed after ANY retained error.
        $wouldPass = $summary.recoveredBeforeRestart -and $summary.baselineMatchedAfterRestart -and -not $summary.error -and $summary.recoveryErrors.Count -eq 0 -and $summary.serviceRestoreErrors.Count -eq 0
        Require ($wouldPass -eq ($case.name -in @('normal','originally-stopped-remains-unrequested'))) 'Recovery concealed an injected failure.'
    }
}
$result = [ordered]@{
    recordedAt=[DateTimeOffset]::Now.ToString('o');passed=(@($results | Where-Object {-not $_.passed}).Count -eq 0)
    checks=$results.Count;failed=@($results | Where-Object {-not $_.passed}).Count
    hardwareAccess=$false;serviceOperations=$false;configurationWrites=0
    sourceHash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'DeepCoolIsolationLifecycle.ps1')).Hash
    testHash=(Get-FileHash -LiteralPath $PSCommandPath).Hash;results=@($results.ToArray())
}
$bytes = [Text.Encoding]::UTF8.GetBytes(($result | ConvertTo-Json -Depth 12))
$file = [IO.FileStream]::new($Output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try { $file.Write($bytes,0,$bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
$result | ConvertTo-Json -Depth 12
if (-not $result.passed) { throw 'DeepCool lifecycle failure checks failed.' }
