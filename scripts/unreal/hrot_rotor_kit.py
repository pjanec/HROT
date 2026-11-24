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
    lst = res[0] if isinstance(res, tuple) else res
    return [(p.x, p.y, p.z) for p in lst.list] if hasattr(lst, "list") else [(p.x, p.y, p.z) for p in lst]


def keep_material(dm, mat_id, keep):
    sel = GS.GeometryScript_MeshSelection.select_mesh_elements_by_material_id(dm, mat_id) if hasattr(GS.GeometryScript_MeshSelection, "select_mesh_elements_by_material_id") \
        else GS.GeometryScript_MeshSelectionFunctions.select_mesh_elements_by_material_id(dm, mat_id)
    sel = sel[0] if isinstance(sel, tuple) else sel
    if keep: sel = GS.GeometryScript_MeshSelectionFunctions.invert_mesh_selection(dm, sel)[0] if False else _invert(dm, sel)
    GS.GeometryScript_MeshBasicEditFunctions.delete_selected_triangles_from_mesh(dm, sel)
    return dm


def _invert(dm, sel):
    r = GS.GeometryScript_MeshSelectionFunctions.invert_mesh_selection(dm, sel)
    return r[0] if isinstance(r, tuple) else r


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
        tb = float(JOB["tailBelowZ"])
        folder = sm.get_path_name().rsplit("/", 1)[0].replace("/Game/Fab/", "/Game/Hrot/Vehicles/Parts/")
        base = sm.get_name()
        def save(dm, name):
            path = f"/Game/Hrot/Vehicles/Parts/{name}"
            if EAL.does_asset_exist(path): EAL.delete_asset(path)
            opts = unreal.GeometryScriptCreateNewStaticMeshAssetOptions(); opts.enable_nanite = False; opts.enable_collision = False
            r = GS.GeometryScript_AssetUtils.create_new_static_mesh_asset_from_mesh(dm, path, opts)
            new = r[0] if isinstance(r, tuple) else r
            new.set_editor_property("static_materials", mats)
            EAL.save_loaded_asset(new, False); return new
        body = save(keep_material(load_dm(sm), mid, False), f"SM_{base}_Body")
        main = keep_material(load_dm(sm), mid, True)
        tail = keep_material(load_dm(sm), mid, True)
        box_lo = unreal.Vector(R["bounds"][0][0] - 10, R["bounds"][0][1] - 10, R["bounds"][0][2] - 10)
        sel_low = GS.GeometryScript_MeshSelectionFunctions.select_mesh_elements_in_box(main, unreal.Box(box_lo, unreal.Vector(R["bounds"][1][0] + 10, R["bounds"][1][1] + 10, tb)))
        sel_low = sel_low[0] if isinstance(sel_low, tuple) else sel_low
        GS.GeometryScript_MeshBasicEditFunctions.delete_selected_triangles_from_mesh(main, sel_low)
        sel_high = GS.GeometryScript_MeshSelectionFunctions.select_mesh_elements_in_box(tail, unreal.Box(unreal.Vector(box_lo.x, box_lo.y, tb), unreal.Vector(R["bounds"][1][0] + 10, R["bounds"][1][1] + 10, R["bounds"][1][2] + 10)))
        sel_high = sel_high[0] if isinstance(sel_high, tuple) else sel_high
        GS.GeometryScript_MeshBasicEditFunctions.delete_selected_triangles_from_mesh(tail, sel_high)
        def centre(dm):
            Q = positions(dm)
            return [(min(q[i] for q in Q) + max(q[i] for q in Q)) / 2 for i in range(3)], [(max(q[i] for q in Q) - min(q[i] for q in Q)) for i in range(3)]
        mc, ms = centre(main); tc, ts = centre(tail)
        log(f"main hub {mc} size {ms}; tail hub {tc} size {ts}")
        smain = save(main, f"SM_{base}_RotorMain"); stail = save(tail, f"SM_{base}_RotorTail")
        T = hull.get_editor_property("transform")   # the part's transform (mesh space -> hull frame)
        def pivot(c, axis_tail):
            p = T.transform_location(unreal.Vector(*c))
            q = T.rotation if not axis_tail else (T.rotation * unreal.Rotator(0, 0, 90).quaternion())   # the tail rotor turns about the hull's Y
            return unreal.Transform(p, q.rotator() if hasattr(q, "rotator") else unreal.Rotator(), unreal.Vector(1, 1, 1))
        hull.set_editor_property("mesh", body); parts[hull_i] = hull
        for mesh, parent in ((smain, unreal.HrotVehicleParent.MAIN_ROTOR), (stail, unreal.HrotVehicleParent.TAIL_ROTOR)):
            pt = unreal.HrotVehiclePart(); pt.set_editor_property("mesh", mesh); pt.set_editor_property("parent", parent); pt.set_editor_property("transform", T)
            parts.append(pt)
        kit.set_editor_property("parts", parts)
        kit.set_editor_property("main_rotor_pivot", pivot(mc, False)); kit.set_editor_property("tail_rotor_pivot", pivot(tc, True))
        kit.set_editor_property("main_rotor_rate", float(JOB.get("mainRate", 430))); kit.set_editor_property("tail_rotor_rate", float(JOB.get("tailRate", 1500)))
        kit.set_editor_property("rotor_radius", max(ms[0], ms[1]) / 2.0 * max(T.scale3d.x, 1e-6))
        log(f"kit saved {EAL.save_loaded_asset(kit, False)}")
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
