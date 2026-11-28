"""hrot_dps_labels.py - CE-4651 (U-18y): entity labels (cards) in the world, against the plugin's FAKE STATION, in the full editor (PIE).

    HROT_DPS_LABELS_JOB = { "domain", "partition", "out" (result json), "shots" (a folder) }

A flat level, a wall; the camera at the ENU origin 2 m up looking east (90 deg). Entities (metres, ENU):
  near (full cards)      1 person friend 'Rifleman' (30,-3) | 2 ground hostile 'T-72' (40,4) | 3 ground neutral, unnamed -> '#3' (50,10)
                         8 person hostile 'Sniper' (25,18)
  behind the wall        4 ground friend 'Hidden' (60,-12); the wall at x 52, y -16..-7, 12 m tall (its card must NOT show)
  1-2 m/px (name only)   5 ground friend 'Far truck' (900,30)
  > 2 m/px (nothing)     6 ground friend 'Very far' (2600,-40)
  an aircraft, far       7 air friend 'Eagle 1' (2600,60, 300 up) - its name at any distance (R-143 rule A)
  DD-025 EntityStatus    9 a body-less UNIT (Entity.IsUnit) 'Alpha' (12,4): symbol, strength 7/9 (amber), ammo 0.3, task Assault
                         10 person friend 'Broken man' (12,-4): health 30/100, Broken mind (fill 0.8, break tick 0.6), behaviour Flee
U-19b (the squad's radio): a fake Report (Lace, Kia 2) from unit 9 and an Utterance CmdMove from man 1; the player must log beep > sgt > rep_losses > squelch and the soundscape's
LibraryMissing must not grow; then the camera follows man 1 (his net = unit 9: the nearest same-side unit) and an Order from unit 9 is heard in the earpiece, with the receiver's answer.
U-19c (bursts): a fake 3-round burst (BurstId 77) from man 1 plays ONE clip wpn/rifle_b3, indices 1 and 2 are covered (no per-round rifle sound); a 4-round burst that stops after 2 is cut.
U-19d (ambience + weather): a fake battle.city bed and a positioned alarm.airraid row start their loops (the bed 6 dB down after the simulated fire), a global rain row sets RainAmount / WindAmount, the dispose fades the loops.
U-19e (the director's fade and subtitle): a fake View with Fade 0.5 and a subtitle is ignored by a followed camera and drawn by the HUD in director mode.
Pictures (HighResShot 1 = the viewport's own size): mode 3 (every unit), mode 2 (the units + every aircraft), mode 1 (none); then the symbol sheet (UHrotCardLayer.export_symbol_sheet). Checks read UHrotDpsWorld.describe_labels.
"""
import glob, json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DPS_LABELS_JOB"], encoding="utf-8-sig"))
R = {"log": [], "checks": [], "shots": []}
S = {"handle": None, "phase": "level", "tries": 0}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()
SIM0 = 10.0
PERSON, GROUND, AIR = 1, 2, 3
ENTS = [(1, PERSON, 1, "Rifleman", (30, -3, 0)), (2, GROUND, 2, "T-72", (40, 4, 0)), (3, GROUND, 0, "", (50, 10, 0)), (8, PERSON, 2, "Sniper", (25, 18, 0)),
        (4, GROUND, 1, "Hidden", (60, -12, 0)), (5, GROUND, 1, "Far truck", (900, 30, 0)), (6, GROUND, 1, "Very far", (2600, -40, 0)),
        (7, AIR, 1, "Eagle 1", (2600, 60, 300))]


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-LBL] {m}"); R["log"].append(m)


def check(name, ok, detail):
    R["checks"].append({"name": name, "ok": bool(ok), "detail": detail}); log(f"{'PASS' if ok else 'FAIL'} {name}: {detail}")


