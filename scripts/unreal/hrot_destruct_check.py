"""hrot_destruct_check.py - CE-4651 (U-20): is HROT's building damage drawn? Without HROT: the client's own reader is fed test damage
(UHrotBuildingDamage.test_damage / test_break_window - the same path a DPS_BuildingDamage sample takes) and each act is photographed
before and after.

    HROT_DESTRUCT_JOB = { "map", "terrain", "shots" (folder), "out",
                          "acts": [ { "name", "eye": [x, y, z] HROT m, "look": [x, y, z],
                                      "breach": [key, panel, from, to, bottom, top] | "window": [key, panel, opening] | "collapse": key,
                                      "live": bool } ] }

PIE on the level (the DPS game mode's camera, no DPS joined). Pictures are for a person (never in git).
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DESTRUCT_JOB"], encoding="utf-8-sig"))
R = {"log": [], "acts": []}
S = {"handle": None, "phase": "level", "busy": False, "acts": list(JOB["acts"]), "t": 0}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
ue = lambda p: unreal.Vector(p[0] * 100.0, -p[1] * 100.0, p[2] * 100.0)


def log(m): unreal.log_warning(f"[HROT-DESTRUCT] {m}"); R["log"].append(m)


def finish(why):
    log("finish: " + why); json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    unreal.unregister_slate_post_tick_callback(S["handle"]); unreal.SystemLibrary.quit_editor()


def shot(name):
    path = os.path.join(JOB["shots"], f"{name}.png").replace("\\", "/")
    if os.path.exists(path): os.remove(path)
    unreal.SystemLibrary.execute_console_command(S["gw"], f'HighResShot 1 filename="{path}"')
    return path


def aim(act):
    e, l = act["eye"], act["look"]
    dx, dy, dz = l[0] - e[0], l[1] - e[1], l[2] - e[2]
    yaw = math.degrees(math.atan2(-dy, dx)); pitch = math.degrees(math.atan2(dz, math.hypot(dx, dy)))
    S["cam"].place_at_rotator(ue(e), unreal.Rotator(0, pitch, yaw), 70.0)


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        now = time.time()
        if now < S["t"]: return
        ph = S["phase"]
        if ph == "level":
            LES().load_level(JOB["map"]); LES().editor_set_viewport_realtime(True)
            for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
            unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
            LES().editor_request_begin_play(); S.update(phase="setup", t=now + 10)
        elif ph == "setup":
            gw = UES().get_game_world()
            b = unreal.HrotDpsWorld.for_world(gw).buildings()
            b.use_terrain(gw, JOB["terrain"])
            S.update(gw=gw, b=b, cam=unreal.GameplayStatics.get_player_pawn(gw, 0), phase="next", t=now + 1)
        elif ph == "next":
            if not S["acts"]: finish("done"); return
            act = S["acts"].pop(0); S["act"] = act; aim(act); S.update(phase="before", t=now + 4)
        elif ph == "before":
            act = S["act"]; R["acts"].append({"name": act["name"], "before": shot(act["name"] + "_before")})
            S.update(phase="damage", t=now + 2)
        elif ph == "damage":
            act, b, live = S["act"], S["b"], bool(S["act"].get("live", False))
            t0 = time.time()
            if "breach" in act:
                k, p, f, to, bo, top = act["breach"]; ok = b.test_damage(k, p, f, to, bo, top, False, live)
            elif "window" in act:
                k, p, o = act["window"]; ok = b.test_break_window(k, p, o, live)
            elif "decals" in act:   # a probe: scorch decals placed straight from Python (location HROT m, rotation pitch/yaw/roll)
                mat = unreal.load_asset("/Game/Hrot/Fx/M_HrotScorch")
                for d in act["decals"]:
                    c = unreal.GameplayStatics.spawn_decal_at_location(S["gw"], mat, unreal.Vector(*d["size"]), ue(d["at"]), unreal.Rotator(d["rot"][2], d["rot"][0], d["rot"][1]), 0.0)
                    log(f"decal {d} -> {c is not None}")
                ok = True
            else:
                ok = b.test_damage(act["collapse"], -1, 0, 0, 0, 0, True, live)
            ms = (time.time() - t0) * 1000.0
            log(f"{act['name']}: applied {ok} in {ms:.1f} ms; houses taken out {b.houses_taken_out()} of {b.indexed_houses()}")
            R["acts"][-1].update(ok=bool(ok), ms=round(ms, 1))
            S.update(phase="after", t=now + float(act.get("wait", 3)))
        elif ph == "after":
            R["acts"][-1]["after"] = shot(S["act"]["name"] + "_after"); S.update(phase="next", t=now + 2)
    except Exception:
        log(traceback.format_exc()); finish("exception")
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
