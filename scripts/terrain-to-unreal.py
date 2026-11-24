#!/usr/bin/env python3
"""terrain-to-unreal.py — one HROT terrain package → a GeoTIFF heightmap + a GeoJSON of its features, for Unreal's Landscape Combinator.

CE-4651 (docs/designs/IG/DRAFT_Unreal_Image_Generator.md, option A; HANDOFF_Coordinator_To_Windows_Unreal_Terrain.md §2).

    python scripts/terrain-to-unreal.py assets/terrain/<name> <out-folder> [--crs auto|local]

Needs:  pip install rasterio pyproj numpy

Writes into <out-folder>:
    <name>.height.tif       float32 GeoTIFF, one pixel per HROT height POST — the post at the pixel's NORTH-WEST CORNER (UL-4: where
                            Landscape Combinator puts its vertex), north row first
    <name>.vectors.geojson  buildings (Polygon: height, building_levels, base_z, template), walls (LineString: material, height,
                            thickness), surfaces (Polygon: class open / forest / water), prisms (Polygon: clutter, height),
                            lone trees (Point), roads (LineString, sampled from roads.json's Hermite segments: lanes, lane_width)
    <name>.<kind>s.geojson  the same features, one file per kind (buildings, roofs, walls, surfaces, prisms, trees, roads), in WGS84
                            lon / lat (UL-5: Landscape Combinator's spline importer reads every vector file as EPSG:4326)
    <name>.height.r16       the same heights as uint16 for tools/unreal/HrotTerrain (UL-9), north row first; frame.json "unreal" holds
                            its exact placement (location, scale, components)
    <name>.frame.json       the frame used: CRS, the local → projected offset, the grid size — what the import / the checks read; "thermal" = the
                            fixed thermal classes and bare ground's (CE-4662 TD-9: every vector feature carries "thermal_class" — the image
                            generator's ground material map, a DRIVER, never a temperature)

The frame (local x east, y north, metres):
    * a terrain with frame.utm (the importer's): EPSG:326zz / 327zz, E = x + e0 + corner[0], N = y + n0 + corner[1] — exact (UTM §12.3).
    * a terrain with only origin (lat, lon): a transverse Mercator centred on the origin (+proj=tmerc +lat_0 +lon_0 +k=1), E = x, N = y.
      ⚠ HROT's own frame there is a TANGENT PLANE (WGS84Transform), not this TM — both agree within millimetres over a few km, and the
      landscape is placed by these numbers, not by lat / lon.
    * --crs local: no projection at all (the coordinates are HROT's local metres) — for Landscape Combinator's "skip coordinate conversion".
Building footprints come from assets/buildings/<template>.building.json, placed as HROT does (TerrainBuildingExpander: the template's
origin at the point, turned counter-clockwise by `rotation`°); height = the sum of the storeys.
"""
import argparse, json, math, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # terrain_look

import numpy as np

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))


# ── heights ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
def read_asc(path):
    """ESRI ASCII grid → (z[rows][cols] with row 0 = SOUTH, x0, y0 = the south-west POST, cell)."""
    with open(path, encoding="utf-8") as f:
        tokens = f.read().split()
    header, i = {}, 0
    while i < len(tokens) and not _is_number(tokens[i]):
        header[tokens[i].lower()] = float(tokens[i + 1]); i += 2
    cols, rows, cell = int(header["ncols"]), int(header["nrows"]), header["cellsize"]
    x0 = header["xllcenter"] if "xllcenter" in header else header["xllcorner"] + cell / 2
    y0 = header["yllcenter"] if "yllcenter" in header else header["yllcorner"] + cell / 2
    z = np.array(tokens[i:i + cols * rows], dtype=np.float64).reshape(rows, cols)[::-1]   # the file's first row is the NORTH edge
    nd = header.get("nodata_value")
    if nd is not None and (z == nd).any():
        raise SystemExit(f"{path}: NODATA posts are not supported by the export")
    return z.astype(np.float32), x0, y0, cell


def _is_number(s):
    try: float(s); return True
    except ValueError: return False


def read_heights(path):
    """HROT's tiled height store (TerrainHeightTiles, "HRTH" v1) → (z row 0 = SOUTH, x0, y0, cell)."""
    with open(path, "rb") as f:
        data = f.read()
    magic, version, cols, rows = struct.unpack_from("<IiII", data, 0)
    if magic != 0x48545248 or version != 1:
        raise SystemExit(f"{path}: not an HRTH v1 height store")
    x0, y0, cell = struct.unpack_from("<ddd", data, 16)
    tile, tx, ty = struct.unpack_from("<III", data, 40)
    tiles = np.frombuffer(data, dtype="<f4", offset=52, count=tx * ty * tile * tile).reshape(ty, tx, tile, tile)
    z = tiles.transpose(0, 2, 1, 3).reshape(ty * tile, tx * tile)[:rows, :cols]   # tiles south row first, each row 0 = south
    return np.ascontiguousarray(z, dtype=np.float32), x0, y0, cell


