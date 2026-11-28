"""hrot_crowd.py — runs INSIDE the Unreal editor (CE-4651, U-16): the 1000-people test.

    HROT_CROWD_JOB = { "lookLevel", "crowdLevel", "count", "centre": [x, y] (HROT local m), "radius", "views": [view json…],
                       "shots", "out", "measureSeconds" }

Copies the City Sample crowd demo's dressed people (each a leader-pose body + outfit parts) into the Stechovice look level until there
are `count`, stands them on the ground around `centre` (a line trace onto our landscape / road), then measures the frame time with
the crowd hidden and shown at each view, and takes a shot of each. ⚠ The editor's frame time is a proxy (it includes the editor), but
the hidden / shown difference at the same view is the crowd's cost.
"""
import json, math, os, random, sys, time, traceback
import unreal
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hrot_person

JOB = json.load(open(os.environ["HROT_CROWD_JOB"], encoding="utf-8"))
T0 = time.time()
R = {"log": [], "measure": [], "shots": []}
S = {"handle": None, "busy": False, "step": 0, "ticks": []}


def log(m):
    unreal.log_warning(f"[HROT-CROWD] {m}"); R["log"].append(f"{time.time() - T0:7.1f}s {m}")


def finish(ok, why=""):
    R["ok"], R["why"] = ok, why
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    if S["handle"] is not None: unreal.unregister_slate_post_tick_callback(S["handle"]); S["handle"] = None
    if not os.environ.get("HROT_CROWD_KEEP_OPEN"): unreal.EditorPythonScripting.set_keep_python_script_alive(False)


EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
LES = lambda: unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
WORLD = lambda: unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()


def step_settings():
    # the editor idles when not focused (EditorPerformanceSettings is not exposed to Python): its console switches instead
    # ⚠ measured: the empty scene read exactly 16.67 ms — capped at 60 fps; the editor's smoothed frame rate is turned off too
    # ⭐ the editor throttles to 60 Hz ON BATTERY (UEditorEngine::GetMaxTickRate) — r.DontLimitOnBattery; above 62 fps the engine's
    # SmoothedFrameRateRange caps it (project DefaultEngine.ini lifts that)
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0", "r.DontLimitOnBattery 1"):
        unreal.SystemLibrary.execute_console_command(None, c)


# ⭐ "soldiers": state-driven US soldiers (hrot_person — the same setup as the anim lab), armed, in a mix of states
SOLDIER_STATES = [
    {"base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"},
    {"base": "MM_Rifle_Idle_Hipfire", "weapon": "rifle"},
    {"base": "MM_Rifle_Crouch_Idle", "weapon": "rifle"},
    {"base": "MF_Rifle_Jog_Fwd", "weapon": "rifle"},
]


def step_copy_demo():
    if JOB.get("people") == "soldiers":
        log("people: state-driven US soldiers"); return
    """Each dressed demo person becomes a Blueprint (/Game/Hrot/People/BP_Person_NN — body + outfit parts on a leader pose), made once."""
    if not LES().load_level(JOB["crowdLevel"]): raise RuntimeError("cannot load " + JOB["crowdLevel"])
    people = [a for a in EAS().get_all_level_actors() if "Crowd" in a.get_class().get_name()]
    classes = []
    for i, a in enumerate(people):
        path = f"/Game/Hrot/People/BP_Person_{i:02d}"
        if unreal.EditorAssetLibrary.does_asset_exist(path):
            classes.append(unreal.EditorAssetLibrary.load_blueprint_class(path)); continue
        c = unreal.HrotTerrainLibrary.blueprint_from_actor(a, path)
        if c: classes.append(c)
    unreal.EditorLoadingAndSavingUtils.save_dirty_packages(False, True)
    S["classes"] = [c for c in classes if c]
    S["n_demo"] = len(S["classes"])
    log(f"{len(S['classes'])} person Blueprints from the crowd demo's {len(people)} dressed people")


def step_open_look():
    if not LES().load_level(JOB["lookLevel"]): raise RuntimeError("cannot load " + JOB["lookLevel"])
    log("opened the look level")
    if JOB.get("people") == "soldiers": S["kit"] = hrot_person.PersonKit(log)


