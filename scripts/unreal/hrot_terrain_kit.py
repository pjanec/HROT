# CE-4651 (U-18w): the shared TERRAIN KIT - every /Game package that the terrain packages repeat (vegetation, ground materials, textures)
# is cooked into the BASE client, so a terrain package holds only its own content.
#   UnrealEditor-Cmd.exe <uproject> -ExecutePythonScript=<this> -unattended -nosplash -nullrhi
# Input: Plugins/HrotTerrain_*/Saved/Cooked/Windows/HrotUnreal/Plugins/HrotTerrain_*/Metadata/ReferencedSet.txt (the last terrain cooks)
# Excluded: terrain-specific /Game/Hrot/Buildings, /Game/Hrot/Roads, /Game/Maps, every /HrotTerrain_* package, anything outside /Game.
import unreal, os, glob

proj = unreal.SystemLibrary.get_project_directory()
sets = glob.glob(os.path.join(proj, "Plugins", "HrotTerrain_*", "Saved", "Cooked", "Windows", "HrotUnreal", "Plugins", "HrotTerrain_*", "Metadata", "ReferencedSet.txt"))
excl = ("/game/hrot/buildings/", "/game/hrot/roads/", "/game/maps/", "/game/hrot/terrainkit/")   # ReferencedSet.txt is lower case
union = set()
for f in sets:
    for line in open(f, encoding="utf-8", errors="ignore"):
        p = line.strip().lower()
        if p.startswith("/game/") and not p.startswith(excl):
            union.add(p)
unreal.log("HROT_KIT: %d referenced-set files, %d packages in the union" % (len(sets), len(union)))

reg = unreal.AssetRegistryHelpers.get_asset_registry()
by_pkg = {}
for ad in reg.get_assets_by_path("/Game", recursive=True):
    pn = str(ad.package_name)
    by_pkg[pn.lower()] = "%s.%s" % (pn, ad.asset_name)
refs = []
missing = []
for p in sorted(union):
    if p in by_pkg:
        refs.append(by_pkg[p])
    else:
        missing.append(p)
unreal.log("HROT_KIT: %d assets resolved, %d not found in the project" % (len(refs), len(missing)))

# The list of FOLDERS holding those assets goes to <project>/Config/HrotTerrainKitDirs.txt (committed in the Unreal project's git);
# hrot_package_client.ps1 adds each as a DirectoriesToAlwaysCook entry. It is a UNION with the existing file - the list only grows, because
# after the kit is in the base the terrain packages no longer reference these assets and a later run would see a smaller set.
# (Measured U-18w: a PrimaryAssetLabel with explicit_assets + AlwaysCook is scanned by the AssetManager but the cook does NOT pull its assets in.)
NL = chr(10)
dirs = set(r.rsplit("/", 1)[0] for r in refs)
dirs.discard("/Game/Hrot")   # loose files of /Game/Hrot stay in the terrain packages; the folder is recursive (Buildings, Roads ...)
out = os.path.join(proj, "Config", "HrotTerrainKitDirs.txt")
if os.path.exists(out):
    dirs |= set(l.strip() for l in open(out) if l.strip().startswith("/Game/"))
with open(out, "w") as fh:
    fh.write(NL.join(sorted(dirs)) + NL)
unreal.log("HROT_KIT: %d folders -> %s" % (len(dirs), out))
with open(os.path.join(proj, "Saved", "TerrainKit.txt"), "w") as fh:
    fh.write(NL.join(refs) + NL)
