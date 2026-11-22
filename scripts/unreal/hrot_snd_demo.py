"""hrot_snd_demo.py - CE-4651 (U-19f): the SOUND DEMO - hear a sound and read on screen what it is. Full editor, PIE, the plugin's FAKE STATION
(modelled on hrot_dps_labels.py).

    HROT_SND_DEMO_JOB = { "domain", "partition", "out" (result json), "pace" (seconds per step, default 4), "shots" (optional folder: one HUD picture per step) }

hrot.snd.Debug 1 draws the SOUND LOG lower-left (RADIO / WORLD / WEAPON / AMBIENCE / EVENT, newest at the bottom) and, BEFORE each step, a CAPTION
(top-centre) saying what the listener should hear. A flat level; a friendly squad: unit 9 'Alpha' (the leader-unit, a body-less unit at the origin) and men
1..4 around it ~10 m apart, two hostiles far east. Steps:
  1 order      a shout in the WORLD, camera 4 m from man 1                         -> a WORLD line  '... man 1 Utterance CmdMove'
  2 radio      the camera follows man 1: his unit's net is in the EARPIECE         -> RADIO lines (an Order, a Report Contact, beep / sgt / squelch)
  3 away       shouts from 5, 20, 35, 45 and 60 m                                  -> WORLD lines with 5 / 20 / 35 m; 45 m 'faint, beyond 40 m'; 60 m 'not played, beyond 50 m'
  4 bursts     3 (rifle), 5 (AK of the east) and 7 (machine gun) rounds, 60 m away -> one WEAPON line each, the clip's own round count in its name
  5 battle     a city battle bed fades in                                          -> an AMBIENCE line 'battle.city'
  6 siren      an air-raid siren from the town 70 m to the north                   -> an AMBIENCE line 'alarm.airraid'
  7 airstrike  an airstrike event                                                  -> an EVENT line 'event.airstrike'
  8 stop       all rows disposed, 'Demo done'
The result json lists every step with the sound-log lines seen during it (UHrotDpsSubsystem.get_sound_log_lines), and the machine checks.
"""
import glob, json, os, re, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_SND_DEMO_JOB"], encoding="utf-8-sig"))
PACE = float(JOB.get("pace", 4))
R = {"log": [], "checks": [], "steps": []}
S = {"handle": None, "phase": "level", "tries": 0}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()
SIM0 = 10.0
PERSON, GROUND = 1, 2
V = unreal.Vector


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-SND] {m}"); R["log"].append(m)


def check(name, ok, detail):
    R["checks"].append({"name": name, "ok": bool(ok), "detail": detail}); log(f"{'PASS' if ok else 'FAIL'} {name}: {detail}")


def finish(ok, why=""):
    log(f"finish ok={ok} why={why}")
    R["ok"] = ok and all(c["ok"] for c in R["checks"]); R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def level():
    # the EDITOR mutes an unfocused window on its own (EditorEngine.cpp: volume 0 unless LevelEditorMiscSettings.bAllowBackgroundAudio), on top of
    # the project's [Audio] UnfocusedVolumeMultiplier=1.0 which only a packaged client honours. Measured 2026-11-22: the user wrote notes in
    # another window and heard nothing after step 2 while every sound was logged as started
    # (LevelEditorMiscSettings is not exposed to Python => set in the editor's Saved EditorPerProjectUserSettings.ini: [/Script/UnrealEd.LevelEditorMiscSettings] bAllowBackgroundAudio=True)
    # new_level refuses an existing path, and neither os.remove nor delete_asset clears it reliably (measured 2026-11-22: failed on 2 runs of 3)
    # => a fresh name per run; the files of earlier runs are removed from disk (never the one being made)
    for f in glob.glob(os.path.join(unreal.Paths.project_content_dir(), "Maps", "DpsSndDemo*.umap")):
        try: os.remove(f)
        except OSError: pass
    name = "/Game/Maps/DpsSndDemo_%d" % int(time.time())
    if not LES().new_level(name): raise RuntimeError("new_level failed: " + name)
    floor = EAS().spawn_actor_from_class(unreal.StaticMeshActor, V(0, 0, 0))
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane")); floor.set_actor_scale3d(V(8000, 8000, 1))
    sun = EAS().spawn_actor_from_class(unreal.DirectionalLight, V(0, 0, 1000), unreal.Rotator(pitch=-40, yaw=60, roll=0))
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    EAS().spawn_actor_from_class(unreal.SkyAtmosphere, V(0, 0, 0))
    sky = EAS().spawn_actor_from_class(unreal.SkyLight, V(0, 0, 500)); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
    unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
    if not LES().save_current_level(): raise RuntimeError("save failed")   # Play on an UNSAVED level ends the unattended editor
    LES().editor_set_viewport_realtime(True)
    LES().editor_request_begin_play()
    S.update(phase="join", t=time.time() + 8)