def ground(x, y):
    """Unreal (x, y) cm → the top of our landscape / road there, or None (a house, a tree, nothing)."""
    hit = unreal.SystemLibrary.line_trace_single(WORLD(), unreal.Vector(x, y, 2_000_000), unreal.Vector(x, y, -2_000_000),
                                                 unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, False, [], unreal.DrawDebugTrace.NONE, True)
    if hit is None: return None
    t = hit.to_tuple()
    if not t[0]: return None
    label = t[9].get_actor_label() if t[9] else ""
    if not (isinstance(t[9], unreal.Landscape) or label.startswith("Road_")): return None
    return t[4].z


def spawn_up_to(target):
    """Adds people around the centre until there are `target` (the earlier ones stay where they are)."""
    rng = S.setdefault("rng", random.Random(4651))
    cx, cy = JOB["centre"][0] * 100, -JOB["centre"][1] * 100
    radius = JOB["radius"] * 100
    crowd = S.setdefault("crowd", [])
    tries = 0
    t = time.time()
    while len(crowd) < target and tries < target * 30:
        tries += 1
        ang = rng.uniform(0, 2 * math.pi); r = radius * math.sqrt(rng.random())
        x, y = cx + r * math.cos(ang), cy + r * math.sin(ang)
        z = ground(x, y)
        if z is None: continue
        if JOB.get("people") == "soldiers":
            made = S["kit"].make("US_Soldier", (x, y, z), rng.uniform(0, 360), f"Person_{len(crowd):04d}",
                                 SOLDIER_STATES[len(crowd) % len(SOLDIER_STATES)], folder="Crowd")
            a = made[0] if made else None
            if made and JOB.get("merge"):   # the body's 15 parts merged into one mesh (UHrotPersonState.MergeBodyParts)
                n = made[1].merge_body_parts()
                if not crowd: log(f"merged {n} parts into the body")
        else:
            cls = S["classes"][len(crowd) % len(S["classes"])]
            a = EAS().spawn_actor_from_class(cls, unreal.Vector(x, y, z), unreal.Rotator(0, 0, rng.uniform(0, 360)))
        if not a: continue
        if JOB.get("people") != "soldiers":   # the demo people only (a soldier is named at spawn: a later rename rebuilds it)
            a.set_folder_path("Crowd"); a.set_actor_label(f"Person_{len(crowd):04d}")
        crowd.append(a)
    R["people"] = len(crowd)
    if JOB.get("fire") and not JOB.get("play"): start_fire(crowd)
    if JOB.get("people") == "soldiers":   # ⭐ check, never assume: who runs our animation and holds a visible weapon
        tally = {}
        for a in crowd:
            st = a.get_component_by_class(unreal.HrotPersonState)
            lead = st.find_leader() if st else None
            ai = lead.get_anim_instance() if lead else None
            w = st.get_weapon_component() if st else None
            key = (ai.get_class().get_name() if ai else None, bool(w and w.is_visible()), str(w.get_attach_socket_name()) if w else None)
            tally[str(key)] = tally.get(str(key), 0) + 1
        R.setdefault("tally", []).append(tally); log(f"people by (anim, weapon visible, weapon socket): {tally}")
    log(f"{len(crowd)} people placed within {JOB['radius']} m in {time.time() - t:.0f} s; editor memory {proc_gb():.1f} GB")


