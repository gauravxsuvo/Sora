param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$Compiler
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Packages an already qualified folder publish. Never builds, deploys, repoints startup, or copies user data.
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$compilerPath = (Resolve-Path -LiteralPath $Compiler).Path
if ($release -match '[{};\r\n]' -or $output -match '[{};\r\n]') { throw 'Unsupported installer source/output path characters.' }
if (Test-Path -LiteralPath $output) { throw 'Installer output already exists; preserve it and choose a new directory.' }
$releasePrefix = $release.TrimEnd('\') + '\'
if ($output.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer output cannot be inside the source release.' }
function Assert-NoReparseAncestor([string]$Path) {
    for ($item = Get-Item -LiteralPath $Path -Force; $null -ne $item; $item = $item.Parent) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point is not an installer source: $($item.FullName)" }
    }
}
Assert-NoReparseAncestor $release
$existingParent = [IO.Path]::GetDirectoryName($output)
while (-not (Test-Path -LiteralPath $existingParent)) { $existingParent = [IO.Path]::GetDirectoryName($existingParent) }
Assert-NoReparseAncestor $existingParent
$signature = Get-AuthenticodeSignature -LiteralPath $compilerPath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Pyrsys B.V.') { throw 'Use an official Inno Setup compiler with a valid Pyrsys B.V. signature.' }

# Walk one level at a time, rejecting links before traversal.
$pending = [Collections.Generic.Queue[string]]::new()
$pending.Enqueue($release)
$files = [Collections.Generic.List[object]]::new()
while ($pending.Count) {
    foreach ($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release contains a reparse point: $($item.FullName)" }
        $relative = $item.FullName.Substring($releasePrefix.Length)
        if ($relative -match '[{};\r\n]' -or $item.Name -ieq 'data' -or $item.Name -ieq 'sora-package.json' -or $item.Extension -in @('.log', '.tmp', '.old')) {
            throw "Release contains user data, temporary files, or a reserved path: $relative"
        }
        if ($item.PSIsContainer) { $pending.Enqueue($item.FullName); continue }
        $files.Add([pscustomobject]@{ relative = $relative; bytes = $item.Length; sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash })
    }
}
$required = @('Sora.exe','Sora.dll','Sora.Core.dll','Sora.deps.json','Sora.runtimeconfig.json','THIRD-PARTY-NOTICES.md',
    'licenses\LibreHardwareMonitor-MPL-2.0.txt')
foreach ($name in $required) { if ($name -notin $files.relative) { throw "Incomplete folder publish: $name" } }
# A RID-specific folder publish flattens this runtime assembly; a portable folder publish retains runtimes/.
# The dependency-manifest walk below independently checks the actual selected asset's destination.
if ('LibreHardwareMonitorLib.dll' -notin $files.relative -and 'runtimes\win-x64\lib\net8.0\LibreHardwareMonitorLib.dll' -notin $files.relative) {
    throw 'Incomplete folder publish: LibreHardwareMonitorLib.dll'
}
$exe = Join-Path $release 'Sora.exe'
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead($exe))
try {
    if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Sora.exe is not a PE executable.' }
    $reader.BaseStream.Position = 0x3c
    $offset = $reader.ReadInt32()
    $reader.BaseStream.Position = $offset
    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Sora.exe must be a Windows x64 executable.' }
} finally { $reader.Dispose() }
$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$version = '{0}.{1}.{2}' -f $versionInfo.FileMajorPart,$versionInfo.FileMinorPart,$versionInfo.FileBuildPart
if ($versionInfo.ProductName -ne 'Sora' -or $versionInfo.FilePrivatePart -ne 0) { throw 'Unrecognized Sora product/version metadata.' }
if ([version]$version -ge [version]'0.12.0') {
    foreach ($name in @('UpdateRunner\Sora.UpdateRunner.exe','UpdateRunner\Sora.UpdateRunner.dll','UpdateRunner\Sora.UpdateRunner.deps.json','UpdateRunner\Sora.UpdateRunner.runtimeconfig.json')) {
        if ($name -notin $files.relative) { throw "Incomplete updater-enabled publish: $name" }
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $release 'Sora.runtimeconfig.json') -Raw | ConvertFrom-Json
$frameworks = @($runtime.runtimeOptions.frameworks)
foreach ($name in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    $entry = @($frameworks | Where-Object name -eq $name)
    if ($entry.Count -ne 1 -or $entry[0].version -ne '8.0.0') { throw 'This installer requires a framework-dependent .NET 8 Windows Desktop publish.' }
}
if ($frameworks.Count -ne 2) { throw 'Unexpected runtime framework requirements.' }
$deps = Get-Content -LiteralPath (Join-Path $release 'Sora.deps.json') -Raw | ConvertFrom-Json
foreach ($target in $deps.targets.PSObject.Properties) {
    foreach ($library in $target.Value.PSObject.Properties) {
        foreach ($kind in @('runtime','native','resources','runtimeTargets')) {
            $section = $library.Value.PSObject.Properties[$kind]
            if ($null -eq $section) { continue }
            foreach ($asset in $section.Value.PSObject.Properties) {
                $name = $asset.Name.Replace('/','\')
                # NuGet runtime/native assets are flattened in a folder publish; RID assets retain runtimes/.
                if ($kind -in @('runtime','native')) { $name = [IO.Path]::GetFileName($name) }
                if ($kind -eq 'resources') { $name = Join-Path $asset.Value.locale ([IO.Path]::GetFileName($name)) }
                if ($name -notin $files.relative) { throw "Dependency manifest asset is missing: $name" }
            }
        }
    }
}
New-Item -ItemType Directory -Path $output -ErrorAction Stop | Out-Null
$payload = Join-Path $output 'payload'
New-Item -ItemType Directory -Path $payload -ErrorAction Stop | Out-Null
$entries = [Collections.Generic.List[string]]::new()
# The existing update helper also hosts bounded, read-only setup discovery.
# Extract its minimal runtime before the main payload, without starting Sora.
foreach ($name in @('Sora.UpdateRunner.exe','Sora.UpdateRunner.dll','Sora.UpdateRunner.deps.json','Sora.UpdateRunner.runtimeconfig.json')) {
    $probeFile = Join-Path $payload ('UpdateRunner\' + $name)
    $entries.Add(('Source: "{0}"; DestDir: "{{tmp}}"; Flags: dontcopy' -f $probeFile))
}
foreach ($file in $files | Sort-Object relative) {
    $destination = Join-Path $payload $file.relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    [IO.File]::Copy((Join-Path $release $file.relative), $destination, $false)
    if ((Get-FileHash -LiteralPath $destination).Hash -ne $file.sha256) { throw 'Payload changed while copying.' }
    $subdirectory = [IO.Path]::GetDirectoryName($file.relative)
    $destDir = '{app}'
    if ($subdirectory) { $destDir += '\' + $subdirectory }
    $entries.Add(('Source: "{0}"; DestDir: "{1}"; Flags: ignoreversion' -f $destination,$destDir))
}
$manifest = [ordered]@{
    product = 'Sora'; appId = 'C29647D7-C220-477C-83B6-4118690E9CF8'; version = $version
    createdAt = [DateTimeOffset]::Now.ToString('o')
    frameworkDependent = $true; architecture = 'x64'; files = @($files | Sort-Object relative)
}
$manifestPath = Join-Path $payload 'sora-package.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
$entries.Add(('Source: "{0}"; DestDir: "{{app}}"; Flags: ignoreversion' -f $manifestPath))
$include = Join-Path $output 'payload-files.iss'
[IO.File]::WriteAllLines($include, $entries, [Text.UTF8Encoding]::new($false))
$template = Join-Path $PSScriptRoot '..\installer\Sora.iss'
Copy-Item -LiteralPath $template -Destination (Join-Path $output 'Sora.iss')
$brandIcon = Join-Path $output 'setup-brand.ico'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\assets\Sora.ico') -Destination $brandIcon
$info = [Diagnostics.ProcessStartInfo]::new($compilerPath)
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.Arguments = '"{0}" "/DAppVersion={1}" "/DPayloadInclude={2}" "/DInstallerOutput={3}" "/DBrandIcon={4}"' -f (Join-Path $output 'Sora.iss'),$version,$include,$output,$brandIcon
$process = [Diagnostics.Process]::Start($info)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $compilerExit = $process.ExitCode
    [IO.File]::WriteAllText((Join-Path $output 'compiler.log'), ($stdout.Result + $stderr.Result), [Text.UTF8Encoding]::new($false))
} finally { $process.Dispose() }
if ($compilerExit -ne 0) { throw "Installer compilation failed ($compilerExit); preserve output and inspect compiler.log." }
$installer = Join-Path $output "Sora-Setup-$version-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Compiler did not produce the expected installer.' }
# Re-read source after compilation: a changing source is never silently accepted.
foreach ($file in $files) {
    if ((Get-FileHash -LiteralPath (Join-Path $release $file.relative)).Hash -ne $file.sha256) { throw 'Source release changed during packaging.' }
}
$audit = [ordered]@{
    completedAt = [DateTimeOffset]::Now.ToString('o'); passed = $true; version = $version
    installer = $installer; installerSha256 = (Get-FileHash -LiteralPath $installer).Hash
    payloadFiles = $files.Count; payloadBytes = ($files | Measure-Object bytes -Sum).Sum
    manifestSha256 = (Get-FileHash -LiteralPath $manifestPath).Hash
    compiler = $compilerPath; compilerSha256 = (Get-FileHash -LiteralPath $compilerPath).Hash
    compilerSigner = $signature.SignerCertificate.Subject
    sourceUnchanged = $true; deployed = $false; startupChanged = $false
}
[IO.File]::WriteAllText((Join-Path $output 'build-audit.json'), ($audit | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$audit | ConvertTo-Json -Depth 6