def finish(ok, why=""):
    log(f"finish ok={ok} why={why}")
    R["ok"] = ok and all(c["ok"] for c in R["checks"]); R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def level():
    # a fresh name per run: new_level refuses an existing path and neither os.remove nor delete_asset clears it reliably (as hrot_snd_demo.py,
    # 2026-11-22: "An asset already exists at this location" - and the editor then crashed on its way out); earlier runs' files go from disk
    for f in glob.glob(os.path.join(unreal.Paths.project_content_dir(), "Maps", "DpsCards*.umap")):
        try: os.remove(f)
        except OSError: pass
    if not LES().new_level("/Game/Maps/DpsCards_%d" % int(time.time())): raise RuntimeError("new_level failed")   # (a retry after a failed save crashes the editor)
    floor = EAS().spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0, 0, 0))
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane")); floor.set_actor_scale3d(unreal.Vector(8000, 8000, 1))
    wall = EAS().spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(5200, 1150, 600))   # ENU (52, -11.5) -> Unreal y = +1150
    wall.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Cube")); wall.set_actor_scale3d(unreal.Vector(0.5, 9, 12))
    sun = EAS().spawn_actor_from_class(unreal.DirectionalLight, unreal.Vector(0, 0, 1000), unreal.Rotator(pitch=-40, yaw=60, roll=0))
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    EAS().spawn_actor_from_class(unreal.SkyAtmosphere, unreal.Vector(0, 0, 0))
    sky = EAS().spawn_actor_from_class(unreal.SkyLight, unreal.Vector(0, 0, 500)); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "r.VSync 0"): unreal.SystemLibrary.execute_console_command(None, c)
    unreal.EditorLevelLibrary.get_editor_world().get_world_settings().set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
    if not LES().save_current_level(): raise RuntimeError("save failed")   # as the travel test: Play on an UNSAVED level ends the unattended editor
    LES().editor_set_viewport_realtime(True)
    LES().editor_request_begin_play()
    S.update(phase="join", t=time.time() + 8)


def join():
    gw = UES().get_game_world()
    if gw is None: raise RuntimeError("PIE did not start")
    w = unreal.HrotDpsWorld.for_world(gw)
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    # the TEST renders at 100 % (only the scene sets this): an upscaled frame would hide whether the gizmo lines are anti-aliased
    for c in ("r.ScreenPercentage 100", "r.SecondaryScreenPercentage.GameViewport 100"): unreal.SystemLibrary.execute_console_command(gw, c)
    sp = [unreal.SystemLibrary.get_console_variable_float_value(n) for n in ("r.ScreenPercentage", "r.SecondaryScreenPercentage.GameViewport")]
    log(f"screen percentage {sp[0]} / secondary {sp[1]} (the scene forces 100)")
    w.set_editor_property("label_mode", 3)
    sub = unreal.get_engine_subsystem(unreal.HrotDpsSubsystem)
    sub.fake_station_start(0.0)
    sub.fake_station_session(1, SIM0)
    for (i, dom, side, name, p) in ENTS:
        sub.fake_station_entity_as(i, dom, side, name, SIM0, unreal.Vector(*p), unreal.Vector(0, 0, 0), 90.0)
    # DD-025: a body-less unit and a man with a Broken mind, each with its EntityStatus
    sub.fake_station_unit(9, 1, "Alpha", SIM0, unreal.Vector(12, 4, 0))
    sub.fake_station_status(9, "SFGPUCI----D---", 0, 0, 7, 9, 0.3, 0, -1, 0, "Advance", "Assault")
    sub.fake_station_entity_as(10, PERSON, 1, "Broken man", SIM0, unreal.Vector(12, -4, 0), unreal.Vector(0, 0, 0), 90.0)
    sub.fake_station_status(10, "", 30, 100, 0, 0, -1, 0.8, 0.6, 3, "Flee", "")
    sub.fake_station_clock(True, SIM0, 1.0, 1, 0)
    S.update(w=w, sub=sub, gw=gw, phase="camera", t=time.time() + 3.0)
    log("joined, entities sent")


def camera():
    pawn = unreal.GameplayStatics.get_player_pawn(S["gw"], 0)
    pawn.place_at_rotator(S["w"].enu_m_to_unreal(unreal.Vector(0, 0, 2)), unreal.Rotator(pitch=-1.5, yaw=0, roll=0), 90.0)
    S.update(phase="shoot", t=time.time() + 3.0, modes=[3, 2, 1], mode=None)


