#!/usr/bin/env python3
"""CE-2476 (docs/DESIGN_Thermal_And_Acoustic_Sensing.md §8.6e) — writes the perception suite's scenarios (pe-*) and their checks.

The listeners only HOLD (InertSop, HoldFire, StayOnTask): what is judged is what they BELIEVE (the `perceives` cue), never tactics.
Run: python3 scripts/make-perception-scenarios.py   (rewrites scenarios/demos/sensing-and-weather/pe-*/)
"""
import json, os

ROOT = os.path.join(os.path.dirname(__file__), '..', 'scenarios', 'demos', 'sensing-and-weather')
NORTH = [0, 0, 0.7071068, 0.7071068]
PASSIVE = {"Roe": {"Fire": "HoldFire", "Reactions": "StayOnTask", "SetBy": "Operator", "ForceLevel": "Lethal"},
           "Sop": {"Name": "InertSop", "Params": {}, "Origin": "Superior"}}


def guid(series, n):
    return f"c247{series:04d}-0000-4000-8000-{n:012d}"


def man(name, force, x, y, tkb=2002, hp=1000, extra=None):
    e = {"SimTransform": {"Position": [x, y, 0], "Rotation": NORTH}, "Health": {"Current": hp, "Max": hp},
         "TkbIdentity": {"TkbType": tkb}, "EntityInfo": {"Name": name, "ForceId": force}, "TargetMemory": {"Entries": []}}
    e.update(extra or {})
    return e


def squad_of_ears(series, ears, base=100):
    """Blue Squad (Friend) of passive listeners at `ears` [(x, y)…]; returns (entities, squad guid)."""
    sq = guid(series, base)
    # ⭐ the squad itself is passive too: without it its own standing behaviour moved the "listeners" (📐 P7: an ear walked 25 m to the corner
    #   and kept SEEING the hidden man; pe-house-shot's ears wandered the corridor) — perception is judged, never tactics
    ents = {sq: {"SimTransform": {"Position": [ears[0][0], ears[0][1], 0], "Rotation": NORTH}, "TkbIdentity": {"TkbType": 302},
                 "EntityInfo": {"Name": "Blue Squad", "ForceId": "Friend"}, **PASSIVE}}
    for i, (x, y) in enumerate(ears):
        ents[guid(series, base + 1 + i)] = man(f"Blue Squad Ear {i + 1}", "Friend", x, y, extra={
            **PASSIVE, "UnitSubordinate": {"CommanderGuid": sq, "Designation": "SquadLeader" if i == 0 else "Wingman"}})
    return ents, sq


def shooter(series, n, name, x, y, target, stay=False):
    """stay: his reactions do not take him off the task (📐 P2: seeing the listeners he went to FightFromCover and never fired at his target)."""
    extra = {"Behavior": {"Name": "FireAtTarget", "Params": {"targetNetworkId": target}, "Origin": "Superior"}}
    if stay: extra["Roe"] = {"Fire": "FireAtWill", "Reactions": "StayOnTask", "SetBy": "Operator", "ForceLevel": "Lethal"}
    return {guid(series, n): man(name, "Hostile", x, y, hp=1000, extra=extra)}


def target(series, n, x, y):
    return {guid(series, n): man("Blue Target", "Friend", x, y, tkb=2003, hp=100000, extra=PASSIVE)}


def per_man(metric, op, value, **kw):
    return {"on": "perceives", "who": "*Blue Squad Ear", "truth": "*Red", "metric": metric, "op": op, "value": value, **kw}


def squad(metric, op, value, **kw):
    return {"on": "perceives", "who": "Blue Squad", "squad": True, "truth": "*Red", "metric": metric, "op": op, "value": value, **kw}


def write(name, terrain, what, judge, ents, checks):
    d = os.path.join(ROOT, name)
    os.makedirs(d, exist_ok=True)
    scen = {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
            "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": terrain,
                       "description": f"What: {what} Judge: {judge} Check: scripts/scenario-regression.sh {name}"},
            "entities": ents}
    with open(os.path.join(d, 'scenario.json'), 'w') as f:
        json.dump(scen, f, indent=2)
        f.write('\n')
    with open(os.path.join(d, 'checks.json'), 'w') as f:
        json.dump({"$doc": f"CE-2476 perception suite (Sensing §8.6e) - {name}", **checks}, f, indent=2)
        f.write('\n')


