param()
$ErrorActionPreference = 'Stop'
# Repair only shortcuts already targeting the installed Sora. Never clear the global icon cache or restart Explorer.
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
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class SoraIconRefresh { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2); }'
[SoraIconRefresh]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
Write-Output 'Sora shortcut icons refreshed. If a Recent item is still cached, unpin/re-pin that item or sign out and back in.'
