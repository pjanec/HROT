# Installs SUMO (netconvert) at the version HROT follows — tools/sumo/version.txt (the vendored SumoSharp's, 1.20.0) — into the repo's
# gitignored .local\sumo\<version>\, from the official Windows release (sumo-win64-<version>.zip, sumo.dlr.de). Terrain From OSM §9.1;
# found by the shared locator (Hrot.Terrain.InputSet.Netconvert). Idempotent. Nothing SUMO-made is committed.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Windows PowerShell 5.1 redraws its progress bar per chunk — the ~100 MB download crawled for 15+ min
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root 'tools\sumo\version.txt') -Raw).Trim()
$dest = Join-Path $root ".local\sumo\$version"
$nc = Join-Path $dest 'bin\netconvert.exe'
if ((Test-Path $nc) -and ((& $nc --version | Select-Object -First 1) -match "Version $version")) { Write-Host "SUMO $version already installed: $nc"; exit 0 }
$url = "https://sumo.dlr.de/releases/$version/sumo-win64-$version.zip"
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("sumo-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
  $zip = Join-Path $tmp 'sumo.zip'
  Write-Host "downloading $url"
  Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
  Expand-Archive -Path $zip -DestinationPath $tmp
  $inner = Get-ChildItem -Path $tmp -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'bin\netconvert.exe') } | Select-Object -First 1
  if (-not $inner) { throw "the archive has no bin\netconvert.exe" }
  if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
  New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
  Move-Item -Path $inner.FullName -Destination $dest
} finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
& $nc --version | Select-Object -First 1
Write-Host "installed: $nc"
