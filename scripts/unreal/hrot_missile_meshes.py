"""hrot_missile_meshes.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17g): the missiles' meshes, cut out of the packs.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_MISSILE_MESH_JOB = { "inventory": [<mesh>],
                                                                                 "cuts": [{"from", "keep": <regex over slot names>, "to"}], "out" }
    inventory   each mesh's slots, triangles and bounds (which axis is long)
    cuts        a NEW static mesh holding only the triangles of the slots whose name matches `keep`, centred on its bounds (the RPG-7's
                rocket out of the loaded launcher, the Strela's missile out of its tube) — Geometry Scripting; the slots keep their materials
"""
import json, os, re, traceback
import unreal

JOB = json.load(open(os.environ["HROT_MISSILE_MESH_JOB"], encoding="utf-8"))
R = {"log": [], "meshes": {}}
GS = unreal


def log(m):
    unreal.log_warning(f"[HROT-MISSILE] {m}"); R["log"].append(m)


def info(path):
    sm = unreal.load_asset(path)
    b = sm.get_bounds()
    slots = [(str(s.get_editor_property("material_slot_name")), s.get_editor_property("material_interface").get_name() if s.get_editor_property("material_interface") else None)
             for s in sm.get_editor_property("static_materials")]
    e = b.box_extent
    d = {"tris": sm.get_num_triangles(0), "origin": [round(b.origin.x, 1), round(b.origin.y, 1), round(b.origin.z, 1)],
         "size": [round(2 * e.x, 1), round(2 * e.y, 1), round(2 * e.z, 1)], "slots": slots}
    R["meshes"][path] = d
    log(f"{path}: {d['tris']} tris, size {d['size']}, origin {d['origin']}, slots {slots}")
    return sm


def cut(c):
    sm = info(c["from"])
    keep = re.compile(c["keep"], re.I)
    dm = unreal.DynamicMesh()
    opts = unreal.GeometryScriptCopyMeshFromAssetOptions()
    lod = unreal.GeometryScriptMeshReadLOD()
    res = unreal.GeometryScript_AssetUtils.copy_mesh_from_static_mesh(sm, dm, opts, lod)
    dm = res[0] if isinstance(res, tuple) else dm
    slots = list(sm.get_editor_property("static_materials"))
    dropped = 0
    for i, s in enumerate(slots):
        if keep.search(str(s.get_editor_property("material_slot_name"))): continue
        r = unreal.GeometryScript_Materials.delete_triangles_by_material_id(dm, i)
        n = r[1] if isinstance(r, tuple) else r
        log(f"  slot {i} {s.get_editor_property('material_slot_name')}: deleted {n}"); dropped += 1
    box = unreal.GeometryScript_MeshQueries.get_mesh_bounding_box(dm)
    centre = (box.min + box.max) * 0.5
    unreal.GeometryScript_MeshTransforms.translate_mesh(dm, centre * -1.0)
    folder, name = c["to"].rsplit("/", 1)
    if unreal.EditorAssetLibrary.does_asset_exist(c["to"]): unreal.EditorAssetLibrary.delete_asset(c["to"])
    copts = unreal.GeometryScriptCreateNewStaticMeshAssetOptions()
    res = unreal.GeometryScript_NewAssetUtils.create_new_static_mesh_asset_from_mesh(dm, c["to"], copts)
    new = res[0] if isinstance(res, tuple) else res
    if not isinstance(new, unreal.StaticMesh): new = unreal.load_asset(c["to"])
    if new is None: raise RuntimeError("could not create " + c["to"])
    mats = list(new.get_editor_property("static_materials"))
    for i, s in enumerate(mats):   # the cut keeps its material ids: put the source's materials back on them
        if i < len(slots):
            s.set_editor_property("material_interface", slots[i].get_editor_property("material_interface"))
            s.set_editor_property("material_slot_name", slots[i].get_editor_property("material_slot_name")); mats[i] = s
    new.set_editor_property("static_materials", mats)
    unreal.EditorAssetLibrary.save_loaded_asset(new, False)
    log(f"cut {c['from']} -> {c['to']} (dropped {dropped} slots, centred from {centre})")
    info(c["to"])


try:
    for p in JOB.get("inventory", []): info(p)
    for c in JOB.get("cuts", []): cut(c)
    R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
