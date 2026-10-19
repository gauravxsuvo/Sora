param([switch]$StartInTray)
$ErrorActionPreference = 'Stop'
$TaskRoot = Split-Path $PSScriptRoot -Parent
$Executable = (Resolve-Path -LiteralPath (Join-Path $TaskRoot 'dist\Sora-0.8.1\Sora.exe')).Path
$BackupDirectory = Join-Path $TaskRoot 'artifacts\sora-0.8.1-install-before'
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
$PriorTask = & schtasks.exe /Query /TN Sora /XML ONE 2>&1
if ($LASTEXITCODE -eq 0) { $PriorTask | Set-Content -LiteralPath (Join-Path $BackupDirectory 'startup-task.xml') -Encoding Unicode }
foreach ($Location in @(@('Desktop', [Environment]::GetFolderPath('Desktop')), @('StartMenu', [Environment]::GetFolderPath('Programs')))) {
    $Link = Join-Path $Location[1] 'Sora.lnk'
    if (Test-Path -LiteralPath $Link) { Copy-Item -LiteralPath $Link -Destination (Join-Path $BackupDirectory ($Location[0] + '-Sora.lnk')) }
}
& (Join-Path $PSScriptRoot 'Install-Shortcuts.ps1') -Target $Executable -UpdateOnly
$Registration = Start-Process -FilePath $Executable -ArgumentList @('--startup-task', 'on') -WindowStyle Hidden -PassThru -Wait
if ($Registration.ExitCode -ne 0) { throw "Startup registration failed: $($Registration.ExitCode)" }
$Registered = & schtasks.exe /Query /TN Sora /XML ONE
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the registered startup task.' }
[xml]$TaskXml = $Registered -join [Environment]::NewLine
$Namespaces = [Xml.XmlNamespaceManager]::new($TaskXml.NameTable)
$Namespaces.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
function TaskValue([string]$Path) { $TaskXml.SelectSingleNode($Path, $Namespaces).InnerText }
if ((TaskValue '//t:Exec/t:Command').Trim('"') -ne $Executable) { throw 'Task points at the wrong release.' }
if ((TaskValue '//t:Exec/t:Arguments') -ne '--startup') { throw 'Task is not set to launch in the tray.' }
if ((TaskValue '//t:Principal/t:RunLevel') -ne 'HighestAvailable') { throw 'Task is missing the required hardware permissions.' }
if ((TaskValue '//t:LogonTrigger/t:Delay') -ne 'PT15S') { throw 'Task sign-in delay is incorrect.' }
if ((TaskValue '//t:EventTrigger/t:Subscription') -notmatch 'Power-Troubleshooter') { throw 'Task has no wake trigger.' }
if ((TaskValue '//t:Settings/t:MultipleInstancesPolicy') -ne 'IgnoreNew') { throw 'Task permits duplicate instances.' }
$Registered | Set-Content -LiteralPath (Join-Path $TaskRoot 'artifacts\sora-0.8.1-startup-task.xml') -Encoding Unicode
$ShortcutTargets = foreach ($Directory in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $LinkPath = Join-Path $Directory 'Sora.lnk'
    if (Test-Path -LiteralPath $LinkPath) {
        $Shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($LinkPath)
        if ($Shortcut.TargetPath -ne $Executable) { throw 'A shortcut points at the wrong release.' }
        [ordered]@{ path = $LinkPath; target = $Shortcut.TargetPath }
    }
}
if ($StartInTray) {
    & schtasks.exe /Run /TN Sora
    if ($LASTEXITCODE -ne 0) { throw 'Could not start Sora in the tray.' }
}
$RunningCopies = @(Get-Process Sora -ErrorAction SilentlyContinue | ForEach-Object { [ordered]@{ id = $_.Id; path = $_.Path } })
[ordered]@{ verifiedAt = [DateTimeOffset]::Now.ToString('O'); executable = $Executable; version = (Get-Item $Executable).VersionInfo.FileVersion; taskVerified = $true; shortcuts = @($ShortcutTargets); startRequested = [bool]$StartInTray; runningCopies = $RunningCopies; startedInTray = [bool]($RunningCopies | Where-Object path -eq $Executable) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $TaskRoot 'artifacts\sora-0.8.1-deployment.json')
