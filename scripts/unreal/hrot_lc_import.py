"""hrot_lc_import.py — runs INSIDE the Unreal editor (CE-4651): builds a landscape from terrain-to-unreal.py's GeoTIFF through Landscape
Combinator, line-traces it at the requested points, and (optionally) builds the buildings from our footprints and measures them.

    UnrealEditor.exe <project>.uproject -ExecutePythonScript="<this file>" -unattended -nosplash -abslog=<editorLog>
    with the environment variable HROT_LC_JOB = a job JSON (written by lc-measure.py):
      { "terrain", "tif", "crs" (the SHORT form, UL-3), "origin": [E, N] (the projected point of HROT local 0,0), "components",
        "points": [[x, y], …] (HROT local metres), "out", "level", "timeout", "editorLog",
        "buildings": "<name>.buildings.geojson" (optional), "buildingConfig": "/LandscapeCombinator/Buildings/BuildingConfigurations/BCFG_Basic" }

The frame: Landscape Combinator's LevelCoordinates says Unreal (0, 0) = `origin` in `crs`, 100 cm per metre east, -100 cm per metre north
(Unreal's Y points south) ⇒ HROT local (x, y, z) m = Unreal (100 x, -100 y, 100 z) cm.

Each step that starts a plugin generator waits for the plugin's own log line "Generation for <actor> finished successfully | failed"
(LCGenerator.h:109-113) — its status is not readable from Python.
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_LC_JOB"], encoding="utf-8"))
OUT = JOB["out"]
T0 = time.time()
R = {"job": {k: v for k, v in JOB.items() if k != "points"}}
state = {"handle": None, "busy": False, "step": 0, "wait": None, "log_pos": 0}


def log(msg):
    unreal.log_warning(f"[HROT-LC] {msg}")
    R.setdefault("log", []).append(f"{time.time() - T0:7.1f}s {msg}")


def finish(ok, why=""):
    R["ok"], R["why"], R["seconds"] = ok, why, round(time.time() - T0, 1)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(R, f, indent=1)
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


def world():
    return unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()


def eas():
    return unreal.get_editor_subsystem(unreal.EditorActorSubsystem)


def new_log_lines():
    path = JOB.get("editorLog")
    if not path or not os.path.exists(path): return []
    with open(path, encoding="utf-8", errors="replace") as f:
        f.seek(state["log_pos"]); text = f.read(); state["log_pos"] = f.tell()
    lines = text.splitlines()
    errs = [l for l in lines if "LogConcurrencyHelpers: Error" in l or "LogImageDownloader: Error" in l or "Error:" in l and "LandscapeCombinator" in l
            or "LogBuildingsFromSplines: Error" in l or "LogSplineImporter: Error" in l or "LogGDALInterface: Error" in l]
    if errs: R.setdefault("pluginErrors", []).extend(errs)
    return lines


def start_generator(actor, label, run):
    """Labels the actor, starts its generation, and makes the tick loop wait for the plugin's log line for it."""
    actor.set_actor_label(label)
    state["wait"] = {"label": label, "since": time.time()}
    log(f"generating {label}")
    run()


def waiting():
    """True while the current generator is running; raises when it failed."""
    w = state["wait"]
    if w is None: return False
    for l in new_log_lines():
        if f"Generation for {w['label']} finished successfully" in l:
            R.setdefault("generatorSeconds", {})[w["label"]] = round(time.time() - w["since"], 1)
            log(f"{w['label']} finished in {R['generatorSeconds'][w['label']]} s")
            state["wait"] = None
            return False
        if f"Generation for {w['label']} failed" in l:
            raise RuntimeError(f"{w['label']} failed: " + " | ".join(R.get("pluginErrors", [])[-5:]))
    if time.time() - T0 > JOB.get("timeout", 900):
        raise RuntimeError(f"timeout waiting for {w['label']}")
    return True


# ── the steps ────────────────────────────────────────────────────────────────────────────────────────────────────────────────
def step_level():
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not les.new_level(JOB["level"]):
        raise RuntimeError("new_level failed: " + JOB["level"])
    coords = eas().spawn_actor_from_class(unreal.LevelCoordinates, unreal.Vector(0, 0, 0))
    coords.set_actor_label("HrotLevelCoordinates")
    gc = coords.get_editor_property("global_coordinates")
    gc.set_editor_property("crs", JOB["crs"])
    gc.set_editor_property("cm_per_long_unit", 100.0)
    gc.set_editor_property("cm_per_lat_unit", -100.0)
    gc.set_editor_property("world_origin_long", float(JOB["origin"][0]))
    gc.set_editor_property("world_origin_lat", float(JOB["origin"][1]))
    new_log_lines()   # skip what the log holds so far


