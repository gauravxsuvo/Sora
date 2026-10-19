function Invoke-QualifiedHostExitPolicy {
    param([scriptblock]$TryGraceful,[scriptblock]$IsRunning,[scriptblock]$StopOwned,[scriptblock]$WaitStopped)
    $result=[ordered]@{gracefulCompleted=$false;forcedTerminationUsed=$false;gracefulError=$null;stopped=$false}
    try {& $TryGraceful} catch {$result.gracefulError=$_.Exception.Message}
    if(-not (& $IsRunning)) {$result.gracefulCompleted= -not $result.gracefulError;$result.stopped=$true;return $result}
    # The live adapter must verify a durable owned-PID/start-time/path record before
    # this fallback. This policy never enumerates or terminates a process itself.
    & $StopOwned
    $result.forcedTerminationUsed=$true
    $null=& $WaitStopped
    if(& $IsRunning) {throw 'The owned test host remains; no competing hardware recovery writes are permitted.'}
    $result.stopped=$true;return $result
}
