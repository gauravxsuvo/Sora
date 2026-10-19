param(
    [Parameter(Mandatory)][string]$RecheckEvidenceDirectory,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [switch]$InstallAndChangeTaskForQualification
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'StartupTaskQualificationCommon.ps1')
if (-not $InstallAndChangeTaskForQualification) { throw 'Explicit temporary installation/task qualification switch is required.' }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run elevated.' }
$evidence = (Resolve-Path -LiteralPath $RecheckEvidenceDirectory).Path
$root = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $root) { throw 'Preserve earlier startup task qualification.' }
$target = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Sora'
$exe = Join-Path $target 'Sora.exe'
$registration = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{C29647D7-C220-477C-83B6-4118690E9CF8}_is1'
$link = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Sora\Sora.lnk'
if ((Test-Path $target) -or (Test-Path $registration) -or (Test-Path $link)) { throw 'Requires no existing installed Sora.' }
if (@(Get-CimInstance Win32_Process -Filter "Name='Sora.exe'").Count) { throw 'Close Sora through its tray menu.' }
$folder = Connect-StartupScheduler
$task = $folder.GetTask('Sora')
if ($task.State -eq 4 -or $task.Definition.Principal.LogonType -ne 3 -or $task.Definition.Actions.Count -ne 1) { throw 'Expected an idle original InteractiveToken task with one Exec action.' }
$originalXml = Export-ScheduledTask -TaskName Sora -TaskPath '\'
if ($originalXml -cne [IO.File]::ReadAllText((Join-Path $evidence 'installer-startup-routing-live\startup-task-before.xml'))) { throw 'Startup task changed since the qualified checkpoint.' }
$build = Get-Content (Join-Path $evidence 'installer-startup-routing\build-audit.json') -Raw | ConvertFrom-Json
$manifest = Get-Content (Join-Path $evidence 'installer-startup-routing\payload\sora-package.json') -Raw | ConvertFrom-Json
if ((Get-FileHash $build.installer).Hash -ne $build.installerSha256) { throw 'Installer hash changed.' }
New-Item -ItemType Directory -Path $root | Out-Null
[IO.File]::WriteAllText((Join-Path $root 'startup-task-before.xml'), $originalXml)
$baseline = [ordered]@{
    xmlSha256 = (Get-FileHash (Join-Path $root 'startup-task-before.xml')).Hash
    securityDescriptor = $task.GetSecurityDescriptor(7)
    runnerPid = $PID; runnerStartUtcTicks = (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks
    recoveryMutex = ('Local\Sora.StartupQualification.' + [Guid]::NewGuid().ToString('N'))
}
Save-StartupEvidence (Join-Path $root 'recovery-baseline.json') $baseline
$result = [ordered]@{ startedAt = [DateTimeOffset]::Now.ToString('o'); passed = $false; error = $null; cleanupErrors = @(); checks = @(); commands = @(); normalHostStarted = $false; hardwareConfigurationWritesRequested = 0; taskRunRequested = $false }
$installed = $false; $watchdog = $null; $taskMayHaveChanged = $false
function Check([string]$Name, [bool]$Passed) {
    $result.checks += [ordered]@{ name = $Name; passed = $Passed }
    if (-not $Passed) { throw ('Startup task check failed: ' + $Name) }
}
function Run([string]$Program, [string[]]$Arguments, [int]$Timeout = 30000) {
    if ($Arguments.Count -gt 0 -and $Arguments[0] -eq '--startup-task' -and $null -ne $watchdog -and $watchdog.HasExited) { throw 'Recovery watchdog ended; refusing further task commands.' }
    Save-StartupEvidence (Join-Path $root ('command-{0:D2}-started.json' -f ($result.commands.Count + 1))) @{ program = $Program; arguments = $Arguments; startedAt = [DateTimeOffset]::Now.ToString('o') }
    $p = Start-Process -FilePath $Program -WindowStyle Hidden -PassThru -ArgumentList $Arguments
    try {
        if (-not $p.WaitForExit($Timeout)) { throw "Command deadline; inspect PID $($p.Id). No process was killed." }
        $result.commands += [ordered]@{ program = $Program; arguments = $Arguments; exitCode = $p.ExitCode }
        return $p.ExitCode
    } finally { $p.Dispose() }
}
function Inspect([string]$Name, [string]$Program = $exe) {
    $output = Join-Path $root ($Name + '.json')
    Check ($Name + ' inspector exits successfully') ((Run $Program @('--startup-task-inspect','--output',('"' + $output + '"'))) -eq 0)
    return Get-Content $output -Raw | ConvertFrom-Json
}
function Payload([string]$Name) {
    $bad = @($manifest.files | Where-Object { -not (Test-Path (Join-Path $target $_.relative)) -or (Get-FileHash (Join-Path $target $_.relative)).Hash -ne $_.sha256 })
    Check $Name ($bad.Count -eq 0 -and (Get-FileHash (Join-Path $target 'sora-package.json')).Hash -eq $build.manifestSha256)
}
function Assert-TaskShape([string]$Name, [string]$ExpectedPath) {
    [xml]$xml = Export-ScheduledTask -TaskName Sora -TaskPath '\'
    [IO.File]::WriteAllText((Join-Path $root ($Name + '.xml')), $xml.OuterXml)
    $n = [Xml.XmlNamespaceManager]::new($xml.NameTable); $n.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    $command = $xml.SelectSingleNode('//t:Actions/t:Exec/t:Command', $n).InnerText.Trim('"')
    Check ($Name + ' action/arguments/working directory') ($command -eq $ExpectedPath -and $xml.Task.Actions.Exec.Arguments -eq '--startup' -and $xml.Task.Actions.Exec.WorkingDirectory -eq [IO.Path]::GetDirectoryName($ExpectedPath) -and $xml.Task.Actions.ChildNodes.Count -eq 1)
    $userText = [string]$xml.Task.Principals.Principal.UserId
    $sid = if ($userText.StartsWith('S-1-')) { $userText } else { ([Security.Principal.NTAccount]::new($userText)).Translate([Security.Principal.SecurityIdentifier]).Value }
    Check ($Name + ' current interactive user and highest available principal') ($sid -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -and $xml.Task.Principals.Principal.LogonType -eq 'InteractiveToken' -and $xml.Task.Principals.Principal.RunLevel -eq 'HighestAvailable')
    $registrationNow = $folder.GetTask('Sora')
    $triggers = $registrationNow.Definition.Triggers
    $enabledTriggers = @($triggers | Where-Object Enabled)
    $logonUser = [string]$xml.Task.Triggers.LogonTrigger.UserId
    $logonSid = if ($logonUser.StartsWith('S-1-')) { $logonUser } else { ([Security.Principal.NTAccount]::new($logonUser)).Translate([Security.Principal.SecurityIdentifier]).Value }
    # Scheduler omits default true Enabled elements from exported XML; read effective COM values.
    Check ($Name + ' enabled current-user sign-in/wake triggers and eight-second wake delay') ($registrationNow.Enabled -and $enabledTriggers.Count -eq 2 -and $logonSid -eq $sid -and $xml.Task.Triggers.EventTrigger.Delay -eq 'PT8S' -and $xml.Task.Triggers.EventTrigger.Subscription -match 'Power-Troubleshooter' -and $xml.Task.Triggers.EventTrigger.Subscription -match 'EventID=1' -and $xml.Task.Triggers.ChildNodes.Count -eq 2)
    Check ($Name + ' concurrency/recovery/battery policy') ($xml.Task.Settings.MultipleInstancesPolicy -eq 'IgnoreNew' -and $xml.Task.Settings.RestartOnFailure.Interval -eq 'PT1M' -and $xml.Task.Settings.RestartOnFailure.Count -eq '3' -and $xml.Task.Settings.DisallowStartIfOnBatteries -eq 'false' -and $xml.Task.Settings.StopIfGoingOnBatteries -eq 'false' -and $xml.Task.Settings.ExecutionTimeLimit -eq 'PT0S')
}
try {
    $watchdog = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + (Join-Path $PSScriptRoot 'Watch-StartupTaskQualification.ps1') + '"'),'-ArtifactDirectory',('"' + $root + '"'))
    for ($i = 0; $i -lt 40 -and -not (Test-Path (Join-Path $root 'watchdog-ready.json')); $i++) { Start-Sleep -Milliseconds 100 }
    Check 'Independent elevated recovery watchdog is ready before task changes' ((Test-Path (Join-Path $root 'watchdog-ready.json')) -and -not $watchdog.HasExited)
    $taskMayHaveChanged = $true
    $task.Enabled = $false
    Check 'Original registration safely disabled during qualification' (-not $folder.GetTask('Sora').Enabled)
    $code = Run $build.installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/LOG="' + (Join-Path $root 'install.log') + '"')) 60000
    $installed = Test-Path (Join-Path $target 'sora-package.json')
    Check 'Temporary qualified installation succeeds without reboot' ($code -eq 0 -and $installed)
    Payload 'Installed 96-file payload hashes match'
    foreach ($action in @('', 'enable', 'ON')) {
        $before = Export-ScheduledTask -TaskName Sora -TaskPath '\'
        $arguments = @('--startup-task'); if ($action) { $arguments += $action }
        $code = Run $exe $arguments
        Check ('Invalid explicit action rejected without task changes: [' + $action + ']') ($code -ne 0 -and (Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $before)
    }
    Check 'Installed explicit off removes an existing disabled registration' ((Run $exe @('--startup-task','off')) -eq 0 -and @(Get-ScheduledTask -TaskName Sora -TaskPath '\' -ErrorAction SilentlyContinue).Count -eq 0)
    Check 'Installed explicit off is idempotent with no registration' ((Run $exe @('--startup-task','off')) -eq 0)
    $inspection = Inspect 'absent-task'
    Check 'Production inspection reports absent task without enabling it' ($null -eq $inspection.inspection.Task -and @(Get-ScheduledTask -TaskName Sora -TaskPath '\' -ErrorAction SilentlyContinue).Count -eq 0)
    Check 'Installed explicit on registers successfully' ((Run $exe @('--startup-task','on')) -eq 0)
    Assert-TaskShape 'installed-enabled' $exe
    $before = Export-ScheduledTask -TaskName Sora -TaskPath '\'
    $inspection = Inspect 'installed-selected'
    Check 'Installed selection inspection is read-only and identifies this copy' ($inspection.inspection.Task.Enabled -and $inspection.inspection.Task.Path -eq $exe -and $inspection.target -eq $exe -and (Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $before)
    $code = Run (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall-enabled-task-refusal.log') + '"'))
    Check 'Uninstall refuses an enabled installed startup registration' ($code -ne 0 -and (Get-Content (Join-Path $root 'uninstall-enabled-task-refusal.log') -Raw) -match 'Turn off Start with Windows')
    Payload 'Enabled-task uninstall refusal preserves every payload hash'
    Check 'Enabled-task uninstall refusal preserves complete task XML' ((Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $before)
    $folder.GetTask('Sora').Enabled = $false
    $before = Export-ScheduledTask -TaskName Sora -TaskPath '\'
    $inspection = Inspect 'installed-disabled'
    Check 'Disabled installed registration stays disabled after inspection' (-not $inspection.inspection.Task.Enabled -and (Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $before)
    $code = Run (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall-disabled-task-refusal.log') + '"'))
    Check 'Uninstall also refuses a disabled task targeting the installed executable' ($code -ne 0 -and (Get-Content (Join-Path $root 'uninstall-disabled-task-refusal.log') -Raw) -match 'Turn off Start with Windows')
    Payload 'Disabled-task uninstall refusal preserves every payload hash'
    Check 'Disabled-task uninstall refusal preserves complete XML' ((Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $before)
    Check 'Installed off removes the disabled installed registration' ((Run $exe @('--startup-task','off')) -eq 0 -and @(Get-ScheduledTask -TaskName Sora -TaskPath '\' -ErrorAction SilentlyContinue).Count -eq 0)
    $candidate = Join-Path $manifest.sourceDirectory 'Sora.exe'
    Check 'Review development candidate can explicitly select itself' ((Run $candidate @('--startup-task','on')) -eq 0)
    Assert-TaskShape 'development-enabled' $candidate
    $folder.GetTask('Sora').Enabled = $false
    $before = Export-ScheduledTask -TaskName Sora -TaskPath '\'
    $inspection = Inspect 'development-selected-from-installed'
    Check 'Installed inspector preserves another deployment and disabled choice' ($inspection.inspection.Task.Path -eq $candidate -and -not $inspection.inspection.Task.Enabled -and (Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $before)
    Check 'Installed explicit off permits intentional reselection' ((Run $exe @('--startup-task','off')) -eq 0)
    Check 'Installed explicit on selects installed copy again' ((Run $exe @('--startup-task','on')) -eq 0)
    $folder.GetTask('Sora').Enabled = $false
    $before = Export-ScheduledTask -TaskName Sora -TaskPath '\'
    $inspection = Inspect 'installed-selected-from-development' $candidate
    Check 'Development inspector preserves installed route and disabled choice' ($inspection.inspection.Task.Path -eq $exe -and -not $inspection.inspection.Task.Enabled -and (Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $before)
    # Deliberate operation failure exercises recovery, without killing any process or running the task.
    try { throw 'Injected qualification failure after actual registration changes.' } catch { $result.injectedFailure = $_.Exception.Message }
    $result.injectedFailureRecovery = Restore-StartupQualificationTask $root
    Check 'Injected failure recovery restores original complete XML/security/enabled choice' $result.injectedFailureRecovery.passed
} catch { $result.error = $_.Exception.ToString() }
finally {
    $restored = -not $taskMayHaveChanged
    if ($taskMayHaveChanged) {
        try { $result.finalRecovery = Restore-StartupQualificationTask $root; $restored = $true }
        catch { $result.cleanupErrors += $_.Exception.ToString(); Save-StartupEvidence (Join-Path $root 'recovery-request.json') @{ requestedAt = [DateTimeOffset]::Now.ToString('o') } }
    }
    if ($installed -and $restored) {
        try {
            if ((Get-FileHash (Join-Path $target 'sora-package.json')).Hash -ne $build.manifestSha256) { throw 'Unexpected installation manifest; refusing cleanup.' }
            $code = Run (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall.log') + '"'))
            for ($i = 0; $i -lt 40 -and (Test-Path $target); $i++) { Start-Sleep -Milliseconds 100 }
            Check 'Supported uninstall succeeds after original workspace registration recovery' ($code -eq 0 -and -not (Test-Path $target) -and -not (Test-Path $registration) -and -not (Test-Path $link))
        } catch { $result.cleanupErrors += $_.Exception.ToString() }
    }
    if ($null -ne $watchdog) {
        try {
            if ($restored) { Save-StartupEvidence (Join-Path $root 'recovery-complete.json') @{ completedAt = [DateTimeOffset]::Now.ToString('o') } }
            if (-not $watchdog.WaitForExit(15000)) { throw 'Recovery watchdog remains active; inspect before continuing.' }
            $result.watchdog = Get-Content (Join-Path $root 'watchdog-result.json') -Raw | ConvertFrom-Json
            Check 'Independent watchdog verifies final original registration' ($result.watchdog.passed -and $result.watchdog.reason -eq 'runner completed' -and -not $result.watchdog.recovery.registrationWritten)
        } catch { $result.cleanupErrors += $_.Exception.ToString() }
        finally { $watchdog.Dispose() }
    }
    try {
        Check 'Final original task XML and security descriptor are unchanged' ((Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $originalXml -and $folder.GetTask('Sora').GetSecurityDescriptor(7) -ceq $baseline.securityDescriptor)
        Check 'No Sora process remains and no normal host was launched' (@(Get-CimInstance Win32_Process -Filter "Name='Sora.exe'").Count -eq 0)
    } catch { $result.cleanupErrors += $_.Exception.ToString() }
    $result.finishedAt = [DateTimeOffset]::Now.ToString('o')
    $result.passed = -not $result.error -and $result.cleanupErrors.Count -eq 0 -and @($result.checks | Where-Object {-not $_.passed}).Count -eq 0
    Save-StartupEvidence (Join-Path $root 'qualification.json') $result
}
if (-not $result.passed) { throw ('Startup task qualification failed: ' + (Join-Path $root 'qualification.json')) }