def shoot():
    w = S["w"]
    if S["mode"] is None:
        if not S["modes"]: S.update(phase="sheet", t=time.time() + 0.5); return
        S["mode"] = S["modes"].pop(0); w.set_editor_property("label_mode", S["mode"]); S["t"] = time.time() + 1.5; return
    m = S["mode"]
    desc = w.describe_labels(); R.setdefault("labels", {})[str(m)] = desc
    log(f"mode {m}: {desc}")
    n = int(desc.split(":")[1].split()[0]) if ":" in desc and desc.split(":")[1].split()[0].isdigit() else -1
    # mode 3: 1,2,3,8,10 full + 9 the unit + 4 (shown - the wall hides it by depth, which the count cannot see) + 5 name only + 7 aircraft (never cut) = 9; 6 none
    # mode 2: the unit 9 + the aircraft 7
    want = {3: 9, 2: 2, 1: 0}[m]
    check(f"mode {m}: cards shown", n == want, f"{n} (want {want}) - {desc}")
    path = os.path.join(JOB["shots"], f"labels-mode{m}.png").replace("\\", "/")
    if os.path.exists(path): os.remove(path)
    unreal.SystemLibrary.execute_console_command(S["gw"], f'HighResShot 1 filename="{path}"')
    R["shots"].append(path)
    S.update(mode=None, t=time.time() + 2.5)


def sheet():
    path = os.path.join(JOB["shots"], "labels-symbols.png").replace("\\", "/")
    ok = unreal.HrotCardLayer.export_symbol_sheet(S["gw"], path)
    check("symbol sheet exported", ok, path)
    R["shots"].append(path)
    # U-18z: HROT's gizmos - ask for a layer, the fake station publishes its test pattern near the units, it is drawn
    S["w"].set_editor_property("label_mode", 1)
    S["w"].set_gizmo_layers(["Paths"])
    S["sub"].fake_station_gizmo_frame("Paths", unreal.Vector(20, -2, 0), 6)
    S.update(phase="gizmo", t=time.time() + 2.0)


def gizmo():
    d = S["w"].describe_gizmos(); R["gizmos"] = d
    check("gizmos: the layer arrived with its 6 primitives", "1 layers shown, 6 primitives" in d, d)
    path = os.path.join(JOB["shots"], "gizmos.png").replace("\\", "/")
    unreal.SystemLibrary.execute_console_command(S["gw"], f'HighResShot 1 filename="{path}"')
    R["shots"].append(path)
    # the lines are px wide (rebuilt as the camera moves): the same pattern from 3x farther, still continuous
    pawn = unreal.GameplayStatics.get_player_pawn(S["gw"], 0)
    pawn.place_at_rotator(S["w"].enu_m_to_unreal(unreal.Vector(-40, 0, 2)), unreal.Rotator(pitch=-1.5, yaw=0, roll=0), 90.0)
    S.update(phase="gizmofar", t=time.time() + 2.5)


def gizmofar():
    path = os.path.join(JOB["shots"], "gizmos-far.png").replace("\\", "/")
    unreal.SystemLibrary.execute_console_command(S["gw"], f'HighResShot 1 filename="{path}"')
    R["shots"].append(path)
    # HROT's 3-D map view: ~25 m above the pattern, ~25 m back, pitched down 45 deg - the pattern fills the frame; the look variants of the lines
    pawn = unreal.GameplayStatics.get_player_pawn(S["gw"], 0)
    pawn.place_at_rotator(S["w"].enu_m_to_unreal(unreal.Vector(-5, -2, 25)), unreal.Rotator(pitch=-45, yaw=0, roll=0), 90.0)
    S.update(phase="gizmotop", t=time.time() + 2.0, variants=[("a", "", 1.5), ("b", "", 2.5), ("c", "NoRaa", 2.5), ("e", "Post", 2.5)], variant=None)


def gizmotop():
    w = S["w"]
    if S["variant"] is None:
        if not S["variants"]: S.update(phase="voice", t=time.time() + 1.0); return
        S["variant"] = S["variants"].pop(0); _, mat, floor = S["variant"]
        w.set_editor_property("gizmo_line_material", mat); w.set_editor_property("gizmo_line_px_floor", floor)
        S["t"] = time.time() + 3.0; return   # (the first use of a material compiles its shaders)
    v = S["variant"][0]
    path = os.path.join(JOB["shots"], f"gizmos-top-{v}.png").replace("\\", "/")
    unreal.SystemLibrary.execute_console_command(S["gw"], f'HighResShot 1 filename="{path}"')
    R["shots"].append(path)
    S.update(variant=None, t=time.time() + 1.5)