def join():
    gw = UES().get_game_world()
    if gw is None: raise RuntimeError("PIE did not start")
    w = unreal.HrotDpsWorld.for_world(gw)
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    sub = unreal.get_engine_subsystem(unreal.HrotDpsSubsystem)
    sub.fake_station_start(0.0)
    sub.fake_station_session(1, SIM0)
    # the squad: unit 9 'Alpha' at the origin, men 1..4 ~10 m around it; two hostiles far east
    sub.fake_station_unit(9, 1, "Alpha", SIM0, V(0, 0, 0))
    for (i, p) in ((1, (10, 0, 0)), (2, (0, 10, 0)), (3, (-10, 0, 0)), (4, (0, -10, 0))):
        sub.fake_station_entity_as(i, PERSON, 1, f"Man {i}", SIM0, V(*p), V(0, 0, 0), 90.0)
    for (i, p) in ((21, (400, 5, 0)), (22, (410, -8, 0))):
        sub.fake_station_entity_as(i, PERSON, 2, f"Hostile {i}", SIM0, V(*p), V(0, 0, 0), 270.0)
    sub.fake_station_clock(True, SIM0, 1.0, 1, 0)
    unreal.SystemLibrary.execute_console_command(gw, "hrot.snd.Debug 1")
    unreal.HrotDpsSubsystem.clear_sound_log()
    S.update(w=w, sub=sub, gw=gw, phase="run", t=time.time() + 4.0)
    build_steps()
    log("joined, the squad is out")


def pawn(): return unreal.GameplayStatics.get_player_pawn(S["gw"], 0)


def cam(x, y, z=2.0, yaw=0.0): pawn().place_at_rotator(S["w"].enu_m_to_unreal(V(x, y, z)), unreal.Rotator(pitch=-1.5, yaw=yaw, roll=0), 90.0)


def lines_of(step): return step.get("lines", [])


def has(step, *needles): return any(all(n in l for n in needles) for l in step["lines"])


def build_steps():
    sub = S["sub"]
    fx = lambda k: (lambda: k())
    steps = []

    def add(name, caption, acts, dur=None, shot=False, verify=None):
        steps.append({"name": name, "caption": caption, "acts": acts, "dur": dur or PACE, "shot": shot, "verify": verify, "lines": []})

    # 1: a shout in the world, near
    add("order", "Squad leader gives an order - you should hear it in the world, near you",
        [(0.0, lambda: cam(10 - 4, 0, 2, 0)), (0.5, lambda: sub.fake_station_utterance(1, "CmdMove", SIM0))],
        verify=lambda s: check("1 order: a WORLD line, man 1 Utterance CmdMove, ~4 m", has(s, "WORLD", " m", "man 1 Utterance CmdMove") and has(s, "WORLD 4 m"), str(s["lines"])))
    # 2: the earpiece: follow man 1 (his net = unit 9, the nearest same-side unit)
    add("radio", "Camera follows man 1 - his unit's radio is now in your earpiece",
        [(0.0, lambda: pawn().follow(1, 14.0)), (1.5, lambda: sub.fake_station_order(9, 1, "Move out", SIM0)),
         (2.0, lambda: sub.fake_station_report(9, 0, 1, 0, 3, 2, 0, 0, 0, False, SIM0))],
        dur=max(PACE, 16.0), shot=True,
        verify=lambda s: check("2 radio: RADIO earpiece lines - the order, the report, the framing clips",
                               sum(1 for l in s["lines"] if " RADIO " in l) >= 6 and has(s, "RADIO", "earpiece", "Order Move out") and has(s, "RADIO", "Report Contact from unit 9") and has(s, "RADIO", "beep"), str(s["lines"])))
    # 3: moving away: shouts at 5, 20, 35, 45 (faint) and 60 m (cut at 50 m)
    for d in (5, 20, 35, 45, 60):
        add(f"away{d}", f"Moving away - {d} m from him" + (" - his shouts fade, cut at 50 m" if d == 5 else ""),
            [(0.0, (lambda d=d: cam(10 - d, 0, 2, 0))), (0.8, lambda: sub.fake_station_utterance(1, "CallReloading", SIM0))],
            verify=(lambda s, d=d: check(f"3 away: the {d} m shout" + {45: " is logged as faint", 60: " is logged as not played"}.get(d, " is a WORLD line"),
                                         {45: has(s, "WORLD", "man 1 Utterance CallReloading", "faint, beyond 40 m"),
                                          60: has(s, "WORLD", "man 1 Utterance CallReloading", "not played, beyond 50 m")}.get(d,
                                          has(s, "WORLD %d m" % d, "man 1 Utterance CallReloading") and not has(s, "beyond")), str(s["lines"]))))
    # 4: bursts, 60 m away: the camera at the origin looking east, the muzzle 60 m east
    base = [(0.0, lambda: cam(0, 0, 2, 0))]
    for (nm, shooter, mg, rounds, bid, pat) in (("burst3", 2, False, 3, 301, "_b3"), ("burst5", 21, False, 5, 302, "_b5"), ("burst7", 3, True, 7, 303, "_b7")):
        def fire(shooter=shooter, mg=mg, rounds=rounds, bid=bid):
            for i in range(rounds):
                sub.fake_station_burst_round_as(SIM0, shooter, V(60, 0, 1.5), V(300, 0, 0), bid, rounds, i, mg)
        add(nm, f"Bursts: {rounds} rounds ({'machine gun' if mg else 'AK of the east' if shooter == 21 else 'rifle'}) - count them, 60 m away",
            (base if nm == "burst3" else [(0.0, lambda: cam(0, 0, 2, 0))]) + [(1.0, fire)], dur=max(PACE, 5.0),
            verify=(lambda s, pat=pat, rounds=rounds: check(f"4 burst of {rounds}: a WEAPON line of the {rounds}-round clip", has(s, "WEAPON", "60 m") and has(s, "WEAPON", pat), str(s["lines"]))))
    # 5-7: ambience
    add("battle", "War around you: a city battle fades in",
        [(0.0, lambda: cam(0, 0, 2, 0)), (0.5, lambda: sub.fake_station_ambience(1, "battle.city", 0.8, False, V(0, 0, 0), 0.0, False))],
        dur=max(PACE, 6.0), verify=lambda s: check("5 battle: an AMBIENCE line, battle.city", has(s, "AMBIENCE", "battle.city"), str(s["lines"])))
    add("siren", "Air-raid siren from the town to the north (70 m ahead)",
        [(0.0, lambda: cam(0, 230, 2, -90)), (0.5, lambda: sub.fake_station_ambience(2, "alarm.airraid", 1.0, True, V(0, 300, 0), 200.0, False))],
        dur=max(PACE, 6.0), verify=lambda s: check("6 siren: an AMBIENCE line, alarm.airraid, ~70 m", has(s, "AMBIENCE", "alarm.airraid") and has(s, "70 m"), str(s["lines"])))
    add("airstrike", "An airstrike event",
        [(0.0, lambda: sub.fake_station_ambience(3, "event.airstrike", 1.0, False, V(0, 0, 0), 0.0, False))],
        dur=max(PACE, 8.0), verify=lambda s: check("7 airstrike: an EVENT line, event.airstrike", has(s, "EVENT", "event.airstrike"), str(s["lines"])))
    def stop():
        for i in (1, 2, 3): sub.fake_station_ambience(i, "", 0.0, False, V(0, 0, 0), 0.0, True)
    add("stop", "All stops", [(0.0, stop)], dur=3.0)
    S["steps"] = steps
    S["i"] = 0


