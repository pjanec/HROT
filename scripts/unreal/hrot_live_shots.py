"""hrot_live_shots.py - CE-4651 (U-19h): pictures of the LIVE client for a check a person reads (civilians up close, a street from above).

    HROT_SHOTS_JOB = { "domain", "partition", "map", "ready", "go", "out" (result json), "shots" (a folder, never in git),
                       "people": [ids], "settle" (seconds after go, default 30) }

The editor plays (PIE, AHrotDpsGameMode) the live station's map and joins; once "go" exists and `settle` seconds pass, for each person id:
a picture from 3 m in front at chest height (the body as drawn: tilt, pose, a held weapon) and one from 25 m above (the street around him:
roads, cars). Bodies are forced to LOD 0 for the pictures (a far LOD collapses bones). The pictures are for the user (SendUserFile).
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_SHOTS_JOB"], encoding="utf-8-sig"))
R = {"log": [], "shots": []}
S = {"handle": None, "phase": "level", "busy": False, "queue": []}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
T0 = time.time()


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-SHOTS] {m}"); R["log"].append(m)


def finish(why):
    log(f"finish: {why}"); R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def gw(): return UES().get_game_world()
def cam(): return unreal.GameplayStatics.get_player_pawn(gw(), 0)


def level():
    if not LES().load_level(JOB["map"]): raise RuntimeError("load_level failed: " + JOB["map"])
    for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
    LES().editor_set_viewport_realtime(True)
    unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
    LES().editor_request_begin_play()
    S.update(phase="join", t=time.time() + 8)


def join():
    if gw() is None: raise RuntimeError("PIE did not start")
    w = unreal.HrotDpsWorld.for_world(gw())
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    open(JOB["ready"], "w").write("ready")
    S.update(w=w, phase="wait_go", t=time.time() + 1); log("joined; ready")


def plan():
    w = S["w"]
    for pid in JOB["people"]:
        a = w.actor_of(int(pid))
        if not a: log(f"person {pid}: not drawn"); continue
        for c in a.get_components_by_class(unreal.SkeletalMeshComponent): c.set_forced_lod(1)   # 1 = LOD 0
        p, yaw = a.get_actor_location(), a.get_actor_rotation().yaw
        fwd = unreal.Vector(math.cos(math.radians(yaw)), math.sin(math.radians(yaw)), 0)
        chest = p + unreal.Vector(0, 0, 120)
        S["queue"].append((f"person{pid}_front", chest + fwd * 300, unreal.Rotator(0, -10, yaw + 180), 60.0, w.person_look(int(pid))))
        S["queue"].append((f"person{pid}_side", chest + unreal.Vector(-fwd.y, fwd.x, 0) * 300, unreal.Rotator(0, -10, yaw - 90), 60.0, ""))
        S["queue"].append((f"street_above_{pid}", p + unreal.Vector(-1500, 0, 2500), unreal.Rotator(0, -58, 0), 75.0, ""))


def shoot():
    if S.get("pending"):
        path, t0 = S["pending"]
        if os.path.exists(path) and os.path.getsize(path) > 0: R["shots"].append(path); log(f"shot {os.path.basename(path)}"); S["pending"] = None
        elif time.time() - t0 > 60: log(f"shot timed out {path}"); S["pending"] = None
        return
    if not S["queue"]: finish("done"); return
    if time.time() < S.get("next", 0): return
    if not S.get("placed"):
        name, at, rot, fov, note = S["queue"][0]
        cam().place_at_rotator(at, rot, fov)
        if note: log(f"{name}: {note[:120]}")
        S["placed"] = True; S["next"] = time.time() + 3; return
    name, *_ = S["queue"].pop(0); S["placed"] = False
    path = os.path.join(JOB["shots"], name + ".png").replace("\\", "/")
    if os.path.exists(path): os.remove(path)
    unreal.SystemLibrary.execute_console_command(gw(), f'HighResShot 1 filename="{path}"')
    S["pending"] = (path, time.time())


def run():
    if S["phase"] == "wait_go":
        if os.path.exists(JOB["go"]): S.update(phase="settle", until=time.time() + float(JOB.get("settle", 30))); log("go")
    elif S["phase"] == "settle":
        if time.time() > S["until"]: plan(); S["phase"] = "shoot"
    elif S["phase"] == "shoot":
        shoot()


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        if time.time() < S.get("t", 0): return
        ph = S["phase"]
        if ph == "level": level()
        elif ph == "join": join()
        else: run()
        if time.time() - T0 > 900: finish("timeout")
    except Exception:
        log(traceback.format_exc()); finish("exception")
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
