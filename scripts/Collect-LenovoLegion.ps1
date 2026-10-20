param([Parameter(Mandatory = $true)][string]$Output)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Choose a new report path to preserve earlier evidence.' }

# Standalone Windows PowerShell 5.1 collector: no Sora build or third-party modules required.
# Metadata only. Never calls Invoke-CimMethod, vendor software, driver IOCTLs or HID reports.
$report = [ordered]@{
    schemaVersion = 1
    capturedAt = [DateTimeOffset]::UtcNow.ToString('o')
    configurationWrites = 0
    firmwareMethodInvocations = 0
    controlsQualified = $false
    completed = $false
    probeSkipped = $false
    identity = $null
    interfaces = @()
    inventory = [ordered]@{}
    errors = @()
}

function Get-ErrorCategory($Failure) {
    if ($Failure.Exception -is [UnauthorizedAccessException] -or $Failure.CategoryInfo.Category -eq 'PermissionDenied') { return 'AccessDenied' }
    if ($Failure.Exception -is [Microsoft.Management.Infrastructure.CimException]) {
        switch ([string]$Failure.Exception.StatusCode) {
            'AccessDenied' { return 'AccessDenied' }
            'InvalidClass' { return 'Absent' }
            'InvalidNamespace' { return 'Absent' }
            'NotFound' { return 'Absent' }
        }
    }
    return 'Failed'
}

function Read-Inventory([string]$Name, [scriptblock]$Read) {
    try { $report.inventory[$Name] = @(& $Read) }
    catch { $report.errors += [ordered]@{ section = $Name; status = Get-ErrorCategory $_ } }
}

try {
    $bios = Get-ItemProperty -LiteralPath 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS'
    $report.identity = [ordered]@{
        manufacturer = [string]$bios.SystemManufacturer
        product = [string]$bios.SystemProductName
        version = [string]$bios.SystemVersion
        sku = [string]$bios.SystemSKU
        biosVersion = [string]$bios.BIOSVersion
    }
    if ([string]::IsNullOrWhiteSpace($report.identity.manufacturer) -or [string]::IsNullOrWhiteSpace($report.identity.product)) {
        throw 'System identity is incomplete.'
    }
    $isLenovo = $report.identity.manufacturer.Trim() -ieq 'LENOVO'
    $report.probeSkipped = -not $isLenovo
    if ($isLenovo) {
        $targets = [ordered]@{
            LENOVO_GAMEZONE_DATA = '887B54E3-DDDC-4B2C-8B88-68A26A8835D0'
            LENOVO_OTHER_METHOD = 'DC2A8805-3A8C-41BA-A6F7-092E0089CD3B'
            LENOVO_CAPABILITY_DATA_00 = '362A3AFE-3D96-4665-8530-96DAD5BB300E'
            LENOVO_CAPABILITY_DATA_01 = '7A8F5407-CB67-4D6E-B547-39B3BE018154'
            LENOVO_FAN_METHOD = $null
            LENOVO_FAN_TABLE_DATA = $null
            LENOVO_FAN_TEST_DATA = 'B642801B-3D21-45DE-90AE-6E86F164FB21'
            LENOVO_LIGHTING_METHOD = $null
            LENOVO_UTILITY_DATA = $null
        }
        foreach ($name in $targets.Keys) {
            $entry = [ordered]@{ className = $name; status = 'Failed'; guid = $null; provider = $null; properties = @(); methods = @() }
            try {
                $schema = Get-CimClass -Namespace root/wmi -ClassName $name -OperationTimeoutSec 3
                $entry.guid = [string]$schema.CimClassQualifiers['Guid'].Value
                $entry.provider = [string]$schema.CimClassQualifiers['Provider'].Value
                $entry.status = 'Observed'
                if ($entry.provider -ine 'WmiProv' -or ($targets[$name] -and $entry.guid.Trim('{}') -ine $targets[$name])) {
                    $entry.status = 'UnexpectedSchema'
                }
                $entry.properties = @($schema.CimClassProperties | Sort-Object Name | ForEach-Object {
                    [ordered]@{ name = $_.Name; type = [string]$_.CimType }
                })
                $entry.methods = @($schema.CimClassMethods | Sort-Object Name | ForEach-Object {
                    [ordered]@{
                        name = $_.Name
                        returnType = [string]$_.ReturnType
                        parameters = @($_.Parameters | Sort-Object Name | ForEach-Object {
                            [ordered]@{ name = $_.Name; type = [string]$_.CimType }
                        })
                    }
                })
            }
            catch { $entry.status = Get-ErrorCategory $_ }
            $report.interfaces += $entry
        }
        Read-Inventory 'windows' { Get-CimInstance Win32_OperatingSystem -Property Caption,Version,BuildNumber,OSArchitecture -OperationTimeoutSec 3 | Select-Object Caption,Version,BuildNumber,OSArchitecture }
        Read-Inventory 'cpu' { Get-CimInstance Win32_Processor -Property Name,Manufacturer -OperationTimeoutSec 3 | Select-Object Name,Manufacturer }
        Read-Inventory 'gpu' { Get-CimInstance Win32_VideoController -Property Name,DriverVersion -OperationTimeoutSec 3 | Select-Object Name,DriverVersion }
        Read-Inventory 'lenovoDrivers' {
            Get-CimInstance Win32_PnPSignedDriver -Filter "DriverProviderName LIKE '%Lenovo%'" -Property DeviceName,DeviceClass,DriverVersion,DriverProviderName,InfName -OperationTimeoutSec 3 |
                Select-Object DeviceName,DeviceClass,DriverVersion,DriverProviderName,InfName
        }
        # Hardware IDs describe model/interface, unlike instance IDs which may embed serial numbers.
        Read-Inventory 'hidHardwareIds' {
            Get-CimInstance Win32_PnPEntity -Filter "PNPClass = 'HIDClass' AND Present = TRUE" -Property HardwareID -OperationTimeoutSec 3 | ForEach-Object {
                @($_.HardwareID) | Where-Object { $_ -match '^HID\\VID_(048D|17EF|1B1C)&' }
            } | Sort-Object -Unique
        }
    }
    $badSchemas = @($report.interfaces | Where-Object { $_.status -notin @('Observed', 'Absent') })
    $report.completed = $report.errors.Count -eq 0 -and $badSchemas.Count -eq 0
}
catch { $report.errors += [ordered]@{ section = 'identity'; status = Get-ErrorCategory $_ } }

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output)) | Out-Null
$json = $report | ConvertTo-Json -Depth 12
$stream = [IO.File]::Open($Output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try { $bytes = [Text.Encoding]::UTF8.GetBytes($json); $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
finally { $stream.Dispose() }
if (-not $report.completed) { throw 'Legion diagnostic incomplete. The report records failed sections; existing controls were not changed.' }
Write-Output "Saved read-only report: $Output"
