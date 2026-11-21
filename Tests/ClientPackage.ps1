param([string]$SpotifyPath = (Join-Path $env:APPDATA 'Spotify\Spotify.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$project = Split-Path $PSScriptRoot -Parent
$work = Join-Path $project ('Verification\ClientPackage-' + [guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($work)
$assembly = [Reflection.Assembly]::LoadFile([IO.Path]::Combine($project,'Dual Spotify.exe'))
$runtime = $assembly.GetType('SpotifyClientPackage',$true)
$package = Join-Path $work 'SpotifyClient.zip'
$client = Join-Path $work 'Client'
$null = [IO.Directory]::CreateDirectory($client)
[IO.File]::WriteAllText((Join-Path $client 'previous-sentinel.txt'),'preserve previous files')
$runtime.GetMethod('Create').Invoke($null,[object[]]@([string](Split-Path $SpotifyPath -Parent),[string]$package))
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $names = @($archive.Entries | ForEach-Object FullName)
    foreach ($forbidden in @('prefs','Users','ad-device-state','debug.log','update.json','wsock32.dll')) {
        if (@($names | Where-Object {$_ -eq $forbidden -or $_.StartsWith($forbidden+'/')}).Count) {throw 'Client package included data outside the application files.'}
    }
    if ('Spotify.exe' -notin $names -or 'client-manifest.json' -notin $names) {throw 'Client package is incomplete.'}
} finally { $archive.Dispose() }
$runtime.GetMethod('Install').Invoke($null,[object[]]@([string]$package,[string]$client))
if (!$runtime.GetMethod('Complete').Invoke($null,[object[]]@([string]$client))) {throw 'The prepared client did not install.'}
if ((Get-FileHash -LiteralPath (Join-Path $client 'Spotify.exe')).Hash -ne (Get-FileHash -LiteralPath $SpotifyPath).Hash) {throw 'Client executable does not match signed source.'}
if (!@(Get-ChildItem -LiteralPath $work -Directory -Filter 'Client.previous-*' | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'previous-sentinel.txt')}).Count) {throw 'Repair discarded the original folder.'}
foreach($name in '../Spotify.exe','Apps/../prefs','Users/credentials','locales/en-US.pak:stream','Apps/xpui.spa.','/Spotify.exe','wsock32.dll') {
    if ($runtime.GetMethod('Allowed').Invoke($null,[object[]]@([string]$name))) {throw "Unsafe package path accepted: $name"}
}
$malicious = Join-Path $work 'Traversal.zip'
$bad = [IO.Compression.ZipFile]::Open($malicious,'Create')
try {
    $entry = $bad.CreateEntry('client-manifest.json')
    $writer = [IO.StreamWriter]::new($entry.Open())
    try {$writer.Write('{"../escape.txt":"bad"}')} finally {$writer.Dispose()}
    $entry = $bad.CreateEntry('../escape.txt')
    $writer = [IO.StreamWriter]::new($entry.Open())
    try {$writer.Write('rejected')} finally {$writer.Dispose()}
} finally {$bad.Dispose()}
$blocked = $false
try {$runtime.GetMethod('Install').Invoke($null,[object[]]@([string]$malicious,[string]$client))}
catch {$blocked = $_.Exception.InnerException -is [IO.IOException]}
if (!$blocked -or (Test-Path -LiteralPath (Join-Path $work 'escape.txt'))) {throw 'Unsafe package was extracted.'}
$corrupted = Join-Path $work 'Checksum.zip'
$bad = [IO.Compression.ZipFile]::Open($corrupted,'Create')
try {
    foreach($fixture in @(@('client-manifest.json','{"Spotify.exe":"0000"}'),@('Spotify.exe','tampered client'))) {
        $writer = [IO.StreamWriter]::new($bad.CreateEntry($fixture[0]).Open())
        try {$writer.Write($fixture[1])} finally {$writer.Dispose()}
    }
} finally {$bad.Dispose()}
$blocked = $false
try {$runtime.GetMethod('Install').Invoke($null,[object[]]@([string]$corrupted,[string]$client))}
catch {$blocked = $_.Exception.InnerException -is [IO.IOException]}
if (!$blocked) {throw 'Corrupted application files were accepted.'}
if (!(Test-Path -LiteralPath (Join-Path $client 'Spotify.exe'))) {throw 'Failed install changed the existing client.'}
'Verified signed client copy, account-data exclusion, incomplete-install recovery, and rejection of unsafe ZIP paths and corrupt checksums.'
