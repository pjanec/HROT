"""hrot_animlab.py — runs INSIDE the Unreal editor (CE-4651, U-16): the animation contact sheet.

    HROT_ANIMLAB_JOB = { "groups": [{"name", "anims": [regex, …]}], "shots", "out", "settleSeconds" }

A small level (/Game/Maps/AnimLab): a floor, sun, sky; per group a row of mannequins (Lyra's SKM_Manny), each playing one animation
found by NAME (the first asset under /Game/Characters whose name matches the regex), the rifle or pistol in its right hand when the
animation is a rifle / pistol one, a label above; one shot per group. What is missing is reported, never faked.
"""
import json, os, re, sys, time, traceback
import unreal
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hrot_person
from hrot_person import body_component, body_of

JOB = json.load(open(os.environ["HROT_ANIMLAB_JOB"], encoding="utf-8"))
T0 = time.time()
R = {"log": [], "groups": [], "shots": []}
S = {"handle": None, "busy": False, "step": 0}
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)


def log(m):
    unreal.log_warning(f"[HROT-ANIM] {m}"); R["log"].append(f"{time.time() - T0:7.1f}s {m}")


def finish(ok, why=""):
    R["ok"], R["why"] = ok, why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    if not os.environ.get("HROT_ANIMLAB_KEEP_OPEN"): unreal.EditorPythonScripting.set_keep_python_script_alive(False)


def spawn(cls, loc, rot=(0, 0, 0), label=None):
    a = EAS().spawn_actor_from_class(cls, unreal.Vector(*loc), unreal.Rotator(roll=rot[2], pitch=rot[0], yaw=rot[1]))
    if label: a.set_actor_label(label)
    return a


def step_level():
    if unreal.EditorAssetLibrary.does_asset_exist("/Game/Maps/AnimLab"):
        unreal.EditorAssetLibrary.delete_asset("/Game/Maps/AnimLab")
    if not LES().new_level("/Game/Maps/AnimLab"): raise RuntimeError("new_level failed")
    floor = spawn(unreal.StaticMeshActor, (0, 0, 0), label="Floor")
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
    floor.set_actor_scale3d(unreal.Vector(400, 400, 1))
    sun = spawn(unreal.DirectionalLight, (0, 0, 1000), (-35, 60, 0), "Sun")   # from the camera's side: the faces lit
    sun.light_component.set_editor_property("atmosphere_sun_light", True); sun.light_component.set_editor_property("intensity", 8.0)
    spawn(unreal.SkyAtmosphere, (0, 0, 0), label="Sky")
    sky = spawn(unreal.SkyLight, (0, 0, 500), label="SkyLight"); sky.light_component.set_editor_property("real_time_capture", True)
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0"): unreal.SystemLibrary.execute_console_command(None, c)
    try: LES().editor_set_viewport_realtime(True)
    except Exception as e: log(f"viewport realtime: {e}")


def step_index():
    S["kit"] = kit = hrot_person.PersonKit(log)   # ⭐ the ONE person setup (shared with the crowd test)
    S["names"] = kit.names
    S["manny"] = kit.manny
    paths = list(unreal.EditorAssetLibrary.list_assets("/Game/Weapons", recursive=True, include_folder=False))
    names = {p.rsplit(".", 1)[-1]: p for p in paths}
    S["rifle"] = unreal.load_asset(names["SM_Rifle"])   # the plain (non-state) rows still hang the static weapon on weapon_r
    pistols = [p for n, p in names.items() if n.startswith("SM_") and "Pistol" in n]
    S["pistol"] = unreal.load_asset(pistols[0]) if pistols else None
    log(f"indexed {len(kit.names)} mannequin assets; pistol mesh {'found' if S['pistol'] else 'MISSING'}")


def resolve(rx, body=None):
    return S["kit"].resolve(rx, body)


def attach_weapon(w, sk):
    """A plain row's static weapon: at weapon_r (Lyra's AttachTransform: yaw −90)."""
    keep = unreal.AttachmentRule.KEEP_WORLD; snap = unreal.AttachmentRule.SNAP_TO_TARGET
    w.attach_to_component(sk, "weapon_r", snap, snap, keep, False)
    w.set_actor_relative_rotation(unreal.Rotator(roll=0, pitch=0, yaw=-90), False, True)


