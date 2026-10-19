param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$TaskRoot = Split-Path $PSScriptRoot -Parent
$LicenseDirectory = Join-Path $ReleaseDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $LicenseDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $TaskRoot 'THIRD-PARTY-NOTICES.md') -Destination $ReleaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $TaskRoot 'assets\licenses\LibreHardwareMonitor-MPL-2.0.txt') -Destination (Join-Path $LicenseDirectory 'LibreHardwareMonitor-MPL-2.0.txt') -Force
foreach ($IconLicense in @('MahApps.IconPacks-MIT.txt', 'Lucide-ISC-Feather-MIT.txt')) {
    Copy-Item -LiteralPath (Join-Path $TaskRoot ('assets\licenses\' + $IconLicense)) -Destination $LicenseDirectory -Force
}
$Packages = @{}
foreach ($ProjectName in @('Sora.Core', 'Sora.App')) {
    $Lock = Get-Content -LiteralPath (Join-Path $TaskRoot "src\$ProjectName\packages.lock.json") -Raw | ConvertFrom-Json
    foreach ($Framework in $Lock.dependencies.PSObject.Properties) {
        foreach ($Package in $Framework.Value.PSObject.Properties) {
            if ($Package.Value.resolved) { $Packages[$Package.Name + '/' + $Package.Value.resolved] = $Package }
        }
    }
}
$PackageRoots = @($env:NUGET_PACKAGES, (Join-Path $TaskRoot '.nuget\packages'), (Join-Path $env:USERPROFILE '.nuget\packages')) | Where-Object { $_ }
$Lines = [Collections.Generic.List[string]]::new()
$Lines.Add('# Resolved NuGet components')
$Lines.Add('')
foreach ($Package in ($Packages.Values | Sort-Object Name)) {
    $Name = $Package.Name
    $Version = $Package.Value.resolved
    $Directory = $PackageRoots | ForEach-Object { Join-Path $_ ($Name.ToLowerInvariant() + '\' + $Version) } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $Directory) { throw "Missing restored package: $Name $Version" }
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
