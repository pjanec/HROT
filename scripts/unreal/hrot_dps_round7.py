"""hrot_dps_round7.py - CE-4651 (U-18p): DPS ROUND 7 end to end over real DDS, against the plugin's FAKE STATION (UHrotDpsSubsystem's
FakeStation* test probes), before the gateway publishes it. Run in the full editor (-ExecutePythonScript), in PLAY (the IG camera as pawn).

    HROT_DPS_R7_JOB = { "domain", "partition", "out" }     (a PRIVATE domain: the fake writes SimClock / View / Session of its own)

Checks (each a row in out["checks"]): ClientWorld Loading before a Session and Ready for each world epoch; the probe thread's offset to a
station 3 s ahead; the anchored clock (continuous, a scale change, a pause, exact steps, a seek = a new TimelineEpoch); View unbound
(dead-reckoned at 10 m/s, no jump within a cut, a jump on a new cut, the vertical fov converted); View bound to a unit (the IG's follow rig
at the binding's distance from OUR prediction of the unit).
"""
import json, math, os, re, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DPS_R7_JOB"], encoding="utf-8-sig"))
R = {"log": [], "checks": [], "samples": []}
S = {"handle": None, "phase": "level", "busy": False, "i": 0}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-R7] {m}"); R["log"].append(m)


def check(name, ok, detail):
    R["checks"].append({"name": name, "ok": bool(ok), "detail": detail}); log(f"{'PASS' if ok else 'FAIL'} {name}: {detail}")


def finish(ok, why=""):
    R["ok"] = ok and all(c["ok"] for c in R["checks"]); R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def level():
    name = "/Game/Maps/DpsRound7"   # ONE scratch level: the runner deletes its file before Unreal starts (new_level SAVES a level, and could not replace one deleted in the same session)
    if not LES().new_level(name): raise RuntimeError("new_level failed: " + name)
    floor = EAS().spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0, 0, 0))
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane")); floor.set_actor_scale3d(unreal.Vector(4000, 4000, 1))
    EAS().spawn_actor_from_class(unreal.DirectionalLight, unreal.Vector(0, 0, 1000), unreal.Rotator(pitch=-40, yaw=60, roll=0))
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 60", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
    unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
    LES().editor_set_viewport_realtime(True)
    LES().editor_request_begin_play()
    S.update(phase="join", t=time.time() + 8)


def join():
    gw = UES().get_game_world()
    if gw is None: raise RuntimeError("PIE did not start")
    w = unreal.HrotDpsWorld.for_world(gw)
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    sub = unreal.get_engine_subsystem(unreal.HrotDpsSubsystem)
    pawn = unreal.GameplayStatics.get_player_pawn(gw, 0)
    S.update(w=w, sub=sub, pawn=pawn, phase="run", t0=time.time())
    log(f"joined domain {JOB['domain']} '{JOB['partition']}'; pawn {pawn.get_class().get_name() if pawn else None}")


def offset_ms(desc):
    m = re.search(r"offset ([-\d.]+) ms", desc)
    return float(m.group(1)) if m else None


def cam():
    t = S["pawn"].camera_transform(); l = t.translation
    return (l.x, l.y, l.z), S["pawn"].get_editor_property("cam").field_of_view


# the fake station's timeline: (seconds after join, action). Expected sim = E["sim"] + (now - E["at"]) * E["scale"] while running.
E = {"sim": 0.0, "at": 0.0, "scale": 1.0, "stepped": False}


def expect(now): return E["sim"] if E["stepped"] else E["sim"] + (now - E["at"]) * E["scale"]


def anchor(stepped, sim, scale, epoch, seq=0):
    now = time.time(); S["sub"].fake_station_clock(stepped, sim, scale, epoch, seq); E.update(sim=sim, at=now, scale=scale, stepped=stepped)


def a_start():
    S["sub"].fake_station_start(3.0)


