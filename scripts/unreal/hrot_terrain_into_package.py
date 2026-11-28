"""hrot_terrain_into_package.py - CE-4651 (U-18t): put a terrain's level into its terrain PACKAGE plugin (run in the editor: -ExecutePythonScript).

    HROT_TERRAIN_NAME   the HROT terrain name ("farm", "demo-city")
    HROT_TERRAIN_SOURCE the level that draws it now ("/Game/Maps/farm_look")

The plugin HrotTerrain_<name> (its descriptor written by scripts/unreal/hrot_package_terrain.ps1 before the editor starts) mounts
/HrotTerrain_<name>/; the level is COPIED to /HrotTerrain_<name>/Maps/<name> (the source stays). Its other assets need no copy: the DLC cook
puts everything the base release lacks into the package.
"""
import os
import unreal

name = os.environ["HROT_TERRAIN_NAME"].replace("-", "_")
src = os.environ["HROT_TERRAIN_SOURCE"]
dst = f"/HrotTerrain_{name}/Maps/{name}"
if not unreal.EditorAssetLibrary.does_asset_exist(src):
    raise RuntimeError("no level " + src)
if unreal.EditorAssetLibrary.does_asset_exist(dst):
    unreal.EditorAssetLibrary.delete_asset(dst)
if not unreal.EditorAssetLibrary.duplicate_asset(src, dst):
    raise RuntimeError(f"could not copy {src} -> {dst}")
unreal.EditorAssetLibrary.save_asset(dst, only_if_is_dirty=False)
unreal.log_warning(f"[HROT-TERRAIN] {src} -> {dst}")
unreal.SystemLibrary.quit_editor()
