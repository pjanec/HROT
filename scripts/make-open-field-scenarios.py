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

# ── platoons ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
def blue_platoon(series, behaviour, x0=82, ys=(470, 510, 550)):
    """'Blue Platoon' (305) over 'Blue Squad 1-3' (302), 5 men each (leader, 2 riflemen, 2 grenadiers), side by side north of y 470."""
    pl = guid(series, 50)
    ents = {pl: {"SimTransform": {"Position": [x0, ys[1], 0], "Rotation": EAST}, "TkbIdentity": {"TkbType": 305},
                 "EntityInfo": {"Name": "Blue Platoon", "ForceId": "Friend"}, "Behavior": {**behaviour, "Origin": "Superior"}}}
    for k, y in enumerate(ys):
        men = [("Leader", x0, y, RIFLE), ("Rifleman 1", x0 - 4, y + 4, RIFLE), ("Grenadier 2", x0 - 8, y + 8, GRENADIER),
               ("Rifleman 3", x0 - 4, y - 4, RIFLE), ("Grenadier 4", x0 - 8, y - 8, GRENADIER)]
        sq, sg = squad(series, 100 + 20 * k, f"Blue Squad {k + 1}", "Friend", men)
        sq[sg]["UnitSubordinate"] = {"CommanderGuid": pl, "Designation": "Wingman"}
        ents.update(sq)
    return ents


def red_defending_squad(series):
    men = [("Leader", 266, 512, RIFLE), ("Rifleman 1", 262, 500, RIFLE), ("Rifleman 2", 262, 524, RIFLE),
           ("Rifleman 3", 270, 494, RIFLE), ("Grenadier 4", 270, 530, GRENADIER), ("Rifleman 5", 274, 512, RIFLE)]
    return squad(series, 200, "Red Squad", "Hostile", men,
                 {"Name": "SquadDefend", "Params": {"Position": [266, 512, 0], "SearchRadius": 25, "Speed": 2}}, rot=WEST)


PLATOON_ANCHORS = {"Red fires": {"on": "shot", "by": RED},
                   "Red beaten": {"on": "field", "who": RED, "path": "Health.Current", "op": "<=", "value": 25, "quantifier": "all"}}


def platoon_never_rows():
    return [
        never("L1 the DITCH WAR: every Blue man in the fight still (within 2 m) for 60 s while Red still fights",
              {"on": "still", "who": BLUE, "m": 2, "for": 60, "quantifier": "all", "fighting": True}, after="Red fires", before="Red beaten"),
        never("L1 nobody of Blue in the fight stands upright in the OPEN in a Red man's sight 10 s once the shooting starts (CE-2494)",
              {"on": "exposed", "who": BLUE, "by": RED, "upright": True, "fighting": True, "for": 10}, after="Red fires"),
        never("L1 no Blue round hits a Blue man", {"on": "shot", "by": BLUE, "hit": BLUE}),
    ]


# ── OF2 — a platoon attacks a squad holding the line ────────────────────────────────────────────────────────────────────────────────
ents = field(2)
for i, (x, y) in enumerate([(270, 494), (270, 530), (274, 512)]):   # three more foxholes for the 6-man line
    ents.update(feature(2, 960 + i, f"Foxhole {4 + i}", GROUND, (x, y), template="foxhole", seed=24930020 + i, points=[(0, 0)]))
ents.update(blue_platoon(2, {"Name": "PlatoonAttack", "Params": {"Position": [266, 512, 0], "SearchRadius": 20, "Preset": "AttackPosition", "Speed": 2}}))
r, _ = red_defending_squad(2)
ents.update(r)
write("of-platoon-attack",
      "CE-2493 open-field suite OF2 - on basic-desert's open field (scattered walls, cars, sandbags, tree clumps, a south ditch), 'Blue Platoon' "
      "(3 squads of 5) at x ~80 attacks (PlatoonAttack, AttackPosition round (266,512)) a 6-man 'Red Squad' defending the line at x 260-275 "
      "(foxholes + sandbags, SquadDefend) ~185 m east.",
      "the platoon attack (ATP 3-21.8): a squad SUPPORTS by fire from cover while another MANOEUVRES (bounds / fire and movement, the south ditch "
      "is the covered flank), the assault goes through, the line is taken (Red beaten, a Blue man on it) within 420 s. Fail: every Blue man "
      "still a minute while Red still fights (the ditch war), no squad supporting or none manoeuvring, or the line not taken.",
      ents,
      {"runFor": 480, "golden": False, "anchors": PLATOON_ANCHORS,
       "expect": [
           {"name": "Red fires", "on": "shot", "by": RED},
           {"name": "Blue fires", "on": "shot", "by": BLUE},
           {"name": "L2 a squad SUPPORTS by fire (SupportByFire)", "on": "task", "who": "*Blue Squad", "is": "SupportByFire"},
           {"name": "L2 a squad MANOEUVRES (BoundingOverwatch)", "on": "task", "who": "*Blue Squad", "is": "BoundingOverwatch"},
           *platoon_never_rows(),
           {"name": "L3 the line is taken: every Red man out of the fight within 420 s of Red's first round",
            "on": "field", "who": RED, "path": "Health.Current", "op": "<=", "value": 25, "quantifier": "all", "after": "Red fires", "before": "Red fires+420"},
           {"name": "L3 ...and a Blue man stands on it (within 15 m of (266,512))", "on": "near", "who": BLUE, "point": [266, 512], "within": 15},
       ]})