def voice():
    """U-19b: a fake Report + Utterance are spoken (the chain beep > sgt > line > squelch, no missing clip), then an Order in the earpiece"""
    sub, w, gw = S["sub"], S["w"], S["gw"]
    P, SND = unreal.HrotVoicePlayer.for_world(gw), unreal.HrotSoundscape.for_world(gw)
    st = S.setdefault("v", {"step": 0})
    if st["step"] == 0:
        st["missing0"] = SND.get_editor_property("library_missing")
        sub.fake_station_report(9, 0, 4, 0, 0, 0, 0, 0, 2, True, SIM0)           # Lace, Kia 2
        sub.fake_station_utterance(1, "CmdMove", SIM0)
        st["step"] = 1; S["t"] = time.time() + 8.5; return
    if st["step"] == 1:
        log("voice: " + P.describe()); R["voice"] = P.describe(); played = P.played_text().split(" > ")
        radio = [c for c in played if c != "vox/cmd_move"]
        want = ["radio/beep", "vox/sgt_brown", "vox/rep_losses", "radio/squelch"]
        check("voice: the report's chain beep > sgt > rep_losses > squelch", radio == want, " > ".join(played))
        check("voice: the shout cmd_move played", "vox/cmd_move" in played, " > ".join(played))
        # U-19f: the same sequence is on the sound log (the transcript UHrotDpsSubsystem.get_sound_log_lines reads): beep, sgt, the report's line with its HROT meaning, squelch
        sl = list(unreal.HrotDpsSubsystem.get_sound_log_lines()); R["sound_log"] = sl
        pos = [next((i for i, l in enumerate(sl) if k in l), -1) for k in ("radio/beep", "vox/sgt_brown", "Report Lace from unit 9", "radio/squelch")]
        check("voice: the sound log lists beep > sgt > the report (with its meaning) > squelch, in that order", all(i >= 0 for i in pos) and pos == sorted(pos), f"{pos} - {sl}")
        check("voice: the sound log names the shout and who said it", any("WORLD" in l and "man 1 Utterance CmdMove" in l for l in sl), str(sl))
        check("voice: no clip missing from the library", SND.get_editor_property("library_missing") == st["missing0"], f"LibraryMissing {st['missing0']} -> {SND.get_editor_property('library_missing')}")
        check("voice: 2 transmissions, none dropped", P.get_editor_property("transmissions") == 2 and P.get_editor_property("dropped") == 0, P.describe())
        # the earpiece: follow man 1 (side 1); his net is unit 9 (the nearest same-side unit)
        pawn = unreal.GameplayStatics.get_player_pawn(gw, 0)
        pawn.follow(1, 14.0)
        st["step"] = 2; S["t"] = time.time() + 1.5; return
    if st["step"] == 2:
        check("voice: the followed man's net is unit 9", w.listener_net() == 9, f"listener_net {w.listener_net()}")
        st["ear0"] = P.get_editor_property("earpiece")
        sub.fake_station_order(9, 0, "Move out", SIM0)
        st["step"] = 3; S["t"] = time.time() + 9.5; return
    if st["step"] == 3:
        played = P.played_text().split(" > "); R["voice2"] = P.describe(); log("voice: " + P.describe())
        check("voice: the order is heard in the earpiece", P.get_editor_property("earpiece") == st["ear0"] + 1, P.describe())
        check("voice: the order's line and the receiver's answer", "vox/ord_move" in played and played[-2] == "vox/ack_order", " > ".join(played[-6:]))
        sl2 = list(unreal.HrotDpsSubsystem.get_sound_log_lines())   # U-19f
        check("voice: the sound log shows the order on the RADIO earpiece line", any("RADIO" in l and "earpiece" in l and "Order Move out" in l for l in sl2), str(sl2[-8:]))
        check("voice: no clip missing after the order", SND.get_editor_property("library_missing") == st["missing0"], f"LibraryMissing {SND.get_editor_property('library_missing')}")
        S.update(phase="burst", t=time.time() + 0.5)


