"""hrot_drive_check.py - CE-4651 (U-19g): drive a possessed soldier to a vehicle LIVE and press use, to read what HROT offers and does.

    HROT_DRIVE_JOB = { "domain", "partition", "map", "out" (result json), "ready" (a file written once joined),
                       "go" (a file the orchestrator writes once the scenario plays), "soldier" (id), "vehicle" (id),
                       "stopAtM" (default 5), "aim" ("body": the vehicle's centre, "roof": 1.5 m above it) }

The editor plays (PIE, AHrotDpsGameMode) the live station's map; the client joins; once "go" exists it possesses the soldier, turns and
walks him to the vehicle, aims at it and taps use, holds W (does the VEHICLE move once the slot hands it over), then taps use again
(get out) - and records. Facts come from the client's log lines (own unit use / seat /
player use press) and from this script's own samples: the slot's played unit, the soldier's distance, the aim target.
No verdict is computed here - the run is a measurement for a note (the user, 2026-11-23: "can you somehow script it yourself?").
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DRIVE_JOB"], encoding="utf-8-sig"))
R = {"log": [], "samples": []}
S = {"handle": None, "phase": "level", "busy": False}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
T0 = time.time()


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-DRIVE] {m}"); R["log"].append(m)


def finish(why):
    log(f"finish: {why}")
    R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def level():
    if not LES().load_level(JOB["map"]): raise RuntimeError("load_level failed: " + JOB["map"])
    for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
    LES().editor_set_viewport_realtime(True)
    unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
    LES().editor_request_begin_play()
    S.update(phase="join", t=time.time() + 8)


def join():
    gw = UES().get_game_world()
    if gw is None: raise RuntimeError("PIE did not start")
    w = unreal.HrotDpsWorld.for_world(gw)
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    open(JOB["ready"], "w").write("ready")
    S.update(w=w, phase="wait_go", t=time.time() + 1)
    log("joined; ready")


def gw(): return UES().get_game_world()
def cam(): return unreal.GameplayStatics.get_player_pawn(gw(), 0)
def station(): return unreal.HrotDpsStation.for_world(gw())


def loc(eid):
    a = S["w"].actor_of(eid)
    return a.get_actor_location() if a else None


def sample(tag):
    c = cam(); st = station()
    s = {"t": round(time.time() - T0, 1), "tag": tag, "slot": st.slot_state() if st else "-", "played": st.played_unit() if st else 0,
         "aim": c.player_aim_target() if c else 0}
    a, b = loc(int(JOB["soldier"])), loc(int(JOB["vehicle"]))
    if a and b: s["distM"] = round((a - b).length() / 100.0, 1)
    R["samples"].append(s); log(json.dumps(s))
    return s


def look_at(target_cm):
    """turn the player's view toward a point (closed loop: the yaw sign of PlayerLook is measured, not assumed)"""
    c = cam()
    eye = c.get_actor_location()
    rot = c.view_rotation()
    d = target_cm - eye
    want_yaw = math.degrees(math.atan2(d.y, d.x))
    want_pitch = math.degrees(math.atan2(d.z, math.hypot(d.x, d.y)))
    dy = (want_yaw - rot.yaw + 540.0) % 360.0 - 180.0
    dp = want_pitch - rot.pitch
    sgn = S.get("yaw_sign", 1.0)
    # a fraction of the error per tick: the view answers a tick late, so the full error each tick swings past the target and back
    # (live 2026-11-23: the crosshair flickered on / off the vehicle and the press went out with no target)
    c.player_look(sgn * max(-20.0, min(20.0, dy * 0.3)), S.get("pitch_sign", 1.0) * max(-10.0, min(10.0, dp * 0.3)))
    return dy, dp


