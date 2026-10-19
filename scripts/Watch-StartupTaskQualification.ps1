param([Parameter(Mandatory)][string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'StartupTaskQualificationCommon.ps1')
$result = [ordered]@{ startedAt = [DateTimeOffset]::Now.ToString('o'); passed = $false; reason = $null; recovery = $null; error = $null }
try {
    $baseline = Get-Content (Join-Path $ArtifactDirectory 'recovery-baseline.json') -Raw | ConvertFrom-Json
    if ((Get-FileHash (Join-Path $ArtifactDirectory 'startup-task-before.xml')).Hash -ne $baseline.xmlSha256) { throw 'Watchdog refused a corrupt recovery baseline.' }
    $null = Connect-StartupScheduler
    Save-StartupEvidence (Join-Path $ArtifactDirectory 'watchdog-ready.json') @{ pid = $PID; readyAt = [DateTimeOffset]::Now.ToString('o') }
    $deadline = [DateTimeOffset]::Now.AddSeconds(300)
    while ($true) {
        if (Test-Path (Join-Path $ArtifactDirectory 'recovery-complete.json')) { $result.reason = 'runner completed'; break }
        if (Test-Path (Join-Path $ArtifactDirectory 'recovery-request.json')) { $result.reason = 'runner requested recovery'; break }
        $parentProcess = Get-Process -Id $baseline.runnerPid -ErrorAction SilentlyContinue
        if ($null -eq $parentProcess -or $parentProcess.StartTime.ToUniversalTime().Ticks -ne $baseline.runnerStartUtcTicks) { $result.reason = 'runner exited'; break }
        if ([DateTimeOffset]::Now -ge $deadline) { $result.reason = 'five-minute recovery deadline'; break }
        Start-Sleep -Milliseconds 250
    }
    $result.recovery = Restore-StartupQualificationTask $ArtifactDirectory
    $result.passed = $true
} catch { $result.error = $_.Exception.ToString() }
finally {
    $result.finishedAt = [DateTimeOffset]::Now.ToString('o')
    Save-StartupEvidence (Join-Path $ArtifactDirectory 'watchdog-result.json') $result
}
