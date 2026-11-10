#!/usr/bin/env python3
"""terrain-scale.py — the SCALE terrain (CE-4363, PLAN_Large_Terrain §7.1): the synthetic 5.5 km set filled with the existing templates.

  scripts/terrain-scale.py                       # seed 1 -> assets/terrain/scale-1/
  scripts/terrain-scale.py --seed 2 --heights asc --spacing 2   # another seed; an .asc at 2 m instead of the 1 m store
  scripts/terrain-scale.py --synth DIR           # use an already generated input set instead of running hrot-terrain-synth

Steps:
  1. hrot-terrain-synth --preset full --seed N  ->  artifacts/terrain-synth/full-N  (skipped when the set is already there)
  2. heights/heights.f32 resampled by hrot-heights (C#, CE-4365) to the binary height store at 1 m (--heights store, the default) or
     an ESRI ASCII grid (--heights asc, 2 m), in terrain-local metres; NODATA posts take their row's nearest valid height
  3. on every synthetic building site, one of the 7 templates in assets/buildings/: the largest whose footprint fits the site's
     rectangle (its longest wall = the template's x), else the smallest; the front (local y = 0) turned to the nearer lane; kept only
     when it stands clear of every lane and junction (the fence demo-city-quarters.py uses — scripts/terrain_placement.py) and of
     every house already placed (1 m)
  3a. with --trees (CE-4369, L-7): trees/trees.csv through hrot-trees (C#, TerrainWoods.Cluster — the importer's one clustering) into
     woods polygons on the 25 m grid (roads left open) and lone trees (trunks)
  4. scripts/terrain-from-sumo.py writes assets/terrain/scale-N/ (net, world, terrain.json) with the houses and the height grid

The output folder is gitignored (assets/terrain/scale-*/) — regenerated, never committed. Deterministic: the same seed and options give
byte-identical files. Pure stdlib.
"""
import argparse
import json
import math
import os
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import terrain_placement as tp  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SHARED = os.path.join(ROOT, "assets", "buildings")
TEMPLATES = ["house-2f", "house-3f", "shop-2f", "courtyard-house", "cqb-house", "office-4f", "market-hall"]
CELL = 50.0  # the spatial hash for the clearance tests (m)


def log(msg, t0):
    print(f"[{time.time() - t0:6.1f} s] {msg}", flush=True)


def run_synth(seed, out, extras):
    cmd = ["dotnet", "run", "-c", "Release", "--project", os.path.join(ROOT, "Hrot", "Tools", "Hrot.Tools.TerrainSynth"), "--",
           "--preset", "full", "--seed", str(seed), "--out", out]
    if extras:
        cmd.append("--extras")
    subprocess.run(cmd, check=True)


# ── heights ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────

def write_heights(synth, spacing, out_path):
    """the set's heights -> the terrain's grid (a .heights store or an .asc) through hrot-heights (CE-4365: ONE resampler, C#)."""
    r = subprocess.run(["dotnet", "run", "-c", "Release", "--project", os.path.join(ROOT, "Hrot", "Tools", "Hrot.Tools.Heights"), "--",
                        "--set", synth, "--spacing", f"{spacing:g}", "--out", out_path], check=True, capture_output=True, text=True)
    return r.stdout.strip()


def write_trees(synth, net_src, anchor, bounds, out_path):
    """the set's trees -> woods polygons + lone trees through hrot-trees (CE-4369: ONE clustering, C#, the importer's)."""
    lanes, _, _, _ = tp.load_net(net_src, anchor)
    segs = [[P[i][0], P[i][1], P[i + 1][0], P[i + 1][1], w / 2] for w, P in lanes for i in range(len(P) - 1)]
    roads = out_path + ".roads.tmp"
    with open(roads, "w", encoding="utf-8") as f:
        json.dump(segs, f)
    try:
        r = subprocess.run(["dotnet", "run", "-c", "Release", "--project", os.path.join(ROOT, "Hrot", "Tools", "Hrot.Tools.Trees"), "--",
                            "--trees", os.path.join(synth, "trees", "trees.csv"), "--offset", f"{bounds[0]:g}", f"{bounds[1]:g}",
                            "--size", f"{bounds[2] - bounds[0]:g}", f"{bounds[3] - bounds[1]:g}", "--roads", roads, "--out", out_path],
                           check=True, capture_output=True, text=True)
    finally:
        os.remove(roads)
    return r.stdout.strip()


