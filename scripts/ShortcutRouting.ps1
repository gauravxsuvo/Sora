Set-StrictMode -Version Latest

function Test-SoraDevelopmentTarget([string]$Path, [string]$DevelopmentRoot) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathRooted($Path)) { return $false }
    try {
        $full = [IO.Path]::GetFullPath($Path)
        $root = [IO.Path]::GetFullPath($DevelopmentRoot).TrimEnd('\')
        if ([IO.Path]::GetFileName($full) -ine 'Sora.exe' -or -not (Test-Path -LiteralPath $full -PathType Leaf)) { return $false }
        if ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
        # A workspace alias must not make an installed/manual target look development-owned.
        for ($ancestor = Get-Item -LiteralPath $root -Force; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
            if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
        }
        $directory = [IO.Path]::GetDirectoryName($full)
        $seen = @{}
        for ($hop = 0; $hop -lt 8; $hop++) {
            if ([IO.Path]::GetDirectoryName($directory) -ine $root -or [IO.Path]::GetFileName($directory) -notmatch '^Sora-(current|\d+\.\d+\.\d+)$') { return $false }
            if ($seen.ContainsKey($directory)) { return $false }
            $seen[$directory] = $true
            $item = Get-Item -LiteralPath $directory -Force
            if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $true }
            $targets = @($item.Target)
            if ($targets.Count -ne 1 -or [string]::IsNullOrWhiteSpace($targets[0])) { return $false }
            $destination = $targets[0]
            if (-not [IO.Path]::IsPathRooted($destination)) { $destination = Join-Path $item.Parent.FullName $destination }
            $directory = [IO.Path]::GetFullPath($destination).TrimEnd('\')
        }
    } catch { return $false }
    return $false
}

function Update-SoraShortcuts {
    param(
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string[]]$Links,
        [Parameter(Mandatory)][string]$DevelopmentRoot,
        [switch]$UpdateOnly,
        [switch]$ReplaceExisting
    )
    if ($UpdateOnly -and $ReplaceExisting) { throw 'ReplaceExisting requires an explicit shortcut installation, not UpdateOnly.' }
    $targetPath = (Resolve-Path -LiteralPath $Target).Path
    if ([IO.Path]::GetFileName($targetPath) -ine 'Sora.exe') { throw 'Shortcut target must be Sora.exe.' }
    if ($UpdateOnly -and -not (Test-SoraDevelopmentTarget $targetPath $DevelopmentRoot)) { throw 'UpdateOnly target must belong to this workspace dist release.' }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($link in $Links) {
        if ([IO.Path]::GetExtension($link) -ine '.lnk') { throw 'Shortcut paths must end in .lnk.' }
        $exists = Test-Path -LiteralPath $link
        if ($UpdateOnly -and -not $exists) { [pscustomobject]@{ link = $link; action = 'missing-skipped' }; continue }
        if ($exists -and ((Get-Item -LiteralPath $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked shortcut: $link" }
        $shortcut = $shell.CreateShortcut($link)
        # Explicit creation is also conservative with an existing foreign shortcut. Replacing it
        # requires -ReplaceExisting; development builds never supply that switch.
        if ($exists -and -not $ReplaceExisting -and -not (Test-SoraDevelopmentTarget $shortcut.TargetPath $DevelopmentRoot)) {
            [pscustomobject]@{ link = $link; action = 'foreign-preserved'; target = $shortcut.TargetPath }; continue
        }
        if ($shortcut.TargetPath -ieq $targetPath -and $shortcut.WorkingDirectory -ieq (Split-Path $targetPath -Parent)) {
            [pscustomobject]@{ link = $link; action = 'unchanged'; target = $targetPath }; continue
        }
        $shortcut.TargetPath = $targetPath
        $shortcut.WorkingDirectory = Split-Path $targetPath -Parent
        $shortcut.IconLocation = "$targetPath,0"
        $shortcut.Description = 'Sora - hardware controls, GPU fans, SSD health and Wi-Fi Compass'
        $shortcut.Save()
        [pscustomobject]@{ link = $link; action = 'saved'; target = $targetPath }
    }
}
