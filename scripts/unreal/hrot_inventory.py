"""hrot_inventory.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17): what a set of content folders holds, measured.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_INVENTORY_JOB = { "folders": [...], "out" }

Per mesh: class, LOD0 triangles, LOD count, size (m), material slots; a skeletal mesh also its bones; a static mesh its sockets.
Per particle system (Cascade P_* / Niagara NS_*): the class. ⭐ The answer to "is the turret a separate part?" and "how heavy is it?".
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_INVENTORY_JOB"], encoding="utf-8"))
R = {"folders": {}}
EAL = unreal.EditorAssetLibrary
SMS = unreal.get_editor_subsystem(unreal.StaticMeshEditorSubsystem)


def size_m(box):
    e = box.box_extent if hasattr(box, "box_extent") else box.get_editor_property("box_extent")
    return [round(e.x * 2 / 100, 2), round(e.y * 2 / 100, 2), round(e.z * 2 / 100, 2)]


def static_info(m):
    return {"kind": "static", "lods": m.get_num_lods(), "size": size_m(m.get_bounds()),
            "mats": len(m.get_editor_property("static_materials")),
            "sockets": [str(n) for n in SMS.get_socket_names(m)] if hasattr(SMS, "get_socket_names") else None}


def skeletal_info(m):
    out = {"kind": "skeletal", "size": size_m(m.get_bounds()), "mats": len(m.get_editor_property("materials"))}
    try:
        sub = unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
        out["lods"] = sub.get_lod_count(m)
    except Exception: pass
    try:
        skel = m.get_editor_property("skeleton")
        ref = skel.get_reference_pose() if hasattr(skel, "get_reference_pose") else None
        out["bones"] = [str(n) for n in ref.get_bone_names()] if ref else None
    except Exception as e: out["bones_err"] = str(e)
    if not out.get("bones"):
        try: out["bones"] = [str(m.get_bone_name(i)) for i in range(m.get_num_bones())] if hasattr(m, "get_num_bones") else None
        except Exception as e: out["bones_err"] = str(e)
    try: out["sockets"] = [str(s.get_editor_property("socket_name")) for s in m.get_editor_property("sockets") or []]
    except Exception: pass
    return out


def main():
    for f in JOB["folders"]:
        rows = {}
        for p in EAL.list_assets(f, recursive=True, include_folder=False):
            d = EAL.find_asset_data(p)
            cls = str(d.asset_class_path.asset_name) if hasattr(d, "asset_class_path") else str(d.asset_class)
            if cls not in ("StaticMesh", "SkeletalMesh", "ParticleSystem", "NiagaraSystem", "AnimSequence", "Blueprint"): continue
            name = p.split(".")[0].replace(f, "").lstrip("/")
            try:
                if cls == "StaticMesh": rows[name] = static_info(unreal.load_asset(p))
                elif cls == "SkeletalMesh": rows[name] = skeletal_info(unreal.load_asset(p))
                else: rows[name] = {"kind": cls}
                for tag in ("Triangles", "Vertices", "LODs", "NaniteEnabled"):   # the asset registry's own tags (LOD0 triangles)
                    v = d.get_tag_value(tag)
                    if v: rows[name]["tag_" + tag] = v
            except Exception as e:
                rows[name] = {"kind": cls, "err": str(e)[:200]}
        R["folders"][f] = rows
        unreal.log_warning(f"[HROT-INV] {f}: {len(rows)}")


try:
    main(); R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
