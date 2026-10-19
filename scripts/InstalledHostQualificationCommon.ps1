function Save-HostEvidence([string]$Path,$Value) {
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 32))
    $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try {$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)} finally {$stream.Dispose()}
}
function Invoke-HostTool([string]$Directory,[string]$Name,[string[]]$Arguments,[int]$TimeoutMilliseconds=60000) {
    $control=Get-Content (Join-Path $Directory 'host-baseline.json') -Raw | ConvertFrom-Json
    if((Get-FileHash -LiteralPath $control.tool).Hash -cne $control.toolSha256) {throw 'Qualification helper changed.'}
    foreach($file in $control.toolFiles) {if((Get-FileHash -LiteralPath (Join-Path (Split-Path $control.tool) $file.relative)).Hash -cne $file.sha256) {throw 'Qualification helper payload changed.'}}
    $path=Join-Path $Directory ($Name+'.json')
    $command=$Arguments[0];$rest=@();if($Arguments.Count -gt 1) {$rest=$Arguments[1..($Arguments.Count-1)]}
    $p=Start-Process -FilePath $control.tool -WindowStyle Hidden -PassThru -ArgumentList (@($command,('"'+$path+'"'))+$rest)
    try {
        if(-not $p.WaitForExit($TimeoutMilliseconds)) {throw ('Qualification helper still running: PID '+$p.Id+'. Do not launch another recovery operation.')}
        if(-not (Test-Path -LiteralPath $path)) {throw 'Qualification helper produced no evidence.'}
        $report=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if($p.ExitCode -ne 0 -or $report.passed -eq $false) {throw ('Qualification helper failed: '+$Name+' '+$report.error+' '+($report.errors -join ';'))}
        return $report
    } finally {$p.Dispose()}
}
function Get-QualifiedHostProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='Sora.exe'" | Select-Object ProcessId,ExecutablePath,CreationDate)
}
function Complete-InstalledHostRecovery([string]$Directory,[string]$Name) {
    $backup=Get-Content (Join-Path $Directory 'host-baseline.json') -Raw | ConvertFrom-Json
    $result=[ordered]@{startedAt=[DateTimeOffset]::Now.ToString('o');passed=$false;error=$null;hardwareRecovered=$false;dataRestored=$false;taskRestored=$false}
    $mutex=[Threading.Mutex]::new($false,$backup.hostRecoveryMutex);$held=$false
    try {
        try {$held=$mutex.WaitOne(10000)} catch [Threading.AbandonedMutexException] {$held=$true}
        if(-not $held) {throw 'Host recovery is already running.'}
        $apps=@(Get-QualifiedHostProcesses)
        foreach($app in $apps) {
            if($app.ExecutablePath -cne 'C:\Program Files\Sora\Sora.exe') {throw 'An unexpected Sora deployment is running; no recovery writes requested.'}
            . (Join-Path $PSScriptRoot 'QualifiedHostExitPolicy.ps1')
            $process=Get-Process -Id $app.ProcessId -ErrorAction Stop
            $owned=@(Get-ChildItem -LiteralPath $Directory -Filter 'owned-host-*.json' | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json} | Where-Object {
                $_.pid -eq $app.ProcessId -and $_.startUtcTicks -eq $process.StartTime.ToUniversalTime().Ticks -and $_.path -ceq $app.ExecutablePath
            })
            $exitResult=Invoke-QualifiedHostExitPolicy -TryGraceful {
                $null=Invoke-HostTool $Directory ($Name+'-exit-'+$app.ProcessId) @('ui',[string]$app.ProcessId,'exit')
                if(-not $process.WaitForExit(15000)) {throw 'Graceful test-host Exit deadline.'}
            } -IsRunning { -not $process.HasExited } -StopOwned {
                if($owned.Count -ne 1) {throw 'No unique owned-PID/start-time/path record; refusing to terminate any process.'}
                $fresh=Get-Process -Id $app.ProcessId -ErrorAction Stop
                if($fresh.StartTime.ToUniversalTime().Ticks -ne $owned[0].startUtcTicks -or $fresh.ProcessName -cne 'Sora') {throw 'Owned process identity changed.'}
                Stop-Process -Id $app.ProcessId -Force -ErrorAction Stop
            } -WaitStopped {$process.WaitForExit(5000)}
            Save-HostEvidence (Join-Path $Directory ($Name+'-exit-policy-'+$app.ProcessId+'.json')) $exitResult
        }
        if(@(Get-QualifiedHostProcesses).Count) {throw 'A Sora hardware owner remains.'}
        $null=Invoke-HostTool $Directory ($Name+'-hardware') @('restore',('"'+$backup.hardwareBaseline+'"'),$backup.hardwareSha256)
        $result.hardwareRecovered=$true
        # Recover data only after actual hardware ownership is relinquished/recovered.
        $expectedRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Sora\data'
        if($backup.dataRoot -cne $expectedRoot) {throw 'Unexpected shared-data root.'}
        foreach($file in $backup.data) {
            $path=[IO.Path]::GetFullPath((Join-Path $backup.dataRoot $file.relative))
            if(-not $path.StartsWith($backup.dataRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Data recovery path escapes the original root.'}
            if(Test-Path -LiteralPath $path) {if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'Data recovery target is a link.'}}
            if($null -eq $file.sha256) {if(Test-Path -LiteralPath $path) {Remove-Item -LiteralPath $path}}
            else {
                $source=Join-Path $Directory ('data-backup\'+$file.relative)
                if((Get-FileHash -LiteralPath $source).Hash -cne $file.sha256) {throw 'Recovery backup hash mismatch.'}
                Copy-Item -LiteralPath $source -Destination $path -Force
            }
        }
        foreach($file in $backup.data) {
            $path=Join-Path $backup.dataRoot $file.relative;$hash=$null;if(Test-Path -LiteralPath $path) {$hash=(Get-FileHash -LiteralPath $path).Hash}
            if($hash -cne $file.sha256) {throw 'Restored shared-data state differs.'}
        }
        $result.dataRestored=$true
        $result.task=Restore-StartupQualificationTask $Directory;$result.taskRestored=$true
        $result.passed=$true
    } catch {$result.error=$_.Exception.ToString()}
    finally {if($held) {$mutex.ReleaseMutex()};$mutex.Dispose();$result.finishedAt=[DateTimeOffset]::Now.ToString('o');Save-HostEvidence (Join-Path $Directory ($Name+'.json')) $result}
    return $result
}
