# Shared by the real isolation workflow and hardware-free failure checks.
# These functions never terminate processes or change service startup types.
function Stop-DeepCoolServiceController($Controller) {
    $deadline = [TimeSpan]::FromSeconds(15)
    $Controller.Refresh()
    if ([string]$Controller.Status -eq 'StopPending') {
        $Controller.WaitForStatus('Stopped', $deadline)
    }
    elseif ([string]$Controller.Status -ne 'Stopped') {
        if ([string]$Controller.Status -eq 'StartPending') {
            $Controller.WaitForStatus('Running', $deadline)
            $Controller.Refresh()
        }
        if ([string]$Controller.Status -ne 'Running') { throw "Cannot safely stop service in state $($Controller.Status)." }
        $Controller.Stop()
        $Controller.WaitForStatus('Stopped', $deadline)
    }
    $Controller.Refresh()
    if ([string]$Controller.Status -ne 'Stopped') { throw 'Service did not remain stopped after the status wait.' }
}

function Start-DeepCoolServiceController($Controller) {
    $deadline = [TimeSpan]::FromSeconds(15)
    $Controller.Refresh()
    if ([string]$Controller.Status -eq 'StopPending') {
        $Controller.WaitForStatus('Stopped', $deadline)
        $Controller.Refresh()
    }
    if ([string]$Controller.Status -eq 'Stopped') {
        $Controller.Start()
        $Controller.WaitForStatus('Running', $deadline)
    }
    elseif ([string]$Controller.Status -eq 'StartPending') {
        $Controller.WaitForStatus('Running', $deadline)
    }
    elseif ([string]$Controller.Status -ne 'Running') {
        throw "Cannot safely start service in state $($Controller.Status)."
    }
    $Controller.Refresh()
    if ([string]$Controller.Status -ne 'Running') { throw 'Service did not remain running after the status wait.' }
}

function Complete-DeepCoolIsolationRecovery {
    param(
        [Parameter(Mandatory=$true)]$Summary,
        [Parameter(Mandatory=$true)][scriptblock]$StopVendors,
        # MUST assert all competing writers absent before any possible write.
        [Parameter(Mandatory=$true)][scriptblock]$RestoreAndVerify,
        [Parameter(Mandatory=$true)][scriptblock]$RestartService,
        [Parameter(Mandatory=$true)][scriptblock]$VerifyAfterRestart
    )
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        # A Stop timeout can complete after the exception. Retry quiescence, and
        # independently attempt guarded recovery even when Stop reported an error.
        # Keep the error: eventual recovery must not turn this into a passing test.
        try { & $StopVendors }
        catch { $Summary.recoveryErrors += ('Stop attempt ' + $attempt + ': ' + $_.Exception.Message) }
        try {
            & $RestoreAndVerify $attempt
            $Summary.recoveredBeforeRestart = $true
            break
        }
        catch { $Summary.recoveryErrors += ('Recovery attempt ' + $attempt + ': ' + $_.Exception.Message) }
    }
    if (-not $Summary.recoveredBeforeRestart) {
        $Summary.serviceRestoreErrors += 'Fan recovery could not be verified. No service restart was requested. Preserve baseline.json and inspect recovery evidence; a writer may still be active.'
        return
    }
    foreach ($service in $Summary.servicesBefore | Where-Object State -eq 'Running') {
        try {
            & $RestartService $service
            $Summary.restarted += $service.Name
        }
        catch { $Summary.serviceRestoreErrors += ($service.Name + ': ' + $_.Exception.Message) }
    }
    # A failed restart must not skip other services or the final hardware audit.
    try {
        & $VerifyAfterRestart
        $Summary.baselineMatchedAfterRestart = $true
    }
    catch { $Summary.recoveryErrors += ('Post-restart audit: ' + $_.Exception.Message) }
}
