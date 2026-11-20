"""hrot_vehiclelab.py — runs in the full Unreal EDITOR (-ExecutePythonScript), CE-4651 (U-17): vehicles in their states, pictured.

    HROT_VEHICLELAB_JOB = { "kit", "vehicles": [{"label", "kit"?, "mesh"?, "yaw", "pitch", "damage", "heading"?}], "fire": <label>,
                            "firePics": [s…], "spacing", "rowBack", "rowUp", "closeBack", "shots", "out" }
    a vehicle is a kit (its own or the job's) — or "mesh": a bare static mesh (a gallery of a pack as it comes, to judge its scale
    and which way it faces) — or "skeletal" + "anim" (+ "loop": false = hold the last frame): an animal playing its own clip;
    "heading" = the actor's yaw (default −60: three-quarter toward the camera); "at": [x, y, z] places it (a grid) and "rowAt"
    aims the row picture at the grid's centre; "animal": a UHrotAnimalKind + "state" (+ "then": {"state", "after"}
    = a change in play, by game time) spawns an AHrotAnimal; "views": [{"name", "label" | "at", "back", "up", "lookZ", "az"}] = more pictures, any framing; "person": a soldier
    (hrot_person); "groundFire": {"radius", "intensity"}; on any entry, in play: "burn": intensity (UHrotBurning), "flamethrower":
    {"socket", "loc", "rot", "size", "firing"} (UHrotFlamethrower; a person's goes on its weapon)

A row of AHrotVehicle (one kit) in a new level, each with its state (turret yaw / gun pitch / damage), played in Simulate (the
damage effects and the shot renderer run in the game world); pictures timed by GAME time: the row at 3 s (smoke grown), the firing
vehicle 0.03 s after its main gun fires, and a close look at each damaged one.
"""
import json, math, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_VEHICLELAB_JOB"], encoding="utf-8"))
R = {"log": [], "shots": []}
S = {"handle": None, "phase": "level", "busy": False}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()
HERE = os.path.dirname(os.path.abspath(__file__))   # ⚠ measured: __file__ is gone inside the slate tick callback — taken here, at load
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
    n = len(JOB["vehicles"])   # ⚠ measured: a 400 m floor at the origin ended under the 7th aircraft (a black void behind it)
    floor = spawn(unreal.StaticMeshActor, ((n - 1) * SPACING / 2, 0, 0), label="Floor")
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
    floor.set_actor_scale3d(unreal.Vector(max(400.0, (n * SPACING + 40000) / 100.0), 1000, 1))
    sun = spawn(unreal.DirectionalLight, (0, 0, 1000), (-35, 60, 0), "Sun")
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    spawn(unreal.SkyAtmosphere, (0, 0, 0), label="Sky")
    sky = spawn(unreal.SkyLight, (0, 0, 500), label="SkyLight"); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0", "r.DontLimitOnBattery 1"):
        unreal.SystemLibrary.execute_console_command(None, c)
    LES().editor_set_viewport_realtime(True)
    for i, v in enumerate(JOB["vehicles"]):
        at, rot = tuple(v.get("at") or (i * SPACING, 0, float(v.get("z", 0)))), (0, float(v.get("heading", -60)), 0)
        if v.get("animal"):   # ⭐ an AHrotAnimal: its kind + its state, as HROT sets them ("then": {"state", "after"} = a change in play)
            a = spawn(unreal.HrotAnimal, at, rot, v["label"])
            setattr(a, "kind", unreal.load_asset(v["animal"]))
            a.set_state(getattr(unreal.HrotAnimalState, v.get("state", "IDLE")))
            continue
        if v.get("skeletal"):   # an animal (or any rigged body) playing one of its own clips — "loop": false holds the last frame (dead)
            a = spawn(unreal.SkeletalMeshActor, at, rot, v["label"])
            c = a.skeletal_mesh_component
            c.set_skeletal_mesh_asset(unreal.load_asset(v["skeletal"]))
            anim = unreal.load_asset(v["anim"])
            if anim is None: raise RuntimeError("no clip " + v["anim"])
            c.set_animation_mode(unreal.AnimationMode.ANIMATION_SINGLE_NODE)
            ad = c.get_editor_property("animation_data")   # ⭐ saved on the actor, so the Simulate copy plays it too (hrot_animlab)
            ad.set_editor_property("anim_to_play", anim); ad.set_editor_property("saved_looping", bool(v.get("loop", True)))
            ad.set_editor_property("saved_playing", True); ad.set_editor_property("saved_position", 0.0)
            c.set_editor_property("animation_data", ad)
            continue
        if v.get("person"):   # ⭐ a soldier (the ONE person setup, hrot_person.PersonKit): {"body", "state": {"base", "upper", "weapon", …}}
            if "kit" not in S:
                import sys; sys.path.insert(0, HERE)
                import hrot_person; S["kit"] = hrot_person.PersonKit(log)
            made = S["kit"].make(v["person"].get("body") or None, at, float(v.get("heading", 0)), v["label"], v["person"].get("state"), always_tick=True)
            if made is None: raise RuntimeError("person clips missing: " + v["label"])
            continue
        if v.get("groundFire"):   # ⭐ an AHrotGroundFire: {"radius", "intensity"}
            g = v["groundFire"]; a = spawn(unreal.HrotGroundFire, at, rot, v["label"])
            setattr(a, "radius", float(g.get("radius", 300))); setattr(a, "intensity", float(g.get("intensity", 1)))
            continue
        if v.get("fx"):   # an effect as the pack ships it ("rot": [pitch, yaw, roll], "scale") — a trial of its look, axis and size
            r = v.get("rot", (0, 0, 0))
            a = unreal.EditorLevelLibrary.spawn_actor_from_object(unreal.load_asset(v["fx"]), unreal.Vector(*at),
                                                                 unreal.Rotator(pitch=r[0], yaw=r[1], roll=r[2]))
            a.set_actor_label(v["label"]); a.set_actor_scale3d(unreal.Vector(*([float(v.get("scale", 1))] * 3)))
            continue
        if v.get("mesh"):   # a bare mesh, as the pack ships it
            a = spawn(unreal.StaticMeshActor, at, rot, v["label"])
            if v.get("scale"): a.set_actor_scale3d(unreal.Vector(*([float(v["scale"])] * 3)))
            b = unreal.load_asset(v["mesh"]).get_bounds()
            log(f"{v['label']}: bounds origin {b.origin.x:.0f}, {b.origin.y:.0f}, {b.origin.z:.0f} extent {b.box_extent.x:.0f}, {b.box_extent.y:.0f}, {b.box_extent.z:.0f}")
            a.static_mesh_component.set_static_mesh(unreal.load_asset(v["mesh"])); a.static_mesh_component.set_mobility(unreal.ComponentMobility.MOVABLE)
            continue
        kit = unreal.load_asset(v.get("kit") or JOB["kit"])
        if kit is None: raise RuntimeError("no kit " + str(v.get("kit") or JOB["kit"]))
        a = spawn(unreal.HrotVehicle, at, rot, v["label"])
        setattr(a, "kit", kit)
        setattr(a, "turret_yaw", float(v.get("yaw", 0))); setattr(a, "gun_pitch", float(v.get("pitch", 0)))
        setattr(a, "damage", getattr(unreal.HrotVehicleDamage, v.get("damage", "INTACT")))
        setattr(a, "flaming", bool(v.get("flaming", False)))   # the kit's flamethrower (HROT's state)
        a.build()
    log(f"{len(JOB['vehicles'])} vehicles")
    LES().editor_play_simulate()
    S.update(phase="find", t=time.time() + 5)


