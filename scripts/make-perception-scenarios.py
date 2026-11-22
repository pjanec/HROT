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
    ents = {sq: {"SimTransform": {"Position": [ears[0][0], ears[0][1], 0], "Rotation": NORTH}, "TkbIdentity": {"TkbType": 302},
                 "EntityInfo": {"Name": "Blue Squad", "ForceId": "Friend"}}}
    for i, (x, y) in enumerate(ears):
        ents[guid(series, base + 1 + i)] = man(f"Blue Squad Ear {i + 1}", "Friend", x, y, extra={
            **PASSIVE, "UnitSubordinate": {"CommanderGuid": sq, "Designation": "SquadLeader" if i == 0 else "Wingman"}})
    return ents, sq


def shooter(series, n, name, x, y, target):
    return {guid(series, n): man(name, "Hostile", x, y, hp=1000, extra={
        "Behavior": {"Name": "FireAtTarget", "Params": {"targetNetworkId": target}, "Origin": "Superior"}})}


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


FOUR_EARS = [(60, 225), (90, 215), (120, 225), (150, 220)]   # 175-190 m south of the shooter: beyond sight (150 m), inside a soldier's ear (200 m — a rifle is not a LOUD class, IsLoud)

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

print("wrote pe-one-shot-four-ears, pe-seen-shooter, pe-two-shooters, pe-shelled")