FOUR_EARS = [(60, 225), (90, 215), (120, 225), (150, 220)]   # 175-190 m south of the shooter: beyond sight (150 m); a rifle is heard far beyond (CE-2480: a LOUD class, 1.5 km)

# ── P1 — one hidden shooter, four ears ────────────────────────────────────────────────────────────────────────────────
ents, _ = squad_of_ears(1, FOUR_EARS)
ents.update(target(1, 300, 100, 460))
ents.update(shooter(1, 200, "Red Shooter", 100, 400, guid(1, 300)))
write("pe-one-shot-four-ears", "basic-desert",
      "CE-2476 perception suite P1 (Sensing §8.6e) - on basic-desert one hidden 'Red Shooter' (100,400) fires at a 'Blue Target' 60 m north of him; "
      "four passive Blue listeners (the 'Blue Squad', hold fire, stay on task) stand 175-190 m south - beyond sight (150 m), inside a soldier's ear (200 m).",
      "every listener believes ONE enemy where the shooter is (matched 1, no phantom, no duplicate), and so does the squad's pool. "
      "Fail: a man or the squad counts the one shooter twice, or believes an enemy where none is.",
      ents,
      {"runFor": 40, "golden": False,
       "anchors": {"first shot": {"on": "shot", "by": "Red Shooter"}},
       "expect": [
           {"name": "the shooter fires", "on": "shot", "by": "Red Shooter", "n": 3},
           {"name": "every ear holds the shooter (matched 1, 5 s)", **per_man("matched", "==", 1, quantifier="all", **{"for": 5}), "after": "first shot"},
           {"name": "no ear ever counts him twice", "never": per_man("duplicate", ">", 0), "after": "first shot"},
           {"name": "no ear ever believes an enemy where none is", "never": per_man("phantom", ">", 0), "after": "first shot"},
           {"name": "the squad's pool holds him once (5 s)", **squad("matched", "==", 1, **{"for": 5}), "after": "first shot"},
           {"name": "the squad never counts him twice", "never": squad("duplicate", ">", 0), "after": "first shot+2"},
           {"name": "the squad never holds a phantom", "never": squad("phantom", ">", 0), "after": "first shot"},
       ]})

# ── P3 — the shooter in plain view of two ears ────────────────────────────────────────────────────────────────────────
ents, _ = squad_of_ears(3, [(90, 150), (110, 150), (50, 80), (150, 80)])   # ears 1-2 ~100 m from him (in sight), 3-4 ~177 m (heard only, inside the 200 m ear)
ents.update(target(3, 300, 100, 310))
ents.update(shooter(3, 200, "Red Shooter", 100, 250, guid(3, 300)))
write("pe-seen-shooter", "basic-desert",
      "CE-2476 perception suite P3 (Sensing §8.6e; CE-2474 attribution) - on basic-desert a 'Red Shooter' (100,250) fires at a 'Blue Target' 60 m north; "
      "two passive Blue listeners stand 100 m south of him in plain view, two more ~177 m off (heard only).",
      "the two who SEE him hold him as one identified contact - his shots are his, no heard phantom beside him; the squad's pool holds one enemy. "
      "Fail: a seeing man also holds a heard contact for the same shooter (a duplicate) or one elsewhere (a phantom).",
      ents,
      {"runFor": 40, "golden": False,
       "anchors": {"first shot": {"on": "shot", "by": "Red Shooter"}},
       "expect": [
           {"name": "the shooter fires", "on": "shot", "by": "Red Shooter", "n": 3},
           {"name": "the seeing ears hold him once (5 s)", "on": "perceives", "who": "*Blue Squad Ear 1", "truth": "*Red", "metric": "matched", "value": 1,
            "for": 5, "after": "first shot"},
           {"name": "  ... and so does Ear 2", "on": "perceives", "who": "*Blue Squad Ear 2", "truth": "*Red", "metric": "matched", "value": 1,
            "for": 5, "after": "first shot"},
           {"name": "no ear ever counts him twice", "never": per_man("duplicate", ">", 0), "after": "first shot"},
           {"name": "no ear ever believes an enemy where none is", "never": per_man("phantom", ">", 0), "after": "first shot"},
           {"name": "the squad never counts him twice", "never": squad("duplicate", ">", 0), "after": "first shot+2"},
           {"name": "the squad's pool holds him once at the end", **squad("matched", "==", 1), "atEnd": True},
       ]})

