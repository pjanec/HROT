"""hrot_add_bridges.py - CE-11063: lay a terrain's bridge decks (scripts/unreal/hrot_bridge_mesh.py) into its EXISTING look level.

    UnrealEditor.exe <project> -ExecutePythonScript=<this> -unattended -nosplash
        HROT_BRIDGE_JOB = { "level": "/Game/Maps/Stechovice_look", "export": <terrain-to-unreal export folder>, "terrain": "Stechovice", "out": <json> }

Earlier Bridge_* actors of the level are removed; each bridge becomes /Game/Hrot/Bridges/SM_<terrain>_Bridge_<edge> at its origin (label
Bridge_<edge>): slot 0 the deck's top (dark asphalt), slot 1 its sides and underside (concrete) - two-sided, flat placeholders until a bridge
material set is chosen. Then the level and the new assets are saved. Nothing else in the level is touched. hrot_look.py's step_bridges lays the
same tiles when a look level is built from scratch (bridges_into).
"""
import json, os, traceback
import unreal

R = {"log": []}
MEL = unreal.MaterialEditingLibrary
ASSETS = unreal.AssetToolsHelpers.get_asset_tools()


def log(m):
    unreal.log_warning(f"[HROT-BRIDGES] {m}"); R["log"].append(m)


def flat(name, rgb, rough):
    path = f"/Game/Hrot/{name}"
    if unreal.EditorAssetLibrary.does_asset_exist(path): return unreal.load_asset(path)
    m = ASSETS.create_asset(name, "/Game/Hrot", unreal.Material, unreal.MaterialFactoryNew())
    c = MEL.create_material_expression(m, unreal.MaterialExpressionConstant3Vector, -400, 0); c.set_editor_property("constant", unreal.LinearColor(*rgb, 1.0))
    MEL.connect_material_property(c, "", unreal.MaterialProperty.MP_BASE_COLOR)
    r = MEL.create_material_expression(m, unreal.MaterialExpressionConstant, -400, 150); r.set_editor_property("r", rough)
    MEL.connect_material_property(r, "", unreal.MaterialProperty.MP_ROUGHNESS)
    m.set_editor_property("two_sided", True)
    MEL.recompile_material(m)
    return m


def bridges_into(export, terrain, logf=log):
    """The bridge tiles of <export>/<terrain>.bridges-mesh.json into the CURRENT level (earlier Bridge_* actors removed). Returns the count."""
    fn = os.path.join(export, f"{terrain}.bridges-mesh.json")
    if not os.path.exists(fn): logf("no bridges-mesh.json - no bridges"); return 0
    eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    old = [a for a in eas.get_all_level_actors() if a.get_actor_label().startswith("Bridge_")]
    for a in old: eas.destroy_actor(a)
    t = terrain.replace("-", "_")
    mats = [flat("M_HrotBridgeDeck", (0.055, 0.055, 0.055), 0.8), flat("M_HrotBridgeConcrete", (0.42, 0.41, 0.39), 0.9)]
    spec = json.load(open(fn, encoding="utf-8"))
    n = 0
    for b in spec["bridges"]:
        edge = b["edge"].replace("-", "m")
        a = unreal.HrotTerrainLibrary.build_mesh(os.path.join(export, b["file"]).replace("\\", "/"), f"/Game/Hrot/Bridges/SM_{t}_Bridge_{edge}",
                                                 mats, unreal.Vector(*b["locationCm"]), f"Bridge_{edge}")
        if a: n += 1
        logf(f"{b['structure']}: {'placed' if a else 'FAILED'} - {b['slabs']} slabs, {b['ramps']} ramps, {b['aboveGround'][0]}..{b['aboveGround'][1]} m above the ground")
    logf(f"bridges: {n} of {len(spec['bridges'])} placed (removed {len(old)} earlier Bridge_* actors)")
    return n


def main():
    JOB = json.load(open(os.environ["HROT_BRIDGE_JOB"], encoding="utf-8-sig"))
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not les.load_level(JOB["level"]): raise RuntimeError("load_level failed: " + JOB["level"])
    bridges_into(JOB["export"], JOB["terrain"])
    les.save_current_level()
    ok = unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    log("saved" if ok else "some packages FAILED to save")
    return JOB


if os.environ.get("HROT_BRIDGE_JOB"):   # run as the editor script (hrot_look.py imports bridges_into without it)
    job = None
    try:
        job = main()
    except Exception:
        R["log"].append(traceback.format_exc())
    out = (job or json.load(open(os.environ["HROT_BRIDGE_JOB"], encoding="utf-8-sig")))["out"]
    json.dump(R, open(out, "w", encoding="utf-8"), indent=1)
    unreal.SystemLibrary.quit_editor()
