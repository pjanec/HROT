"""hrot_vehiclelab.py — runs in the full Unreal EDITOR (-ExecutePythonScript), CE-4651 (U-17): vehicles in their states, pictured.

    HROT_VEHICLELAB_JOB = { "kit", "vehicles": [{"label", "kit"?, "mesh"?, "yaw", "pitch", "damage", "heading"?}], "fire": <label>,
                            "firePics": [s…], "spacing", "rowBack", "rowUp", "closeBack", "shots", "out" }
    a vehicle is a kit (its own or the job's) — or "mesh": a bare static mesh (a gallery of a pack as it comes, to judge its scale
    and which way it faces); "heading" = the actor's yaw (default −60: three-quarter toward the camera)

A row of AHrotVehicle (one kit) in a new level, each with its state (turret yaw / gun pitch / damage), played in Simulate (the
damage effects and the shot renderer run in the game world); pictures timed by GAME time: the row at 3 s (smoke grown), the firing
vehicle 0.03 s after its main gun fires, and a close look at each damaged one.
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_VEHICLELAB_JOB"], encoding="utf-8"))
R = {"log": [], "shots": []}
S = {"handle": None, "phase": "level", "busy": False}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()
SPACING = float(JOB.get("spacing", 1100.0))   # cm between vehicles along X


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-VLAB] {m}"); R["log"].append(m)


def finish(ok, why=""):
    R["ok"] = ok; R["why"] = why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    unreal.SystemLibrary.quit_editor()


def spawn(cls, loc, rot=(0, 0, 0), label=None):
    a = EAS().spawn_actor_from_class(cls, unreal.Vector(*loc), unreal.Rotator(pitch=rot[0], yaw=rot[1], roll=rot[2]))
    if a and label: a.set_actor_label(label)
    return a


def level():
    if unreal.EditorAssetLibrary.does_asset_exist("/Game/Maps/VehicleLab"): unreal.EditorAssetLibrary.delete_asset("/Game/Maps/VehicleLab")
    if not LES().new_level("/Game/Maps/VehicleLab"): raise RuntimeError("new_level failed")
    floor = spawn(unreal.StaticMeshActor, (0, 0, 0), label="Floor")
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
    floor.set_actor_scale3d(unreal.Vector(400, 400, 1))
    sun = spawn(unreal.DirectionalLight, (0, 0, 1000), (-35, 60, 0), "Sun")
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    spawn(unreal.SkyAtmosphere, (0, 0, 0), label="Sky")
    sky = spawn(unreal.SkyLight, (0, 0, 500), label="SkyLight"); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0", "r.DontLimitOnBattery 1"):
        unreal.SystemLibrary.execute_console_command(None, c)
    LES().editor_set_viewport_realtime(True)
    for i, v in enumerate(JOB["vehicles"]):
        at, rot = (i * SPACING, 0, float(v.get("z", 0))), (0, float(v.get("heading", -60)), 0)
        if v.get("mesh"):   # a bare mesh, as the pack ships it
            a = spawn(unreal.StaticMeshActor, at, rot, v["label"])
            a.static_mesh_component.set_static_mesh(unreal.load_asset(v["mesh"])); a.static_mesh_component.set_mobility(unreal.ComponentMobility.MOVABLE)
            continue
        kit = unreal.load_asset(v.get("kit") or JOB["kit"])
        if kit is None: raise RuntimeError("no kit " + str(v.get("kit") or JOB["kit"]))
        a = spawn(unreal.HrotVehicle, at, rot, v["label"])
        setattr(a, "kit", kit)
        setattr(a, "turret_yaw", float(v.get("yaw", 0))); setattr(a, "gun_pitch", float(v.get("pitch", 0)))
        setattr(a, "damage", getattr(unreal.HrotVehicleDamage, v.get("damage", "INTACT")))
        a.build()
    log(f"{len(JOB['vehicles'])} vehicles")
    LES().editor_play_simulate()
    S.update(phase="find", t=time.time() + 5)


def game_now():
    w = UES().get_game_world()
    return unreal.GameplayStatics.get_time_seconds(w) if w else 0.0


def camera(at, back, up=350.0):
    """Looks at `at` from `back` cm toward −Y (three-quarter: a little from +X), `up` cm high."""
    cam = unreal.Vector(at.x + back * 0.35, at.y - back, up)
    UES().set_level_viewport_camera_info(cam, unreal.MathLibrary.find_look_at_rotation(cam, unreal.Vector(at.x, at.y, 150.0)))


def picture(name):
    path = os.path.join(JOB["shots"], f"vehicle-{name}.png").replace("\\", "/")
    if os.path.exists(path): os.remove(path)
    unreal.AutomationLibrary.take_high_res_screenshot(1600, 900, path, None)
    S["pending"] = (path, time.time())


def tick(dt):
    if S["busy"]: return
    S["busy"] = True
    try:
        step()
    except Exception:
        finish(False, traceback.format_exc())
    finally:
        S["busy"] = False


def step():
    if S.get("pending"):   # wait for the picture's file
        path, t0 = S["pending"]
        if not ((os.path.exists(path) and os.path.getsize(path) > 0) or time.time() - t0 > 30): return
        R["shots"].append(path); S["pending"] = None
    p = S["phase"]
    if p == "level": level(); return
    if p == "find":
        if time.time() < S["t"]: return
        w = UES().get_game_world()
        if w is None: raise RuntimeError("Simulate did not start")
        S["v"] = {a.get_actor_label(): a for a in unreal.GameplayStatics.get_all_actors_of_class(w, unreal.Actor)
                  if a.get_actor_label() in {v["label"] for v in JOB["vehicles"]}}
        log(f"play: {sorted(S['v'])}")
        S["queue"] = [("row", None)] + [("close", v["label"]) for v in JOB["vehicles"] if v.get("damage", "INTACT") != "INTACT"] + \
                     ([("fire", JOB["fire"])] if JOB.get("fire") else [])
        S.update(phase="next", ready=game_now() + 3.0); return
    if p == "next":
        if game_now() < S["ready"]: return
        if not S["queue"]:
            LES().editor_request_end_play(); S.update(phase="end", t=time.time() + 3); return
        kind, label = S["queue"].pop(0)
        n = len(JOB["vehicles"])
        if kind == "row":
            camera(unreal.Vector((n - 1) * SPACING / 2, 0, 0), float(JOB.get("rowBack", 2600.0)), float(JOB.get("rowUp", 700.0)))
            S.update(phase="shoot", name="row", at=game_now() + 0.3)
        elif kind == "close":
            camera(S["v"][label].get_actor_location(), float(JOB.get("closeBack", 1100.0)), 450.0)
            S.update(phase="shoot", name=f"close-{label}", at=game_now() + 0.3)
        else:
            v = S["v"][label]; camera(v.get_actor_location(), 1300.0, 400.0)
            S.update(phase="fire", fire=v, at=game_now() + 0.3)
        return
    if p == "fire":
        if game_now() < S["at"]: return
        S["fire"].fire_main_gun(); log(f"{S['fire'].get_actor_label()}: main gun fired")
        t0 = game_now()   # ⭐ a series: at once (the flash), then the blast and the smoke as they grow
        S["series"] = [(f"fire-{S['fire'].get_actor_label()}-{t:.2f}s", t0 + t) for t in JOB.get("firePics", [0.0, 0.5, 2.0])]
        name, at = S["series"].pop(0); S.update(phase="shoot", name=name, at=at); return
    if p == "shoot":
        if game_now() < S["at"]: return
        picture(S["name"])
        if S.get("series"):
            name, at = S["series"].pop(0); S.update(phase="shoot", name=name, at=at); return
        S.update(phase="next", ready=game_now() + 0.2); return
    if p == "end":
        if time.time() >= S["t"]: finish(True)


S["handle"] = unreal.register_slate_post_tick_callback(tick)

unreal.EditorPythonScripting.set_keep_python_script_alive(True)   # else -ExecutePythonScript quits the editor when the file ends
