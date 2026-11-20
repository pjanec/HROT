"""hrot_level_probe.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17): what a map holds — every actor's static / skeletal
mesh components with their world transforms (how a vendor's showcase map assembles a vehicle from its parts).

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_PROBE_JOB = { "map", "blueprints"?, "out" }
    blueprints: Blueprint paths — each spawned at the origin (label = its name) and only those reported: where a vendor's
    vehicle Blueprint puts its body, wheels and glass
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
    actors = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor)
    if JOB.get("blueprints"):
        eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
        actors = []
        for bp in JOB["blueprints"]:
            cls = unreal.EditorAssetLibrary.load_blueprint_class(bp)
            a = eas.spawn_actor_from_class(cls, unreal.Vector(0, 0, 0), unreal.Rotator(0, 0, 0)) if cls else None
            if a: a.set_actor_label(bp.rsplit("/", 1)[-1]); actors.append(a)
            else: R.setdefault("missing", []).append(bp)
    for a in actors:
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