def person(g, spec, x, y, row):
    """One state-driven person (hrot_person.PersonKit.make): base (stance + gait), upper (weapon + state), action, aim, weapon."""
    made = S["kit"].make(spec.get("body"), (x, y, 0), float(spec.get("facing", 140)), f"{g['name']}_{spec['label']}", spec, always_tick=True)
    if made is None:
        row.append({"want": spec["label"], "found": None}); return
    row.append({"want": spec["label"], "found": spec["label"]})
    p, ai, sk = made
    S.setdefault("rows", {}).setdefault(g["name"], []).append(p)
    if spec.get("seq"): S.setdefault("live", []).append({"label": p.get_actor_label(), "spec": spec, "crowd": spec.get("body")})
    if spec.get("pose") is not None: S.setdefault("pose_secs", {})[p.get_actor_label()] = float(spec["pose"])
    if spec.get("then"):   # a TRANSITION: posed in this state, then switched to "then" and shot `after` seconds later (in shoot())
        S.setdefault("transitions", {})[p.get_actor_label()] = (ai, spec)
    t = spawn(unreal.TextRenderActor, (x, y, 205), (0, -90, 0), f"L_{spec['label']}")
    tc = t.text_render
    tc.set_editor_property("text", spec["label"]); tc.set_editor_property("world_size", 11.0)
    tc.set_editor_property("horizontal_alignment", unreal.HorizTextAligment.EHTA_CENTER)
    tc.set_editor_property("text_render_color", unreal.Color(20, 20, 20, 255))
    S["rows"][g["name"]].append(t)


def step_build():
    S["shots_todo"] = []
    for gi, g in enumerate(JOB["groups"]):
        y = gi * 2500.0
        row = []
        for ai, rx in enumerate(g["anims"]):
            x = ai * 160.0 * (2 if g.get("live") else 1)
            if isinstance(rx, dict):
                if g.get("live"): rx = dict(rx, facing=rx.get("facing", 180))   # walking toward the camera   # a COMBINATION through UHrotPersonAnimInstance (the state-driven person)
                person(g, rx, x, y, row); continue
            name, anim = resolve(rx)
            row.append({"want": rx, "found": name})
            if not anim: continue
            p = spawn(unreal.SkeletalMeshActor, (x, y, 0), (0, 140, 0), f"{g['name']}_{name}")   # the mannequin mesh faces its +Y: yaw 180 faces the camera (−Y)
            sk = p.skeletal_mesh_component
            sk.set_skeletal_mesh_asset(S["manny"])
            sk.set_animation_mode(unreal.AnimationMode.ANIMATION_SINGLE_NODE)
            ad = sk.get_editor_property("animation_data")
            ad.set_editor_property("anim_to_play", anim); ad.set_editor_property("saved_looping", True); ad.set_editor_property("saved_playing", True)
            ad.set_editor_property("saved_position", 0.35 * anim.get_play_length())   # a pose in the middle of the action
            sk.set_editor_property("animation_data", ad)
            sk.set_update_animation_in_editor(True)   # an editor world does not advance animation otherwise
            sk.play_animation(anim, True)
            sk.set_position(0.35 * anim.get_play_length(), False)
            S.setdefault("rows", {}).setdefault(g["name"], []).append(p)
            weapon = S["rifle"] if "Rifle" in name else (S["pistol"] if "Pistol" in name else None)
            if weapon:
                w = spawn(unreal.StaticMeshActor, (x, y, 100), label=f"W_{name}")
                S.setdefault("rows", {}).setdefault(g["name"], []).append(w)
                w.static_mesh_component.set_static_mesh(weapon)
                w.static_mesh_component.set_mobility(unreal.ComponentMobility.MOVABLE)
                w.attach_to_component(sk, "weapon_r", unreal.AttachmentRule.SNAP_TO_TARGET, unreal.AttachmentRule.SNAP_TO_TARGET,
                                      unreal.AttachmentRule.KEEP_WORLD, False)
                # Lyra's WID_Rifle / WID_Pistol AttachTransform: rotation quaternion (0, 0, -0.7071, 0.7071) = yaw −90 at "weapon_r"
                w.set_actor_relative_rotation(unreal.Rotator(roll=0, pitch=0, yaw=-90), False, True)
            t = spawn(unreal.TextRenderActor, (x, y, 205), (0, -90, 0), f"L_{name}")
            S.setdefault("rows", {}).setdefault(g["name"], []).append(t)
            tc = t.text_render
            tc.set_editor_property("text", name.replace("MM_", "").replace("MF_", "F ").replace("_", " "))
            tc.set_editor_property("world_size", 12.0); tc.set_editor_property("horizontal_alignment", unreal.HorizTextAligment.EHTA_CENTER)
            tc.set_editor_property("text_render_color", unreal.Color(20, 20, 20, 255))
        R["groups"].append({"name": g["name"], "anims": row})
        n = max(1, len(g["anims"]))
        cx = (n - 1) * 80.0
        dist = max(520.0, n * 160.0 * 1.15 / (2 * 0.4663))   # the whole row in a 50° field of view (tan 25° = 0.4663); was cut at 5 of 7
        if g.get("live"):   # the live row is spaced twice as wide
            S["live_cam"] = {"x": 2 * cx, "y": y, "dist": dist + 250}
            # the firing target: a wall on the empty −X side, where the gunner ends (he walks ~10 m toward −Y first)
            wall = spawn(unreal.StaticMeshActor, (-900, y - 1000, 300), label="FireWall")
            wall.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Cube"))
            wall.set_actor_scale3d(unreal.Vector(0.2, 30, 6))
            wall.static_mesh_component.set_mobility(unreal.ComponentMobility.MOVABLE)   # moved in play, across the line of fire
            S["rows"].setdefault(g["name"], []).append(wall)
            gr = spawn(unreal.StaticMeshActor, (0, y - 3000, -500), label="Grenade")   # Lyra's grenade, flown in play like HROT's positions
            gr.static_mesh_component.set_static_mesh(unreal.load_asset("/Game/Weapons/Grenade/Mesh/SM_grenade"))
            gr.static_mesh_component.set_mobility(unreal.ComponentMobility.MOVABLE)
            gr.set_actor_scale3d(unreal.Vector(2, 2, 2))   # seen from 15 m: a little larger than life
            S["rows"][g["name"]].append(gr)
            continue
        S["shots_todo"].append({"name": g["name"], "loc": (cx, y - dist, 110.0), "rot": (-4.0, 90.0, 0.0)})
    found = sum(1 for g in R["groups"] for a in g["anims"] if a["found"])
    log(f"{found} animations placed, {sum(len(g['anims']) for g in R['groups']) - found} not found")
    S["wait_until"] = time.time() + JOB.get("settleSeconds", 25) * 2   # the first shader compile


