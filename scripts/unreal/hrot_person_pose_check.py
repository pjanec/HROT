"""hrot_person_pose_check.py - CE-4651 (U-19g): measure LIVE how every drawn person stands - which layer tilts a body.

    HROT_POSE_JOB = { "domain", "partition", "map", "out" (result json), "ready", "go", "seconds" (default 150), "every" (default 5) }

The editor plays (PIE, AHrotDpsGameMode) the live station's map; the client joins; once "go" exists, every `every` seconds, for each person
actor: the ACTOR's pitch / roll, the body MESH component's world pitch / roll, and the bones in world space - the lean of pelvis -> head from
up (degrees), the head's height above the lowest foot (cm) - plus the client's own look line (UHrotDpsWorld.PersonLook: role / clip / stance).
Three layers can tilt a body: the actor (HROT's orientation as drawn), the mesh component (the kit's offsets) and the pose (the clip on that
skeleton). A body that lies (lean > 45) while its role is a standing one names the layer that did it. Facts only (user, 2026-11-23: the
civilians' tilt "is what you can detect yourselves").
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_POSE_JOB"], encoding="utf-8-sig"))
R = {"log": [], "samples": []}
S = {"handle": None, "phase": "level", "busy": False}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
T0 = time.time()


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-POSE] {m}"); R["log"].append(m)


def finish(why):
    log(f"finish: {why}"); R["why"] = why
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
    S.update(w=w, phase="wait_go", t=time.time() + 1); log("joined; ready")


def body_mesh(actor):
    """the visible skeletal mesh that carries a head bone (a crowd body has several parts; the driver one is the skeleton's)"""
    best = None
    for c in actor.get_components_by_class(unreal.SkeletalMeshComponent):
        if c.does_socket_exist("head") and c.does_socket_exist("pelvis"):
            if best is None or c.is_visible(): best = c
    return best


def sample():
    w = S["w"]
    rows = []
    for eid in w.entity_ids():
        a = w.actor_of(eid)
        if not a: continue
        m = body_mesh(a)
        if not m: continue
        look = w.person_look(eid)
        if not look: continue
        ar, mr = a.get_actor_rotation(), m.get_world_rotation() if hasattr(m, "get_world_rotation") else m.get_editor_property("relative_rotation")
        head, pelvis = m.get_socket_location("head"), m.get_socket_location("pelvis")
        feet = [m.get_socket_location(f) for f in ("foot_l", "foot_r") if m.does_socket_exist(f)]
        d = head - pelvis
        lean = math.degrees(math.acos(max(-1.0, min(1.0, d.z / max(1e-6, d.length())))))
        low = min(f.z for f in feet) if feet else pelvis.z
        rows.append({"id": eid, "actorPitch": round(ar.pitch, 1), "actorRoll": round(ar.roll, 1), "meshPitch": round(mr.pitch, 1), "meshRoll": round(mr.roll, 1),
                     "lean": round(lean, 1), "headAboveFeet": round(head.z - low, 1), "feetAboveActor": round(low - a.get_actor_location().z, 1),
                     "look": look, "name": a.get_actor_label() if hasattr(a, "get_actor_label") else a.get_name()})
    R["samples"].append({"t": round(time.time() - T0, 1), "people": rows})
    tilted = [r for r in rows if r["lean"] > 45]
    log(f"{len(rows)} people, {len(tilted)} lying: " + "; ".join(f"{r['id']} lean {r['lean']} actor {r['actorPitch']}/{r['actorRoll']} mesh {r['meshPitch']}/{r['meshRoll']} [{r['look'][:80]}]" for r in tilted[:8]))


def run():
    if S["phase"] == "wait_go":
        if not os.path.exists(JOB["go"]): return
        S.update(phase="sample", until=time.time() + float(JOB.get("seconds", 150)), next_s=0); log("go")
    elif S["phase"] == "sample":
        if time.time() >= S["next_s"]: sample(); S["next_s"] = time.time() + float(JOB.get("every", 5))
        if time.time() > S["until"]: finish("done")


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
