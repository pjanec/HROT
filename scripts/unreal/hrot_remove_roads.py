"""hrot_remove_roads.py - CE-4651 (U-19h): remove HROT's road-scene meshes (the Road_* actors look.py / hrot_add_roads.py place) from a look level.

    UnrealEditor.exe <project> -ExecutePythonScript=<this> -unattended -nosplash      HROT_ROADS_JOB = { "level", "out" }

The user, live on Stechovice 2026-11-23: "the brown asphalt carpet on the top is useless on stechovice terrain - wrong height and variously
broken" - the ground material's own road mask (the dirt road at the right height) stays. Only the level's Road_* actors go; the mesh assets
stay on disk (another rebuild may want them).
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_ROADS_JOB"], encoding="utf-8-sig"))
R = {"log": []}
try:
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not les.load_level(JOB["level"]): raise RuntimeError("load_level failed")
    eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    old = [a for a in eas.get_all_level_actors() if a.get_actor_label().startswith("Road_")]
    for a in old: eas.destroy_actor(a)
    R["log"].append(f"removed {len(old)} Road_* actors")
    les.save_current_level()
except Exception:
    R["log"].append(traceback.format_exc())
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
unreal.SystemLibrary.quit_editor()
