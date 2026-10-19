param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Choose a new path; earlier recovery evidence must be preserved.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$report = [ordered]@{
    capturedAt=[DateTimeOffset]::Now.ToString('o')
    elevated=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    configurationWrites=0; inventoryComplete=$false; readyForFirmware=$false; errors=@()
}
function Read-Section([string]$Name, [scriptblock]$Read) {
    try { $report[$Name] = @(& $Read) }
    catch { $report.errors += [ordered]@{section=$Name;error=$_.Exception.Message} }
}
function Read-Native([string]$File, [string]$Arguments) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName=Join-Path $env:WINDIR ('System32\'+$File)
    $start.Arguments=$Arguments
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $process = New-Object Diagnostics.Process
    $process.StartInfo=$start
    try {
        if (-not $process.Start()) { throw 'Read-only command did not start.' }
        $stdout=$process.StandardOutput.ReadToEndAsync()
        $stderr=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(20000)) {
            $process.Kill()
            throw 'Read-only recovery query exceeded 20 seconds.'
        }
        [ordered]@{command=($File+' '+$Arguments);exitCode=$process.ExitCode;stdout=$stdout.GetAwaiter().GetResult();stderr=$stderr.GetAwaiter().GetResult()}
    } finally { $process.Dispose() }
}
Read-Section 'disks' {
    Get-Disk | Select-Object Number,FriendlyName,PartitionStyle,BusType,Size,IsBoot,IsSystem,OperationalStatus,HealthStatus
}
Read-Section 'partitions' {
    Get-Partition | ForEach-Object {
        [ordered]@{diskNumber=$_.DiskNumber;partitionNumber=$_.PartitionNumber;driveLetter=if([int][char]$_.DriveLetter -gt 0){[string]$_.DriveLetter}else{$null};type=$_.Type;size=$_.Size;isBoot=$_.IsBoot;isSystem=$_.IsSystem}
    }
}
Read-Section 'volumes' {
    Get-Volume | Where-Object DriveLetter | Select-Object DriveLetter,FileSystem,Size,SizeRemaining,HealthStatus
}
Read-Section 'storageDevices' {
    Get-CimInstance Win32_DiskDrive | Select-Object Index,Model,FirmwareRevision,InterfaceType,PNPDeviceID,Size
}
Read-Section 'storageDrivers' {
    Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceClass -in @('SCSIAdapter','HDC','DiskDrive','Volume') } |
        Select-Object DeviceName,DeviceClass,DeviceID,DriverVersion,DriverProviderName,InfName,IsSigned
}
Read-Section 'encryptionStatus' {
    # Only status methods: never enumerate/read key protectors, recovery passwords, or keys.
    Get-CimInstance -Namespace 'root/CIMV2/Security/MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume | ForEach-Object {
        $protection=Invoke-CimMethod -InputObject $_ -MethodName GetProtectionStatus
        $conversion=Invoke-CimMethod -InputObject $_ -MethodName GetConversionStatus
        if ($protection.ReturnValue -ne 0 -or $conversion.ReturnValue -ne 0) { throw 'Encryption status query returned an error.' }
        [ordered]@{driveLetter=$_.DriveLetter;protectionStatus=$protection.ProtectionStatus;conversionStatus=$conversion.ConversionStatus;encryptionPercentage=$conversion.EncryptionPercentage}
    }
}
Read-Section 'windowsRecovery' { Read-Native 'reagentc.exe' '/info' }
Read-Section 'windowsBackupCatalog' { Read-Native 'wbadmin.exe' 'get versions' }
$report.nativeQueryFailures=@(foreach($section in @('windowsRecovery','windowsBackupCatalog')) {
    foreach($result in $report[$section]) {
        if($result.exitCode -ne 0) { [ordered]@{section=$section;exitCode=$result.exitCode} }
    }
})
foreach($section in @('disks','partitions','volumes','storageDevices','encryptionStatus')) {
    if(@($report[$section]).Count -eq 0) { $report.errors += [ordered]@{section=$section;error='No records returned; do not assume absence or readiness.'} }
}
$report.inventoryComplete=$report.errors.Count -eq 0 -and
    $report.nativeQueryFailures.Count -eq 0
$report.notes=@(
    'Read-only status collection; no backup, repair, encryption change, service change, recovery boot, reboot, install or firmware operation.',
    'A local backup catalog entry does not prove availability, integrity, completeness or a successful restore. Other backup products are not inventoried.',
    'Windows RE being enabled does not qualify external boot media or recovery after complete system-disk failure.',
    'Free space and physical disk separation are inventory only, not backup verification or permission to overwrite a disk.',
    'Encryption keys/protector identifiers/passwords were not queried. Any required recovery material must be verified privately.',
    'readyForFirmware stays false: verified backup, recovery media, package, exact offer and reviewed execution remain separate gates.'
)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output)) | Out-Null
$stream=[IO.File]::Open($Output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try {
    $bytes=[Text.Encoding]::UTF8.GetBytes(($report | ConvertTo-Json -Depth 9))
    $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true)
} finally { $stream.Dispose(); $identity.Dispose() }
if (-not $report.inventoryComplete) { throw 'Recovery inventory incomplete; inspect recorded section errors and native exit codes.' }
