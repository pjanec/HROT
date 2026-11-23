"""hrot_air_nose.py - CE-4651 (U-19h 21): turn the air kits that fly TAIL-FIRST round (HROT flies +X).

The nose of each air kit's hull mesh was found by LOOKING at its vertex silhouette (top + side views, the mesh's own part yaw applied): the Apache's canopy,
the F-16's tail fin, the C-130's blunt nose all pointed to -X. Their part is turned 180 deg about its own centre (the part's translation is moved with it so the
model stays where it was). Measured nose direction after (+X in the actor frame): Apache, F-16, C-130 fixed; Typhoon, OH-1, F-22 were right already.

    UnrealEditor-Cmd <project> -ExecutePythonScript=scripts/unreal/hrot_air_nose.py -nullrhi -unattended
⚠ the kit files can come back READ-ONLY (git/LFS restore): cleared before saving, and the file's timestamp is checked after (a silent failed save looks the same).
"""
import math, os, stat
import unreal

FLIP = ("DA_Air_Apache", "DA_Air_F16", "DA_Air_AC130")
PML = unreal.ProceduralMeshLibrary


def centre(mesh):
    xs, ys = [], []
    for s in range(max(1, mesh.get_num_sections(0))):
        v = PML.get_section_from_static_mesh(mesh, 0, s)[0]
        xs += [p.x for p in v]; ys += [p.y for p in v]
    return (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2


def rot(yaw_deg, x, y):
    a = math.radians(yaw_deg); return x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a)


for name in FLIP:
    path = "/Game/Hrot/Vehicles/" + name
    kit = unreal.load_asset(path)
    parts = kit.get_editor_property("parts")
    new = []
    for p in parts:
        tr = p.get_editor_property("transform"); yaw = tr.rotation.rotator().yaw
        cx, cy = centre(p.get_editor_property("mesh"))
        wx, wy = rot(yaw, cx, cy); wx += tr.translation.x; wy += tr.translation.y     # the mesh centre in the actor frame now
        yaw2 = yaw + 180.0
        rx, ry = rot(yaw2, cx, cy)
        tr.rotation = unreal.Rotator(0, 0, yaw2).quaternion()
        tr.translation = unreal.Vector(wx - rx, wy - ry, tr.translation.z)
        p.set_editor_property("transform", tr); new.append(p)
    kit.set_editor_property("parts", new)
    f = os.path.join(unreal.Paths.project_content_dir(), "Hrot", "Vehicles", name + ".uasset")
    before = os.path.getmtime(f)
    os.chmod(f, os.stat(f).st_mode | stat.S_IWRITE)
    ok = unreal.EditorAssetLibrary.save_loaded_asset(kit)
    unreal.log_warning("[HROT-NOSE] %s yaw -> %.0f saved %s mtime changed %s writable %s" % (name, yaw2, ok, os.path.getmtime(f) != before, os.access(f, os.W_OK)))
