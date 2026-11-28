"""hrot_missile_kinds.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17g): missile KINDS (UHrotMissileKind).

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_MISSILE_KIND_JOB = { "kinds": [{...}], "out" }
    a kind: {"kind", "mesh", "yaw" (the nose onto +X), "scale", "length" (cm, as drawn — measured from the mesh when absent),
             "flame", "puffSize", "puffGrowth", "puffLife", "puffSpacing", "puffBrightness", "launchBlast", "blast": GRENADE | ROCKET | MORTAR | TANK_HE}
    The mesh is centred on its bounds (the packs' pivots are elsewhere). An existing kind is updated in place.
"""
import json, math, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_MISSILE_KIND_JOB"], encoding="utf-8"))
R = {"log": []}
EAL = unreal.EditorAssetLibrary


def log(m):
    unreal.log_warning(f"[HROT-MKIND] {m}"); R["log"].append(m)


def main(J):
    path = J["kind"]; folder, name = path.rsplit("/", 1)
    if EAL.does_asset_exist(path): kind = unreal.load_asset(path)
    else:
        f = unreal.DataAssetFactory(); f.set_editor_property("data_asset_class", unreal.HrotMissileKind)
        kind = unreal.AssetToolsHelpers.get_asset_tools().create_asset(name, folder, unreal.HrotMissileKind, f)
    sm = unreal.load_asset(J["mesh"])
    if sm is None: raise RuntimeError("no mesh " + J["mesh"])
    k, yaw = float(J.get("scale", 1)), float(J.get("yaw", 0))
    b = sm.get_bounds(); o, e = b.origin, b.box_extent
    a = math.radians(yaw)
    ox, oy = o.x * math.cos(a) - o.y * math.sin(a), o.x * math.sin(a) + o.y * math.cos(a)   # the bounds' centre, turned
    t = unreal.Transform(location=unreal.Vector(-k * ox, -k * oy, -k * o.z), rotation=unreal.Rotator(pitch=0, yaw=yaw, roll=0),
                         scale=unreal.Vector(k, k, k))
    length = J.get("length") or 2 * k * max(e.x, e.y, e.z)   # the long side: a missile's length
    kind.set_editor_property("mesh", sm)
    kind.set_editor_property("mesh_transform", t)
    kind.set_editor_property("length", float(length))
    for key, prop in (("flame", "flame_size"), ("puffSize", "puff_size"), ("puffGrowth", "puff_growth"), ("puffLife", "puff_life"),
                      ("puffSpacing", "puff_spacing"), ("puffBrightness", "puff_brightness"), ("launchBlast", "launch_blast")):
        if key in J: kind.set_editor_property(prop, float(J[key]))
    kind.set_editor_property("blast", getattr(unreal.HrotBlast, J.get("blast", "ROCKET")))
    EAL.save_loaded_asset(kind, False)
    log(f"{path}: {sm.get_name()} x{k} yaw {yaw}, length {length:.0f} cm")


try:
    for J in JOB["kinds"]: main(J)
    R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
