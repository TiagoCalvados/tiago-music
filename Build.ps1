$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$wpf = Join-Path $framework 'WPF'
$references = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xaml.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'System.Speech.dll') | ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', ('/out:' + (Join-Path $PSScriptRoot 'Tiago Music.exe')), ('/resource:' + (Join-Path $PSScriptRoot 'Player.xaml') + ',Player.xaml'))
if (Test-Path (Join-Path $PSScriptRoot 'Tiago Music.ico')) { $arguments += '/win32icon:' + (Join-Path $PSScriptRoot 'Tiago Music.ico') }
$arguments += $references
$arguments += @((Join-Path $PSScriptRoot 'App.cs'), (Join-Path $PSScriptRoot 'Lyrics.cs'), (Join-Path $PSScriptRoot 'Tests.cs'))
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Tiago Music build failed.' }
Write-Output 'Built Tiago Music.exe'
