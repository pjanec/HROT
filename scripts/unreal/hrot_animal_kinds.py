"""hrot_animal_kinds.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17e): animal KINDS (UHrotAnimalKind) — a body and one
clip per state.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_ANIMAL_JOB = { "kinds": [{"kind", "mesh", "meshYaw",
                                                                                                  "clips": {"IDLE": "<anim>" | {"anim", "loops"}}}],
                                                                                                  "out" }
    a state's clip loops unless it says "loops": false; DEAD defaults to false (the death once, then held). An existing kind is updated
    in place (a map that references it blocks deleting it — U-17c).
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_ANIMAL_JOB"], encoding="utf-8"))
R = {"log": []}
EAL = unreal.EditorAssetLibrary


def log(m):
    unreal.log_warning(f"[HROT-ANIMAL] {m}"); R["log"].append(m)


def seats_of(J):
    """JOB "seats": [{"name", "bone", "loc", "rot", "pose"}] -> FHrotSeat list (U-17j: riders)"""
    out = []
    for d in J.get("seats") or []:
        s = unreal.HrotSeat()
        s.set_editor_property("name", d.get("name", "seat"))
        if d.get("bone"): s.set_editor_property("bone", d["bone"])
        r = d.get("rot", (0, 0, 0))
        s.set_editor_property("offset", unreal.Transform(location=unreal.Vector(*d.get("loc", (0, 0, 0))), rotation=unreal.Rotator(pitch=r[0], yaw=r[1], roll=r[2])))
        if d.get("pose"):
            a = unreal.load_asset(d["pose"])
            if a is None: raise RuntimeError("no seat pose " + d["pose"])
            s.set_editor_property("pose", a)
        out.append(s)
    return out


def main(J):
    path = J["kind"]; folder, name = path.rsplit("/", 1)
    if EAL.does_asset_exist(path):
        kind = unreal.load_asset(path)
    else:
        f = unreal.DataAssetFactory(); f.set_editor_property("data_asset_class", unreal.HrotAnimalKind)
        kind = unreal.AssetToolsHelpers.get_asset_tools().create_asset(name, folder, unreal.HrotAnimalKind, f)
    if kind is None: raise RuntimeError("cannot create or load " + path)
    mesh = unreal.load_asset(J["mesh"])
    if mesh is None: raise RuntimeError("no mesh " + J["mesh"])
    kind.set_editor_property("mesh", mesh)
    kind.set_editor_property("mesh_yaw", float(J.get("meshYaw", -90)))
    clips = {}
    for st, c in J["clips"].items():
        c = {"anim": c} if isinstance(c, str) else c
        anim = unreal.load_asset(c["anim"])
        if anim is None: raise RuntimeError("no clip " + c["anim"])
        clip = unreal.HrotAnimalClip()
        clip.set_editor_property("anim", anim)
        clip.set_editor_property("loops", bool(c.get("loops", st != "DEAD")))
        clips[getattr(unreal.HrotAnimalState, st)] = clip
    kind.set_editor_property("clips", clips)
    if "seats" in J: kind.set_editor_property("seats", seats_of(J))
    EAL.save_loaded_asset(kind, False)
    log(f"{path}: {len(clips)} states ({', '.join(sorted(J['clips']))})")


try:
    for J in JOB["kinds"]: main(J)
    R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