def shoot():
    if time.time() < S.get("wait_until", 0): return True
    if S.get("pending"):
        path, t = S["pending"]
        if (os.path.exists(path) and os.path.getsize(path) > 0) or time.time() - t > 90:
            R["shots"].append(path if os.path.exists(path) else None); S["pending"] = None
        return True
    if not S["shots_todo"]: return False
    sh = S["shots_todo"][0]
    if not sh.get("shown"):   # only this row visible — then give its poses a few frames before the shot (a hidden mesh is not posed)
        for row, actors in S.get("rows", {}).items():
            for a in actors: a.set_is_temporarily_hidden_in_editor(row != sh["name"])
        for a in S["rows"].get(sh["name"], []):   # pose every person of this row now (deterministic; the editor's own tick is not)
            sk = body_component(a) if a.get_component_by_class(unreal.HrotPersonState) else None
            if sk is not None:
                secs = S.get("pose_secs", {}).get(a.get_actor_label(), 0.6)   # a one-shot clip shown `pose` seconds in
                unreal.HrotTerrainLibrary.pose_now(sk, secs, max(12, int(secs / 0.05)))
                for o in a.get_components_by_class(unreal.SkeletalMeshComponent):   # meshes with their OWN anim (the crowd face
                    if o != sk and o.get_anim_instance():                            # copies the body: posed after it, or its
                        unreal.HrotTerrainLibrary.pose_now(o, 0.1, 2)                 # head stays at standing height)
                tr = S.get("transitions", {}).get(a.get_actor_label())
                if tr:   # switch to the second state, then let `after` seconds of the blend pass
                    st, spec = tr
                    for k, prop in (("base", "base_anim"), ("upper", "upper_anim")):
                        if k in spec["then"]:
                            setattr(st, prop, resolve(spec["then"][k])[1] if spec["then"][k] else None)
                    if "yaw" in spec["then"]: setattr(st, "aim_yaw", float(spec["then"]["yaw"]))
                    if "pitch" in spec["then"]: setattr(st, "aim_pitch", float(spec["then"]["pitch"]))
                    steps = max(1, int(round(spec.get("after", 0.1) / 0.02)))
                    unreal.HrotTerrainLibrary.pose_now(sk, spec.get("after", 0.1), steps)
        sh["shown"] = True; S["wait_until"] = time.time() + 3
        return True
    S["shots_todo"].pop(0)
    cam = spawn(unreal.CameraActor, sh["loc"], sh["rot"], "Cam_" + sh["name"])
    cam.camera_component.set_editor_property("field_of_view", 50.0)
    unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).set_level_viewport_camera_info(cam.get_actor_location(), cam.get_actor_rotation())
    path = os.path.join(JOB["shots"], f"anim-{sh['name']}.png").replace("\\", "/")
    if os.path.exists(path): os.remove(path)
    unreal.AutomationLibrary.take_high_res_screenshot(1920, 1080, path, cam)
    S["pending"] = (path, time.time()); S["wait_until"] = time.time() + 6
    return True


