"""hrot_follow_shots.py - CE-12010/12012: pictures of a LIVE unit, the camera kept beside it (the door-gun UH-60's tracer streams, the Apache's
rockets). The editor plays (PIE) the station's map, joins, and once "go" exists follows each target in turn.

    HROT_FOLLOW_JOB = { "domain", "partition", "map", "out", "ready", "go", "shots",
                        "targets": [{ "name", "id", "seconds", "every", "offset": [back, side, up] m (in the unit's frame), "lookUp": m }] }

Facts only: each picture's time and the unit's position; the client's counters (tracer from a mounted gun, rounds, rocket fires) are in the log.
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_FOLLOW_JOB"], encoding="utf-8-sig"))
R = {"log": [], "shots": []}
S = {"handle": None, "phase": "level", "busy": False, "t": 0.0}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
T0 = time.time()


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-FOLLOW] {m}"); R["log"].append(m)


def finish(why):
    log("finish: " + why)
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        now = time.time(); ph = S["phase"]
        if ph == "level":
            if not LES().load_level(JOB["map"]): raise RuntimeError("load_level failed")
            for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
            LES().editor_set_viewport_realtime(True)
            unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
            LES().editor_request_begin_play(); S.update(phase="join", t=now + 8)
        elif ph == "join" and now >= S["t"]:
            gw = UES().get_game_world(); w = unreal.HrotDpsWorld.for_world(gw)
            if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
            open(JOB["ready"], "w").write("ready"); S.update(gw=gw, w=w, cam=unreal.GameplayStatics.get_player_pawn(gw, 0), phase="wait_go"); log("joined")
        elif ph == "wait_go":
            if os.path.exists(JOB["go"]): S.update(phase="follow", ti=0, start=now, next=now); log("go")
        elif ph == "follow":
            tg = JOB["targets"][S["ti"]]
            a = S["w"].actor_of(int(tg["id"]))
            if a:
                L = a.get_actor_location(); yaw = a.get_actor_rotation().yaw
                back, side, up = tg.get("offset", [18, 10, 4])
                c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
                eye = unreal.Vector(L.x - c * back * 100 - s_ * side * 100, L.y - s_ * back * 100 + c * side * 100, L.z + up * 100)
                look = unreal.Vector(L.x, L.y, L.z + tg.get("lookUp", 0) * 100)
                d = look - eye
                S["cam"].place_at_rotator(eye, unreal.Rotator(0, math.degrees(math.atan2(d.z, math.hypot(d.x, d.y))), math.degrees(math.atan2(d.y, d.x))), 70.0)
                if now >= S["next"]:
                    k = len([x for x in R["shots"] if x["target"] == tg["name"]])
                    path = os.path.join(JOB["shots"], f"{tg['name'].replace(' ', '_')}_{k:02d}.png").replace("\\", "/")
                    unreal.SystemLibrary.execute_console_command(S["gw"], f'HighResShot 1 filename="{path}"')
                    R["shots"].append({"target": tg["name"], "t": round(now - S["start"], 1), "path": path, "at": [round(L.x / 100), round(-L.y / 100), round(L.z / 100)]})
                    S["next"] = now + float(tg.get("every", 3))
            if now - S["start"] > float(tg.get("seconds", 60)):
                S["ti"] += 1; S.update(start=now, next=now)
                if S["ti"] >= len(JOB["targets"]): finish("done"); return
        if now - T0 > 1500: finish("timeout")
    except Exception:
        log(traceback.format_exc()); finish("exception")
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
