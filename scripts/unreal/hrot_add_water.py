"""hrot_add_water.py - CE-4651 (U-19h): lay a terrain's water tiles (scripts/unreal/hrot_water_mesh.py) into its EXISTING look level.

    UnrealEditor.exe <project> -ExecutePythonScript=<this> -unattended -nosplash
        HROT_WATER_JOB = { "level": "/Game/Maps/Stechovice_look", "export": <terrain-to-unreal export folder>, "terrain": "Stechovice", "out": <json> }

Earlier Water_* actors of the level are removed; each tile becomes /Game/Hrot/Water/SM_<terrain>_Water_<tx>_<ty> at its origin with the
terrain's own water material (M_HrotWater_<terrain>: a dark green-blue, glossy, two-sided - the sheet is drawn from both sides), then the
level and the new assets are saved. Nothing else in the level is touched.
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_WATER_JOB"], encoding="utf-8-sig"))
R = {"log": []}
MEL = unreal.MaterialEditingLibrary
ASSETS = unreal.AssetToolsHelpers.get_asset_tools()


def log(m):
    unreal.log_warning(f"[HROT-WATER] {m}"); R["log"].append(m)


def water_material(t):
    name = f"M_HrotWater_{t}"; path = f"/Game/Hrot/{name}"
    if unreal.EditorAssetLibrary.does_asset_exist(path): return unreal.load_asset(path)
    m = ASSETS.create_asset(name, "/Game/Hrot", unreal.Material, unreal.MaterialFactoryNew())
    E = lambda cls, x, y: MEL.create_material_expression(m, cls, x, y)
    c = E(unreal.MaterialExpressionConstant3Vector, -400, 0); c.set_editor_property("constant", unreal.LinearColor(0.035, 0.065, 0.06, 1.0))
    MEL.connect_material_property(c, "", unreal.MaterialProperty.MP_BASE_COLOR)
    r = E(unreal.MaterialExpressionConstant, -400, 150); r.set_editor_property("r", 0.06)
    MEL.connect_material_property(r, "", unreal.MaterialProperty.MP_ROUGHNESS)
    s = E(unreal.MaterialExpressionConstant, -400, 250); s.set_editor_property("r", 0.8)
    MEL.connect_material_property(s, "", unreal.MaterialProperty.MP_SPECULAR)
    m.set_editor_property("two_sided", True)
    MEL.recompile_material(m)
    return m


def main():
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not les.load_level(JOB["level"]): raise RuntimeError("load_level failed: " + JOB["level"])
    eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    old = [a for a in eas.get_all_level_actors() if a.get_actor_label().startswith("Water_")]
    for a in old: eas.destroy_actor(a)
    log(f"removed {len(old)} earlier Water_* actors")
    t = JOB["terrain"].replace("-", "_")
    mat = water_material(t)
    spec = json.load(open(os.path.join(JOB["export"], f"{JOB['terrain']}.water-mesh.json"), encoding="utf-8"))
    n, tris = 0, 0
    for tile in spec["tiles"]:
        a = unreal.HrotTerrainLibrary.build_mesh(os.path.join(JOB["export"], tile["file"]).replace("\\", "/"),
                                                 f"/Game/Hrot/Water/SM_{t}_Water_{tile['tile'][0]}_{tile['tile'][1]}",
                                                 [mat], unreal.Vector(*tile["locationCm"]), f"Water_{tile['tile'][0]}_{tile['tile'][1]}")
        if a:
            n += 1; tris += tile["triangles"]
            for c in a.get_components_by_class(unreal.StaticMeshComponent): c.set_collision_enabled(unreal.CollisionEnabled.NO_COLLISION)
    log(f"{n} of {len(spec['tiles'])} water tiles placed, {tris} triangles, {spec['cells']} cells of {spec['cell']} m, {spec['polygons']} polygons")
    les.save_current_level()
    ok = unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    log("saved" if ok else "some packages FAILED to save")


try:
    main()
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
unreal.SystemLibrary.quit_editor()
