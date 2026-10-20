$ErrorActionPreference = 'Stop'
$collector = Join-Path $PSScriptRoot 'Collect-LenovoLegion.ps1'
$fixtureRoot = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/legion-collector-check-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$global:SoraLegionCollectorScenario = 'observed'
$global:SoraLegionCollectorQueries = 0

# These mocks prevent the Lenovo path from touching real firmware or enumerating real devices.
function Get-ItemProperty {
    param($LiteralPath)
    if ($LiteralPath -ne 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS') { throw 'Unexpected registry read.' }
    [pscustomobject]@{ SystemManufacturer='LENOVO'; SystemProductName='82JQ'; SystemVersion='Legion 5 Pro'; SystemSKU='Test'; BIOSVersion='TEST'; SerialNumber='PRIVATE_SERIAL' }
}
function Get-CimClass {
    param($Namespace, $ClassName, $OperationTimeoutSec)
    $global:SoraLegionCollectorQueries++
    if ($Namespace -ne 'root/wmi') { throw 'Unexpected WMI namespace.' }
    if ($global:SoraLegionCollectorScenario -eq 'denied') { throw [UnauthorizedAccessException]::new('PRIVATE_ERROR') }
    $guids = @{
        LENOVO_GAMEZONE_DATA='887B54E3-DDDC-4B2C-8B88-68A26A8835D0'
        LENOVO_OTHER_METHOD='DC2A8805-3A8C-41BA-A6F7-092E0089CD3B'
        LENOVO_CAPABILITY_DATA_00='362A3AFE-3D96-4665-8530-96DAD5BB300E'
        LENOVO_CAPABILITY_DATA_01='7A8F5407-CB67-4D6E-B547-39B3BE018154'
        LENOVO_FAN_TEST_DATA='B642801B-3D21-45DE-90AE-6E86F164FB21'
    }
    $guid = if ($guids.ContainsKey($ClassName)) { $guids[$ClassName] } else { '00000000-0000-0000-0000-000000000000' }
    $provider = if ($global:SoraLegionCollectorScenario -eq 'unexpected') { 'DifferentProvider' } else { 'WmiProv' }
    [pscustomobject]@{
        CimClassQualifiers=@{ Guid=[pscustomobject]@{Value=$guid}; Provider=[pscustomobject]@{Value=$provider} }
        CimClassProperties=@([pscustomobject]@{Name='Active';CimType='Boolean'})
        CimClassMethods=@([pscustomobject]@{Name='SetSmartFanMode';ReturnType='UInt32';Parameters=@([pscustomobject]@{Name='Data';CimType='UInt32'})})
    }
}
function Get-CimInstance {
    param($ClassName, $Filter, $Property, $OperationTimeoutSec)
    if ($ClassName -eq 'Win32_PnPEntity') {
        return [pscustomobject]@{ HardwareID=@('HID\VID_048D&PID_C985&UP:FF89_U:00CC'); DeviceID='PRIVATE_INSTANCE' }
    }
    [pscustomobject]@{Name='Test';Manufacturer='Test';DriverProviderName='Lenovo';DriverVersion='1';DeviceName='Test';DeviceClass='Test';InfName='test.inf';Caption='Windows';Version='11';BuildNumber='Test';OSArchitecture='64-bit';SerialNumber='PRIVATE_SERIAL';DeviceID='PRIVATE_INSTANCE'}
}
function Invoke-CimMethod { throw 'The collector must never invoke firmware methods.' }

foreach ($case in @('observed', 'denied', 'unexpected')) {
    $global:SoraLegionCollectorScenario = $case
    $path = Join-Path $fixtureRoot ($case + '.json')
    $failed = $false
    try { & $collector -Output $path | Out-Null } catch { $failed = $true }
    $raw = [IO.File]::ReadAllText($path)
    $result = $raw | ConvertFrom-Json
    if ($raw -match 'PRIVATE_' -or $result.configurationWrites -ne 0 -or $result.firmwareMethodInvocations -ne 0 -or $result.controlsQualified) {
        throw "Privacy or write isolation failed: $case"
    }
    if ($result.interfaces.Count -ne 9) { throw "Schema inspection incomplete: $case" }
    if ($case -eq 'observed') {
        if ($failed -or -not $result.completed -or $result.inventory.hidHardwareIds.Count -ne 1) { throw 'Observed metadata capture failed.' }
        $before = [IO.File]::ReadAllText($path)
        $queryCount = $global:SoraLegionCollectorQueries
        try { & $collector -Output $path | Out-Null; throw 'Existing evidence was overwritten.' }
        catch { if ($_.Exception.Message -notmatch 'Choose a new report path') { throw } }
        if ($before -ne [IO.File]::ReadAllText($path) -or $queryCount -ne $global:SoraLegionCollectorQueries) { throw 'Existing-report refusal must precede inspection.' }
    }
    else {
        $expected = if ($case -eq 'denied') { 'AccessDenied' } else { 'UnexpectedSchema' }
        if (-not $failed -or $result.completed -or @($result.interfaces | Where-Object status -ne $expected).Count -ne 0) {
            throw "Failed metadata must stay distinguishable: $case; failed=$failed; completed=$($result.completed); statuses=$($result.interfaces.status -join ',')"
        }
    }
}
Write-Output 'PASS Legion collector: schema metadata, provider mismatch, access errors, privacy and evidence preservation (mocked Lenovo hardware).'
# Keep temporary JSON fixtures available for inspection; no recursive cleanup is needed.
