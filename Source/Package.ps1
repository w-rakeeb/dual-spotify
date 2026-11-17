$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$project = Split-Path $PSScriptRoot -Parent
$appFile = [IO.Path]::Combine($project, 'Dual Spotify.exe')
$assembly = [Reflection.Assembly]::LoadFrom($appFile)
$packageType = $assembly.GetType('DualSpotify.UpdatePackage', $true)
$service = $assembly.GetType('DualSpotify.UpdateService', $true)
$version = $service.GetField('CurrentVersion').GetRawConstantValue()
$assetName = $service.GetField('AssetName').GetRawConstantValue()
if ($assembly.GetName().Version.ToString(3) -ne $version) { throw 'Build the current release before packaging.' }

$release = Join-Path $project 'Release'
$null = New-Item -ItemType Directory -Path $release -Force
$package = [IO.Path]::Combine($release, $assetName)
$staging = [IO.Path]::Combine($release, [Guid]::NewGuid().ToString('N') + '.zip')
try {
    $archive = [IO.Compression.ZipFile]::Open($staging, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $packageType.GetField('Files').GetValue($null)) {
            $source = Join-Path $project $relative
            if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release file: $relative" }
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $source, $relative, 'Optimal')
        }
    } finally { $archive.Dispose() }
    $null = $packageType.GetMethod('Validate').Invoke($null, [object[]]@($staging))
    if ([IO.File]::Exists($package)) { [IO.File]::Replace($staging, $package, $null) }
    else { [IO.File]::Move($staging, $package) }
} finally {
    if ([IO.File]::Exists($staging)) { [IO.File]::Delete($staging) }
}
$sha = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'), "$sha  $assetName" + [Environment]::NewLine)
Get-Item -LiteralPath $package
