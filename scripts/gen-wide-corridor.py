#!/usr/bin/env python3
"""gen-wide-corridor.py — writes assets/terrain/wide-corridor (CE-4672 / CE-4673; STEER_Coordinator_To_Terrain_Street_Fight_Corridor.md).

🔒 User, 2026-11-19: "a wide corridor (... maybe 30 m wide) surrounded by family houses not very dense" · "no railway needed just wide corridor".

The ground: 600 x 400 m, flat. A straight OPEN corridor of grass, 30 m wide (y 185..215), 480 m long (x 60..540), centreline y 200. On both sides
detached family houses (`house-2f`: two storeys, a front door on the corridor side, a back door, windows) on plots 26..38 m wide, about one plot in five
left empty, set back 6..12 m from the corridor; each plot's front boundary is a low brick garden wall (1.0 m), a wooden fence (1.4 m), a hedge (1.2 m) or
nothing, with a gate gap at the door; hedges / fences between the gardens; a shed in some back gardens. Every house is ENTERABLE (its template's doors).

Deterministic: one seed, so a re-run writes the same files. Run from the repo root:  python3 scripts/gen-wide-corridor.py
"""
import json
import os
import random

SEED = 4672
NAME = "wide-corridor"
OUT = os.path.join("assets", "terrain", NAME)
W, H = 600.0, 400.0
C_SOUTH, C_NORTH = 185.0, 215.0          # the corridor's edges (30 m)
X0, X1 = 60.0, 540.0                     # its length (480 m)
PLOT_DEPTH = 34.0                        # a garden's depth, from the corridor edge back

rnd = random.Random(SEED)
house = json.load(open(os.path.join("assets", "buildings", "house-2f.building.json")))
hw = max(p[0] for p in house["footprint"])        # 10
hd = max(p[1] for p in house["footprint"])        # 8
front = next(w for w in house["storeys"][0]["walls"] if w["from"] == [0, 0])
door = next(o for o in front["openings"] if o["kind"] == "door")
door_mid = door["at"] + door.get("width", 0.9) / 2.0   # along the front wall, local x

features = []


def feat(props, geom):
    features.append({"type": "Feature", "properties": props, "geometry": geom})


def line(kind, label, pts, height, material, thickness):
    feat({"kind": kind, "label": label, "height": height, "material": material, "thickness": thickness},
         {"type": "LineString", "coordinates": [[round(x, 2), round(y, 2)] for x, y in pts]})


def boundary(label, a, b, style):
    """A front / side boundary of one plot in one of the styles; returns nothing for 'none'."""
    if style == "wall":
        line("wall", label, [a, b], 1.0, "brick", 0.25)
    elif style == "fence":
        line("fence", label, [a, b], 1.4, "fence-wood", 0.04)
    elif style == "hedge":
        line("wall", label, [a, b], 1.2, "hedge", 0.8)


def front_with_gate(label, xa, xb, y, gate_x, style):
    if style == "none":
        return
    g0, g1 = gate_x - 0.8, gate_x + 0.8
    if g0 - xa > 0.5:
        boundary(label + " left", (xa, y), (g0, y), style)
    if xb - g1 > 0.5:
        boundary(label + " right", (g1, y), (xb, y), style)


feat({"kind": "surface", "surface": "open", "label": "The corridor"},
     {"type": "Polygon", "coordinates": [[[X0, C_SOUTH], [X1, C_SOUTH], [X1, C_NORTH], [X0, C_NORTH], [X0, C_SOUTH]]]})

houses = 0
for side in ("N", "S"):
    x = X0 + rnd.uniform(0, 8)
    i = 0
    while True:
        plot_w = rnd.uniform(26, 38)
        if x + plot_w > X1:
            break
        i += 1
        xa, xb = x, x + plot_w
        tag = f"{side}{i:02d}"
        empty = rnd.random() < 0.2
        setback = rnd.uniform(6, 12)
        hx = xa + rnd.uniform(4, plot_w - hw - 4)              # the house's west edge
        if side == "N":
            edge = C_NORTH + 0.5                              # the front boundary, just inside the plot
            hy = C_NORTH + setback                            # the front wall (rotation 0: the door faces south, onto the corridor)
            gate_x = hx + door_mid
            if not empty:
                feat({"kind": "building", "template": "house-2f", "label": f"House {tag}", "baseZ": 0, "rotation": 0},
                     {"type": "Point", "coordinates": [round(hx, 2), round(hy, 2)]})
            back = C_NORTH + PLOT_DEPTH
            shed = (round(hx + rnd.uniform(0, hw - 6), 2), round(hy + hd + 5, 2), 0)
        else:
            edge = C_SOUTH - 0.5
            hy = C_SOUTH - setback                            # rotation 180: the template turns about its origin, the front wall faces north
            gate_x = hx + hw - door_mid
            if not empty:
                feat({"kind": "building", "template": "house-2f", "label": f"House {tag}", "baseZ": 0, "rotation": 180},
                     {"type": "Point", "coordinates": [round(hx + hw, 2), round(hy, 2)]})
            back = C_SOUTH - PLOT_DEPTH
            shed = (round(hx + 6 + rnd.uniform(0, hw - 6), 2), round(hy - hd - 5, 2), 180)
        if not empty:
            houses += 1
            if rnd.random() < 0.4:
                feat({"kind": "building", "template": "shed", "label": f"Shed {tag}", "baseZ": 0, "rotation": shed[2]},
                     {"type": "Point", "coordinates": [shed[0], shed[1]]})
        style = rnd.choices(["wall", "fence", "hedge", "none"], [0.3, 0.35, 0.25, 0.1])[0]
        front_with_gate(f"Front {tag} {style}", xa + 0.5, xb - 0.5, edge, gate_x if not empty else (xa + xb) / 2, style)
        side_style = rnd.choices(["hedge", "fence", "none"], [0.5, 0.35, 0.15])[0]
        boundary(f"Side {tag} {side_style}", (xb, edge), (xb, back), side_style)
        x = xb

doc = {
    "type": "FeatureCollection",
    "hrot": {
        "schemaVersion": 1, "bounds": [0, 0, W, H], "groundZ": 0,
        "description": ("CE-4672 / CE-4673 street-fight ground (generated by scripts/gen-wide-corridor.py, seed 4672): a straight OPEN grass corridor "
                        "30 m wide (y 185..215) and 480 m long (x 60..540), centreline y 200, between detached two-storey family houses (house-2f, "
                        "enterable, front doors on the corridor) on 26..38 m plots, about one in five empty, set back 6..12 m; front boundaries of "
                        "low brick walls, fences or hedges with gate gaps; hedges / fences between gardens; some sheds. Local metres, X east, Y north, Z up."),
    },
    "features": features,
}
os.makedirs(OUT, exist_ok=True)
with open(os.path.join(OUT, f"{NAME}.world.geojson"), "w") as f:
    json.dump(doc, f, indent=1)
with open(os.path.join(OUT, "terrain.json"), "w") as f:
    json.dump({"schemaVersion": 2, "origin": {"lat": 52.52, "lon": 13.405, "alt": 0.0}, "name": NAME, "world": f"{NAME}.world.geojson"}, f, indent=2)
print(f"{OUT}: {houses} houses, {len(features)} features")
