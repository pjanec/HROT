#!/usr/bin/env python3
"""demo-city-quarters.py — give demo-city its QUARTERS (UI-8 … UI-11, HANDOFF_Behaviors_To_Ui_Demo_City_Quarters).

  scripts/demo-city-quarters.py            # write demo-city.houses.json + merge it into demo-city.world.geojson
  scripts/demo-city-quarters.py --check    # verify only (exit 1 when a fence breaks or the files are stale)
  scripts/demo-city-quarters.py --svg OUT  # also draw the quarter plan (docs/designs/group-maneuvers/img/demo-city-quarters.svg)

The placements are AUTHORED here as rows along named streets (R-2053: our buildings may be made however suits — R-4052's "no
procedural buildings" is SumoSharp's viewer buildings only). Each row is a street side: a facing, a front line and a list of
(template, lot start). The script keeps the coordinator's CE-4028 houses except the ones a quarter REPLACES (named below), writes
them all to demo-city.houses.json (the file scripts/terrain-from-sumo.py --houses merges), and rewrites the world file's houses the
same way that script does (its data buildings kept as they are).

Fences checked (the hand-off's section 2):
  - every building stands clear of every lane and junction of the SUMO net (no change to the road net or the traffic);
  - no two buildings overlap (1 m apart at least) and all lie inside the terrain;
  - every front door faces a sidewalk (the market square's shops: the square, a pedestrian plaza), and every sidewalk point stays
    within 70 m of a way in (the civilian shelter query searches 80 m);
  - the landing square (UI-11) is clear of every building.

📄 docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md §4a. Pure stdlib, deterministic.
"""
import argparse
import json
import math
import os
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
TERRAIN = os.path.join(ROOT, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "demo-city")
SHARED = os.path.join(ROOT, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Buildings")
ANCHOR = (2200.0, 2200.0)
SIZE = 800.0
SHELTER = 70.0            # every sidewalk point within this of a way in (the query searches 80 m)
SQUARE = (661.0, 411.0, 758.0, 488.0)    # the market square: a pedestrian PLAZA — its shops' doors face it, not a sidewalk
LANDING = (565.0, 295.0, 615.0, 345.0)   # UI-11: x0, y0, x1, y1 — 50 x 50 m, clear, behind the battle street's west row

# CE-4028 houses a quarter replaces (the quarter's own buildings stand there, doors on the same sidewalk)
REPLACED = {
    "house-1f-26": "battle street, east side",
    "house-1f-13": "battle street, east side",
    "house-1f-23": "market square, north side",
    "house-1f-1": "Avenue E, east side, north of the square",
    "shop-1f-8": "business quarter, Street N south side",
    "house-1f-22": "business quarter, Street N south side",
}

# Facing = where the FRONT (local y = 0) looks: S (rotation 0), E (90), N (180), W (270). front = the world line of the front wall
# (each street's sidewalk edge + 0.35 m: the half wall and the lane clearance). A lot is (template, start along the street); the
# building covers [start, start + width] along it.
ROWS = [
    # ── RESIDENTIAL — the battle street (Avenue E, x ≈ 650: sidewalks both sides) from the open south edge to Street M ──
    ("residential", "res-bw", "battle street, west side", "E", 639.2, [
        ("house-3f", 4), ("house-2f", 18), ("courtyard-house", 32), ("house-2f", 52), ("house-3f", 66), ("house-2f", 80),
        ("courtyard-house", 94), ("house-2f", 114),
        ("house-2f", 170), ("house-3f", 184), ("house-2f", 198), ("courtyard-house", 212), ("house-2f", 232), ("house-2f", 246),
        ("house-3f", 260), ("courtyard-house", 274), ("house-2f", 294),
        # y 304..318: the lane to the landing square
        ("house-2f", 318), ("house-3f", 332), ("house-2f", 346), ("courtyard-house", 360)]),
    ("residential", "res-be", "battle street, east side", "W", 660.8, [
        ("house-2f", 4), ("courtyard-house", 18), ("house-2f", 38), ("house-3f", 52), ("house-2f", 66), ("house-2f", 80),
        ("courtyard-house", 94), ("house-3f", 114),
        ("house-2f", 170), ("house-2f", 184), ("courtyard-house", 198), ("house-3f", 218), ("house-2f", 232), ("house-2f", 246),
        ("house-2f", 260), ("house-3f", 274), ("house-2f", 288), ("courtyard-house", 302), ("house-2f", 322), ("house-2f", 336),
        ("courtyard-house", 350), ("house-3f", 370)]),
    ("residential", "res-sgn", "Street S, north side, west of the battle street", "S", 164.0, [
        ("house-2f", 420), ("house-2f", 434), ("house-3f", 448), ("courtyard-house", 462), ("house-2f", 482), ("house-2f", 496),
        ("house-2f", 510), ("house-3f", 524), ("courtyard-house", 538), ("house-2f", 558), ("house-2f", 572), ("house-2f", 586),
        ("house-3f", 600)]),
    ("residential", "res-sgs", "Street S, south side, west of the battle street", "N", 136.0, [
        ("house-2f", 442), ("house-3f", 456), ("house-2f", 470), ("house-2f", 504), ("courtyard-house", 518), ("house-2f", 538),
        ("house-3f", 552), ("house-2f", 566), ("house-2f", 580), ("house-2f", 594)]),
    ("residential", "res-shn", "Street S, north side, east of the battle street", "S", 164.0, [
        ("house-2f", 676), ("house-2f", 690), ("house-3f", 704), ("courtyard-house", 718), ("house-2f", 738), ("house-2f", 752),
        ("house-2f", 766), ("house-3f", 780)]),
    ("residential", "res-shs", "Street S, south side, east of the battle street", "N", 136.0, [
        ("house-2f", 676), ("house-3f", 690), ("house-2f", 704), ("house-2f", 718), ("house-2f", 752), ("courtyard-house", 766)]),
    # ── COMMERCIAL — the market square (x 661..758, y 411..488) at the battle street's north end, NE of the Avenue E × Street M ──
    ("commercial", "com-se", "market square, east side", "W", 760.2, [
        ("shop-2f", 416), ("shop-2f", 430), ("shop-2f", 444), ("shop-2f", 458), ("shop-2f", 472)]),
    ("commercial", "com-sn", "market square, north side", "S", 490.0, [
        ("shop-2f", 662), ("shop-2f", 676), ("shop-2f", 690), ("shop-2f", 704), ("shop-2f", 718), ("shop-2f", 732), ("shop-2f", 746)]),
    ("commercial", "com-ee", "Avenue E, east side, north of the square", "W", 660.8, [
        ("shop-2f", 504), ("shop-2f", 518), ("shop-2f", 532), ("shop-2f", 546), ("shop-2f", 560), ("shop-2f", 574),
        ("shop-2f", 588), ("shop-2f", 602)]),
    ("commercial", "com-ew", "Avenue E, west side, facing the square", "E", 639.2, [
        ("shop-2f", 416), ("shop-2f", 430), ("shop-2f", 444), ("shop-2f", 458), ("shop-2f", 472), ("shop-2f", 486)]),
    # ── BUSINESS — office blocks round the tower (bldg_office_10) and along Street N ─────────────────────────────────
    ("business", "biz-ms", "Street M, north side", "S", 410.8, [
        ("office-4f", 418), ("office-4f", 444), ("office-4f", 470), ("office-4f", 496), ("office-4f", 522), ("office-4f", 548),
        ("office-4f", 574)]),
    ("business", "biz-mw", "Avenue M, east side", "W", 410.8, [
        ("office-4f", 430), ("office-4f", 456), ("office-4f", 482), ("office-4f", 528), ("office-4f", 554), ("office-4f", 580)]),
    ("business", "biz-ns", "Street N, south side", "N", 639.2, [
        ("office-4f", 428), ("office-4f", 454), ("office-4f", 480), ("office-4f", 506), ("office-4f", 532), ("office-4f", 558),
        ("office-4f", 584), ("office-4f", 610)]),
    ("business", "biz-nn", "Street N, north side", "S", 660.8, [
        ("office-4f", 418), ("office-4f", 444), ("office-4f", 470), ("office-4f", 496), ("office-4f", 522), ("office-4f", 548),
        ("office-4f", 574), ("office-4f", 600)]),
]
# single buildings: (quarter, label, where, template, position, rotation)
SINGLES = [
    ("commercial", "com-hall", "the market square", "market-hall", (688.0, 446.0), 0.0),
]

ROTATION = {"S": 0.0, "E": 90.0, "N": 180.0, "W": 270.0}


def template_size(name):
    for d in (os.path.join(TERRAIN, "buildings"), SHARED):
        p = os.path.join(d, name + ".building.json")
        if os.path.exists(p):
            t = json.load(open(p, encoding="utf-8"))
            xs = [q[0] for q in t["footprint"]]
            ys = [q[1] for q in t["footprint"]]
            doors = []
            for s in t.get("storeys", [])[:1]:
                for w in s.get("walls", []):
                    for o in w.get("openings", []):
                        if o.get("kind") == "door" and "doorId" in o:
                            doors.append((w["from"], w["to"], o["at"] + o["width"] / 2, o["doorId"]))
            return max(xs) - min(xs), max(ys) - min(ys), doors
    raise SystemExit(f"template '{name}' not found in {TERRAIN}/buildings or {SHARED}")


def place(facing, front, start, width):
    """The instance position (template origin) and rotation for a lot [start, start + width] on a street side."""
    r = ROTATION[facing]
    if facing == "S":
        return (start, front), r            # x along, building north of the front
    if facing == "N":
        return (start + width, front), r    # local (0,0) → (px, py); x runs west
    if facing == "E":
        return (front, start), r            # local x → +y, building west of the front
    return (front, start + width), r        # W: local x → −y, building east of the front


def world(pos, rot, p):
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    return (pos[0] + p[0] * c - p[1] * s, pos[1] + p[0] * s + p[1] * c)


def footprint(pos, rot, w, d):
    return [world(pos, rot, q) for q in ((0, 0), (w, 0), (w, d), (0, d))]


def seg_dist(p, a, b):
    ax, ay = a; bx, by = b; px, py = p
    dx, dy = bx - ax, by - ay
    L = dx * dx + dy * dy
    t = 0.0 if L == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / L))
    return math.hypot(ax + t * dx - px, ay + t * dy - py)


