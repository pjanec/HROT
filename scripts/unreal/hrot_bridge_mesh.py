#!/usr/bin/env python3
"""hrot_bridge_mesh.py - CE-11063: HROT's OSM bridge decks as mesh tiles for the Unreal look level (NOTE_UiIntegration_To_UnrealIg_Osm_Bridges_Ready.md).

    python scripts/unreal/hrot_bridge_mesh.py <HROT terrain folder> <export folder> [--thickness 0.6] [--structure osm-bridge/]

Reads the IMPORTED terrain's <name>.world.geojson (HROT's importer, revision 5+, DESIGN_Terrain_OSM_Bridges.md D-6): every `slab` / `ramp` Polygon
whose `structure` starts with `osm-bridge/`, `zRef: ground` - each corner's third coordinate is its height ABOVE THE GROUND under that corner.
The ground is the export's <name>.height.tif (the posts the Unreal landscape is built from), sampled by HROT's own rule (TerrainHeightGrid.Sample:
planar in each cell's two triangles, the diagonal from the south-west post to the north-east one) - and a ramp's foot (above <= 0.01 m) is
buried 0.3 m as HROT's parser does (TerrainWorldParser.Buried, TH-K). ⇒ the deck's TOP is HROT's walkable, exactly.

⭐ Only the top is HROT's: a walkable has no thickness of its own, so the underside (`thickness` below the top, default 0.6 m) is drawn for the
look only. Slot 0 = the deck's top (the road surface), slot 1 = its sides and underside.
⛔ Nothing comes from OSM here: every height is HROT's (the user's "prepare, do not invent").

Writes one HrotTerrain.BuildMesh file per bridge (structure), <name>.bridge_<edge>.bin (int32 vertices, int32 triangles, float32 xyz Unreal cm
relative to the bridge's origin, int32 a b c, uint8 slot per triangle) + <name>.bridges-mesh.json, beside the export (never in git).
The mapping is the client's: Unreal (x, -y, z) * 100 of HROT's local metres.
"""
import argparse, json, os, struct
import numpy as np

RAMP_FOOT_TOLERANCE, RAMP_FOOT_SINK = 0.01, 0.3   # TerrainWorldParser.RampFootTolerance / RampFootSink


