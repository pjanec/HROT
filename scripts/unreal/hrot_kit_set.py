"""hrot_kit_set.py - set named properties on vehicle kits (UHrotVehicleKit) and save them; prints the flame / wing values before and after.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi     HROT_KIT_SET = { "kits": { "<asset path>": { "<python prop>": value } }, "out" }

A value is a number / bool, or {"loc": [x, y, z], "rot": [roll, pitch, yaw]} for a transform. ⚠ LFS assets come back READ-ONLY: clear the flag before running.
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_KIT_SET"], encoding="utf-8-sig"))
R = {"kits": {}, "log": []}
SHOW = ("flamethrower", "flame_size", "flame_mount", "flap_amplitude", "flap_rate", "flap_bob", "glide_angle", "creature")


def show(kit):
    out = {}
    for p in SHOW:
        try:
            v = kit.get_editor_property(p)
            out[p] = [round(v.translation.x, 1), round(v.translation.y, 1), round(v.translation.z, 1)] if hasattr(v, "translation") else v
        except Exception as e:
            out[p] = f"? {e}"
    return out


try:
    for path, props in JOB["kits"].items():
        kit = unreal.load_asset(path)
        if not kit: R["log"].append("missing " + path); continue
        K = {"before": show(kit)}
        for p, v in props.items():
            if isinstance(v, dict):
                t = unreal.Transform(unreal.Vector(*v.get("loc", [0, 0, 0])), unreal.Rotator(*v.get("rot", [0, 0, 0])), unreal.Vector(1, 1, 1))
                kit.set_editor_property(p, t)
            else:
                kit.set_editor_property(p, v)
        K["saved"] = bool(unreal.EditorAssetLibrary.save_loaded_asset(kit, only_if_is_dirty=False))
        K["after"] = show(kit)
        R["kits"][path] = K
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1, default=str)