def start_fire(crowd):
    """⭐ every soldier fires bursts (UHrotShotRenderer's native driver — measures the shots, not Python). In play: the PLAY copies."""
    if crowd:
        r = unreal.HrotShotRenderer.for_world(crowd[0])
        if JOB.get("allNiagara"):   # the comparison: the old way — every extra spawned per shot, no range, no budget
            r.near_range = 1e9; r.impact_range = 1e9
            r.smoke_per_second = r.shell_per_second = r.impact_per_second = 1e7
        for k, prop in (("smokePerSecond", "smoke_per_second"), ("shellPerSecond", "shell_per_second"), ("impactPerSecond", "impact_per_second"),
                        ("maxLights", "max_lights"), ("nearRange", "near_range"),
                        ("nearSmoke", "near_smoke"), ("nearShell", "near_shell"), ("impacts", "impacts")):
            if JOB.get(k) is not None: setattr(r, prop, JOB[k])   # the knobs: what each part of a shot costs
        states = [a.get_component_by_class(unreal.HrotPersonState) for a in crowd]
        r.start_test_fire([s for s in states if s], float(JOB["fire"]), 1.0, 1.0)
        S["fire_start"] = (time.time(), 0)
        log(f"firing: {len(states)} shooters, {JOB['fire']} rounds/s in 1 s bursts, 1 s pauses ({'all Niagara' if JOB.get('allNiagara') else 'instanced + near budget'}; budgets/s smoke {r.smoke_per_second:.0f} shell {r.shell_per_second:.0f} impact {r.impact_per_second:.0f}, lights {r.max_lights})")


def proc_gb():
    """The editor process's working set (GB) — ctypes, psapi."""
    try:
        import ctypes
        from ctypes import wintypes
        class PMC(ctypes.Structure):
            _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD), ("PeakWorkingSetSize", ctypes.c_size_t),
                        ("WorkingSetSize", ctypes.c_size_t), ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                        ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t)]
        c = PMC(); c.cb = ctypes.sizeof(PMC)
        h = ctypes.windll.kernel32.GetCurrentProcess()
        ctypes.windll.psapi.GetProcessMemoryInfo(h, ctypes.byref(c), c.cb)
        return c.WorkingSetSize / 2**30
    except Exception:
        return -1.0


def set_crowd_hidden(hidden):
    for a in S["crowd"]: a.set_is_temporarily_hidden_in_editor(hidden)


def camera(v):
    ues = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
    ues.set_level_viewport_camera_info(unreal.Vector(*v["unreal"]["locationCm"]), unreal.Rotator(roll=0, pitch=v["unreal"]["pitch"], yaw=v["unreal"]["yaw"]))


def plan():
    """Stepped: nobody first (the baseline at each view), then for each count in JOB["steps"] — spawn up to it, measure each view."""
    q = [{"view": v, "count": 0} for v in JOB["views"]]
    for n in JOB["steps"]:
        q.append({"spawn": n})
        q += [{"view": v, "count": n} for v in JOB["views"]]
    if JOB.get("play") and JOB.get("playVariants"):   # ⭐ what the soldiers' UPDATE costs: settings switched live, one Simulate session
        street = [v for v in JOB["views"] if v["name"] == "street"] or JOB["views"][:1]
        q.append({"play": "start"})
        for var in JOB["playVariants"]:
            q.append({"variant": var})
            q += [{"view": v, "count": JOB["steps"][-1], "mode": "play: " + var} for v in street]
        q.append({"play": "stop"})
    elif JOB.get("play"):   # ⭐ PLAY MODE (Simulate): the engine runs as in the image generator; soldiers shown, then hidden (still animating)
        q.append({"play": "start"})
        q += [{"view": v, "count": JOB["steps"][-1], "mode": "play"} for v in JOB["views"]]
        q.append({"hide": True})
        q += [{"view": v, "count": JOB["steps"][-1], "mode": "play, hidden"} for v in JOB["views"]]
        q.append({"play": "stop"})
    S["queue"] = q