# ── P4 — two shooters 30 m apart (a fire team's spacing) ──────────────────────────────────────────────────────────────
ents, _ = squad_of_ears(4, FOUR_EARS)
ents.update(target(4, 300, 100, 460))
ents.update(shooter(4, 200, "Red Shooter A", 85, 400, guid(4, 300)))
ents.update(shooter(4, 201, "Red Shooter B", 115, 400, guid(4, 300)))
write("pe-two-shooters", "basic-desert",
      "CE-2476 perception suite P4 (Sensing §8.6e) - the guard against OVER-merging: on basic-desert two hidden Red riflemen 30 m apart (85,400 / 115,400) fire "
      "at a 'Blue Target'; four passive Blue listeners stand 175-190 m south (about 10 degrees apart as they hear them).",
      "the squad believes TWO enemies (matched 2) - never one; no phantom. Fail: the two shooters are merged into one contact.",
      ents,
      {"runFor": 40, "golden": False,
       "anchors": {"both fire": {"on": "shot", "by": "Red Shooter B"}},
       "expect": [
           {"name": "shooter A fires", "on": "shot", "by": "Red Shooter A", "n": 3},
           {"name": "shooter B fires", "on": "shot", "by": "Red Shooter B", "n": 3},
           {"name": "the squad's pool holds BOTH shooters (5 s)", **squad("matched", "==", 2, **{"for": 5}), "after": "both fire"},
           {"name": "the squad never holds a phantom", "never": squad("phantom", ">", 0), "after": "both fire"},
           {"name": "no ear ever believes an enemy where none is", "never": per_man("phantom", ">", 0), "after": "both fire"},
       ]})

# ── P5 — shelled, no enemy near ───────────────────────────────────────────────────────────────────────────────────────
ents, _ = squad_of_ears(5, [(320, 280), (340, 270), (360, 280), (340, 300)])   # around the aim point (340,330), 30-60 m off
bat = guid(5, 200)
ents[bat] = {"SimTransform": {"Position": [-3800, 0, 0], "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": 304},
             "EntityInfo": {"Name": "Red Battery", "ForceId": "Hostile"},
             "Behavior": {"Name": "ArtilleryUnitStrike", "Params": {"Location": [340, 330, 0], "Control": "TimeOnTarget", "Sheaf": "Parallel", "Rounds": 6},
                          "Origin": "Superior"}}
for i, y in enumerate((-40, 0, 40)):
    ents[guid(5, 201 + i)] = {"SimTransform": {"Position": [-3500, y, 0], "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": 107},
                              "EntityInfo": {"Name": f"Red Gun {i + 1}", "ForceId": "Hostile"},
                              "UnitSubordinate": {"CommanderGuid": bat, "Designation": "SquadLeader"}}
write("pe-shelled", "indirect-fire-range",
      "CE-2476 perception suite P5 (Sensing §8.6e; R-2071) - on indirect-fire-range a 'Red Battery' (three guns, 3.8 km back) drops 6 rounds 30-60 m from four "
      "passive Blue listeners; no enemy is anywhere near them.",
      "a burst says where a round LANDED, never where an enemy is: no listener and not the squad ever believes an enemy near them (no phantom) - the guns, "
      "if heard at all, are believed where they are. Fail: a burst becomes an enemy contact.",
      ents,
      {"runFor": 120, "golden": False,
       "anchors": {"first burst": {"on": "blast", "by": "*Red Gun"}},
       "expect": [
           {"name": "rounds land", "on": "blast", "by": "*Red Gun", "n": 3},
           {"name": "no ear ever believes an enemy where none is", "never": per_man("phantom", ">", 0), "after": "first burst"},
           {"name": "the squad never holds a phantom", "never": squad("phantom", ">", 0), "after": "first burst"},
       ]})

