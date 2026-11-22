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
    if not LES().new_level("/Game/Maps/DpsCards"): raise RuntimeError("new_level failed")   # (a retry after a failed save crashes the editor)
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
    S.update(phase="end", t=time.time() + 2.5)


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
        elif ph == "end": LES().editor_request_end_play(); finish(True)
        if time.time() - T0 > 240: finish(False, "timeout")
    except Exception:
        log(traceback.format_exc()); finish(False, "exception")


S["handle"] = unreal.register_slate_post_tick_callback(tick)
# -ExecutePythonScript ends the editor when the script returns; the ticks need it alive (the travel script does the same)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