def burst():
    """U-19c (DD-027): a fake 3-round burst plays ONE clip (wpn/rifle_b3) - no per-round rifle sound for indices 1 and 2; a burst that stops
    after 2 of 4 rounds is faded out"""
    sub, w, gw = S["sub"], S["w"], S["gw"]
    st = S.setdefault("b", {"step": 0})
    at, vel = unreal.Vector(30, -3, 1.5), unreal.Vector(300, 0, 0)
    if st["step"] == 0:
        st["e0"] = dict(w.event_counts())
        SR = unreal.HrotShotRenderer.for_world(gw)
        st["sp0"], st["h0"] = SR.get_editor_property("sounds_played"), SR.get_editor_property("held_started")
        sub.fake_station_burst_round(SIM0, 1, at, vel, 77, 3, 0)
        st["step"] = 1; S["t"] = time.time() + 0.1; return
    if st["step"] == 1:
        sub.fake_station_burst_round(SIM0, 1, at, vel, 77, 3, 1)
        sub.fake_station_burst_round(SIM0, 1, at, vel, 77, 3, 2)
        st["step"] = 2; S["t"] = time.time() + 1.0; return
    if st["step"] == 2:
        e, e0 = dict(w.event_counts()), st["e0"]
        d = lambda k: e.get(k, 0) - e0.get(k, 0)
        R["burst"] = {k: d(k) for k in e if d(k)}
        check("burst: exactly ONE clip started, the 3-round rifle clip", d("burst clip wpn/rifle_b3") == 1, str(R["burst"]))
        SR = unreal.HrotShotRenderer.for_world(gw)
        sp, hs = SR.get_editor_property("sounds_played") - st["sp0"], SR.get_editor_property("held_started") - st["h0"]
        check("burst: indices 1 and 2 covered, ONE sound played in all (no per-round rifle sound)", d("burst covered") == 2 and sp == 1 and hs == 1, f"{R['burst']}; sounds played {sp}, held {hs}")
        check("burst: the whole burst needed no cut", d("burst cut") == 0, str(R["burst"]))
        SND = unreal.HrotSoundscape.for_world(gw)
        check("burst: no clip missing from the library", SND.get_editor_property("library_missing") == S["v"]["missing0"], f"LibraryMissing {SND.get_editor_property('library_missing')}")
        # a burst of 4 that stops after 2 rounds: its clip is cut
        sub.fake_station_burst_round(SIM0, 1, at, vel, 78, 4, 0)
        sub.fake_station_burst_round(SIM0, 1, at, vel, 78, 4, 1)
        st["step"] = 3; S["t"] = time.time() + 1.2; return
    if st["step"] == 3:
        e, e0 = dict(w.event_counts()), st["e0"]
        d = lambda k: e.get(k, 0) - e0.get(k, 0)
        R["burst2"] = {k: d(k) for k in e if d(k)}
        check("burst: a burst that stops is faded out (one cut)", d("burst clip wpn/rifle_b4") == 1 and d("burst cut") == 1, str(R["burst2"]))
        S.update(phase="ambience", t=time.time() + 0.5)


