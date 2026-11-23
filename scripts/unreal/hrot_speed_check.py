"""hrot_speed_check.py - CE-4651 (U-19h): how fast does HROT move a POSSESSED soldier, walking and running (the user, live on Stechovice
2026-11-23: "the character movement was dead slow on walk and felt like walking on run (with SHIFT)").

    HROT_SPEED_JOB = { "domain", "partition", "map", "out", "ready", "go", "soldier" (id), "seconds" (per gait, default 12) }

The editor plays (PIE) the live station's map and joins; once "go" exists it possesses the soldier, holds W for `seconds` (walk), then W +
the run key (PlayerRun) for `seconds`, then stops. Every second: the soldier's position as HROT sent it (the client's actor, which is HROT's
pose) -> the horizontal speed over that second, and the slope (rise / run). Facts only - the client sends only W and the run flag; the speed is HROT's.
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_SPEED_JOB"], encoding="utf-8-sig"))
R = {"log": [], "samples": []}
S = {"handle": None, "phase": "level", "busy": False}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
T0 = time.time()


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-SPEED] {m}"); R["log"].append(m)


def finish(why):
    log(f"finish: {why}"); R["why"] = why
    for g in ("walk", "run"):
        v = [s["speed"] for s in R["samples"] if s["gait"] == g and s["t_in"] >= 3]   # after the first 3 s (acceleration)
        R[g] = {"meanSpeed": round(sum(v) / len(v), 2) if v else None, "maxSpeed": round(max(v), 2) if v else None, "n": len(v)}
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def gw(): return UES().get_game_world()
def cam(): return unreal.GameplayStatics.get_player_pawn(gw(), 0)


def run():
    ph, now = S["phase"], time.time()
    if ph == "level":
        if not LES().load_level(JOB["map"]): raise RuntimeError("load_level failed")
        for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
        LES().editor_set_viewport_realtime(True)
        unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
        LES().editor_request_begin_play(); S.update(phase="join", t=now + 8)
    elif ph == "join":
        w = unreal.HrotDpsWorld.for_world(gw())
        if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
        open(JOB["ready"], "w").write("ready"); S.update(w=w, phase="wait_go"); log("joined; ready")
    elif ph == "wait_go":
        if os.path.exists(JOB["go"]): cam().possess(int(JOB["soldier"])); S.update(phase="possess", until=now + 60); log("go")
    elif ph == "possess":
        st = unreal.HrotDpsStation.for_world(gw())
        if st and st.is_playing() and st.played_unit() == int(JOB["soldier"]):
            log("possessed"); S.update(phase="settle", until=now + 4)
        elif now > S["until"]: finish("possession not granted")
    elif ph == "settle":
        if now > S["until"]: S.update(phase="walk", start=now, next_s=now + 1, prev=None); cam().player_move(1.0, 0.0); log("walk")
    elif ph in ("walk", "run"):
        if now >= S["next_s"]:
            a = S["w"].actor_of(int(JOB["soldier"]))
            p = a.get_actor_location() if a else None
            if p is not None and S["prev"] is not None:
                q, tq = S["prev"]
                d = math.hypot(p.x - q.x, p.y - q.y) / 100.0
                rise = (p.z - q.z) / 100.0
                R["samples"].append({"gait": ph, "t_in": round(now - S["start"], 1), "speed": round(d / (now - tq), 2),
                                     "slope": round(rise / d, 2) if d > 0.05 else None, "look": S["w"].person_look(int(JOB["soldier"]))[:40]})
            S["prev"] = (p, now) if p is not None else None; S["next_s"] = now + 1
        if now - S["start"] > float(JOB.get("seconds", 12)):
            if ph == "walk": S.update(phase="run", start=now, prev=None); cam().player_run(True); log("run")
            else: cam().player_run(False); cam().player_move(0.0, 0.0); finish("done")


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        if time.time() >= S.get("t", 0): run()
        if time.time() - T0 > 900: finish("timeout")
    except Exception:
        log(traceback.format_exc()); finish("exception")
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
