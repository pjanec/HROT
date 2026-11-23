"""hrot_look.py — runs INSIDE the Unreal editor (CE-4651, UL-10): builds a LOOK level from one exported terrain and renders its views.

    UnrealEditor.exe <project> -ExecutePythonScript="<this file>" -unattended -nosplash -abslog=<log>    (driven by look.py)
    HROT_LOOK_JOB = { "terrain", "unreal" (frame.json's "unreal", r16 made absolute), "cover", "trees", "views" (views.json),
                      "level", "shots" (a folder), "out" (result json), "settleSeconds", "width", "height",
                      "treeMesh", "treeMaterial" (optional asset paths; default: a placeholder cone) }

Everything is OUR data: the ground (HrotTerrain.ImportHeights, exact), the ground's colour by our forest / water / open areas
(<name>.cover.png) and by slope, the trees at our forest points (HrotTerrain.ScatterInstances), plus Unreal's own sky, sun, clouds,
fog and Lumen. ⚠ The procedural colours and the cone trees are PLACEHOLDERS until real assets (Megascans / Megaplants) are in the project.
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_LOOK_JOB"], encoding="utf-8"))
T0 = time.time()
R = {"job": {k: v for k, v in JOB.items() if k != "views"}, "log": [], "shots": []}
state = {"handle": None, "busy": False, "step": 0, "wait_until": 0.0, "pending": None}
MEL = unreal.MaterialEditingLibrary
ASSETS = unreal.AssetToolsHelpers.get_asset_tools()


def log(msg):
    unreal.log_warning(f"[HROT-LOOK] {msg}")
    R["log"].append(f"{time.time() - T0:7.1f}s {msg}")


def finish(ok, why=""):
    R["ok"], R["why"], R["seconds"] = ok, why, round(time.time() - T0, 1)
    with open(JOB["out"], "w", encoding="utf-8") as f:
        json.dump(R, f, indent=1)
    if state["handle"] is not None:
        unreal.unregister_slate_post_tick_callback(state["handle"]); state["handle"] = None
    if not os.environ.get("HROT_LOOK_KEEP_OPEN"):
        unreal.EditorPythonScripting.set_keep_python_script_alive(False)


def tname(base):
    """U-19h (2026-11-23): an asset that holds ONE terrain's data (its cover / road masks, the ground and road materials reading them) is named
    per terrain - a shared name let rebuilding one terrain's level overwrite the masks every other look level reads."""
    return f"{base}_{JOB['terrain'].replace('-', '_')}"


def eas():
    return unreal.get_editor_subsystem(unreal.EditorActorSubsystem)


def spawn(cls, loc=(0, 0, 0), rot=(0, 0, 0), label=None):
    a = eas().spawn_actor_from_class(cls, unreal.Vector(*loc), unreal.Rotator(roll=rot[2], pitch=rot[0], yaw=rot[1]))
    if label: a.set_actor_label(label)
    return a


def asset_path(folder, name):
    return f"{folder}/{name}"


def fresh_asset(folder, name, cls, factory):
    path = asset_path(folder, name)
    if unreal.EditorAssetLibrary.does_asset_exist(path):
        unreal.EditorAssetLibrary.delete_asset(path)
    return ASSETS.create_asset(name, folder, cls, factory)


# ── the steps ────────────────────────────────────────────────────────────────────────────────────────────────────────────────
def step_level():
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not les.new_level(JOB["level"]): raise RuntimeError("new_level failed: " + JOB["level"])


def step_ground():
    u = JOB["unreal"]
    ls = unreal.HrotTerrainLibrary.import_heights(u["r16"], u["sizeX"], u["sizeY"], u["quadsPerSection"], u["sectionsPerComponent"],
                                                  unreal.Vector(*u["locationCm"]), unreal.Vector(*u["scaleCm"]), JOB["terrain"])
    if not ls: raise RuntimeError("HrotTerrain import failed")
    state["landscape"] = ls
    log("ground imported")