# ── OF4 — a platoon bounding across the field is fired on from its north flank ───────────────────────────────────────────────────────
ents = field(4, enemy_line=False)
ents.update(feature(4, 970, "Ambush clump", TREE_GROUP, (230, 600), pad=6, seed=24930030, points=[(-9, -6), (9, -6), (9, 6), (-9, 6)]))
for i, x in enumerate((224, 236)):
    ents.update(feature(4, 971 + i, f"Ambush sandbags {i + 1}", CLUTTER, (x, 592), template="sandbag-wall", heading=0))
ents.update(blue_platoon(4, {"Name": "PlatoonBound", "Params": {"SupportPoint": [150, 510, 0], "Objective": [400, 510, 0], "ArrivalRadius": 10, "SitrepEverySeconds": 15}}))
men = [("Leader", 230, 596, RIFLE), ("Rifleman 1", 224, 595, RIFLE), ("Rifleman 2", 236, 595, RIFLE)]
r, _ = squad(4, 200, "Red Squad", "Hostile", men, {"Name": "SquadHold", "Params": {}}, rot=[0, 0, -0.7071068, 0.7071068])
ents.update(r)
write("of-platoon-flank-contact",
      "CE-2493 open-field suite OF4 - on basic-desert's open field, 'Blue Platoon' (3 squads of 5) bounds east across it (PlatoonBound: support "
      "point (150,510), objective (400,510)); a 3-man 'Red Squad' fire team in a tree clump north of the field (230,600; sandbags, SquadHold) opens "
      "fire on the nearest squad from ~80 m.",
      "react to contact at PLATOON level (ATP 3-21.8): the squad in contact returns fire and gets into cover; the platoon does NOT freeze - another "
      "squad moves to support or to the flank, and the fire team is beaten within 300 s. Fail: every Blue man still a minute while Red still fights "
      "(the whole platoon holding), or the fire team never beaten.",
      ents,
      {"runFor": 360, "golden": False, "anchors": PLATOON_ANCHORS,
       "expect": [
           {"name": "Red fires", "on": "shot", "by": RED},
           {"name": "Blue fires", "on": "shot", "by": BLUE},
           {"name": "L2 Blue returns fire within 5 s of Red's first round", "on": "shot", "by": BLUE, "after": "Red fires", "before": "Red fires+5"},
           *platoon_never_rows(),
           {"name": "L3 the fire team is beaten within 300 s of its first round",
            "on": "field", "who": RED, "path": "Health.Current", "op": "<=", "value": 25, "quantifier": "all", "after": "Red fires", "before": "Red fires+300"},
       ]})

# ── OF3 — a squad BOUNDS across the field, then meets the fire team ──────────────────────────────────────────────────────────────────
ents = field(3)
b, _ = blue_squad(3, behaviour={"Name": "BoundingOverwatch", "Params": {"Objective": [420, 510, 0], "BoundMetres": 30, "Speed": 2.5, "ArrivalRadius": 10}})
r, _ = red_fire_team(3)
ents.update(b); ents.update(r)
write("of-squad-bounds-to-contact",
      "CE-2493 open-field suite OF3 - on basic-desert's open field, a 6-man 'Blue Squad' BOUNDS east across it (BoundingOverwatch to (420,510), "
      "30 m bounds) and meets the 3-man 'Red Squad' fire team dug in at x ~260 (foxholes + sandbags, SquadHold).",
      "bounding overwatch, then react to contact (ATP 3-21.8): before contact each bound ENDS at cover (the overwatching half watches from cover, "
      "never from the open); in contact, return fire at once and fight forward by fire and movement until the fire team is beaten within 300 s. "
      "Fail: a man left in the open 15 s before contact (a bound that ended in the open), a man upright in the open in Red's sight 10 s, the "
      "ditch war, or the fight undecided.",
      ents,
      {"runFor": 360, "golden": False,
       "anchors": {**PLATOON_ANCHORS,   # ⚠ the start line is open by design (§5): the bound-ends-at-cover row starts once the squad is in the cover band
                   "in the cover band": {"on": "near", "who": "Blue Squad Leader", "point": [125, 510], "within": 8}},
       "expect": [
           {"name": "Red fires", "on": "shot", "by": RED},
           {"name": "Blue fires", "on": "shot", "by": BLUE},
           {"name": "L2 Blue returns fire within 5 s of Red's first round", "on": "shot", "by": BLUE, "after": "Red fires", "before": "Red fires+5"},
           never("L1 in the scattered cover, before contact, no Blue man stays OUT of cover (no cover point within 2.5 m) for 15 s - each bound ends at cover",
                 {"on": "cover", "who": BLUE, "is": "open", "m": 2.5, "fighting": True, "for": 15}, after="in the cover band", before="Red fires"),
           *platoon_never_rows(),
           {"name": "L3 the fire team is beaten within 300 s of its first round",
            "on": "field", "who": RED, "path": "Health.Current", "op": "<=", "value": 25, "quantifier": "all", "after": "Red fires", "before": "Red fires+300"},
       ]})

