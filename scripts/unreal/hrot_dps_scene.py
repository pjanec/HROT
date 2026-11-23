"""hrot_dps_scene.py — CE-4651 (U-18b): the DPS WORLD proof, run in the full Unreal EDITOR (-ExecutePythonScript).

    HROT_DPS_SCENE_JOB = { "domain", "partition", "seconds", "ready", "out", "shots", "simBase",
                           "pictures": [{"name", "sim" (scene seconds: sim time - simBase), "look", "back", "up", "lookZ", "top"}],
                           "personKit": <a UHrotPersonKit path> (U-18d: people as that body, their looks recorded every 0.5 s),
                           "missileKind": <a UHrotMissileKind path> (U-18e: rounds with a motor), "props": [{"mesh", "at" cm, "scale"}],
                           "map": an existing level to open instead (U-18g), "floor": size in m (default 600), "groundToMedian": true (U-18g LIVE: shift the world down by the people's median
                           height after 3 s — the flat test floor stands in for HROT's terrain), pictures may "follow": <name text> or
                           "centroid" instead of "look" (a live scene has no scripted positions) }

A flat level played in Simulate; UHrotDpsWorld joins the station; the test's publisher (the stand-in for the gateway: same protocol
assembly, the gateway's send rule) starts when the ready file appears. Records, every 50 ms, each entity's SHOWN position (contract
frame) against the sim clock — the runner compares it with the publisher's truth — and renders the pictures at their times (real seconds
after ready). The kit table here is the TEST's choice of models, not a product mapping.
"""
import glob, json, math, os, shutil, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DPS_SCENE_JOB"], encoding="utf-8-sig"))
R = {"log": [], "shots": [], "track": [], "describe": {}}
S = {"handle": None, "phase": "level", "busy": False, "pending": None}
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
T0 = time.time()
KITS = "/Game/Hrot/Vehicles/"


def log(m):
    m = f"{time.time() - T0:7.1f}s {m}"; unreal.log_warning(f"[HROT-DPS] {m}"); R["log"].append(m)


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
    if JOB.get("map"):   # U-18g LIVE: an existing level (a terrain's look level, with its own sky and sun) instead of the flat test floor
        if not LES().load_level(JOB["map"]): raise RuntimeError("load_level failed: " + JOB["map"])
        for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0", "r.DontLimitOnBattery 1"):
            unreal.SystemLibrary.execute_console_command(None, c)
        LES().editor_set_viewport_realtime(True)
        if JOB.get("pie"):   # U-18m: PLAY (a player controller, AHrotDpsGameMode: the IG camera as the pawn, the HUD) instead of Simulate
            ws = unreal.EditorLevelLibrary.get_editor_world().get_world_settings()
            ws.set_editor_property("default_game_mode", unreal.HrotDpsGameMode.static_class())
            LES().editor_request_begin_play()
        else: LES().editor_play_simulate()
        S.update(phase="join", t=time.time() + 8); return
    if unreal.EditorAssetLibrary.does_asset_exist("/Game/Maps/DpsScene"): unreal.EditorAssetLibrary.delete_asset("/Game/Maps/DpsScene")
    if not LES().new_level("/Game/Maps/DpsScene"): raise RuntimeError("new_level failed")
    floor = spawn(unreal.StaticMeshActor, (0, 0, 0), label="Floor")
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
    fs = float(JOB.get("floor", 600)); floor.set_actor_scale3d(unreal.Vector(fs, fs, 1))
    sun = spawn(unreal.DirectionalLight, (0, 0, 1000), (-40, 60, 0), "Sun")
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    spawn(unreal.SkyAtmosphere, (0, 0, 0), label="Sky")
    sky = spawn(unreal.SkyLight, (0, 0, 500), label="SkyLight"); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0", "r.DontLimitOnBattery 1"):
        unreal.SystemLibrary.execute_console_command(None, c)
    for i, pr in enumerate(JOB.get("props", [])):   # U-18e: the level's static world the scene shoots at (a wall)
        a = spawn(unreal.StaticMeshActor, tuple(pr["at"]), label=f"Prop{i}")
        a.static_mesh_component.set_static_mesh(unreal.load_asset(pr["mesh"])); a.set_actor_scale3d(unreal.Vector(*pr["scale"]))
    LES().editor_set_viewport_realtime(True)
    LES().editor_play_simulate()
    S.update(phase="join", t=time.time() + 5)


