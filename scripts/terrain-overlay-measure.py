#!/usr/bin/env python3
# usage: python3 scripts/terrain-overlay-measure.py <repo root>   (needs assets/terrain/scale-1 — scripts/terrain-scale.py); then
#   HROT_MEASURE_OVERLAY=1 HROT_REPO=<root> dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter O1_Measure   (Terrain Authoring §18b)
# O-1 measurement input: scale-1 split into a BASE (scale-1 minus 700 houses) and an OVERLAY (those 700 houses + 40 woods), terrain format
import json, os, sys, shutil, uuid, random
root = sys.argv[1]; src = os.path.join(root, "assets/terrain/scale-1"); base = os.path.join(root, "assets/terrain/scale-1-o1base")
ov = os.path.join(root, "scenarios/scale-rig-o1/terrain-overlay")
for d in (base, ov): shutil.rmtree(d, ignore_errors=True); os.makedirs(d)
w = json.load(open(os.path.join(src, "scale-1.world.geojson")))
rnd = random.Random(1); feats = w["features"]; idx = sorted(rnd.sample(range(len(feats)), 700))
pick = set(idx); g = uuid.UUID(int=rnd.getrandbits(128))
over = []
for n, i in enumerate(idx):
    f = json.loads(json.dumps(feats[i])); f["properties"]["label"] = f"{g}/{n}"; over.append(f)
for k in range(40):   # woods: 40 squares of 60 m near overlay houses
    c = feats[idx[k * 17]]["geometry"]["coordinates"]; x, y = c[0] + 30, c[1] + 30
    over.append({"type": "Feature", "properties": {"kind": "surface", "surface": "forest", "label": f"{g}/w{k}"},
                 "geometry": {"type": "Polygon", "coordinates": [[[x, y], [x + 60, y], [x + 60, y + 60], [x, y + 60], [x, y]]]}})
over.sort(key=lambda f: f["properties"]["label"])
w2 = dict(w); w2["features"] = [f for i, f in enumerate(feats) if i not in pick]
json.dump(w2, open(os.path.join(base, "scale-1.world.geojson"), "w"))
for fn in os.listdir(src):
    if fn.endswith(".world.geojson"): continue
    os.link(os.path.join(src, fn), os.path.join(base, fn))
wo = {"type": "FeatureCollection", "features": over}
json.dump(wo, open(os.path.join(ov, "overlay.world.geojson"), "w"), separators=(",", ":"))
json.dump({"format": 1, "contentHash": "measure", "base": {"name": "scale-1-o1base", "contentHash": "measure"}, "scenario": "scale-rig-o1", "world": "overlay.world.geojson"},
          open(os.path.join(ov, "overlay.json"), "w"))
print("base houses", len(w2["features"]), "overlay features", len(over), "overlay world bytes", os.path.getsize(os.path.join(ov, "overlay.world.geojson")))
