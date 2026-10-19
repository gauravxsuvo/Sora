param(
    [switch]$Elevated,
    [string]$RequestingUserSid
)
$ErrorActionPreference = 'Stop'
# Repair only shortcuts already targeting the installed Sora. Never clear the global icon cache or restart Explorer.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($Elevated -and $RequestingUserSid -ne $identity.User.Value) {
    throw 'Approve the administrator prompt using the same Windows account so Sora repairs the correct user shortcuts.'
}
if (-not $isAdministrator) {
    if ($Elevated) { throw 'Windows did not grant administrator access. Run this script from PowerShell opened with Run as administrator.' }
    Write-Output 'Sora needs administrator access to update its icon in Program Files. Approve the Windows prompt to continue.'
    $powershell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
    $arguments = '-NoProfile -File "{0}" -Elevated -RequestingUserSid "{1}"' -f $PSCommandPath, $identity.User.Value
    try {
        $repairProcess = Start-Process -FilePath $powershell -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ErrorAction Stop
    } catch {
        throw 'Administrator access was not granted. Run the script again and choose Yes in the Windows prompt, or open PowerShell with Run as administrator.'
    }
    if ($repairProcess.ExitCode -ne 0) {
        throw 'The administrator repair did not complete. Open PowerShell with Run as administrator and run this script there to see the error details.'
    }
    Write-Output 'Sora shortcut icons refreshed. If Windows still shows a cached icon, unpin and re-pin Sora or sign out and back in.'
    return
}
$install = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Sora'
$exe = Join-Path $install 'Sora.exe'
if (-not (Test-Path -LiteralPath (Join-Path $install 'sora-package.json'))) { throw 'A registered Sora installation manifest is required.' }
for ($item = Get-Item -LiteralPath $install -Force; $null -ne $item; $item = $item.Parent) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The installed path contains a link.' }
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($version.ProductName -ne 'Sora' -or $version.FileMajorPart -ne 0 -or $version.FileMinorPart -lt 12) { throw 'Install the purple-logo Sora before repairing its shortcuts.' }
$iconDirectory = Join-Path $install 'Assets'
if ((Test-Path -LiteralPath $iconDirectory) -and ((Get-Item -LiteralPath $iconDirectory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Icon directory is a link.' }
[IO.Directory]::CreateDirectory($iconDirectory) | Out-Null
$icon = Join-Path $iconDirectory ('sora-brand-{0}.{1}.{2}.ico' -f $version.FileMajorPart,$version.FileMinorPart,$version.FileBuildPart)
if ((Test-Path -LiteralPath $icon) -and ((Get-Item -LiteralPath $icon -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Icon file is a link.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\assets\Sora.ico') -Destination $icon
$roots = @([Environment]::GetFolderPath('Programs'),[Environment]::GetFolderPath('CommonPrograms'),[Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('CommonDesktopDirectory')) | Select-Object -Unique
$shell = New-Object -ComObject WScript.Shell
foreach ($root in $roots) {
    foreach ($relative in @('Sora.lnk','Sora\Sora.lnk')) {
        $path = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $path)) { continue }
        if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Shortcut is a link.' }
        $shortcut = $shell.CreateShortcut($path)
        if ($shortcut.TargetPath -ine $exe) { continue }
        $shortcut.IconLocation = $icon + ',0'; $shortcut.Save()
        Write-Output "Updated icon: $path"
    }
}
if (-not ('SoraIconRefresh' -as [type])) {
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class SoraIconRefresh { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2); }'
}
[SoraIconRefresh]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
Write-Output 'Sora shortcut icons refreshed. If a Recent item is still cached, unpin/re-pin that item or sign out and back in.'
