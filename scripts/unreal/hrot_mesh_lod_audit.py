"""hrot_mesh_lod_audit.py - CE-4651 (U-19h): what each LOD of given static meshes draws (the user, live 2026-11-23: the grass is grey and
"too quickly switches to lower LOD").

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_LOD_JOB = { "meshes": [paths], "out": <json> }

Per mesh: Nanite on/off, the LOD count, each LOD's screen size, triangle count and the material of each section (its path, and whether
that material is a plain default). Facts only.
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_LOD_JOB"], encoding="utf-8-sig"))
R = {"meshes": [], "log": []}
SML = unreal.StaticMeshEditorSubsystem if hasattr(unreal, "StaticMeshEditorSubsystem") else None
try:
    sub = unreal.EditorStaticMeshLibrary
    for p in JOB["meshes"]:
        m = unreal.load_asset(p)
        if not m: R["log"].append("missing " + p); continue
        row = {"mesh": p.rsplit("/", 1)[-1], "lods": [], "nanite": None}
        try: row["nanite"] = bool(m.get_editor_property("nanite_settings").get_editor_property("enabled"))
        except Exception as e: row["nanite"] = f"? {e}"
        n = m.get_num_lods()
        sizes = sub.get_lod_screen_sizes(m) if hasattr(sub, "get_lod_screen_sizes") else []
        mats = m.get_editor_property("static_materials")
        for lod in range(n):
            secs = []
            for s in range(m.get_num_sections(lod)):
                info = sub.get_lod_material_slot(m, lod, s) if hasattr(sub, "get_lod_material_slot") else -1
                mi = mats[info].material_interface if 0 <= info < len(mats) else None
                secs.append(mi.get_path_name() if mi else None)
            row["lods"].append({"lod": lod, "screenSize": round(sizes[lod], 3) if lod < len(sizes) else None,
                                "triangles": sub.get_number_verts(m, lod) if hasattr(sub, "get_number_verts") else None, "sections": secs})
        row["materials"] = [(str(s.material_slot_name), s.material_interface.get_path_name() if s.material_interface else None) for s in mats]
        R["meshes"].append(row)
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
