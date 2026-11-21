"""terrain_placement.py — the template-placement geometry shared by the terrain scripts.

Used by scripts/demo-city-quarters.py (demo-city's quarters) and scripts/terrain-scale.py (the scale terrain, CE-4363). One copy of
the rules: a template's size and doors, a placement's world footprint, and the clearance test against SUMO lanes and junctions.
Pure stdlib, deterministic.
"""
import json
import math
import os
import xml.etree.ElementTree as ET


def template_size(name, dirs):
    """(width, depth, ground-floor doors) of the template `name`, searched in `dirs` in order."""
    for d in dirs:
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
    raise SystemExit(f"template '{name}' not found in {', '.join(dirs)}")


def world(pos, rot, p):
    """A template-local point placed at `pos` rotated `rot` degrees (counter-clockwise)."""
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


def load_net(path, anchor):
    """A SUMO net's lanes [(width, points)], sidewalk points, walk polylines and junction shapes, in net − anchor metres."""
    def pts(s): return [(float(q.split(",")[0]) - anchor[0], float(q.split(",")[1]) - anchor[1]) for q in s.split()]
    net = ET.parse(path).getroot()
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


def on_lane(fp, lanes):
    """The first lane point a footprint stands on (closer than half the lane + 0.2 m — demo-city's fence), else None."""
    for w, P in lanes:
        for i in range(len(P) - 1):
            if poly_seg_dist(fp, P[i], P[i + 1]) < w / 2 + 0.2:
                return P[i]
    return None


def on_junction(fp, juncs):
    return any(any(inside(J, q) for q in fp) or any(inside(fp, q) for q in J) for J in juncs)