def step_save():
    LES().save_current_level(); unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)


UES = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)


def live():
    """⭐ PLAY MODE: Simulate in the editor — the engine ticks the people (no pose_now); each state of `seq` applied to the PIE copies,
    held, shot. Returns True while busy."""
    L = S.setdefault("L", {"phase": "start"})
    if not S.get("live"): return False
    now = time.time()
    if L["phase"] == "start":
        for row, actors in S.get("rows", {}).items():   # only the live row in the game world (editor-hidden does not carry over)
            for a in actors:
                a.set_is_temporarily_hidden_in_editor(row != "live"); a.set_actor_hidden_in_game(row != "live")
        # ⭐ the editor runs HIDDEN (HROT_HIDDEN_WINDOW) and throttles itself when not in front — measured: six blast pictures
        # 0.12 s / 1 s apart showed the SAME frame. The crowd test's switches (hrot_crowd.step_settings)
        for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0", "r.DontLimitOnBattery 1"):
            unreal.SystemLibrary.execute_console_command(None, c)
        LES().editor_play_simulate()
        L.update(phase="find", t=now + 6); return True
    if now < L.get("t", 0): return True
    if L["phase"] == "find":
        w = UES().get_game_world()
        if w is None: raise RuntimeError("Simulate did not start (no game world)")
        by_label = {a.get_actor_label(): a for a in unreal.GameplayStatics.get_all_actors_of_class(w, unreal.Actor)}
        L["people"] = []
        for e in S["live"]:
            a = by_label.get(e["label"])
            st = a.get_component_by_class(unreal.HrotPersonState) if a else None
            if st is None: log(f"live: {e['label']} not in the game world"); continue
            L["people"].append(dict(e, actor=a, state=st))
        log(f"live: Simulate running, {len(L['people'])} people found in the game world")
        L.update(phase="state", k=0, last=now); return True
    people = L["people"]
    n = max(len(p["spec"]["seq"]) for p in people) if people else 0
    if L["phase"] == "state":
        if L["k"] >= n:
            gunner = next((p for p in people if p["spec"].get("fire")), None)
            if gunner is None:
                LES().editor_request_end_play(); L.update(phase="end", t=now + 4); return True
            L.update(phase="fire_aim", gunner=gunner, yaw=0.0, until=now + 1.5, shots_taken=0)
            for p in people: p["speed"] = 0.0
            st = gunner["state"]
            setattr(st, "base_anim", resolve("MM_Rifle_Idle_Hipfire", gunner["crowd"])[1])
            setattr(st, "upper_anim", resolve("MM_Rifle_Idle_ADS", gunner["crowd"])[1])
            setattr(st, "aim_yaw", 0.0); setattr(st, "aim_pitch", 0.0)   # straight ahead, level — seen from the side
            w = UES().get_game_world()   # the wall across his line of fire, 9 m ahead (he faces −Y)
            for a in unreal.GameplayStatics.get_all_actors_of_class(w, unreal.StaticMeshActor):
                if a.get_actor_label() == "FireWall":
                    gl = gunner["actor"].get_actor_location()
                    a.set_actor_location_and_rotation(unreal.Vector(gl.x, gl.y - 900, 300), unreal.Rotator(roll=0, pitch=0, yaw=90), False, False)
            return True
        for p in people:
            seq = p["spec"]["seq"]; st = seq[min(L["k"], len(seq) - 1)]
            for key, prop in (("base", "base_anim"), ("upper", "upper_anim")):
                if key in st: setattr(p["state"], prop, resolve(st[key], p["crowd"])[1] if st[key] else None)
            if "yaw" in st: setattr(p["state"], "aim_yaw", float(st["yaw"]))
            p["speed"] = float(st.get("speed", 0))
        L.update(phase="hold", until=now + 1.6, last=now, shots=0); return True
    if L["phase"] == "hold":   # move the walkers (the clip plays in place) and keep the camera on them
        dt = min(0.1, now - L["last"]); L["last"] = now
        ys = []
        for p in people:
            a = p["actor"]
            lead = body_component(a)
            fwd = lead.get_right_vector() if lead else a.get_actor_right_vector()   # every body's mesh faces its +Y
            a.set_actor_location(a.get_actor_location() + fwd * (p["speed"] * dt), False, False)
            ys.append(a.get_actor_location().y)
        c = S["live_cam"]; my = sum(ys) / max(1, len(ys))
        UES().set_level_viewport_camera_info(unreal.Vector(c["x"], my - c["dist"], 120.0), unreal.Rotator(roll=0, pitch=-4, yaw=90))
        if now < L["until"]: return True
        if L.get("pending"):
            path, t = L["pending"]
            if not ((os.path.exists(path) and os.path.getsize(path) > 0) or now - t > 60): return True
            R["shots"].append(path if os.path.exists(path) else None); L["pending"] = None
            if L["k"] == 0 and L["shots"] == 1:   # the first state twice, 0.5 s apart: the poses must differ (the engine ticks them)
                L["until"] = now + 0.5; return True
            L["k"] += 1; L["phase"] = "state"; return True
        name = f"live-{L['k'] + 1}" + ("b" if L["shots"] >= 1 else "")
        path = os.path.join(JOB["shots"], f"anim-{name}.png").replace("\\", "/")
        if os.path.exists(path): os.remove(path)
        unreal.AutomationLibrary.take_high_res_screenshot(1600, 900, path, None)
        L["pending"] = (path, now); L["shots"] = L.get("shots", 0) + 1
        return True
    if L["phase"] in ("fire_aim", "firing"):   # ⭐ the SHOT effects: flash, tracer, shell, impacts (UHrotWeaponFx.FireShot)
        g = L["gunner"]
        if "weapon" not in g:
            g["weapon"] = g["state"].get_weapon_component(); g["fx"] = unreal.HrotWeaponFx.lyra_rifle_fx()
            if g["weapon"] is None: raise RuntimeError("live fire: the gunner holds no weapon component")
            wc = g["weapon"]; par = wc.get_attach_parent()
            log(f"live fire: weapon {wc.get_class().get_name()} on {par.get_name() if par else None} at {wc.get_attach_socket_name()}")
            ga = g["actor"]; lead = body_component(ga)
            for mc in ga.get_components_by_class(unreal.MeshComponent):
                if "Weapon" in mc.get_name() or isinstance(mc, unreal.StaticMeshComponent):
                    par = mc.get_attach_parent()
                    log(f"live fire: comp {mc.get_name()} {mc.get_class().get_name()} visible {mc.is_visible()} on "
                        f"{par.get_name() if par else None}@{mc.get_attach_socket_name()} at {mc.get_world_location()}")
            mzt = unreal.HrotWeaponFx.muzzle_transform(g["weapon"], "Muzzle")
            log(f"live fire: hand_r {lead.get_socket_location('hand_r')}; chosen weapon {g['weapon'].get_name()}; muzzle {mzt.translation}")
            log(f"live fire: person state applied in play: {g['state'].get_editor_property('applied_in_play')}, active {g['state'].is_active()}")
            log(f"live fire: muzzle socket {'found' if g['weapon'].does_socket_exist('Muzzle') else 'MISSING (longest axis)'}; "
                f"fx {[bool(g['fx'].get_editor_property(k)) for k in ('muzzle_flash', 'tracer', 'shell_eject', 'impact')]}")
        mz = unreal.HrotWeaponFx.muzzle_transform(g["weapon"], "Muzzle")
        at, d = mz.translation, mz.rotation.get_axis_x()
        a = g["actor"].get_actor_location()
        # from his side, looking at the middle of his line of fire (he faces −Y, the wall 9 m ahead)
        cam, look = unreal.Vector(a.x - 900, a.y - 350, 170.0), unreal.Vector(a.x, a.y - 400, 120.0)
        if L.get("shots_taken", 0) >= 2:   # the last shot: a close-up of the muzzle from the side (the flash's shape)
            cam, look = at + unreal.Vector(-160, 0, 20), at + d * 25
        UES().set_level_viewport_camera_info(cam, unreal.MathLibrary.find_look_at_rotation(cam, look))
        if now < L["until"]: return True
        if L["phase"] == "fire_aim":   # the side with nobody on it (the others stand at +X)

            st = g["state"]; lead = body_component(g["actor"]); ai = lead.get_anim_instance() if lead else None
            log(f"live fire: aim yaw {L['yaw']:.0f}, muzzle dir ({d.x:.2f}, {d.y:.2f}, {d.z:.2f}); state base "
                f"{st.get_editor_property('base_anim').get_name() if st.get_editor_property('base_anim') else None} upper "
                f"{st.get_editor_property('upper_anim').get_name() if st.get_editor_property('upper_anim') else None} yaw "
                f"{st.get_editor_property('aim_yaw')}; leader {lead.get_name() if lead else None} anim "
                f"{ai.get_class().get_name() if ai else None}")
            L.update(phase="firing", next_shot=now, until=now)
        if L.get("pending"):
            path, t = L["pending"]
            if not ((os.path.exists(path) and os.path.getsize(path) > 0) or now - t > 60):
                shot(g, at, d)   # ⚠ while a picture is pending: a round EVERY frame — a flash lives 60 ms, the capture lands frames later
                return True
            R["shots"].append(path if os.path.exists(path) else None); L["pending"] = None; L["shots_taken"] += 1
            L["until"] = now + 0.35
            return True
        if L["shots_taken"] >= 3:   # then the BLASTS (UHrotExplosionFx)
            L.update(phase="blasts", blast=0, blast_t=None); return True
        shot(g, at, d); L["next_shot"] = now + 0.1
        path = os.path.join(JOB["shots"], f"anim-fire-{L['shots_taken'] + 1}.png").replace("\\", "/")
        if os.path.exists(path): os.remove(path)
        unreal.AutomationLibrary.take_high_res_screenshot(1600, 900, path, None)
        L["pending"] = (path, now)
        return True
    if L["phase"] == "blasts":   # ⭐ a grenade thrown (its flight = positions each frame, as HROT will send), then a mortar, a tank HE shell
        # ⭐ GAME time here, not the wall clock: a picture "0.12 s after" means the world's 0.12 s (a slow frame cannot shift it)
        now = unreal.GameplayStatics.get_time_seconds(UES().get_game_world())
        a = L["gunner"]["actor"].get_actor_location()
        # the wall stands 9 m ahead (−Y): the blasts land 5 m ahead, seen from 15 m off to the side. ⭐ blastSpacing (a trial): each
        # blast lands that far from the last, the camera with it — measured: side by side, a picture showed the previous blast's smoke
        sp = float(JOB.get("blastSpacing") or 0)
        ox = L["blast"] * sp
        cam, look = unreal.Vector(a.x - 1500 + ox, a.y - 500, 300.0), unreal.Vector(a.x + ox, a.y - 500, 150.0)
        UES().set_level_viewport_camera_info(cam, unreal.MathLibrary.find_look_at_rotation(cam, look))
        # the job's blasts (animlab.py --blasts): {name, kind, effect (a path = try that system), scale}; default the three kinds
        kinds = [(b["name"], getattr(unreal.HrotBlast, b["kind"]), b.get("effect"), b.get("scale", 1.0)) for b in JOB.get("blasts") or
                 [{"name": "grenade", "kind": "GRENADE"}, {"name": "mortar", "kind": "MORTAR"}, {"name": "tank-he", "kind": "TANK_HE"}]]
        if L["blast"] >= len(kinds):
            LES().editor_request_end_play(); L.update(phase="end", t=now + 4); return True
        name, kind, effect, scale = kinds[L["blast"]]
        land = unreal.Vector(a.x + ox, a.y - 500, 0.0) if sp else unreal.Vector(a.x + ((L["blast"] % 3) - 1) * 300, a.y - 500, 0.0)
        if L.get("blast_t") is None:
            L.update(blast_t=now, exploded=False, pics=list(JOB.get("blastPics") or [0.12, 1.0]), pending=None)
            if name == "grenade" and L.get("grenade_actor") is None:
                w = UES().get_game_world()
                L["grenade_actor"] = next((x for x in unreal.GameplayStatics.get_all_actors_of_class(w, unreal.StaticMeshActor)
                                           if x.get_actor_label() == "Grenade"), None)
        t = now - L["blast_t"]
        g = L.get("grenade_actor")
        if name == "grenade" and g and not L["exploded"]:   # the flight: 1.2 s on an arc from the thrower's hand
            u = min(t / 1.2, 1.0)
            start = a + unreal.Vector(0, -40, 150)
            g.set_actor_location(start + (land - start) * u + unreal.Vector(0, 0, 400 * 4 * u * (1 - u)), False, True)
            g.set_actor_hidden_in_game(False)
            if u < 1.0: return True
        if not L["exploded"]:
            if g and name == "grenade": g.set_actor_hidden_in_game(True)
            ok = (unreal.HrotExplosionFx.explode_with(L["gunner"]["actor"], land, unreal.Vector(0, 0, 1), kind, unreal.load_asset(effect), scale)
                  if effect else unreal.HrotExplosionFx.explode(L["gunner"]["actor"], land, unreal.Vector(0, 0, 1), kind))
            log(f"blast {name}: {'shown' if ok else 'dropped (budget / range)'}")
            L.update(exploded=True, boom=now); return True
        if L.get("pending"):
            path, t0 = L["pending"]
            if not ((os.path.exists(path) and os.path.getsize(path) > 0) or now - t0 > 30): return True
            R["shots"].append(path); L["pending"] = None
        if L["pics"]:
            if now - L["boom"] < L["pics"][0]: return True
            secs = L["pics"].pop(0)
            path = os.path.join(JOB["shots"], f"anim-blast-{name}-{secs:.2f}s.png").replace("\\", "/")
            if os.path.exists(path): os.remove(path)
            unreal.AutomationLibrary.take_high_res_screenshot(1600, 900, path, None)
            L["pending"] = (path, now); return True
        L.update(blast=L["blast"] + 1, blast_t=None); return True
    if L["phase"] == "end":
        log("live: Simulate ended"); return False
    return False