def ambience():
    """U-19d (DD-028): a fake battle.city bed and a positioned alarm.airraid row start their loops (the bed 6 dB down: simulated shots were just heard),
    a global rain row raises RainAmount / WindAmount; the dispose fades the loops (2 s) and the weather row ends"""
    sub, w, gw = S["sub"], S["w"], S["gw"]
    A, SND = unreal.HrotAmbiencePlayer.for_world(gw), unreal.HrotSoundscape.for_world(gw)
    st = S.setdefault("a", {"step": 0})
    if st["step"] == 0:
        st["missing0"] = SND.get_editor_property("library_missing"); st["s0"] = A.get_editor_property("loops_started")
        sub.fake_station_ambience(1, "battle.city", 0.8, False, unreal.Vector(0, 0, 0), 0.0, False)
        sub.fake_station_ambience(2, "alarm.airraid", 1.0, True, unreal.Vector(40, 0, 0), 300.0, False)
        sub.fake_station_ambience(3, "weird.unknown", 1.0, False, unreal.Vector(0, 0, 0), 0.0, False)
        sub.fake_station_weather(1, [], 0.0, 7.5, 2, 4.0, False)   # a global row: rain 4 mm/h, wind 7.5 m/s
        st["step"] = 1; S["t"] = time.time() + 5.0; return
    if st["step"] == 1:
        txt = A.loops_text(); R["ambience"] = A.describe(); log("ambience: " + A.describe())
        check("ambience: the bed and the positioned alarm started (2 loops, the unknown kind none)", A.get_editor_property("loops_started") - st["s0"] == 2 and "amb/war_city" in txt and "amb/al_airraid" in txt, A.describe())
        bed = [x for x in txt.split(", ") if x.startswith("amb/war_city")]
        check("ambience: the bed is 6 dB down under the simulated fire (0.8 x 0.5)", bool(bed) and abs(float(bed[0].split()[-1]) - 0.4) < 0.02, str(bed))
        check("ambience: the weather row sets the soundscape's rain and wind", abs(SND.get_editor_property("rain_amount") - 0.5) < 0.01 and abs(SND.get_editor_property("wind_amount") - 0.5) < 0.01 and w.describe_weather() == "0.50 0.50",
              f"rain {SND.get_editor_property('rain_amount')} wind {SND.get_editor_property('wind_amount')} - {w.describe_weather()}")
        check("ambience: no clip missing from the library", SND.get_editor_property("library_missing") == st["missing0"], f"LibraryMissing {SND.get_editor_property('library_missing')}")
        sub.fake_station_ambience(1, "", 0.0, False, unreal.Vector(0, 0, 0), 0.0, True)    # the rows end
        sub.fake_station_ambience(2, "", 0.0, False, unreal.Vector(0, 0, 0), 0.0, True)
        sub.fake_station_weather(1, [], 0.0, 0.0, 0, 0.0, True)
        st["step"] = 2; S["t"] = time.time() + 4.0; return
    if st["step"] == 2:
        R["ambience2"] = A.describe(); log("ambience: " + A.describe())
        check("ambience: the dispose faded both loops out", A.get_editor_property("fades") >= 2 and A.get_editor_property("loops_playing") == 0 and A.loops_text() == "", A.describe())
        check("ambience: the weather row ended (the hand values are the soundscape's again)", w.describe_weather() == "none", w.describe_weather())
        S.update(phase="overlay", t=time.time() + 0.5)


def overlay():
    """U-19e (DD-022 P-8, F-2): a fake View with Fade 0.5 and a subtitle - ignored while the camera follows a man, drawn by the HUD (both, through the one
    painter) once it shows the director's view; the render job's frame with FrameRequest.Fade 1 is NOT run here (the render harness is hrot_dps_render7.py) -
    Hrot.Dps.Overlay rails the painter's maths"""
    sub, w, gw = S["sub"], S["w"], S["gw"]
    st = S.setdefault("o", {"step": 0})
    pawn = unreal.GameplayStatics.get_player_pawn(gw, 0)
    if st["step"] == 0:
        sub.fake_station_view_overlay(SIM0, unreal.Vector(0, 0, 5), 0.0, 60.0, 0.5, "Phase two", 3, 0.9)
        st["step"] = 1; S["t"] = time.time() + 2.0; return
    if st["step"] == 1:
        n = (w.get_editor_property("overlay_subtitles_drawn"), w.get_editor_property("overlay_fades_drawn"))
        check("overlay: a followed camera ignores the director's fade and subtitle (F-2)", n == (0, 0) and "follow" in pawn.describe(), f"drawn {n}; camera {pawn.describe()}")
        pawn.director()
        st["step"] = 2; S["t"] = time.time() + 2.5; return
    if st["step"] == 2:
        n = (w.get_editor_property("overlay_subtitles_drawn"), w.get_editor_property("overlay_fades_drawn"))
        R["overlay"] = {"subtitles": n[0], "fades": n[1], "camera": pawn.describe()}; log(f"overlay: drawn {n}, camera {pawn.describe()}")
        check("overlay: in director mode the HUD drew the subtitle and the fade", n[0] > 0 and n[1] > 0, f"subtitles {n[0]}, fades {n[1]}; camera {pawn.describe()}")
        sub.fake_station_view_overlay(SIM0, unreal.Vector(0, 0, 5), 0.0, 60.0, 0.0, "", 1, 1.0)   # the dip is over, the caption gone
        st["step"] = 3; S["t"] = time.time() + 1.0; return
    if st["step"] == 3:
        st["n3"] = (w.get_editor_property("overlay_subtitles_drawn"), w.get_editor_property("overlay_fades_drawn"))
        st["step"] = 4; S["t"] = time.time() + 1.0; return
    if st["step"] == 4:
        n = (w.get_editor_property("overlay_subtitles_drawn"), w.get_editor_property("overlay_fades_drawn"))
        check("overlay: fade 0 and no subtitle draw nothing more", n == st["n3"], f"{st['n3']} -> {n}")
        S.update(phase="pick", t=time.time() + 0.5)