def apply_variant(var):
    """always: tick + refresh bones even off-screen (the lab setting) · rendered: only when rendered · uro: + update-rate optimisation
    (distant people update less often) · single: the engine's plain single-clip animation instead of UHrotPersonAnimInstance"""
    T = unreal.VisibilityBasedAnimTickOption
    opt = T.ALWAYS_TICK_POSE_AND_REFRESH_BONES if var == "always" else T.ONLY_TICK_POSE_WHEN_RENDERED
    unreal.SystemLibrary.execute_console_command(None, "a.URO.ForceAnimRate " + ("8" if var == "uro8" else "0"))
    n = 0
    for a in S.get("play_crowd", []):
        st = a.get_component_by_class(unreal.HrotPersonState)
        lead = st.find_leader() if st else None
        if not lead: continue
        # ⚠ setattr, never set_editor_property: the edit notification REBUILDS the person (measured: its parts ticked again, +8 ms)
        setattr(lead, "visibility_based_anim_tick_option", opt)
        setattr(lead, "enable_update_rate_optimizations", var in ("uro", "uro8", "single"))
        w = st.get_weapon_component()
        if isinstance(w, unreal.SkeletalMeshComponent): setattr(w, "visibility_based_anim_tick_option", opt)
        if var == "notick":   # nothing on the soldier ticks at all (the per-actor overhead, isolated)
            if n == 0:
                ticking = [f"{c.get_name()}({c.get_class().get_name()})" for c in a.get_components_by_class(unreal.ActorComponent) if c.is_component_tick_enabled()]
                log(f"ticking on one soldier: actor tick {a.is_actor_tick_enabled()}; components {ticking}")
            a.set_actor_tick_enabled(False)
            for c in a.get_components_by_class(unreal.ActorComponent): c.set_component_tick_enabled(False)
            a.set_actor_hidden_in_game(True)
        if var == "nolod" and n == 0:   # the comparison: every person every frame (UHrotPeopleLod off)
            setattr(unreal.HrotPeopleLod.for_world(a), "enabled", False)
        if var.startswith("interval"):   # the body ticks every N ms (interval100 = 10 Hz): does the cost scale with tick COUNT?
            lead.set_component_tick_interval(float(var[len("interval"):]) / 1000.0)
        if var == "noweapontick" and w:   # the weapon's bones never animate: its tick is pure cost
            w.set_component_tick_enabled(False)
        if var == "hiddenquiet":   # neither drawn nor animated: what remains is the per-actor overhead
            a.set_actor_hidden_in_game(True)
        if var == "frozen":   # no animation at all: what remains is skinning, drawing, per-actor overhead
            lead.set_component_tick_enabled(False)
            if w: w.set_component_tick_enabled(False)
        if var == "single":
            clip = st.get_editor_property("base_anim")
            lead.set_animation_mode(unreal.AnimationMode.ANIMATION_SINGLE_NODE); lead.play_animation(clip, True)
        n += 1
    log(f"play variant '{var}' on {n} people")


def game_world():
    return unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_game_world()


