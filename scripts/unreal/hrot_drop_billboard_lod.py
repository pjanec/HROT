"""hrot_drop_billboard_lod.py - CE-4651 (U-19h): drop the BILLBOARD LOD (the last one, drawn with the *_Billboard material) of given
static meshes - the user, live on Stechovice 2026-11-23: the grass is grey and "too quickly switches to lower LOD"; measured: each Megascans
grass mesh's last LOD is a billboard card with MI_<id>_Billboard, which draws as grey cards. Without it the grass keeps its real LODs out to
the level's grass cull distance and then ends.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_LOD_JOB = { "meshes": [paths], "out": <json> }

Only a mesh whose LAST material slot is a *_Billboard is touched (its LOD count reduced by one, the mesh saved). Third-party assets: commit
them with `git add -f` in the Unreal project.
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_LOD_JOB"], encoding="utf-8-sig"))
R = {"meshes": [], "log": []}
try:
    for p in JOB["meshes"]:
        m = unreal.load_asset(p)
        if not m: R["log"].append("missing " + p); continue
        mats = m.get_editor_property("static_materials")
        n = m.get_num_lods()
        last = str(mats[-1].material_slot_name) if mats else ""
        if n < 2 or not last.endswith("_Billboard"):
            R["meshes"].append({"mesh": p, "lods": n, "changed": False, "why": "no billboard LOD"}); continue
        m.set_num_source_models(n - 1)
        ok = unreal.EditorAssetLibrary.save_loaded_asset(m, False)
        R["meshes"].append({"mesh": p, "lods": f"{n} -> {m.get_num_lods()}", "changed": True, "saved": bool(ok)})
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