# ── the frame ────────────────────────────────────────────────────────────────────────────────────────────────────────────────
def frame_of(terrain, mode):
    from pyproj import CRS
    fr = (terrain.get("frame") or {}).get("utm")
    if mode == "local":
        return None, (0.0, 0.0), "local metres (no CRS)"
    if fr:
        corner = (terrain.get("frame") or {}).get("corner") or [0, 0]
        south = str(fr.get("hemisphere", "N")).upper().startswith("S")
        epsg = (32700 if south else 32600) + int(fr["zone"])
        return CRS.from_epsg(epsg), (fr["e0"] + corner[0], fr["n0"] + corner[1]), f"EPSG:{epsg} (UTM frame)"
    o = terrain["origin"]
    crs = CRS.from_proj4(f"+proj=tmerc +lat_0={o['lat']!r} +lon_0={o['lon']!r} +k=1 +x_0=0 +y_0=0 +datum=WGS84 +units=m +no_defs")
    return crs, (0.0, 0.0), "transverse Mercator at the origin"


# ── features ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
_templates = {}
TEMPLATE_DIRS = []   # the terrain's own buildings/ first (an imported terrain generates one template per house), then the shared ones


def template(name):
    if name not in _templates:
        for d in TEMPLATE_DIRS:
            path = os.path.join(d, f"{name}.building.json")
            if os.path.exists(path):
                with open(path, encoding="utf-8") as f:
                    _templates[name] = json.load(f)
                break
        else:
            raise SystemExit(f"building template '{name}' not found in {TEMPLATE_DIRS}")
    return _templates[name]