# ── P2 — a shot inside a house, heard from four sides (bt-range House A: house-2f at (100,100), 10 x 8 m; the inner wall at x 105 with the
#    hall door OPEN at (105,106.6); the front door (104.2,100) closed; an east window at (110,103.6)) ─────────────────────────────────────────
HOUSE_SHOOTER = (102, 103)
ents, _ = squad_of_ears(2, [(108, 102), (116, 103.6), (102, 93), (104, 114)])   # east room · outside the east window · south · north
ents.update(target(2, 300, 103, 107))
ents.update(shooter(2, 200, "Red Shooter", *HOUSE_SHOOTER, guid(2, 300), stay=True))
write("pe-house-shot", "bt-range",
      "CE-2476 / CE-2475 perception suite P2 (Sensing §8.6e) - on bt-range a 'Red Shooter' in House A's west room (102,103) fires at a 'Blue Target' "
      "in the same room; four passive Blue listeners hear him from the east room (one inner wall, the hall door open beside it), outside the east "
      "window, south of the house and north of it - each by a different way (walls, the door, the window).",
      "the squad believes ONE enemy (matched 1, no duplicate) though every ear heard him by a different way; a hearing that came through an opening "
      "is a place to look (D-8), a straight one a place he is (CE-2479: a concrete inner wall costs a shot ~24 dB, so through the open hall door is the way). "
      "Fail: one shot is two enemies in the squad, or a straight hearing puts an enemy where none is.",
      ents,
      {"runFor": 40, "golden": False,
       "anchors": {"first shot": {"on": "shot", "by": "Red Shooter"}},
       "expect": [
           {"name": "the shooter fires", "on": "shot", "by": "Red Shooter", "n": 3},
           {"name": "every ear hears him (contacts >= 1, 5 s)", **per_man("contacts", ">=", 1, quantifier="all", **{"for": 5}), "after": "first shot"},
           {"name": "the squad's pool holds him once (5 s)", **squad("matched", "==", 1, **{"for": 5}), "after": "first shot"},
           {"name": "the squad never counts him twice", "never": squad("duplicate", ">", 0), "after": "first shot+2"},
           {"name": "no straight hearing believes an enemy where none is (a sound heard through an opening says where to LOOK, D-8)", "never":
               {**per_man("phantom", ">", 0), "through": False}, "after": "first shot"},
       ]})

# ── P6 — steps round a corner (bt-range House A: x 100-110, y 100-108): a man walks along the house's far (north) side; two listeners south of it,
#    the house always between them ⇒ heard only round its corners (D-8: where to look), never seen, never a straight hearing ───────────────────────
def walker(series, n, name, x, y, to, speed=1.2):
    return {guid(series, n): man(name, "Hostile", x, y, extra={
        "Roe": PASSIVE["Roe"], "Sop": PASSIVE["Sop"],
        "Behavior": {"Name": "AdvanceTo", "Params": {"Objective": [to[0], to[1], 0], "Speed": speed, "ArrivalRadius": 1.5}, "Origin": "Superior"}})}

#    📐 a walker's steps carry ~6 m at 1.2 m/s (scaled by speed): he JOGS (2.5 m/s) along the north wall and stops before the NE corner; the two
#    listeners stand just east of the house, 8-12 m round that corner — never in sight of him (every line crosses the house)
ents, _ = squad_of_ears(6, [(112, 103), (112, 104.5)])
ents.update(walker(6, 200, "Red Walker", 96, 110.5, (106, 110.5), speed=2.5))
write("pe-steps-round-corner", "bt-range",
      "CE-2476 perception suite P6 (Sensing §8.6e) - on bt-range a 'Red Walker' jogs west to east along the north wall of House A and stops short of "
      "its north-east corner; two passive Blue listeners stand just east of the house, 8-12 m round that corner; the house is always between them.",
      "his steps are heard, but only ROUND the house's corners: a place to look (D-8), never a place he is - no listener ever holds a straight "
      "hearing or a sighting of him. Fail: nobody hears him, or a listener believes a spot for him through the house.",
      ents,
      {"runFor": 30, "golden": False,
       "anchors": {"behind": {"on": "near", "who": "Red Walker", "point": [104, 110.5], "within": 3}},
       "expect": [
           {"name": "he jogs along the far wall", "on": "near", "who": "Red Walker", "point": [104, 110.5], "within": 3},
           {"name": "a listener hears his steps", **per_man("contacts", ">=", 1, quantifier="any")},
           {"name": "no listener ever holds a SPOT for him (no straight hearing, no sighting: through the house he is only a place to look)",
            "never": {**per_man("contacts", ">", 0), "through": False}},
       ]})

