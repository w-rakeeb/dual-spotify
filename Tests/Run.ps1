param([switch]$Live)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$launcher = Join-Path $root 'Dual Spotify.exe'
$engine = [Reflection.Assembly]::LoadFrom($launcher).GetType('Engine', $true)
$profile = [IO.Path]::Combine($root, 'Data', 'Profiles', 'Second Spotify', 'Cache')
$classify = $engine.GetMethod('Classify')

function Assert($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Classify([string[]]$command) {
    $classify.Invoke($null, [object[]]@($command, $profile))
}

$arguments = $engine.GetMethod('SecondArguments').Invoke($null, [object[]]@($profile))
$split = $engine.GetMethod('Split', [Reflection.BindingFlags]'Static, NonPublic')
$parsed = $split.Invoke($null, [object[]]@('Spotify.exe ' + $arguments))
Assert ((Classify $parsed) -eq 1) 'Profile paths with spaces must round-trip.'
Assert ($parsed -contains '--disable-desktop-launcher') 'Second Spotify must bypass the shared desktop launcher.'
Assert ((Classify @('Spotify.exe')) -eq 0) 'The default client must be recognized.'
Assert ((Classify @('Spotify.exe', '--type=renderer')) -eq -1) 'Helpers must be excluded.'
Assert ((Classify @('Spotify.exe', '--mu=Other', "--cache-path=$profile")) -eq -1) 'An unrelated instance must be excluded.'
Assert ((Classify @('Spotify.exe', $parsed[1], "--cache-path=${profile}-other")) -eq -1) 'A sibling profile folder must be excluded.'
Assert ((Classify ($parsed + '--mu=Other')) -eq -1) 'Ambiguous launch flags must be rejected.'
'Seven launch and process-classification checks passed.'
if (!$Live) { return }

$managed = $engine.Assembly.GetType('ManagedSecond', $true)
Assert ($managed.GetProperty('Configured').GetValue($null)) 'Set up Second Spotify in this copy before running live checks.'

Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes

function State {
    $engine.GetMethod('Snapshot').Invoke($null, $null)
}

function Wait-For([scriptblock]$predicate, [string]$failure) {
    for ($i=0; $i -lt 30; $i++) {
        if (& $predicate) { return }
        Start-Sleep -Milliseconds 500
    }
    throw $failure
}

function Invoke-Button([string]$label) {
    $manager = Get-Process 'Dual Spotify' | Where-Object { $_.Path -eq $launcher -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    $window = [Windows.Automation.AutomationElement]::FromHandle($manager.MainWindowHandle)
    $match = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $label)
    $matches = $window.FindAll([Windows.Automation.TreeScope]::Descendants, $match)
    $button = $matches | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button } | Select-Object -First 1
    Assert ($button -and $button.Current.IsEnabled) "$label is unavailable."
    $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

$mainBefore = (State)[0]
Assert ($mainBefore.Count -gt 0) 'Open the main Spotify client before running live checks.'
Start-Process -FilePath $launcher
Wait-For { (Get-Process 'Dual Spotify' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $launcher -and $_.MainWindowHandle -ne 0 }).Count -eq 1 } 'The dashboard did not open.'
Start-Process -FilePath $launcher -ArgumentList '--second' -Wait
Wait-For {
    $ids = (State)[1]
    $ids.Count -eq 1 -and (Get-Process -Id $ids[0]).MainWindowHandle -ne 0
} 'The direct command did not open Second Spotify.'
$secondBefore = (State)[1][0]
Start-Process -FilePath $launcher -ArgumentList '--second' -Wait
Assert (((State)[1]).Count -eq 1 -and (State)[1][0] -eq $secondBefore) 'Repeated direct launch opened a duplicate profile.'
Start-Process -FilePath $launcher -Wait
Start-Sleep -Seconds 3
Invoke-Button 'Quit Second Spotify'
Wait-For { ((State)[1]).Count -eq 0 } 'Quit did not close Second Spotify.'
Assert ((Compare-Object $mainBefore (State)[0]).Count -eq 0) 'The main client changed when Second Spotify quit.'
Wait-For {
    try { Invoke-Button 'Open Second Spotify'; return $true }
    catch { return $false }
} 'The second profile did not return to Open after quitting.'
Wait-For {
    $ids = (State)[1]
    $ids.Count -eq 1 -and (Get-Process -Id $ids[0]).MainWindowHandle -ne 0
} 'The dashboard did not reopen Second Spotify.'
$second = (State)[1][0]
Assert ($second -ne $secondBefore) 'The second process did not restart.'
Assert ((Compare-Object $mainBefore (State)[0]).Count -eq 0) 'The main client changed when Second Spotify reopened.'
'Live checks passed. The direct command opened one second window, repeated launch reused it, Quit closed it, and the dashboard reopened it while Main Spotify kept running.'
