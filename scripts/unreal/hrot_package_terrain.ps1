# CE-4651 (U-18t) - cook ONE terrain as its own package for the packaged IG client (no client rebuild):
#   powershell -File scripts/unreal/hrot_package_terrain.ps1 -Terrain farm [-Source /Game/Maps/farm_look] [-Release 1.0] [-Client D:\Work\HrotIG\1.0]
# 1. the content plugin HrotTerrain_<name> in the project's Plugins (its descriptor; mount point /HrotTerrain_<name>/)
# 2. the terrain's level copied into it as /HrotTerrain_<name>/Maps/<name> (scripts/unreal/hrot_terrain_into_package.py)
# 3. BuildCookRun as DLC against the client's release: -dlcname -basedonreleaseversion -> only what the release lacks is cooked in
# 4. the package (pak + IoStore utoc/ucas) copied into <Client>\Windows\HrotUnreal\Terrains\<name>\ - the client mounts it by name
param([Parameter(Mandatory = $true)][string]$Terrain, [string]$Source = "", [string]$Release = "1.0", [string]$Client = "D:\Work\HrotIG\1.0",
      [string]$Project = "D:\Work\HrotUnreal\HrotUnreal.uproject", [string]$Engine = "C:\UE_5.8\UE_5.8")
$ErrorActionPreference = "Stop"
if (Get-Process UnrealEditor -ErrorAction SilentlyContinue) { "EDITOR OPEN - close it first"; exit 1 }
$Proj = Split-Path $Project
$Here = Split-Path $MyInvocation.MyCommand.Path
$N = $Terrain.Replace("-", "_")
$Plugin = "HrotTerrain_$N"
if (-not $Source) { $Source = if (Test-Path "$Proj\Content\Maps\${N}_look.umap") { "/Game/Maps/${N}_look" } else { "/Game/Maps/$N" } }
if (-not (Test-Path "$Proj\Releases\$Release")) { "no release $Release in $Proj\Releases - package the client first (hrot_package_client.ps1)"; exit 2 }

# 1. the plugin
$pd = "$Proj\Plugins\$Plugin"
New-Item -ItemType Directory -Force "$pd\Content\Maps" | Out-Null
@{ FileVersion = 3; Version = 1; VersionName = "1"; FriendlyName = "HROT terrain $Terrain"; Category = "HROT Terrain"
   Description = "The HROT terrain '$Terrain' as its own package for the IG client (scripts/unreal/hrot_package_terrain.ps1)."
   CanContainContent = $true; EnabledByDefault = $true } | ConvertTo-Json | Set-Content -Encoding utf8 "$pd\$Plugin.uplugin"

# 2. the level into it
$env:HROT_TERRAIN_NAME = $Terrain; $env:HROT_TERRAIN_SOURCE = $Source
& "$Engine\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" $Project "-ExecutePythonScript=$Here\hrot_terrain_into_package.py" -unattended -nosplash -nullrhi | Out-Null
if (-not (Test-Path "$pd\Content\Maps\$N.umap")) { "the level was not copied into $pd\Content\Maps\$N.umap"; exit 3 }
"level: $Source -> /$Plugin/Maps/$N"

# 3. the DLC cook against the client's release
& "$Engine\Engine\Build\BatchFiles\RunUAT.bat" BuildCookRun "-project=$Project" -platform=Win64 -clientconfig=Development -cook -stage -pak -iostore `
  "-dlcname=$Plugin" "-basedonreleaseversion=$Release" -DLCIncludeEngineContent "-map=/$Plugin/Maps/$N" -skipbuild -nocompileeditor -unattended -utf8output -nop4
if ($LASTEXITCODE -ne 0) { "BuildCookRun failed: $LASTEXITCODE"; exit $LASTEXITCODE }

# 4. into the client's Terrains folder
$out = "$Client\Windows\HrotUnreal\Terrains\$N"
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force $out | Out-Null
$files = Get-ChildItem -Recurse "$pd\Saved\StagedBuilds" -File | Where-Object { $_.Extension -in ".pak", ".utoc", ".ucas", ".sig" }
if (-not $files) { "no package files under $pd\Saved\StagedBuilds"; exit 4 }
$files | % { Copy-Item $_.FullName $out; "{0,9:N1} MB  {1}" -f ($_.Length / 1MB), $_.Name }
"terrain package '$Terrain' -> $out"