# ── OF5 — a squad meets a much stronger force: it must BREAK CONTACT, cover to cover ──────────────────────────────────────────────────
ents = field(5)
for i, (x, y) in enumerate([(270, 494), (270, 530), (274, 512), (262, 482), (262, 542)]):
    ents.update(feature(5, 960 + i, f"Foxhole {4 + i}", GROUND, (x, y), template="foxhole", seed=24930040 + i, points=[(0, 0)]))
b, _ = blue_squad(5, behaviour={"Name": "InfantrySquadMove", "Params": {"Location": [420, 510, 0], "Speed": 2}})
ents.update(b)
MG = 2012
for k, (yc, mg_y) in enumerate([(497, 494), (527, 530)]):
    men = [("Leader", 266, yc, RIFLE), ("Machine gunner", 270, mg_y, MG), ("Rifleman 1", 262, yc - 9, RIFLE), ("Rifleman 2", 262, yc + 9, RIFLE),
           ("Rifleman 3", 274, yc - 4, RIFLE), ("Rifleman 4", 274, yc + 4, RIFLE)]
    r, _ = squad(5, 200 + 20 * k, f"Red Squad {k + 1}", "Hostile", men,
                 {"Name": "SquadDefend", "Params": {"Position": [266, yc, 0], "SearchRadius": 20, "Speed": 2}}, rot=WEST)
    ents.update(r)
write("of-squad-outmatched",
      "CE-2493 open-field suite OF5 - on basic-desert's open field, a 6-man 'Blue Squad' moves east across it (InfantrySquadMove, a movement to "
      "contact) and meets 'Red Squad 1' and 'Red Squad 2' (12 men, two machine guns) defending the line at x 260-275 behind foxholes and sandbags.",
      "the COUNTERWEIGHT (ATP 3-21.8 break contact): outmatched 2 : 1 by men and by machine guns, the squad returns fire, then BREAKS CONTACT - "
      "suppressing and withdrawing cover to cover out of Red's sight - and never assaults or closes on the line. Fail: an assault on the line, "
      "a man upright in the open in Red's sight 10 s, or Blue still in Red's sight a minute after contact.",
      ents,
      {"runFor": 240, "golden": False, "anchors": {"Red fires": {"on": "shot", "by": RED}},
       "expect": [
           {"name": "Red fires", "on": "shot", "by": RED},
           {"name": "Blue fires", "on": "shot", "by": BLUE},
           {"name": "L2 Blue BREAKS CONTACT within 30 s of Red's first round", "on": "task", "who": "Blue Squad", "is": "BreakContact",
            "after": "Red fires", "before": "Red fires+30"},
           never("L1 Blue never ASSAULTS the line (SuppressAndManeuver)", {"on": "task", "who": "Blue Squad", "is": "SuppressAndManeuver"}),
           never("L1 no Blue man closes within 80 m of the line", {"on": "near", "who": BLUE, "point": [266, 512], "within": 80}),
           never("L1 nobody of Blue in the fight stands upright in the OPEN in a Red man's sight 10 s once the shooting starts (CE-2494)",
                 {"on": "exposed", "who": BLUE, "by": RED, "upright": True, "fighting": True, "for": 10}, after="Red fires"),
           never("L3 Blue gets out of Red's sight: from 60 s after Red's first round no Blue man stays in a Red man's sight 10 s",
                 {"on": "exposed", "who": BLUE, "by": RED, "fighting": True, "for": 10}, after="Red fires+60"),
       ]})

print("wrote of-squad-meets-fire-team, of-platoon-attack, of-platoon-flank-contact, of-squad-bounds-to-contact, of-squad-outmatched")