def run():
    ph = S["phase"]
    if ph == "wait_go":
        if not os.path.exists(JOB["go"]): return
        st = station()
        log(f"go; offers {list(st.offer_ids()) if st else '-'}")
        cam().possess(int(JOB["soldier"]))
        S.update(phase="possess", until=time.time() + 30)
    elif ph == "possess":
        st = station()
        if st and st.is_playing() and st.played_unit() == int(JOB["soldier"]):
            sample("possessed")
            # measure PlayerLook's yaw sign once: a +10 deg nudge, then read the view
            S["yaw0"] = cam().view_rotation().yaw; cam().player_look(10.0, 0.0)
            S.update(phase="calib", t=time.time() + 0.6)
        elif time.time() > S["until"]: finish(f"possession not granted: {st.slot_state() if st else '-'} {st.slot_reason() if st else ''}")
    elif ph == "calib":
        d = (cam().view_rotation().yaw - S["yaw0"] + 540.0) % 360.0 - 180.0
        S["yaw_sign"] = 1.0 if d >= 0 else -1.0
        log(f"PlayerLook yaw sign {S['yaw_sign']} (moved {d:.1f} deg for +10)")
        S.update(phase="walk", until=time.time() + 90, next_s=0)
    elif ph == "walk":
        # steer by his ACTUAL motion (live 2026-11-23: steering by the camera's yaw walked him away, 12 m -> 114 m): every 0.5 s the angle
        # from where he moved to where the vehicle is turns him; the sign of PlayerLook is the calibrated one
        a, b = loc(int(JOB["soldier"])), loc(int(JOB["vehicle"]))
        if not a or not b: return
        dist = (a - b).length() / 100.0
        now = time.time()
        if now > S["next_s"]: sample("walk"); S["next_s"] = now + 2
        if dist <= float(JOB.get("stopAtM", 5)):
            cam().player_move(0.0, 0.0); S.update(phase="aim", until=now + 4); log(f"at the vehicle: {dist:.1f} m"); return
        cam().player_move(1.0, 0.0)
        if now >= S.get("next_turn", 0):
            prev = S.get("prev")
            if prev is not None:
                m = a - prev
                if m.length() > 20.0:   # moved > 0.2 m: a heading to correct
                    d = b - a
                    err = (math.degrees(math.atan2(d.y, d.x) - math.atan2(m.y, m.x)) + 540.0) % 360.0 - 180.0
                    if "turn_sign" not in S and abs(err) > 20:   # the first big correction measures the sign
                        S["turn_sign"] = 1.0; S["err0"] = abs(err)
                    elif "err0" in S and "turn_fixed" not in S:
                        if abs(err) > S["err0"] + 10: S["turn_sign"] = -1.0
                        S["turn_fixed"] = True; log(f"turn sign {S['turn_sign']} (err {S['err0']:.0f} -> {abs(err):.0f})")
                    cam().player_look(S.get("turn_sign", 1.0) * max(-45.0, min(45.0, err)) * 0.8, 0.0)
            S["prev"] = a; S["next_turn"] = now + 0.5
        if now > S["until"]: cam().player_move(0.0, 0.0); finish(f"did not reach the vehicle (at {dist:.1f} m)")
    elif ph == "aim":
        b = loc(int(JOB["vehicle"]))
        tgt = b + unreal.Vector(0, 0, 150.0 if JOB.get("aim") == "roof" else 60.0)
        look_at(tgt)
        # press only once the crosshair has rested on the vehicle for 10 ticks in a row (or give up after 15 s)
        # ... AND HROT offers its action now (OwnUnit.CanUse - live 2026-11-23: at 4.4 m the reach flickered and the press was refused OutOfReach)
        st = station()
        offered = bool(st and st.use_prompt())
        S["on"] = S.get("on", 0) + 1 if cam().player_aim_target() == int(JOB["vehicle"]) and offered else 0
        if time.time() > S["until"] and (S["on"] >= 10 or time.time() > S["until"] + 15):
            s = sample("aimed")
            log(f"crosshair rests on {s['aim']} (vehicle {JOB['vehicle']})")
            cam().player_use()
            S.update(phase="after", until=time.time() + 12, next_s=time.time() + 1)
    elif ph == "after":
        if time.time() > S["next_s"]: sample("after use"); S["next_s"] = time.time() + 2
        if time.time() > S["until"]:
            # the keys a driver would press: W for 3 s, then whatever moved is in the samples / the HROT log
            S["veh0"] = loc(int(JOB["vehicle"]))
            cam().player_move(1.0, 0.0); S.update(phase="press_w", until=time.time() + 6, next_s=0)
    elif ph == "press_w":
        if time.time() > S["next_s"]: sample("W held"); S["next_s"] = time.time() + 1
        if time.time() > S["until"]:
            cam().player_move(0.0, 0.0)
            b, b0 = loc(int(JOB["vehicle"])), S.get("veh0")
            R["vehicleMovedM"] = round((b - b0).length() / 100.0, 1) if b and b0 else None
            log(f"vehicle moved {R['vehicleMovedM']} m under W")
            # get out (gateway DD-029: any new press while the slot drives a vehicle with our soldier aboard)
            S.update(phase="settle", until=time.time() + 3)
    elif ph == "settle":
        if time.time() > S["until"]:
            sample("before get-out"); cam().player_use()
            S.update(phase="get_out", until=time.time() + 10, next_s=time.time() + 1)
    elif ph == "get_out":
        if time.time() > S["next_s"]: sample("after get-out"); S["next_s"] = time.time() + 2
        if time.time() > S["until"]: finish("done")


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        if time.time() < S.get("t", 0): return
        ph = S["phase"]
        if ph != S.get("logged"): S["logged"] = ph; log(f"phase {ph}")
        if ph == "level": level()
        elif ph == "join": join()
        else: run()
        if time.time() - T0 > 600: finish("timeout")
    except Exception:
        log(traceback.format_exc()); finish("exception")
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
