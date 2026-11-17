$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
$root = Split-Path $PSScriptRoot -Parent
$work = [IO.Path]::Combine($root, 'Verification', 'Settings-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($work)
$app = [Reflection.Assembly]::LoadFrom([IO.Path]::Combine($root, 'Dual Spotify.exe'))

function Assert($value, [string]$message) {
    if (!$value) { throw $message }
}

$type = $app.GetType('Preferences', $true)
[IO.File]::WriteAllLines([IO.Path]::Combine($work, 'Preferences.txt'), [string[]]@('Light','False','True'))
$type.GetMethod('Load').Invoke($null, [object[]]@($work))
$prefs = $type.GetField('Current').GetValue($null)
Assert ($prefs.ThemeMode -eq 'Light' -and !$prefs.TrayOnClose -and $prefs.SecondTray) 'Existing preferences did not migrate.'
Assert ($prefs.SeparateTaskbar -and $prefs.KeepSecondRunning -and $prefs.AutoUpdates -and !$prefs.StartSecondWithWindows) 'New defaults should preserve lifecycle and stay opt-in for startup.'
[IO.File]::WriteAllText([IO.Path]::Combine($work, 'preferences.json'), '{"ThemeMode":"System","AutoUpdates":false,"SeparateTaskbar":false}')
$type.GetMethod('Load').Invoke($null, [object[]]@($work))
$prefs = $type.GetField('Current').GetValue($null)
Assert ($prefs.ThemeMode -eq 'System' -and !$prefs.AutoUpdates -and !$prefs.SeparateTaskbar -and $prefs.TrayOnClose) 'New preferences did not override legacy values or preserve defaults.'

$storage = $app.GetType('ProfileStorage', $true).GetMethod('CopyVerified')
$source = [IO.Path]::Combine($work, 'Original profile')
$target = [IO.Path]::Combine($work, 'Second profile - ' + [char]0x00E9)
$relative = 'Cache\Users\profile-test'
$fixture = [IO.Path]::Combine($source, $relative)
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fixture))
[IO.File]::WriteAllText($fixture, 'profile sentinel')
$blocked = [IO.Path]::Combine($work, 'Main files')
$returned = $storage.Invoke($null, [object[]]@($source, $target, [string[]]@($blocked)))
Assert ($returned -eq $target -and [IO.File]::ReadAllText($fixture) -eq 'profile sentinel' -and
    [IO.File]::ReadAllText([IO.Path]::Combine($target, $relative)) -eq 'profile sentinel') 'Copy failed or changed the old profile.'
foreach ($reject in @($target, $blocked, [IO.Path]::Combine($source, 'Child'))) {
    $failed = $false
    try { $null = $storage.Invoke($null, [object[]]@($source, $reject, [string[]]@($blocked))) }
    catch { $failed = $_.Exception.InnerException -is [IO.IOException] }
    Assert $failed 'Profile relocation accepted a nonempty or protected destination.'
}

$scheduleType = $app.GetType('DualSpotify.UpdateSchedule', $true)
$schedule = [Activator]::CreateInstance($scheduleType)
$clock = [datetime]::new(2026, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
Assert (!$schedule.TakeCheck($false, $clock, $false)) 'Disabled automatic checks still polled.'
Assert ($schedule.TakeCheck($true, $clock, $false)) 'Startup was not checked.'
Assert (!$schedule.TakeCheck($true, $clock.AddSeconds(59), $true)) 'Reopen throttle failed.'
Assert ($schedule.TakeCheck($true, $clock.AddMinutes(1), $true)) 'Reopen should refresh after a minute.'
Assert (!$schedule.TakeCheck($true, $clock.AddMinutes(15), $false)) 'Periodic check arrived early after reopen.'
Assert ($schedule.TakeCheck($true, $clock.AddMinutes(16), $false)) 'Periodic check was not scheduled.'
$schedule.Failed($clock.AddMinutes(16), $false)
Assert (!$schedule.TakeCheck($true, $clock.AddSeconds(1019), $true)) 'A failed request retried too soon.'
Assert ($schedule.TakeCheck($true, $clock.AddMinutes(17), $false)) 'A failed request did not retry after a minute.'
$schedule.Failed($clock.AddMinutes(17), $true)
Assert (!$schedule.TakeCheck($true, $clock.AddMinutes(31), $true)) 'GitHub rate-limit backoff failed.'
Assert ($schedule.TakeCheck($true, $clock.AddMinutes(32), $true)) 'GitHub rate-limit backoff did not expire.'
Assert ($schedule.Announce('v0.3.0') -and !$schedule.Announce('v0.3.0') -and $schedule.Announce('v0.4.0')) 'Update notices did not deduplicate by version.'
'Settings, profile copy and update schedule checks passed.'
