"""hrot_rotor_check.py - CE-12007: does a helicopter kit's rotor turn while flying? PIE on a level, one AHrotVehicle of the kit set flying,
two pictures `gap` s apart (the blades must have moved) and one with it landed (still).

    HROT_ROTOR_CHECK = { "map", "kit", "shots", "at": [x, y, z] HROT m, "eye": [x, y, z], "gap": 0.15 }
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_ROTOR_CHECK"], encoding="utf-8-sig"))
S = {"phase": "level", "busy": False, "t": 0.0, "handle": None}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
ue = lambda p: unreal.Vector(p[0] * 100.0, -p[1] * 100.0, p[2] * 100.0)


def log(m): unreal.log_warning(f"[HROT-ROTOR-CHECK] {m}")


def shot(name):
    path = os.path.join(JOB["shots"], name + ".png").replace("\\", "/")
    if os.path.exists(path): os.remove(path)
    unreal.SystemLibrary.execute_console_command(S["gw"], f'HighResShot 1 filename="{path}"')


def finish():
    unreal.unregister_slate_post_tick_callback(S["handle"]); unreal.SystemLibrary.quit_editor()


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        now = time.time()
        if now < S["t"]: return
        ph = S["phase"]
        if ph == "level":
            LES().load_level(JOB["map"]); LES().editor_set_viewport_realtime(True)
            for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)   # an unfocused editor ticks slowly
            unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
            # Python cannot spawn into the PIE world: the vehicle goes into the editor's level first (not saved) and play copies it
            v0 = unreal.EditorLevelLibrary.spawn_actor_from_class(unreal.HrotVehicle, ue(JOB["at"]), unreal.Rotator(0, 0, 30))   # (roll, pitch, yaw): a yaw only
            v0.set_editor_property("kit", unreal.load_asset(JOB["kit"])); v0.build()
            LES().editor_request_begin_play(); S.update(phase="spawn", t=now + 10)
        elif ph == "spawn":
            gw = UES().get_game_world(); S["gw"] = gw
            v = unreal.GameplayStatics.get_all_actors_of_class(gw, unreal.HrotVehicle)[0]
            v.set_flying(True); S["v"] = v
            if JOB.get("riders"):   # CE-12009: soldiers in the kit's seats (the seat's clip), no HROT
                log(f"seated {unreal.HrotDpsWorld.for_world(gw).test_seat_people(v, int(JOB['riders']))} riders")
            e, a = JOB["eye"], JOB["at"]
            dx, dy, dz = a[0] - e[0], a[1] - e[1], a[2] - e[2]
            cam = unreal.GameplayStatics.get_player_pawn(gw, 0)
            cam.place_at_rotator(ue(e), unreal.Rotator(0, math.degrees(math.atan2(dz, math.hypot(dx, dy))), math.degrees(math.atan2(-dy, dx))), 70.0)
            log(f"spawned {v.get_name()} flying"); S.update(phase="a", t=now + 4)
        elif ph == "a": shot("rotor_a"); S.update(phase="b", t=now + float(JOB.get("gap", 0.15)))
        elif ph == "b": shot("rotor_b"); S.update(phase="land", t=now + 1.0)
        elif ph == "land": S["v"].set_flying(False); S.update(phase="c", t=now + 1.5)
        elif ph == "c": shot("rotor_landed"); S.update(phase="end", t=now + 2)
        elif ph == "end": log("done"); finish()
    except Exception:
        log(traceback.format_exc()); finish()
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