def step_landscape():
    sp = eas().spawn_actor_from_class(unreal.LandscapeSpawner, unreal.Vector(0, 0, 0))
    sp.set_editor_property("landscape_label", JOB["terrain"])
    sp.set_editor_property("landscape_tag", "hrot_ground")
    sp.set_editor_property("z_scale", 1.0)
    sp.set_editor_property("components_method", enum(unreal.ComponentsMethod, JOB.get("components", "Auto")))
    hd = sp.get_editor_property("heightmap_downloader")
    hd.set_editor_property("image_source_kind", unreal.ImageSourceKind.LOCAL_FILE)
    hd.set_editor_property("local_file_path", JOB["tif"])
    hd.set_editor_property("crs", JOB["crs"])
    hd.set_editor_property("merge_images", True)   # F-2: a basic level refuses "multiple tiles" without it (LandscapeSpawner.cpp:205)
    state["spawner"] = sp
    start_generator(sp, "HrotLandscapeSpawner", sp.spawn_landscape)


def step_landscape_hrot():
    """UL-9: our own import (tools/unreal/HrotTerrain) — the exporter computed the 16-bit heights and the exact placement."""
    u = JOB["unreal"]
    t = time.time()
    ls = unreal.HrotTerrainLibrary.import_heights(u["r16"], u["sizeX"], u["sizeY"], u["quadsPerSection"], u["sectionsPerComponent"],
                                                  unreal.Vector(*u["locationCm"]), unreal.Vector(*u["scaleCm"]), JOB["terrain"])
    if not ls: raise RuntimeError("HrotTerrain import failed — see LogHrotTerrain")
    ls.tags = ["hrot_ground"]
    state["landscape"] = ls
    R.setdefault("generatorSeconds", {})["HrotTerrain"] = round(time.time() - t, 1)
    log(f"HrotTerrain imported the landscape in {R['generatorSeconds']['HrotTerrain']} s")


def trace(x, y):
    start, end = unreal.Vector(100 * x, -100 * y, 1_000_000), unreal.Vector(100 * x, -100 * y, -1_000_000)
    hit = unreal.SystemLibrary.line_trace_single(world(), start, end, unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, False, [],
                                                 unreal.DrawDebugTrace.NONE, True)
    if hit is None: return None
    t = hit.to_tuple()
    if not t[0]: return None
    return t[4].z / 100.0, t[9].get_actor_label() if t[9] else ""


def bounds_m(actor):
    o, e = actor.get_actor_bounds(False)
    return [(o.x - e.x) / 100, -(o.y + e.y) / 100, (o.z - e.z) / 100], [(o.x + e.x) / 100, -(o.y - e.y) / 100, (o.z + e.z) / 100]


def step_measure_landscape():
    landscape = state.get("landscape")
    if landscape is None:
        ls = state["spawner"].get_editor_property("spawned_landscape")
        landscape = ls if isinstance(ls, unreal.Landscape) else (ls.get() if hasattr(ls, "get") else None)
    if not landscape: raise RuntimeError("the spawner reports no landscape")
    lo, hi = bounds_m(landscape)
    R["landscape"] = {"label": landscape.get_actor_label(), "bounds_min_m": lo, "bounds_max_m": hi,
                      "components": len(landscape.get_components_by_class(unreal.LandscapeComponent))}
    R["traces"] = [[x, y, *(trace(x, y) or (None, None))] for x, y in JOB["points"]]
    log(f"traced {len(R['traces'])} points, {sum(1 for p in R['traces'] if p[2] is None)} missed")


def selection_by_tag(tag):
    s = unreal.ActorSelection()
    s.set_editor_property("actor_selection_mode", unreal.ActorSelectionMode.ACTOR_TAG)
    s.set_editor_property("actor_tag", tag)
    return s


def step_building_splines():
    si = eas().spawn_actor_from_class(unreal.SplineImporter, unreal.Vector(0, 0, 0))
    si.set_editor_property("source", unreal.VectorSource.LOCAL_FILE)
    si.set_editor_property("local_file", JOB["buildings"])
    si.set_editor_property("bounding_method", unreal.BoundingMethod.BOUNDING_ACTOR)
    si.set_editor_property("bounding_actor_selection", selection_by_tag("hrot_ground"))
    si.set_editor_property("actors_or_landscapes_to_place_splines_selection", selection_by_tag("hrot_ground"))
    si.set_editor_property("spline_owner_kind", unreal.SplineOwnerKind.MANY_SPLINE_COLLECTIONS)
    si.set_editor_property("splines_tag", "hrot_buildings")
    si.set_editor_property("spline_components_tag", "hrot_buildings")
    start_generator(si, "HrotBuildingSplines", lambda: si.call_method("Import"))   # a CallInEditor button, not exposed as a method


