"""hrot_dps_travel.py - CE-4651 (U-18r): the IG follows the station's TERRAIN (Session.Terrain -> a level), reported truthfully in ClientWorld,
over real DDS against the plugin's FAKE STATION. Run in the full editor (-ExecutePythonScript), PLAY, a private domain.

    HROT_DPS_TRAVEL_JOB = { "domain", "partition", "out" }

Two scratch levels (DpsTravelA, DpsTravelB; the runner deletes them before Unreal starts). Checks: terrain alpha -> Ready in A; terrain beta ->
Loading (the level load) -> Ready in B, the participant kept (the ClientWorld writer never goes away); an unknown terrain -> Failed with the
reason; beta again in a new epoch -> Ready without a load.
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DPS_TRAVEL_JOB"], encoding="utf-8-sig"))
R = {"log": [], "checks": []}
S = {"handle": None, "phase": "levels", "busy": False}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()
A, B = "/Game/Maps/DpsTravelA", "/Game/Maps/DpsTravelB"


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-TRAVEL] {m}"); R["log"].append(m)


def check(name, ok, detail):
    R["checks"].append({"name": name, "ok": bool(ok), "detail": detail}); log(f"{'PASS' if ok else 'FAIL'} {name}: {detail}")


def finish(ok, why=""):
    R["ok"] = ok and all(c["ok"] for c in R["checks"]); R["why"] = why
    if "sub" in S: R["worldLog"] = list(S["sub"].fake_station_client_world_log())
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def make(path, yaw):
    if not LES().new_level(path): raise RuntimeError("new_level failed: " + path)
    floor = EAS().spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0, 0, 0))
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane")); floor.set_actor_scale3d(unreal.Vector(400, 400, 1))
    EAS().spawn_actor_from_class(unreal.DirectionalLight, unreal.Vector(0, 0, 1000), unreal.Rotator(pitch=-40, yaw=yaw, roll=0))
    unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
    if not LES().save_current_level(): raise RuntimeError("save failed: " + path)


def levels():
    make(B, 120); make(A, 60)   # A last: Play starts in it
    for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
    LES().editor_set_viewport_realtime(True)
    LES().editor_request_begin_play()
    S.update(phase="join", t=time.time() + 8)


def world():
    gw = UES().get_game_world()
    return gw, (unreal.HrotDpsWorld.for_world(gw) if gw else None)


def join():
    gw, w = world()
    if w is None: raise RuntimeError("PIE did not start")
    sub = unreal.get_engine_subsystem(unreal.HrotDpsSubsystem)
    sub.set_editor_property("terrain_levels", {"alpha": A, "beta": B})
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    sub.fake_station_start(0.0)
    S.update(sub=sub, phase="step", step=0, t=time.time() + 1.0)
    log(f"joined in {gw.get_name()}")


def said(): return S["sub"].fake_station_client_world()


STEPS = [
    ("alpha", 1, lambda cw: " epoch 1 Ready " in cw and "DpsTravelA" in cw, 6, "terrain alpha: Ready in its level (A, where Play started)"),
    ("beta", 2, lambda cw: " epoch 2 Ready " in cw and "DpsTravelB" in cw, 90, "terrain beta: the IG loads level B and reports Ready for epoch 2 there"),
    ("nowhere", 3, lambda cw: " epoch 3 Failed " in cw, 8, "an unknown terrain: Failed for epoch 3 (no level)"),
    ("beta", 4, lambda cw: " epoch 4 Ready " in cw and "DpsTravelB" in cw, 8, "beta again in a new epoch: Ready without a load"),
]


def step():
    i = S["step"]
    if i >= len(STEPS):
        logw = list(S["sub"].fake_station_client_world_log())
        check("Loading was reported for epoch 2 while the level loaded", any(e.startswith("2 Loading") and "DpsTravelB" in e for e in logw), " | ".join(logw))
        check("the IG's ClientWorld writer never went away (the participant kept across the load)", S["sub"].fake_station_client_world_lost() == 0,
              f"lost {S['sub'].fake_station_client_world_lost()}")
        fails = [e for e in logw if " Failed " in e]
        check("the Failed carries the reason", any("no Unreal level for terrain 'nowhere'" in e for e in fails), " | ".join(fails))
        LES().editor_request_end_play(); S.update(phase="end", t=time.time() + 3); return
    terrain, ep, ok, limit, name = STEPS[i]
    if not S.get("sent"):
        S["sub"].fake_station_session(ep, 10.0 * ep, terrain); S.update(sent=time.time()); log(f"Session epoch {ep} terrain '{terrain}'"); return
    cw = said()
    if ok(cw):
        gw, _ = world()
        check(name, True, f"{cw} after {time.time() - S['sent']:.1f} s; Play world {gw.get_name() if gw else None}")
        S.update(step=i + 1, sent=None); return
    if time.time() - S["sent"] > limit:
        check(name, False, f"last ClientWorld: {cw} | log {list(S['sub'].fake_station_client_world_log())}"); S.update(step=i + 1, sent=None)


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        p = S["phase"]
        if p == "levels": levels()
        elif p == "join" and time.time() >= S["t"]: join()
        elif p == "step" and time.time() >= S["t"]: step()
        elif p == "end" and time.time() >= S["t"]: finish(True)
    except Exception:
        finish(False, traceback.format_exc())
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