def inside(poly, p):
    x, y = p; n = len(poly); c = False
    for i in range(n):
        x1, y1 = poly[i]; x2, y2 = poly[(i + 1) % n]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            c = not c
    return c


def segs_cross(a, b, c, d):
    def o(p, q, r): return (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])
    return o(a, b, c) * o(a, b, d) < 0 and o(c, d, a) * o(c, d, b) < 0


def poly_seg_dist(poly, a, b):
    if inside(poly, a) or inside(poly, b):
        return 0.0
    n = len(poly)
    best = min(min(seg_dist(poly[i], a, b) for i in range(n)), seg_dist(a, poly[0], poly[1]))
    for i in range(n):
        e0, e1 = poly[i], poly[(i + 1) % n]
        if segs_cross(e0, e1, a, b):
            return 0.0
        best = min(best, seg_dist(a, e0, e1), seg_dist(b, e0, e1))
    return best


def load_net():
    def pts(s): return [(float(q.split(",")[0]) - ANCHOR[0], float(q.split(",")[1]) - ANCHOR[1]) for q in s.split()]
    net = ET.parse(os.path.join(TERRAIN, "demo-city.net.xml")).getroot()
    lanes, sidewalk, walks = [], [], []
    for e in net.iter("edge"):
        for l in e.iter("lane"):
            P = pts(l.get("shape"))
            lanes.append((float(l.get("width", 3.2)), P))
            if l.get("allow") == "pedestrian" and e.get("function") != "internal":
                sidewalk.extend(P)
                walks.append(P)
    juncs = [pts(j.get("shape")) for j in net.iter("junction") if j.get("shape") and j.get("type") != "internal"]
    return lanes, sidewalk, walks, juncs


