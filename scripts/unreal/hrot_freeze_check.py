"""hrot_freeze_check.py - CE-4651 (U-19g): reproduce the client's freeze on the first shots LIVE and record where the frames went.

    HROT_FREEZE_JOB = { "domain", "partition", "map", "out" (result json), "ready", "go", "soldier" (id), "watch" (s after the shots, default 60), "every" (pose sampling, s) }

The user (2026-11-22): "when I aim at a target and I fire first shot, the Unreal client freezes for a few seconds" - also on hits.
The editor plays (PIE, AHrotDpsGameMode) the live station's map; the client joins; once "go" exists it possesses the soldier, idles 12 s
(the baseline frame times), turns toward the nearest drawn person, fires ONE round, waits 12 s, then fires one round every 2 s for 10 s.
Every frame's wall time is recorded; a frame over 0.25 s is a HITCH, logged with a "[HROT-FREEZE] hitch" line so the editor log's own
lines around it (asset loads, skinned-asset compiles, shader compiles, GC) can be read by their timestamps. Facts only.
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_FREEZE_JOB"], encoding="utf-8-sig"))
R = {"log": [], "hitches": [], "marks": []}
S = {"handle": None, "phase": "level", "busy": False, "last": None}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
T0 = time.time()


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-FREEZE] {m}"); R["log"].append(m)


def mark(what):
    R["marks"].append({"t": round(time.time() - T0, 2), "what": what}); log("mark: " + what)


def finish(why):
    log(f"finish: {why}"); R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def gw(): return UES().get_game_world()
def cam(): return unreal.GameplayStatics.get_player_pawn(gw(), 0)
def station(): return unreal.HrotDpsStation.for_world(gw())


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


def body_mesh(actor):
    best = None
    for c in actor.get_components_by_class(unreal.SkeletalMeshComponent):
        # a hidden window draws nothing, and an undrawn body does not update its bones (VisibilityBasedAnimTickOption) - force it for the check
        c.set_editor_property("visibility_based_anim_tick_option", unreal.VisibilityBasedAnimTickOption.ALWAYS_TICK_POSE_AND_REFRESH_BONES)
        c.set_forced_lod(1)
        if c.does_socket_exist("head") and c.does_socket_exist("foot_l") and c.does_socket_exist("pelvis") and (best is None or c.is_visible()): best = c
    return best


def poses(tag):
    """every drawn person: the actor's pitch / roll, the body mesh's, and the bones' lean (pelvis -> head from up) and height"""
    w, rows = S["w"], []
    for eid in w.entity_ids():
        look = w.person_look(eid)
        a = w.actor_of(eid) if look else None
        m = body_mesh(a) if a else None
        if not m: continue
        ar, mr = a.get_actor_rotation(), m.get_world_rotation()
        head, pelvis = m.get_socket_location("head"), m.get_socket_location("pelvis")
        feet = [m.get_socket_location(f) for f in ("foot_l", "foot_r") if m.does_socket_exist(f)]
        d = head - pelvis
        lean = math.degrees(math.acos(max(-1.0, min(1.0, d.z / max(1e-6, d.length())))))
        low = min(f.z for f in feet) if feet else pelvis.z
        rows.append({"id": eid, "actor": [round(ar.pitch, 1), round(ar.roll, 1)], "mesh": [round(mr.pitch, 1), round(mr.roll, 1)], "lean": round(lean, 1),
                     "headAboveFeet": round(head.z - low, 1), "feetAboveActor": round(low - a.get_actor_location().z, 1), "look": look[:90]})
    R.setdefault("poses", []).append({"t": round(time.time() - T0, 1), "tag": tag, "people": rows})
    odd = [r for r in rows if (r["lean"] > 45 and "Prone" not in r["look"] and "Death" not in r["look"] and "Dying" not in r["look"])
           or abs(r["actor"][0]) > 20 or abs(r["actor"][1]) > 20 or abs(r["mesh"][0]) > 20 or abs(r["mesh"][1]) > 20]
    log(f"poses ({tag}): {len(rows)} people; lying in a non-lying role: " + ("; ".join(f"{r['id']} lean {r['lean']} actor {r['actor']} mesh {r['mesh']} [{r['look'][:60]}]" for r in odd) or "none"))