def step_sky():
    sun = spawn(unreal.DirectionalLight, (0, 0, 100000), (-32, 215, 0), "Sun")
    lc = sun.light_component
    lc.set_editor_property("atmosphere_sun_light", True)
    lc.set_editor_property("intensity", 9.0)
    lc.set_mobility(unreal.ComponentMobility.MOVABLE)
    spawn(unreal.SkyAtmosphere, label="SkyAtmosphere")
    sky = spawn(unreal.SkyLight, (0, 0, 100000), label="SkyLight")
    sky.light_component.set_editor_property("real_time_capture", True)
    sky.light_component.set_mobility(unreal.ComponentMobility.MOVABLE)
    fog = spawn(unreal.ExponentialHeightFog, (0, 0, 20000), label="HeightFog")
    fc = fog.get_component_by_class(unreal.ExponentialHeightFogComponent)
    fc.set_editor_property("fog_density", 0.0015)   # pass 1 had 0.008: the overview washed out
    fc.set_editor_property("fog_height_falloff", 0.2)
    spawn(unreal.VolumetricCloud, label="Clouds")
    ppv = spawn(unreal.PostProcessVolume, label="PostProcess")
    ppv.set_editor_property("unbound", True)
    pps = ppv.get_editor_property("settings")   # pass 7: a little darker (the foliage and the meadow read washed out)
    pps.set_editor_property("override_auto_exposure_bias", True); pps.set_editor_property("auto_exposure_bias", -0.6)
    ppv.set_editor_property("settings", pps)
    log("sky, sun, clouds, fog")


