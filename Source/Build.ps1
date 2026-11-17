$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName
& "$framework\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /main:App `
    "/win32manifest:$project\Assets\app.manifest" "/win32icon:$project\Assets\dual-spotify-green.ico" `
    "/out:$project\Dual Spotify.exe" /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Management.dll /reference:System.Windows.Forms.dll /reference:Microsoft.CSharp.dll `
    /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" `
    "/reference:$wpf\PresentationFramework.dll" "/reference:$framework\System.Xaml.dll" $sources
if ($LASTEXITCODE -ne 0) { throw 'Dual Spotify build failed.' }

& "$framework\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /main:DualSpotify.UpdaterProgram `
    "/out:$project\Dual Spotify Updater.exe" /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll "$PSScriptRoot\UpdateCore.cs" "$PSScriptRoot\UpdateService.cs" "$PSScriptRoot\UpdaterMain.cs"
if ($LASTEXITCODE -ne 0) { throw 'Dual Spotify updater build failed.' }

'<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>' |
    Set-Content -LiteralPath "$project\Dual Spotify.exe.config" -Encoding utf8
