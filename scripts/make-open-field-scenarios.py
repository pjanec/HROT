#!/usr/bin/env python3
"""CE-2493 (docs/designs/group-maneuvers/DESIGN_Open_Field_Suite.md) — writes the open-field suite's scenarios (of-*) and their checks.

The field: basic-desert x 60-440, y 440-580 (flat, empty there), all cover scenario-placed TerrainFeatures (§5): scattered low walls, cars,
sandbag walls, two tree clumps and a ditch on the south flank on the approach (x 110-250); the enemy line at x ~260 (foxholes + sandbags).
Run: python3 scripts/make-open-field-scenarios.py   (rewrites scenarios/demos/open-field/of-*/)
"""
import json, os

ROOT = os.path.join(os.path.dirname(__file__), '..', 'scenarios', 'demos', 'open-field')
EAST, WEST = [0, 0, 0, 1], [0, 0, 1, 0]   # heading 0° (facing +X) / 180°
FEATURE_TKB = 8818


def guid(series, n):
    return f"c249{series:04d}-0000-4000-8000-{n:012d}"


# ── the field ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
def feature(series, n, name, kind, at, template="", heading=0, pad=0, seed=0, points=None):
    e = {"TkbIdentity": {"TkbType": FEATURE_TKB},
         "TerrainFeature": {"Kind": kind, "Template": template, "HeadingDeg": heading, "Pad": pad, "Seed": seed},
         "SimTransform": {"Position": [at[0], at[1], 0], "Rotation": [0, 0, 0, 1]},
         "EntityInfo": {"Name": name, "ForceId": "Neutral"}}
    if points is not None:
        e["EditablePolyline"] = {"Points": [{"X": x, "Y": y} for x, y in points], "Version": 0}
    return {guid(series, n): e}


WALL, TREE_GROUP, CLUTTER, GROUND = 2, 15, 8, 13


def field(series, enemy_line=True):
    """§5 — the scattered cover on the approach; with enemy_line, the dug-in line at x ~260 (foxholes + sandbags)."""
    f, n = {}, 900
    def add(*a, **k):
        nonlocal n
        n += 1
        f.update(feature(series, n, *a, **k))
    # low walls (1 m, brick), a few metres long, scattered
    for i, (x, y, length) in enumerate([(128, 492, 14), (172, 532, 12), (206, 480, 13), (150, 560, 10), (226, 512, 8)]):
        add(f"Field wall {i + 1}", WALL, (x, y), template="brick", pad=1.0, points=[(0, 0), (length, 0)])
    # cars and sandbag walls
    for i, (x, y, h) in enumerate([(150, 518, 90), (196, 500, 30), (232, 546, 0), (118, 455, 70)]):
        add(f"Field car {i + 1}", CLUTTER, (x, y), template="car", heading=h)
    for i, (x, y, h) in enumerate([(120, 506, 90), (186, 462, 0), (238, 492, 90)]):
        add(f"Field sandbags {i + 1}", CLUTTER, (x, y), template="sandbag-wall", heading=h)
    # two tree clumps
    add("Tree clump north", TREE_GROUP, (165, 568), pad=6, seed=24930001, points=[(-8, -6), (8, -6), (8, 6), (-8, 6)])
    add("Tree clump south", TREE_GROUP, (222, 458), pad=5, seed=24930002, points=[(-7, -5), (7, -5), (7, 5), (-7, 5)])
    # the ditch on the south flank: a covered way toward the enemy's south end
    add("South ditch", GROUND, (110, 448), template="ditch", seed=24930003, points=[(0, 0), (60, 0), (120, 4), (150, 10)])
    if enemy_line:
        for i, (x, y) in enumerate([(262, 500), (266, 512), (262, 524)]):
            add(f"Foxhole {i + 1}", GROUND, (x, y), template="foxhole", seed=24930010 + i, points=[(0, 0)])
        for i, (x, y) in enumerate([(257, 500), (260, 512), (257, 524)]):
            add(f"Line sandbags {i + 1}", CLUTTER, (x, y), template="sandbag-wall", heading=90)
    return f


# ── units ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
def man(name, force, x, y, tkb, commander, lead=False, rot=EAST, hp=100):
    return {"SimTransform": {"Position": [x, y, 0], "Rotation": rot}, "Health": {"Current": hp, "Max": hp},
            "TkbIdentity": {"TkbType": tkb}, "EntityInfo": {"Name": name, "ForceId": force}, "TargetMemory": {"Entries": []},
            "UnitSubordinate": {"CommanderGuid": commander, "Designation": "SquadLeader" if lead else "Wingman"}}


def squad(series, base, name, force, men, behaviour=None, rot=EAST):
    """men: [(role, x, y, tkb)…] — the first is the leader."""
    sq = guid(series, base)
    ents = {sq: {"SimTransform": {"Position": [men[0][1], men[0][2], 0], "Rotation": rot}, "TkbIdentity": {"TkbType": 302},
                 "EntityInfo": {"Name": name, "ForceId": force}}}
    if behaviour:
        ents[sq]["Behavior"] = {**behaviour, "Origin": "Superior"}
    for i, (role, x, y, tkb) in enumerate(men):
        ents[guid(series, base + 1 + i)] = man(f"{name} {role}", force, x, y, tkb, sq, lead=(i == 0), rot=rot)
    return ents, sq