def step_ground_material():
    task = unreal.AssetImportTask()
    task.set_editor_property("filename", JOB["cover"]); task.set_editor_property("destination_path", "/Game/Hrot")
    task.set_editor_property("destination_name", tname("T_HrotCover")); task.set_editor_property("replace_existing", True)
    task.set_editor_property("automated", True); task.set_editor_property("save", True)
    ASSETS.import_asset_tasks([task])
    tex = unreal.load_asset("/Game/Hrot/" + tname("T_HrotCover"))
    tex.set_editor_property("srgb", False)
    tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_VECTOR_DISPLACEMENTMAP)
    tex.set_editor_property("mip_gen_settings", unreal.TextureMipGenSettings.TMGS_NO_MIPMAPS)
    tex.set_editor_property("address_x", unreal.TextureAddress.TA_CLAMP); tex.set_editor_property("address_y", unreal.TextureAddress.TA_CLAMP)

    roads_tex = None
    if JOB.get("roads") and os.path.exists(JOB["roads"]):
        rt = unreal.AssetImportTask()
        rt.set_editor_property("filename", JOB["roads"]); rt.set_editor_property("destination_path", "/Game/Hrot")
        rt.set_editor_property("destination_name", tname("T_HrotRoads")); rt.set_editor_property("replace_existing", True)
        rt.set_editor_property("automated", True); rt.set_editor_property("save", True)
        ASSETS.import_asset_tasks([rt])
        roads_tex = unreal.load_asset("/Game/Hrot/" + tname("T_HrotRoads"))
        roads_tex.set_editor_property("srgb", False)
        roads_tex.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_DEFAULT)   # RGB: R asphalt, G dirt
        roads_tex.set_editor_property("mip_gen_settings", unreal.TextureMipGenSettings.TMGS_NO_MIPMAPS)
        roads_tex.set_editor_property("address_x", unreal.TextureAddress.TA_CLAMP); roads_tex.set_editor_property("address_y", unreal.TextureAddress.TA_CLAMP)
    state["roads_tex"] = roads_tex
    m = fresh_asset("/Game/Hrot", tname("M_HrotGround"), unreal.Material, unreal.MaterialFactoryNew())
    E = lambda cls, x, y: MEL.create_material_expression(m, cls, x, y)
    def c3(rgb, x, y):
        e = E(unreal.MaterialExpressionConstant3Vector, x, y); e.set_editor_property("constant", unreal.LinearColor(*rgb, 1.0)); return e
    def c1(v, x, y):
        e = E(unreal.MaterialExpressionConstant, x, y); e.set_editor_property("r", v); return e
    def c2(a, b, x, y):
        e = E(unreal.MaterialExpressionConstant2Vector, x, y); e.set_editor_property("r", a); e.set_editor_property("g", b); return e
    def link(a, b, pin, out=""):
        MEL.connect_material_expressions(a, out, b, pin)
    def op(cls, a, b, x, y, out_a="", out_b=""):
        e = E(cls, x, y); link(a, e, "A", out_a); link(b, e, "B", out_b); return e
    def lerp(a, b, alpha, x, y, out_alpha=""):
        e = E(unreal.MaterialExpressionLinearInterpolate, x, y); link(a, e, "A"); link(b, e, "B"); link(alpha, e, "Alpha", out_alpha); return e

    mask = JOB["mask"]
    wp = E(unreal.MaterialExpressionWorldPosition, -1600, 0)
    xy = E(unreal.MaterialExpressionComponentMask, -1400, 0)
    xy.set_editor_property("r", True); xy.set_editor_property("g", True); xy.set_editor_property("b", False); xy.set_editor_property("a", False)
    link(wp, xy, "")
    uv = op(unreal.MaterialExpressionDivide, op(unreal.MaterialExpressionSubtract, xy, c2(*mask["northWestCm"], -1400, 120), -1200, 0),
            c2(*mask["extentCm"], -1200, 160), -1000, 0)
    ts = E(unreal.MaterialExpressionTextureSample, -800, 0)
    ts.set_editor_property("texture", tex); ts.set_editor_property("sampler_type", unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR)
    link(uv, ts, "UVs")

    nrm = E(unreal.MaterialExpressionVertexNormalWS, -1000, 400)
    nz = E(unreal.MaterialExpressionComponentMask, -800, 400)
    for p, v in (("r", False), ("g", False), ("b", True), ("a", False)): nz.set_editor_property(p, v)
    link(nrm, nz, "")
    steep = E(unreal.MaterialExpressionSaturate, -400, 400)
    link(op(unreal.MaterialExpressionMultiply, op(unreal.MaterialExpressionSubtract, c1(0.86, -800, 500), nz, -600, 400), c1(9.0, -600, 520), -500, 400), steep, "")

    noise = E(unreal.MaterialExpressionNoise, -800, -400)
    noise.set_editor_property("scale", 0.0004); noise.set_editor_property("output_min", 0.0); noise.set_editor_property("output_max", 1.0)
    noise.set_editor_property("levels", 6)
    link(wp, noise, "Position")
    if JOB.get("assets") and JOB["assets"].get("grass") and JOB["assets"].get("forestFloor") and JOB["assets"].get("rock") and JOB["assets"].get("wildGrass"):
        rs = None
        if state.get("roads_tex"):
            rs = E(unreal.MaterialExpressionTextureSample, -800, 700)
            rs.set_editor_property("texture", state["roads_tex"]); rs.set_editor_property("sampler_type", unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR)
            link(uv, rs, "UVs")
        bc, nrm_, rough_, ao_ = textured_ground(m, E, link, op, lerp, c1, c2, c3, ts, steep, wp, noise, rs)
        MEL.connect_material_property(bc, "", unreal.MaterialProperty.MP_BASE_COLOR)
        MEL.connect_material_property(nrm_, "", unreal.MaterialProperty.MP_NORMAL)
        MEL.connect_material_property(rough_, "", unreal.MaterialProperty.MP_ROUGHNESS)
        MEL.connect_material_property(ao_, "", unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)
        MEL.recompile_material(m)
        state["landscape"].set_editor_property("landscape_material", m)
        log("ground material: Megascans grass / wild grass / forest floor / rock by our cover mask and slope")
        return
    grass = lerp(c3((0.075, 0.13, 0.035), -600, -500), c3((0.13, 0.16, 0.05), -600, -420), noise, -400, -450)   # pass 1: too yellow
    forest = c3((0.045, 0.07, 0.03), -400, -300)
    water = c3((0.02, 0.05, 0.06), -400, -200)
    rock = c3((0.30, 0.28, 0.25), -400, -100)
    col = lerp(grass, forest, ts, -200, -400, out_alpha="R")
    col = lerp(col, water, ts, 0, -350, out_alpha="G")
    col = lerp(col, rock, steep, 200, -300)
    MEL.connect_material_property(col, "", unreal.MaterialProperty.MP_BASE_COLOR)
    rough = lerp(c1(0.92, 0, 100), c1(0.06, 0, 160), ts, 200, 100, out_alpha="G")
    MEL.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    MEL.recompile_material(m)
    state["landscape"].set_editor_property("landscape_material", m)
    log("ground material: our forest / water / open mask + slope")