def shot(g, at, d):
    """One round: the hit = the first thing along the barrel within 300 m (the ground, aimed 10° down), else no impact."""
    w = UES().get_game_world()
    hit = unreal.SystemLibrary.line_trace_single(w, at + d * 20, at + d * 30000, unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, True,
                                                 [g["actor"]], unreal.DrawDebugTrace.NONE, True)
    t = hit.to_tuple() if hit is not None else None
    hits, normals = ([t[4]], [t[6]]) if t and t[0] else ([], [])
    unreal.HrotWeaponFx.fire_shot(g["weapon"], g["fx"], hits, normals)
    if not L_LOGGED.get("hit"): L_LOGGED["hit"] = True; log(f"live fire: first round hit {'at ' + str(hits[0]) if hits else 'nothing'}")


L_LOGGED = {}


STEPS = [step_level, step_index, step_build, None, step_save, "live"]


def tick(dt):
    if S["busy"]: return
    try:
        S["busy"] = True
        if S["step"] < len(STEPS) and STEPS[S["step"]] in (None, "live"):
            if (shoot() if STEPS[S["step"]] is None else live()): return
            S["step"] += 1; return
        if S["step"] >= len(STEPS): finish(True); return
        s = STEPS[S["step"]]; S["step"] += 1; s()
    except Exception:
        S["step"] = len(STEPS) + 1; finish(False, traceback.format_exc())
    finally:
        S["busy"] = False


unreal.EditorPythonScripting.set_keep_python_script_alive(True)
S["handle"] = unreal.register_slate_post_tick_callback(tick)
