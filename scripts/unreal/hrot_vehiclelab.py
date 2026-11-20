"""hrot_vehiclelab.py — runs in the full Unreal EDITOR (-ExecutePythonScript), CE-4651 (U-17): vehicles in their states, pictured.

    HROT_VEHICLELAB_JOB = { "kit", "vehicles": [{"label", "kit"?, "mesh"?, "yaw", "pitch", "damage", "heading"?}], "fire": <label>,
                            "firePics": [s…], "spacing", "rowBack", "rowUp", "closeBack", "shots", "out" }
    a vehicle is a kit (its own or the job's) — or "mesh": a bare static mesh (a gallery of a pack as it comes, to judge its scale
    and which way it faces) — or "skeletal" + "anim" (+ "loop": false = hold the last frame): an animal playing its own clip;
    "heading" = the actor's yaw (default −60: three-quarter toward the camera); "at": [x, y, z] places it (a grid) and "rowAt"
    aims the row picture at the grid's centre; "animal": a UHrotAnimalKind + "state" (+ "then": {"state", "after"}
    = a change in play, by game time) spawns an AHrotAnimal; "views": [{"name", "label" | "at", "back", "up", "lookZ", "az"}] = more pictures, any framing; "person": a soldier
    (hrot_person); "groundFire": {"radius", "intensity"}; on any entry, in play: "burn": intensity (UHrotBurning), "flamethrower":
    {"socket", "loc", "rot", "size", "firing"} (UHrotFlamethrower; a person's goes on its weapon); "missile": a UHrotMissileKind ("fly" = a test flight in play); job "floor": [x, y, size]; job "guns": a vehicle's gun firing (fire_guns, "from" / "until" s);
    job "explode": timed explosions; job "record": s of the audio recorded to <shots>/audio.wav; job "hold": s to keep playing;
    a soldier's "launcher" (shoulder_launcher) + "fireRpg" (fire_rpg)

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
    # job "level": the lab's map (⚠ measured: when the editor last had /Game/Maps/VehicleLab open it starts loaded, cannot be deleted,
    # and new_level fails — a job may name another)
    # ⚠ measured: the editor opens on the map it last had — the lab's own — and deleting a loaded map only drops it in memory (the
    # .umap stays, so new_level finds the path taken). ⇒ a FRESH map name per run; earlier runs' maps (not loaded) are deleted
    base_lv = JOB.get("level", "/Game/Maps/VehicleLab")
    for old in unreal.EditorAssetLibrary.list_assets("/Game/Maps", recursive=False):
        if old.split(".")[0].startswith(base_lv + "_R"):
            try: unreal.EditorAssetLibrary.delete_asset(old.split(".")[0])
            except Exception: pass
    lv = f"{base_lv}_R{int(time.time())}"
    if not LES().new_level(lv): raise RuntimeError(f"new_level failed ({lv})")
    n = len(JOB["vehicles"])   # ⚠ measured: a 400 m floor at the origin ended under the 7th aircraft (a black void behind it)
    fl = JOB.get("floor") or [(n - 1) * SPACING / 2, 0, max(40000.0, n * SPACING + 40000)]   # [centre x, centre y, size] (cm)
    floor = spawn(unreal.StaticMeshActor, (fl[0], fl[1], 0), label="Floor")
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
    floor.set_actor_scale3d(unreal.Vector(fl[2] / 100.0, max(1000.0, fl[2] / 100.0), 1))
    sun = spawn(unreal.DirectionalLight, (0, 0, 1000), (-35, 60, 0), "Sun")
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    spawn(unreal.SkyAtmosphere, (0, 0, 0), label="Sky")
    sky = spawn(unreal.SkyLight, (0, 0, 500), label="SkyLight"); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0", "r.DontLimitOnBattery 1"):
        unreal.SystemLibrary.execute_console_command(None, c)
    LES().editor_set_viewport_realtime(True)
    # ⚠ measured (U-17k): the editor's viewports are SILENT unless "Enable Real Time Audio" is on (off by default; not reachable from
    # Python) — a recording run launches with -ini:EditorPerProjectUserSettings:[/Script/UnrealEd.LevelEditorMiscSettings]:bEnableRealTimeAudio=True
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
            b = c.get_skinned_asset().get_bounds()
            log(f"{v['label']}: bounds origin {b.origin.x:.0f},{b.origin.y:.0f},{b.origin.z:.0f} extent {b.box_extent.x:.0f},{b.box_extent.y:.0f},{b.box_extent.z:.0f}")
            if not v.get("anim"): continue   # shown in its rest pose (a vehicle's skinned body)
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
        if v.get("missile"):   # ⭐ an AHrotMissile: its kind; "rot" [pitch, yaw, roll]; "motor": false = shown still (in play "fly" moves it)
            r = v.get("rot", (0, float(v.get("heading", 0)), 0))
            a = spawn(unreal.HrotMissile, at, (r[0], r[1], r[2]), v["label"])
            setattr(a, "kind", unreal.load_asset(v["missile"])); a.apply_kind()
            setattr(a, "motor", bool(v.get("motor", True)))
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
        setattr(a, "flying", bool(v.get("flying", False)))     # the kit's wings beat (U-17i)
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


def shoulder_launcher(a, L):
    """⭐ "launcher": {"mesh", "scale", "back", "up", "frontAxis"} on a soldier holding the RIFLE aimed: the launcher takes the rifle's
    place along the rifle's barrel line (its `frontAxis` mesh axis onto the barrel), its middle `back` cm behind the rifle's muzzle and
    `up` cm above the barrel — on the shoulder, in the aimed pose (no RPG animation exists). Returns (weapon component, its mesh origin)."""
    ps = a.get_component_by_class(unreal.HrotPersonState)
    rifle = ps.get_weapon_component()
    if rifle is None: raise RuntimeError("no rifle in the hand to replace")
    W = rifle.get_world_transform()
    M = unreal.HrotWeaponFx.muzzle_transform(rifle, "Muzzle")
    ML = unreal.MathLibrary
    fwd = ML.inverse_transform_direction(W, M.rotation.get_axis_x()); fwd.normalize()
    up = ML.inverse_transform_direction(W, M.rotation.get_axis_z()); up.normalize()
    muz = ML.inverse_transform_location(W, M.translation)
    k = float(L.get("scale", 1.5))
    sm = unreal.load_asset(L["mesh"]); b = sm.get_bounds()
    rot = unreal.MathLibrary.make_rot_from_yz(fwd, up) if L.get("frontAxis", "Y") == "Y" else unreal.MathLibrary.make_rot_from_xz(fwd, up)
    centre = muz - fwd * float(L.get("back", 70)) + up * float(L.get("up", 8))
    loc = centre - rot.quaternion().rotate_vector(b.origin * k)   # the mesh's own centre onto that point
    in_rifle = unreal.Transform(location=loc, rotation=rot, scale=unreal.Vector(k, k, k))
    off = unreal.MathLibrary.compose_transforms(in_rifle, ps.get_editor_property("weapon_offset"))
    setattr(ps, "weapon", sm); setattr(ps, "weapon_offset", off)
    comp = ps.apply_weapon()
    log(f"{a.get_actor_label()}: launcher on the shoulder (barrel local {fwd.x:.2f},{fwd.y:.2f},{fwd.z:.2f})")
    return comp


def fire_rpg(a, F):
    """⭐ "fireRpg": {"after", "kind", "rocketAt" (mesh space), "dist", "speed"} — the loaded rocket hidden (M_HrotInvisible on its slot),
    a flying AHrotMissile from the tube's front along the launcher's front axis, launched (its backblast) — by game time"""
    comp = S["launchers"][a.get_actor_label()]
    T = comp.get_world_transform()
    fwd = unreal.MathLibrary.transform_direction(T, unreal.Vector(0, 1, 0)); fwd.normalize()
    at = unreal.MathLibrary.transform_location(T, unreal.Vector(*F.get("rocketAt", (-8.2, 20.0, 1.9))))
    comp.set_material(int(F.get("slot", 1)), unreal.load_asset("/Game/Hrot/Fx/M_HrotInvisible"))
    w = UES().get_game_world()
    xf = unreal.Transform(location=at, rotation=unreal.MathLibrary.make_rot_from_x(fwd))
    m = unreal.HrotMissile.spawn_missile(w, unreal.load_asset(F["kind"]), xf)
    m.fly_test(at + fwd * float(F.get("dist", 6000)), float(F.get("speed", 11500)), 0.0, -1.0, 0.0, True)
    log(f"{a.get_actor_label()}: RPG fired from {at.x:.0f},{at.y:.0f},{at.z:.0f} along {fwd.x:.2f},{fwd.y:.2f},{fwd.z:.2f}")


def fire_guns():
    """⭐ JOB "guns": [{"label", "fx": "MINIGUN" | "HMG", "rate", "target": [x, y, z], "spread", "aim": [yaw, pitch]}] — a vehicle's gun
    firing at `rate` rounds a second onto a patch of ground (the lab's stand-in for HROT's shot events), by GAME time"""
    now = game_now()
    for g in S["guns"]:
        a = S["v"].get(g["label"])
        if a is None: continue
        g["carry"] = g.get("carry", 0.0) + float(g.get("rate", 10)) * max(0.0, now - g.get("last", now)); g["last"] = now
        if "fxs" not in g: g["fxs"] = unreal.HrotWeaponFx.minigun_fx() if g.get("fx", "HMG") == "MINIGUN" else unreal.HrotWeaponFx.heavy_mg_fx()
        if g.get("until") is not None and now > S["tfind"] + float(g["until"]): g["carry"] = 0.0; continue   # a burst that ends
        if g.get("from") is not None and now < S["tfind"] + float(g["from"]): g["carry"] = 0.0; continue
        while g["carry"] >= 1.0:
            g["carry"] -= 1.0
            import random
            t, sp = g["target"], float(g.get("spread", 300))
            hit = unreal.Vector(t[0] + random.uniform(-sp, sp), t[1] + random.uniform(-sp, sp), t[2])
            a.fire_gun(g["fxs"], [hit], [unreal.Vector(0, 0, 1)])
            g["n"] = g.get("n", 0) + 1


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
    if S.get("guns"): fire_guns()
    if JOB.get("test2d") and not S.get("did2d") and S.get("tfind") is not None and game_now() >= S["tfind"] + 2.0:   # a plain 2D sound (no listener)
        S["did2d"] = True
        snd = unreal.load_asset(JOB["test2d"]); unreal.GameplayStatics.play_sound2d(UES().get_game_world(), snd, 1.0, 1.0)
        log(f"2D test sound {snd} at {game_now() - S['tfind']:.2f} s")
    for b in list(S.get("blasts") or []):
        if game_now() >= S["tfind"] + float(b.get("after", 1)):
            unreal.HrotExplosionFx.explode(UES().get_game_world(), unreal.Vector(*b["at"]), unreal.Vector(0, 0, 1), getattr(unreal.HrotBlast, b.get("kind", "GRENADE")))
            log(f"explosion {b.get('kind', 'GRENADE')} at {b['at']} — game {game_now() - S['tfind']:.2f} s after the start"); S["blasts"].remove(b)
    for item in list(S.get("rpgs") or []):
        if game_now() >= S["tfind"] + float(item[1].get("after", 3.6)):
            fire_rpg(S["v"][item[0]], item[1]); S["rpgs"].remove(item)
    for item in list(S.get("later") or []):   # state changes in play (an animal shot: alive -> dead), by game time
        label, then = item
        if game_now() >= S["t0"] + float(then.get("after", 1.0)):
            if then.get("embark"):   # ⭐ into a vehicle (U-17m): hidden, travels with it
                log(f"{label}: embark into {then['embark']} -> {unreal.HrotRiding.embark(S['v'][label], S['v'][then['embark']])}")
            elif then.get("disembark"):
                unreal.HrotRiding.disembark(S["v"][label]); log(f"{label}: disembarked")
            else: S["v"][label].set_state(getattr(unreal.HrotAnimalState, then["state"]))
            S["later"].remove(item)
            log(f"{label}: -> {then}")
    if p == "find":
        if time.time() < S["t"]: return
        w = UES().get_game_world()
        if w is None: raise RuntimeError("Simulate did not start")
        S["v"] = {a.get_actor_label(): a for a in unreal.GameplayStatics.get_all_actors_of_class(w, unreal.Actor)
                  if a.get_actor_label() in {v["label"] for v in JOB["vehicles"]}}
        log(f"play: {sorted(S['v'])}")
        for v in JOB["vehicles"]:
            if not v.get("skeletal") or not v.get("anim") or v["label"] not in S["v"]: continue
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
            if v.get("fly"):   # the TEST flight (standing in for HROT): {"to", "speed", "arc", "motor", "delay", "detonate"}
                f = v["fly"]
                a.fly_test(unreal.Vector(*f["to"]), float(f.get("speed", 20000)), float(f.get("arc", 0)), float(f.get("motor", -1)),
                           float(f.get("delay", 0)), bool(f.get("detonate", True)))
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
        for v in JOB["vehicles"]:   # ⭐ riders (U-17j): "ride": {"mount": <label>, "seat": n} — seated in play, as HROT would
            if v.get("ride") and v["label"] in S["v"]:
                ok = unreal.HrotRiding.sit_on(S["v"][v["label"]], S["v"][v["ride"]["mount"]], int(v["ride"].get("seat", 0)))
                log(f"{v['label']}: seat {v['ride'].get('seat', 0)} on {v['ride']['mount']} -> {ok}")
            if v.get("logBones") and v["label"] in S["v"]:
                c = S["v"][v["label"]].get_component_by_class(unreal.SkeletalMeshComponent)
                names = [str(c.get_bone_name(i)) for i in range(c.get_num_bones())]
                log(f"{v['label']} bones: " + ", ".join(f"{n}@{c.get_socket_location(n).z - S['v'][v['label']].get_actor_location().z:.0f}" for n in names))
        S["launchers"] = {}
        for v in JOB["vehicles"]:
            if v.get("launcher") and v["label"] in S["v"]: S["launchers"][v["label"]] = shoulder_launcher(S["v"][v["label"]], v["launcher"])
        S["rpgs"] = [(v["label"], v["fireRpg"]) for v in JOB["vehicles"] if v.get("fireRpg") and v["label"] in S["v"]]
        S["tfind"] = game_now()
        S["guns"] = [dict(g) for g in JOB.get("guns", [])]
        S["blasts"] = [dict(b) for b in JOB.get("explode", [])]   # ⭐ timed explosions: {"at", "kind", "after"} (U-17k: sound timing)
        if JOB.get("record"):   # ⭐ the AUDIO the view hears, recorded to a WAV (U-17k): sound proven without listening
            rr = unreal.HrotShotRenderer.for_world(UES().get_game_world()); rr.set_editor_property("log_sounds", True)
            log(f"world audio active: {rr.activate_world_audio()}")
            unreal.AudioMixerLibrary.start_recording_output(UES().get_game_world(), float(JOB["record"]))
            log(f"audio recording started at game {game_now():.2f}")
            for nm, fx in (("rifle", unreal.HrotWeaponFx.lyra_rifle_fx()), ("hmg", unreal.HrotWeaponFx.heavy_mg_fx())):
                log(f"  {nm}: sound {fx.get_editor_property('sound')}, impact {fx.get_editor_property('impact_sound')}, att {fx.get_editor_property('sound_attenuation')}")
            unreal.SystemLibrary.execute_console_command(None, "au.Debug.Sounds 1")
        for g in S["guns"]:
            if g.get("aim") and g["label"] in S["v"]: S["v"][g["label"]].set_aim(float(g["aim"][0]), float(g["aim"][1]))
        S["later"] = [(v["label"], v["then"]) for v in JOB["vehicles"] if v.get("then") and v["label"] in S["v"]]
        if S["later"]: S["t0"] = game_now()
        S["later"] = [(l, t) for l, t in S["later"]]   # (label, {"state" | "embark": <vehicle> | "disembark", "after"})
        S["queue"] = [("row", None)] + [("close", v["label"]) for v in JOB["vehicles"] if v.get("damage", "INTACT") != "INTACT"] + \
                     [("view", w) for w in JOB.get("views", [])] + ([("fire", JOB["fire"])] if JOB.get("fire") else [])
        S.update(phase="next", ready=game_now() + 3.0); return
    if p == "next":
        if game_now() < S["ready"]: return
        if not S["queue"]:
            if game_now() < S.get("tfind", 0) + float(JOB.get("hold", 0)): return   # keep playing (a recording, timed events)
            for g in S.get("guns") or []: log(f"gun {g['label']}: {g.get('n', 0)} rounds")
            if JOB.get("record"):
                w = UES().get_game_world()
                unreal.AudioMixerLibrary.stop_recording_output(w, unreal.AudioRecordingExportType.WAV_FILE, "audio", JOB["shots"])
                r = unreal.HrotShotRenderer.for_world(w)
                log(f"audio recording stopped at game {game_now() - S['tfind']:.2f} s; sounds played {r.get_editor_property('sounds_played')}")
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
            if w.get("waitUntil") and game_now() < S["tfind"] + float(w["waitUntil"]):   # not before game time T after the start
                S["queue"].insert(0, (kind, label)); S.update(phase="next", ready=S["tfind"] + float(w["waitUntil"])); return
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
