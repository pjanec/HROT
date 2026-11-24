"""hrot_heli_kit.py - CE-12008/12009 (user 2026-11-24): "use the Japanese helicopter model and cut the opening on the side like if there was a door,
and sit the soldiers there. And please use the machine gun that you cut off the door of the vehicle and put it there instead of minigun."

The UH-60 stand-in kit /Game/Hrot/Vehicles/DA_Air_UH60, built from the OH-1 kit (DA_Air_OH1 stays as it is):
  * scaled `scale` (the OH-1 is ~12 m; its cabin then ~2 m wide, HROT's door seats are at +-1.05 m)
  * a DOOR OPENING cut on each side (`door` box, mesh space, mirrored in x) and the main rotor (`rotorBelow`: everything above that z) cut
    off as its own part on a MainRotor pivot (+ the blur disc radius)
  * eight SEATS in HROT's DoorSeats() order (NedTkbCatalog: side +1 = HROT left = Unreal -Y first, x front to back, then side -1),
    each on the door sill facing out, with the seated clip `pose`
  * the roof machine gun (SM_HrotRoofMg, cut from the VBL) on a pintle at the front of each door, pointing out

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi
    HROT_HELI_JOB = { "src", "dst", "scale", "door": [[x0, y0, z0], [x1, y1, z1]] (mesh, the +x side), "rotorBelow": z, "sill": z (mesh),
                      "edge": x (mesh, the door's outer face), "seatYs": [4 mesh y, front to back], "pose", "gun", "gunScale", "gunY" (mesh y), "gunZ" (above sill, cm), "out" }
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_HELI_JOB"], encoding="utf-8-sig"))
R = {"log": []}
GS = unreal
EAL = unreal.EditorAssetLibrary


def log(m): unreal.log_warning(f"[HROT-HELI] {m}"); R["log"].append(m)


def pick(res, cls):
    if isinstance(res, tuple):
        for v in res:
            if type(v).__name__ == cls: return v
        return res[0]
    return res


def load_dm(sm):
    dm = unreal.DynamicMesh()
    return pick(GS.GeometryScript_AssetUtils.copy_mesh_from_static_mesh(sm, dm, unreal.GeometryScriptCopyMeshFromAssetOptions(), unreal.GeometryScriptMeshReadLOD()), "DynamicMesh")


def positions(dm):
    res = GS.GeometryScript_MeshQueries.get_all_vertex_positions(dm, True)
    items = res if isinstance(res, tuple) else (res,)
    lst = next(x for x in items if type(x).__name__ == "GeometryScriptVectorList")
    r2 = lst.convert_vector_list_to_array() if hasattr(lst, "convert_vector_list_to_array") else GS.GeometryScript_ListUtils.convert_vector_list_to_array(lst)
    pts = r2[1] if isinstance(r2, tuple) else r2
    return [(p.x, p.y, p.z) for p in pts]


def box(dm, lo, hi):
    return pick(GS.GeometryScript_MeshSelection.select_mesh_elements_in_box(dm, unreal.Box(unreal.Vector(*lo), unreal.Vector(*hi)),
                                                                           unreal.GeometryScriptMeshSelectionType.TRIANGLES, False, 1), "GeometryScriptMeshSelection")


def delete(dm, sel): GS.GeometryScript_MeshEdits.delete_selected_triangles_from_mesh(dm, sel)


def save(dm, name, mats):
    path = f"/Game/Hrot/Vehicles/Parts/{name}"
    if EAL.does_asset_exist(path): EAL.delete_asset(path)
    opts = unreal.GeometryScriptCreateNewStaticMeshAssetOptions(); opts.enable_nanite = False; opts.enable_collision = False
    new = pick(GS.GeometryScript_NewAssetUtils.create_new_static_mesh_asset_from_mesh(dm, path, opts), "StaticMesh")
    new.set_editor_property("static_materials", mats); EAL.save_loaded_asset(new, False); return new


try:
    # updated IN PLACE when it exists (a delete + duplicate of a loaded kit returned None); the hull always from the SOURCE kit
    kit = unreal.load_asset(JOB["dst"]) if EAL.does_asset_exist(JOB["dst"]) else EAL.duplicate_asset(JOB["src"], JOB["dst"])
    src_hull = list(unreal.load_asset(JOB["src"]).get_editor_property("parts"))[0]
    hull = unreal.HrotVehiclePart(); hull.set_editor_property("mesh", src_hull.get_editor_property("mesh")); hull.set_editor_property("parent", src_hull.get_editor_property("parent"))
    hull.set_editor_property("transform", src_hull.get_editor_property("transform")); sm = hull.get_editor_property("mesh")
    mats = sm.get_editor_property("static_materials")
    s = float(JOB["scale"])
    T0 = hull.get_editor_property("transform")
    T = unreal.Transform(unreal.Vector(T0.translation.x * s, T0.translation.y * s, T0.translation.z * s), T0.rotation.rotator(), unreal.Vector(s, s, s))
    (x0, y0, z0), (x1, y1, z1) = JOB["door"]; rb = float(JOB["rotorBelow"])
    body = load_dm(sm)
    delete(body, box(body, (-2000, -2000, rb), (2000, 2000, 2000)))       # the rotor
    delete(body, box(body, (x0, y0, z0), (x1, y1, z1)))                    # the door, +x side
    delete(body, box(body, (-x1, y0, z0), (-x0, y1, z1)))                  # the door, -x side
    rotor = load_dm(sm)
    delete(rotor, box(rotor, (-2000, -2000, -2000), (2000, 2000, rb)))
    Q = positions(rotor)
    hub = [(min(q[0] for q in Q) + max(q[0] for q in Q)) / 2, (min(q[1] for q in Q) + max(q[1] for q in Q)) / 2, min(q[2] for q in Q)]
    span = max(max(q[0] for q in Q) - min(q[0] for q in Q), max(q[1] for q in Q) - min(q[1] for q in Q))
    log(f"body {len(positions(body))} vertices, rotor {len(Q)} vertices, hub {hub}, span {span}")
    sbody = save(body, "SM_UH60_Body", mats); srotor = save(rotor, "SM_UH60_RotorMain", mats)
    hull.set_editor_property("mesh", sbody); hull.set_editor_property("transform", T)
    newparts = [hull]
    rp = unreal.HrotVehiclePart(); rp.set_editor_property("mesh", srotor); rp.set_editor_property("parent", unreal.HrotVehicleParent.MAIN_ROTOR); rp.set_editor_property("transform", T)
    newparts.append(rp)
    W = lambda x, y, z: T.transform_location(unreal.Vector(x, y, z))   # mesh -> the vehicle actor's frame
    # the doors' outer faces: mesh +x / -x map to the actor's sides; which is the actor's -Y (HROT left)?
    left_x = JOB["edge"] if W(JOB["edge"], 0, 0).y < W(-JOB["edge"], 0, 0).y else -JOB["edge"]
    gun = unreal.load_asset(JOB["gun"]); gs = float(JOB.get("gunScale", 1.5))
    gb = gun.get_bounds(); go = gb.origin
    for side_x in (left_x, -left_x):
        out = W(side_x, 0, 0) - W(0, 0, 0); out = unreal.Vector(0, 1 if out.y > 0 else -1, 0)
        at = W(side_x, JOB["gunY"], JOB["sill"]) + unreal.Vector(0, 0, float(JOB.get("gunZ", 95)))
        yaw = -90.0 + (90.0 if out.y > 0 else -90.0)   # the mesh's barrel is +Y; yaw -90 -> +X, then turned outward
        rot = unreal.Rotator(0, 0, yaw)
        # bring the gun's own centre (it keeps its place on the VBL roof) to `at`
        import math
        ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))   # the yaw by hand: Unreal's Python has no rotate_vector here
        c = unreal.Vector((go.x * ca - go.y * sa) * gs, (go.x * sa + go.y * ca) * gs, go.z * gs)
        gp = unreal.HrotVehiclePart(); gp.set_editor_property("mesh", gun); gp.set_editor_property("parent", unreal.HrotVehicleParent.HULL)
        gp.set_editor_property("transform", unreal.Transform(at - c, rot, unreal.Vector(gs, gs, gs)))
        newparts.append(gp)
    # the cabin FLOOR (the OH-1 is a hollow shell): a thin plate at the sill across the cabin, the body's own metal
    cube = unreal.load_asset("/Engine/BasicShapes/Cube.Cube")
    fy0, fy1 = JOB["door"][0][1], JOB["door"][1][1]; fx = float(JOB["edge"])
    c0 = W(0, (fy0 + fy1) / 2, JOB["sill"]); a_ = W(0, fy0, JOB["sill"]); b_ = W(0, fy1, JOB["sill"]); e_ = W(fx, 0, 0) - W(-fx, 0, 0)
    fl = unreal.HrotVehiclePart(); fl.set_editor_property("mesh", cube); fl.set_editor_property("parent", unreal.HrotVehicleParent.HULL)
    fl.set_editor_property("transform", unreal.Transform(c0 - unreal.Vector(0, 0, 2), unreal.Rotator(0, 0, 0),
                                                         unreal.Vector(abs(b_.x - a_.x) / 100.0, abs(e_.y) / 100.0, 0.04)))
    metal = next((m.material_interface for m in mats if m.material_interface and "metal" in (str(m.material_slot_name) + m.material_interface.get_name()).lower()), mats[0].material_interface)
    fl.set_editor_property("materials", [metal])
    newparts.append(fl)
    kit.set_editor_property("parts", newparts)
    hubw = W(*hub)
    kit.set_editor_property("main_rotor_pivot", unreal.Transform(hubw, unreal.Rotator(0, 0, 0), unreal.Vector(1, 1, 1)))
    kit.set_editor_property("main_rotor_rate", 430.0); kit.set_editor_property("rotor_radius", span / 2.0 * s)
    pose = unreal.load_asset(JOB["pose"])
    seats = []
    for side_x in (left_x, -left_x):            # HROT DoorSeats(): side +1 (left) first, then -1; x 2.4, 1.8, 1.2, 0.6 = front to back
        for i, y in enumerate(JOB["seatYs"]):
            p = W(side_x, y, JOB["sill"]) + unreal.Vector(0, 0, float(JOB.get("hips", 14)))
            facing = 90.0 if p.y > hubw.y else -90.0
            st = unreal.HrotSeat(); st.set_editor_property("name", f"door {'left' if side_x == left_x else 'right'} {i}")
            st.set_editor_property("offset", unreal.Transform(p, unreal.Rotator(0, 0, facing), unreal.Vector(1, 1, 1)))
            st.set_editor_property("pose", pose); seats.append(st)
    kit.set_editor_property("seats", seats)
    log(f"kit {JOB['dst']}: {len(newparts)} parts, {len(seats)} seats, rotor pivot {hubw}, radius {span / 2.0 * s}; saved {EAL.save_loaded_asset(kit, False)}")
    R["seats"] = [[round(st.get_editor_property('offset').translation.x), round(st.get_editor_property('offset').translation.y), round(st.get_editor_property('offset').translation.z)] for st in seats]
except Exception:
    log(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
