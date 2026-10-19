[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RecheckEvidenceDirectory,[Parameter(Mandatory=$true)][string]$ArtifactDirectory,[string]$QualificationToolDirectory='lifecycle-qualification-tool',[string]$InstallerBuildDirectory='installer-mouse-native-routing',[ValidateRange(1,5)][int]$CycleCount=5,[switch]$SupervisedPhysicalTest,[switch]$SupervisedTrayExit,[switch]$InstallAndRunScopedHost,[switch]$ExclusiveInputSessionConfirmed)
$ErrorActionPreference='Stop'
if(-not $InstallAndRunScopedHost) {throw 'Explicit scoped qualification switch required.'}
if(-not $ExclusiveInputSessionConfirmed) {throw 'Live mouse/UI qualification requires an explicitly confirmed idle input session. Do not run during gaming or normal PC use.'}
if($SupervisedTrayExit -and $CycleCount -ne 1) {throw 'Run one supervised tray Exit cycle per invocation. Wait for operator acknowledgement and completed recovery before another launch.'}
if(-not ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {throw 'Actual administrator token required.'}
. (Join-Path $PSScriptRoot 'StartupTaskQualificationCommon.ps1')
. (Join-Path $PSScriptRoot 'InstalledHostQualificationCommon.ps1')
$evidence=(Resolve-Path -LiteralPath $RecheckEvidenceDirectory).Path
$root=[IO.Path]::GetFullPath($ArtifactDirectory)
if(Test-Path -LiteralPath $root) {throw 'Preserve prior qualification evidence.'}
$target='C:\Program Files\Sora';$exe=Join-Path $target 'Sora.exe'
$registration='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{C29647D7-C220-477C-83B6-4118690E9CF8}_is1'
if((Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath $registration) -or @(Get-QualifiedHostProcesses).Count) {throw 'Qualification requires no installed Sora and no running Sora.'}
if(@(Get-Process lghub_agent,DeepCool,OemDrv -ErrorAction SilentlyContinue).Count) {throw 'Competing vendor desktops are running.'}
New-Item -ItemType Directory -Path $root | Out-Null
$result=[ordered]@{startedAt=[DateTimeOffset]::Now.ToString('o');passed=$false;error=$null;checks=@();cycles=@();cleanupErrors=@();normalHostStarted=$false;scope='Saved mouse controls at 800 DPI, GPU Automatic and LD360 startup; motherboard fans/RAM/board/reactive lighting staged inactive';fullSavedProfileQualified=$false;servicesChanged=$false}
function Check([string]$Name,[bool]$Passed) {$result.checks+=@{name=$Name;passed=$Passed};if(-not $Passed) {throw $Name}}
function Run([string]$Path,[string[]]$Arguments,[int]$Timeout=60000) {
    if($Arguments.Count) {$p=Start-Process -FilePath $Path -WindowStyle Hidden -PassThru -ArgumentList $Arguments}
    else {$p=Start-Process -FilePath $Path -WindowStyle Hidden -PassThru}
    try {if(-not $p.WaitForExit($Timeout)) {throw ('Process deadline; inspect PID '+$p.Id)};return $p.ExitCode} finally {$p.Dispose()}
}
function Ui([int]$HostId,[string]$Action,[string]$Name) {Invoke-HostTool $root $Name @('ui',[string]$HostId,$Action)}
function Wait-Visible([int]$HostId,[int]$Count,[string]$Name) {
    for($attempt=1;$attempt -le 8;$attempt++) {
        $report=Ui $HostId 'inspect' ($Name+'-'+$attempt)
        if($report.visibleMainWindows -eq $Count) {return $report}
        Start-Sleep -Milliseconds 250
    }
    throw ('Expected visible window count '+$Count)
}
$armed=$false;$installed=$false;$watchdog=$null
try {
    $package=Get-Content (Join-Path $evidence ($InstallerBuildDirectory+'/build-audit.json')) -Raw | ConvertFrom-Json
    $manifest=Get-Content (Join-Path $evidence ($InstallerBuildDirectory+'/payload/sora-package.json')) -Raw | ConvertFrom-Json
    Check ('Exact reviewed '+$package.version+' setup hash') ((Get-FileHash -LiteralPath $package.installer).Hash -ceq $package.installerSha256)
    $reference=Get-Content (Join-Path $evidence 'continuation-shared-data-verified.json') -Raw | ConvertFrom-Json
    $data=@(foreach($file in $reference.results) {
        $path=Join-Path $reference.dataRoot $file.relative;$hash=$null;if(Test-Path -LiteralPath $path) {$hash=(Get-FileHash -LiteralPath $path).Hash}
        if($hash -cne $file.current) {throw 'Shared data differs; preserve it and investigate.'}
        if($hash) {$destination=Join-Path $root ('data-backup\'+$file.relative);New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null;Copy-Item -LiteralPath $path -Destination $destination}
        [ordered]@{relative=$file.relative;sha256=$hash}
    })
    foreach($relative in @('logitech\reactive-lighting.json.ownership','fans-service-ownership.json')) {
        if(Test-Path -LiteralPath (Join-Path $reference.dataRoot $relative)) {throw 'Unexpected existing runtime ownership; no changes requested.'}
    }
    $taskFolder=Connect-StartupScheduler;$task=$taskFolder.GetTask('Sora')
    if($task.State -notin @(1,3)) {throw 'Original task is not disabled or idle/ready.'}
    $xml=Export-ScheduledTask -TaskName Sora -TaskPath '\'
    [IO.File]::WriteAllText((Join-Path $root 'startup-task-before.xml'),$xml)
    $token=[guid]::NewGuid().ToString('N')
    Save-HostEvidence (Join-Path $root 'recovery-baseline.json') @{xmlSha256=(Get-FileHash (Join-Path $root 'startup-task-before.xml')).Hash;securityDescriptor=$task.GetSecurityDescriptor(7);recoveryMutex=('Local\Sora.TaskRecovery.'+$token)}
    $tool=Join-Path (Join-Path $evidence $QualificationToolDirectory) 'LifecycleQualification.exe'
    $toolRoot=Split-Path $tool
    $toolPrefix=$toolRoot.TrimEnd('\')+'\'
    $toolFiles=@(Get-ChildItem -LiteralPath $toolRoot -File -Recurse | ForEach-Object {[ordered]@{relative=$_.FullName.Substring($toolPrefix.Length);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    $control=[ordered]@{tool=$tool;toolSha256=(Get-FileHash -LiteralPath $tool).Hash;toolFiles=$toolFiles;dataRoot=$reference.dataRoot;data=$data;runnerPid=$PID;runnerStartUtcTicks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks;hostRecoveryMutex=('Local\Sora.HostRecovery.'+$token);hardwareBaseline=(Join-Path $root 'hardware-baseline.json');hardwareSha256=$null}
    # Baseline command does not depend on the control record (it is not written yet).
    Check 'Fresh independent mouse selection/stage/preferences/slots and raw GPU backup captured' ((Run $tool @('capture',('"'+$control.hardwareBaseline+'"'))) -eq 0)
    $control.hardwareSha256=(Get-FileHash -LiteralPath $control.hardwareBaseline).Hash
    Save-HostEvidence (Join-Path $root 'host-baseline.json') $control
    $null=Invoke-HostTool $root 'baseline-verify' @('verify',('"'+$control.hardwareBaseline+'"'),$control.hardwareSha256)
    $watchdog=Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $PSScriptRoot 'Watch-InstalledHostQualification.ps1')+'"'),'-ArtifactDirectory',('"'+$root+'"'))
    for($i=0;$i -lt 40 -and -not (Test-Path (Join-Path $root 'host-watchdog-ready.json'));$i++) {Start-Sleep -Milliseconds 100}
    Check 'Independent recovery supervisor ready before staging/startup' ((Test-Path (Join-Path $root 'host-watchdog-ready.json')) -and -not $watchdog.HasExited)
    $armed=$true
    Save-HostEvidence (Join-Path $root 'changes-armed.json') @{at=[DateTimeOffset]::Now.ToString('o');scope=$result.scope;hardwareSha256=$control.hardwareSha256}
    $task.Enabled=$false
    $lightingPath=Join-Path $reference.dataRoot 'lighting.json';$lighting=Get-Content -LiteralPath $lightingPath -Raw | ConvertFrom-Json
    $lighting.ApplyAtStartup=$false;$lighting.Sync=$false;$lighting.RainbowOnExit=$false;$lighting.KeepOnBoard=$false
    [IO.File]::WriteAllText($lightingPath,($lighting | ConvertTo-Json -Depth 20))
    $fanPath=Join-Path $reference.dataRoot 'fans.json';$fanPreferences=Get-Content -LiteralPath $fanPath -Raw | ConvertFrom-Json;$fanPreferences.ApplyAtStartup=$false
    [IO.File]::WriteAllText($fanPath,($fanPreferences | ConvertTo-Json -Depth 20))
    Remove-Item -LiteralPath (Join-Path $reference.dataRoot 'logitech\firmware-lighting.json')
    $gpuProfile=Get-Content (Join-Path $reference.dataRoot 'gpu-fans-profile.json') -Raw | ConvertFrom-Json
    Check 'GPU saved mode is Automatic with no native target' ($gpuProfile.Profile.Mode -eq 0 -and $null -eq $gpuProfile.NativeTarget)
    $setupLog=Join-Path $root 'install.log'
    $installCode=Run $package.installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/LOG="'+$setupLog+'"'))
    $installed=Test-Path -LiteralPath $exe
    Check ('Temporary '+$package.version+' install without reboot') ($installCode -eq 0 -and $installed)
    Check 'All 96 installed payload hashes match reviewed package' (@($manifest.files | Where-Object {(Get-FileHash -LiteralPath (Join-Path $target $_.relative)).Hash -cne $_.sha256}).Count -eq 0)
    $log=Join-Path $reference.dataRoot 'sora.log'
    for($cycle=1;$cycle -le $CycleCount;$cycle++) {
        if($watchdog.HasExited) {throw 'Recovery supervisor ended before qualification completed.'}
        $p=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList '--startup'
        Save-HostEvidence (Join-Path $root ('owned-host-'+$cycle+'.json')) @{pid=$p.Id;startUtcTicks=$p.StartTime.ToUniversalTime().Ticks;path=$exe}
        $result.normalHostStarted=$true
        try {
            Start-Sleep -Seconds 3
            Check ('Cycle '+$cycle+': one actual installed host') (@(Get-QualifiedHostProcesses).Count -eq 1 -and -not $p.HasExited)
            $null=Wait-Visible $p.Id 0 ('cycle-'+$cycle+'-hidden')
            $live=Invoke-HostTool $root ('cycle-'+$cycle+'-mouse-live') @('mouse-live')
            Check ('Cycle '+$cycle+': independent mouse 800 DPI/software mode/1000 Hz') ($live.snapshot.Sensors[0].Dpi -eq 800 -and $live.snapshot.OnboardMode -eq 2 -and $live.snapshot.ReportRate -eq 1000)
            Check ('Cycle '+$cycle+': standard native mouse routing retained') ($live.buttonMap -ceq '01020304050000000000000000000000')
            Check ('Cycle '+$cycle+': duplicate startup exits') ((Run $exe @('--startup') 15000) -eq 0)
            $null=Wait-Visible $p.Id 0 ('cycle-'+$cycle+'-duplicate-hidden')
            Check ('Cycle '+$cycle+': second normal launch exits into original host') ((Run $exe @() 15000) -eq 0 -and @(Get-QualifiedHostProcesses).Count -eq 1)
            $null=Wait-Visible $p.Id 1 ('cycle-'+$cycle+'-normal-open')
            $null=Ui $p.Id 'close' ('cycle-'+$cycle+'-normal-close')
            $null=Wait-Visible $p.Id 0 ('cycle-'+$cycle+'-normal-tray')
            $null=Ui $p.Id 'open' ('cycle-'+$cycle+'-tray-open')
            $null=Wait-Visible $p.Id 1 ('cycle-'+$cycle+'-tray-visible')
            $null=Ui $p.Id 'close' ('cycle-'+$cycle+'-tray-close')
            $null=Wait-Visible $p.Id 0 ('cycle-'+$cycle+'-tray-hidden')
            if($SupervisedPhysicalTest) {
                Save-HostEvidence (Join-Path $root ('cycle-'+$cycle+'-physical-ready.json')) @{hostPid=$p.Id;at=[DateTimeOffset]::Now.ToString('o')}
                $physical=Invoke-HostTool $root ('cycle-'+$cycle+'-physical-buttons') @('physical-buttons') 135000
                Check ('Cycle '+$cycle+': physical right/middle/back/forward down and up') $physical.passed
            }
            if($SupervisedPhysicalTest -or $SupervisedTrayExit) {
                # Keep four supervised repeat cycles within the independent eight-minute deadline.
                $userExitTimeout=120000;if($SupervisedTrayExit -and -not $SupervisedPhysicalTest) {$userExitTimeout=60000}
                Save-HostEvidence (Join-Path $root ('cycle-'+$cycle+'-awaiting-user-exit.json')) @{hostPid=$p.Id;at=[DateTimeOffset]::Now.ToString('o');timeoutMilliseconds=$userExitTimeout;instruction='User must select actual tray Exit Sora before the recorded deadline.'}
                $timer=[Diagnostics.Stopwatch]::StartNew();$exited=$p.WaitForExit($userExitTimeout)
            } else {
                $timer=[Diagnostics.Stopwatch]::StartNew();$null=Ui $p.Id 'exit' ('cycle-'+$cycle+'-tray-exit')
                $exited=$p.WaitForExit(30000)
            }
            $exitCode=$null;if($exited) {$exitCode=$p.ExitCode}
            Save-HostEvidence (Join-Path $root ('cycle-'+$cycle+'-exit-observed.json')) @{exited=$exited;exitCode=$exitCode;milliseconds=$timer.ElapsedMilliseconds;remaining=@(Get-QualifiedHostProcesses)}
            Check ('Cycle '+$cycle+': actual tray Exit completes') ($exited -and $exitCode -eq 0 -and @(Get-QualifiedHostProcesses).Count -eq 0)
            $restored=Invoke-HostTool $root ('cycle-'+$cycle+'-verified-original') @('verify',('"'+$control.hardwareBaseline+'"'),$control.hardwareSha256)
            Check ('Cycle '+$cycle+': complete original mouse/raw GPU restored') $restored.passed
            $result.cycles+=@{cycle=$cycle;passed=$true;exitMilliseconds=$timer.ElapsedMilliseconds;mouseDpi=800;windowOpenCloseCycles=2;independentOriginalStateVerified=$true}
        } catch {
            $cycleError=$_.Exception.ToString()
            $hostExited=$p.HasExited;$hostExitCode=$null;if($hostExited){$hostExitCode=$p.ExitCode}
            Save-HostEvidence (Join-Path $root ('cycle-'+$cycle+'-failure-observation.json')) @{at=[DateTimeOffset]::Now.ToString('o');error=$cycleError;hostPid=$p.Id;hostExited=$hostExited;hostExitCode=$hostExitCode;remaining=@(Get-QualifiedHostProcesses)}
            throw
        } finally {$p.Dispose()}
    }
    if(Test-Path -LiteralPath $log) {Copy-Item -LiteralPath $log -Destination (Join-Path $root 'sora-runtime-log.txt')}
} catch {$result.error=$_.Exception.ToString()}
finally {
    if($armed) {
        $recovery=Complete-InstalledHostRecovery $root 'runner-recovery'
        $result.recovery=$recovery
        if($recovery.passed) {Save-HostEvidence (Join-Path $root 'host-recovery-complete.json') @{passed=$true;at=[DateTimeOffset]::Now.ToString('o')}}
        else {$result.cleanupErrors+=$recovery.error}
    }
    if($watchdog) {
        if(-not $watchdog.WaitForExit(20000)) {$result.cleanupErrors+='Recovery supervisor remains active; inspect before proceeding.'}
        elseif(Test-Path (Join-Path $root 'host-watchdog-result.json')) {$result.watchdog=Get-Content (Join-Path $root 'host-watchdog-result.json') -Raw | ConvertFrom-Json;if(-not $result.watchdog.passed) {$result.cleanupErrors+=$result.watchdog.error}}
        $watchdog.Dispose()
    }
    if($installed -and $result.recovery.passed -and @(Get-QualifiedHostProcesses).Count -eq 0) {
        try {
            Check 'Temporary installed package removed through supported uninstaller' ((Run (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $root 'uninstall.log')+'"'))) -eq 0)
            for($attempt=0;$attempt -lt 40 -and (Test-Path -LiteralPath $target);$attempt++) {Start-Sleep -Milliseconds 250}
        } catch {$result.cleanupErrors+=$_.ToString()}
    }
    $result.installationRetained=Test-Path -LiteralPath $target
    $result.finishedAt=[DateTimeOffset]::Now.ToString('o')
    $result.requestedCycles=$CycleCount;$result.supervisedPhysicalTest=[bool]$SupervisedPhysicalTest;$result.supervisedTrayExit=[bool]($SupervisedPhysicalTest -or $SupervisedTrayExit)
    $result.passed= -not $result.error -and $result.cleanupErrors.Count -eq 0 -and $result.cycles.Count -eq $CycleCount -and -not $result.installationRetained
    Save-HostEvidence (Join-Path $root 'qualification.json') $result
}
if(-not $result.passed) {throw ('Installed host qualification failed; inspect '+$root)}
