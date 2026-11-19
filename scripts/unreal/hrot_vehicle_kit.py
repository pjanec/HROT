"""hrot_vehicle_kit.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17): a vehicle KIT (UHrotVehicleKit) from a vendor's parts.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_KIT_JOB = { "kit", "probe", "turret", "gun", "skip",
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


def main():
    probe = json.load(open(JOB["probe"], encoding="utf-8"))
    tp, gp = unreal.Vector(*JOB["turret"]), unreal.Vector(*JOB["gun"])
    rx = lambda key: re.compile(JOB.get(key) or "^$")
    skip, on_turret, on_gun = rx("skip"), rx("turretParts"), rx("gunParts")
    parts = []
    for a in probe["actors"]:
        for c in a["comps"]:
            if not c["mesh"]: continue
            name = c["mesh"].rsplit(".", 1)[-1]
            if skip.search(name): continue
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
    path = JOB["kit"]
    if EAL.does_asset_exist(path): EAL.delete_asset(path)
    folder, name = path.rsplit("/", 1)
    f = unreal.DataAssetFactory(); f.set_editor_property("data_asset_class", unreal.HrotVehicleKit)
    kit = unreal.AssetToolsHelpers.get_asset_tools().create_asset(name, folder, unreal.HrotVehicleKit, f)
    kit.set_editor_property("parts", parts)
    kit.set_editor_property("turret_pivot", T(JOB["turret"]))
    g = gp - tp
    kit.set_editor_property("gun_pivot", T((g.x, g.y, g.z)))
    kit.set_editor_property("muzzle", unreal.Vector(*JOB["muzzle"]))
    kit.set_editor_property("engine_deck", unreal.Vector(*JOB["engineDeck"]))
    kit.set_editor_property("flash_size", float(JOB.get("flashSize", 8)))
    EAL.save_loaded_asset(kit, False)
    R["parts"] = len(parts)
    log(f"{path}: {len(parts)} parts")


try:
    main(); R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
