param([Parameter(Mandatory)][string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ShortcutRouting.ps1')
$root = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $root) { throw 'Preserve earlier shortcut qualification; choose a new directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
$result = [ordered]@{ startedAt = [DateTimeOffset]::Now.ToString('o'); passed = $false; checks = @(); error = $null; realShortcutsModified = $false; fixtureRoot = $root }
function Check([string]$Name, [bool]$Passed) {
    $result.checks += [ordered]@{ name = $Name; passed = $Passed }
    if (-not $Passed) { throw "Shortcut routing check failed: $Name" }
}
function New-Executable([string]$Directory) {
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    $path = Join-Path $Directory 'Sora.exe'
    [IO.File]::WriteAllText($path, 'Shortcut routing fixture; not an executable.')
    return $path
}
$shell = New-Object -ComObject WScript.Shell
function New-Link([string]$Name, [string]$Target) {
    $path = Join-Path $root ($Name + '.lnk')
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = Split-Path $Target -Parent
    $shortcut.Description = 'Preserve this fixture description unless explicitly replaced.'
    $shortcut.Arguments = '--settings'
    $shortcut.Save()
    return $path
}
function Snapshot([string]$Path) { return (Get-FileHash -LiteralPath $Path).Hash + '|' + (Get-Item -LiteralPath $Path).LastWriteTimeUtc.Ticks }
try {
    $dist = Join-Path $root 'workspace\dist'
    $old = New-Executable (Join-Path $dist 'Sora-0.10.5')
    $new = New-Executable (Join-Path $dist 'Sora-0.10.7')
    $installed = New-Executable (Join-Path $root 'Program Files\Sora')
    $manual = New-Executable (Join-Path $root 'manual\Sora-0.10.5')
    $otherWorkspace = New-Executable (Join-Path $root 'other-workspace\dist\Sora-0.10.5')
    $prefixSpoof = New-Executable (Join-Path $root 'workspace\dist-other\Sora-0.10.5')
    $nested = New-Executable (Join-Path $dist 'nested\Sora-0.10.5')
    $invalidName = New-Executable (Join-Path $dist 'manual-copy')
    $current = Join-Path $dist 'Sora-current'
    New-Item -ItemType Junction -Path $current -Target (Split-Path $new -Parent) | Out-Null
    $stable = Join-Path $current 'Sora.exe'
    Check 'Versioned workspace release is owned' (Test-SoraDevelopmentTarget $old $dist)
    Check 'Stable junction resolving inside workspace dist is owned' (Test-SoraDevelopmentTarget $stable $dist)
    Check 'Ownership is case insensitive' (Test-SoraDevelopmentTarget $old.ToUpperInvariant() $dist)
    Check 'Installed target is foreign' (-not (Test-SoraDevelopmentTarget $installed $dist))
    Check 'Another workspace is foreign' (-not (Test-SoraDevelopmentTarget $otherWorkspace $dist))
    Check 'Prefix-spoofed dist path is foreign' (-not (Test-SoraDevelopmentTarget $prefixSpoof $dist))
    Check 'Nested or unrecognized dist directories are foreign' (-not (Test-SoraDevelopmentTarget $nested $dist) -and -not (Test-SoraDevelopmentTarget $invalidName $dist))
    $ownedLink = New-Link 'owned-release' $old
    $actions = @(Update-SoraShortcuts -Target $stable -Links @($ownedLink) -DevelopmentRoot $dist -UpdateOnly)
    Check 'Development refresh updates an owned legacy release' ($actions[0].action -eq 'saved' -and $shell.CreateShortcut($ownedLink).TargetPath -ieq $stable)
    Check 'Refresh preserves shortcut arguments' ($shell.CreateShortcut($ownedLink).Arguments -eq '--settings')
    $before = Snapshot $ownedLink
    $actions = @(Update-SoraShortcuts -Target $stable -Links @($ownedLink) -DevelopmentRoot $dist -UpdateOnly)
    Check 'Repeated stable refresh preserves shortcut bytes and timestamp' ($actions[0].action -eq 'unchanged' -and (Snapshot $ownedLink) -eq $before)
    foreach ($case in @(@('installed',$installed), @('manual',$manual), @('other-workspace',$otherWorkspace), @('prefix-spoof',$prefixSpoof), @('missing-target',(Join-Path $dist 'Sora-0.0.0\Sora.exe')))) {
        $link = New-Link $case[0] $case[1]
        $before = Snapshot $link
        $actions = @(Update-SoraShortcuts -Target $stable -Links @($link) -DevelopmentRoot $dist -UpdateOnly)
        Check ("Refresh preserves $($case[0]) shortcut bytes and timestamp") ($actions[0].action -eq 'foreign-preserved' -and (Snapshot $link) -eq $before)
    }
    $missing = Join-Path $root 'missing.lnk'
    $actions = @(Update-SoraShortcuts -Target $stable -Links @($missing) -DevelopmentRoot $dist -UpdateOnly)
    Check 'UpdateOnly does not create missing shortcuts' ($actions[0].action -eq 'missing-skipped' -and -not (Test-Path -LiteralPath $missing))
    $link = Join-Path $root 'installed.lnk'
    $before = Snapshot $link
    $actions = @(Update-SoraShortcuts -Target $stable -Links @($link) -DevelopmentRoot $dist)
    Check 'Explicit creation without replacement preserves a foreign shortcut' ($actions[0].action -eq 'foreign-preserved' -and (Snapshot $link) -eq $before)
    $actions = @(Update-SoraShortcuts -Target $stable -Links @($link) -DevelopmentRoot $dist -ReplaceExisting)
    Check 'Explicit replacement can select development for rollback' ($actions[0].action -eq 'saved' -and $shell.CreateShortcut($link).TargetPath -ieq $stable)
    $actions = @(Update-SoraShortcuts -Target $installed -Links @($link) -DevelopmentRoot $dist -ReplaceExisting)
    Check 'Explicit replacement can select installed copy' ($actions[0].action -eq 'saved' -and $shell.CreateShortcut($link).TargetPath -ieq $installed)
    $before = Snapshot $link
    $actions = @(Update-SoraShortcuts -Target $stable -Links @($link) -DevelopmentRoot $dist -UpdateOnly)
    Check 'A later development refresh preserves installed cutover' ($actions[0].action -eq 'foreign-preserved' -and (Snapshot $link) -eq $before)
    $escaped = Join-Path $dist 'Sora-0.0.1'
    New-Item -ItemType Junction -Path $escaped -Target (Split-Path $installed -Parent) | Out-Null
    Check 'Junction escaping dist is foreign' (-not (Test-SoraDevelopmentTarget (Join-Path $escaped 'Sora.exe') $dist))
    $escapeLink = New-Link 'escape' (Join-Path $escaped 'Sora.exe')
    $before = Snapshot $escapeLink
    $actions = @(Update-SoraShortcuts -Target $stable -Links @($escapeLink) -DevelopmentRoot $dist -UpdateOnly)
    Check 'Refresh preserves installed shortcut disguised by a dist junction' ($actions[0].action -eq 'foreign-preserved' -and (Snapshot $escapeLink) -eq $before)
    $rejected = $false
    try { Update-SoraShortcuts -Target $installed -Links @($ownedLink) -DevelopmentRoot $dist -UpdateOnly | Out-Null } catch { $rejected = $true }
    Check 'UpdateOnly refuses a non-development proposed target' $rejected
    $rejected = $false
    try { Update-SoraShortcuts -Target $stable -Links @($ownedLink) -DevelopmentRoot $dist -UpdateOnly -ReplaceExisting | Out-Null } catch { $rejected = $true }
    Check 'Development refresh cannot request forced replacement' $rejected
    $created = Join-Path $root 'created.lnk'
    $actions = @(Update-SoraShortcuts -Target $stable -Links @($created) -DevelopmentRoot $dist)
    Check 'Explicit shortcut installation creates a missing link' ($actions[0].action -eq 'saved' -and $shell.CreateShortcut($created).TargetPath -ieq $stable)
    $result.passed = $true
} catch { $result.error = $_.Exception.ToString() }
$result.completedAt = [DateTimeOffset]::Now.ToString('o')
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(($result | ConvertTo-Json -Depth 10))
$stream = [IO.FileStream]::new((Join-Path $root 'result.json'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
$result | ConvertTo-Json -Depth 10
if (-not $result.passed) { throw $result.error }
