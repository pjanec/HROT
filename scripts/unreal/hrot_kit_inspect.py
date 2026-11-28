"""hrot_kit_inspect.py - CE-12008/12009: facts about vehicle kits - their parts (mesh, parent, transform), seats (offset, pose clip), pivots,
and per part mesh: bounds and a vertex profile along X (the width / height of the body in slices: where a cabin is, where a tail begins).

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi     HROT_KIT_INSPECT = { "kits": [paths], "profile": [kit paths], "out" }
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_KIT_INSPECT"], encoding="utf-8-sig"))
R = {"kits": {}, "log": []}
GS = unreal


def positions(sm):
    dm = unreal.DynamicMesh()
    res = GS.GeometryScript_AssetUtils.copy_mesh_from_static_mesh(sm, dm, unreal.GeometryScriptCopyMeshFromAssetOptions(), unreal.GeometryScriptMeshReadLOD())
    dm = res[0] if isinstance(res, tuple) and type(res[0]).__name__ == "DynamicMesh" else dm
    res = GS.GeometryScript_MeshQueries.get_all_vertex_positions(dm, True)
    items = res if isinstance(res, tuple) else (res,)
    lst = next(x for x in items if type(x).__name__ == "GeometryScriptVectorList")
    r2 = lst.convert_vector_list_to_array() if hasattr(lst, "convert_vector_list_to_array") else GS.GeometryScript_ListUtils.convert_vector_list_to_array(lst)
    pts = r2[1] if isinstance(r2, tuple) else r2
    return [(p.x, p.y, p.z) for p in pts]


def tf(t):
    r = t.rotation.rotator()
    return {"loc": [round(t.translation.x, 1), round(t.translation.y, 1), round(t.translation.z, 1)], "rot": [round(r.roll, 1), round(r.pitch, 1), round(r.yaw, 1)],
            "scale": [round(t.scale3d.x, 3), round(t.scale3d.y, 3), round(t.scale3d.z, 3)]}


try:
    for path in JOB["kits"]:
        kit = unreal.load_asset(path)
        if not kit: R["log"].append("missing " + path); continue
        K = {"parts": [], "seats": []}
        for p in kit.get_editor_property("parts"):
            m = p.get_editor_property("mesh")
            part = {"mesh": m.get_path_name() if m else None, "parent": str(p.get_editor_property("parent")), "t": tf(p.get_editor_property("transform"))}
            if m:
                part["slots"] = [str(s.material_slot_name) for s in m.get_editor_property("static_materials")]
                if path in JOB.get("profile", []):
                    P = positions(m)
                    part["n"] = len(P); part["min"] = [round(min(q[i] for q in P), 1) for i in range(3)]; part["max"] = [round(max(q[i] for q in P), 1) for i in range(3)]
                    ax = int(JOB.get("axis", 0)); zmax = float(JOB.get("zBelow", 1e9)); o = [i for i in (0, 1, 2) if i != ax]
                    Pz = [q for q in P if q[2] < zmax]
                    lo, hi = min(q[ax] for q in Pz), max(q[ax] for q in Pz); nb = int(JOB.get("bins", 24)); bins = [[] for _ in range(nb)]
                    for q in Pz: bins[min(nb - 1, int((q[ax] - lo) / max(hi - lo, 1e-6) * nb))].append(q)
                    part["profile"] = [{"at": round(lo + (i + 0.5) * (hi - lo) / nb), "n": len(b), "a": [round(min(q[o[0]] for q in b)), round(max(q[o[0]] for q in b))] if b else None,
                                        "b": [round(min(q[o[1]] for q in b)), round(max(q[o[1]] for q in b))] if b else None} for i, b in enumerate(bins)]
            K["parts"].append(part)
        for s in kit.get_editor_property("seats"):
            pose = s.get_editor_property("pose")
            K["seats"].append({"name": str(s.get_editor_property("name")), "offset": tf(s.get_editor_property("offset")), "pose": pose.get_path_name() if pose else None})
        for f in ("main_rotor_pivot", "turret_pivot", "muzzle"):
            try:
                v = kit.get_editor_property(f); K[f] = tf(v) if hasattr(v, "translation") else [v.x, v.y, v.z]
            except Exception: pass
        R["kits"][path] = K
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
