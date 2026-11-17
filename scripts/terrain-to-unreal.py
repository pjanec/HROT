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
    <name>.<kind>s.geojson  the same features, one file per kind (buildings, walls, surfaces, prisms, trees, roads), in WGS84
                            lon / lat (UL-5: Landscape Combinator's spline importer reads every vector file as EPSG:4326)
    <name>.frame.json       the frame used: CRS, the local → projected offset, the grid size — what the import / the checks read

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


def template(name):
    if name not in _templates:
        with open(os.path.join(REPO, "assets", "buildings", f"{name}.building.json"), encoding="utf-8") as f:
            _templates[name] = json.load(f)
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


def features(folder, terrain, world):
    feats, counts = [], {}

    def add(geom_type, coords, props):
        feats.append({"type": "Feature", "properties": props, "geometry": {"type": geom_type, "coordinates": coords}})
        counts[props["hrot_kind"]] = counts.get(props["hrot_kind"], 0) + 1

    for f in world["features"]:
        p, g = f["properties"], f["geometry"]
        kind = p.get("kind")
        if kind == "building":
            if g["type"] == "Point":   # a template placement
                t = template(p["template"])
                height = float(t["height"]) if t.get("solid") else sum(float(s["height"]) for s in t["storeys"])
                levels = 1 if t.get("solid") else len(t["storeys"])
                fp = ring(place(t["footprint"], g["coordinates"], float(p.get("rotation", 0))))
                extra = {"template": p["template"], "base_z": p.get("baseZ")}
            else:                       # an inline prism building
                height = float(p.get("height", 3.0))
                levels = int(p.get("floors") or max(1, round(height / 3.0)))
                fp = ring(g["coordinates"][0]); extra = {}
            add("Polygon", [fp], {"hrot_kind": "building", "building": "yes", "height": round(height, 3),
                                  "building_levels": levels, "name": p.get("label", ""), **extra})
        elif kind == "wall":
            add("LineString", [tuple(c[:2]) for c in g["coordinates"]],
                {"hrot_kind": "wall", "barrier": "hedge" if p.get("material") == "hedge" else "wall", "material": p.get("material", ""),
                 "height": p.get("height"), "thickness": p.get("thickness"), "name": p.get("label", "")})
        elif kind == "surface":
            cls = p.get("surface", "open")
            osm = {"forest": {"landuse": "forest"}, "water": {"natural": "water"}}.get(cls, {"landuse": "grass"})
            add("Polygon", [ring(r) for r in g["coordinates"]], {"hrot_kind": "surface", "class": cls, **osm, "name": p.get("label", "")})
        elif kind == "prism":
            add("Polygon", [ring(r) for r in g["coordinates"]], {"hrot_kind": "prism", "clutter": p.get("clutter", ""),
                                                                   "material": p.get("material", ""), "height": p.get("height")})
        elif kind == "tree" or (g["type"] == "Point" and "tree" in str(kind)):
            add("Point", tuple(g["coordinates"][:2]), {"hrot_kind": "tree", "natural": "tree", "height": p.get("height")})
        else:
            counts["skipped:" + str(kind)] = counts.get("skipped:" + str(kind), 0) + 1

    for road_file in terrain.get("roadNetworks") or []:
        with open(os.path.join(folder, road_file), encoding="utf-8") as f:
            net = json.load(f)
        for seg in net.get("segments", []):
            add("LineString", hermite(seg), {"hrot_kind": "road", "highway": "residential", "lanes": seg.get("laneCount"),
                                              "lane_width": seg.get("laneWidth"), "width": (seg.get("laneCount") or 1) * (seg.get("laneWidth") or 3.5),
                                              "name": f"segment {seg['id']}"})
    return feats, counts


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
    a = ap.parse_args()
    import rasterio
    from rasterio.transform import from_origin

    folder = os.path.abspath(a.terrain)
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

    feats, counts = features(folder, terrain, world)
    for f in feats: offset_geometry(f["geometry"], de, dn)
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
            "localToProjected": [de, dn], "posts": [cols, rows], "cell": cell, "southWestPost": [x0, y0],
            "zMin": float(z.min()), "zMax": float(z.max()), "features": counts}
    with open(os.path.join(a.out, f"{name}.frame.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=1)
    print(json.dumps(info, indent=1)[:1500] if crs is None else json.dumps({k: v for k, v in info.items() if k != "crs"}, indent=1))


if __name__ == "__main__":
    main()