# ── buildings ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

def site_frame(ring):
    """The site's rectangle along its longest wall: (centre, angle deg, length, width)."""
    pts = ring[:-1] if ring[0] == ring[-1] else ring
    best = None
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        if best is None or L > best[0] + 1e-9:
            best = (L, math.atan2(b[1] - a[1], b[0] - a[0]))
    ang = best[1]
    c, s = math.cos(ang), math.sin(ang)
    us = [p[0] * c + p[1] * s for p in pts]
    vs = [-p[0] * s + p[1] * c for p in pts]
    u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
    cu, cv = (u0 + u1) / 2, (v0 + v1) / 2
    centre = (cu * c - cv * s, cu * s + cv * c)
    return centre, math.degrees(ang), u1 - u0, v1 - v0


def choose_template(sizes, length, width, key):
    """The largest template that fits length x width (its x along the length); ties by the site's key; else the smallest."""
    fits = [t for t in TEMPLATES if sizes[t][0] <= length + 1e-6 and sizes[t][1] <= width + 1e-6]
    pool = fits or [min(TEMPLATES, key=lambda t: (sizes[t][0] * sizes[t][1], t))]
    area = max(sizes[t][0] * sizes[t][1] for t in pool)
    top = sorted(t for t in pool if sizes[t][0] * sizes[t][1] == area)
    return top[key % len(top)], bool(fits)


def stable_key(s):
    h = 2166136261
    for ch in s.encode("utf-8"):
        h = ((h ^ ch) * 16777619) & 0xFFFFFFFF
    return h


class Grid:
    def __init__(self):
        self.cells = {}

    def keys(self, pts, pad):
        xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
        for i in range(int(math.floor((min(xs) - pad) / CELL)), int(math.floor((max(xs) + pad) / CELL)) + 1):
            for j in range(int(math.floor((min(ys) - pad) / CELL)), int(math.floor((max(ys) + pad) / CELL)) + 1):
                yield (i, j)

    def add(self, pts, item, pad=0.0):
        for k in self.keys(pts, pad):
            self.cells.setdefault(k, []).append(item)

    def near(self, pts, pad):
        seen = set()
        for k in self.keys(pts, pad):
            for it in self.cells.get(k, ()):
                if id(it) not in seen:
                    seen.add(id(it))
                    yield it


def polys_apart(a, b, gap):
    if any(tp.inside(b, q) for q in a) or any(tp.inside(a, q) for q in b):
        return False
    return min(tp.seg_dist(q, b[k], b[(k + 1) % len(b)]) for q in a for k in range(len(b))) >= gap and \
        min(tp.seg_dist(q, a[k], a[(k + 1) % len(a)]) for q in b for k in range(len(a))) >= gap


def fit(t, w, d, centre, ang, lane_grid, junc_grid, house_grid):
    """Template t (w x d) centred on the site, its x along the site's longest wall, its front (local y = 0) toward the nearer lane:
    (position, rotation, footprint, None) when it stands clear, else (.., .., .., the reason)."""
    def at(rot):
        c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
        return (centre[0] - (w / 2 * c - d / 2 * s), centre[1] - (w / 2 * s + d / 2 * c))

    best = None
    for rot in (ang, ang + 180.0):
        pos = at(rot)
        front = tp.world(pos, rot, (w / 2, -3.0))
        dist = min((tp.seg_dist(front, a, b) for (_, a, b) in lane_grid.near([front], 60.0)), default=1e9)
        if best is None or dist < best[0] - 1e-9:
            best = (dist, rot, pos)
    _, rot, pos = best
    rot = round(rot % 360.0, 3)
    pos = at(rot)
    fp = tp.footprint(pos, rot, w, d)
    if any(tp.poly_seg_dist(fp, a, b) < lw / 2 + 0.2 for (lw, a, b) in lane_grid.near(fp, 0.0)):
        return pos, rot, fp, "lane"
    if tp.on_junction(fp, list(junc_grid.near(fp, 0.0))):
        return pos, rot, fp, "junction"
    if any(not polys_apart(fp, o["fp"], 1.0) for o in house_grid.near(fp, 1.0)):
        return pos, rot, fp, "house"
    return pos, rot, fp, None


