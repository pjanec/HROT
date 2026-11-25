"""hrot_material_usage.py - set the usage flags a material needs in the GAME and save it (a -game run or a packaged build cannot compile a missing
usage at run time: it draws the default grey material instead - "missing usage flag ... Default Material will be used in game").

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_MATERIAL_USAGE = { "materials": { "<asset path>": ["<flag>", ...] },
                                                                                                    "meshes": ["<static mesh path>", ...], "out" }
A material instance's flags live on its ROOT material: the flags are set there. Flags are unreal.MaterialUsage names (e.g. MATUSAGE_INSTANCED_STATIC_MESHES,
MATUSAGE_NANITE). "meshes" only reports each mesh's height (to judge a scatter's scale). LFS assets come back READ-ONLY: clear the flag before running.
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_MATERIAL_USAGE"], encoding="utf-8-sig"))
R = {"materials": {}, "meshes": {}, "log": []}


def root_of(m):
    while isinstance(m, unreal.MaterialInstance):
        p = m.get_editor_property("parent")
        if not p: break
        m = p
    return m


try:
    for path, flags in JOB.get("materials", {}).items():
        m = unreal.load_asset(path)
        if not m: R["log"].append("missing " + path); continue
        root = root_of(m)
        done = []
        for f in flags:
            usage = getattr(unreal.MaterialUsage, f)
            needs = [False]
            ok = unreal.MaterialEditingLibrary.set_material_usage(root, usage, needs) if hasattr(unreal.MaterialEditingLibrary, "set_material_usage") else None
            if ok is None:   # older API: the bool property on the material
                prop = {"MATUSAGE_INSTANCED_STATIC_MESHES": "used_with_instanced_static_meshes", "MATUSAGE_NANITE": "used_with_nanite"}[f]
                root.set_editor_property(prop, True); ok = True
            done.append(f"{f}={ok}")
        saved = bool(unreal.EditorAssetLibrary.save_loaded_asset(root, only_if_is_dirty=False))
        R["materials"][path] = {"root": root.get_path_name(), "set": done, "saved": saved}
    for path in JOB.get("meshes", []):
        sm = unreal.load_asset(path)
        if not sm: R["meshes"][path] = "missing"; continue
        b = sm.get_bounding_box()
        R["meshes"][path] = {"height_m": round((b.max.z - b.min.z) / 100.0, 2), "width_m": round((b.max.x - b.min.x) / 100.0, 2)}
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1, default=str)
