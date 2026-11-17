param([string]$SpotifyPath = (Join-Path $env:APPDATA 'Spotify\Spotify.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path $PSScriptRoot -Parent
$package = [IO.Path]::Combine($root, 'Release', 'Dual-Spotify-Windows.zip')
$work = [IO.Path]::Combine($root, 'Verification', 'Installation-' + [Guid]::NewGuid().ToString('N'))
$clean = [IO.Path]::Combine($work, 'Portable launcher')
[IO.Compression.ZipFile]::ExtractToDirectory($package, $clean)

$assembly = [Reflection.Assembly]::LoadFile([IO.Path]::Combine($clean, 'Dual Spotify.exe'))
$engine = $assembly.GetType('Engine', $true)
if ($engine.GetField('Home').GetValue($null) -ne $clean) { throw 'The test must run in the extracted copy.' }
$install = $assembly.GetType('SpotifyInstallation', $true)
$valid = $install.GetMethod('IsStandalone')
$customFolder = [IO.Path]::Combine($work, 'Custom Spotify - ' + [char]0x00E9)
$dummyFolder = [IO.Path]::Combine($work, 'Invalid executable')
$null = [IO.Directory]::CreateDirectory($customFolder)
$null = [IO.Directory]::CreateDirectory($dummyFolder)
$custom = [IO.Path]::Combine($customFolder, 'Spotify.exe')
$dummy = [IO.Path]::Combine($dummyFolder, 'Spotify.exe')
[IO.File]::Copy($SpotifyPath, $custom)
[IO.File]::WriteAllText($dummy, 'test file')
if (!$valid.Invoke($null, [object[]]@($custom)) -or $valid.Invoke($null, [object[]]@($dummy))) {
    throw 'The locator must accept Spotify and reject unrelated files.'
}
$engine.GetMethod('SelectSpotify').Invoke($null, [object[]]@($custom))
$engine.GetMethod('DetectInstallations').Invoke($null, $null)
if ($engine.GetProperty('Spotify').GetValue($null) -ne $custom -or
    [IO.File]::ReadAllText([IO.Path]::Combine($clean, 'Data', 'SpotifyPath.txt')) -ne $custom) {
    throw 'Detect Spotify failed to retain the selected installation.'
}
$expectedStore = [bool](Get-AppxPackage -Name SpotifyAB.SpotifyMusic)
if ($engine.GetProperty('StoreAvailable').GetValue($null) -ne $expectedStore) {
    throw 'Microsoft Store detection disagrees with the registered packages.'
}
$packageFolder = $install.GetMethod('FindPackageFolder')
$registered = Get-AppxPackage -Name SpotifyAB.SpotifyMusic | Select-Object -First 1
if (!$registered) { $registered = Get-AppxPackage -Name Microsoft.WindowsTerminal | Select-Object -First 1 }
if ($registered -and $registered.InstallLocation) {
    $family = [string]$registered.PackageFamilyName
    $actual = $packageFolder.Invoke($null, [object[]]@($family))
    if ($actual -ne $registered.InstallLocation) { throw 'Native package lookup returned a different install location.' }
    'Native Store package lookup matched Windows package registration.'
}
$absent = $packageFolder.Invoke($null, [object[]]@('DualSpotify.MissingPackage_zpdnekdrzrea0'))
if ($absent) { throw 'Native package lookup returned a path for an unregistered package.' }
if ($assembly.GetType('ManagedSecond').GetProperty('Configured').GetValue($null)) {
    throw 'The portable ZIP must start without a managed Windows user.'
}
'Verified alternate installation discovery, path persistence, Store package detection, and clean managed-profile state.'
