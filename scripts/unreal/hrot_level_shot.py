"""hrot_level_shot.py - CE-4651 (U-19h): pictures of an Unreal look level from given HROT-local viewpoints (a check a person reads; never in git).

    UnrealEditor.exe <project> -ExecutePythonScript=<this> -unattended -nosplash
        HROT_SHOT_JOB = { "level", "shots" (folder), "views": [{ "name", "eye": [x, y, z] (HROT local m), "at": [x, y, z] (HROT local m) }],
                          "settle" (s, default 15), "out" }
The client's mapping: Unreal (x, -y, z) * 100. One view per call of the tick: a camera placed, the level settles, a high-res shot.
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_SHOT_JOB"], encoding="utf-8-sig"))
R = {"log": [], "shots": []}
S = {"handle": None, "queue": list(JOB["views"]), "phase": "load", "t": 0, "busy": False}
ue = lambda p: unreal.Vector(p[0] * 100.0, -p[1] * 100.0, p[2] * 100.0)


def finish():
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    unreal.unregister_slate_post_tick_callback(S["handle"])
    unreal.SystemLibrary.quit_editor()


def tick(dt):
    if S["busy"]: return   # a level load ticks Slate again: never re-enter
    S["busy"] = True
    try:
        now = time.time()
        if now < S["t"]: return
        if S["phase"] == "load":
            unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level(JOB["level"])
            unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).editor_set_viewport_realtime(True)
            S.update(phase="place", t=now + 5); return
        if S["phase"] == "place":
            if not S["queue"]: finish(); return
            v = S["queue"][0]
            eye, at = ue(v["eye"]), ue(v["at"]); d = at - eye
            rot = unreal.Rotator(0, math.degrees(math.atan2(d.z, math.hypot(d.x, d.y))), math.degrees(math.atan2(d.y, d.x)))
            cam = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).spawn_actor_from_class(unreal.CameraActor, eye, rot)
            cam.camera_component.set_editor_property("field_of_view", 70.0)
            unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).set_level_viewport_camera_info(eye, rot)
            S.update(phase="shoot", cam=cam, t=now + float(JOB.get("settle", 15))); return
        if S["phase"] == "shoot":
            v = S["queue"].pop(0)
            path = os.path.join(JOB["shots"], v["name"] + ".png").replace("\\", "/")
            if os.path.exists(path): os.remove(path)
            unreal.AutomationLibrary.take_high_res_screenshot(1920, 1080, path, S["cam"])
            R["shots"].append(path)
            S.update(phase="wait", path=path, t=now + 2); return
        if S["phase"] == "wait":
            if os.path.exists(S["path"]) or now - S.get("w0", now) > 60:
                unreal.get_editor_subsystem(unreal.EditorActorSubsystem).destroy_actor(S["cam"]); S.pop("w0", None)
                S.update(phase="place", t=now + 1)
            else: S.setdefault("w0", now)
    except Exception:
        R["log"].append(traceback.format_exc()); finish()
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