def textured_ground(m, E, link, op, lerp, c1, c2, c3, ts, steep, wp, noise, roads=None):
    """Pass 3: the real Megascans surfaces, placed by OUR cover mask (R forest, G water) and the slope; world-aligned, two scales of
    meadow mixed by a large noise so the tiling does not show. Returns (base colour, normal, roughness, AO) expressions."""
    A = JOB["assets"]
    xy = E(unreal.MaterialExpressionComponentMask, -2200, 800)
    for p_, v in (("r", True), ("g", True), ("b", False), ("a", False)): xy.set_editor_property(p_, v)
    link(wp, xy, "")
    def layer(key, tile_cm, x, y):
        uv = op(unreal.MaterialExpressionDivide, xy, c1(tile_cm, x - 200, y + 60), x - 100, y)
        out = {}
        for i, ch in enumerate(("B", "N", "ORM")):
            t = E(unreal.MaterialExpressionTextureSample, x, y + i * 140)
            t.set_editor_property("texture", unreal.load_asset(A[key][ch]))
            t.set_editor_property("sampler_type", unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL if ch == "N" else
                                  (unreal.MaterialSamplerType.SAMPLERTYPE_COLOR if ch == "B" else unreal.MaterialSamplerType.SAMPLERTYPE_MASKS))   # pass 3: ORM must be Masks
            t.set_editor_property("sampler_source", unreal.SamplerSourceMode.SSM_WRAP_WORLD_GROUP_SETTINGS)   # shared samplers: 16+ textures
            link(uv, t, "UVs"); out[ch] = t
        return out
    g1 = layer("grass", 300.0, -1800, -1400); g2 = layer("wildGrass", 400.0, -1800, -900)
    ff = layer("forestFloor", 350.0, -1800, -400); rk = layer("rock", 800.0, -1800, 100)
    soil = layer("soil", 350.0, -1800, 600) if A.get("soil") else None
    road = layer("gravel", 250.0, -1800, 1100) if (A.get("gravel") and roads is not None) else None
    dirt = layer("dirt", 300.0, -1800, 1600) if (A.get("dirt") and roads is not None) else None
    # soil patches: a second, larger noise thresholded — bare earth breaking up the meadows (never in forest)
    n2 = E(unreal.MaterialExpressionNoise, -1400, 600)
    n2.set_editor_property("scale", 0.00012); n2.set_editor_property("output_min", 0.0); n2.set_editor_property("output_max", 1.0); n2.set_editor_property("levels", 4)
    link(wp, n2, "Position")
    soil_a = E(unreal.MaterialExpressionSaturate, -1100, 600)
    # pass 7: soil only on the highest noise (fewer, smaller patches) and at most 70 % (the meadow shows through)
    link(op(unreal.MaterialExpressionMultiply, op(unreal.MaterialExpressionSubtract, n2, c1(0.76, -1300, 700), -1250, 600), c1(3.0, -1250, 720), -1150, 600), soil_a, "")
    soil_a = op(unreal.MaterialExpressionMultiply, soil_a, c1(0.7, -1100, 700), -1050, 600)
    meadow_tint = c3((0.82, 1.0, 0.72), -1300, -1250)   # the Megascans grass reads dry from the air: pulled toward green
    def mix(a_, b_, alpha, ch, x, y, out_alpha=""):
        return lerp(a_[ch] if isinstance(a_, dict) else a_, b_[ch] if isinstance(b_, dict) else b_, alpha, x, y, out_alpha)
    res = {}
    for k, ch in enumerate(("B", "N", "ORM")):
        y = -1200 + k * 500
        v = mix(g1, g2, noise, ch, -1200, y)
        if ch == "B": v = op(unreal.MaterialExpressionMultiply, v, meadow_tint, -1150, y + 60)
        if soil is not None: v = mix(v, soil, soil_a, ch, -1100, y)
        v = mix(v, ff, ts, ch, -1000, y, out_alpha="R")
        v = mix(v, rk, steep, ch, -800, y)
        if dirt is not None:   # tracks, paths: dirt (G)
            v = mix(v, dirt, roads, ch, -700, y, out_alpha="G")
        if road is not None:   # streets: dark, fine gravel = asphalt-like (R)
            rv = road[ch]
            if ch == "B": rv = op(unreal.MaterialExpressionMultiply, rv, c1(0.28, -700, y + 80), -680, y + 40)
            v = mix(v, rv, roads, ch, -650, y, out_alpha="R")
        res[ch] = v
    water = c3((0.02, 0.05, 0.06), -800, 900)
    res["B"] = lerp(res["B"], water, ts, -600, -1200, out_alpha="G")
    rough = E(unreal.MaterialExpressionComponentMask, -600, -200)
    for p_, v in (("r", False), ("g", True), ("b", False), ("a", False)): rough.set_editor_property(p_, v)
    link(res["ORM"], rough, "")
    ao = E(unreal.MaterialExpressionComponentMask, -600, -100)
    for p_, v in (("r", True), ("g", False), ("b", False), ("a", False)): ao.set_editor_property(p_, v)
    link(res["ORM"], ao, "")
    rough_w = lerp(rough, c1(0.05, -600, 0), ts, -400, -200, out_alpha="G")
    return res["B"], res["N"], rough_w, ao


