$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$policy = Join-Path $root 'release\update-policy.json'
Copy-Item -LiteralPath $policy -Destination (Join-Path $root 'web\sora-downloads\public\update-policy.json')
Write-Host 'Public release trust policy copied to the website. No private key is copied.'
