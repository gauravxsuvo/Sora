param([Parameter(Mandatory=$true)][string]$ArtifactDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'StartupTaskQualificationCommon.ps1')
. (Join-Path $PSScriptRoot 'InstalledHostQualificationCommon.ps1')
$result=[ordered]@{startedAt=[DateTimeOffset]::Now.ToString('o');passed=$false;reason=$null;recovery=$null;error=$null}
try {
    $baseline=Get-Content (Join-Path $ArtifactDirectory 'host-baseline.json') -Raw | ConvertFrom-Json
    if((Get-FileHash -LiteralPath $baseline.hardwareBaseline).Hash -cne $baseline.hardwareSha256) {throw 'Hardware recovery baseline hash mismatch.'}
    Save-HostEvidence (Join-Path $ArtifactDirectory 'host-watchdog-ready.json') @{pid=$PID;readyAt=[DateTimeOffset]::Now.ToString('o')}
    $deadline=[DateTimeOffset]::Now.AddMinutes(8)
    while($true) {
        if(Test-Path (Join-Path $ArtifactDirectory 'host-recovery-complete.json')) {$result.reason='runner completed recovery';break}
        $parent=Get-Process -Id $baseline.runnerPid -ErrorAction SilentlyContinue
        if(-not $parent -or $parent.StartTime.ToUniversalTime().Ticks -ne $baseline.runnerStartUtcTicks) {$result.reason='runner exited';break}
        if([DateTimeOffset]::Now -ge $deadline) {$result.reason='eight-minute deadline';break}
        Start-Sleep -Milliseconds 250
    }
    if($result.reason -eq 'runner completed recovery') {
        $done=Get-Content (Join-Path $ArtifactDirectory 'host-recovery-complete.json') -Raw | ConvertFrom-Json
        if(-not $done.passed -or @(Get-QualifiedHostProcesses).Count) {throw 'Runner marker did not establish completed recovery.'}
        $result.recovery=Invoke-HostTool $ArtifactDirectory 'watchdog-verify' @('verify',('"'+$baseline.hardwareBaseline+'"'),$baseline.hardwareSha256)
    } else {$result.recovery=Complete-InstalledHostRecovery $ArtifactDirectory 'watchdog-recovery';if(-not $result.recovery.passed) {throw 'Independent recovery failed; retain evidence and installation for investigation.'}}
    $result.passed=$true
} catch {$result.error=$_.Exception.ToString()}
$result.finishedAt=[DateTimeOffset]::Now.ToString('o');Save-HostEvidence (Join-Path $ArtifactDirectory 'host-watchdog-result.json') $result
