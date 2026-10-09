param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Use a version such as 1.0.0.' }
& (Join-Path $PSScriptRoot 'Build.ps1')
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$stage = Join-Path $dist ('package-' + [guid]::NewGuid().ToString('N'))
$appFolder = Join-Path $stage 'Tiago Music'
New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
$files = @('Tiago Music.exe', 'Tiago Music.exe.config', 'Tiago Music.ico', 'Read me.txt', 'LICENSE')
foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $appFolder $name) }
$archive = Join-Path $dist ('Tiago-Music-Windows-v' + $Version + '.zip')
Compress-Archive -LiteralPath $appFolder -DestinationPath $archive -Force
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
($hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Output $archive
