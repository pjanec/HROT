"""hrot_person_pose_check.py - CE-4651 (U-19g): measure LIVE how every drawn person stands - which layer tilts a body.

    HROT_POSE_JOB = { "domain", "partition", "map", "out" (result json), "ready", "go", "seconds" (default 150), "every" (default 5), "shots" (a folder for the pictures of a lying civilian) }

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
    """the visible skeletal mesh that carries a head bone (a crowd body has several parts; the driver one is the skeleton's).
    Measured 2026-11-23: a hidden window does not update undrawn bodies' bones, and a far LOD collapses the legs onto the pelvis (feet
    at hip height) - so every part is forced to LOD 0 and to refresh its bones while this check runs"""
    best = None
    for c in actor.get_components_by_class(unreal.SkeletalMeshComponent):
        c.set_editor_property("visibility_based_anim_tick_option", unreal.VisibilityBasedAnimTickOption.ALWAYS_TICK_POSE_AND_REFRESH_BONES)
        c.set_forced_lod(1)
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
    # a civilian seen before and missing now: what does the client still hold for him? (2026-11-23: they vanished after 2-3.5 min)
    now_ids = {r["id"] for r in rows}
    for pid in sorted(S.get("civs", set()) - now_ids):
        if pid in S.setdefault("gone", set()): continue
        S["gone"].add(pid)
        a = w.actor_of(pid)
        parts = [f"{c.get_name()} vis {int(c.is_visible())} head {int(c.does_socket_exist('head'))}" for c in a.get_components_by_class(unreal.SkeletalMeshComponent)] if a else []
        log(f"CIVILIAN GONE: {pid} actor {'yes' if a else 'none'} hidden {a.get_editor_property('hidden') if a else '-'} at {a.get_actor_location() if a else '-'} "
            f"look '{w.person_look(pid)[:80]}' describe '{w.describe(pid)[:200]}' parts {parts}")
        if a: S.setdefault("queue", []).append((pid, ["front", "side", "above"]))
    for r in rows:
        if "Unarmed" in r["look"] or "Phone" in r["look"]: S.setdefault("civs", set()).add(r["id"])
    # every `portraitEvery` s: a picture of each civilian (a skinning fault shows in a picture, never in the bones)
    pe = float(JOB.get("portraitEvery", 0))
    if pe and time.time() >= S.get("next_portrait", 0):
        S["next_portrait"] = time.time() + pe
        for pid in sorted(S.get("civs", set()) & now_ids)[:3]: S.setdefault("queue", []).append((pid, ["front"]))
    # a civilian (no rifle roles) that lies, or whose role turns to a lying one: pictures of him, once per id and role
    for r in rows:
        civ = "Unarmed" in r["look"] or "Dying" in r["look"] or "Phone" in r["look"]
        lying = r["lean"] > 45 or any(k in r["look"] for k in ("Prone", "Death", "Dying"))
        key = (r["id"], r["look"].split(" ")[0])
        if civ and lying and key not in S.setdefault("shot", set()) and len(S["shot"]) < 40:
            S["shot"].add(key); S.setdefault("queue", []).append((r["id"], ["front", "side", "above"]))
            log(f"CIVILIAN LYING: {r['id']} lean {r['lean']} actor {r['actorPitch']}/{r['actorRoll']} mesh {r['meshPitch']}/{r['meshRoll']} head-feet {r['headAboveFeet']} [{r['look'][:90]}]")
    tilted = [r for r in rows if r["lean"] > 45]
    log(f"{len(rows)} people, {len(tilted)} lying: " + "; ".join(f"{r['id']} lean {r['lean']} actor {r['actorPitch']}/{r['actorRoll']} mesh {r['meshPitch']}/{r['meshRoll']} [{r['look'][:80]}]" for r in tilted[:8]))


def shoot_person():
    """pictures of the first queued person from 3 m in front and beside (the free camera; the run's sampling waits meanwhile)"""
    st = S.setdefault("shoot", {})
    now = time.time()
    if st.get("pending"):
        path, t0 = st["pending"]
        if (os.path.exists(path) and os.path.getsize(path) > 0) or now - t0 > 30:
            R.setdefault("shots", []).append(path); st["pending"] = None
        return True
    if now < st.get("next", 0): return True
    pid, want = S["queue"][0]
    a = S["w"].actor_of(pid)
    if not a: S["queue"].pop(0); st.clear(); return False
    views = st.setdefault("views", list(want))
    if not st.get("placed"):
        p, yaw = a.get_actor_location(), a.get_actor_rotation().yaw
        f = unreal.Vector(math.cos(math.radians(yaw)), math.sin(math.radians(yaw)), 0)
        v = views[0]
        at = p + unreal.Vector(0, 0, 150) + (f * 350 if v == "front" else unreal.Vector(-f.y, f.x, 0) * 350 if v == "side" else unreal.Vector(-f.x * 600, -f.y * 600, 700))
        d = p + unreal.Vector(0, 0, 60) - at
        rot = unreal.Rotator(0, math.degrees(math.atan2(d.z, math.hypot(d.x, d.y))), math.degrees(math.atan2(d.y, d.x)))
        unreal.GameplayStatics.get_player_pawn(UES().get_game_world(), 0).place_at_rotator(at, rot, 60.0)
        st["placed"] = True; st["next"] = now + 2; return True
    v = views.pop(0); st["placed"] = False
    path = os.path.join(JOB["shots"], f"t{int(now - T0):04d}_person{pid}_{v}.png").replace("\\", "/")
    unreal.SystemLibrary.execute_console_command(UES().get_game_world(), f'HighResShot 1 filename="{path}"')
    st["pending"] = (path, now)
    if not views: S["queue"].pop(0); st.pop("views", None)
    return True


def run():
    if S["phase"] == "wait_go":
        if not os.path.exists(JOB["go"]): return
        S.update(phase="sample", until=time.time() + float(JOB.get("seconds", 150)), next_s=0); log("go")
    elif S["phase"] == "sample":
        if S.get("queue") and shoot_person(): return
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