def a_loading():
    cw = S["sub"].fake_station_client_world()
    check("ClientWorld Loading before any Session", " Loading " in cw and "wait 1" in cw, cw)
    S["sub"].fake_station_session(1, 0.0)


def a_offset():
    d = S["w"].describe_clock(); o = offset_ms(d)
    check("the probe thread measured the station 3 s ahead", o is not None and abs(o - 3000.0) < 25.0 and S["sub"].fake_station_echoes() >= 2,
          f"offset {o} ms, echoes answered {S['sub'].fake_station_echoes()} | {d}")
    cw = S["sub"].fake_station_client_world()
    check("ClientWorld Ready for world epoch 1", " epoch 1 Ready " in cw, cw)
    anchor(False, 100.0, 1.0, 1)


def sim_err(name, tol=0.03):
    now = time.time(); got = S["w"].sim_now(); want = expect(now)
    R["samples"].append({"name": name, "got": got, "want": want})
    check(name, abs(got - want) < tol, f"sim {got:.4f}, the station's {want:.4f} (error {1000 * (got - want):+.1f} ms) | {S['w'].describe_clock()}")


def a_scale2():
    sim_err("continuous: the station's formula 2 s after the anchor")
    anchor(False, expect(time.time()), 2.0, 1)


def a_check2(): sim_err("scale 2: the formula 1.5 s on")


def a_pause():
    anchor(True, expect(time.time()), 2.0, 1, 5); S["frozen"] = E["sim"]


def a_frozen():
    got = S["w"].sim_now(); check("paused: frozen at AnchorSimTime exactly", got == S["frozen"], f"{got!r} vs {S['frozen']!r}")


def a_step(seq):
    def f():
        E["sim"] += 1.0 / 60; S["sub"].fake_station_step(seq, E["sim"], 1); S["want_step"] = (seq, E["sim"])
    return f


def a_stepped():
    seq, want = S["want_step"]; got = S["w"].sim_now()
    check(f"step {seq}: TargetSimTime presented exactly", got == want, f"{got!r} vs {want!r} | {S['w'].describe_clock()}")


def a_seek():
    anchor(False, 50.0, 1.0, 2)


def a_seeked(): sim_err("seek: a new TimelineEpoch jumps back to 50 s")


def a_view1():
    S["pawn"].director()
    S["v0"] = expect(time.time())
    S["sub"].fake_station_view(S["v0"], 1, 0, 0.0, unreal.Vector(0, 0, 60), unreal.Vector(10, 0, 0), 0.0, 0.0, -10.0, 50.0, 1)


def a_view_a():
    S["c1"], fov = cam(); S["c1t"] = time.time()
    hfov = math.degrees(2 * math.atan(math.tan(math.radians(25)) * 16 / 9))
    check("View: the vertical fov 50 becomes Unreal's horizontal", abs(fov - hfov) < 8.0, f"fov {fov:.1f} (16:9 would be {hfov:.1f}; the viewport's own aspect rules)")
    log(f"view: {S['w'].describe_view()} | camera {S['pawn'].describe()}")


def a_view_b():
    c2, _ = cam(); dt = time.time() - S["c1t"]; v = (c2[0] - S["c1"][0]) / 100.0 / dt
    check("View unbound: dead-reckoned at its 10 m/s between samples", abs(v - 10.0) < 0.5 and abs(c2[2] - 6000) < 5, f"{v:.2f} m/s east over {dt:.2f} s, z {c2[2]:.0f} cm")
    # a new sample ON the curve (same cut): no jump
    now = expect(time.time()); S["pre"], _ = cam()
    S["sub"].fake_station_view(now, 1, 0, 0.0, unreal.Vector(10 * (now - S["v0"]), 0, 60), unreal.Vector(10, 0, 0), 0.0, 0.0, -10.0, 50.0, 1)