def tree_material():
    path = JOB.get("treeMaterial")
    if path: return unreal.load_asset(path)
    m = fresh_asset("/Game/Hrot", "M_HrotTreePlaceholder", unreal.Material, unreal.MaterialFactoryNew())
    c = MEL.create_material_expression(m, unreal.MaterialExpressionConstant3Vector, -300, 0)
    c.set_editor_property("constant", unreal.LinearColor(0.035, 0.075, 0.025, 1.0))
    MEL.connect_material_property(c, "", unreal.MaterialProperty.MP_BASE_COLOR)
    r = MEL.create_material_expression(m, unreal.MaterialExpressionConstant, -300, 120); r.set_editor_property("r", 0.85)
    MEL.connect_material_property(r, "", unreal.MaterialProperty.MP_ROUGHNESS)
    MEL.recompile_material(m)
    return m


def step_trees():
    trees = (JOB.get("assets") or {}).get("trees") or []
    if trees and not JOB.get("treeMesh"):
        # the scatter split round-robin into one file per variant (each its own instanced mesh)
        data = open(JOB["trees"], "rb").read(); rec = 24; n = len(data) // rec; k = len(trees)
        total = 0
        for i, path in enumerate(trees):
            part = b"".join(data[j * rec:(j + 1) * rec] for j in range(i, n, k))
            fn = JOB["trees"][:-4] + f".v{i}.bin"
            open(fn, "wb").write(part)
            mesh = unreal.load_asset(path)
            a = unreal.HrotTerrainLibrary.scatter_instances(fn, mesh, None, f"Trees_{i}", 800000.0, unreal.Vector(1, 1, 1), 0.0)
            if not a: raise RuntimeError("ScatterInstances failed: " + path)
            total += len(part) // rec
        log(f"trees: {total} European beech in {k} variants")
        return
    mesh = unreal.load_asset(JOB.get("treeMesh") or "/Engine/BasicShapes/Cone")
    placeholder = not JOB.get("treeMesh")
    scale = unreal.Vector(5.5, 5.5, 13.0) if placeholder else unreal.Vector(1, 1, 1)
    a = unreal.HrotTerrainLibrary.scatter_instances(JOB["trees"], mesh, tree_material(), "Trees", 800000.0, scale, 50.0 if placeholder else 0.0)
    if not a: raise RuntimeError("ScatterInstances failed")
    log("trees scattered" + (" (placeholder cones)" if placeholder else ""))


def flat_material(name, rgb, rough, two_sided=True):
    m = fresh_asset("/Game/Hrot", name, unreal.Material, unreal.MaterialFactoryNew())
    c = MEL.create_material_expression(m, unreal.MaterialExpressionConstant3Vector, -300, 0)
    c.set_editor_property("constant", unreal.LinearColor(*rgb, 1.0))
    MEL.connect_material_property(c, "", unreal.MaterialProperty.MP_BASE_COLOR)
    r = MEL.create_material_expression(m, unreal.MaterialExpressionConstant, -300, 120); r.set_editor_property("r", rough)
    MEL.connect_material_property(r, "", unreal.MaterialProperty.MP_ROUGHNESS)
    m.set_editor_property("two_sided", two_sided)
    MEL.recompile_material(m)
    return m