def ground_sampler(export, name):
    import rasterio
    frame = json.load(open(os.path.join(export, f"{name}.frame.json"), encoding="utf-8"))
    E0, N0 = frame["localToProjected"]
    dem = rasterio.open(os.path.join(export, f"{name}.height.tif"))
    H = dem.read(1).astype(np.float64)
    rows, cols = H.shape
    # the export's posts: pixel corners (UL-4); the transform's origin is the NORTH-WEST post ⇒ local (x, y) of post (c, r_from_north)
    west = dem.transform.c - E0; north = dem.transform.f - N0; cell = dem.transform.a

    def at(x, y):   # HROT TerrainHeightGrid.Sample on the same posts (row 0 = south there)
        fx = min(max((x - west) / cell, 0.0), cols - 1); fy = min(max((y - (north - (rows - 1) * cell)) / cell, 0.0), rows - 1)
        c = min(int(fx), cols - 2); r = min(int(fy), rows - 2)
        u, v = fx - c, fy - r
        z = lambda cc, rr: H[rows - 1 - rr, cc]   # r counted from the south
        z00, z10, z01, z11 = z(c, r), z(c + 1, r), z(c, r + 1), z(c + 1, r + 1)
        return (z00 + u * (z10 - z00) + v * (z11 - z10)) if u >= v else (z00 + v * (z01 - z00) + u * (z11 - z01))
    return at


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("terrain"); ap.add_argument("export")
    ap.add_argument("--thickness", type=float, default=0.6); ap.add_argument("--structure", default="osm-bridge/")
    a = ap.parse_args()
    terrain = json.load(open(os.path.join(a.terrain, "terrain.json"), encoding="utf-8"))
    name = terrain.get("name") or os.path.basename(os.path.normpath(a.terrain))
    world = json.load(open(os.path.join(a.terrain, terrain["world"]), encoding="utf-8"))
    ground = ground_sampler(a.export, name)

    bridges = {}   # structure -> [(kind, label, [(x, y, top)])]
    for f in world["features"]:
        p, g = f.get("properties") or {}, f.get("geometry") or {}
        if p.get("kind") not in ("slab", "ramp") or not str(p.get("structure", "")).startswith(a.structure) or g.get("type") != "Polygon": continue
        if p.get("zRef") != "ground": raise SystemExit(f"{p.get('label')}: zRef {p.get('zRef')!r} - only ground-relative decks are expected (D-6)")
        ring = [tuple(c) for c in g["coordinates"][0]]
        if len(ring) > 1 and ring[0] == ring[-1]: ring = ring[:-1]
        pts = []
        for x, y, above in ring:
            if p["kind"] == "ramp" and above <= RAMP_FOOT_TOLERANCE: above = -RAMP_FOOT_SINK
            pts.append((x, y, ground(x, y) + above))
        area2 = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))
        if area2 < 0: pts = pts[::-1]   # counter-clockwise in HROT (x east, y north)
        bridges.setdefault(p["structure"], []).append((p["kind"], p.get("label", ""), pts))

    spec = {"terrain": name, "thickness": a.thickness, "bridges": []}
    for structure, parts in sorted(bridges.items()):
        edge = structure.split("/", 1)[1]
        ox, oy = parts[0][2][0][0], parts[0][2][0][1]
        oz = min(z for _, _, pts in parts for _, _, z in pts) - a.thickness
        V, T, M = [], [], []
        def vtx(x, y, z):
            V.append(((x - ox) * 100.0, -(y - oy) * 100.0, (z - oz) * 100.0)); return len(V) - 1
        lo_above, hi_above, length = 1e9, -1e9, 0.0
        for kind, label, pts in parts:
            n = len(pts)
            top = [vtx(x, y, z) for x, y, z in pts]
            bot = [vtx(x, y, z - a.thickness) for x, y, z in pts]
            for i in range(1, n - 1):   # the top (slot 0) and the underside (slot 1), a fan over the convex quad
                T.append((top[0], top[i], top[i + 1])); M.append(0)
                T.append((bot[0], bot[i + 1], bot[i])); M.append(1)
            for i in range(n):          # the sides, outward-facing
                j = (i + 1) % n
                T += [(bot[i], bot[j], top[j]), (bot[i], top[j], top[i])]; M += [1, 1]
            for x, y, z in pts:
                lo_above = min(lo_above, z - ground(x, y)); hi_above = max(hi_above, z - ground(x, y))
        xs = [x for _, _, pts in parts for x, _, _ in pts]; ys = [y for _, _, pts in parts for _, y, _ in pts]
        fn = f"{name}.bridge_{edge.replace('-', 'm')}.bin"
        with open(os.path.join(a.export, fn), "wb") as f:
            f.write(struct.pack("<ii", len(V), len(T)))
            f.write(np.asarray(V, dtype="<f4").tobytes()); f.write(np.asarray(T, dtype="<i4").tobytes()); f.write(np.asarray(M, dtype="u1").tobytes())
        spec["bridges"].append({"structure": structure, "edge": edge, "file": fn, "locationCm": [ox * 100.0, -oy * 100.0, oz * 100.0],
                                "slabs": sum(1 for k, _, _ in parts if k == "slab"), "ramps": sum(1 for k, _, _ in parts if k == "ramp"),
                                "triangles": len(T), "aboveGround": [round(lo_above, 2), round(hi_above, 2)],
                                "extent": [round(max(xs) - min(xs), 1), round(max(ys) - min(ys), 1)], "centre": [round((max(xs) + min(xs)) / 2, 1), round((max(ys) + min(ys)) / 2, 1)]})
    json.dump(spec, open(os.path.join(a.export, f"{name}.bridges-mesh.json"), "w", encoding="utf-8"), indent=1)
    for b in spec["bridges"]:
        print(f"{b['structure']}: {b['slabs']} slabs, {b['ramps']} ramps, {b['triangles']} triangles, {b['aboveGround'][0]}..{b['aboveGround'][1]} m above the ground, "
              f"extent {b['extent']} m at {b['centre']}")
    print(f"{len(spec['bridges'])} bridges -> {os.path.join(a.export, name + '.bridges-mesh.json')}")


if __name__ == "__main__":
    main()
