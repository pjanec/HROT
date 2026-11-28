"""hrot_kit_audit.py - CE-4651 (U-19g): what each person kit's clips ARE, read from the assets (no live run).

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_KIT_AUDIT = <result json>

For each kit (/Game/Hrot/People/DA_Person_*): the body (BodyClass / BodyMesh) and the skeleton(s) its skeletal meshes use; every clip role
-> the clip, ITS skeleton, its length and root motion; whether a death role (Death.Stand / Crouch / Prone, Dying) exists. A clip whose
skeleton differs from the body's plays on bone NAMES only (or not at all), which is how a body leans or sinks - the facts for the
civilians who lie back / sink (user, 2026-11-22/23). No verdict here; the json is read by the caller.
"""
import json, os, traceback
import unreal

OUT = os.environ["HROT_KIT_AUDIT"]
R = {"kits": [], "log": []}
DEATH = ("Death.Stand", "Death.Crouch", "Death.Prone", "Dying")


def skel_of(mesh):
    try:
        s = mesh.get_editor_property("skeleton")
        return s.get_path_name() if s else None
    except Exception as e:
        return f"? {e}"


def body_skeletons(kit):
    out = []
    m = kit.get_editor_property("body_mesh")
    if m: out.append({"from": "BodyMesh", "mesh": m.get_path_name(), "skeleton": skel_of(m)})
    cls_ptr = kit.get_editor_property("body_class")
    try:
        path = str(cls_ptr.get_asset_name()) if hasattr(cls_ptr, "get_asset_name") else None
    except Exception:
        path = None
    try:
        cls = unreal.SystemLibrary.load_class_asset_blocking(cls_ptr) if cls_ptr else None
    except Exception as e:
        cls = None; R["log"].append(f"body class load: {e}")
    if cls:
        cdo = unreal.get_default_object(cls)
        bp = unreal.EditorAssetLibrary.load_asset(cls.get_path_name().rsplit(".", 1)[0])
        comps = []
        try:
            sub = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
            handles = sub.k2_gather_subobject_data_for_blueprint(bp) if bp else []
            for h in handles:
                d = unreal.SubobjectDataBlueprintFunctionLibrary.get_data(h)
                o = unreal.SubobjectDataBlueprintFunctionLibrary.get_object(d)
                if isinstance(o, unreal.SkeletalMeshComponent):
                    sm = o.get_editor_property("skeletal_mesh_asset") if hasattr(o, "skeletal_mesh_asset") else o.get_editor_property("skeletal_mesh")
                    comps.append({"from": "BodyClass/" + o.get_name(), "mesh": sm.get_path_name() if sm else None, "skeleton": skel_of(sm) if sm else None})
        except Exception as e:
            R["log"].append(f"components of {cls.get_name()}: {e}")
        out += comps
        out.append({"from": "BodyClass", "class": cls.get_path_name()})
    return out


def main():
    paths = unreal.EditorAssetLibrary.list_assets("/Game/Hrot/People", recursive=False, include_folder=False)
    for p in sorted(paths):
        name = p.rsplit("/", 1)[-1].split(".")[0]
        if not name.startswith("DA_Person_"): continue
        kit = unreal.EditorAssetLibrary.load_asset(p)
        if not kit: continue
        k = {"kit": name, "bodies": body_skeletons(kit), "clips": {}}
        skels = {b.get("skeleton") for b in k["bodies"] if b.get("skeleton")}
        clips = kit.get_editor_property("clips")
        for role, clip in clips.items():
            role = str(role)
            if not clip: k["clips"][role] = None; continue
            cs = clip.get_editor_property("skeleton")
            cs = cs.get_path_name() if cs else None
            k["clips"][role] = {"clip": clip.get_path_name(), "skeleton": cs, "sameSkeleton": cs in skels,
                                "seconds": round(clip.get_editor_property("sequence_length") if hasattr(clip, "sequence_length") else clip.get_play_length(), 2),
                                "rootMotion": bool(clip.get_editor_property("enable_root_motion"))}
        k["deathRoles"] = [d for d in DEATH if k["clips"].get(d)]
        R["kits"].append(k)


try:
    main()
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(OUT, "w", encoding="utf-8"), indent=1)
