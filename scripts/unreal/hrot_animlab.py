"""hrot_animlab.py — runs INSIDE the Unreal editor (CE-4651, U-16): the animation contact sheet.

    HROT_ANIMLAB_JOB = { "groups": [{"name", "anims": [regex, …]}], "shots", "out", "settleSeconds" }

A small level (/Game/Maps/AnimLab): a floor, sun, sky; per group a row of mannequins (Lyra's SKM_Manny), each playing one animation
found by NAME (the first asset under /Game/Characters whose name matches the regex), the rifle or pistol in its right hand when the
animation is a rifle / pistol one, a label above; one shot per group. What is missing is reported, never faked.
"""
import json, os, re, time, traceback
import unreal

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
    paths = list(unreal.EditorAssetLibrary.list_assets("/Game/Characters", recursive=True, include_folder=False))
    paths += list(unreal.EditorAssetLibrary.list_assets("/Game/Weapons", recursive=True, include_folder=False))
    if unreal.EditorAssetLibrary.does_directory_exist("/Game/Mixamo/Retargeted"):   # Mixamo, retargeted onto the mannequin (hrot_mixamo.py)
        paths += list(unreal.EditorAssetLibrary.list_assets("/Game/Mixamo/Retargeted", recursive=True, include_folder=False))
    S["names"] = {p.rsplit(".", 1)[-1]: p for p in paths}
    body = "/Game/Hrot/People/Anims"   # the same clips on the City Sample crowd skeleton (retarget.py) — same names, own folder
    S["body_names"] = ({p.rsplit(".", 1)[-1]: p for p in unreal.EditorAssetLibrary.list_assets(body, recursive=True, include_folder=False)}
                       if unreal.EditorAssetLibrary.does_directory_exist(body) else {})
    S["manny"] = unreal.load_asset(next(p for n, p in S["names"].items() if n == "SKM_Manny"))
    S["rifle"] = unreal.load_asset(next(p for n, p in S["names"].items() if n == "SM_Rifle"))
    pistols = [p for n, p in S["names"].items() if n.startswith("SM_") and "Pistol" in n]
    S["pistol"] = unreal.load_asset(pistols[0]) if pistols else None
    S["weapon_rel"] = None   # weapon_r relative to hand_r on the mannequin: a body without weapon_r holds the weapon there
    tmp = spawn(unreal.SkeletalMeshActor, (0, 0, -5000), label="tmp_manny")
    tmp.skeletal_mesh_component.set_skeletal_mesh_asset(S["manny"])
    S["weapon_rel"] = tmp.skeletal_mesh_component.get_socket_transform("weapon_r", unreal.RelativeTransformSpace.RTS_PARENT_BONE_SPACE)
    tmp.destroy_actor()
    log(f"indexed {len(paths)} assets, {len(S['body_names'])} body clips; pistol mesh {'found' if S['pistol'] else 'MISSING'}")


def resolve(rx, body=False):
    rxc = re.compile(rx, re.I)
    names = S["body_names"] if body else S["names"]
    for n in sorted(names):
        if rxc.fullmatch(n):
            a = unreal.load_asset(names[n])
            if isinstance(a, unreal.AnimSequenceBase): return n, a
    return None, None


def body_component(actor):
    """The dressed person's LEADER — the component the body and outfit parts follow (leader pose). Measured on the crowd Blueprints:
    a hidden root `SkeletalMeshComponent0` (the *_base mesh, playing the crowd's own clip) leads Body, Top, Bottom, Shoes; the Face
    runs its own head Anim BP. ⚠ Driving `Body` does nothing: a follower copies its leader's pose."""
    comps = list(actor.get_components_by_class(unreal.SkeletalMeshComponent))
    leaders = {c.get_editor_property("leader_pose_component") for c in comps}
    for c in comps:
        if c in leaders: return c
    return next((c for c in comps if c.get_name().startswith("Body")), None)


def attach_weapon(w, sk):
    """At weapon_r (Lyra's AttachTransform: yaw −90); a body without weapon_r: at hand_r, where weapon_r sits on the mannequin."""
    keep = unreal.AttachmentRule.KEEP_WORLD; snap = unreal.AttachmentRule.SNAP_TO_TARGET
    turn = unreal.Transform(rotation=unreal.Rotator(roll=0, pitch=0, yaw=-90))
    if sk.does_socket_exist("weapon_r"):
        w.attach_to_component(sk, "weapon_r", snap, snap, keep, False)
        w.set_actor_relative_rotation(unreal.Rotator(roll=0, pitch=0, yaw=-90), False, True)
    else:
        w.attach_to_component(sk, "hand_r", snap, snap, keep, False)
        rel = turn * S["weapon_rel"]   # child first: the yaw in weapon_r's frame, then weapon_r in hand_r's
        w.set_actor_relative_transform(rel, False, True)