def buildings():
    """Every new placement: dicts with quarter, label, where, template, position, rotation, footprint, doors (world)."""
    out = []
    for quarter, prefix, where, facing, front, lots in ROWS:
        for i, (t, start) in enumerate(lots, 1):
            w, d, doors = template_size(t)
            pos, rot = place(facing, front, start, w)
            out.append(dict(quarter=quarter, label=f"{prefix}-{i:02d}", where=where, template=t, position=pos, rotation=rot,
                            footprint=footprint(pos, rot, w, d), doors=doors, facing=facing))
    for quarter, label, where, t, pos, rot in SINGLES:
        w, d, doors = template_size(t)
        out.append(dict(quarter=quarter, label=label, where=where, template=t, position=pos, rotation=rot,
                        footprint=footprint(pos, rot, w, d), doors=doors, facing=None))
    return out


def door_points(b):
    """Each door's world centre and a point 2 m inside it (EnterableDoorGenerator.TryInside's 2 m)."""
    pts = []
    for a, c, along, did in b["doors"]:
        ax, ay = a; cx, cy = c
        L = math.hypot(cx - ax, cy - ay)
        ux, uy = (cx - ax) / L, (cy - ay) / L
        mid = (ax + ux * along, ay + uy * along)
        cen = (sum(q[0] for q in b["fp_local"]) / 4, sum(q[1] for q in b["fp_local"]) / 4)
        nx, ny = -uy, ux
        if (cen[0] - mid[0]) * nx + (cen[1] - mid[1]) * ny < 0:
            nx, ny = -nx, -ny
        pts.append((did, world(b["position"], b["rotation"], mid),
                    world(b["position"], b["rotation"], (mid[0] + nx * 2, mid[1] + ny * 2))))
    return pts


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--svg", default=None)
    ap.add_argument("--table", action="store_true", help="print the placements by quarter as a markdown table")
    a = ap.parse_args()

    houses_path = os.path.join(TERRAIN, "demo-city.houses.json")
    world_path = os.path.join(TERRAIN, "demo-city.world.geojson")
    houses = json.load(open(houses_path, encoding="utf-8"))
    original = [f for f in houses["features"] if not f["properties"].get("quarter")]
    kept = [f for f in original if f["properties"]["label"] not in REPLACED]
    # (a replaced house is gone from the file once this has run — the script is idempotent)

    new = buildings()
    for b in new:
        w, d, _ = template_size(b["template"])
        b["fp_local"] = [(0, 0), (w, 0), (w, d), (0, d)]

    # the kept CE-4028 houses, as the same shape
    old = []
    for f in kept:
        p = f["properties"]; t = p["template"]; w, d, doors = template_size(t)
        pos = tuple(f["geometry"]["coordinates"][:2]); rot = p.get("rotation", 0.0)
        old.append(dict(quarter="existing", label=p["label"], template=t, position=pos, rotation=rot,
                        footprint=footprint(pos, rot, w, d), doors=doors, fp_local=[(0, 0), (w, 0), (w, d), (0, d)]))

    lanes, sidewalk, walks, juncs = load_net()
    world_json = json.load(open(world_path, encoding="utf-8"))
    data = [f for f in world_json["features"] if f["geometry"]["type"] == "Polygon"]
    data_polys = [[tuple(q[:2]) for q in f["geometry"]["coordinates"][0]] for f in data]

    errors = []
    allb = old + new
    for b in new:
        fp = b["footprint"]
        if any(q[0] < 0.5 or q[1] < 0.5 or q[0] > SIZE - 0.5 or q[1] > SIZE - 0.5 for q in fp):
            errors.append(f"{b['label']}: outside the terrain")
        for w, P in lanes:
            for i in range(len(P) - 1):
                if poly_seg_dist(fp, P[i], P[i + 1]) < w / 2 + 0.2:
                    errors.append(f"{b['label']}: on a lane near {P[i][0]:.0f},{P[i][1]:.0f}"); break
            else:
                continue
            break
        for J in juncs:
            if any(inside(J, q) for q in fp) or any(inside(fp, q) for q in J):
                errors.append(f"{b['label']}: on a junction"); break
        for D in data_polys:
            if any(inside(D, q) for q in fp) or any(inside(fp, q) for q in D):
                errors.append(f"{b['label']}: on a data building"); break
        x0, y0, x1, y1 = LANDING
        sq = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        if any(inside(sq, q) for q in fp) or any(inside(fp, q) for q in sq):
            errors.append(f"{b['label']}: in the landing square")
    for i, b in enumerate(allb):
        for c in allb[i + 1:]:
            grown = [(q[0], q[1]) for q in c["footprint"]]
            if any(inside(grown, q) for q in b["footprint"]) or any(inside(b["footprint"], q) for q in grown) \
               or min(seg_dist(q, b["footprint"][k], b["footprint"][(k + 1) % 4]) for q in grown for k in range(4)) < 1.0:
                if b["quarter"] != "existing" or c["quarter"] != "existing":
                    errors.append(f"{b['label']} and {c['label']} overlap or stand < 1 m apart")

    # shelter: every sidewalk point within SHELTER of a way in
    ways = [ins for b in allb for _, _, ins in door_points(b)]
    far = [p for p in sidewalk if 4 <= p[0] <= SIZE - 4 and 4 <= p[1] <= SIZE - 4
           and not any(math.hypot(p[0] - q[0], p[1] - q[1]) <= SHELTER for q in ways)]
    if far:
        errors.append(f"{len(far)} sidewalk points with no way in within {SHELTER} m, e.g. {far[:3]}")
    # every front door faces a sidewalk: the point 3 m outside the front door is within 6 m of a sidewalk point
    for b in new:
        for did, mid, ins in door_points(b):
            if did not in ("front", "gate") or b["template"] == "market-hall":
                continue
            out = (2 * mid[0] - ins[0] + (mid[0] - ins[0]) * 0.5, 2 * mid[1] - ins[1] + (mid[1] - ins[1]) * 0.5)
            on_plaza = SQUARE[0] <= out[0] <= SQUARE[2] and SQUARE[1] <= out[1] <= SQUARE[3]
            if not on_plaza and not any(seg_dist(out, P[k], P[k + 1]) <= 4.0 for P in walks for k in range(len(P) - 1)):
                errors.append(f"{b['label']}: its {did} door does not face a sidewalk")

    feats = []
    for f in kept:
        feats.append(f)
    for b in new:
        feats.append({"type": "Feature",
                      "properties": {"kind": "building", "template": b["template"], "label": b["label"], "baseZ": 0,
                                     "rotation": b["rotation"], "quarter": b["quarter"]},
                      "geometry": {"type": "Point", "coordinates": [round(b["position"][0], 2), round(b["position"][1], 2)]}})
    houses_out = dict(houses)
    houses_out["$comment"] = (houses["$comment"].split(" ⭐ UI-10")[0]
                              + f" ⭐ UI-10 (HANDOFF_Behaviors_To_Ui_Demo_City_Quarters, DESIGN_Urban_Battle_Demo §4a): demo-city's QUARTERS — "
                              f"{len(new)} buildings in residential, commercial and business quarters (the 'quarter' property), "
                              f"{len(REPLACED)} CE-4028 houses replaced; written by scripts/demo-city-quarters.py — edit the rows there, not here.")
    houses_out["features"] = feats
    world_out = dict(world_json)
    world_out["features"] = data + feats
    desc = world_json["hrot"]["description"]
    marker = " ⭐ UI-10"
    world_out["hrot"] = dict(world_json["hrot"])
    world_out["hrot"]["description"] = desc.split(marker)[0] + (
        f"{marker}: + demo-city's quarters ({len(new)} buildings, scripts/demo-city-quarters.py; {len(REPLACED)} CE-4028 houses replaced).")

    houses_text = json.dumps(houses_out, indent=1, ensure_ascii=False) + "\n"
    world_text = json.dumps(world_out, indent=4, ensure_ascii=False) + "\n"

    counts = ", ".join("%s %d" % (q, sum(1 for b in new if b["quarter"] == q)) for q in ("residential", "commercial", "business"))
    print(f"demo-city quarters: {len(new)} new buildings ({counts}), "
          f"{len(kept)} CE-4028 houses kept, {len(REPLACED)} replaced; {len(ways)} ways in; {len(sidewalk)} sidewalk points")
    if a.table:
        print("\n| quarter | street side | buildings (label: template) |\n|---|---|---|")
        groups = {}
        for b in new:
            groups.setdefault((b["quarter"], b["where"]), []).append(b)
        for (q, where), bs in groups.items():
            from collections import Counter
            cnt = Counter(b["template"] for b in bs)
            print(f"| {q} | {where} | `{bs[0]['label']}` … `{bs[-1]['label']}` — " + ", ".join(f"{n} × {t}" for t, n in cnt.items()) + " |")
    if a.svg:
        write_svg(a.svg, lanes, juncs, data_polys, old, new)
        print(f"plan -> {a.svg}")
    if errors:
        print("FENCES BROKEN:\n  " + "\n  ".join(errors[:40]))
        sys.exit(1)
    if a.check:
        stale = open(houses_path, encoding="utf-8").read() != houses_text or open(world_path, encoding="utf-8").read() != world_text
        if stale:
            print("demo-city.houses.json / demo-city.world.geojson are STALE — run scripts/demo-city-quarters.py")
            sys.exit(1)
        print("fences hold; files are current")
        return
    open(houses_path, "w", encoding="utf-8").write(houses_text)
    open(world_path, "w", encoding="utf-8").write(world_text)
    print(f"wrote {houses_path} and {world_path}")