def join():
    gw = UES().get_game_world()
    if gw is None: raise RuntimeError("Simulate did not start")
    w = unreal.HrotDpsWorld.for_world(gw)
    if w is None: raise RuntimeError("no UHrotDpsWorld in the game world")
    L = unreal.load_asset
    if JOB.get("personKit"): w.set_editor_property("person_kit", L(JOB["personKit"]))
    if JOB.get("missileKind"): w.set_editor_property("default_missile_kind", L(JOB["missileKind"]))
    if JOB.get("civilianKits"): w.set_editor_property("civilian_kits", [L(k) for k in JOB["civilianKits"]])   # U-18i: civilians and pedestrians
    if JOB.get("missileKinds"): w.set_editor_property("missile_kinds_by_type", {int(k): L(v) for k, v in JOB["missileKinds"].items()})   # U-18f: a munition type -> its kind
    w.set_editor_property("kits_by_type", {2001: L(KITS + "DA_ArmoredTruck")})                       # by the contract's type id
    # U-18k: the product maps HROT's vehicle types itself (UHrotDpsWorld::DefaultLife — the French pack); a job may add / override
    w.set_editor_property("kits_by_domain", {"Air": L(KITS + "DA_Air_Apache")})                     # by domain
    if JOB.get("kitsByDis"): w.set_editor_property("kits_by_dis", {k: L(v) for k, v in JOB["kitsByDis"].items()})   # U-18g: a job's own types
    w.set_editor_property("person_mesh", L("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny"))
    w.set_editor_property("person_clip", L("/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Rifle/MF_Rifle_Walk_Fwd"))
    if not w.start(int(JOB["domain"]), JOB["partition"]): raise RuntimeError("DPS start failed")
    S.update(w=w, phase="run", t0=time.time(), next_rec=0.0, pics=list(JOB.get("pictures", [])))
    # U-18l: the IG camera (AHrotDpsCamera) — pictures with "rig" are taken THROUGH it (its camera copied into the viewport every frame)
    if JOB.get("pie"):   # U-18m: the game mode's pawn IS the IG camera
        S["rig"] = unreal.GameplayStatics.get_player_pawn(gw, 0)
        log(f"PIE: the player pawn is {S['rig'].get_class().get_name() if S['rig'] else None}")
    elif any(p.get("rig") or p.get("player") for p in S["pics"]):
        S["rig"] = unreal.HrotDpsCamera.spawn_in(gw, unreal.Vector(0, 0, 5000), unreal.Rotator(pitch=-30, yaw=0, roll=0))
        LES().editor_set_game_view(True)   # U-18m: the IG camera's HUD draws through the debug-draw service's "Game" flag (an editor viewport has it off)
    open(JOB["ready"], "w").write("ready")
    log("joined; ready")


def gw_now():
    return UES().get_game_world()


def camera(look, back, up, side=0.45):
    cam = unreal.Vector(look[0] + back * side, look[1] + back, up)   # from the south-east (Unreal +Y = south)
    UES().set_level_viewport_camera_info(cam, unreal.MathLibrary.find_look_at_rotation(cam, unreal.Vector(*look)))


def person_extras(w, i):
    """the rifle's world direction (grip -> muzzle) and the actor's yaw. ⚠ only TRUE while the person is on screen: people animate
    only when rendered (the product default, UHrotPersonState::ApplyAnimation) — off screen the bones are the last drawn pose"""
    a = w.actor_of(i); extra = {}
    ps = a.get_component_by_class(unreal.HrotPersonState) if a else None
    wc = ps.get_weapon_component() if ps else None
    if wc and wc.does_socket_exist("Muzzle"):
        m = wc.get_socket_location("Muzzle"); g = wc.get_world_location(); d = m - g
        extra["rifleYaw"] = round(math.degrees(math.atan2(d.y, d.x)), 1); extra["riflePitch"] = round(math.degrees(math.atan2(d.z, math.hypot(d.x, d.y))), 1)
    leader = ps.find_leader() if ps else None
    if leader and not getattr(leader, "_hrot_always", False):   # TEST ONLY: bones refreshed every frame, so a reading is the current pose
        leader.set_editor_property("visibility_based_anim_tick_option", unreal.VisibilityBasedAnimTickOption.ALWAYS_TICK_POSE_AND_REFRESH_BONES)
    # the SHOULDER line (clavicle_l -> clavicle_r): the twist itself, not the held weapon
    if leader and leader.does_socket_exist("clavicle_r"):
        d = leader.get_socket_location("clavicle_r") - leader.get_socket_location("clavicle_l"); extra["shoulderYaw"] = round(math.degrees(math.atan2(d.y, d.x)), 1)
    if a: extra["actorYaw"] = round(a.get_actor_rotation().yaw, 1)
    return extra


