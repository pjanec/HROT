"""hrot_blast_check.py - CE-4651 (U-19h): is an explosion SEEN from afar? (the user, live 2026-11-23: "so many big explosions heard but none
seen" · "explosions should have visual always no matter how far - they are big and bright")

    HROT_BLAST_JOB = { "map", "shots" (folder), "out", "eye": [x, y, z] HROT local m, "dir": [dx, dy] (heading), "distancesM": [...],
                       "kind": "TANK_HE" | "ROCKET" | "GRENADE" | "MORTAR", "delay": 0.3 }

PIE on the level (the game mode's camera, no DPS needed); the camera is placed at `eye` looking along `dir`; for each distance a blast is set
off on the ground straight ahead (UHrotExplosionFx.explode) and a picture is taken `delay` s later. The pictures are for a person (never in git).
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_BLAST_JOB"], encoding="utf-8-sig"))
R = {"log": [], "shots": []}
S = {"handle": None, "phase": "level", "busy": False, "queue": list(JOB["distancesM"])}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
ue = lambda p: unreal.Vector(p[0] * 100.0, -p[1] * 100.0, p[2] * 100.0)


def log(m): unreal.log_warning(f"[HROT-BLAST] {m}"); R["log"].append(m)


def finish(why):
    log("finish: " + why); json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    unreal.unregister_slate_post_tick_callback(S["handle"]); unreal.SystemLibrary.quit_editor()


def ground(world, x, y):
    hit = unreal.SystemLibrary.line_trace_single(world, unreal.Vector(x, y, 200000.0), unreal.Vector(x, y, -200000.0),
                                                 unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, False, [], unreal.DrawDebugTrace.NONE, True)
    t = hit.to_tuple() if hit else None
    return t[4].z if t and t[0] else None


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        now = time.time()
        if now < S.get("t", 0): return
        ph = S["phase"]
        if ph == "level":
            LES().load_level(JOB["map"]); LES().editor_set_viewport_realtime(True)
            unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
            LES().editor_request_begin_play(); S.update(phase="place", t=now + 10)
        elif ph == "place":
            gw = UES().get_game_world()
            cam = unreal.GameplayStatics.get_player_pawn(gw, 0)
            dx, dy = JOB["dir"]; yaw = math.degrees(math.atan2(-dy, dx))   # Unreal Y is -north
            cam.place_at_rotator(ue(JOB["eye"]), unreal.Rotator(0, -3, yaw), 70.0)
            S.update(phase="blast", gw=gw, t=now + 6)
        elif ph == "blast":
            if not S["queue"]: finish("done"); return
            d = S["queue"].pop(0); dx, dy = JOB["dir"]; n = math.hypot(dx, dy)
            x, y = JOB["eye"][0] + dx / n * d, JOB["eye"][1] + dy / n * d
            z = ground(S["gw"], x * 100.0, -y * 100.0)
            at = unreal.Vector(x * 100.0, -y * 100.0, (z if z is not None else JOB["eye"][2] * 100.0) + 20.0)
            ok = unreal.HrotExplosionFx.explode(S["gw"], at, unreal.Vector(0, 0, 1), getattr(unreal.HrotBlast, JOB.get("kind", "TANK_HE")))
            log(f"blast at {d} m (ground {'%.1f' % (z / 100.0) if z is not None else '?'} m): spawned {ok}")
            S.update(phase="shoot", d=d, t0=now, delays=list(JOB.get("delays", [JOB.get("delay", 0.3)])))
        elif ph == "shoot":
            if not S["delays"]: S.update(phase="blast", t=now + 6); return
            if now - S["t0"] < S["delays"][0]: return
            dl = S["delays"].pop(0)
            path = os.path.join(JOB["shots"], f"blast_{int(S['d'])}m_{dl:.1f}s.png").replace("\\", "/")
            if os.path.exists(path): os.remove(path)
            unreal.SystemLibrary.execute_console_command(S["gw"], f'HighResShot 1 filename="{path}"')
            R["shots"].append(path)
    except Exception:
        log(traceback.format_exc()); finish("exception")
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
