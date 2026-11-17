"""hrot_lc_import.py — runs INSIDE the Unreal editor (CE-4651): builds a landscape from terrain-to-unreal.py's GeoTIFF through Landscape
Combinator, then line-traces it at the requested points and writes the traced heights.

    UnrealEditor.exe <project>.uproject -ExecutePythonScript="<this file>" -unattended -nosplash
    with the environment variable HROT_LC_JOB = a job JSON:
      { "terrain": "basic-desert", "tif": "...height.tif", "crs": "<CRS of the tif>", "origin": [E, N] (the projected point of HROT local 0,0),
        "components": "Auto" | "AutoWithoutBorder" | "Preset_…", "points": [[x, y], …] (HROT local metres), "out": "<result.json>",
        "level": "/Game/Maps/<name>", "timeout": 900 }

The frame: Landscape Combinator's LevelCoordinates says Unreal (0, 0) = `origin` in `crs`, 100 cm per metre east, -100 cm per metre north
(Unreal's Y points south) ⇒ HROT local (x, y, z) m = Unreal (100 x, -100 y, 100 z) cm.
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_LC_JOB"], encoding="utf-8"))
OUT = JOB["out"]
T0 = time.time()
state = {"phase": "setup", "spawner": None, "handle": None, "result": {"job": {k: v for k, v in JOB.items() if k != "points"}}}


def log(msg):
    unreal.log_warning(f"[HROT-LC] {msg}")
    state["result"].setdefault("log", []).append(f"{time.time() - T0:7.1f}s {msg}")


def finish(ok, why=""):
    state["result"]["ok"] = ok
    state["result"]["why"] = why
    state["result"]["seconds"] = round(time.time() - T0, 1)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(state["result"], f, indent=1)
    if state["handle"] is not None:
        unreal.unregister_slate_post_tick_callback(state["handle"])
        state["handle"] = None
    if not os.environ.get("HROT_LC_KEEP_OPEN"):
        unreal.EditorPythonScripting.set_keep_python_script_alive(False)   # the executer then closes the editor


def enum(enum_type, name):
    for v in dir(enum_type):
        if v.lower() == name.lower().replace("_", "") or v.lower() == name.lower():
            return getattr(enum_type, v)
    up = "".join(("_" + c if c.isupper() and i else c) for i, c in enumerate(name)).upper().replace("__", "_")
    return getattr(enum_type, up)


def setup():
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not les.new_level(JOB["level"]):
        raise RuntimeError("new_level failed: " + JOB["level"])
    eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)

    coords = eas.spawn_actor_from_class(unreal.LevelCoordinates, unreal.Vector(0, 0, 0))
    gc = coords.get_editor_property("global_coordinates")
    gc.set_editor_property("crs", JOB["crs"])
    gc.set_editor_property("cm_per_long_unit", 100.0)
    gc.set_editor_property("cm_per_lat_unit", -100.0)
    gc.set_editor_property("world_origin_long", float(JOB["origin"][0]))
    gc.set_editor_property("world_origin_lat", float(JOB["origin"][1]))

    sp = eas.spawn_actor_from_class(unreal.LandscapeSpawner, unreal.Vector(0, 0, 0))
    sp.set_editor_property("landscape_label", JOB["terrain"])
    sp.set_editor_property("z_scale", 1.0)
    sp.set_editor_property("components_method", enum(unreal.ComponentsMethod, JOB.get("components", "Auto")))
    hd = sp.get_editor_property("heightmap_downloader")
    hd.set_editor_property("image_source_kind", unreal.ImageSourceKind.LOCAL_FILE)
    hd.set_editor_property("local_file_path", JOB["tif"])
    hd.set_editor_property("crs", JOB["crs"])
    hd.set_editor_property("merge_images", True)   # a basic (non World Partition) level refuses "multiple tiles" without it (LandscapeSpawner.cpp:205)
    state["spawner"] = sp
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    found = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.LevelCoordinates)
    log(f"LevelCoordinates in the world: {len(found)}, hidden {[a.get_editor_property('hidden') for a in found]}, "
        f"spawner world == coords world: {sp.get_world() == coords.get_world() == world}")
    gc2 = found[0].get_editor_property("global_coordinates") if found else None
    log(f"the world CRS as the plugin will read it: {gc2.get_editor_property('crs')[:80] if gc2 else None!r} == job crs: "
        f"{gc2 is not None and gc2.get_editor_property('crs') == JOB['crs']}")
    log("spawning the landscape")
    state["spawn_started"] = time.time()
    sp.spawn_landscape()


def trace(world, x, y):
    start, end = unreal.Vector(100 * x, -100 * y, 1_000_000), unreal.Vector(100 * x, -100 * y, -1_000_000)
    hit = unreal.SystemLibrary.line_trace_single(world, start, end, unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, False, [],
                                                 unreal.DrawDebugTrace.NONE, True)
    if hit is None: return None
    t = hit.to_tuple()
    blocking, loc, actor = t[0], t[4], t[9]
    if not blocking: return None
    return loc.z / 100.0, actor.get_actor_label() if actor else ""


def measure(landscape):
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    info = {"label": landscape.get_actor_label(), "location": str(landscape.get_actor_location()), "scale": str(landscape.get_actor_scale3d())}
    try:
        o, e = landscape.get_actor_bounds(False)
        info["bounds_min_m"] = [(o.x - e.x) / 100, -(o.y + e.y) / 100, (o.z - e.z) / 100]
        info["bounds_max_m"] = [(o.x + e.x) / 100, -(o.y - e.y) / 100, (o.z + e.z) / 100]
    except Exception as ex:
        info["bounds_error"] = str(ex)
    for prop in ("component_size_quads", "subsection_size_quads", "num_subsections"):
        try: info[prop] = landscape.get_editor_property(prop)
        except Exception as ex: info[prop] = f"? {ex}"
    try:
        info["components"] = len(landscape.get_components_by_class(unreal.LandscapeComponent))
    except Exception as ex:
        info["components"] = f"? {ex}"
    state["result"]["landscape"] = info
    pts = []
    for x, y in JOB["points"]:
        r = trace(world, x, y)
        pts.append([x, y, None if r is None else r[0], None if r is None else r[1]])
    state["result"]["traces"] = pts
    log(f"traced {len(pts)} points, {sum(1 for p in pts if p[2] is None)} missed")


def plugin_failed():
    """The plugin's generation runs on its own thread and reports a failure only in the log ⇒ read the editor's log (-abslog)."""
    path = JOB.get("editorLog")
    if not path or not os.path.exists(path): return ""
    with open(path, encoding="utf-8", errors="replace") as f:
        f.seek(state.get("log_pos", 0)); text = f.read(); state["log_pos"] = f.tell()
    errs = [l for l in text.splitlines() if "LogConcurrencyHelpers: Error" in l or "LogImageDownloader: Error" in l or "Generation for" in l and "failed" in l]
    state.setdefault("plugin_errors", []).extend(errs)
    state["result"]["pluginErrors"] = state["plugin_errors"]
    return next((l for l in state["plugin_errors"] if "Generation for" in l), "")


def tick(dt):
    try:
        if state.get("busy"):   # new_level / the spawn pump the editor's ticks: never re-enter
            return
        if state["phase"] == "setup":
            state["busy"] = True
            try:
                setup()
            finally:
                state["busy"] = False
            state["phase"] = "spawning"
            return
        if state["phase"] == "spawning":
            sp = state["spawner"]
            ls = sp.get_editor_property("spawned_landscape")
            landscape = ls if isinstance(ls, unreal.Landscape) else (ls.get() if hasattr(ls, "get") else None)
            if landscape:
                state["result"]["spawn_seconds"] = round(time.time() - state["spawn_started"], 1)
                log(f"landscape spawned in {state['result']['spawn_seconds']} s")
                state["phase"] = "settle"; state["settle_until"] = time.time() + 5
                state["landscape"] = landscape
            elif plugin_failed():
                finish(False, "the plugin reported a failure: " + plugin_failed())
            elif time.time() - T0 > JOB.get("timeout", 900):
                finish(False, "timeout waiting for the landscape")
            return
        if state["phase"] == "settle" and time.time() >= state["settle_until"]:
            state["phase"] = "done"
            measure(state["landscape"])
            unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).save_current_level()
            finish(True)
    except Exception:
        state["phase"] = "done"
        finish(False, traceback.format_exc())


# ⛔ -ExecutePythonScript closes the editor on its next tick unless the script asks to stay (EditorPythonExecuter.cpp:73)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
state["handle"] = unreal.register_slate_post_tick_callback(tick)
