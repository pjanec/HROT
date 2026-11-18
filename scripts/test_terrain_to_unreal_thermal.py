#!/usr/bin/env python3
"""CE-4662 rail (docs/designs/thermal/DESIGN_Thermal_Drivers.md TD-9) — the terrain export (terrain-to-unreal.py) writes a THERMAL CLASS on every
feature of every shipped terrain, from the fixed list, and it round-trips: a wall's / prism's class is its material's class in the library a node
reads (the shared materials.json + the terrain's own), a surface's is its surface's. No GeoTIFF is written (features() only: no rasterio needed).
Run: python3 scripts/test_terrain_to_unreal_thermal.py
🔴 Red-proof: drop "thermal_class" from the wall properties in features() ⇒ red."""
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, ".."))
spec = importlib.util.spec_from_file_location("t2u", os.path.join(HERE, "terrain-to-unreal.py"))
t2u = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t2u)

failed, terrains, checked = 0, 0, 0
root = os.path.join(REPO, "assets", "terrain")
for name in sorted(os.listdir(root)):
    folder = os.path.join(root, name)
    path = os.path.join(folder, "terrain.json")
    if not os.path.exists(path): continue
    with open(path, encoding="utf-8") as f:
        terrain = json.load(f)
    if not terrain.get("world"): continue
    with open(os.path.join(folder, terrain["world"]), encoding="utf-8") as f:
        world = json.load(f)
    t2u.TEMPLATE_DIRS[:] = [d for d in (os.path.join(folder, "buildings"), os.path.join(REPO, "assets", "buildings")) if os.path.isdir(d)]
    by_mat, by_surf = t2u.thermal_library(folder)
    feats, _ = t2u.features(folder, terrain, world)
    terrains += 1
    for f in feats:
        pr = f["properties"]
        cls = pr.get("thermal_class")
        checked += 1
        if cls not in t2u.THERMAL_CLASSES:
            print(f"FAIL {name}: a {pr['hrot_kind']} '{pr.get('name', '')}' has thermal_class {cls!r}"); failed += 1; continue
        if pr["hrot_kind"] in ("wall", "prism") and pr.get("material") and pr["material"] in by_mat and cls != by_mat[pr["material"]]:
            print(f"FAIL {name}: material {pr['material']} exported as {cls}, the library says {by_mat[pr['material']]}"); failed += 1
        if pr["hrot_kind"] == "surface" and cls != by_surf.get(pr["class"]):
            print(f"FAIL {name}: surface {pr['class']} exported as {cls}, the library says {by_surf.get(pr['class'])}"); failed += 1
if terrains < 10 or checked == 0:
    print(f"FAIL vacuous: {terrains} terrains, {checked} features"); failed += 1
print(f"{'FAILED' if failed else 'PASSED'}: {checked} features of {terrains} terrains, {failed} failures")
sys.exit(1 if failed else 0)
