function Save-StartupEvidence([string]$Path, $Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 24))
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Connect-StartupScheduler {
    $scheduler = New-Object -ComObject Schedule.Service
    $scheduler.Connect()
    return $scheduler.GetFolder('\')
}
function Restore-StartupQualificationTask([string]$Directory) {
    $backup = Get-Content (Join-Path $Directory 'recovery-baseline.json') -Raw | ConvertFrom-Json
    $xmlPath = Join-Path $Directory 'startup-task-before.xml'
    if ((Get-FileHash -LiteralPath $xmlPath).Hash -ne $backup.xmlSha256) { throw 'Startup recovery XML hash mismatch.' }
    $mutex = [Threading.Mutex]::new($false, $backup.recoveryMutex)
    $held = $false
    try {
        try { $held = $mutex.WaitOne(10000) } catch [Threading.AbandonedMutexException] { $held = $true }
        if (-not $held) { throw 'Startup recovery lock timeout.' }
        $folder = Connect-StartupScheduler
        $xml = [IO.File]::ReadAllText($xmlPath)
        $current = $null
        try { $current = $folder.GetTask('Sora') } catch {
            if ($_.Exception.HResult -ne -2147024894 -and $_.Exception.InnerException.HResult -ne -2147024894) { throw }
        }
        $unchanged = $null -ne $current -and (Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $xml -and $current.GetSecurityDescriptor(7) -ceq $backup.securityDescriptor
        $permissionsWritten = $false
        if (-not $unchanged) {
            # Passing SDDL to RegisterTask embeds a new XML SecurityDescriptor element.
            # Restore XML separately, then preserve owner/group/DACL using the registered-task API.
            # 0x20 suppresses registration triggers; no task Run call is made.
            $restored = $folder.RegisterTask('Sora', $xml, (6 -bor 0x20), $null, $null, 3, $null)
            if ($restored.GetSecurityDescriptor(7) -cne $backup.securityDescriptor) {
                $restored.SetSecurityDescriptor($backup.securityDescriptor, 0x10)
                $permissionsWritten = $true
            }
        }
        $current = $folder.GetTask('Sora')
        if ((Export-ScheduledTask -TaskName Sora -TaskPath '\') -cne $xml -or $current.GetSecurityDescriptor(7) -cne $backup.securityDescriptor) { throw 'Original startup XML/security descriptor was not recovered exactly.' }
        return [ordered]@{ passed = $true; registrationWritten = -not $unchanged; permissionsWritten = $permissionsWritten; enabled = [bool]$current.Enabled; path = [string]$current.Definition.Actions.Item(1).Path }
    } finally { if ($held) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
}