def place_houses(synth, anchor, net_path, t0):
    sizes = {t: tp.template_size(t, (SHARED,))[:2] for t in TEMPLATES}
    lanes, _, _, juncs = tp.load_net(net_path, anchor)
    lane_grid, junc_grid = Grid(), Grid()
    for w, P in lanes:
        for i in range(len(P) - 1):
            lane_grid.add([P[i], P[i + 1]], (w, P[i], P[i + 1]), pad=w / 2 + 0.2)
    for J in juncs:
        junc_grid.add(J, J)
    log(f"net: {len(lanes)} lanes, {len(juncs)} junctions", t0)

    gj = json.load(open(os.path.join(synth, "buildings", "buildings.geojson"), encoding="utf-8"))
    placed, house_grid = [], Grid()
    stats = {"sites": 0, "fit": 0, "smallest": 0, "lane": 0, "junction": 0, "house": 0, "shrunk": 0}
    for feat in gj["features"]:
        stats["sites"] += 1
        sid = feat["properties"]["id"]
        ring = [(p[0] - anchor[0], p[1] - anchor[1]) for p in feat["geometry"]["coordinates"][0]]
        centre, ang, length, width = site_frame(ring)
        t, fitted = choose_template(sizes, length, width, stable_key(sid))
        # the chosen template first, then each smaller one: a site the first one does not clear may still take a smaller house
        smaller = sorted((u for u in TEMPLATES if sizes[u][0] * sizes[u][1] < sizes[t][0] * sizes[t][1]),
                         key=lambda u: (-sizes[u][0] * sizes[u][1], u))
        why = None
        for t in [t] + smaller:
            w, d = sizes[t]
            pos, rot, fp, why = fit(t, w, d, centre, ang, lane_grid, junc_grid, house_grid)
            if why is None:
                break
        if why is not None:
            stats[why] += 1
            continue
        if t != choose_template(sizes, length, width, stable_key(sid))[0]:
            stats["shrunk"] += 1
        stats["fit" if fitted else "smallest"] += 1
        rec = {"fp": fp, "feature": {
            "type": "Feature",
            "properties": {"kind": "building", "template": t, "label": sid, "rotation": rot},
            "geometry": {"type": "Point", "coordinates": [round(pos[0], 2), round(pos[1], 2)]}}}
        placed.append(rec)
        house_grid.add(fp, rec)
    return [r["feature"] for r in placed], stats


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--heights", choices=("store", "asc"), default="store",
                    help="store: the binary height store (.heights, CE-4365) · asc: an ESRI ASCII grid (the small-terrain format)")
    ap.add_argument("--spacing", type=float, default=None, help="height post spacing (m); default 1 for the store, 2 for an .asc")
    ap.add_argument("--synth", default=None, help="an existing input set (default: artifacts/terrain-synth/full-<seed>, generated when absent)")
    ap.add_argument("--extras", action="store_true", help="generate the input set with --extras (woods, water, scrub)")
    ap.add_argument("--trees", action="store_true", help="CE-4369: woods + lone trees from the set's trees/trees.csv (hrot-trees)")
    ap.add_argument("--name", default=None, help="default: scale-<seed>")
    ap.add_argument("--out", default=None, help="default: assets/terrain/<name>")
    a = ap.parse_args()
    t0 = time.time()

    name = a.name or f"scale-{a.seed}"
    synth = a.synth or os.path.join(ROOT, "artifacts", "terrain-synth", f"full-{a.seed}" + ("-extras" if a.extras else ""))
    if not os.path.exists(os.path.join(synth, "manifest.json")):
        log(f"generating the input set -> {synth}", t0)
        run_synth(a.seed, synth, a.extras)
    manifest = json.load(open(os.path.join(synth, "manifest.json"), encoding="utf-8"))
    bounds = manifest["bounds"]
    net_src = os.path.join(synth, "roads", "roads.net.xml")
    if not os.path.exists(net_src):
        raise SystemExit(f"{net_src} is missing — the synth tool writes it only when netconvert is found (SUMO 1.20.0, Terrain From OSM §9.1)")

    # SUMO shifts a net by its netOffset: net = input-set frame + offset ⇒ the anchor (the bounds' lower-left) in net metres
    loc = ET.parse(net_src).getroot().find("location")
    off = [float(v) for v in loc.get("netOffset", "0,0").split(",")]
    anchor = (bounds[0] + off[0], bounds[1] + off[1])
    box = (anchor[0], anchor[1], anchor[0] + bounds[2] - bounds[0], anchor[1] + bounds[3] - bounds[1])

    out = a.out or os.path.join(ROOT, "assets", "terrain", name)
    os.makedirs(out, exist_ok=True)
    spacing = a.spacing or (1.0 if a.heights == "store" else 2.0)
    asc = f"{name}.heights" if a.heights == "store" else f"{name}.height.asc"
    for old in (f"{name}.heights", f"{name}.height.asc"):
        if old != asc and os.path.exists(os.path.join(out, old)):
            os.remove(os.path.join(out, old))
    log(write_heights(synth, spacing, os.path.join(out, asc)), t0)

    houses, st = place_houses(synth, anchor, net_src, t0)
    by_t = {}
    for h in houses:
        by_t[h["properties"]["template"]] = by_t.get(h["properties"]["template"], 0) + 1
    log(f"houses: {len(houses)} of {st['sites']} sites ({st['fit']} fitted, {st['smallest']} the smallest template); "
        f"{st['shrunk']} shrunk to clear; dropped: {st['lane']} on a lane, {st['junction']} on a junction, {st['house']} on a neighbour; "
        + ", ".join(f"{n} {t}" for t, n in sorted(by_t.items())), t0)

    houses_path = os.path.join(out, f"{name}.houses.json")
    with open(houses_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"$comment": f"Generated by scripts/terrain-scale.py (CE-4363) from hrot-terrain-synth full seed {a.seed}: the 7 shared "
                               f"templates on the synthetic building sites. Regenerated, never committed.",
                   "type": "FeatureCollection", "features": houses}, f, indent=1)
        f.write("\n")
    extra = []
    trees_path = os.path.join(out, f"{name}.trees.json")
    if a.trees:
        log(write_trees(synth, net_src, anchor, bounds, trees_path), t0)
        extra = ["--extra-features", trees_path]
    elif os.path.exists(trees_path):
        os.remove(trees_path)
    empty = os.path.join(out, ".no-data-buildings.json")
    with open(empty, "w", encoding="utf-8") as f:
        json.dump({"buildings": []}, f)
    subprocess.run([sys.executable, os.path.join(ROOT, "scripts", "terrain-from-sumo.py"), "--net", net_src, "--buildings", empty,
                    "--name", name, "--box", *[f"{v:g}" for v in box], "--out", out, "--houses", houses_path,
                    "--height-grid", os.path.join(out, asc), *extra,
                    "--source", f"hrot-terrain-synth full seed {a.seed} (scripts/terrain-scale.py, CE-4363)"], check=True)
    os.remove(empty)
    log(f"{name} -> {out}", t0)


if __name__ == "__main__":
    main()
