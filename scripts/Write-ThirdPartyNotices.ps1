param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$TaskRoot = Split-Path $PSScriptRoot -Parent
$LicenseDirectory = Join-Path $ReleaseDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $LicenseDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $TaskRoot 'THIRD-PARTY-NOTICES.md') -Destination $ReleaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $TaskRoot 'vendor\LibreHardwareMonitor\LICENSE') -Destination (Join-Path $LicenseDirectory 'LibreHardwareMonitor-MPL-2.0.txt') -Force
$Lock = Get-Content -LiteralPath (Join-Path $TaskRoot 'src\Sora.Core\packages.lock.json') -Raw | ConvertFrom-Json
$Lines = [Collections.Generic.List[string]]::new()
$Lines.Add('# Resolved NuGet components')
$Lines.Add('')
foreach ($Package in $Lock.dependencies.'net8.0'.PSObject.Properties) {
    $Name = $Package.Name
    $Version = $Package.Value.resolved
    $Directory = Join-Path $TaskRoot ('.nuget\packages\' + $Name.ToLowerInvariant() + '\' + $Version)
    $Nuspec = Get-ChildItem -LiteralPath $Directory -Filter '*.nuspec' -File | Select-Object -First 1
    if (-not $Nuspec) { throw "Missing package metadata: $Name $Version" }
    [xml]$Metadata = Get-Content -LiteralPath $Nuspec.FullName -Raw
    $License = $Metadata.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="license"]')
    $LicenseUrl = $Metadata.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="licenseUrl"]')
    $ProjectUrl = $Metadata.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="projectUrl"]')
    $LicenseText = if ($License) { $License.InnerText } elseif ($LicenseUrl) { $LicenseUrl.InnerText } else { 'See package metadata' }
    $Lines.Add("- **$Name $Version** — $LicenseText")
    if ($ProjectUrl) { $Lines.Add('  Project: ' + $ProjectUrl.InnerText) }
    $Texts = Get-ChildItem -LiteralPath $Directory -File | Where-Object { $_.Name -match '(?i)license|notice|copying' }
    if ($Texts) {
        $PackageOutput = Join-Path $LicenseDirectory ($Name + '-' + $Version)
        New-Item -ItemType Directory -Force -Path $PackageOutput | Out-Null
        foreach ($TextFile in $Texts) { Copy-Item -LiteralPath $TextFile.FullName -Destination $PackageOutput -Force }
    }
}
$Lines | Set-Content -LiteralPath (Join-Path $LicenseDirectory 'dependencies.md')