def person(g, spec, x, y, row):
    """One state-driven person: base (stance + gait), upper (weapon + state), action (additive), aim yaw / pitch."""
    clips = {}
    for k in ("base", "upper", "action"):
        if spec.get(k):
            n, a = resolve(spec[k], bool(spec.get("body"))); clips[k] = a
            if not a: row.append({"want": spec[k], "found": None}); return
    row.append({"want": spec["label"], "found": spec["label"]})
    if spec.get("body"):   # a DRESSED person: a City Sample crowd Blueprint (body + outfit parts on a leader pose)
        cls = unreal.EditorAssetLibrary.load_blueprint_class("/Game/Hrot/People/" + spec["body"])
        # measured (live shots): the crowd body faces its actor's +Y, like the mannequin mesh ⇒ the same yaw
        p = spawn(cls, (x, y, 0), (0, float(spec.get("facing", 140)), 0), f"{g['name']}_{spec['label']}")
        sk = body_component(p)
        if sk is None: raise RuntimeError("no Body component on " + spec["body"])
    else:
        p = spawn(unreal.SkeletalMeshActor, (x, y, 0), (0, float(spec.get("facing", 140)), 0), f"{g['name']}_{spec['label']}")
        sk = p.skeletal_mesh_component
        sk.set_skeletal_mesh_asset(S["manny"])
    sk.set_animation_mode(unreal.AnimationMode.ANIMATION_BLUEPRINT)
    sk.set_anim_instance_class(unreal.HrotPersonAnimInstance.static_class())
    sk.set_update_animation_in_editor(True)
    sk.set_editor_property("visibility_based_anim_tick_option", unreal.VisibilityBasedAnimTickOption.ALWAYS_TICK_POSE_AND_REFRESH_BONES)
    ai = unreal.HrotTerrainLibrary.add_person_state(p)   # the STATE on the actor (persists)
    if ai is None: raise RuntimeError("cannot add HrotPersonState to " + spec["label"])
    ai.set_editor_property("base_anim", clips.get("base"))
    if clips.get("upper"): ai.set_editor_property("upper_anim", clips["upper"])
    if clips.get("action"):
        ai.set_editor_property("action_anim", clips["action"])
        ai.set_editor_property("action_time", float(spec.get("actionTime", 0.35 * clips["action"].get_play_length())))
    ai.set_editor_property("aim_yaw", float(spec.get("yaw", 0))); ai.set_editor_property("aim_pitch", float(spec.get("pitch", 0)))
    ai.set_editor_property("time_offset", 0.3)
    S.setdefault("rows", {}).setdefault(g["name"], []).append(p)
    if spec.get("seq"): S.setdefault("live", []).append({"label": p.get_actor_label(), "spec": spec, "crowd": bool(spec.get("body"))})
    if spec.get("then"):   # a TRANSITION: posed in this state, then switched to "then" and shot `after` seconds later (in shoot())
        S.setdefault("transitions", {})[p.get_actor_label()] = (ai, spec)
    weapon = {"rifle": S["rifle"], "pistol": S["pistol"]}.get(spec.get("weapon"))
    if weapon:
        w = spawn(unreal.StaticMeshActor, (x, y, 100), label=f"W_{spec['label']}")
        w.static_mesh_component.set_static_mesh(weapon); w.static_mesh_component.set_mobility(unreal.ComponentMobility.MOVABLE)
        attach_weapon(w, sk)
        S["rows"][g["name"]].append(w)
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
        dist = max(520.0, n * 125.0)
        if g.get("live"):   # the live row is spaced twice as wide
            S["live_cam"] = {"x": 2 * cx, "y": y, "dist": dist + 250}; continue
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
            if isinstance(a, unreal.SkeletalMeshActor) and a.skeletal_mesh_component.get_animation_mode() == unreal.AnimationMode.ANIMATION_BLUEPRINT:
                sk = a.skeletal_mesh_component
                unreal.HrotTerrainLibrary.pose_now(sk, 0.6, 12)
                tr = S.get("transitions", {}).get(a.get_actor_label())
                if tr:   # switch to the second state, then let `after` seconds of the blend pass
                    st, spec = tr
                    for k, prop in (("base", "base_anim"), ("upper", "upper_anim")):
                        if k in spec["then"]:
                            st.set_editor_property(prop, resolve(spec["then"][k])[1] if spec["then"][k] else None)
                    if "yaw" in spec["then"]: st.set_editor_property("aim_yaw", float(spec["then"]["yaw"]))
                    if "pitch" in spec["then"]: st.set_editor_property("aim_pitch", float(spec["then"]["pitch"]))
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
            LES().editor_request_end_play(); L.update(phase="end", t=now + 4); return True
        for p in people:
            seq = p["spec"]["seq"]; st = seq[min(L["k"], len(seq) - 1)]
            for key, prop in (("base", "base_anim"), ("upper", "upper_anim")):
                if key in st: p["state"].set_editor_property(prop, resolve(st[key], p["crowd"])[1] if st[key] else None)
            if "yaw" in st: p["state"].set_editor_property("aim_yaw", float(st["yaw"]))
            p["speed"] = float(st.get("speed", 0))
        L.update(phase="hold", until=now + 1.6, last=now, shots=0); return True
    if L["phase"] == "hold":   # move the walkers (the clip plays in place) and keep the camera on them
        dt = min(0.1, now - L["last"]); L["last"] = now
        ys = []
        for p in people:
            a = p["actor"]
            fwd = a.get_actor_right_vector()   # both bodies face their actor's +Y
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
    if L["phase"] == "end":
        log("live: Simulate ended"); return False
    return False


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