def pick():
    """U-19g: click-to-select on a SOLDIER (a skeletal body is invisible to the visibility trace): man 1 stands 30 m ahead; a left click at his projected
    screen position selects him and the HUD draws the selection brackets; a right click selects him and opens the unit menu"""
    gw, w = S["gw"], S["w"]
    pawn = unreal.GameplayStatics.get_player_pawn(gw, 0)
    st = S.setdefault("p", {"step": 0})
    if st["step"] == 0:
        pawn.place_at_rotator(w.enu_m_to_unreal(unreal.Vector(0, 0, 2)), unreal.Rotator(pitch=-1.5, yaw=0, roll=0), 90.0)
        pawn.select(0)
        st["step"] = 1; S["t"] = time.time() + 3.5; return   # (a skeletal body's bounds are those of its last RENDER)
    if st["step"] == 1:
        sp = pawn.screen_of(1)
        check("pick: man 1, 30 m ahead, projects onto the screen", sp.x >= 0 and sp.y >= 0, f"({sp.x:.0f}, {sp.y:.0f})")
        got = pawn.test_click(unreal.Vector2D(sp.x + 5, sp.y - 4), False)
        check("pick: a left click 6 px from him selects him", got == 1 and pawn.selected_id() == 1, f"picked {got}, selected {pawn.selected_id()}")
        st["m0"] = pawn.get_editor_property("selection_marks_drawn"); st["sp"] = sp
        pawn.test_click(unreal.Vector2D(5, 5), False)
        check("pick: a click on nothing deselects", pawn.selected_id() == 0, f"selected {pawn.selected_id()}")
        pawn.test_click(unreal.Vector2D(sp.x, sp.y), False)
        st["step"] = 2; S["t"] = time.time() + 1.0; return
    if st["step"] == 2:
        n = pawn.get_editor_property("selection_marks_drawn") - st["m0"]
        check("pick: the selected man is VISIBLY marked (the HUD drew the brackets)", n > 0, f"{n} frames")
        sp = st["sp"]
        pawn.select(0)
        got = pawn.test_click(unreal.Vector2D(sp.x, sp.y), True)
        check("pick: a right click on him selects him and opens the unit menu", got == 1 and pawn.selected_id() == 1 and pawn.is_menu_open(), f"picked {got}, menu {pawn.is_menu_open()}")
        pawn.close_menu()
        # U-19g: a man is a SEGMENT feet -> head: his BODY (not only his head) picks him; 30 px beside him misses
        r = pawn.screen_rect_of(1); h = r.max.y - r.min.y; x = (r.min.x + r.max.x) / 2
        low = unreal.Vector2D(x, r.min.y + 0.7 * h)
        pawn.select(0); got = pawn.test_click(low, False)
        check("pick: a click on his LOWER body (70 % down his rectangle) selects him", got == 1, f"picked {got} at ({low.x:.0f}, {low.y:.0f}), rect y {r.min.y:.0f}..{r.max.y:.0f}")
        pawn.select(0); got = pawn.test_click(unreal.Vector2D(x + 30 + (r.max.x - r.min.x) / 2, r.min.y + 0.5 * h), False)
        check("pick: a click 30 px beside his body misses him", got != 1, f"picked {got}")
        # the vehicle's brackets enclose ITS body: a part attached to it (an M2 mount, an antenna), 6 m above, does not stretch them
        veh = w.actor_of(2)
        bt = w.body_box_text(2); bz = [float(v) for v in bt.split()]
        part = w.attach_test_part(2, 6.0)
        st["bt"] = bt; st["bz"] = bz; st["part"] = part
        st["step"] = 3; S["t"] = time.time() + 1.0; return
    if st["step"] == 3:
        part, bz = st["part"], st["bz"]
        bt2 = w.body_box_text(2)
        check("pick: the vehicle's body box ignores an attached part 6 m above it", bt2 == st["bt"] and (bz[5] - bz[2]) < 5.0, f"before {st['bt']}, after {bt2}")
        r = pawn.screen_rect_of(2)
        res = unreal.GameplayStatics.project_world_to_screen(unreal.GameplayStatics.get_player_controller(gw, 0), part.get_actor_location(), False)
        ok, ps = res if isinstance(res, tuple) else (True, res)
        check("pick: the selection rectangle of the vehicle stops below the part (encloses the body, not the part)", r.is_valid and ok and r.min.y > ps.y, f"rect y {r.min.y:.0f}..{r.max.y:.0f}, part at y {ps.y:.0f}")
        pawn.select(2)
        n0 = pawn.get_editor_property("selection_marks_drawn")
        st["n0"] = n0; st["part"].destroy_actor()
        st["step"] = 4; S["t"] = time.time() + 0.5; return
    if st["step"] == 4:
        S["w"].set_editor_property("label_mode", 1)
        # U-19g: entering and leaving his eyes keeps the view direction (a jump to a fixed direction was reported live)
        pawn.follow(1, 3.0)
        st["step"] = 5; S["t"] = time.time() + 1.5; return
    if st["step"] == 5:
        pawn.orbit(40.0, 10.0); pawn.step_follow_for_test()
        import re
        dist = lambda: float(re.search(r"at ([0-9.]+) m", pawn.describe()).group(1))
        ang = lambda a, b: abs((a.yaw - b.yaw + 180) % 360 - 180) + abs(a.pitch - b.pitch)
        worst = 0.0; stages = 0; last = pawn.view_rotation()
        for i in range(40):                       # in: wheel until under 0.4 m, comparing each step's rotation with the last
            pawn.wheel(1.0); pawn.step_follow_for_test(); r1 = pawn.view_rotation(); d = ang(last, r1); worst = max(worst, d); last = r1
            if dist() < 0.4: stages += 1; break
        for i in range(40):                       # out
            pawn.wheel(-1.0); pawn.step_follow_for_test(); r1 = pawn.view_rotation(); d = ang(last, r1); worst = max(worst, d); last = r1
            if dist() >= 0.4: stages += 1; break
        check("follow: the eyes stage was entered and left", stages == 2, f"{stages} stage changes; {pawn.describe()}")
        check("follow: crossing the eyes stage (in and out) never turns the view by 5 deg or more in one step", worst < 5.0, f"worst single-step change {worst:.2f} deg")
        S.update(phase="end", t=time.time() + 0.5)


def tick(dt):
    if S.get("busy"): return   # new_level / PIE pump Slate: a re-entered tick would start a second new_level inside the first (its save fails, a retry crashes)
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
        elif ph == "camera": camera()
        elif ph == "shoot": shoot()
        elif ph == "sheet": sheet()
        elif ph == "gizmo": gizmo()
        elif ph == "gizmofar": gizmofar()
        elif ph == "gizmotop": gizmotop()
        elif ph == "voice": voice()
        elif ph == "burst": burst()
        elif ph == "ambience": ambience()
        elif ph == "overlay": overlay()
        elif ph == "pick": pick()
        elif ph == "end": LES().editor_request_end_play(); finish(True)
        if time.time() - T0 > 300: finish(False, "timeout")
    except Exception:
        log(traceback.format_exc()); finish(False, "exception")


S["handle"] = unreal.register_slate_post_tick_callback(tick)
# -ExecutePythonScript ends the editor when the script returns; the ticks need it alive (the travel script does the same)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
