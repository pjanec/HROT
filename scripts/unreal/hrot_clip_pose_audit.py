"""hrot_clip_pose_audit.py - CE-4651 (U-19g): how each person clip actually POSES the body, from the animation data (no live run).

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi
        HROT_CLIP_AUDIT = { "kits": ["DA_Person_Civ_00", ...], "out": <result json> }

For every clip of the named kits, at 5 times through the clip, the component-space positions of the pelvis, the head and both feet are
composed from the clip's local bone transforms (AnimationLibrary.get_bone_pose_for_time, root -> pelvis -> spine -> head / thigh -> calf ->
foot). Reported per clip: the body's lean (the angle between pelvis->head and up, degrees), the head's height and the feet's lowest height
above the component origin (cm). A standing clip is upright (lean ~0-15, head ~150-180 cm above the feet); a clip that lays a standing
body on its back shows lean ~90 while its role says Stand. The user saw civilians lie back with a walk still playing (2026-11-22/23).
No verdict here; the caller reads the json.
"""
import json, math, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_CLIP_AUDIT"], encoding="utf-8-sig"))
R = {"clips": [], "log": []}
HEAD = ["root", "pelvis", "spine_01", "spine_02", "spine_03", "spine_04", "spine_05", "neck_01", "neck_02", "head"]
FOOT = lambda s: ["root", "pelvis", f"thigh_{s}", f"calf_{s}", f"foot_{s}"]


def chain(anim, bones, t):
    """component-space location of the chain's last bone (bones missing from the clip use the identity: a gap is logged)"""
    x = unreal.Transform()
    for b in bones:
        try:
            local = unreal.AnimationLibrary.get_bone_pose_for_time(anim, b, t, False)
        except Exception as e:
            R["log"].append(f"{anim.get_name()} {b}: {e}"); local = unreal.Transform()
        x = local.multiply(x)   # child * parent: UE's FTransform order
    return x.translation


def main():
    seen = set()
    for kn in JOB["kits"]:
        kit = unreal.EditorAssetLibrary.load_asset(f"/Game/Hrot/People/{kn}")
        if not kit: R["log"].append(f"no kit {kn}"); continue
        for role, anim in kit.get_editor_property("clips").items():
            if not anim or anim.get_path_name() in seen: continue
            seen.add(anim.get_path_name())
            n = anim.get_play_length()
            rows = []
            for i in range(5):
                t = n * (0.05 + 0.9 * i / 4)
                p, h = chain(anim, HEAD[:2], t), chain(anim, HEAD, t)
                fl, fr = chain(anim, FOOT("l"), t), chain(anim, FOOT("r"), t)
                d = h - p
                lean = math.degrees(math.acos(max(-1.0, min(1.0, d.z / max(1e-6, d.length())))))
                rows.append({"t": round(t, 2), "lean": round(lean, 1), "headZ": round(h.z, 1), "pelvisZ": round(p.z, 1),
                             "feetZ": round(min(fl.z, fr.z), 1), "headAboveFeet": round(h.z - min(fl.z, fr.z), 1)})
            R["clips"].append({"kit": kn, "role": str(role), "clip": anim.get_name(), "seconds": round(n, 2), "poses": rows})


try:
    main()
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