def nearest_person():
    w, me = S["w"], cam().get_actor_location()
    best, bd = None, 1e18
    for eid in w.entity_ids():
        look = w.person_look(eid)
        if eid == int(JOB["soldier"]) or not look: continue
        if JOB.get("targetCivilians") and "Unarmed" not in look: continue   # shoot toward the civilians (the user's session: he fought near them)
        a = w.actor_of(eid)
        if not a: continue
        d = (a.get_actor_location() - me).length()
        if d < bd: best, bd = (eid, a.get_actor_location()), d
    return best, bd / 100.0


def run():
    ph, now = S["phase"], time.time()
    if ph == "wait_go":
        if not os.path.exists(JOB["go"]): return
        cam().possess(int(JOB["soldier"])); S.update(phase="possess", until=now + 30); log("go")
    elif ph == "possess":
        st = station()
        if st and st.is_playing() and st.played_unit() == int(JOB["soldier"]):
            mark("possessed"); poses("before"); S.update(phase="baseline", until=now + 12)
        elif now > S["until"]: finish("possession not granted")
    elif ph == "baseline":
        if now > S["until"]:
            p, d = nearest_person()
            S["target"] = p; mark(f"aim at person {p[0] if p else None} {d:.0f} m away")
            S.update(phase="aim", until=now + 3)
    elif ph == "aim":
        if S.get("target"):
            eye, tgt = cam().get_actor_location(), S["target"][1] + unreal.Vector(0, 0, 120)
            rot, d = cam().view_rotation(), tgt - eye
            dy = (math.degrees(math.atan2(d.y, d.x)) - rot.yaw + 540.0) % 360.0 - 180.0
            dp = math.degrees(math.atan2(d.z, math.hypot(d.x, d.y))) - rot.pitch
            cam().player_look(-0.3 * dy, 0.3 * dp)   # PlayerLook's yaw sign is -1 (measured by hrot_drive_check.py)
        if now > S["until"]:
            mark(f"FIRST SHOT (crosshair on {cam().player_aim_target()})"); cam().player_fire(True)
            S.update(phase="release", until=now + 0.15, after="wait1")
    elif ph == "release":
        if now > S["until"]:
            cam().player_fire(False); S.update(phase=S["after"], until=now + (12 if S["after"] == "wait1" else 1.85))
    elif ph == "wait1":
        if now > S["until"]: S.update(phase="more", n=0, until=now)
    elif ph == "after":
        if now >= S["next_p"]: poses("after the shots"); S["next_p"] = now + float(JOB.get("every", 5))
        if now > S["until"]: finish("done")
    elif ph == "more":
        if now > S["until"]:
            if S["n"] >= 5: S.update(phase="after", until=now + float(JOB.get("watch", 60)), next_p=0); return
            S["n"] += 1; mark(f"shot {S['n'] + 1}"); cam().player_fire(True); S.update(phase="release", until=now + 0.15, after="more")


def tick(dt):
    now = time.time()
    if S["last"] is not None and S["phase"] not in ("level", "join"):
        gap = now - S["last"]
        if gap > 0.25:
            R["hitches"].append({"t": round(now - T0, 2), "seconds": round(gap, 2), "phase": S["phase"]})
            log(f"hitch {gap:.2f} s in phase {S['phase']}")
    S["last"] = now
    if S["busy"]: return
    S["busy"] = True
    try:
        if now < S.get("t", 0): return
        if S["phase"] == "level": level()
        elif S["phase"] == "join": join()
        else: run()
        if now - T0 > 900: finish("timeout")
    except Exception:
        log(traceback.format_exc()); finish("exception")
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
