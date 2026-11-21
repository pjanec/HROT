# CE-4651 (U-18s) - package the Unreal IG CLIENT once (Win64): the base app that later loads cooked terrain packages by name (U-18t).
#   powershell -File scripts/unreal/hrot_package_client.ps1 [-Release 1.0] [-Out D:\Work\HrotIG] [-Config Development]
# - makes the start level /Game/Maps/IgStart (scripts/unreal/hrot_ig_start_level.py) when it is missing
# - writes the packaging settings into the project's Config/DefaultGame.ini (a marked block, replaced on every run) and the game's start level
# - BuildCookRun: build, cook ONLY the start level + the shared folders the code loads by path (no terrain level: terrains are packages),
#   pak + IoStore, stage, archive to <Out>\<Release>, and -createreleaseversion=<Release> (the base a terrain package is cooked against)
param([string]$Release = "1.0", [string]$Out = "D:\Work\HrotIG", [string]$Config = "Development",
      [string]$Project = "D:\Work\HrotUnreal\HrotUnreal.uproject", [string]$Engine = "C:\UE_5.8\UE_5.8")
$ErrorActionPreference = "Stop"
if (Get-Process UnrealEditor -ErrorAction SilentlyContinue) { "EDITOR OPEN - close it first"; exit 1 }
$Proj = Split-Path $Project
$Here = Split-Path $MyInvocation.MyCommand.Path

# the start level
if (-not (Test-Path "$Proj\Content\Maps\IgStart.umap")) {
  "making /Game/Maps/IgStart"
  & "$Engine\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" $Project "-ExecutePythonScript=$Here\hrot_ig_start_level.py" -unattended -nosplash -nullrhi | Out-Null
  if (-not (Test-Path "$Proj\Content\Maps\IgStart.umap")) { "IgStart was not written"; exit 2 }
}

# the packaging settings (a marked block in DefaultGame.ini, replaced each run)
$always = "/Game/Hrot/Vehicles", "/Game/Hrot/People", "/Game/Hrot/Air", "/Game/Hrot/Dragon", "/Game/Hrot/Paint", "/Game/Hrot/Missiles",
          "/Game/Hrot/Animals", "/Game/Hrot/Fx", "/Game/Hrot/Audio", "/Game/Hrot/AudioPro",
          # Lyra: ONLY the subfolders of the assets the code loads by path (its whole /Game/Audio and /Game/Characters/Heroes hold gameplay
          # Blueprints needing LyraGame, MotionWarping, GameplayMessageRuntime - not in this project: measured, the first cook failed on 203 errors there)
          "/Game/Audio/AttenuationPresets", "/Game/Audio/Concurrency", "/Game/Audio/MetaSounds", "/Game/Audio/Sounds/Explosions", "/Game/Audio/Sounds/Weapons/Rifle",
          "/Game/Effects/Particles/Impacts", "/Game/Effects/Particles/Weapons", "/Game/Realistic_Starter_VFX_Pack_Vol2/Particles"
          # (not Lyra's Mannequin folders: its rifle anim-layer Blueprints need LyraGame - the person kits under /Game/Hrot/People carry the people)
$block = @("; >>> HROT IG packaging (scripts/unreal/hrot_package_client.ps1 writes this block; edits inside it are lost)",
  "[/Script/UnrealEd.ProjectPackagingSettings]", "UsePakFile=True", "bUseIoStore=True", "bShareMaterialShaderCode=True", "bCookAll=False",
  "+MapsToCook=(FilePath=`"/Game/Maps/IgStart`")") + ($always | % { "+DirectoriesToAlwaysCook=(Path=`"$_`")" }) + @("; <<< HROT IG packaging")
$gi = "$Proj\Config\DefaultGame.ini"
$text = if (Test-Path $gi) { [IO.File]::ReadAllText($gi) } else { "" }
$text = [regex]::Replace($text, "(?s); >>> HROT IG packaging.*?; <<< HROT IG packaging\r?\n?", "")
[IO.File]::WriteAllText($gi, $text.TrimEnd() + "`r`n" + ($block -join "`r`n") + "`r`n", [Text.UTF8Encoding]::new($false))
$ei = "$Proj\Config\DefaultEngine.ini"
$e = [IO.File]::ReadAllText($ei)
$e = [regex]::Replace($e, "(?m)^GameDefaultMap=.*$", "GameDefaultMap=/Game/Maps/IgStart")
[IO.File]::WriteAllText($ei, $e, [Text.UTF8Encoding]::new($false))

# U-18t: the terrain packages' plugins (Plugins\HrotTerrain_*) are set ASIDE while the base is packaged - a terrain must never land in the base
$aside = "$Proj\Saved\TerrainPluginsAside"
$moved = @(Get-ChildItem "$Proj\Plugins" -Directory -Filter "HrotTerrain_*" -ErrorAction SilentlyContinue)
if ($moved) { New-Item -ItemType Directory -Force $aside | Out-Null; $moved | % { Move-Item $_.FullName (Join-Path $aside $_.Name) -Force; "aside: $($_.Name)" } }
try {
  # build, cook, pak, stage, archive; the release version the terrain packages are cooked against
  $archive = Join-Path $Out $Release
  & "$Engine\Engine\Build\BatchFiles\RunUAT.bat" BuildCookRun "-project=$Project" -platform=Win64 "-clientconfig=$Config" -build -cook -stage -pak -iostore `
    -archive "-archivedirectory=$archive" -map=/Game/Maps/IgStart "-createreleaseversion=$Release" -unattended -utf8output -nop4 -nocompileeditor
  $code = $LASTEXITCODE
  "BuildCookRun exit $code; archive $archive; release $Proj\Releases\$Release"
} finally {
  $moved | % { Move-Item (Join-Path $aside $_.Name) $_.FullName -Force; "back: $($_.Name)" }
}
"note: terrain packages cooked against an older build of release $Release must be cooked again (hrot_package_terrain.ps1)"
exit $code
