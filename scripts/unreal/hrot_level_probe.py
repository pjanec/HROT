"""hrot_level_probe.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17): what a map holds — every actor's static / skeletal
mesh components with their world transforms (how a vendor's showcase map assembles a vehicle from its parts).

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_PROBE_JOB = { "map", "out" }
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_PROBE_JOB"], encoding="utf-8"))
R = {"actors": []}


def xf(t):
    l, r, s = t.translation, t.rotation.rotator(), t.scale3d
    return {"loc": [round(l.x, 2), round(l.y, 2), round(l.z, 2)], "rot": [round(r.pitch, 2), round(r.yaw, 2), round(r.roll, 2)],
            "scale": [round(s.x, 3), round(s.y, 3), round(s.z, 3)]}


def main():
    unreal.EditorLoadingAndSavingUtils.load_map(JOB["map"])
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    for a in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor):
        comps = []
        for c in a.get_components_by_class(unreal.MeshComponent):
            mesh = None
            if isinstance(c, unreal.StaticMeshComponent): mesh = c.get_editor_property("static_mesh")
            elif isinstance(c, unreal.SkinnedMeshComponent): mesh = c.get_skinned_asset()
            parent = c.get_attach_parent()
            comps.append({"name": c.get_name(), "mesh": mesh.get_path_name() if mesh else None, "parent": parent.get_name() if parent else None,
                          "world": xf(c.get_world_transform()), "rel": xf(c.get_relative_transform()) if hasattr(c, "get_relative_transform") else None})
        if comps: R["actors"].append({"label": a.get_actor_label(), "class": a.get_class().get_name(), "comps": comps})


try:
    main(); R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
