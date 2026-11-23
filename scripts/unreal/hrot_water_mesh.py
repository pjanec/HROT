#!/usr/bin/env python3
"""hrot_water_mesh.py - CE-4651 (U-19h): a terrain's WATER as mesh tiles for the Unreal look level (the user, 2026-11-23: "There is no water in
Vltava, just grass (dry river)").

    python scripts/unreal/hrot_water_mesh.py <export folder> [--cell 3] [--lift 0.3] [--tile 500]

Reads the terrain-to-unreal export (<name>.vectors.geojson: surfaces of class "water"; <name>.height.tif: the ground the Unreal landscape is
built from; <name>.frame.json: localToProjected). A grid of `cell` metres is laid over each water polygon; a cell whose centre lies inside is
two triangles whose corners sit `lift` metres above THAT ground - the public height data measures the water's surface, so the sheet follows
the river's slope instead of being one flat plane. Written as HrotTerrain.BuildMesh tiles (int32 vertices, int32 triangles, float32 xyz in
Unreal cm relative to the tile origin, int32 a b c, uint8 slot per triangle) + <name>.water-mesh.json, beside the export (never in git).
The mapping is the client's: Unreal (x, -y, z) * 100 of HROT's local metres.
"""
import argparse, json, math, os, struct
import numpy as np
import rasterio
import shapely
from shapely.geometry import shape


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export"); ap.add_argument("--cell", type=float, default=3.0); ap.add_argument("--lift", type=float, default=0.3)
    ap.add_argument("--tile", type=float, default=500.0)
    a = ap.parse_args()
    name = os.path.basename(os.path.normpath(a.export))
    frame = json.load(open(os.path.join(a.export, f"{name}.frame.json"), encoding="utf-8"))
    E0, N0 = frame["localToProjected"]
    vec = json.load(open(os.path.join(a.export, f"{name}.vectors.geojson"), encoding="utf-8"))
    polys = [shape(f["geometry"]) for f in vec["features"] if f["properties"].get("class") == "water"]
    dem = rasterio.open(os.path.join(a.export, f"{name}.height.tif"))
    H = dem.read(1)

    def height(e, n):   # bilinear on the posts (the landscape's own interpolation between them)
        c, r = ~dem.transform * (np.asarray(e, dtype=float), np.asarray(n, dtype=float))   # pixel (col, row) of the point (posts at pixel corners)
        r0 = np.clip(np.floor(r).astype(int), 0, H.shape[0] - 2); c0 = np.clip(np.floor(c).astype(int), 0, H.shape[1] - 2)
        fr, fc = np.clip(r - r0, 0, 1), np.clip(c - c0, 0, 1)
        return (H[r0, c0] * (1 - fr) * (1 - fc) + H[r0, c0 + 1] * (1 - fr) * fc + H[r0 + 1, c0] * fr * (1 - fc) + H[r0 + 1, c0 + 1] * fr * fc)

    tiles = {}   # (tx, ty) -> (verts list, tris list)
    cells = 0
    for poly in polys:
        x0, y0, x1, y1 = poly.bounds
        xs = np.arange(math.floor(x0 / a.cell) * a.cell, x1, a.cell); ys = np.arange(math.floor(y0 / a.cell) * a.cell, y1, a.cell)
        gx, gy = np.meshgrid(xs, ys)
        inside = shapely.contains_xy(poly, gx + a.cell / 2, gy + a.cell / 2)
        for e, n in zip(gx[inside], gy[inside]):
            lx, ly = e - E0, n - N0   # local metres
            key = (int(lx // a.tile), int(ly // a.tile))
            V, T = tiles.setdefault(key, ([], []))
            ox, oy = key[0] * a.tile, key[1] * a.tile
            base = len(V)
            for de, dn in ((0, 0), (a.cell, 0), (a.cell, a.cell), (0, a.cell)):
                z = float(height(np.array([e + de]), np.array([n + dn]))[0]) + a.lift
                V.append(((lx + de - ox) * 100.0, -(ly + dn - oy) * 100.0, z * 100.0))
            T.append((base, base + 1, base + 2)); T.append((base, base + 2, base + 3))
            cells += 1
    spec = {"terrain": name, "tileMetres": a.tile, "cell": a.cell, "lift": a.lift, "polygons": len(polys), "cells": cells, "tiles": []}
    for (tx, ty), (V, T) in sorted(tiles.items()):
        fn = f"{name}.water_{tx}_{ty}.bin"
        with open(os.path.join(a.export, fn), "wb") as f:
            f.write(struct.pack("<ii", len(V), len(T)))
            f.write(np.asarray(V, dtype="<f4").tobytes()); f.write(np.asarray(T, dtype="<i4").tobytes()); f.write(bytes(len(T)))
        spec["tiles"].append({"file": fn, "tile": [tx, ty], "locationCm": [tx * a.tile * 100.0, -ty * a.tile * 100.0, 0.0], "triangles": len(T)})
    json.dump(spec, open(os.path.join(a.export, f"{name}.water-mesh.json"), "w", encoding="utf-8"), indent=1)
    print(f"{len(polys)} water polygons, {cells} cells of {a.cell} m, {len(spec['tiles'])} tiles")


if __name__ == "__main__":
    main()