def run():
    w, t = S["w"], time.time() - S["t0"]
    # U-18i: the frame rate in play (frames and wall time since the first run frame), and the worst frame
    now = time.time(); S["nf"] = S.get("nf", 0) + 1; S.setdefault("f0", now)
    if S.get("fprev"): S["fmax"] = max(S.get("fmax", 0.0), now - S["fprev"])
    S["fprev"] = now
    if JOB.get("simBase") == "join":   # U-18g LIVE: scene time from the first sim time this world sees (a running exercise joined late)
        if w.sim_now() <= 0: return
        JOB["simBase"] = w.sim_now(); log(f"joined a running exercise at sim {JOB['simBase']:.1f} s")
    if t >= S["next_rec"]:
        S["next_rec"] += 0.05
        now = w.placed_at()   # the time the shown poses belong to (a stalled frame keeps its old time: not recorded twice)
        fresh = now != S.get("last_placed"); S["last_placed"] = now
        for i in (w.entity_ids() if fresh else []):
            p = w.shown_position(i); a = w.actor_of(i)
            loc = a.get_actor_location() if a else None
            R["track"].append([round(now, 4), i, round(p.x, 4), round(p.y, 4), round(p.z, 4),
                               [round(loc.x, 1), round(loc.y, 1), round(loc.z, 1), round(a.get_actor_rotation().yaw, 2)] if a else None])
    r = unreal.HrotShotRenderer.for_world(UES().get_game_world())   # what the shot renderer DREW this frame (U-18c)
    if r:
        lt, lf = r.get_editor_property("live_tracers"), r.get_editor_property("live_flashes")
        R["maxLiveTracers"] = max(R.get("maxLiveTracers", 0), lt); R["maxLiveFlashes"] = max(R.get("maxLiveFlashes", 0), lf)
        if lt or lf: R.setdefault("shotFrames", []).append([round(w.sim_now() - float(JOB.get("simBase", 0)), 2), lt, lf])
    scene = w.sim_now() - float(JOB.get("simBase", 0)) if w.sim_now() > 0 else -1   # ⭐ pictures on the SIM clock: a burst is caught mid-burst
    if JOB.get("personKit") and scene >= S.get("next_look", 0):   # U-18d: every person's look, every 0.5 s of sim
        S["next_look"] = (int(scene * 2) + 1) / 2.0
        for i in w.entity_ids():
            look = w.person_look(i)
            if not look: continue
            person_extras(w, i)   # (sets the test-only always-refresh on its body)
            R.setdefault("looks", []).append([round(scene, 2), i, look])
    snd_job = JOB.get("sound")   # U-18n: the soundscape - rain / night / volumes set once, the game's mixed output recorded to WAV at sim times
    if snd_job and scene >= 0:
        gw = UES().get_game_world()
        if not S.get("snd_set"):
            S["snd_set"] = True
            for k, cv in (("rain", "hrot.snd.Rain"), ("wind", "hrot.snd.Wind"), ("night", "hrot.snd.Night")):
                if k in snd_job: unreal.SystemLibrary.execute_console_command(gw, f"{cv} {snd_job[k]}")
            rr_ = unreal.HrotShotRenderer.for_world(gw)
            log(f"sound: world audio active {rr_.activate_world_audio() if rr_ else None}")
        recs = snd_job.get("record", [])
        if S.get("rec_on") and time.time() >= S["rec_on"][1]:
            nm = S.pop("rec_on")[0]
            unreal.AudioMixerLibrary.stop_recording_output(gw, unreal.AudioRecordingExportType.WAV_FILE, f"dps-{nm}", JOB["shots"].replace("\\", "/") + "/")
            sc_ = unreal.HrotSoundscape.for_world(gw)
            R.setdefault("recordings", []).append({"name": f"dps-{nm}.wav", "end": round(scene, 2), "soundscape": sc_.describe() if sc_ else None})
            log(f"sound: recorded dps-{nm}.wav at scene {scene:.1f} s - {sc_.describe() if sc_ else '(no soundscape)'}")
        elif not S.get("rec_on") and recs and scene >= recs[0]["at"]:
            rc = recs.pop(0)
            for k, cv in (("rain", "hrot.snd.Rain"), ("wind", "hrot.snd.Wind"), ("night", "hrot.snd.Night")):   # a recording may set its own weather
                if k in rc: unreal.SystemLibrary.execute_console_command(gw, f"{cv} {rc[k]}")
            unreal.AudioMixerLibrary.start_recording_output(gw, float(rc["seconds"]))
            S["rec_on"] = (rc["name"], time.time() + float(rc["seconds"]))
            log(f"sound: recording {rc['name']} for {rc['seconds']} s from scene {scene:.1f} s")
    if S.get("await_file"):   # ⚠ measured (U-18e): the screenshot is taken at a LATER render — the next camera move before its file exists
        path, t0 = S["await_file"]  # changed the earlier picture (two close pictures came out the same)
        if not (os.path.exists(path) and os.path.getsize(path) > 0) and time.time() - t0 < 20: return
        S["await_file"] = None
    if JOB.get("groundToMedian") and not S.get("grounded") and scene >= 3.0:   # U-18g: the flat floor under the people
        zs = sorted(w.shown_position(i).z for i in w.entity_ids() if "Person" in w.describe(i))
        if zs:
            S["grounded"] = True; z = zs[len(zs) // 2]
            w.set_editor_property("origin_cm", unreal.Vector(0, 0, -z * 100.0)); log(f"world shifted down {z:.1f} m (the people's median height)")
    # U-18h: a picture that follows the FLAME waits (until its "until") for a lit jet
    if S["pics"] and S["pics"][0].get("follow") == "jet" and not w.jet_shooter_ids() and scene < S["pics"][0].get("until", 1e9) and scene >= S["pics"][0]["sim"]:
        pass
    elif S["pics"] and not S.get("pending_pic") and scene >= S["pics"][0]["sim"]:   # one picture at a time (a second camera move lost the first)
        pic = S["pics"].pop(0)
        if pic.get("follow") in ("jet", "traffic", "animals", "rider", "walker"):   # U-18h: the flamer while he fires · a drawn car · the animals' centre
            locs = []
            if pic["follow"] == "jet":
                locs = [w.actor_of(i).get_actor_location() for i in w.jet_shooter_ids() if w.actor_of(i)][:1]
            elif pic["follow"] == "rider":   # U-18i: a person seated on a mount
                locs = [w.actor_of(i).get_actor_location() for i in w.rider_ids() if w.actor_of(i)][:1]
            elif pic["follow"] == "animals":
                locs = [w.actor_of(i).get_actor_location() for i in w.animal_ids() if w.actor_of(i) and pic.get("name_has", "").lower() in w.describe(i).lower()]
            else:
                gw = UES().get_game_world()
                if pic["follow"] == "walker":   # U-18i: a pedestrian mover's body
                    cars = list(unreal.GameplayStatics.get_all_actors_with_tag(gw, "DPS_Walker"))
                else:
                    cars = list(unreal.GameplayStatics.get_all_actors_with_tag(gw, "DPS_Mover"))
                cars = [a for a in cars if not a.get_editor_property("hidden")] if cars else []
                locs = [a.get_actor_location() for a in cars[:1]]
                if pic.get("near") and cars:   # the car nearest a point (Unreal cm) — ⚠ measured: an all-pairs "crowd" pick over 1078 cars stalled the editor ~30 s
                    nx, ny = pic["near"]
                    best = min(cars, key=lambda a: (a.get_actor_location().x - nx) ** 2 + (a.get_actor_location().y - ny) ** 2)
                    locs = [best.get_actor_location()]
            if locs:
                n = len(locs)
                c = unreal.Vector(sum(l.x for l in locs) / n, sum(l.y for l in locs) / n, sum(l.z for l in locs) / n + pic.get("lookZ", 120))
                pic = dict(pic, look=[c.x, c.y, c.z])
                camera(pic["look"], pic.get("back", 1500), c.z + pic.get("up", 500), pic.get("side", 0.45))
            else: log(f"picture {pic['name']}: nothing to follow for {pic['follow']!r}")
        elif pic.get("follow"):   # U-18g: frame a named entity (or the centre of all) where it is NOW
            locs = []
            for i in w.entity_ids():
                a = w.actor_of(i)
                if a and (pic["follow"] == "centroid" or pic["follow"].lower() in w.describe(i).lower()): locs.append(a.get_actor_location())
            if locs:
                n = len(locs) if pic["follow"] == "centroid" else 1
                c = unreal.Vector(sum(l.x for l in locs[:n]) / n, sum(l.y for l in locs[:n]) / n, sum(l.z for l in locs[:n]) / n + pic.get("lookZ", 120))
                pic = dict(pic, look=[c.x, c.y, c.z])
                camera(pic["look"], pic.get("back", 1500), c.z + pic.get("up", 500), pic.get("side", 0.45))
            else: log(f"picture {pic['name']}: nothing to follow for {pic['follow']!r}")
        elif pic.get("player") and S.get("rig"):   # U-18m: {"possess": offer index | "letgo": true | "look": [yaw, pitch] deg, "move": [f, side], "fire", "zoom", "distance", "menu": offer index, "settle"}
            rg, spec = S["rig"], pic["player"]
            st = unreal.HrotDpsStation.for_world(UES().get_game_world())
            offers = list(st.offer_ids()) if st else []
            R.setdefault("offersAtPicture", {})[pic["name"]] = {str(i): st.offer_name(i) for i in offers} if st else None
            if "menu" in spec and offers: rg.select(offers[int(spec["menu"])]); rg.open_menu(offers[int(spec["menu"])], unreal.Vector2D(700, 380))
            if "possess" in spec and offers: rg.close_menu(); rg.possess(offers[int(spec["possess"])])
            if "choose" in spec: rg.menu_choose(int(spec["choose"]))   # a click on a menu row
            if spec.get("letgo"): rg.let_go()
            if "distance" in spec: rg.set_player_distance(float(spec["distance"]))
            if spec.get("look"): rg.player_look(float(spec["look"][0]), float(spec["look"][1]))
            rg.player_move(*(float(v) for v in spec.get("move", [0, 0])))
            rg.player_fire(bool(spec.get("fire", False))); rg.player_zoom(bool(spec.get("zoom", False)))
            S["rig_on"] = True
            log(f"player step {pic['name']}: {spec} -> {rg.describe()}; offers {R['offersAtPicture'][pic['name']]}")
        elif pic.get("rig") and S.get("rig"):   # U-18l: {"follow": name text, "distance", "orbit": [yaw, pitch] deg, "wheel": notches, "settle": s}
            rg, spec = S["rig"], pic["rig"]
            ids = [i for i in w.entity_ids() if spec.get("follow", "").lower() in w.describe(i).lower()]
            if ids:
                if rg.followed_id() != ids[0] or spec.get("refollow"): rg.follow(ids[0], float(spec.get("distance", 14)))
                else: rg.set_follow_distance(float(spec.get("distance", rg.get_follow_distance())))
                if spec.get("orbit"): rg.orbit(float(spec["orbit"][0]), float(spec["orbit"][1]))
                if spec.get("wheel"): rg.wheel(float(spec["wheel"]))
                S["rig_on"] = True
            else: log(f"picture {pic['name']}: nothing to follow for {spec.get('follow')!r}")
        elif pic.get("top"):   # straight down, screen up = east (+X), screen right = south (+Y)
            UES().set_level_viewport_camera_info(unreal.Vector(pic["look"][0], pic["look"][1], pic.get("up", 5000)), unreal.Rotator(pitch=-90, yaw=0, roll=0))
        elif "look" in pic: camera(pic["look"], pic.get("back", 6000), pic.get("up", 3500), pic.get("side", 0.45))
        S["pending_pic"] = pic["name"]; S["pic_at"] = time.time() + 0.3 + float((pic.get("rig") or pic.get("player") or {}).get("settle", 0)); S["pic_frames"] = 3   # the camera settles: 0.3 s AND 3 frames
    if S.get("rig_on") and S.get("rig"):   # U-18l: the viewport looks through the IG camera every frame (it follows the unit as it moves)
        t_ = S["rig"].camera_transform()
        UES().set_level_viewport_camera_info(t_.translation, t_.rotation.rotator())
    if S.get("ui_req") and time.time() >= S["ui_req"][1] and not S.get("await_file") and not S.get("pending_pic"):   # one screenshot request at a time (Unreal keeps ONE global request)
        nm, _, sd = S.pop("ui_req")
        S["ui_shot"] = (nm, time.time(), set(glob.glob(os.path.join(sd, "**", "*.png"), recursive=True)), sd)
        unreal.SystemLibrary.execute_console_command(gw_now(), "shot showui")
    if S.get("ui_shot"):   # U-18m: the UI shot lands in Saved/Screenshots under its own name — copied beside the others
        nm, t_ui, before, sd = S["ui_shot"]
        new = [f for f in glob.glob(os.path.join(sd, "**", "*.png"), recursive=True) if f not in before]
        if new:
            shutil.copy(new[0], os.path.join(JOB["shots"], f"dps-{nm}-ui.png")); S.pop("ui_shot"); R["shots"].append(f"dps-{nm}-ui.png")
        elif time.time() - t_ui > 10: log(f"ui shot {nm}: none appeared"); S.pop("ui_shot")
    if S.get("pending_pic"): S["pic_frames"] = S.get("pic_frames", 0) - 1
    if S.get("pending_pic") and time.time() >= S["pic_at"] and S["pic_frames"] <= 0:
        name = S.pop("pending_pic")
        path = os.path.join(JOB["shots"], f"dps-{name}.png").replace("\\", "/")
        if os.path.exists(path): os.remove(path)
        if JOB.get("pie"): unreal.SystemLibrary.execute_console_command(gw_now(), f'HighResShot 1600x900 filename="{path}"')
        else: unreal.AutomationLibrary.take_high_res_screenshot(1600, 900, path, None)
        if S.get("rig") and str(S["rig"].describe()).startswith(("player", "follow", "free")) and name.startswith("pl-"):   # U-18m: the HUD is canvas — only a UI shot has it
            sd = unreal.Paths.screen_shot_dir()
            S["ui_req"] = (name, time.time() + 1.5, sd)   # a second after the high-res one: two captures in one frame collide
        S["await_file"] = (path, time.time())
        R["shots"].append(path)
        R["describe"][name] = {str(i): w.describe(i) for i in w.entity_ids()}
        rr = unreal.HrotShotRenderer.for_world(UES().get_game_world())
        R.setdefault("rendererAtPicture", {})[name] = {k: rr.get_editor_property(k) for k in ("live_puffs", "live_tracers", "live_dashes", "live_flashes")} if rr else None
        R.setdefault("atPicture", {})[name] = {"scene": round(w.sim_now() - float(JOB.get("simBase", 0)), 2),
                                                "people": {str(i): [w.person_look(i), person_extras(w, i)] for i in w.entity_ids() if w.person_look(i)}}
        R.setdefault("framesAtPicture", {})[name] = [S.get("nf", 0), round(time.time() - S.get("f0", time.time()), 2)]   # U-18i: fps per interval
        if S.get("rig"): R.setdefault("rigAtPicture", {})[name] = S["rig"].describe()
        sc_ = unreal.HrotSoundscape.for_world(UES().get_game_world())   # U-18n
        if sc_: R.setdefault("soundAtPicture", {})[name] = sc_.describe()
        R.setdefault("lifeAtPicture", {})[name] = {"fires": w.fire_summary(), "movers": [w.movers_known(), w.movers_drawn()],
                                                    "riders": w.riders_now(), "walkers": w.walkers_drawn(),
                                                    "jets": list(w.jet_shooter_ids()), "animals": {str(i): w.describe(i) for i in w.animal_ids()}}
        log(f"picture {name} at scene {w.sim_now() - float(JOB.get('simBase', 0)):.2f} s  [{w.fire_summary()}; movers {w.movers_known()}/{w.movers_drawn()}; riders {w.riders_now()}; walkers {w.walkers_drawn()}]")
    if t >= float(JOB["seconds"]):
        R["final"] = {str(i): w.describe(i) for i in w.entity_ids()}
        R["spawned"], R["removed"] = w.spawned_total(), w.removed_total()
        R["events"] = dict(w.event_counts())
        el = time.time() - S["f0"]; R["frames"] = {"n": S["nf"], "seconds": round(el, 1), "fps": round(S["nf"] / max(el, 1e-3), 1), "worstFrameMs": round(S.get("fmax", 0) * 1000)}
        R["life"] = {"fires": w.fire_summary(), "movers": [w.movers_known(), w.movers_drawn()], "animals": {str(i): w.describe(i) for i in w.animal_ids()}}
        LES().editor_request_end_play(); S.update(phase="end", t=time.time() + 3)


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