def measure_tick(dt):
    """One phase at a time; True while there is work."""
    if not S.get("queue"): return False
    ph = S["queue"][0]
    if "waitFile" in ph:
        if (os.path.exists(ph["waitFile"]) and os.path.getsize(ph["waitFile"]) > 0) or time.time() > ph["until"]: S["queue"].pop(0)
        return True
    if "spawn" in ph:
        spawn_up_to(ph["spawn"]); S["queue"].pop(0); return True
    if "play" in ph:
        if ph["play"] == "start":
            if "t" not in ph:
                LES().editor_play_simulate(); ph["t"] = time.time() + 8; return True
            if time.time() < ph["t"]: return True
            w = game_world()
            S["play_crowd"] = [a for a in unreal.GameplayStatics.get_all_actors_of_class(w, unreal.Actor) if a.get_actor_label().startswith("Person_")] if w else []
            log(f"play: Simulate running, {len(S['play_crowd'])} people in the game world")
            if JOB.get("fire") and S["play_crowd"]: start_fire(S["play_crowd"])
        else:
            LES().editor_request_end_play(); log("play: ended")
        S["queue"].pop(0); return True
    if "variant" in ph:
        apply_variant(ph["variant"]); S["queue"].pop(0); return True
    if "hide" in ph:
        for a in S.get("play_crowd", []): a.set_actor_hidden_in_game(True)
        log(f"play: {len(S.get('play_crowd', []))} people hidden (still animating)")
        S["queue"].pop(0); return True
    now = time.time()
    if "start" not in ph:
        camera(ph["view"])
        ph["start"] = now; ph["settle_until"] = now + JOB.get("settleSeconds", 15); ph["dts"] = []
        return True
    if now < ph["settle_until"]: return True
    if now < ph["settle_until"] + JOB.get("measureSeconds", 10):
        ph["dts"].append(dt); return True
    d = sorted(ph["dts"])
    m = {"view": ph["view"]["name"], "people": ph["count"], "mode": ph.get("mode", "editor"), "frames": len(d),
         "msMean": round(1000 * sum(d) / max(len(d), 1), 2), "msMedian": round(1000 * d[len(d) // 2], 2) if d else None,
         "ms95": round(1000 * d[int(len(d) * 0.95)], 2) if d else None, "editorGB": round(proc_gb(), 2)}
    if S.get("fire_start") and S.get("crowd") and (not JOB.get("play") or S.get("play_crowd")):
        r = unreal.HrotShotRenderer.for_world((S.get("play_crowd") or S["crowd"])[0])
        t0, n0 = S["fire_start"]; m["roundsPerSecond"] = round((r.test_rounds_fired - n0) / max(time.time() - t0, 0.001))
        m["liveFlashes"], m["liveTracers"], m["nearSpawnedLastFrame"] = r.live_flashes, r.live_tracers, r.near_spawned_last_frame
        m["fireMs"], m["rebuildMs"] = round(r.fire_ms_last_frame, 2), round(r.rebuild_ms_last_frame, 2)
        S["fire_start"] = (time.time(), r.test_rounds_fired)
    R["measure"].append(m)
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)   # partial results survive a stop
    log(f"[{m['mode']}] {m['view']} with {m['people']} people: {m['msMean']} ms mean, {m['ms95']} ms p95, editor {m['editorGB']} GB"
        + (f"; {m['roundsPerSecond']} rounds/s, {m['liveFlashes']} flashes / {m['liveTracers']} tracers live; renderer: fire {m['fireMs']} ms, rebuild {m['rebuildMs']} ms" if 'roundsPerSecond' in m else ""))
    if ph["count"]:
        tag = ph.get("mode", "editor").replace(": ", "-").replace(", ", "-").replace(" ", "-")
        path = os.path.join(JOB["shots"], f"crowd-{ph['count']}-{ph['view']['name']}-{tag}.png").replace("\\", "/")
        if os.path.exists(path): os.remove(path)
        unreal.AutomationLibrary.take_high_res_screenshot(1920, 1080, path)
        R["shots"].append(path)
        # ⚠ the capture lands frames later: hold this camera until the file exists (measured: the next view's camera was in it)
        S["queue"].insert(1, {"waitFile": path, "until": time.time() + 30})
    S["queue"].pop(0)
    return True


def step_save():
    LES().save_current_level()
    unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    log("saved")


STEPS = [step_settings, step_copy_demo, step_open_look, plan, None]   # ⛔ not saved: a test, the look level stays as look.py built it


def system_free_gb():
    try:
        import ctypes
        class MS(ctypes.Structure):
            _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong), ("ullTotalPhys", ctypes.c_ulonglong),
                        ("ullAvailPhys", ctypes.c_ulonglong), ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                        ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong), ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]
        m = MS(); m.dwLength = ctypes.sizeof(MS); ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
        return m.ullAvailPhys / 2**30
    except Exception:
        return None


def tick(dt):
    if S["busy"]: return
    f = system_free_gb()   # ⭐ the editor's OWN watchdog: the driver's can be stopped (it was, run 3) and leave the editor unsupervised
    if f is not None and f < JOB.get("minFreeGb", 6.0):
        R["stoppedFor"] = f"free memory {f:.1f} GB"; log(R["stoppedFor"] + " - stopping")
        S["step"] = len(STEPS) + 1; finish(False, R["stoppedFor"]); unreal.SystemLibrary.quit_editor(); return
    try:
        S["busy"] = True
        if S["step"] < len(STEPS) and STEPS[S["step"]] is None:
            if measure_tick(dt): return
            S["step"] += 1; return
        if S["step"] >= len(STEPS): finish(True); return
        s = STEPS[S["step"]]; S["step"] += 1; s()
    except Exception:
        S["step"] = len(STEPS) + 1; finish(False, traceback.format_exc())
    finally:
        S["busy"] = False


unreal.EditorPythonScripting.set_keep_python_script_alive(True)
S["handle"] = unreal.register_slate_post_tick_callback(tick)
