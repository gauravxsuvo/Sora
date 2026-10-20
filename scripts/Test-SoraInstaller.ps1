param(
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$RecheckEvidenceDirectory,
    [switch]$InstallForQualification,
    [switch]$VerifyStartupInspection
)
$ErrorActionPreference = 'Stop'
if (-not $InstallForQualification) { throw 'This qualification temporarily installs Sora in Program Files. Supply -InstallForQualification to run it.' }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run installer qualification elevated.' }
$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
$root = [IO.Path]::GetFullPath($ArtifactDirectory)
$evidence = (Resolve-Path -LiteralPath $RecheckEvidenceDirectory).Path
$target = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Sora'
$link = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Sora\Sora.lnk'
$registration = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{C29647D7-C220-477C-83B6-4118690E9CF8}_is1'
if (Test-Path -LiteralPath $root) { throw 'Preserve earlier installation qualification.' }
if ((Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath $link) -or (Test-Path -LiteralPath $registration)) { throw 'Qualification requires no existing Program Files Sora installation or common Sora shortcut.' }
if (@(Get-CimInstance Win32_Process -Filter "Name='Sora.exe'").Count) { throw 'Close Sora through its tray menu first.' }
New-Item -ItemType Directory -Path $root | Out-Null
$audit = Get-Content (Join-Path $build 'build-audit.json') -Raw | ConvertFrom-Json
$manifest = Get-Content (Join-Path $build 'payload\sora-package.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $audit.installer).Hash -ne $audit.installerSha256) { throw 'Installer hash does not match the build audit.' }
$priorData = Get-Content (Join-Path $evidence 'continuation-shared-data-verified.json') -Raw | ConvertFrom-Json
$dataRoot = $priorData.dataRoot
$taskBefore = Export-ScheduledTask -TaskName Sora -TaskPath '\'
[IO.File]::WriteAllText((Join-Path $root 'startup-task-before.xml'), $taskBefore)
$servicesBefore = @(Get-CimInstance Win32_Service | Where-Object PathName -match '\\DeepCool\\' | Select-Object Name,State,StartMode | Sort-Object Name)
$stablePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\Sora-current'
$stableBefore = @(if (Test-Path -LiteralPath $stablePath) { (Get-Item -LiteralPath $stablePath).Target })
$result = [ordered]@{ startedAt = [DateTimeOffset]::Now.ToString('o'); passed = $false; error = $null; checks = @(); probes = @(); cleanupError = $null; normalStartupTested = $false; hardwareConfigurationWrites = 0 }
function Save-NewJson([string]$Path, $Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 18))
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Check([string]$Name, [bool]$Passed) {
    $result.checks += [ordered]@{ name = $Name; passed = $Passed }
    if (-not $Passed) { throw "Qualification failed: $Name" }
}
function Run([string]$Exe, [string[]]$Arguments, [int]$Timeout = 60000) {
    $proc = Start-Process -FilePath $Exe -WindowStyle Hidden -PassThru -ArgumentList $Arguments
    try {
        if (-not $proc.WaitForExit($Timeout)) { throw "Process still running after deadline; inspect PID $($proc.Id) before retrying: $Exe" }
        return $proc.ExitCode
    } finally { $proc.Dispose() }
}
function Setup([string]$Name, [string[]]$Extra = @()) {
    $log = Join-Path $root ($Name + '.log')
    return Run $audit.installer (@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/LOG="' + $log + '"')) + $Extra)
}
function Assert-Payload([string]$Name) {
    $verified = @($manifest.files | Where-Object {
        $path = Join-Path $target $_.relative
        -not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $_.sha256
    }).Count -eq 0
    Check $Name ($verified -and (Get-FileHash (Join-Path $target 'sora-package.json')).Hash -eq $audit.manifestSha256)
}
function Probe([string]$Name, [string[]]$Arguments, [int]$Timeout = 60000) {
    $output = Join-Path $root ($Name + '.json')
    $code = Run (Join-Path $target 'Sora.exe') ($Arguments + @('--output',('"' + $output + '"'))) $Timeout
    $result.probes += [ordered]@{ name = $Name; exitCode = $code; output = $output }
    Check ($Name + ' exit/output') ($code -eq 0 -and (Test-Path -LiteralPath $output))
    return Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
}
$installedByThisRun = $false
$fixtureProcess = $null
try {
    # A benign, isolated process exercises actual process enumeration; it never opens hardware.
    $fixturePath = Join-Path $root 'Sora.exe'
    Add-Type -TypeDefinition 'public static class InstallerProcessFixture { public static void Main() { System.Threading.Thread.Sleep(60000); } }' -OutputAssembly $fixturePath -OutputType ConsoleApplication
    $fixtureProcess = Start-Process -FilePath $fixturePath -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 250
    $code = Setup 'running-refusal'
    Check 'Setup refuses an active Sora process without creating the destination' ($code -ne 0 -and -not (Test-Path -LiteralPath $target) -and (Get-Content (Join-Path $root 'running-refusal.log') -Raw) -match 'Exit Sora from its tray menu')
    $fixtureProcess.Kill(); $fixtureProcess.WaitForExit(); $fixtureProcess.Dispose(); $fixtureProcess = $null
    $wrongDestination = Join-Path $root 'unsupported-destination'
    $code = Setup 'directory-refusal' @('/DIR="' + $wrongDestination + '"')
    Check 'Setup rejects a destination outside Program Files before copying files' ($code -ne 0 -and -not (Test-Path -LiteralPath $wrongDestination) -and -not (Test-Path -LiteralPath $target))
    $code = Setup 'clean-install'
    $installedByThisRun = Test-Path -LiteralPath (Join-Path $target 'sora-package.json')
    Check 'Clean install succeeded without requesting a reboot' ($code -eq 0 -and $installedByThisRun)
    Assert-Payload 'All installed payload hashes match the qualified candidate'
    Check 'No app or hardware profile launched automatically' (@(Get-CimInstance Win32_Process -Filter "Name='Sora.exe'").Count -eq 0)
    $registered = Get-ItemProperty -LiteralPath $registration
    Check '64-bit Apps registration has correct version and install location' ($registered.DisplayVersion -eq $manifest.version -and $registered.InstallLocation.TrimEnd('\') -eq $target)
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($link)
    Check 'Start menu shortcut targets the installed executable' ($shortcut.TargetPath -eq (Join-Path $target 'Sora.exe') -and $shortcut.WorkingDirectory -eq $target)
    $acl = Get-Acl -LiteralPath $target
    $acl.Sddl | Set-Content (Join-Path $root 'installation-acl.txt')
    $writeMask = [Security.AccessControl.FileSystemRights]::Write -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    $broadWrites = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object {
        $sid = $_.IdentityReference.Value
        $sid -in @('S-1-1-0','S-1-5-11','S-1-5-32-545') -and $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band $writeMask)
    })
    Check 'Program Files root ACL grants no broad Users/Everyone/Authenticated Users writes' ($broadWrites.Count -eq 0)
    if ($VerifyStartupInspection) {
        $inspection = Probe 'startup-routing-inspection' @('--startup-task-inspect')
        [xml]$originalTask = $taskBefore
        $originalCommand = ([string]$originalTask.Task.Actions.Exec.Command).Trim('"')
        Check 'Installed startup inspection reports the existing task without migration' ($inspection.executable -eq (Join-Path $target 'Sora.exe') -and $inspection.target -eq (Join-Path $target 'Sora.exe') -and $inspection.inspection.Readable -and $inspection.inspection.Task.Path -eq $originalCommand)
        Check 'Production startup inspection leaves the complete task XML unchanged' ((Export-ScheduledTask -TaskName Sora -TaskPath '\') -ceq $taskBefore)
    }
    $ui = Probe 'maintenance-ui' @('--maintenance-ui-check')
    Check 'All six installed maintenance UI scenarios pass' ($ui.passed -and $ui.checks.Count -eq 6 -and $ui.configurationWrites -eq 0)
    $hardware = Probe 'hardware-tools' @('--hardware-tools-probe')
    Check 'Installed storage, GPU and Wi-Fi libraries load without probe errors' ($null -eq $hardware.storageError -and $null -eq $hardware.gpuError -and $null -eq $hardware.wifiError -and @($hardware.storage).Count -eq 2 -and $hardware.configurationWrites -eq 0)
    $cpu = Probe 'cpu-display-readonly' @('--diagnose','--samples','2')
    Check 'Installed CPU sampling and LD output-handle access succeed without output' ($cpu.readings.Count -eq 2 -and $cpu.outputHandleReady -and $cpu.hardwareWrites -eq 0)
    $lighting = Probe 'lighting-readonly' @('--lighting-probe')
    Check 'Installed motherboard and DIMM lighting probes report no lighting writes' ($lighting.administrator -and $lighting.lightingWrites -eq 0)
    $mouse = Probe 'logitech-readonly' @('--logitech-diagnose')
    Check 'Installed mouse probe reads original 2400 DPI without configuration writes' ($mouse.snapshot.Sensors[0].Dpi -eq 2400 -and $mouse.snapshot.ReportRate -eq 1000 -and $mouse.snapshot.OnboardMode -eq 1 -and $mouse.configurationWrites -eq 0)
    $fans = Probe 'fans-readonly' @('--fan-maintenance-capture')
    $expected = (Get-Content (Join-Path $evidence 'deepcool-lifecycle-final-readonly.json') -Raw | ConvertFrom-Json).Snapshot
    $actual = $fans.Snapshot
    $matches = @($actual.Headers).Count -eq 6
    foreach ($header in $expected.Headers) {
        $now = @($actual.Headers | Where-Object Header -eq $header.Header)
        if ($now.Count -ne 1 -or ($header.Registers | ConvertTo-Json -Depth 8 -Compress) -ne ($now[0].Registers | ConvertTo-Json -Depth 8 -Compress) -or ($header.FirmwareStop | ConvertTo-Json -Compress) -ne ($now[0].FirmwareStop | ConvertTo-Json -Compress)) { $matches = $false }
    }
    Check 'All 258 native fan bytes and six firmware configurations are preserved' ($matches -and @($actual.Headers | ForEach-Object Registers).Count -eq 258)
    foreach ($fan in $actual.Headers | Where-Object Header -in @(0,5)) { Check ('Cooling remains automatic, header ' + $fan.Header) ($fan.Automatic -and $fan.Rpm -gt 700 -and -not $fan.FirmwareStop.Enabled) }
    $result.nativeBytesVerified = 258; $result.firmwareHeadersVerified = 6
    $code = Setup 'same-version-repair'
    Check 'Same-version repair succeeded' ($code -eq 0)
    Assert-Payload 'Same-version repair retains all payload hashes'
    $fixtureProcess = Start-Process -FilePath $fixturePath -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 250
    $code = Run (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall-running-refusal.log') + '"'))
    Check 'Uninstaller refuses while a Sora process is active' ($code -ne 0 -and (Test-Path -LiteralPath (Join-Path $target 'Sora.exe')))
    Assert-Payload 'Refused uninstall leaves installed payload intact'
    $fixtureProcess.Kill(); $fixtureProcess.WaitForExit(); $fixtureProcess.Dispose(); $fixtureProcess = $null
} catch { $result.error = $_.ToString() }
finally {
    if ($null -ne $fixtureProcess) { if (-not $fixtureProcess.HasExited) { $fixtureProcess.Kill(); $fixtureProcess.WaitForExit() }; $fixtureProcess.Dispose() }
    if ($installedByThisRun) {
        try {
            if ((Get-FileHash -LiteralPath (Join-Path $target 'sora-package.json')).Hash -ne $audit.manifestSha256) { throw 'Do not uninstall an installation with an unexpected manifest.' }
            $code = Run (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall.log') + '"'))
            Check 'Qualification installation uninstalled without a reboot request' ($code -eq 0)
            for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $target); $attempt++) { Start-Sleep -Milliseconds 250 }
            Check 'Qualification folder, common shortcut and Apps entry removed' (-not (Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath $link) -and -not (Test-Path -LiteralPath $registration))
        } catch { $result.cleanupError = $_.ToString() }
    }
    try {
        $result.sharedData = @(foreach ($file in $priorData.results) {
            $path = Join-Path $dataRoot $file.relative
            $hash = $null; if (Test-Path -LiteralPath $path) { $hash = (Get-FileHash -LiteralPath $path).Hash }
            [ordered]@{ relative = $file.relative; expected = $file.current; actual = $hash; unchanged = $file.current -eq $hash }
        })
        Check 'All 26 shared-data states match the prior qualified baseline' ($result.sharedData.Count -eq 26 -and @($result.sharedData | Where-Object {-not $_.unchanged}).Count -eq 0)
        $taskAfter = Export-ScheduledTask -TaskName Sora -TaskPath '\'
        [IO.File]::WriteAllText((Join-Path $root 'startup-task-after.xml'), $taskAfter)
        Check 'Existing startup task XML unchanged' ($taskAfter -ceq $taskBefore)
        Check 'Existing stable deployment junction unchanged' ((@(if (Test-Path -LiteralPath $stablePath) { (Get-Item -LiteralPath $stablePath).Target }) | ConvertTo-Json -Compress) -ceq ($stableBefore | ConvertTo-Json -Compress))
        $result.services = @(Get-CimInstance Win32_Service | Where-Object PathName -match '\\DeepCool\\' | Select-Object Name,State,StartMode | Sort-Object Name)
        Check 'Original DeepCool service states and start modes unchanged' (($result.services | ConvertTo-Json -Compress) -ceq ($servicesBefore | ConvertTo-Json -Compress))
        Check 'No Sora process remains' (@(Get-CimInstance Win32_Process -Filter "Name='Sora.exe'").Count -eq 0)
    } catch { if (-not $result.error) { $result.error = $_.ToString() } else { $result.error += '; final audit: ' + $_.ToString() } }
    $result.installerSha256 = $audit.installerSha256
    $result.installedPath = $target
    $result.installationRetained = Test-Path -LiteralPath $target
    $result.startupChanged = $taskAfter -cne $taskBefore
    $result.finishedAt = [DateTimeOffset]::Now.ToString('o')
    $result.passed = -not $result.error -and -not $result.cleanupError -and @($result.checks | Where-Object {-not $_.passed}).Count -eq 0 -and -not $result.installationRetained
    Save-NewJson (Join-Path $root 'qualification.json') $result
}
if (-not $result.passed) { throw ('Installer qualification failed; inspect ' + (Join-Path $root 'qualification.json')) }
