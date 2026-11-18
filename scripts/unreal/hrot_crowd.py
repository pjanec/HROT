"""hrot_crowd.py — runs INSIDE the Unreal editor (CE-4651, U-16): the 1000-people test.

    HROT_CROWD_JOB = { "lookLevel", "crowdLevel", "count", "centre": [x, y] (HROT local m), "radius", "views": [view json…],
                       "shots", "out", "measureSeconds" }

Copies the City Sample crowd demo's dressed people (each a leader-pose body + outfit parts) into the Stechovice look level until there
are `count`, stands them on the ground around `centre` (a line trace onto our landscape / road), then measures the frame time with
the crowd hidden and shown at each view, and takes a shot of each. ⚠ The editor's frame time is a proxy (it includes the editor), but
the hidden / shown difference at the same view is the crowd's cost.
"""
import json, math, os, random, time, traceback
import unreal

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
    for c in ("t.IdleWhenNotForeground 0", "t.MaxFPS 0", "r.VSync 0"):
        unreal.SystemLibrary.execute_console_command(None, c)


def step_copy_demo():
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
        cls = S["classes"][len(crowd) % len(S["classes"])]
        a = EAS().spawn_actor_from_class(cls, unreal.Vector(x, y, z), unreal.Rotator(0, 0, rng.uniform(0, 360)))
        if not a: continue
        a.set_folder_path("Crowd"); a.set_actor_label(f"Person_{len(crowd):04d}")
        crowd.append(a)
    R["people"] = len(crowd)
    log(f"{len(crowd)} people placed within {JOB['radius']} m in {time.time() - t:.0f} s; editor memory {proc_gb():.1f} GB")


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
    S["queue"] = q


def measure_tick(dt):
    """One phase at a time; True while there is work."""
    if not S.get("queue"): return False
    ph = S["queue"][0]
    if "spawn" in ph:
        spawn_up_to(ph["spawn"]); S["queue"].pop(0); return True
    now = time.time()
    if "start" not in ph:
        camera(ph["view"])
        ph["start"] = now; ph["settle_until"] = now + JOB.get("settleSeconds", 15); ph["dts"] = []
        return True
    if now < ph["settle_until"]: return True
    if now < ph["settle_until"] + JOB.get("measureSeconds", 10):
        ph["dts"].append(dt); return True
    d = sorted(ph["dts"])
    m = {"view": ph["view"]["name"], "people": ph["count"], "frames": len(d),
         "msMean": round(1000 * sum(d) / max(len(d), 1), 2), "msMedian": round(1000 * d[len(d) // 2], 2) if d else None,
         "ms95": round(1000 * d[int(len(d) * 0.95)], 2) if d else None, "editorGB": round(proc_gb(), 2)}
    R["measure"].append(m)
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)   # partial results survive a stop
    log(f"{m['view']} with {m['people']} people: {m['msMean']} ms mean, {m['ms95']} ms p95, editor {m['editorGB']} GB")
    if ph["count"]:
        path = os.path.join(JOB["shots"], f"crowd-{ph['count']}-{ph['view']['name']}.png").replace("\\", "/")
        if os.path.exists(path): os.remove(path)
        unreal.AutomationLibrary.take_high_res_screenshot(1920, 1080, path)
        R["shots"].append(path)
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
