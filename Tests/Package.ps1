param([string]$PreviousPackage)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
$package = [IO.Path]::Combine($root, 'Release', 'Dual-Spotify-Windows.zip')
$work = [IO.Path]::Combine($root, 'Verification', 'Update-' + [Guid]::NewGuid().ToString('N'))
$target = [IO.Path]::Combine($work, 'Portable install')
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::Combine($root, 'Dual Spotify.exe'))
$updater = [IO.Path]::Combine($root, 'Dual Spotify Updater.exe')
$version = $assembly.GetType('DualSpotify.UpdateService').GetField('CurrentVersion').GetRawConstantValue()
$digest = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()

[IO.Compression.ZipFile]::ExtractToDirectory($(if ($PreviousPackage) { $PreviousPackage } else { $package }), $target)
if ($PreviousPackage) {
    $oldUpdater = [IO.Path]::Combine($work, 'Dual Spotify Updater.exe')
    [IO.File]::Copy([IO.Path]::Combine($target, 'Dual Spotify Updater.exe'), $oldUpdater)
    $updater = $oldUpdater
}
$installed = [IO.Path]::Combine($target, 'Dual Spotify.exe')
$profile = [IO.Path]::Combine($target, 'Data', 'Keep.txt')
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($profile))
[IO.File]::WriteAllText($profile, 'profile sentinel')
$readme = [IO.Path]::Combine($target, 'README.md')
[IO.File]::WriteAllText($readme, 'prior README')
$requestFile = [IO.Path]::Combine($work, 'request.json')

function Invoke-Updater([string]$sha) {
    @{
        Target = $target
        Package = $package
        Sha256 = $sha
        Version = $version
        ManagerPid = 0
        Restart = $false
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath $requestFile -Encoding UTF8
    $process = Start-Process -FilePath $updater -ArgumentList '--request', ('"' + $requestFile + '"') -Wait -PassThru
    return $process.ExitCode
}

if ((Invoke-Updater ('0' * 64)) -eq 0 -or [IO.File]::ReadAllText($readme) -ne 'prior README') {
    throw 'An invalid checksum should leave app files untouched.'
}
if ((Invoke-Updater $digest) -ne 0) {
    throw (Get-Content -LiteralPath (Join-Path $work 'result.txt') -Raw)
}
if ([IO.File]::ReadAllText($profile) -ne 'profile sentinel') { throw 'The updater touched the profile.' }
$backup = @(Get-ChildItem -LiteralPath (Join-Path $target 'Backups') -Directory)
if ($backup.Count -ne 1 -or [IO.File]::ReadAllText((Join-Path $backup[0].FullName 'README.md')) -ne 'prior README') {
    throw 'The updater did not preserve the previous app file.'
}
if ((Get-FileHash -LiteralPath $readme).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'README.md')).Hash) {
    throw 'The updated README does not match the package.'
}
if ([Reflection.AssemblyName]::GetAssemblyName($installed).Version.ToString(3) -ne $version) {
    throw 'The updater did not install the expected version.'
}
'Package checks passed. The updater rejected the wrong checksum, installed the verified package, backed up app files, and preserved profile data.'
