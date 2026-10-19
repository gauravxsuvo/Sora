param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Choose a new inventory path to preserve earlier evidence.' }
$report = [ordered]@{capturedAt=[DateTimeOffset]::Now.ToString('o'); configurationWrites=0; complete=$false; errors=@()}
function Read-Inventory([string]$Name, [scriptblock]$Read) {
    try { $report[$Name] = @(& $Read) }
    catch { $report.errors += [ordered]@{section=$Name;error=$_.Exception.Message} }
}
Read-Inventory 'board' { Get-CimInstance Win32_BaseBoard | Select-Object Manufacturer,Product,Version }
Read-Inventory 'bios' { Get-CimInstance Win32_BIOS | Select-Object Manufacturer,SMBIOSBIOSVersion,ReleaseDate }
Read-Inventory 'windows' { Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture }
Read-Inventory 'disks' { Get-CimInstance Win32_DiskDrive | Select-Object Index,Model,FirmwareRevision,Size,PNPDeviceID }
Read-Inventory 'display' { Get-CimInstance Win32_VideoController | Select-Object Name,PNPDeviceID,DriverVersion,DriverDate,VideoProcessor }
Read-Inventory 'drivers' { Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceID -match '^(PCI|USB|HDAUDIO|ACPI)\\' } | Select-Object DeviceName,DeviceClass,DeviceID,DriverVersion,DriverDate,DriverProviderName,InfName,IsSigned }
Read-Inventory 'presentDevices' {
    Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match '^(PCI|USB|HID|ACPI|HDAUDIO)\\' } | ForEach-Object {
        $device = $_
        $properties = @(Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName DEVPKEY_Device_HardwareIds,DEVPKEY_Device_BusReportedDeviceDesc -ErrorAction SilentlyContinue)
        [ordered]@{name=$device.FriendlyName;class=$device.Class;status=$device.Status;instanceId=$device.InstanceId;hardwareIds=@(($properties | Where-Object KeyName -eq 'DEVPKEY_Device_HardwareIds').Data);busDescription=@(($properties | Where-Object KeyName -eq 'DEVPKEY_Device_BusReportedDeviceDesc').Data)}
    }
}
Read-Inventory 'nvidiaSmi' {
    $tool = Join-Path $env:WINDIR 'System32\nvidia-smi.exe'
    if (-not (Test-Path -LiteralPath $tool)) { throw 'NVIDIA driver inventory tool unavailable.' }
    $lines = & $tool '--query-gpu=name,pci.device_id,pci.sub_device_id,pci.bus_id,vbios_version,driver_version' '--format=csv,noheader'
    if ($LASTEXITCODE -ne 0) { throw 'NVIDIA read-only query failed.' }
    $lines | ConvertFrom-Csv -Header Name,DeviceId,SubsystemId,Bus,Vbios,DriverVersion
}
$report.complete = $report.errors.Count -eq 0
$report.notes = @('SMBIOS x.x and PCI silicon revision are not PCB revisions. Verify board/card labels before firmware selection.', 'USB REV/bcdDevice is a descriptor identity, not a qualified firmware version. No HID commands or vendor app launch performed.', 'Installed driver entries may include disconnected devices; use presentDevices to establish presence.', 'No updater, service stop/start, install, uninstall, firmware write or active Wi-Fi scan performed.')
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output)) | Out-Null
$json = $report | ConvertTo-Json -Depth 10
$stream = [IO.File]::Open($Output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try { $bytes=[Text.Encoding]::UTF8.GetBytes($json); $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
if (-not $report.complete) { throw 'Inventory incomplete; inspect recorded section errors.' }