def step_buildings():
    """UL-11: OUR building shapes (the exporter's mesh tiles) — walls and roofs as placeholders until facade / roof materials arrive."""
    spec = JOB.get("buildingMeshes")
    if not spec: return
    A = JOB.get("assets") or {}
    def two_sided_instance(name, parent_path):
        mi = fresh_asset("/Game/Hrot", name, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
        mi.set_editor_property("parent", unreal.load_asset(parent_path))
        o = mi.get_editor_property("base_property_overrides")
        o.set_editor_property("override_two_sided", True); o.set_editor_property("two_sided", True)
        mi.set_editor_property("base_property_overrides", o)
        return mi
    def surface_or(key, fallback_rgb, name):
        return two_sided_instance("MI_Hrot_" + name, A[key]["MI"]) if A.get(key) else flat_material("M_Hrot_" + name, fallback_rgb, 0.85)
    # the slots of terrain_look.py: plaster, old plaster, brick, red roof, dark roof, glass, frame, door
    mats = [surface_or("plaster", (0.55, 0.51, 0.45), "Plaster"), surface_or("oldPlaster", (0.50, 0.47, 0.42), "OldPlaster"),
            surface_or("brick", (0.36, 0.16, 0.11), "Brick"), surface_or("roofTiles", (0.30, 0.10, 0.06), "RoofRed"),
            surface_or("roofDark", (0.16, 0.12, 0.10), "RoofDark"), flat_material("M_HrotGlass", (0.05, 0.065, 0.075), 0.02),
            flat_material("M_HrotFrame", (0.75, 0.74, 0.70), 0.6), surface_or("wood", (0.25, 0.15, 0.08), "Door")]
    n = 0
    for t in spec["tiles"]:
        a = unreal.HrotTerrainLibrary.build_mesh(os.path.join(JOB["exportDir"], t["file"]).replace("\\", "/"),
                                                 f"/Game/Hrot/Buildings/SM_{JOB['terrain'].replace('-', '_')}_{t['tile'][0]}_{t['tile'][1]}",
                                                 mats, unreal.Vector(*t["locationCm"]), f"Buildings_{t['tile'][0]}_{t['tile'][1]}")
        if a: n += t["buildings"]
    log(f"buildings: {n} shapes in {len(spec['tiles'])} tiles")


def scatter_split(bin_path, meshes, label, cull_cm):
    data = open(bin_path, "rb").read(); rec = 24; n = len(data) // rec; k = len(meshes)
    total = 0
    for i, path in enumerate(meshes):
        part = b"".join(data[j * rec:(j + 1) * rec] for j in range(i, n, k))
        if not part: continue
        fn = bin_path[:-4] + f".v{i}.bin"; open(fn, "wb").write(part)
        a = unreal.HrotTerrainLibrary.scatter_instances(fn, unreal.load_asset(path), None, f"{label}_{i}", cull_cm, unreal.Vector(1, 1, 1), 0.0)
        if not a: raise RuntimeError("ScatterInstances failed: " + path)
        total += len(part) // rec
    return total


def road_material():
    """The road surface: our roads mask decides it per pixel — dirt where the mask's G (tracks, paths), dark fine gravel (asphalt-like)
    elsewhere — so the ONE mesh of lane ribbons (Sim.Scene builds tracks and streets alike) reads right everywhere."""
    A = JOB.get("assets") or {}
    m = fresh_asset("/Game/Hrot", tname("M_HrotRoad"), unreal.Material, unreal.MaterialFactoryNew())
    m.set_editor_property("two_sided", True)
    E = lambda cls, x, y: MEL.create_material_expression(m, cls, x, y)
    def link(a, b, pin, out=""): MEL.connect_material_expressions(a, out, b, pin)
    def c1(v, x, y):
        e = E(unreal.MaterialExpressionConstant, x, y); e.set_editor_property("r", v); return e
    def c2(a, b, x, y):
        e = E(unreal.MaterialExpressionConstant2Vector, x, y); e.set_editor_property("r", a); e.set_editor_property("g", b); return e
    def op(cls, a, b, x, y, oa="", ob=""):
        e = E(cls, x, y); link(a, e, "A", oa); link(b, e, "B", ob); return e
    wp = E(unreal.MaterialExpressionWorldPosition, -1600, 0)
    xy = E(unreal.MaterialExpressionComponentMask, -1400, 0)
    for p_, v in (("r", True), ("g", True), ("b", False), ("a", False)): xy.set_editor_property(p_, v)
    link(wp, xy, "")
    mask = JOB["mask"]
    uvm = op(unreal.MaterialExpressionDivide, op(unreal.MaterialExpressionSubtract, xy, c2(*mask["northWestCm"], -1400, 120), -1200, 0), c2(*mask["extentCm"], -1200, 160), -1000, 0)
    rs = E(unreal.MaterialExpressionTextureSample, -800, 0)
    rs.set_editor_property("texture", state["roads_tex"]); rs.set_editor_property("sampler_type", unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR)
    link(uvm, rs, "UVs")
    alpha = E(unreal.MaterialExpressionSaturate, -600, 0)
    link(op(unreal.MaterialExpressionSubtract, rs, rs, -700, 0, "G", "R"), alpha, "")
    def tex(key, ch, tile_cm, x, y):
        uv = op(unreal.MaterialExpressionDivide, xy, c1(tile_cm, x - 200, y + 60), x - 100, y)
        t = E(unreal.MaterialExpressionTextureSample, x, y)
        t.set_editor_property("texture", unreal.load_asset(A[key][ch]))
        t.set_editor_property("sampler_type", unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL if ch == "N" else
                              (unreal.MaterialSamplerType.SAMPLERTYPE_COLOR if ch == "B" else unreal.MaterialSamplerType.SAMPLERTYPE_MASKS))
        link(uv, t, "UVs"); return t
    out = {}
    for k, ch in enumerate(("B", "N", "ORM")):
        g = tex("gravel", ch, 200.0, -400, -600 + k * 400); d = tex("dirt", ch, 300.0, -400, -400 + k * 400)
        if ch == "B": g = op(unreal.MaterialExpressionMultiply, g, c1(0.26, -300, -500), -250, -600)
        e = E(unreal.MaterialExpressionLinearInterpolate, -100, -600 + k * 400)
        link(g, e, "A"); link(d, e, "B"); link(alpha, e, "Alpha")
        out[ch] = e
    MEL.connect_material_property(out["B"], "", unreal.MaterialProperty.MP_BASE_COLOR)
    MEL.connect_material_property(out["N"], "", unreal.MaterialProperty.MP_NORMAL)
    rough = E(unreal.MaterialExpressionComponentMask, 100, 200)
    for p_, v in (("r", False), ("g", True), ("b", False), ("a", False)): rough.set_editor_property(p_, v)
    link(out["ORM"], rough, "")
    MEL.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    MEL.recompile_material(m)
    return m


def step_roads():
    """UL-14: HROT's OWN road scene (hrot-roadscene → TrafficRoadScene, SumoSharp's Sim.Scene): lane ribbons, junction fills, sidewalks,
    lane markings, crosswalks — the geometry HROT's 3-D map draws, laid on our ground."""
    spec = JOB.get("roadMeshes")
    A = JOB.get("assets") or {}
    if not spec or not state.get("roads_tex") or not (A.get("gravel") and A.get("dirt")): return
    road = road_material()
    paint = flat_material("M_HrotRoadPaint", (0.82, 0.82, 0.80), 0.55)
    walk = two_sided_instance_of(A["gravel"]["MI"], "MI_HrotSidewalk") if A.get("gravel") else road
    by_kind = {"Asphalt": road, "Junction": road, "Sidewalk": walk, "LaneMarking": paint, "Crosswalk": paint}
    n = 0
    for t in spec["tiles"]:
        mat = by_kind.get(t["kind"], road)
        a = unreal.HrotTerrainLibrary.build_mesh(os.path.join(JOB["roadMeshDir"], t["file"]).replace("\\", "/"),
                                                 f"/Game/Hrot/Roads/SM_{JOB['terrain'].replace('-', '_')}_{t['kind']}_{t['tile'][0]}_{t['tile'][1]}",
                                                 [mat], unreal.Vector(*t["locationCm"]), f"Road_{t['kind']}_{t['tile'][0]}_{t['tile'][1]}")
        if a: n += t["triangles"]
    log(f"roads: HROT's road scene, {len(spec['tiles'])} tiles, {n} triangles ({', '.join(f'{k} {v}' for k, v in spec['triangles'].items())})")


def two_sided_instance_of(parent_path, name):
    mi = fresh_asset("/Game/Hrot", name, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
    mi.set_editor_property("parent", unreal.load_asset(parent_path))
    o = mi.get_editor_property("base_property_overrides")
    o.set_editor_property("override_two_sided", True); o.set_editor_property("two_sided", True)
    mi.set_editor_property("base_property_overrides", o)
    return mi


def step_grass():
    A = JOB.get("assets") or {}
    msg = []
    if A.get("grassMeshes") and JOB.get("grass") and os.path.exists(JOB["grass"]):
        msg.append(f"{scatter_split(JOB['grass'], A['grassMeshes'], 'Grass', 9000.0)} grass clumps ({len(A['grassMeshes'])} meshes)")
    if A.get("fernMeshes") and JOB.get("ferns") and os.path.exists(JOB["ferns"]):
        msg.append(f"{scatter_split(JOB['ferns'], A['fernMeshes'], 'Ferns', 12000.0)} ferns")
    log("ground cover: " + (", ".join(msg) or "none (no plant assets)"))


def step_shots():
    state["queue"] = list(JOB["views"])
    state["first"] = True


def shoot_next():
    """One view per call: place a camera, let Lumen and streaming settle, take the shot, wait for the file."""
    if state.get("pending"):
        path, t = state["pending"]
        if os.path.exists(path) and os.path.getsize(path) > 0:
            R["shots"].append(path); log(f"shot {os.path.basename(path)}"); state["pending"] = None
        elif time.time() - t > 120:
            R["shots"].append(None); log(f"shot timed out: {path}"); state["pending"] = None
        return True
    if not state.get("queue"): return False
    if time.time() < state["wait_until"]: return True
    if not state.get("camera_for"):
        v = state["queue"][0]
        cam = spawn(unreal.CameraActor, tuple(v["unreal"]["locationCm"]), (v["unreal"]["pitch"], v["unreal"]["yaw"], 0), "View_" + v["name"])
        cam.camera_component.set_editor_property("field_of_view", 60.0)
        ues = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
        ues.set_level_viewport_camera_info(cam.get_actor_location(), cam.get_actor_rotation())
        state["camera_for"] = (v, cam)
        state["wait_until"] = time.time() + (JOB.get("settleSeconds", 20) * (2 if state.get("first") else 1))
        state["first"] = False
        return True
    v, cam = state.pop("camera_for")
    state["queue"].pop(0)
    path = os.path.join(JOB["shots"], f"{JOB['terrain']}-{v['name']}.png").replace("\\", "/")
    if os.path.exists(path): os.remove(path)
    unreal.AutomationLibrary.take_high_res_screenshot(JOB.get("width", 1920), JOB.get("height", 1080), path, cam)
    state["pending"] = (path, time.time())
    return True


def step_save():
    """The map AND every asset this run made (the ground material, the masks, the building meshes, the material instances) — pass 6
    saved only the map, so the level opened later in the editor fell back to older copies (the user saw no roads, no grass)."""
    unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).save_current_level()
    ok = unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    log(f"saved the map and every new asset ({'ok' if ok else 'some packages FAILED to save'})")


STEPS = [step_level, step_ground, step_sky, step_ground_material, step_trees, step_buildings, step_roads, step_grass, step_shots, None, step_save]


def tick(dt):
    if state["busy"]: return
    try:
        state["busy"] = True
        if state["step"] < len(STEPS) and STEPS[state["step"]] is None:   # the shots run over many ticks
            if shoot_next(): return
            state["step"] += 1; return
        if state["step"] >= len(STEPS):
            finish(True); return
        s = STEPS[state["step"]]; state["step"] += 1
        s()
    except Exception:
        state["step"] = len(STEPS) + 1
        finish(False, traceback.format_exc())
    finally:
        state["busy"] = False


unreal.EditorPythonScripting.set_keep_python_script_alive(True)   # F-1
state["handle"] = unreal.register_slate_post_tick_callback(tick)
