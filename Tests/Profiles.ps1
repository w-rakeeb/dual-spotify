$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$copy = [IO.Path]::Combine($project,'Verification','Profiles-' + [Guid]::NewGuid().ToString('N'))
$data = [IO.Path]::Combine($copy,'Data')
$null = [IO.Directory]::CreateDirectory($data)
$exe = [IO.Path]::Combine($copy,'Dual Spotify.exe')
[IO.File]::Copy([IO.Path]::Combine($project,'Dual Spotify.exe'),$exe)
$assembly = [Reflection.Assembly]::LoadFile($exe)
$engine = $assembly.GetType('Engine',$true)
if ($engine.GetField('Data').GetValue($null) -ne $data) { throw 'Tests must use the isolated assembly.' }
$profiles = $assembly.GetType('ManagedSecond',$true)
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$dedicated = @{Name='DualSpotify_00000001'; Owner=$owner; Sid='dedicated'; Password='test-placeholder'}
$shared = @{Name='DualSteamLocal'; Owner=$owner; Sid='shared'; SteamHome='C:\Example\Dual Steam'; Folder='DualSpotify_00000002'; Active=$true}
$oldDedicated = Join-Path $data 'SecondWindowsAccount.json'
$oldShared = Join-Path $data 'SharedWindowsAccount.json'
$dedicated | ConvertTo-Json -Compress | Set-Content -LiteralPath $oldDedicated
$shared | ConvertTo-Json -Compress | Set-Content -LiteralPath $oldShared
$hashes = @((Get-FileHash $oldDedicated).Hash,(Get-FileHash $oldShared).Hash)
if ($profiles.GetMethod('SelectedAccount').Invoke($null,$null).Sid -ne 'shared') {throw 'Active v0.4.0 shared selection did not migrate.'}
if ($profiles.GetMethod('Profiles').Invoke($null,$null).Count -ne 2) {throw 'A previous profile is missing from the picker.'}
$profiles.GetMethod('RemoveSelection').Invoke($null,$null)
if ($profiles.GetProperty('Configured').GetValue($null)) {throw 'Removing the selection automatically picked another account.'}
$saved = Get-Content -LiteralPath (Join-Path $data 'WindowsProfiles.json') -Raw | ConvertFrom-Json
if ($saved.Accounts.Count -ne 2 -or !($saved.Accounts | Where-Object { $_.Sid -eq 'shared' -and $_.Removed })) {throw 'The removed account could not be restored later.'}
if ((Get-FileHash $oldDedicated).Hash -ne $hashes[0] -or (Get-FileHash $oldShared).Hash -ne $hashes[1]) {throw 'Migration modified the original settings.'}
'Profile catalog migrated v0.4.0, retained both choices, and removed the current selection without a fallback.'
