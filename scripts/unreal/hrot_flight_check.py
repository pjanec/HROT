"""hrot_flight_check.py - CE-4651 (U-19h): WHERE and WHEN does the camera flight freeze on a terrain (the user, live Stechovice 2026-11-24: "the
camera flight is frozen if I come somewhere for the first time ... presumably some explosions or sounds").

    HROT_FLIGHT_JOB = { "domain", "partition", "map", "out", "ready", "go", "speed" (m/s, 60), "agl" (m above ground, 40),
                        "path": [[x, y], ...] HROT local m, "hitchMs": 80 }

The editor plays (PIE) the live station's map and joins; once "go" exists the free camera flies the path at `speed`, `agl` over the ground,
looking ahead and a little down. Every frame longer than hitchMs is logged with the camera position, the time into the flight and what the
DPS world counted that frame (new actors, rounds, blasts) - so a freeze can be tied to a place (first visit) or to an event. Facts only.
Run the editor with -trace=default,loadtime,assetloadtime to attribute the hitches afterwards in Unreal Insights.
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_FLIGHT_JOB"], encoding="utf-8-sig"))
R = {"log": [], "hitches": [], "frames": 0, "flight_s": 0.0}
S = {"handle": None, "phase": "level", "busy": False, "t": 0.0}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
T0 = time.time()


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-FLIGHT] {m}"); R["log"].append(m)


def finish(why):
    log("finish: " + why); R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def ground(gw, x, y):
    hit = unreal.SystemLibrary.line_trace_single(gw, unreal.Vector(x * 100, -y * 100, 300000.0), unreal.Vector(x * 100, -y * 100, -300000.0),
                                                 unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, False, [], unreal.DrawDebugTrace.NONE, True)
    t = hit.to_tuple() if hit else None
    return t[4].z / 100.0 if t and t[0] else None


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        now = time.time()
        ph = S["phase"]
        if ph == "level":
            if not LES().load_level(JOB["map"]): raise RuntimeError("load_level failed")
            for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
            LES().editor_set_viewport_realtime(True)
            unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
            LES().editor_request_begin_play(); S.update(phase="join", t=now + 8)
        elif ph == "join" and now >= S["t"]:
            gw = UES().get_game_world()
            w = unreal.HrotDpsWorld.for_world(gw)
            if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
            open(JOB["ready"], "w").write("ready"); S.update(gw=gw, w=w, cam=unreal.GameplayStatics.get_player_pawn(gw, 0), phase="wait_go"); log("joined; ready")
        elif ph == "wait_go":
            if os.path.exists(JOB["go"]):
                S.update(phase="fly", leg=0, s=0.0, start=now, last=now, z=None); log("go: flying")
        elif ph == "fly":
            frame = now - S["last"]; S["last"] = now; R["frames"] += 1
            path, speed = JOB["path"], float(JOB.get("speed", 60))
            a, b = path[S["leg"]], path[S["leg"] + 1]
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            S["s"] += speed * min(frame, 0.25)   # a freeze does not teleport the camera: at most 0.25 s of travel per frame
            while S["s"] >= L:
                S["s"] -= L; S["leg"] += 1
                if S["leg"] >= len(path) - 1: R["flight_s"] = now - S["start"]; finish("done"); return
                a, b = path[S["leg"]], path[S["leg"] + 1]; L = math.hypot(b[0] - a[0], b[1] - a[1])
            f = S["s"] / L; x, y = a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f
            g = ground(S["gw"], x, y)
            zt = (g if g is not None else (S["z"] or 300.0)) + float(JOB.get("agl", 40))
            S["z"] = zt if S["z"] is None else S["z"] + (zt - S["z"]) * min(1.0, frame * 1.5)
            yaw = math.degrees(math.atan2(-(b[1] - a[1]), b[0] - a[0]))
            S["cam"].place_at_rotator(unreal.Vector(x * 100, -y * 100, S["z"] * 100), unreal.Rotator(0, -12, yaw), 70.0)
            if frame * 1000.0 > float(JOB.get("hitchMs", 80)) and R["frames"] > 3:
                h = {"t": round(now - S["start"], 2), "ms": round(frame * 1000.0), "x": round(x), "y": round(y), "leg": S["leg"]}
                R["hitches"].append(h); log(f"HITCH {h}")
            if now - T0 > 1500: finish("timeout")
    except Exception:
        log(traceback.format_exc()); finish("exception")
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
