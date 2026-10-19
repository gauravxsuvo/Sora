param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\logo-export'))
$ErrorActionPreference = 'Stop'
# Compatibility entry point: export the current reference-derived logo, never the archived spiral.
& (Join-Path $PSScriptRoot 'Build-Brand.ps1')
$brand = Join-Path $PSScriptRoot '..\assets\brand'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $brand 'sora-mark.svg') -Destination (Join-Path $OutputDirectory 'sora-logo.svg')
Copy-Item -LiteralPath (Join-Path $brand 'sora-mark-current.svg') -Destination (Join-Path $OutputDirectory 'sora-symbol.svg')
Copy-Item -LiteralPath (Join-Path $brand 'sora-mark.png') -Destination (Join-Path $OutputDirectory 'sora-logo.png')
Write-Host "Current Sora logo exported to $OutputDirectory"