def step_buildings():
    b = eas().spawn_actor_from_class(unreal.BuildingsFromSplines, unreal.Vector(0, 0, 0))
    b.set_editor_property("splines_tag", "hrot_buildings")
    b.set_editor_property("spline_components_tag", "hrot_buildings")
    path = JOB.get("buildingConfig", "/LandscapeCombinator/Buildings/BuildingConfigurations/BCFG_Basic")
    cfg = unreal.load_asset(path)
    if isinstance(cfg, unreal.Blueprint):   # the plugin's styles are Blueprint subclasses of BuildingConfiguration: use the class's defaults
        cfg = unreal.get_default_object(unreal.EditorAssetLibrary.load_blueprint_class(path))
    if cfg is None: raise RuntimeError("no building configuration asset")
    # ⭐ the floors from OUR data: building_levels first, else height / 3 m (BuildingConfiguration.cpp:50). BCFG_Basic ships with it OFF
    # (a fixed floor count ⇒ every building ~13 m). Set on the class defaults for this run only (the asset is not saved)
    cfg.set_editor_property("auto_compute_num_floors", True)
    w = unreal.WeightedBuildingConfiguration()
    w.set_editor_property("building_configuration", cfg)
    w.set_editor_property("weight", 1.0)
    b.set_editor_property("building_configurations", [w])
    R["buildingConfig"] = {p: str(cfg.get_editor_property(p)) for p in ("level_height", "auto_compute_num_floors", "num_floors", "levels_tag", "height_tag", "roof_kind", "roof_height", "external_wall_thickness", "internal_wall_thickness")
                           if _has(cfg, p)}
    start_generator(b, "HrotBuildings", b.generate_buildings_editor)


def _has(obj, prop):
    try: obj.get_editor_property(prop); return True
    except Exception: return False


def step_measure_buildings():
    out = []
    for a in unreal.GameplayStatics.get_all_actors_of_class(world(), unreal.Building):
        try:
            lo, hi = bounds_m(a)
            ud = a.root_component.get_asset_user_data_of_class(unreal.OSMUserData)
            name = ud.get_editor_property("fields").get("name", "") if ud else ""   # our footprint's id, carried from the GeoJSON
            out.append({"label": a.get_actor_label(), "name": name, "min": [round(v, 3) for v in lo], "max": [round(v, 3) for v in hi]})
        except Exception as ex:
            out.append({"label": a.get_actor_label(), "error": str(ex)})
    R["buildings"] = out
    # the INPUT of each building too: the footprint splines the importer made (by the same id), to tell the importer's error from the generator's
    sp = []
    for a in unreal.GameplayStatics.get_all_actors_with_tag(world(), "hrot_buildings"):
        for c in a.get_components_by_class(unreal.SplineComponent):
            ud = c.get_asset_user_data_of_class(unreal.OSMUserData)
            name = ud.get_editor_property("fields").get("name", "") if ud else ""
            n = c.get_number_of_spline_points()
            pts = [c.get_location_at_spline_point(i, unreal.SplineCoordinateSpace.WORLD) for i in range(n)]
            if pts:
                xs = [p.x / 100 for p in pts]; ys = [-p.y / 100 for p in pts]
                sp.append({"name": name, "min": [round(min(xs), 3), round(min(ys), 3)], "max": [round(max(xs), 3), round(max(ys), 3)], "points": n})
    R["buildingSplines"] = sp
    n_splines = len(unreal.GameplayStatics.get_all_actors_with_tag(world(), "hrot_buildings"))
    R["buildingSplineActors"] = n_splines
    log(f"{len(out)} buildings, {n_splines} spline actors tagged hrot_buildings")


def step_save():
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).save_current_level()


STEPS = [step_level, step_landscape_hrot if JOB.get("importer") == "hrot" else step_landscape, step_measure_landscape]
if JOB.get("buildings"):
    STEPS += [step_building_splines, step_buildings, step_measure_buildings]
STEPS += [step_save]


def tick(dt):
    if state["busy"]: return   # new_level and the plugin pump the editor's ticks: never re-enter
    try:
        state["busy"] = True
        if waiting(): return
        if state["step"] >= len(STEPS):
            finish(True); return
        s = STEPS[state["step"]]; state["step"] += 1
        s()
    except Exception:
        state["step"] = len(STEPS) + 1
        finish(False, traceback.format_exc())
    finally:
        state["busy"] = False


# ⛔ F-1: -ExecutePythonScript closes the editor on its next tick unless the script asks to stay (EditorPythonExecuter.cpp:73)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
state["handle"] = unreal.register_slate_post_tick_callback(tick)