RIFLE, GRENADIER = 2002, 2004


def blue_squad(series, x0=80, y0=510, behaviour=None):
    men = [("Leader", x0, y0, RIFLE), ("Rifleman 1", x0 - 4, y0 + 5, RIFLE), ("Grenadier 2", x0 - 8, y0 + 10, GRENADIER),
           ("Rifleman 3", x0 - 4, y0 - 5, RIFLE), ("Grenadier 4", x0 - 8, y0 - 10, GRENADIER), ("Rifleman 5", x0 - 12, y0, RIFLE)]
    return squad(series, 100, "Blue Squad", "Friend", men, behaviour)


def red_fire_team(series):
    men = [("Leader", 266, 512, RIFLE), ("Rifleman 1", 262, 500, RIFLE), ("Rifleman 2", 262, 524, RIFLE)]
    return squad(series, 200, "Red Squad", "Hostile", men, {"Name": "SquadHold", "Params": {}}, rot=WEST)


# ── checks ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
BLUE, RED = "*Blue Squad ", "*Red Squad "


def never(name, cue, **kw):
    return {"name": name, "never": cue, **kw}


def write(name, what, judge, ents, checks):
    d = os.path.join(ROOT, name)
    os.makedirs(d, exist_ok=True)
    scen = {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
            "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "basic-desert",
                       "description": f"What: {what} Judge: {judge} Check: scripts/scenario-regression.sh {name}"},
            "entities": ents}
    with open(os.path.join(d, 'scenario.json'), 'w') as f:
        json.dump(scen, f, indent=2)
        f.write('\n')
    with open(os.path.join(d, 'checks.json'), 'w') as f:
        json.dump({"$doc": f"CE-2493 open-field suite (docs/designs/group-maneuvers/DESIGN_Open_Field_Suite.md §6) - {name}", **checks}, f, indent=2)
        f.write('\n')


# ── OF1 — a squad meets a dug-in fire team in a movement to contact ──────────────────────────────────────────────────────────────────
ents = field(1)
b, _ = blue_squad(1, behaviour={"Name": "InfantrySquadMove", "Params": {"Location": [420, 510, 0], "Speed": 2}})
r, _ = red_fire_team(1)
ents.update(b); ents.update(r)
write("of-squad-meets-fire-team",
      "CE-2493 open-field suite OF1 - on basic-desert's open field (x 60-440, y 440-580; scattered low walls, cars, sandbag walls, two tree clumps "
      "and a ditch on the south flank), a 6-man 'Blue Squad' moves east across the field (InfantrySquadMove to (420,510), a movement to contact) and "
      "meets a 3-man 'Red Squad' fire team dug in at x ~260 (foxholes + sandbags, SquadHold) at ~150 m.",
      "react to contact, then WIN and MOVE (ATP 3-21.8): Blue returns fire at once, every man is in cover within 20 s, nobody stands upright in the "
      "open in Red's sight, and the squad does NOT sit: it suppresses and moves (fire and movement, the south ditch is the covered flank) until the "
      "fire team is beaten within 300 s. Fail: the whole squad still for a minute while Red still fights (the ditch war), a man upright in the "
      "open in Red's sight 10 s, or the fight undecided.",
      ents,
      {"runFor": 360, "golden": False,
       "anchors": {"Red fires": {"on": "shot", "by": RED},
                   "Red beaten": {"on": "field", "who": RED, "path": "Health.Current", "op": "<=", "value": 25, "quantifier": "all"}},
       "expect": [
           {"name": "Red fires", "on": "shot", "by": RED},
           {"name": "Blue fires", "on": "shot", "by": BLUE},
           {"name": "L2 Blue returns fire within 5 s of Red's first round", "on": "shot", "by": BLUE, "after": "Red fires", "before": "Red fires+5"},
           {"name": "L2 every Blue man in the fight is in cover (a cover point within 2.5 m) within 20 s of Red's first round",
            "on": "cover", "who": BLUE, "is": "in", "m": 2.5, "quantifier": "all", "fighting": True, "after": "Red fires", "before": "Red fires+20"},
           never("L1 nobody of Blue in the fight stands upright in the OPEN in a Red man's sight 10 s once the shooting starts",
                 {"on": "exposed", "who": BLUE, "by": RED, "upright": True, "fighting": True, "for": 10}, after="Red fires"),
           never("L1 the DITCH WAR: every Blue man in the fight still (within 2 m) for 60 s while Red still fights",
                 {"on": "still", "who": BLUE, "m": 2, "for": 60, "quantifier": "all", "fighting": True}, after="Red fires", before="Red beaten"),
           never("L1 Blue is no knot (three or more within 3 m) for 10 s after the first half minute",
                 {"on": "bunched", "who": BLUE, "m": 3, "fighting": True, "for": 10, "men": 3}, after="Red fires+30"),
           never("L1 no Blue round hits a Blue man", {"on": "shot", "by": BLUE, "hit": BLUE}),
           {"name": "L3 the fire team is BEATEN (every Red man out of the fight) within 300 s of its first round",
            "on": "field", "who": RED, "path": "Health.Current", "op": "<=", "value": 25, "quantifier": "all", "after": "Red fires", "before": "Red fires+300"},
       ]})

print("wrote of-squad-meets-fire-team")
