$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$fixture = [IO.Path]::Combine($project,'Verification','Discovery-' + [Guid]::NewGuid().ToString('N'))
$launcher = [IO.Path]::Combine($project,'Dual Spotify.exe')
$assembly = [Reflection.Assembly]::LoadFile($launcher)
$discover = $assembly.GetType('SteamWindowsProfile',$true)
$location = [IO.Path]::Combine($fixture,'Other downloads','Dual-Steam-Windows','Dual Steam')
$elsewhere = [IO.Path]::Combine($fixture,'Some other drive layout','Portable apps')
$null = [IO.Directory]::CreateDirectory($location)
$null = [IO.Directory]::CreateDirectory($elsewhere)
$steamExe = [IO.Path]::Combine($elsewhere,'Dual Steam.exe')
$notSteamExe = [IO.Path]::Combine($elsewhere,'Another.exe')
[IO.File]::WriteAllText($steamExe,'fixture')
[IO.File]::WriteAllText($notSteamExe,'fixture')
$found = @($discover.GetMethod('CandidateHomes').Invoke($null,[object[]]@(,[string[]]@([IO.Path]::Combine($fixture,'Other downloads')))))
if ($location -notin $found) { throw 'A nested extracted Dual Steam folder in Downloads was missed.' }
$shell = New-Object -ComObject WScript.Shell
try {
    foreach($item in @(@('Dual Steam.lnk',$steamExe,$elsewhere), @('Second Steam.lnk',$notSteamExe,$null))) {
        $path = [IO.Path]::Combine($fixture,$item[0])
        $lnk = $shell.CreateShortcut($path)
        try { $lnk.TargetPath = $item[1]; $lnk.Save() }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($lnk) }
        $result = $discover.GetMethod('ShortcutHome').Invoke($null,[object[]]@([string]$path))
        if ($result -ne $item[2]) { throw 'Dual Steam shortcut discovery accepted an invalid target or missed a valid one.' }
    }
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
'Verified discovery of nested Downloads and a relocated Dual Steam launcher via shortcut. Unrelated shortcuts were rejected.'