def game_now():
    w = UES().get_game_world()
    return unreal.GameplayStatics.get_time_seconds(w) if w else 0.0


def camera(at, back, up=350.0, look_z=150.0, az=None):
    """Looks at `at` (`look_z` cm high) from `back` cm toward −Y (three-quarter: a little from +X), `up` cm high — or, given `az`
    (degrees, 0 = from +X = the vehicle's nose when its heading is 0, 90 = from +Y), from exactly that direction."""
    if az is None: cam = unreal.Vector(at.x + back * 0.35, at.y - back, up)
    else: cam = unreal.Vector(at.x + back * math.cos(math.radians(az)), at.y + back * math.sin(math.radians(az)), up)
    UES().set_level_viewport_camera_info(cam, unreal.MathLibrary.find_look_at_rotation(cam, unreal.Vector(at.x, at.y, look_z)))


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
    for item in list(S.get("later") or []):   # state changes in play (an animal shot: alive -> dead), by game time
        label, then = item
        if game_now() >= S["t0"] + float(then.get("after", 1.0)):
            S["v"][label].set_state(getattr(unreal.HrotAnimalState, then["state"])); S["later"].remove(item)
            log(f"{label}: -> {then['state']}")
    if p == "find":
        if time.time() < S["t"]: return
        w = UES().get_game_world()
        if w is None: raise RuntimeError("Simulate did not start")
        S["v"] = {a.get_actor_label(): a for a in unreal.GameplayStatics.get_all_actors_of_class(w, unreal.Actor)
                  if a.get_actor_label() in {v["label"] for v in JOB["vehicles"]}}
        log(f"play: {sorted(S['v'])}")
        for v in JOB["vehicles"]:
            if not v.get("skeletal") or v["label"] not in S["v"]: continue
            # ⚠ measured: the saved single-node data alone left a one-shot (a death) STANDING in play — start it here, in the game
            # world; a one-shot is frozen on its last frame (the body on the ground)
            c, anim, loop = S["v"][v["label"]].skeletal_mesh_component, unreal.load_asset(v["anim"]), bool(v.get("loop", True))
            c.set_animation_mode(unreal.AnimationMode.ANIMATION_SINGLE_NODE); c.play_animation(anim, loop)
            if not loop: c.set_position(anim.get_play_length() * 0.995, False); c.stop()
        for v in JOB["vehicles"]:   # ⭐ fire set in PLAY, as HROT would: "burn" = intensity; "flamethrower" = {"socket", "loc", "rot", "size", "firing"}
            a = S["v"].get(v["label"])
            if a is None: continue
            if v.get("burn") is not None:
                b = unreal.HrotBurning.set_burning(a, float(v["burn"])); log(f"{v['label']}: burning {v['burn']} -> {b.num_fires() if b else 0} fires")
            if v.get("person") and v["person"].get("flame"):   # ⭐ a flamethrower soldier: the person state's own flamethrower (U-17f)
                ps = a.get_component_by_class(unreal.HrotPersonState)
                setattr(ps, "flamethrower", True); ps.set_flame_firing(True); log(f"{v['label']}: flame firing -> {ps.apply_flame() is not None}")
            if v.get("flamethrower"):
                f = v["flamethrower"]; parent = a.root_component
                if v.get("person"):   # the soldier's weapon: its muzzle
                    ps = a.get_component_by_class(unreal.HrotPersonState)
                    parent = ps.get_weapon_component() if ps else None
                    if parent is None: raise RuntimeError(v["label"] + ": no weapon component in play")
                r = f.get("rot", (0, 0, 0))
                off = unreal.Transform(location=unreal.Vector(*f.get("loc", (0, 0, 0))), rotation=unreal.Rotator(pitch=r[0], yaw=r[1], roll=r[2]))
                ft = unreal.HrotFlamethrower.attach(parent, unreal.Name(f.get("socket", "None")), off, float(f.get("size", 1)))
                ft.set_firing(bool(f.get("firing", True))); S.setdefault("flames", {})[v["label"]] = ft
                log(f"{v['label']}: flamethrower on {parent.get_name()} socket {f.get('socket', 'None')}")
        S["later"] = [(v["label"], v["then"]) for v in JOB["vehicles"] if v.get("then") and v["label"] in S["v"]]
        if S["later"]: S["t0"] = game_now()
        S["queue"] = [("row", None)] + [("close", v["label"]) for v in JOB["vehicles"] if v.get("damage", "INTACT") != "INTACT"] + \
                     [("view", w) for w in JOB.get("views", [])] + ([("fire", JOB["fire"])] if JOB.get("fire") else [])
        S.update(phase="next", ready=game_now() + 3.0); return
    if p == "next":
        if game_now() < S["ready"]: return
        if not S["queue"]:
            LES().editor_request_end_play(); S.update(phase="end", t=time.time() + 3); return
        kind, label = S["queue"].pop(0)
        n = len(JOB["vehicles"])
        if kind == "row":
            at = unreal.Vector(*JOB["rowAt"]) if JOB.get("rowAt") else unreal.Vector((n - 1) * SPACING / 2, 0, 0)   # a grid names its centre
            camera(at, float(JOB.get("rowBack", 2600.0)), float(JOB.get("rowUp", 700.0)))
            S.update(phase="shoot", name="row", at=game_now() + 0.3)
        elif kind == "close":
            camera(S["v"][label].get_actor_location(), float(JOB.get("closeBack", 1100.0)), 450.0)
            S.update(phase="shoot", name=f"close-{label}", at=game_now() + 0.3)
        elif kind == "view":   # {"name", "label" | "at", "back", "up", "lookZ"}: any framing — one aircraft, one row of animals
            w = label
            at = S["v"][w["label"]].get_actor_location() if w.get("label") else unreal.Vector(*w["at"])
            camera(at, float(w.get("back", 1100.0)), float(w.get("up", 450.0)), float(w.get("lookZ", 150.0)), w.get("az"))
            S.update(phase="shoot", name=w["name"], at=game_now() + 0.3)
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
