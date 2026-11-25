"""hrot_rotor_kit.py - CE-4651 (U-19h): a helicopter kit's ROTORS cut out of its hull mesh so they can turn (the user, live 2026-11-24: "the
helicopter rotor is not moving when the helicopter is hovering or flying ... either rotate it or replace it with a semi-transparent flat dish").

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi
    HROT_ROTOR_JOB = { "kit": "/Game/Hrot/Vehicles/DA_Air_Apache", "material": "Rotor_Blade", "out": <json>, "measure": true|false,
                       "tailBelowZ": <cm, mesh space - rotor triangles below it are the tail rotor>, "mainRate": 430, "tailRate": 1500 }

measure: the hull part's rotor-material triangles - their vertex bounds and a z / x histogram, to place the main / tail split (facts only).
cut:     three new meshes beside the hull's (…_Body, …_RotorMain, …_RotorTail; the pack's mesh untouched), the kit's hull part swapped for the
         body, two parts parented MainRotor / TailRotor, their pivots at each rotor's hub (the centre of its blade bounds; main axis Z, tail
         axis the hull's Y), RotorRadius from the main blades. And /Game/Hrot/Fx/M_HrotRotorDisc (translucent, faint, darker at the rim).
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_ROTOR_JOB"], encoding="utf-8-sig"))
R = {"log": []}
GS = unreal
EAL = unreal.EditorAssetLibrary


def log(m): unreal.log_warning(f"[HROT-ROTOR] {m}"); R["log"].append(m)


def load_dm(sm):
    dm = unreal.DynamicMesh()
    res = GS.GeometryScript_AssetUtils.copy_mesh_from_static_mesh(sm, dm, unreal.GeometryScriptCopyMeshFromAssetOptions(), unreal.GeometryScriptMeshReadLOD())
    return res[0] if isinstance(res, tuple) else dm


def positions(dm):
    res = GS.GeometryScript_MeshQueries.get_all_vertex_positions(dm, True)
    items = res if isinstance(res, tuple) else (res,)
    lst = next(x for x in items if type(x).__name__ == "GeometryScriptVectorList")
    r2 = lst.convert_vector_list_to_array() if hasattr(lst, "convert_vector_list_to_array") else GS.GeometryScript_ListUtils.convert_vector_list_to_array(lst)
    pts = r2[1] if isinstance(r2, tuple) else r2
    return [(p.x, p.y, p.z) for p in pts]


def pick(res, cls):
    if isinstance(res, tuple):
        for v in res:
            if type(v).__name__ == cls: return v
        return res[0]
    return res


def keep_material(dm, mat_id, keep):
    """keep (True) or delete (False) the triangles of one material slot"""
    sel = pick(GS.GeometryScript_MeshSelection.select_mesh_elements_by_material_id(dm, mat_id), "GeometryScriptMeshSelection")
    if keep: sel = pick(GS.GeometryScript_MeshSelection.invert_mesh_selection(dm, sel), "GeometryScriptMeshSelection")
    GS.GeometryScript_MeshEdits.delete_selected_triangles_from_mesh(dm, sel)
    return dm


def select_box(dm, lo, hi):
    return pick(GS.GeometryScript_MeshSelection.select_mesh_elements_in_box(dm, unreal.Box(lo, hi), unreal.GeometryScriptMeshSelectionType.TRIANGLES, False, 3),
                "GeometryScriptMeshSelection")


try:
    kit = unreal.load_asset(JOB["kit"])
    parts = list(kit.get_editor_property("parts"))
    hull_i = max(range(len(parts)), key=lambda i: len(parts[i].get_editor_property("mesh").get_editor_property("static_materials")) if parts[i].get_editor_property("mesh") else -1)
    hull = parts[hull_i]; sm = hull.get_editor_property("mesh")
    mats = sm.get_editor_property("static_materials")
    names = [(str(m.material_slot_name), m.material_interface.get_name() if m.material_interface else "") for m in mats]
    mid = next((i for i, (s, n) in enumerate(names) if JOB["material"].lower() in (s + n).lower()), -1)
    log(f"kit {kit.get_name()}: hull part {hull_i} mesh {sm.get_path_name()} slots {names}; rotor slot {mid}; part transform {hull.get_editor_property('transform')}")
    if mid < 0: raise RuntimeError("no rotor material slot")
    ALL = positions(load_dm(sm))
    zt = max(q[2] for q in ALL); R["all"] = {"n": len(ALL), "min": [min(q[i] for q in ALL) for i in range(3)], "max": [max(q[i] for q in ALL) for i in range(3)]}
    for band in (20, 40, 80, 150):
        top = [q for q in ALL if q[2] > zt - band]
        R[f"top{band}"] = {"n": len(top), "min": [round(min(q[i] for q in top), 1) for i in range(3)], "max": [round(max(q[i] for q in top), 1) for i in range(3)]}
    rear = [q for q in ALL if q[0] < R["all"]["min"][0] + 120]   # the tail end (the mesh's -X)
    front = [q for q in ALL if q[0] > R["all"]["max"][0] - 120]
    for nm, s_ in (("minX120", rear), ("maxX120", front)):
        R[nm] = {"n": len(s_), "min": [round(min(q[i] for q in s_), 1) for i in range(3)], "max": [round(max(q[i] for q in s_), 1) for i in range(3)]}
    rot = keep_material(load_dm(sm), mid, True)
    P = positions(rot)
    xs, ys, zs = [p[0] for p in P], [p[1] for p in P], [p[2] for p in P]
    R["bounds"] = [[min(xs), min(ys), min(zs)], [max(xs), max(ys), max(zs)]]; R["n"] = len(P)
    def hist(v, n=12):
        lo, hi = min(v), max(v); b = [0] * n
        for q in v: b[min(n - 1, int((q - lo) / max(hi - lo, 1e-6) * n))] += 1
        return {"lo": round(lo, 1), "hi": round(hi, 1), "bins": b}
    R["z"] = hist(zs); R["x"] = hist(xs); R["y"] = hist(ys)
    log(f"rotor vertices {len(P)} bounds {R['bounds']}")
    if not JOB.get("measure", True):
        # the main rotor by its BOX (mesh space): measured on SM_Apache - the blades are a flat band 3.2-3.4 m up, 14.6 m across, the hub on
        # top; the pack's "Rotor_Blade" slot holds only 64 small low vertices, so a material cut would miss the rotor. ⏳ the tail rotor (mixed
        # with the fin at the tail end) is left on the body for now
        (lx, ly, lz), (hx, hy, hz) = JOB["mainBox"]
        lo, hi = unreal.Vector(lx, ly, lz), unreal.Vector(hx, hy, hz)
        base = sm.get_name()
        def save(dm, name):
            path = f"/Game/Hrot/Vehicles/Parts/{name}"
            if EAL.does_asset_exist(path): EAL.delete_asset(path)
            opts = unreal.GeometryScriptCreateNewStaticMeshAssetOptions(); opts.enable_nanite = False; opts.enable_collision = False
            new = pick(GS.GeometryScript_NewAssetUtils.create_new_static_mesh_asset_from_mesh(dm, path, opts), "StaticMesh")
            new.set_editor_property("static_materials", mats)
            EAL.save_loaded_asset(new, False); return new
        body_dm = load_dm(sm); GS.GeometryScript_MeshEdits.delete_selected_triangles_from_mesh(body_dm, select_box(body_dm, lo, hi))
        main_dm = load_dm(sm); GS.GeometryScript_MeshEdits.delete_selected_triangles_from_mesh(main_dm, pick(GS.GeometryScript_MeshSelection.invert_mesh_selection(main_dm, select_box(main_dm, lo, hi)), "GeometryScriptMeshSelection"))
        Q = positions(main_dm)
        mc = [(min(q[i] for q in Q) + max(q[i] for q in Q)) / 2 for i in range(3)]; ms = [max(q[i] for q in Q) - min(q[i] for q in Q) for i in range(3)]
        mc[2] = min(q[2] for q in Q)
        log(f"main rotor: {len(Q)} vertices, hub {mc}, size {ms}; body vertices {len(positions(body_dm))}")
        body = save(body_dm, f"SM_{base}_Body"); smain = save(main_dm, f"SM_{base}_RotorMain")
        T = hull.get_editor_property("transform")
        hull.set_editor_property("mesh", body); parts[hull_i] = hull
        pt = unreal.HrotVehiclePart(); pt.set_editor_property("mesh", smain); pt.set_editor_property("parent", unreal.HrotVehicleParent.MAIN_ROTOR); pt.set_editor_property("transform", T)
        parts = [p_ for p_ in parts if p_.get_editor_property("parent") != unreal.HrotVehicleParent.MAIN_ROTOR] + [pt]
        kit.set_editor_property("parts", parts)
        hub = T.transform_location(unreal.Vector(*mc))
        kit.set_editor_property("main_rotor_pivot", unreal.Transform(hub, unreal.Rotator(0, 0, 0), unreal.Vector(1, 1, 1)))
        kit.set_editor_property("main_rotor_rate", float(JOB.get("mainRate", 430)))
        kit.set_editor_property("rotor_radius", max(ms[0], ms[1]) / 2.0 * T.scale3d.x)
        log(f"kit: pivot {hub}, radius {max(ms[0], ms[1]) / 2.0 * T.scale3d.x} cm; saved {EAL.save_loaded_asset(kit, False)}")
        # the blur disc material
        AT = unreal.AssetToolsHelpers.get_asset_tools(); MEL = unreal.MaterialEditingLibrary
        if EAL.does_asset_exist("/Game/Hrot/Fx/M_HrotRotorDisc"): EAL.delete_asset("/Game/Hrot/Fx/M_HrotRotorDisc")
        m = AT.create_asset("M_HrotRotorDisc", "/Game/Hrot/Fx", unreal.Material, unreal.MaterialFactoryNew())
        m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT); m.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
        m.set_editor_property("two_sided", True)
        tc_ = MEL.create_material_expression(m, unreal.MaterialExpressionTextureCoordinate, -800, 0)
        half = MEL.create_material_expression(m, unreal.MaterialExpressionConstant, -800, 120); half.set_editor_property("r", 0.5)
        sub = MEL.create_material_expression(m, unreal.MaterialExpressionSubtract, -650, 0); MEL.connect_material_expressions(tc_, "", sub, "A"); MEL.connect_material_expressions(half, "", sub, "B")
        ln = MEL.create_material_expression(m, unreal.MaterialExpressionLength, -500, 0); MEL.connect_material_expressions(sub, "", ln, "")
        k = MEL.create_material_expression(m, unreal.MaterialExpressionConstant, -500, 120); k.set_editor_property("r", 0.28)
        mul = MEL.create_material_expression(m, unreal.MaterialExpressionMultiply, -350, 0); MEL.connect_material_expressions(ln, "", mul, "A"); MEL.connect_material_expressions(k, "", mul, "B")
        MEL.connect_material_property(mul, "", unreal.MaterialProperty.MP_OPACITY)   # faint at the hub (0), ~0.14 at the rim
        col = MEL.create_material_expression(m, unreal.MaterialExpressionConstant3Vector, -350, 200); col.set_editor_property("constant", unreal.LinearColor(0.06, 0.06, 0.06, 1))
        MEL.connect_material_property(col, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
        MEL.recompile_material(m); log(f"disc material saved {EAL.save_loaded_asset(m, False)}")
except Exception:
    log(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
