param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-beta\.\d+)?$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$Sha256,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [string]$CandidateInstaller
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# This test deliberately purges synthetic app data. Never run on a personal PC or self-hosted runner.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:OS -ne 'Windows_NT') { throw 'Requires a disposable GitHub-hosted Windows runner.' }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Qualification needs an administrator token.' }
$root = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $root) { throw 'Preserve earlier evidence.' }
[IO.Directory]::CreateDirectory($root) | Out-Null
$install = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Sora'
$exe = Join-Path $install 'Sora.exe'
$registration = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{C29647D7-C220-477C-83B6-4118690E9CF8}_is1'
$profileRegistry = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
$realProfiles = @(Get-ChildItem $profileRegistry | Where-Object PSChildName -Match '^S-1-(5-21|12-1)-' | ForEach-Object { [Environment]::ExpandEnvironmentVariables((Get-ItemProperty $_.PSPath).ProfileImagePath) } | Where-Object { Test-Path -LiteralPath $_ })
$syntheticProfile = Join-Path $env:RUNNER_TEMP 'SoraQualificationProfile'
$sidKey = Join-Path $profileRegistry ('S-1-5-21-170019-170020-170021-' + (Get-Random -Minimum 10000 -Maximum 99999))
$profiles = @($realProfiles) + @($syntheticProfile)
$dataRoots = @($profiles | ForEach-Object { Join-Path $_ 'AppData\Local\Sora'; Join-Path $_ 'AppData\Roaming\Sora'; Join-Path $_ 'Downloads\Sora updates'; Join-Path $_ 'AppData\Local\Temp\.net\Sora' }) + @(Join-Path $env:ProgramData 'Sora'; Join-Path $env:ProgramData 'Sora-Updates'; Join-Path $install 'data')
$commonLink = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Sora\Sora.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Sora.lnk'
foreach ($path in @($install,$registration,$commonLink,$desktopLink,$syntheticProfile) + $dataRoots) {
    if (Test-Path -LiteralPath $path) { throw "Runner is not clean: $path" }
}
if (Get-ScheduledTask -TaskName Sora -ErrorAction SilentlyContinue) { throw 'Runner already has a Sora task.' }
if (Get-Process Sora,Sora.UpdateRunner -ErrorAction SilentlyContinue) { throw 'Runner already has Sora running.' }
$checks = [Collections.Generic.List[object]]::new()
$result = [ordered]@{ passed = $false; version = $Version; sha256 = $Sha256; githubRun = $env:GITHUB_RUN_ID; checks = $checks; error = $null }
function Check([string]$Name, [bool]$Passed) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed })
    Write-Output "$Passed : $Name"
    if (-not $Passed) {
        if (Test-Path -LiteralPath $install) {
            Get-ChildItem -LiteralPath $install -Force | Select-Object Name,Attributes,Length |
                ConvertTo-Json | Set-Content (Join-Path $root 'installation-leftovers.json')
        }
        throw "Failed: $Name"
    }
}
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class SoraQualificationDialogs {
    delegate bool Callback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    public static int DismissErrors() {
        int count=0;
        EnumWindows((window, parameter) => {
            var title = new StringBuilder(512); GetWindowText(window,title,title.Capacity);
            if(title.ToString() == "Sora uninstall") { count++; SendMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero); }
            return true;
        },IntPtr.Zero);
        return count;
    }
}
'@
function Run([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (-not $process.WaitForExit(200)) {
        [SoraQualificationDialogs]::DismissErrors() | Out-Null
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out: $File, PID $($process.Id)" }
    }
    $code = $process.ExitCode; $process.Dispose(); return $code
}
function Setup([string]$File, [string]$Label, [string[]]$Extra = @()) {
    return Run $File (@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/LOG="' + (Join-Path $root ($Label + '.log')) + '"')) + $Extra)
}
function Uninstall([string]$Label, [switch]$Purge) {
    $extra = @(); if ($Purge) { $extra += '/PURGEDATA=1' }
    $code = Setup (Join-Path $install 'unins000.exe') $Label $extra
    # Inno's launcher can return while its temporary uninstaller is still finishing.
    # Wait for the actual uninstall log to close before checking removal or reinstalling.
    $log = Join-Path $root ($Label + '.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $text = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
        if ($text -match 'Log closed\.') { break }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Uninstaller did not finish its log: $Label" }
        Start-Sleep -Milliseconds 200
    } while ($true)
    if ($code -eq 0 -and $text -notmatch 'Uninstallation process succeeded\.') { throw "Uninstall launcher returned success without completed removal: $Label" }
    return $code
}
function Payload([string]$Label) {
    $manifest = Get-Content (Join-Path $install 'sora-package.json') -Raw | ConvertFrom-Json
    $bad = @($manifest.files | Where-Object { $path = Join-Path $install $_.relative; -not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $_.sha256 })
    Check $Label ($manifest.version -eq $Version -and $bad.Count -eq 0)
}
function SeedData {
    foreach ($path in $dataRoots) { [IO.Directory]::CreateDirectory($path) | Out-Null; [IO.File]::WriteAllText((Join-Path $path 'qualification.txt'), 'synthetic saved Sora configuration') }
}
function DataPreserved {
    return @($dataRoots | Where-Object { $file = Join-Path $_ 'qualification.txt'; -not (Test-Path -LiteralPath $file) -or [IO.File]::ReadAllText($file) -ne 'synthetic saved Sora configuration' }).Count -eq 0
}
function Shortcut([string]$Path, [string]$Target) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    $link = $shell.CreateShortcut($Path); $link.TargetPath = $Target; $link.IconLocation = $Target + ',0'; $link.Save()
}
function WaitUninstalled {
    for ($i = 0; $i -lt 40 -and (Test-Path -LiteralPath $exe); $i++) { Start-Sleep -Milliseconds 250 }
    return -not (Test-Path -LiteralPath $exe) -and -not (Test-Path -LiteralPath $registration)
}
$fixtureProcess = $null
try {
    $candidate = Join-Path $root "Sora-Setup-$Version-x64.exe"
    if ($CandidateInstaller) {
        Copy-Item -LiteralPath (Resolve-Path -LiteralPath $CandidateInstaller).Path -Destination $candidate
    } else {
        Invoke-WebRequest "https://sora.portways.app/releases/$Version/win-x64/Sora-Setup-$Version-x64.exe" -OutFile $candidate
    }
    Check 'Public candidate bytes match the expected release hash' ((Get-FileHash $candidate).Hash -eq $Sha256)
    $previous = Join-Path $root 'Sora-Setup-0.12.1-x64.exe'
    Invoke-WebRequest 'https://sora.portways.app/releases/0.12.1/win-x64/Sora-Setup-0.12.1-x64.exe' -OutFile $previous
    Check 'Previous stable installer matches its known hash' ((Get-FileHash $previous).Hash -eq '417CB1B6F1441A5042FB2CD191232896BFBA852780267791C3E3D23F6140E696')
    Check 'Previous stable installs successfully' ((Setup $previous 'install-previous') -eq 0)
    Check 'Setup never starts hardware controls automatically' (-not (Get-Process Sora -ErrorAction SilentlyContinue))
    [IO.Directory]::CreateDirectory($syntheticProfile) | Out-Null
    New-Item $sidKey | Out-Null; New-ItemProperty $sidKey -Name ProfileImagePath -Value $syntheticProfile | Out-Null
    SeedData
    $shell = New-Object -ComObject WScript.Shell
    $ownedLink = Join-Path $syntheticProfile 'Desktop\Sora.lnk'
    $ownedMenuLink = Join-Path $syntheticProfile 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Sora\Sora.lnk'
    $foreignLink = Join-Path $syntheticProfile 'OneDrive\Desktop\Sora.lnk'
    Shortcut $ownedLink $exe; Shortcut $ownedMenuLink $exe; Shortcut $foreignLink (Join-Path $env:SystemRoot 'System32\notepad.exe')
    $foreignHash = (Get-FileHash $foreignLink).Hash
    Register-ScheduledTask -TaskName Sora -Action (New-ScheduledTaskAction -Execute $exe) | Out-Null
    $taskBefore = Export-ScheduledTask -TaskName Sora
    Check 'Upgrade from 0.12.1 succeeds' ((Setup $candidate 'upgrade') -eq 0)
    Payload 'Upgraded payload matches every manifest hash'
    Check 'Upgrade retains all saved data across registered profiles' (DataPreserved)
    Check 'Upgrade preserves startup task definition' ((Export-ScheduledTask -TaskName Sora) -ceq $taskBefore)
    $numericVersion = ($Version -split '-')[0]
    $icon = Join-Path $install "Assets\sora-brand-$numericVersion.ico"
    foreach ($linkPath in @($commonLink,$ownedLink,$ownedMenuLink)) { Check "Versioned icon on $linkPath" ($shell.CreateShortcut($linkPath).IconLocation -eq ($icon + ',0')) }
    Check 'Foreign shortcut is unchanged' ((Get-FileHash $foreignLink).Hash -eq $foreignHash)
    Check 'Same-version repair succeeds' ((Setup $candidate 'repair') -eq 0)
    Payload 'Repair retains all payload hashes'
    Check 'Downgrade is refused' ((Setup $previous 'downgrade-refusal') -ne 0)
    Payload 'Refused downgrade leaves payload unchanged'
    $uiFile = Join-Path $root 'maintenance-ui.json'
    Check 'Installed app maintenance UI runs successfully' ((Run $exe @('--maintenance-ui-check','--output',('"' + $uiFile + '"'))) -eq 0)
    $ui = Get-Content $uiFile -Raw | ConvertFrom-Json
    Check 'Installed maintenance UI scenarios pass without configuration writes' ($ui.passed -and $ui.configurationWrites -eq 0)
    if ([version]($Version -split '-')[0] -ge [version]'0.13.0') {
        $legionUiFile = Join-Path $root 'legion-ui.json'
        Check 'Installed Legion button interaction check runs successfully' ((Run $exe @('--legion-ui-check','--output',('"' + $legionUiFile + '"'))) -eq 0)
        $legionUi = Get-Content $legionUiFile -Raw | ConvertFrom-Json
        Check 'Installed Legion interaction scenarios pass with zero physical configuration writes' ($legionUi.passed -and $legionUi.synthetic -and $legionUi.configurationWrites -eq 0)
    }
    $numericCandidate = [version]($Version -split '-')[0]
    if ($numericCandidate -gt [version]'0.13.0' -or ($numericCandidate -eq [version]'0.13.0' -and ($Version -notmatch '-beta\.' -or [int]($Version -split '-beta\.')[1] -ge 3))) {
        $updateUiFile = Join-Path $root 'application-update-ui.json'
        Check 'Installed beta channel Check and Download buttons run successfully' ((Run $exe @('--application-update-ui-check','--output',('"' + $updateUiFile + '"'))) -eq 0)
        $updateUi = Get-Content $updateUiFile -Raw | ConvertFrom-Json
        Check 'Installed updater rejects tampering and isolates channels without executing fixture installers' ($updateUi.passed -and $updateUi.synthetic -and $updateUi.configurationWrites -eq 0 -and $updateUi.installerExecutions -eq 0)
    }
    for ($cycle = 1; $cycle -le 3; $cycle++) {
        $image = Join-Path $root "hardware-ui-$cycle.png"
        Check "Installed read-only hardware UI cycle $cycle exits successfully" ((Run $exe @('--screenshot',('"' + $image + '"'),'--settings','--isolated-ui-check','--hardware-ui-check')) -eq 0)
        $hardwareUi = Get-Content ($image + '.hardware-checks.json') -Raw | ConvertFrom-Json
        $shutdown = Get-Content ($image + '.shutdown-checks.json') -Raw | ConvertFrom-Json
        Check "Installed hardware availability cycle $cycle passes" ($hardwareUi.passed)
        Check "Shutdown cycle $cycle joins an in-flight scan without configuration writes" ($shutdown.passed -and $shutdown.configurationWrites -eq 0)
        Check "Read-only hardware UI cycle $cycle leaves no Sora process" (-not (Get-Process Sora -ErrorAction SilentlyContinue))
    }
    $fixture = Join-Path $root 'fixture'
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'Fixture.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><AssemblyName>Sora</AssemblyName></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $fixture 'Program.cs'), 'System.Threading.Thread.Sleep(120000);')
    dotnet build (Join-Path $fixture 'Fixture.csproj') -c Release -o (Join-Path $fixture 'bin')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot build harmless process fixture.' }
    $fixtureProcess = Start-Process (Join-Path $fixture 'bin\Sora.exe') -WindowStyle Hidden -PassThru
    Check 'Upgrade refuses a running Sora' ((Setup $candidate 'running-upgrade-refusal') -ne 0)
    Check 'Uninstall refuses a running Sora' ((Uninstall 'running-uninstall-refusal' -Purge) -ne 0)
    Payload 'Running-process refusal leaves installed files intact'
    Check 'Running-process refusal preserves data' (DataPreserved)
    $fixtureProcess.Kill(); $fixtureProcess.WaitForExit(); $fixtureProcess.Dispose(); $fixtureProcess = $null
    Check 'Keep-data uninstall succeeds' ((Uninstall 'keep-data-uninstall') -eq 0)
    Check 'Keep-data uninstall removes app registration and executable' (WaitUninstalled)
    Check 'Keep-data uninstall preserves saved data' (DataPreserved)
    Check 'Owned startup task is removed' (-not (Get-ScheduledTask -TaskName Sora -ErrorAction SilentlyContinue))
    Check 'Owned shortcuts are removed and foreign shortcut retained' (-not (Test-Path $ownedLink) -and -not (Test-Path $ownedMenuLink) -and (Get-FileHash $foreignLink).Hash -eq $foreignHash)
    Check 'Reinstall after keeping data succeeds' ((Setup $candidate 'reinstall-kept-data') -eq 0)
    Payload 'Reinstalled payload matches manifest'
    Check 'Reinstall retains saved data' (DataPreserved)
    $outside = Join-Path $root 'OutsideSora'; [IO.Directory]::CreateDirectory($outside) | Out-Null
    [IO.File]::WriteAllText((Join-Path $outside 'keep.txt'), 'unrelated data')
    $junction = Join-Path $dataRoots[0] 'unsafe-junction'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    Check 'Purge refuses a junction in Sora data' ((Uninstall 'junction-refusal' -Purge) -ne 0)
    Payload 'Refused purge retains installed payload'
    Check 'Refused purge preserves every saved-data fixture' (DataPreserved)
    Check 'Junction target is untouched' ([IO.File]::ReadAllText((Join-Path $outside 'keep.txt')) -eq 'unrelated data')
    # Delete only the verified junction itself, never recursively or through a different shell.
    if (-not ((Get-Item -LiteralPath $junction).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Expected a junction fixture.' }
    [IO.Directory]::Delete($junction)
    Register-ScheduledTask -TaskName Sora -Action (New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\notepad.exe')) | Out-Null
    Check 'Purge refuses a task named Sora owned by another application' ((Uninstall 'foreign-task-refusal' -Purge) -ne 0)
    Check 'Foreign task remains intact' ((Get-ScheduledTask Sora).Actions.Execute -like '*notepad.exe')
    Check 'Foreign-task refusal preserves saved data' (DataPreserved)
    Unregister-ScheduledTask Sora -Confirm:$false
    Shortcut $ownedLink $exe; Shortcut $ownedMenuLink $exe
    Register-ScheduledTask -TaskName Sora -Action (New-ScheduledTaskAction -Execute $exe) | Out-Null
    Check 'Complete-data uninstall succeeds' ((Uninstall 'complete-uninstall' -Purge) -eq 0)
    Check 'Complete uninstall removes app and registration' (WaitUninstalled)
    Check 'Complete uninstall removes every Sora data directory' (@($dataRoots | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0)
    Check 'Complete uninstall removes installation directory' (-not (Test-Path -LiteralPath $install))
    Check 'Complete uninstall removes owned startup task' (-not (Get-ScheduledTask Sora -ErrorAction SilentlyContinue))
    Check 'Unrelated files and foreign shortcut remain unchanged' ([IO.File]::ReadAllText((Join-Path $outside 'keep.txt')) -eq 'unrelated data' -and (Get-FileHash $foreignLink).Hash -eq $foreignHash)
    Check 'Clean install after complete removal succeeds' ((Setup $candidate 'clean-reinstall') -eq 0)
    Payload 'Clean reinstall matches the full manifest'
    Check 'Final clean uninstall succeeds' ((Uninstall 'final-uninstall' -Purge) -eq 0 -and (WaitUninstalled))
    $result.passed = $true
} catch { $result.error = $_.ToString(); Write-Warning $result.error }
finally {
    if ($null -ne $fixtureProcess) { if (-not $fixtureProcess.HasExited) { $fixtureProcess.Kill() }; $fixtureProcess.Dispose() }
    # The VM is discarded by GitHub; retain failure state and logs instead of destructive cleanup after a failed check.
    $result | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $root 'qualification.json')
}
if (-not $result.passed) { throw "Installed lifecycle qualification failed. Inspect $root" }
