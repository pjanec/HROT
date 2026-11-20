"""hrot_vehicle_kit.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17): a vehicle KIT (UHrotVehicleKit) from a vendor's parts.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_KIT_JOB = { "kit", "probe", "turret", "gun", "skip",
                                                                                                "actor", "meshes": [{"mesh", "loc" | "ground": true, "rot", "scale"}],
                                                                                                "turretParts", "gunParts", "muzzle",
                                                                                                "engineDeck", "flashSize", "out" }
    probe        hrot_level_probe.py's result for the vendor's showcase map (every part placed: the hull frame = the map origin)
    turret, gun  the pivots: the turret ring centre (hull space) and the trunnion (hull space) — here: where the vendor placed the
                 turret and the gun meshes (their pivots are the ring and the trunnion)
    turretParts / gunParts   regexes over the MESH names riding on the turret / the gun; skip = not built (interiors)
    muzzle       the bore's end in the gun's space (cm); engineDeck (hull space)
"""
import json, os, re, traceback
import unreal

JOB = json.load(open(os.environ["HROT_KIT_JOB"], encoding="utf-8"))
R = {"log": []}
EAL = unreal.EditorAssetLibrary


def log(m):
    unreal.log_warning(f"[HROT-KIT] {m}"); R["log"].append(m)


def T(loc, rot=(0, 0, 0)):
    return unreal.Transform(location=unreal.Vector(*loc), rotation=unreal.Rotator(pitch=rot[0], yaw=rot[1], roll=rot[2]))


def main(JOB):
    probe = json.load(open(JOB["probe"], encoding="utf-8")) if JOB.get("probe") else {"actors": []}
    tp, gp = unreal.Vector(*JOB.get("turret", (0, 0, 0))), unreal.Vector(*JOB.get("gun", (0, 0, 0)))   # none: a car, an aircraft
    rx = lambda key: re.compile(JOB.get(key) or "^$")
    skip, on_turret, on_gun = rx("skip"), rx("turretParts"), rx("gunParts")
    parts = []
    for a in probe["actors"]:
        if JOB.get("actor") and a["label"] != JOB["actor"]: continue   # one vehicle out of a multi-vehicle probe
        for c in a["comps"]:
            if not c["mesh"]: continue
            name = c["mesh"].rsplit(".", 1)[-1]
            if skip.search(name): continue
            if not isinstance(unreal.load_asset(c["mesh"]), unreal.StaticMesh): continue   # a kit holds static parts (not a vendor's skeletal proxy)
            w = c["world"]; loc = unreal.Vector(*w["loc"]); rot = w["rot"]
            if on_gun.search(name): parent, base, rot = unreal.HrotVehicleParent.GUN, gp, (0, 0, 0)   # the vendor showed it elevated
            elif on_turret.search(name): parent, base = unreal.HrotVehicleParent.TURRET, tp
            else: parent, base = unreal.HrotVehicleParent.HULL, unreal.Vector(0, 0, 0)
            d = loc - base
            p = unreal.HrotVehiclePart()
            p.set_editor_property("mesh", unreal.load_asset(c["mesh"]))
            p.set_editor_property("parent", parent)
            p.set_editor_property("transform", T((d.x, d.y, d.z), rot))
            parts.append(p)
            log(f"{name:20} {str(parent).split('.')[-1]:7} at {d.x:.1f}, {d.y:.1f}, {d.z:.1f} rot {rot}")
    for m in JOB.get("meshes") or []:   # parts given directly: {"mesh", "loc", "rot", "scale"} (no probe needed)
        p = unreal.HrotVehiclePart()
        sm = unreal.load_asset(m["mesh"])
        p.set_editor_property("mesh", sm)
        loc = m.get("loc", (0, 0, 0))
        if m.get("ground"):   # no measured offset: centre it over the origin, its lowest point on the ground (scale 1)
            b = sm.get_bounds(); o, x = b.origin, b.box_extent
            loc = (-o.x, -o.y, x.z - o.z)
            log(f"{sm.get_name()}: grounded at {loc[0]:.1f}, {loc[1]:.1f}, {loc[2]:.1f} (size {2*x.x:.0f} x {2*x.y:.0f} x {2*x.z:.0f} cm)")
        t = T(loc, m.get("rot", (0, 0, 0)))
        if m.get("scale"): t.scale3d = unreal.Vector(m["scale"], m["scale"], m["scale"])
        p.set_editor_property("transform", t)
        parts.append(p)
    path = JOB["kit"]
    folder, name = path.rsplit("/", 1)
    if EAL.does_asset_exist(path):   # ⚠ measured: a lab map that references a kit blocks deleting it — update it in place
        kit = unreal.load_asset(path)
    else:
        f = unreal.DataAssetFactory(); f.set_editor_property("data_asset_class", unreal.HrotVehicleKit)
        kit = unreal.AssetToolsHelpers.get_asset_tools().create_asset(name, folder, unreal.HrotVehicleKit, f)
    if kit is None: raise RuntimeError("cannot create or load " + path)
    kit.set_editor_property("parts", parts)
    kit.set_editor_property("turret_pivot", T(JOB.get("turret", (0, 0, 0))))
    g = gp - tp
    kit.set_editor_property("gun_pivot", T((g.x, g.y, g.z)))
    kit.set_editor_property("muzzle", unreal.Vector(*JOB.get("muzzle", (0, 0, 0))))
    kit.set_editor_property("engine_deck", unreal.Vector(*JOB.get("engineDeck", (0, 0, 100))))
    kit.set_editor_property("flash_size", float(JOB.get("flashSize", 8)))
    kit.set_editor_property("damage_fx_scale", float(JOB.get("damageFxScale", 1)))
    kit.set_editor_property("flash_life", float(JOB.get("flashLife", 0.15)))
    EAL.save_loaded_asset(kit, False)
    R["parts"] = len(parts)
    log(f"{path}: {len(parts)} parts")


try:
    for J in (JOB["kits"] if "kits" in JOB else [JOB]):   # one kit, or several in one run: {"kits": [...], "out"}
        main(J)
    R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
