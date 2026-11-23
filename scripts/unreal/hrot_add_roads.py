"""hrot_add_roads.py - CE-4651 (U-19h): lay HROT's road scene into an EXISTING look level (only the roads; nothing else is rebuilt).

    UnrealEditor.exe <project> -ExecutePythonScript=<this> -unattended -nosplash
        HROT_ROADS_JOB = { "level": "/Game/Maps/demo_city_look", "meshes": <hrot-roadscene out folder>, "terrain": "demo-city", "out": <json> }

The meshes are hrot-roadscene's tiles (HROT's own TrafficRoadScene laid on our ground, Hrot/Tools/Hrot.Tools.RoadScene) - the same as
hrot_look.py's step_roads (UL-14) builds for a NEW level. Here the level exists (demo-city was built before UL-14, the user: "so many sumo
cars, but there are no roads rendered at all"), so: earlier Road_* actors of this level are removed, each tile becomes
/Game/Hrot/Roads/SM_<terrain>_<kind>_<tx>_<ty> placed at its origin, and the level and the new assets are saved.
Materials: this terrain's own flat ones (M_HrotAsphalt_<terrain>, M_HrotSidewalk_<terrain>, M_HrotRoadPaint_<terrain>) - ⛔ never the
shared M_HrotRoad, whose road mask is another terrain's (Stechovice's level uses it).
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_ROADS_JOB"], encoding="utf-8-sig"))
R = {"log": []}
MEL = unreal.MaterialEditingLibrary
ASSETS = unreal.AssetToolsHelpers.get_asset_tools()


def log(m):
    unreal.log_warning(f"[HROT-ROADS] {m}"); R["log"].append(m)


def flat(name, rgb, rough):
    path = f"/Game/Hrot/{name}"
    if unreal.EditorAssetLibrary.does_asset_exist(path): return unreal.load_asset(path)
    m = ASSETS.create_asset(name, "/Game/Hrot", unreal.Material, unreal.MaterialFactoryNew())
    c = MEL.create_material_expression(m, unreal.MaterialExpressionConstant3Vector, -300, 0)
    c.set_editor_property("constant", unreal.LinearColor(*rgb, 1.0))
    MEL.connect_material_property(c, "", unreal.MaterialProperty.MP_BASE_COLOR)
    r = MEL.create_material_expression(m, unreal.MaterialExpressionConstant, -300, 120); r.set_editor_property("r", rough)
    MEL.connect_material_property(r, "", unreal.MaterialProperty.MP_ROUGHNESS)
    m.set_editor_property("two_sided", True)
    MEL.recompile_material(m)
    return m


def main():
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not les.load_level(JOB["level"]): raise RuntimeError("load_level failed: " + JOB["level"])
    eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    old = [a for a in eas.get_all_level_actors() if a.get_actor_label().startswith("Road_")]
    for a in old: eas.destroy_actor(a)
    log(f"removed {len(old)} earlier Road_* actors")
    t = JOB["terrain"].replace("-", "_")
    asphalt = flat(f"M_HrotAsphalt_{t}", (0.045, 0.045, 0.048), 0.85)
    walk = flat(f"M_HrotSidewalk_{t}", (0.32, 0.31, 0.29), 0.9)
    paint = flat(f"M_HrotRoadPaint_{t}", (0.82, 0.82, 0.80), 0.55)
    by_kind = {"Asphalt": asphalt, "Junction": asphalt, "Sidewalk": walk, "LaneMarking": paint, "Crosswalk": paint}
    spec = json.load(open(os.path.join(JOB["meshes"], f"{JOB['terrain']}.roads-mesh.json"), encoding="utf-8"))
    n, tris = 0, 0
    for tile in spec["tiles"]:
        a = unreal.HrotTerrainLibrary.build_mesh(os.path.join(JOB["meshes"], tile["file"]).replace("\\", "/"),
                                                 f"/Game/Hrot/Roads/SM_{t}_{tile['kind']}_{tile['tile'][0]}_{tile['tile'][1]}",
                                                 [by_kind.get(tile["kind"], asphalt)], unreal.Vector(*tile["locationCm"]),
                                                 f"Road_{tile['kind']}_{tile['tile'][0]}_{tile['tile'][1]}")
        if a: n += 1; tris += tile["triangles"]
    log(f"{n} of {len(spec['tiles'])} tiles placed, {tris} triangles ({', '.join(f'{k} {v}' for k, v in spec['triangles'].items())})")
    les.save_current_level()
    ok = unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    log("saved" if ok else "some packages FAILED to save")
    R["tiles"], R["placed"] = len(spec["tiles"]), n


try:
    main()
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
unreal.SystemLibrary.quit_editor()