def run():
    steps, gw = S["steps"], S["gw"]
    now = time.time()
    if "cur" not in S:   # begin a step: the caption first, the log's position noted
        if S["i"] >= len(steps): S.update(phase="end", t=now + 1.0); return
        st = steps[S["i"]]
        S.update(cur=st, t0=now, n0=len(list(unreal.HrotDpsSubsystem.get_sound_log_lines())), done=0)
        unreal.HrotDpsSubsystem.set_sound_caption(st["caption"], st["dur"] + 0.5)
        log(f"step {S['i'] + 1}/{len(steps)} {st['name']}: {st['caption']}")
        return
    st = S["cur"]
    while S["done"] < len(st["acts"]) and now - S["t0"] >= st["acts"][S["done"]][0]:
        st["acts"][S["done"]][1](); S["done"] += 1
    if S["done"] >= len(st["acts"]) and st["shot"] and not st.get("shot_taken") and now - S["t0"] >= st["dur"] - 3.0:
        st["shot_taken"] = True
        if JOB.get("shots"):
            path = os.path.join(JOB["shots"], f"snd-{st['name']}.png").replace("\\", "/")
            if os.path.exists(path): os.remove(path)
            unreal.SystemLibrary.execute_console_command(gw, f'HighResShot 1 filename="{path}"')
    if now - S["t0"] >= st["dur"]:
        all_lines = list(unreal.HrotDpsSubsystem.get_sound_log_lines())
        st["lines"] = all_lines[S["n0"]:]
        R["steps"].append({"name": st["name"], "caption": st["caption"], "lines": st["lines"]})
        log(f"  {st['name']}: {len(st['lines'])} sound-log lines" + "".join(f"\n      {l}" for l in st["lines"]))
        if st["verify"]: st["verify"](st)
        del S["cur"]; S["i"] += 1


def end():
    unreal.HrotDpsSubsystem.set_sound_caption("Demo done", 3.0)
    R["transcript"] = list(unreal.HrotDpsSubsystem.get_sound_log_lines())
    LES().editor_request_end_play(); finish(True)


def tick(dt):
    if S.get("busy"): return   # new_level / PIE pump Slate: a re-entered tick would start a second new_level inside the first
    S["busy"] = True
    try:
        tick_once()
    finally:
        S["busy"] = False


def tick_once():
    try:
        if time.time() < S.get("t", 0): return
        ph = S["phase"]
        if ph != S.get("logged"): S["logged"] = ph; log(f"phase {ph}")
        if ph == "level": level()
        elif ph == "join": join()
        elif ph == "run": run()
        elif ph == "end": end()
        if time.time() - T0 > 240 + PACE * 40: finish(False, "timeout")
    except Exception:
        log(traceback.format_exc()); finish(False, "exception")


S["handle"] = unreal.register_slate_post_tick_callback(tick)
# -ExecutePythonScript ends the editor when the script returns; the ticks need it alive (the labels script does the same)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