COLOURS = {"residential": "#d9a441", "commercial": "#4a90c2", "business": "#7a5ea8", "existing": "#9aa09a"}


def write_svg(path, lanes, juncs, data_polys, old, new):
    S = 1.0
    def T(p): return (p[0] * S, (SIZE - p[1]) * S)
    def poly(P): return " ".join(f"{T(q)[0]:.1f},{T(q)[1]:.1f}" for q in P)
    o = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE * S + 230:.0f}" height="{SIZE * S:.0f}" viewBox="0 0 {SIZE * S + 230:.0f} {SIZE * S:.0f}" font-family="sans-serif">',
         f'<defs><clipPath id="box"><rect width="{SIZE * S:.0f}" height="{SIZE * S:.0f}"/></clipPath></defs>',
         f'<rect width="{SIZE * S:.0f}" height="{SIZE * S:.0f}" fill="#efece4"/>', '<g clip-path="url(#box)">']
    for J in juncs:
        o.append(f'<polygon points="{poly(J)}" fill="#9a9a9a"/>')
    for w, P in lanes:
        o.append(f'<polyline points="{poly(P)}" fill="none" stroke="#a5a5a5" stroke-width="{w * S:.1f}"/>')
    for D in data_polys:
        o.append(f'<polygon points="{poly(D)}" fill="#b9b2a6" stroke="#6d675d" stroke-width="0.6"/>')
    x0, y0, x1, y1 = LANDING
    o.append(f'<polygon points="{poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)])}" fill="none" stroke="#2e7d32" stroke-width="2" stroke-dasharray="5,3"/>')
    o.append(f'<text x="{T((x0 + 18, y1 - 30))[0]:.0f}" y="{T((x0, y1 - 30))[1]:.0f}" font-size="16" font-weight="bold" fill="#2e7d32">H</text>')
    for b in old + new:
        col = COLOURS[b["quarter"]]
        o.append(f'<polygon points="{poly(b["footprint"])}" fill="{col}" stroke="#333" stroke-width="0.4"/>')
    # the battle street
    o.append(f'<polyline points="{poly([(650, 0), (650, 400)])}" fill="none" stroke="#c62828" stroke-width="3" stroke-dasharray="10,5"/>')
    o.append(f'<text x="{T((606, 70))[0]:.0f}" y="{T((606, 70))[1]:.0f}" font-size="12" fill="#c62828" transform="rotate(-90 {T((606, 70))[0]:.0f} {T((606, 70))[1]:.0f})">BATTLE STREET 400 m</text>')
    o.append(f'<text x="{T((664, 476))[0]:.0f}" y="{T((664, 476))[1]:.0f}" font-size="11" fill="#1d5d8c">market square</text>')
    for name, p in {"Avenue W": (122, 760), "Avenue M": (414, 770), "Avenue E": (666, 770), "Street S": (20, 166), "Street M": (20, 416), "Street N": (20, 666)}.items():
        o.append(f'<text x="{T(p)[0]:.0f}" y="{T(p)[1]:.0f}" font-size="11" fill="#444">{name}</text>')
    o.append(f'<text x="{T((520, 6))[0]:.0f}" y="{T((520, 6))[1]:.0f}" font-size="11" fill="#c62828">▲ open south edge — the convoy and the jet come in here</text>')
    o.append("</g>")
    lx = SIZE * S + 12
    for i, (q, c) in enumerate(COLOURS.items()):
        n = sum(1 for b in (old if q == "existing" else new) if b["quarter"] == q)
        label = "kept CE-4028 house" if q == "existing" else q
        o.append(f'<rect x="{lx}" y="{20 + i * 22}" width="14" height="14" fill="{c}" stroke="#333" stroke-width="0.4"/>')
        o.append(f'<text x="{lx + 20}" y="{32 + i * 22}" font-size="12">{label} ({n})</text>')
    o.append(f'<rect x="{lx}" y="{20 + 4 * 22}" width="14" height="14" fill="#b9b2a6" stroke="#6d675d" stroke-width="0.6"/>')
    o.append(f'<text x="{lx + 20}" y="{32 + 4 * 22}" font-size="12">SUMO data building</text>')
    o.append(f'<line x1="{lx}" y1="{27 + 5 * 22}" x2="{lx + 14}" y2="{27 + 5 * 22}" stroke="#c62828" stroke-width="3" stroke-dasharray="5,3"/>')
    o.append(f'<text x="{lx + 20}" y="{32 + 5 * 22}" font-size="12">battle street</text>')
    o.append(f'<rect x="{lx}" y="{20 + 6 * 22}" width="14" height="14" fill="none" stroke="#2e7d32" stroke-width="2" stroke-dasharray="4,2"/>')
    o.append(f'<text x="{lx + 20}" y="{32 + 6 * 22}" font-size="12">H landing square 50 × 50 m</text>')
    o.append(f'<text x="{lx}" y="{32 + 8 * 22}" font-size="11" fill="#555">800 × 800 m, north up</text>')
    o.append("</svg>")
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    open(path, "w", encoding="utf-8").write("\n".join(o) + "\n")


if __name__ == "__main__":
    main()
