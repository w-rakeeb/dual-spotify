$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
$root = Split-Path $PSScriptRoot -Parent
$isolated = [IO.Path]::Combine($root, 'Verification', 'Settings-' + [Guid]::NewGuid().ToString('N'))
$work = [IO.Path]::Combine($isolated, 'Data')
$null = [IO.Directory]::CreateDirectory($work)
$exe = [IO.Path]::Combine($isolated, 'Dual Spotify.exe')
[IO.File]::Copy([IO.Path]::Combine($root, 'Dual Spotify.exe'), $exe)
$app = [Reflection.Assembly]::LoadFile($exe)

function Assert($value, [string]$message) {
    if (!$value) { throw $message }
}

$type = $app.GetType('Preferences', $true)
Assert ($app.GetType('Engine', $true).GetField('Data').GetValue($null) -eq $work) 'Settings tests must write only to the isolated copy.'
[IO.File]::WriteAllLines([IO.Path]::Combine($work, 'Preferences.txt'), [string[]]@('Light','False','True'))
$type.GetMethod('Load').Invoke($null, [object[]]@($work))
$prefs = $type.GetField('Current').GetValue($null)
Assert ($prefs.ThemeMode -eq 'Light' -and !$prefs.CloseToTray -and $prefs.SecondTray) 'Existing preferences did not migrate.'
Assert ($prefs.SeparateTaskbar -and $prefs.KeepSecondRunning -and $prefs.AutoUpdates -and !$prefs.StartSecondWithWindows) 'New defaults should preserve lifecycle and stay opt-in for startup.'
[IO.File]::WriteAllText([IO.Path]::Combine($work, 'preferences.json'), '{"ThemeMode":"System","AutoUpdates":false,"SeparateTaskbar":false,"TrayOnClose":true}')
$type.GetMethod('Load').Invoke($null, [object[]]@($work))
$prefs = $type.GetField('Current').GetValue($null)
Assert ($prefs.ThemeMode -eq 'System' -and !$prefs.AutoUpdates -and !$prefs.SeparateTaskbar -and !$prefs.CloseToTray) 'New preferences did not override the legacy close-to-tray default.'

$prefs.CloseToTray = $true
$type.GetMethod('Save').Invoke($null, $null)
$type.GetMethod('Load').Invoke($null, [object[]]@($work))
Assert ($type.GetField('Current').GetValue($null).CloseToTray) 'Explicit close-to-tray opt-in did not persist.'

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
'Settings, close behavior and update schedule checks passed.'
