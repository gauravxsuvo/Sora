[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RecheckEvidenceDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
# Read-only preparation. Does not start SoraHost, stop services, change settings,
# install a package, register/run a task, or send hardware configuration changes.
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RecheckEvidenceDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output) {throw 'Choose a new evidence directory.'}
if(-not ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {throw 'Actual administrator token required for complete hardware readbacks.'}
New-Item -ItemType Directory -Path $output | Out-Null
. (Join-Path $PSScriptRoot 'StartupTaskQualificationCommon.ps1')
function Save([string]$Name,$Value) {
    # -InputObject preserves an empty array; pipeline serialization returns no text.
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 32))
    $stream=[IO.FileStream]::new((Join-Path $output $Name),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try {$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)} finally {$stream.Dispose()}
}
function DataState {
    $reference=Get-Content -LiteralPath (Join-Path $root 'continuation-shared-data-verified.json') -Raw | ConvertFrom-Json
    @(foreach($file in $reference.results) {
        $path=Join-Path $reference.dataRoot $file.relative
        $hash=$null;if(Test-Path -LiteralPath $path) {$hash=(Get-FileHash -LiteralPath $path).Hash}
        [ordered]@{relative=$file.relative;sha256=$hash;expectedSha256=$file.current;matchesCheckpoint=($hash -ceq $file.current)}
    })
}
function Services {
    @(Get-CimInstance Win32_Service | Where-Object {$_.PathName -match '(?i)DeepCool|Logitech|LGHUB|GIGABYTE|GCC|Kreo' -or $_.Name -in @('FanControl','EasyTuneEngineService','GvLedService')} |
        Select-Object Name,State,StartMode,PathName,ProcessId | Sort-Object Name)
}
function DesktopProcesses {
    @(Get-CimInstance Win32_Process | Where-Object {$_.Name -match '(?i)^(Sora|DeepCool|OemDrv|GCC|GCC32|lghub|lghub_agent|GvLedService|MSIAfterburner|PrecisionX_x64)\.exe$'} |
        Select-Object Name,ProcessId,ExecutablePath)
}
function Probe([string]$Name,[string]$Argument,[string[]]$Extra=@()) {
    $path=Join-Path $output ($Name+'.json')
    if(Test-Path -LiteralPath $path) {throw 'Probe output already exists.'}
    $start=[DateTimeOffset]::Now
    $process=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList (@($Argument,'--output',('"'+$path+'"'))+$Extra)
    try {
        if(-not $process.WaitForExit(60000)) {throw ('Read-only '+$Name+' exceeded its deadline. Inspect PID '+$process.Id+'; no configuration or service actions follow.')}
        if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $path)) {throw ('Read-only '+$Name+' failed with exit '+$process.ExitCode)}
        if((Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $start.UtcDateTime) {throw 'Stale probe output.'}
        $report=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if($report.error) {throw ('Read-only '+$Name+': '+$report.error)}
        $summary.probes+= [ordered]@{name=$Name;commandExitedSuccessfully=$true;sha256=(Get-FileHash -LiteralPath $path).Hash}
        return $report
    } finally {$process.Dispose()}
}
$summary=[ordered]@{startedAt=[DateTimeOffset]::Now.ToString('o');passed=$false;error=$null;probes=@();normalHostStarted=$false;configurationWritesRequested=0;serviceChangesRequested=0;taskChangesRequested=0;installedNormalStartupReady=$false;gates=@()}
try {
    $manifest=Get-Content (Join-Path $root 'installer-startup-routing/payload/sora-package.json') -Raw | ConvertFrom-Json
    foreach($file in $manifest.files) {
        if((Get-FileHash -LiteralPath (Join-Path $manifest.sourceDirectory $file.relative)).Hash -cne $file.sha256) {throw 'Previously qualified probe payload changed.'}
    }
    $exe=Join-Path $manifest.sourceDirectory 'Sora.exe'
    $summary.probeCandidate=$exe;$summary.probeVersion='0.10.7';$summary.payloadFilesVerified=@($manifest.files).Count
    $processes=@(DesktopProcesses);Save 'desktop-processes-before.json' $processes
    if($processes.Count) {throw 'Close Sora and competing vendor desktop applications before read-only preparation.'}
    $beforeServices=@(Services);Save 'services-before.json' $beforeServices
    $dataBefore=@(DataState);Save 'data-before.json' $dataBefore
    if($dataBefore.Count -ne 26 -or @($dataBefore | Where-Object {-not $_.matchesCheckpoint}).Count) {throw 'Shared data differs from the saved checkpoint; investigate without replacing it.'}
    $taskXml=Export-ScheduledTask -TaskName Sora -TaskPath '\'
    $taskSecurity=(Connect-StartupScheduler).GetTask('Sora').GetSecurityDescriptor(7)
    Save 'task-before.json' ([ordered]@{xml=$taskXml;securityDescriptor=$taskSecurity})
    $fan=Probe 'fans' '--fan-maintenance-capture'
    $previous=(Get-Content (Join-Path $root 'installed-startup-task-final-fans.json') -Raw | ConvertFrom-Json).Snapshot
    foreach($header in $previous.Headers) {
        $current=@($fan.Snapshot.Headers | Where-Object Header -eq $header.Header)
        if($current.Count -ne 1 -or ($current[0].Registers | ConvertTo-Json -Depth 8 -Compress) -cne ($header.Registers | ConvertTo-Json -Depth 8 -Compress) -or ($current[0].FirmwareStop | ConvertTo-Json -Compress) -cne ($header.FirmwareStop | ConvertTo-Json -Compress)) {throw 'Native/firmware fan state differs from the previous independent audit.'}
    }
    foreach($number in @(0,5)) {
        $header=@($fan.Snapshot.Headers | Where-Object Header -eq $number)
        if($header.Count -ne 1 -or -not $header[0].Automatic -or $null -eq $header[0].Rpm -or $header[0].Rpm -lt 700 -or $header[0].FirmwareStop.Enabled) {throw 'Automatic pump/radiator cooling preflight failed.'}
    }
    if($fan.Snapshot.TemperaturesC.Count -ne 6 -or @($fan.Snapshot.TemperaturesC | Where-Object {$null -eq $_ -or $_ -le 0 -or $_ -ge 75}).Count) {throw 'Fan temperatures outside qualification range.'}
    $summary.nativeBytesVerified=@($fan.Snapshot.Headers | ForEach-Object Registers).Count;$summary.firmwareHeadersVerified=$fan.Snapshot.Headers.Count
    $mouse=Probe 'mouse' '--logitech-diagnose'
    $oldMouse=Get-Content (Join-Path $root 'installed-startup-task-final-mouse.json') -Raw | ConvertFrom-Json
    foreach($name in @('buttonMap','indicator','settings','onboard')) {
        if(($mouse.$name | ConvertTo-Json -Depth 24 -Compress) -cne ($oldMouse.$name | ConvertTo-Json -Depth 24 -Compress)) {throw ('Original mouse state changed: '+$name)}
    }
    if($mouse.snapshot.Identity -cne $oldMouse.snapshot.Identity -or $mouse.snapshot.Sensors[0].Dpi -ne 2400 -or $mouse.snapshot.ReportRate -ne 1000 -or $mouse.snapshot.OnboardMode -ne 1) {throw 'Original mouse identity/runtime differs.'}
    $summary.mouseDpi=$mouse.snapshot.Sensors[0].Dpi;$summary.mouseOnboardSlots=@($mouse.onboard).Count
    $lighting=Probe 'lighting' '--lighting-probe'
    if($lighting.conflict -or $lighting.boardError -or $lighting.dramError -or @($lighting.dram.Dimms).Count -ne 2) {throw 'Lighting readback is incomplete or another writer is present.'}
    $summary.dimmCount=@($lighting.dram.Dimms).Count
    $readErrors=@($lighting.board.reads.PSObject.Properties | Where-Object Value -match '^error:' | ForEach-Object Name)
    $summary.boardDiagnosticReadErrors=$readErrors
    if($readErrors.Count) {throw 'A motherboard lighting diagnostic read failed.'}
    $hardware=Probe 'hardware' '--hardware-tools-probe'
    if($hardware.gpuError -or @($hardware.gpu).Count -ne 1 -or @($hardware.gpu[0].Fans).Count -ne 3) {throw 'GPU readback incomplete.'}
    $summary.gpuIdentity=$hardware.gpu[0].Identity
    $receiver=Probe 'receiver' '--receiver-diagnose'
    if(-not $receiver.receiver -or @($receiver.log).Count -ne 6 -or @($receiver.log | Where-Object {-not $_.Complete -or $_.BadChecksums -ne 0}).Count) {throw 'Receiver read-only sample incomplete/corrupt; do not infer full transport reliability.'}
    $summary.receiverReads=@($receiver.log).Count
    $cpu=Probe 'cpu' '--diagnose' @('--samples','5')
    if($cpu.sensorError -or @($cpu.readings).Count -lt 5 -or -not $cpu.outputHandleReady) {throw 'Independent CPU/display-handle readback incomplete.'}
    $summary.cpuSamples=@($cpu.readings).Count
    $summary.gates=@(
        'Normal installed profile startup/Exit not executed: independent multi-domain recovery supervisor not yet armed.',
        'Saved fan header 4 is Unknown: identify its physical role or stage a temporary radiator-only scope with exact data recovery.',
        'Motherboard lighting diagnostic reads do not capture every effect/keep/initialization state; exact raw rollback is not established.',
        'GPU native/software recovery needs a fresh raw control/curve/stop capture, not only this read-only snapshot.',
        'Mouse full recovery needs fresh selection/stage plus independent ownership restoration; snapshot alone is insufficient.',
        'Lighting worker join correction requires new-candidate Windows qualification; old installed package remains historical.',
        'Firmware installation/vendor removal remain HOLD; no verified backup.'
    )
    $summary.readbacksPassed=$true
} catch {$summary.error=$_.ToString()}
finally {
    try {
        $afterData=@(DataState);Save 'data-after.json' $afterData
        if($dataBefore -and ($afterData | ConvertTo-Json -Depth 8 -Compress) -cne ($dataBefore | ConvertTo-Json -Depth 8 -Compress)) {throw 'Data changed during read-only preparation.'}
        $afterServices=@(Services);Save 'services-after.json' $afterServices
        # Process IDs can change independently; preserve state/start mode and retain both inventories.
        $serviceFields=@('Name','State','StartMode','PathName')
        if($beforeServices -and ($afterServices | Select-Object $serviceFields | ConvertTo-Json -Compress) -cne ($beforeServices | Select-Object $serviceFields | ConvertTo-Json -Compress)) {throw 'Vendor service state/start mode changed.'}
        if($taskXml -and ((Export-ScheduledTask -TaskName Sora -TaskPath '\') -cne $taskXml -or (Connect-StartupScheduler).GetTask('Sora').GetSecurityDescriptor(7) -cne $taskSecurity)) {throw 'Startup XML/security changed.'}
        $summary.desktopProcessesAfter=@(DesktopProcesses)
        if($summary.desktopProcessesAfter.Count) {throw 'A desktop process remains.'}
        $summary.sharedDataStatesPreserved=0;if($dataBefore) {$summary.sharedDataStatesPreserved=$afterData.Count}
        $summary.servicesPreserved=[bool]$beforeServices;$summary.taskPreserved=[bool]$taskXml
    } catch {if($summary.error) {$summary.error+='; final state check: '+$_.ToString()} else {$summary.error=$_.ToString()}}
    $summary.passed=$summary.readbacksPassed -and -not $summary.error
    $summary.finishedAt=[DateTimeOffset]::Now.ToString('o');Save 'summary.json' $summary
}
if(-not $summary.passed) {throw $summary.error}