# ── P7 — seen, then hidden: a man crosses open ground in view of two listeners, then walks behind House A and stops ────────────────────────────
ents, _ = squad_of_ears(7, [(118, 85), (126, 85)])
ents.update(walker(7, 200, "Red Walker", 132, 104, (104, 113), speed=1.0))
write("pe-seen-then-hidden", "bt-range",
      "CE-2476 perception suite P7 (Sensing §8.6e) - on bt-range a 'Red Walker' crosses open ground 20 m in front of two passive Blue listeners, "
      "then walks behind House A and stops there out of their sight.",
      "he is identified while he crosses (within the sighting dwell); once hidden he is REMEMBERED for a while (a man does not forget an enemy who "
      "ducked out of sight) and FORGOTTEN once the memory fades (~70 s), not before 20 s. Fail: never identified, dropped the moment he hides, "
      "or held for ever.",
      ents,
      {"runFor": 130, "golden": False,
       "anchors": {"hidden": {"on": "near", "who": "Red Walker", "point": [104, 113], "within": 2}},
       "expect": [
           {"name": "he reaches his hiding place", "on": "near", "who": "Red Walker", "point": [104, 113], "within": 2},
           {"name": "both listeners identify him while he crosses (2 s)", **per_man("matched", "==", 1, quantifier="all", **{"for": 2}), "before": "hidden"},
           {"name": "...and still remember him for 20 s after he hid", "never": per_man("matched", "==", 0, quantifier="any"),
            "after": "hidden", "before": "hidden+20"},
           {"name": "...and have forgotten him at the end (the memory faded)", **per_man("contacts", "==", 0, quantifier="all"), "atEnd": True},
       ]})

# ── P2b — the FIRST WAVEFRONT (CE-2475): a shooter behind ONE thin timber wall; the ways round (the open south door, the east / west windows)
#    are 4.5-5 m from him. timber 20 dB x the shot's Mid band 0.6 = 12 dB straight vs a few dB round ⇒ the ear takes HIS direction ──────────
TIMBER = {guid(8, 1): {"TkbIdentity": {"TkbType": 8818}, "TerrainFeature": {"Kind": 1, "Template": "timber-house", "HeadingDeg": 0, "Pad": 0, "Seed": 0},
                       "SimTransform": {"Position": [300, 300, 0], "Rotation": [0, 0, 0, 1]}, "EntityInfo": {"Name": "Timber House", "ForceId": "Neutral"}}}
ents, _ = squad_of_ears(8, [(302, 312), (306, 312)])   # 7 m north of the house; the north wall (no opening) between them and him
ents.update(TIMBER)
ents.update(target(8, 300, 304, 302))
ents.update(shooter(8, 200, "Red Shooter", 304, 304.8, guid(8, 300), stay=True))
write("pe-wall-and-door", "basic-desert",
      "CE-2476 / CE-2475 perception suite P2b (Sensing §8.6e) - on basic-desert a 'Red Shooter' inside a one-storey TIMBER HOUSE (8 x 6 m at (300,300)), "
      "just inside its north wall, fires at a 'Blue Target' in the same room; two passive Blue listeners stand 7 m north of the house. The sound reaches "
      "them straight through the thin north wall AND round the house through its open south door and its east / west windows (4.5-5 m from him).",
      "the ear takes its DIRECTION from the first wavefront - the straight way through the thin wall (12 dB) - not from the louder way round: both "
      "listeners believe him where he is (within 2 m), never at the door or a window, and the squad holds him once. Fail: he is believed at an opening.",
      ents,
      {"runFor": 30, "golden": False,
       "anchors": {"first shot": {"on": "shot", "by": "Red Shooter"}},
       "expect": [
           {"name": "the shooter fires", "on": "shot", "by": "Red Shooter", "n": 3},
           {"name": "both listeners place him where he is (matched within 2 m, 5 s)", **per_man("matched", "==", 1, quantifier="all", m=2, **{"for": 5}),
            "after": "first shot"},
           {"name": "no listener ever places him at the door or a window (a phantom more than 2 m from him)", "never": per_man("phantom", ">", 0, m=2),
            "after": "first shot"},
           {"name": "the squad never counts him twice", "never": squad("duplicate", ">", 0), "after": "first shot+2"},
       ]})

print("wrote pe-one-shot-four-ears, pe-seen-shooter, pe-two-shooters, pe-shelled, pe-house-shot, pe-steps-round-corner, pe-seen-then-hidden, pe-wall-and-door")