def place(points, at, deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [(at[0] + x * c - y * s, at[1] + x * s + y * c) for x, y in points]


def ring(points):
    pts = [tuple(p[:2]) for p in points]
    return pts if pts[0] == pts[-1] else pts + [pts[0]]


def hermite(seg, step=2.0):
    cp = seg["controlPoints"]
    p0, t0, p1, t1 = (np.array([cp[k]["x"], cp[k]["y"]], dtype=float) for k in ("p0", "t0", "p1", "t1"))
    n = max(2, int(math.ceil(np.linalg.norm(p1 - p0) / step)) + 1)
    out = []
    for t in np.linspace(0.0, 1.0, n):
        h00, h10, h01, h11 = 2*t**3 - 3*t**2 + 1, t**3 - 2*t**2 + t, -2*t**3 + 3*t**2, t**3 - t**2
        q = h00 * p0 + h10 * t0 + h01 * p1 + h11 * t1
        out.append((float(q[0]), float(q[1])))
    return out


# ── thermal classes (CE-4662, docs/designs/thermal/DESIGN_Thermal_Drivers.md TD-9) ─────────────────────────────────────────────────
SHARED_MATERIALS = os.path.join(REPO, "FDP", "Toolkits", "Fdp.Toolkits", "Terrain", "Data", "materials.json")
DEFAULT_MATERIAL = "concrete"   # TerrainMaterialLibrary.DefaultMaterial: a panel / house that names no material
THERMAL_CLASSES = ["asphalt", "concrete", "brick", "metal", "glass", "wood", "soil-dry", "soil-wet", "sand", "grass", "dense-vegetation", "water", "snow"]   # TerrainThermalClass.All


def thermal_library(folder):
    """The material library a node reads (the shared materials.json, overlaid by the terrain folder's own materials.json) as
    (material name -> thermalClass, surface name -> thermalClass) — the SAME file and keys TerrainMaterialLibrary parses."""
    by_mat, by_surf = {}, {}
    for path in (SHARED_MATERIALS, os.path.join(folder, "materials.json")):
        if not os.path.exists(path): continue
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        for k, v in (d.get("materials") or {}).items():
            if isinstance(v, dict) and v.get("thermalClass"): by_mat[k] = v["thermalClass"]
        for k, v in (d.get("surfaceThermalClasses") or {}).items():
            if not k.startswith("$"): by_surf[k] = v
    return by_mat, by_surf


def features(folder, terrain, world):
    feats, counts, roof_of = [], {}, {}
    by_mat, by_surf = thermal_library(folder)

    def thermal(material):   # ⭐ CE-4662 — the class of a material (an unnamed one is the default material's)
        return by_mat.get(material or DEFAULT_MATERIAL)

    def add(geom_type, coords, props):
        feats.append({"type": "Feature", "properties": props, "geometry": {"type": geom_type, "coordinates": coords}})
        counts[props["hrot_kind"]] = counts.get(props["hrot_kind"], 0) + 1

    for f in world["features"]:
        p, g = f["properties"], f["geometry"]
        kind = p.get("kind")
        label = str(p.get("label", ""))
        if kind == "building" and g["type"] == "Polygon" and label.endswith("-roof"):
            # an imported terrain models a house's ROOF as its own prism on top of the walls ("<id>-roof", baseZ = the eaves): in Unreal it
            # is that house's roof, not another building from the ground ⇒ its own file, and the house gets its roof height
            add("Polygon", [ring(g["coordinates"][0])], {"hrot_kind": "roof", "parent": label[:-5], "height": p.get("height"),
                                                          "base_z": p.get("baseZ"), "name": label, "thermal_class": thermal(p.get("material"))})
            roof_of[label[:-5]] = p.get("height")
            continue
        if kind == "building":
            if g["type"] == "Point":   # a template placement
                t = template(p["template"])
                height = float(t["height"]) if t.get("solid") else sum(float(s["height"]) for s in t["storeys"])
                levels = 1 if t.get("solid") else len(t["storeys"])
                fp = ring(place(t["footprint"], g["coordinates"], float(p.get("rotation", 0))))
                extra = {"template": p["template"], "base_z": p.get("baseZ"), "origin": list(g["coordinates"][:2]),
                         "rotation": float(p.get("rotation", 0))}   # terrain_look builds the house from its template (UL-13)
            else:                       # an inline prism building
                height = float(p.get("height", 3.0))
                levels = int(p.get("floors") or max(1, round(height / 3.0)))
                fp = ring(g["coordinates"][0]); extra = {}
            add("Polygon", [fp], {"hrot_kind": "building", "building": "yes", "height": round(height, 3),
                                  "building_levels": levels, "name": p.get("label", ""), **extra, "thermal_class": thermal(p.get("material"))})
        elif kind in ("wall", "fence"):
            add("LineString", [tuple(c[:2]) for c in g["coordinates"]],
                {"hrot_kind": "wall", "barrier": "hedge" if p.get("material") == "hedge" else ("fence" if kind == "fence" else "wall"), "material": p.get("material", kind),
                 "height": p.get("height"), "thickness": p.get("thickness"), "name": p.get("label", ""),
                 "thermal_class": thermal(p.get("material", "fence-wood" if kind == "fence" else None))})
        elif kind == "surface":
            cls = p.get("surface", "open")
            osm = {"forest": {"landuse": "forest"}, "water": {"natural": "water"}}.get(cls, {"landuse": "grass"})
            add("Polygon", [ring(r) for r in g["coordinates"]], {"hrot_kind": "surface", "class": cls, **osm, "name": p.get("label", ""),
                                                               "thermal_class": by_surf.get(cls)})
        elif kind == "prism":
            add("Polygon", [ring(r) for r in g["coordinates"]], {"hrot_kind": "prism", "clutter": p.get("clutter", ""),
                                                                   "material": p.get("material", ""), "height": p.get("height"),
                                                                   "thermal_class": thermal(p.get("material"))})
        elif kind == "tree" or (g["type"] == "Point" and "tree" in str(kind)):
            add("Point", tuple(g["coordinates"][:2]), {"hrot_kind": "tree", "natural": "tree", "height": p.get("height"), "thermal_class": thermal("tree-trunk")})
        else:
            counts["skipped:" + str(kind)] = counts.get("skipped:" + str(kind), 0) + 1

    for f in feats:   # each house gets its roof's height (the roof prisms come in any order)
        pr = f["properties"]
        if pr["hrot_kind"] == "building" and pr.get("name") in roof_of:
            pr["roof_height"] = roof_of[pr["name"]]
    for road_file in terrain.get("roadNetworks") or []:
        with open(os.path.join(folder, road_file), encoding="utf-8") as f:
            net = json.load(f)
        for seg in net.get("segments", []):
            add("LineString", hermite(seg), {"hrot_kind": "road", "highway": "residential", "lanes": seg.get("laneCount"),
                                              "lane_width": seg.get("laneWidth"), "width": (seg.get("laneCount") or 1) * (seg.get("laneWidth") or 3.5),
                                              "name": f"segment {seg['id']}", "thermal_class": "asphalt"})
    return feats, counts


def write_look(out, name, feats, z, x0, y0, cell, rows, cols, unreal, de, dn, tree_spacing=5.0, mask_cell=2.0, seed=4651,
               folder=None, terrain=None):
    """UL-10, the LOOK inputs — all from OUR data, so the picture follows the simulation's map:
    <name>.cover.png  RGB 8-bit, one pixel per `mask_cell` m over the whole (padded) landscape, the NORTH row first:
                      R = forest, G = water, B = open (a surface of class open); the landscape material samples it by world position
    <name>.trees.bin  float32 records X, Y, Z (Unreal cm), yaw, width scale, height scale — one tree per ~tree_spacing² m² of forest,
                      jittered, Z = HROT's own ground (TerrainHeightGrid.Sample) — for HrotTerrain.ScatterInstances
    <name>.views.json camera poses picked from the data (Unreal cm / degrees)"""
    from PIL import Image, ImageDraw
    W, H = (cols - 1) * cell, (rows - 1) * cell
    mw, mh = int(round(W / mask_cell)), int(round(H / mask_cell))
    layers = {}
    for key, cls in (("R", "forest"), ("G", "water"), ("B", "open")):
        img = Image.new("L", (mw, mh), 0); dr = ImageDraw.Draw(img)
        for f in feats:
            pr = f["properties"]
            if pr["hrot_kind"] != "surface" or pr.get("class") != cls: continue
            rings = f["geometry"]["coordinates"]
            for k, ring_ in enumerate(rings):
                px = [((x - de - x0) / mask_cell, (y0 + H - (y - dn)) / mask_cell) for x, y in ring_]
                if len(px) >= 3: dr.polygon(px, fill=255 if k == 0 else 0)
        layers[key] = img
    Image.merge("RGB", (layers["R"], layers["G"], layers["B"])).save(os.path.join(out, f"{name}.cover.png"))
    forest = np.asarray(layers["R"]) > 127   # row 0 = north

    rng = np.random.default_rng(seed)
    gx = np.arange(x0 + tree_spacing / 2, x0 + W, tree_spacing); gy = np.arange(y0 + tree_spacing / 2, y0 + H, tree_spacing)
    X, Y = np.meshgrid(gx, gy)
    X = X + rng.uniform(-0.45, 0.45, X.shape) * tree_spacing; Y = Y + rng.uniform(-0.45, 0.45, Y.shape) * tree_spacing
    ci = np.clip(((X - x0) / mask_cell).astype(int), 0, mw - 1); ri = np.clip(((y0 + H - Y) / mask_cell).astype(int), 0, mh - 1)
    keep = forest[ri, ci]
    X, Y = X[keep], Y[keep]
    fx = np.clip((X - x0) / cell, 0, cols - 1.000001); fy = np.clip((Y - y0) / cell, 0, rows - 1.000001)
    c = fx.astype(int); r = fy.astype(int); u = fx - c; v = fy - r
    z00, z10, z01, z11 = z[r, c], z[r, c + 1], z[r + 1, c], z[r + 1, c + 1]
    Z = np.where(u >= v, z00 + u * (z10 - z00) + v * (z11 - z10), z00 + v * (z01 - z00) + u * (z11 - z01))
    n = len(X)
    rec = np.stack([X * 100, -Y * 100, Z * 100 - 30, rng.uniform(0, 360, n), rng.uniform(0.8, 1.25, n), rng.uniform(0.75, 1.3, n)], 1)
    rec.astype("<f4").tofile(os.path.join(out, f"{name}.trees.bin"))

    def ground(x, y):
        return float(z[int(np.clip((y - y0) / cell, 0, rows - 1)), int(np.clip((x - x0) / cell, 0, cols - 1))])
    views = []
    bc = [np.mean(np.array(f["geometry"]["coordinates"][0]), 0) - [de, dn] for f in feats if f["properties"]["hrot_kind"] == "building"]
    if bc:
        bc = np.array(bc)
        H2, xe, ye = np.histogram2d(bc[:, 0], bc[:, 1], bins=[max(1, int(W // 250)), max(1, int(H // 250))], range=[[x0, x0 + W], [y0, y0 + H]])
        i, j = np.unravel_index(np.argmax(H2), H2.shape); tx, ty = (xe[i] + xe[i + 1]) / 2, (ye[j] + ye[j + 1]) / 2
        views.append({"name": "village-aerial", "target": [tx, ty, ground(tx, ty)], "yaw": 35.0, "pitch": -22.0, "distance": 650.0})
        views.append({"name": "village-low", "target": [tx, ty, ground(tx, ty)], "yaw": 200.0, "pitch": -4.0, "distance": 350.0, "minAboveGround": 12.0})
    if forest.any():
        rr, cc = np.nonzero(forest); k = len(rr) // 2; fx_, fy_ = x0 + cc[k] * mask_cell, y0 + H - rr[k] * mask_cell
        views.append({"name": "forest-aerial", "target": [fx_, fy_, ground(fx_, fy_)], "yaw": 120.0, "pitch": -25.0, "distance": 900.0})
    views.append({"name": "overview", "target": [x0 + W / 2, y0 + H / 2, float(np.median(z))], "yaw": 30.0, "pitch": -35.0, "distance": max(W, H) * 0.9})
    bc = bc if len(bc) else None
    for vw in views:
        if "eye" in vw: continue
        tx, ty, tz = vw["target"]; d = vw["distance"]; yaw = np.radians(vw["yaw"]); pitch = np.radians(vw["pitch"])
        # HROT local: the camera BEHIND the target along its heading (yaw 0 = looking north, clockwise), raised by the pitch
        ex = tx - d * np.cos(pitch) * np.sin(yaw); ey = ty - d * np.cos(pitch) * np.cos(yaw); ez = tz - d * np.sin(pitch)
        ez = max(ez, ground(ex, ey) + vw.get("minAboveGround", 2.0))
        vw["eye"] = [ex, ey, ez]
        vw["unreal"] = {"locationCm": [ex * 100, -ey * 100, ez * 100], "pitch": vw["pitch"], "yaw": vw["yaw"] - 90.0}   # UE yaw 0 = +X (east), clockwise in its left-handed frame
    # UL-12 roads: the SUMO lanes rasterised at 1 m (the ground material paints them) - and the street view stands on one
    import terrain_look as tl
    lanes = []
    if folder and terrain and (terrain.get("sumoNet") or {}).get("file"):
        lanes = tl.sumo_lanes(os.path.join(folder, terrain["sumoNet"]["file"]), de, dn, (terrain.get("frame") or {}).get("utm"))
    roads_img = tl.roads_mask(lanes, x0, y0, W, H, 1.0)
    roads_img.save(os.path.join(out, f"{name}.roads.png"))
    roads = np.asarray(roads_img)[..., :2].max(-1) > 127
    if lanes and bc is not None:
        tx, ty = views[0]["target"][:2]
        best = None
        for pts, wdt, cls_ in lanes:
            if cls_ != "asphalt": continue   # the street view stands on a street (not a forest track, not a junction area)
            for i in range(len(pts) - 1):
                (ax, ay), (bx, by) = pts[i], pts[i + 1]
                d = math.hypot(ax - tx, ay - ty)
                fi = int(np.clip((y0 + H - ay) / mask_cell, 0, mh - 1)); fj = int(np.clip((ax - x0) / mask_cell, 0, mw - 1))
                if forest[fi, fj] or math.hypot(bx - ax, by - ay) < 5: continue
                if best is None or d < best[0]: best = (d, ax, ay, bx, by)
        if best:
            _, ax, ay, bx, by = best
            yaw = math.degrees(math.atan2(bx - ax, by - ay))   # HROT heading: 0 = north, clockwise
            views.insert(1, {"name": "street", "eye": [ax, ay, ground(ax, ay) + 1.7], "yaw": yaw, "pitch": -2.0, "target": [bx, by, ground(bx, by)]})
    # UL-13 grass / ferns around the close views (never on roads, water, under houses; ferns only in forest)
    from PIL import Image, ImageDraw
    bimg = Image.new("L", (int(round(W)), int(round(H))), 0); bdr = ImageDraw.Draw(bimg)
    for f in feats:
        if f["properties"]["hrot_kind"] in ("building", "roof"):
            rr = np.array(f["geometry"]["coordinates"][0], dtype=float) - [de, dn]
            bdr.polygon([((x - x0), (y0 + H - y)) for x, y in rr], fill=255)
    built = np.asarray(bimg) > 127
    water = np.asarray(layers["G"]) > 127
    def mask_px(X, Y):
        return np.clip(((X - x0) / mask_cell).astype(int), 0, mw - 1), np.clip(((y0 + H - Y) / mask_cell).astype(int), 0, mh - 1)
    def px1(ri, ci):   # mask (2 m) index -> 1 m index
        return np.clip(ri * int(mask_cell), 0, roads.shape[0] - 1), np.clip(ci * int(mask_cell), 0, roads.shape[1] - 1)
    def open_ok(ri, ci):
        r1, c1 = px1(ri, ci)
        return ~forest[ri, ci] & ~water[ri, ci] & ~roads[r1, c1] & ~built[r1, c1]
    def forest_ok(ri, ci):
        r1, c1 = px1(ri, ci)
        return forest[ri, ci] & ~roads[r1, c1]
    def zs(X, Y):
        fx = np.clip((X - x0) / cell, 0, cols - 1.000001); fy = np.clip((Y - y0) / cell, 0, rows - 1.000001)
        c_ = fx.astype(int); r_ = fy.astype(int); u_ = fx - c_; v_ = fy - r_
        z00, z10, z01, z11 = z[r_, c_], z[r_, c_ + 1], z[r_ + 1, c_], z[r_ + 1, c_ + 1]
        return np.where(u_ >= v_, z00 + u_ * (z10 - z00) + v_ * (z11 - z10), z00 + v_ * (z01 - z00) + u_ * (z11 - z01))
    near = [tuple((vw.get("eye") or vw["target"])[:2]) for vw in views if vw["name"] in ("street", "village-low")]
    # dense close to the cameras (0.45 m within 90 m), lighter beyond (0.9 m to 220 m) — the outer points inside 90 m are dropped
    if near:
        g_in = np.concatenate([tl.scatter_near(near, 60.0, 0.35, open_ok, zs, rng, mask_px),
                               tl.scatter_near(near, 90.0, 0.6, open_ok, zs, rng, mask_px)])
        g_out = tl.scatter_near(near, 220.0, 0.9, open_ok, zs, rng, mask_px)
        P = g_out[:, :2] / np.array([100.0, -100.0])
        far = np.ones(len(P), bool)
        for cx, cy in near: far &= (P[:, 0] - cx) ** 2 + (P[:, 1] - cy) ** 2 > 90.0 ** 2
        grass = np.concatenate([g_in, g_out[far]])
    else:
        grass = np.zeros((0, 6), "<f4")
    ferns = tl.scatter_near(near, 220.0, 2.5, forest_ok, zs, rng, mask_px) if near else np.zeros((0, 6), "<f4")
    grass.tofile(os.path.join(out, f"{name}.grass.bin")); ferns.tofile(os.path.join(out, f"{name}.ferns.bin"))
    for vw in views:
        if "eye" in vw and "unreal" not in vw:
            ex, ey, ez = vw["eye"]; vw["unreal"] = {"locationCm": [ex * 100, -ey * 100, ez * 100], "pitch": vw["pitch"], "yaw": vw["yaw"] - 90.0}
    with open(os.path.join(out, f"{name}.views.json"), "w", encoding="utf-8") as f:
        json.dump({"views": views, "mask": {"cell": mask_cell, "size": [mw, mh], "northWestCm": [x0 * 100, -(y0 + H) * 100], "extentCm": [W * 100, H * 100]},
                   "roads": {"lanes": sum(1 for l in lanes if not l[2].endswith("-area")), "junctions": sum(1 for l in lanes if l[2].endswith("-area")),
                             "cell": 1.0, "extentCm": [W * 100, H * 100]}, "grass": int(len(grass)), "ferns": int(len(ferns)),
                   "trees": int(n), "treeSpacing": tree_spacing}, f, indent=1)
    return {"trees": int(n), "views": [v["name"] for v in views], "mask": [mw, mh], "lanes": sum(1 for l in lanes if not l[2].endswith("-area")),
            "junctions": sum(1 for l in lanes if l[2].endswith("-area")), "grass": int(len(grass)), "ferns": int(len(ferns))}


def write_buildings(out, name, feats, z, x0, y0, cell, rows, cols, tile_m=500.0):
    """UL-11, OUR building shapes as meshes for HrotTerrain.BuildMesh — exact by construction: each footprint extruded from its base to
    its height (slot 0 = wall, slot 1 = roof cap), each imported roof volume ("<id>-roof", base = the eaves) extruded as a roof (slot 1).
    One file per tile_m × tile_m tile (culling), vertices relative to the tile's south-west corner at the tile's lowest base, Unreal cm.
    The base: a template's baseZ when it has one, else the LOWEST ground under the footprint (HROT's TerrainHeightGrid.LowestUnder rule)."""
    import mapbox_earcut as earcut
    def lowest(ring_):
        return min(float(z[int(np.clip(round((y - y0) / cell), 0, rows - 1)), int(np.clip(round((x - x0) / cell), 0, cols - 1))]) for x, y in ring_)
    tiles = {}
    for f in feats:
        pr = f["properties"]
        if pr["hrot_kind"] not in ("building", "roof"): continue
        ring_ = np.array(f["geometry"]["coordinates"][0], dtype=float)[:, :2]
        if len(ring_) > 1 and np.allclose(ring_[0], ring_[-1]): ring_ = ring_[:-1]
        if len(ring_) < 3: continue
        area2 = np.sum(ring_[:, 0] * np.roll(ring_[:, 1], -1) - np.roll(ring_[:, 0], -1) * ring_[:, 1])
        if area2 < 0: ring_ = ring_[::-1]   # counter-clockwise in HROT (x east, y north)
        base = pr.get("base_z")
        base = float(base) if base is not None else lowest(ring_)
        top = base + float(pr.get("height") or 3.0)
        c = ring_.mean(0); key = (int((c[0] - x0) // tile_m), int((c[1] - y0) // tile_m))
        tiles.setdefault(key, []).append((ring_, base, top, pr["hrot_kind"]))
    written = []
    for (tx, ty), items in tiles.items():
        ox, oy = x0 + tx * tile_m, y0 + ty * tile_m
        oz = min(b for _, b, _, _ in items)
        V, T, M = [], [], []
        def vtx(x, y, h):
            V.append(((x - ox) * 100.0, -(y - oy) * 100.0, (h - oz) * 100.0)); return len(V) - 1
        for ring_, base, top, kind in items:
            n = len(ring_)
            wall_slot = 0 if kind == "building" else 1
            for i in range(n):   # walls: a quad per edge, outward-facing
                ax, ay = ring_[i]; bx, by = ring_[(i + 1) % n]
                a0, b0, b1, a1 = vtx(ax, ay, base), vtx(bx, by, base), vtx(bx, by, top), vtx(ax, ay, top)
                T += [(a0, b0, b1), (a0, b1, a1)]; M += [wall_slot, wall_slot]
            tri = earcut.triangulate_float64(ring_, np.array([n], dtype=np.uint32)).reshape(-1, 3)
            first = len(V)
            for x, y in ring_: vtx(x, y, top)
            for a_, b_, c_ in tri:   # the top cap (earcut is counter-clockwise like the ring)
                T.append((first + a_, first + b_, first + c_)); M.append(1)
        path = os.path.join(out, f"{name}.bld_{tx}_{ty}.bin")
        with open(path, "wb") as f:
            f.write(struct.pack("<ii", len(V), len(T)))
            f.write(np.array(V, dtype="<f4").tobytes()); f.write(np.array(T, dtype="<i4").tobytes()); f.write(np.array(M, dtype="u1").tobytes())
        written.append({"file": os.path.basename(path), "locationCm": [ox * 100.0, -oy * 100.0, oz * 100.0], "buildings": len(items),
                        "triangles": len(T), "tile": [tx, ty]})
    with open(os.path.join(out, f"{name}.buildings-mesh.json"), "w", encoding="utf-8") as f:
        json.dump({"tiles": written, "tileMetres": tile_m}, f, indent=1)
    return {"tiles": len(written), "buildings": sum(w["buildings"] for w in written), "triangles": sum(w["triangles"] for w in written)}


def _shift(c, de, dn):
    if isinstance(c[0], (int, float)): return [c[0] + de, c[1] + dn]
    return [_shift(x, de, dn) for x in c]


def offset_geometry(geom, de, dn):
    def m(c):
        if isinstance(c[0], (int, float)): return [round(c[0] + de, 3), round(c[1] + dn, 3)]
        return [m(x) for x in c]
    geom["coordinates"] = m(geom["coordinates"])


# ── main ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("terrain"); ap.add_argument("out")
    ap.add_argument("--crs", choices=("auto", "local"), default="auto")
    ap.add_argument("--component-quads", type=int, choices=(7, 15, 31, 63, 127, 255), default=None,
                    help="pad to whole components of this many quads (default: 255 above 2 000 quads, 127 above 400, else 63)")
    ap.add_argument("--tree-spacing", type=float, default=8.0, help="metres between trees in our forests (8 ⇒ one per ~64 m²)")
    ap.add_argument("--look", action="store_true", help="also the look inputs: the cover mask, the tree scatter, the views (UL-10)")
    ap.add_argument("--panels", default=None, help="U-20: hrot-building-panels output; template houses are built from HROT's panels")
    ap.add_argument("--no-pad", action="store_true", help="write the grid as it is (Unreal may resample it - UL-7)")
    a = ap.parse_args()
    import rasterio
    from rasterio.transform import from_origin

    folder = os.path.abspath(a.terrain)
    # the shared templates: the repo's assets/buildings, and the editor's data root (data/shared/assets/buildings) when it has one
    TEMPLATE_DIRS[:] = [os.path.join(folder, "buildings")] + [d for d in (os.path.join(os.path.dirname(os.path.dirname(folder)), "buildings"),
                        os.path.join(REPO, "data", "shared", "assets", "buildings"), os.path.join(REPO, "assets", "buildings")) if os.path.isdir(d)]
    with open(os.path.join(folder, "terrain.json"), encoding="utf-8") as f:
        terrain = json.load(f)
    name = terrain.get("name") or os.path.basename(folder)
    with open(os.path.join(folder, terrain["world"]), encoding="utf-8") as f:
        world = json.load(f)
    hg = (world.get("hrot") or {}).get("heightGrid")
    if not hg: raise SystemExit("the world names no heightGrid (a flat terrain) — nothing to export as a landscape")
    hp = os.path.join(folder, hg)
    z, x0, y0, cell = read_heights(hp) if hp.endswith(".heights") else read_asc(hp)
    rows, cols = z.shape
    hrot_posts = [cols, rows]

    # ⭐ UL-7 (measured): Unreal builds a landscape from whole COMPONENTS of 7 · 15 · 31 · 63 · 127 · 255 quads. A grid that is not a whole
    # number of them is RESAMPLED by "Auto" (Stechovice 4 066 × 4 304 quads → stretched to 16 × 17 components of 255: heights off by up to
    # 1 m) or tiled into the smallest component that divides it (city-battle 1 500 = 100 × 15 ⇒ 10 000 components). ⇒ pad the east and
    # north edges up to whole components by REPEATING the edge posts — what HROT's own ground does past its edge (Sample clamps), so the
    # play area is untouched and the vertices stay on our posts.
    quads = max(cols, rows) - 1
    comp = a.component_quads or (255 if quads > 2000 else 127 if quads > 400 else 63)
    pad_c = (-(cols - 1)) % comp; pad_r = (-(rows - 1)) % comp
    if not a.no_pad and (pad_c or pad_r):
        z = np.pad(z, ((0, pad_r), (0, pad_c)), mode="edge")   # row 0 = south ⇒ the added rows are NORTH, the added columns EAST
        rows, cols = z.shape

    crs, (de, dn), what = frame_of(terrain, a.crs)
    os.makedirs(a.out, exist_ok=True)
    tif = os.path.join(a.out, f"{name}.height.tif")
    # ⭐ UL-4 (measured): Landscape Combinator puts a landscape VERTEX on each pixel's north-west CORNER, not its centre — a pixel-is-area
    # GeoTIFF of our posts landed half a cell east and south (5 m on basic-desert). ⇒ the corner sits ON the post: the landscape's vertices
    # are then exactly HROT's posts. (A GIS tool reading this file as pixel-is-area sees the grid half a cell off — the file is for Unreal.)
    transform = from_origin(x0 + de, y0 + dn + (rows - 1) * cell, cell, cell)
    with rasterio.open(tif, "w", driver="GTiff", width=cols, height=rows, count=1, dtype="float32", crs=crs, transform=transform,
                       compress="deflate") as ds:
        ds.write(z[::-1], 1)   # north row first
        ds.update_tags(HROT_TERRAIN=name, HROT_VERTEX="pixel north-west corner = HROT post (UL-4)")

    # ⭐ our own Unreal import (tools/unreal/HrotTerrain, UL-9): the 16-bit heights and the exact placement, computed HERE so nothing is
    # resampled in Unreal. Unreal: X east, Y SOUTH, cm; a landscape vertex (i, j) sits at Location + (i·Sx, j·Sy) and its height is
    # Location.Z + (h − 32768) / 128 · Sz. ⇒ the NORTH row first; Sz spreads the height range over the 16 bits.
    zmin, zmax = float(z.min()), float(z.max())
    rng = max(zmax - zmin, 0.01)
    h16 = np.clip(np.rint((z - zmin) / rng * 65535.0), 0, 65535).astype("<u2")
    h16[::-1].tofile(os.path.join(a.out, f"{name}.height.r16"))   # row 0 = south in z ⇒ reversed: the north row first
    sz = rng * 100.0 * 128.0 / 65535.0
    unreal = {"r16": f"{name}.height.r16", "sizeX": cols, "sizeY": rows, "quadsPerSection": comp if not a.no_pad else None,
              "sectionsPerComponent": 1,
              "locationCm": [x0 * 100.0, -(y0 + (rows - 1) * cell) * 100.0, zmin * 100.0 + 32768.0 * rng * 100.0 / 65535.0],
              "scaleCm": [cell * 100.0, cell * 100.0, sz], "heightStep_m": rng / 65535.0}

    feats, counts = features(folder, terrain, world)
    for f in feats: offset_geometry(f["geometry"], de, dn)
    look = write_look(a.out, name, feats, z, x0, y0, cell, rows, cols, unreal, de, dn, tree_spacing=a.tree_spacing, folder=folder,
                      terrain=terrain) if a.look else None
    if a.look:
        import terrain_look as tl
        local = [{**f, "geometry": {"type": f["geometry"]["type"], "coordinates": _shift(f["geometry"]["coordinates"], -de, -dn)}} for f in feats]
        def lowest(r_):
            return min(float(z[int(np.clip(round((y - y0) / cell), 0, rows - 1)), int(np.clip(round((x - x0) / cell), 0, cols - 1))]) for x, y in r_)
        panels = json.load(open(a.panels, encoding="utf-8")) if a.panels else None
        look["buildingMeshes"] = tl.write_buildings(a.out, name, local, template, lowest, x0, y0, panels=panels)
    vec = {"type": "FeatureCollection", "name": name, "features": feats}
    if crs is not None:
        epsg = crs.to_epsg()
        vec["crs"] = {"type": "name", "properties": {"name": f"urn:ogc:def:crs:EPSG::{epsg}" if epsg else crs.to_wkt()}}
    with open(os.path.join(a.out, f"{name}.vectors.geojson"), "w", encoding="utf-8") as f:
        json.dump(vec, f)
    # one file per kind as well: Landscape Combinator's spline importer turns EVERY feature of a file into a spline, so each of its
    # importers (buildings, roads, walls, forests) is pointed at its own kind
    # ⭐ UL-5 (measured): its spline importer reads EVERY vector file as WGS84 lon / lat (EPSG:4326 — SplineImporter.cpp:231), whatever
    # CRS the file declares ⇒ these per-kind files are in lon / lat (RFC 7946's own CRS); the plugin projects them into the level's CRS
    to_ll = None
    if crs is not None:
        from pyproj import Transformer
        to_ll = Transformer.from_crs(crs, "EPSG:4326", always_xy=True)
    def ll(c):
        if isinstance(c[0], (int, float)):
            lon, lat = to_ll.transform(c[0], c[1]); return [round(lon, 9), round(lat, 9)]
        return [ll(x) for x in c]
    for kind in sorted({f["properties"]["hrot_kind"] for f in feats}):
        sub = [x for x in feats if x["properties"]["hrot_kind"] == kind]
        if to_ll is not None:
            sub = [{**x, "geometry": {"type": x["geometry"]["type"], "coordinates": ll(x["geometry"]["coordinates"])}} for x in sub]
        with open(os.path.join(a.out, f"{name}.{kind}s.geojson"), "w", encoding="utf-8") as f:
            json.dump({"type": "FeatureCollection", "name": f"{name}.{kind}s", "features": sub}, f)

    # ⭐ the SHORT form Landscape Combinator needs (UL-3): it names a working folder after the CRS (only ':' replaced), so a WKT string
    # (quotes, brackets) is an invalid path and its Reproject phase fails ⇒ "EPSG:n", or the PROJ string for the transverse Mercator
    user_input = (f"EPSG:{crs.to_epsg()}" if crs.to_epsg() else crs.to_proj4().replace(" +type=crs", "")) if crs else None
    info = {"terrain": name, "frame": what, "crsUserInput": user_input, "crs": crs.to_wkt() if crs else None, "epsg": crs.to_epsg() if crs else None,
            "localToProjected": [de, dn], "posts": [cols, rows], "hrotPosts": hrot_posts, "componentQuads": None if a.no_pad else comp,
            "components": None if a.no_pad else [(cols - 1) // comp, (rows - 1) // comp], "cell": cell, "southWestPost": [x0, y0],
            "zMin": float(z.min()), "zMax": float(z.max()), "unreal": unreal, "features": counts, "look": look,
            # ⭐ CE-4662 (TD-9) — the ground material map's legend: every feature carries "thermal_class" (one of these); bare ground is "ground"
            "thermal": {"classes": THERMAL_CLASSES, "ground": thermal_library(folder)[1].get("ground")}}
    with open(os.path.join(a.out, f"{name}.frame.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=1)
    print(json.dumps(info, indent=1)[:1500] if crs is None else json.dumps({k: v for k, v in info.items() if k != "crs"}, indent=1))


if __name__ == "__main__":
    main()