def a_view_c():
    post, _ = cam()
    check("View: a sample on the curve within one cut does not jump", abs(post[0] - S["pre"][0]) < 400, f"x {S['pre'][0]:.0f} -> {post[0]:.0f} cm in ~0.3 s")
    now = expect(time.time())
    S["sub"].fake_station_view(now, 1, 0, 0.0, unreal.Vector(500, 0, 60), unreal.Vector(0, 0, 0), 90.0, 0.0, 0.0, 50.0, 2)


def a_view_d():
    c, _ = cam()
    check("View: a new CutSeq is a hard cut", abs(c[0] - 50000) < 5 and abs(c[1]) < 5, f"camera at {c[0]:.0f}, {c[1]:.0f} cm (the cut's pose 50000, 0); {S['w'].describe_view()}")
    now = expect(time.time()); S["e0"] = now
    S["sub"].fake_station_entity(77, now, unreal.Vector(100, 50, 0), unreal.Vector(5, 0, 0), 0.0)


def a_bind():
    now = expect(time.time())
    S["sub"].fake_station_view(now, 2, 77, 12.0, unreal.Vector(0, 0, 0), unreal.Vector(0, 0, 0), 0.0, 0.0, -20.0, 60.0, 3)


def a_bound(label):
    def f():
        got = S["w"].pose_now(77)   # Unreal Python: the out-params (position, rotation, domain), None when false
        ok = got is not None; pos = got[0] if ok else unreal.Vector(0, 0, 0)
        c, _ = cam(); u = S["w"].enu_m_to_unreal(pos)
        d = math.dist(c, (u.x, u.y, u.z)) / 100.0
        t = expect(time.time()) - S["e0"]; want_x = 100 + 5 * t
        check(f"View bound ({label}): the IG's rig at the binding's distance from OUR prediction of unit 77",
              ok and 8.0 < d < 16.0 and abs(pos.x - want_x) < 0.5, f"unit at x {pos.x:.2f} (its curve {want_x:.2f}), camera {d:.2f} m from it | {S['pawn'].describe()}")
    return f


def a_epoch2():
    S["sub"].fake_station_session(2, expect(time.time()))


def a_epoch2_done():
    cw = S["sub"].fake_station_client_world(); ids = list(S["w"].entity_ids())
    check("a new world epoch: entities dropped, ClientWorld Ready for epoch 2", " epoch 2 Ready " in cw and 77 not in ids, f"{cw}; entities {ids}")
    R["clock"] = S["w"].describe_clock(); R["view"] = S["w"].describe_view(); R["camera"] = S["pawn"].describe()


PLAN = [(0.5, a_start), (3.0, a_loading),   # ClientWorld is re-said every 2 s: the fake reader (from 0.5 s) hears the next one
        (5.0, a_offset), (6.5, a_scale2), (8.0, a_check2), (8.5, a_pause), (9.5, a_frozen),
        (10.0, a_step(6)), (10.3, a_stepped), (10.5, a_step(7)), (10.8, a_stepped), (11.0, a_step(8)), (11.3, a_stepped),
        (11.5, a_seek), (12.5, a_seeked), (13.0, a_view1), (13.6, a_view_a), (15.1, a_view_b), (15.4, a_view_c), (15.8, a_view_d),
        (16.3, a_bind), (17.5, a_bound("settled")), (19.0, a_bound("1.5 s later")), (19.3, a_epoch2), (21.5, a_epoch2_done)]


def run():
    el = time.time() - S["t0"]
    while S["i"] < len(PLAN) and el >= PLAN[S["i"]][0]:
        PLAN[S["i"]][1](); S["i"] += 1
    if S["i"] >= len(PLAN): LES().editor_request_end_play(); S.update(phase="end", t=time.time() + 3)


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        p = S["phase"]
        if p == "level": level()
        elif p == "join" and time.time() >= S["t"]: join()
        elif p == "run": run()
        elif p == "end" and time.time() >= S["t"]: finish(True)
    except Exception:
        finish(False, traceback.format_exc())
    finally:
        S["busy"] = False


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
