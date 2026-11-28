#!/usr/bin/env python3
"""Writes the SHOWCASE "Player station" scenario: a small, fast-loading test range for an EXTERNAL PLAYER STATION
(a client that possesses a Playable unit over the network and renders the world — docs/DESIGN_Player_Join.md D-9).

  python3 scripts/gen-showcase-player-station.py        ->  scenarios/showcases/player-station/scenario.json

Terrain demo-city: a SUMO net (traffic) and enterable houses whose windows are glazed by default (DESIGN_Glass_Windows D-1).
Everything sits in the open block x 170-390, y 160-400 (roads on three sides), within ~250 m:

  two Blue riflemen marked Playable ("Rifleman 1", "Rifleman 2") beside house-1f (295.9, 388.4) — AI on their post until taken
  a Blue AI squad beside them; a Red squad 150 m south (SquadDefend + BasicSquadSop, fire at will) — the firefight
  an empty HMMWV (free driver seat), a motorcycle (driver + firing pillion), a riding horse (rider firing seat)
  a flamethrower soldier burning a timber shed (+ a car beside it); a timed 8 kg charge breaching a timber hut's wall at 12 s
  civilians, a dog, a cattle herd, a bird flock, SUMO traffic
  rules: "Hold the house" (HoldArea, Blue only, progress), Blue tickets (Counter), a per-man score, a welcome message to Blue

Edit this script, not the file.
"""
import json, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenario_json_readable import readable  # CE-4295 — params written as the objects they hold

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = "scenarios/showcases/player-station/scenario.json"
FRIEND, HOSTILE = 1, 2           # ForceId numbers (a Message's toValue)
BLUE_ONLY = 1 << FRIEND          # an objective's visibleSides bit mask


def xyz(at):
    """A position as [x, y, z] (a 2-tuple lies on the ground, z = 0)."""
    return [at[0], at[1], at[2] if len(at) > 2 else 0]


def rot(deg):
    """Compass heading (0 = north, clockwise) to the scenario's quaternion (yaw 0 = +X)."""
    yaw = math.radians(90 - deg)
    return [0, 0, round(math.sin(yaw / 2), 7), round(math.cos(yaw / 2), 7)]


class Ids:
    def __init__(self, prefix):
        self.prefix, self.n = prefix, 0

    def __call__(self):
        self.n += 1
        return f"{self.prefix}-0000-4000-8000-{self.n:012d}"


def beh(name, params):
    return {"Name": name, "Params": json.dumps(params), "Origin": "Superior"}


def man(name, force, at, facing, tkb=2002, commander=None, designation="Wingman"):
    e = {"SimTransform": {"Position": xyz(at), "Rotation": rot(facing)}, "Health": {"Current": 100, "Max": 100},
         "TkbIdentity": {"TkbType": tkb}, "EntityInfo": {"Name": name, "ForceId": force}, "TargetMemory": {"Entries": []}}
    if commander:
        e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": designation}
    return e


def thing(tkb, name, force, at, facing=0, health=None):
    e = {"SimTransform": {"Position": xyz(at), "Rotation": rot(facing)}, "TkbIdentity": {"TkbType": tkb},
         "EntityInfo": {"Name": name, "ForceId": force}}
    if health:
        e["Health"] = {"Current": health, "Max": health}
    return e


def area(points):
    return {"Points": [{"X": x, "Y": y} for x, y in points], "Version": 0}


def animals(name, at, species, count, half, seed):
    hx, hy = half
    return {"SimTransform": {"Position": xyz(at), "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": 712},
            "EntityInfo": {"Name": name, "ForceId": "Neutral"},
            "EditablePolyline": area([(-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy)]),
            "AnimalArea": {"Species": species, "Enclosed": 0, "Count": count, "Seed": seed}}


def scenario():
    ids = Ids("9157a710")
    ents = {}

    def put(e):
        g = ids()
        ents[g] = e
        return g

    fire_at_will = {"Fire": "FireAtWill", "Reactions": "StayOnTask", "SetBy": "Operator", "ForceLevel": "Lethal"}

    # ── Blue: the team (the objectives' subject), two PLAYABLE riflemen, an AI squad ────────────────────────────
    team = put(thing(305, "Blue Team", "Friend", (312, 366), 180))
    ents[team]["RefKey"] = {"Value": "BlueTeam"}
    playables = []
    for i, x in enumerate((300.0, 305.0), start=1):
        at = (x, 373.0, 0)
        e = man(f"Rifleman {i}", "Friend", at, 180, commander=team)
        # a free Playable unit runs as AI until a player takes it (D-9): it holds its post and fights from it
        e["Behavior"] = beh("CombatPosture", {"Advance": {"Objective": list(at), "ArrivalRadius": 1}, "Stay": True,
                                              "Post": {"Point": list(at), "Radius": 2.0}})
        e["Roe"] = dict(fire_at_will)
        e["Playable"] = {"Side": "Friend", "SlotName": f"Rifleman {i}"}
        playables.append(put(e))
    squad = put(thing(302, "Blue Squad", "Friend", (330, 362), 180))
    ents[squad]["UnitSubordinate"] = {"CommanderGuid": team, "Designation": "Wingman"}
    ents[squad]["Behavior"] = beh("SquadDefend", {"Position": [330, 362, 0], "SearchRadius": 12, "Speed": 3})
    for i, (designation, dx) in enumerate((("SquadLeader", 0), ("MachineGunner", 3), ("Wingman", -3), ("Wingman", 6)), start=1):
        put(man(f"Blue 1-{i}", "Friend", (330 + dx, 362, 0), 180, commander=squad, designation=designation))

    # ── Red: one squad 150 m south, in the open, that engages ────────────────────────────────────────────────────
    red = put(thing(302, "Red Squad", "Hostile", (300, 222), 0))
    ents[red]["Behavior"] = beh("SquadDefend", {"Position": [300, 222, 0], "SearchRadius": 12, "Speed": 3})
    ents[red]["Sop"] = {"Name": "BasicSquadSop", "Params": "{}", "Origin": "Superior"}
    ents[red]["Roe"] = dict(fire_at_will)
    for i, (designation, dx) in enumerate((("SquadLeader", 0), ("MachineGunner", 3), ("Wingman", -3), ("Wingman", 6)), start=1):
        put(man(f"Red 1-{i}", "Hostile", (300 + dx, 222, 0), 0, commander=red, designation=designation))

    # ── vehicles and seats: all empty, so every seat is free for a player ───────────────────────────────────────
    put(thing(111, "HMMWV (empty)", "Friend", (322, 381), 90, 2500))             # seat 5 = Driver, free (Drive / Board)
    put(thing(115, "Motorcycle", "Friend", (337, 383), 90, 200))                 # seat 0 driver, seat 1 firing pillion
    put(thing(703, "Riding horse", "Friend", (352, 378), 90, 100))                # seat 0 rider (fires ±150° riding), seat 1 passenger

    # ── fire and breach: a flamer burns a timber shed (+ a car); a timed charge breaches a timber hut's blank wall ──
    put({"TkbIdentity": {"TkbType": 8818}, "TerrainFeature": {"Kind": 1, "Template": "timber-house", "HeadingDeg": 0, "Pad": 0, "Seed": 0},
         "SimTransform": {"Position": [200, 300, 0], "Rotation": [0, 0, 0, 1]}, "EntityInfo": {"Name": "Timber shed", "ForceId": "Neutral"}})
    flamer = man("Flamer", "Friend", (185, 300.8, 0), 90, tkb=2022)
    flamer["Roe"] = dict(fire_at_will)
    flamer["Behavior"] = beh("FireAtPoint", {"x": 200, "y": 300.8, "z": 1.2, "mount": 0, "rounds": 60, "cooldownSeconds": 0.1})
    put(flamer)
    put(thing(1002, "Parked car", "Neutral", (212, 296), 0, 500))

    put({"TkbIdentity": {"TkbType": 8818}, "TerrainFeature": {"Kind": 1, "Template": "timber-house", "HeadingDeg": 0, "Pad": 0, "Seed": 0},
         "SimTransform": {"Position": [240, 330, 0], "Rotation": [0, 0, 0, 1]}, "EntityInfo": {"Name": "Breach hut", "ForceId": "Neutral"}})
    # a placed 8 kg demolition charge 0.4 m off the hut's blank north wall (y = 336), on a TIMER (ChargeTimer, CE-4621): it goes off at
    #   12 s with nobody carrying it — a time-fuzed placed charge makes a walkable doorway (breaches.json). ⛔ Not DetonateCharge: that bursts
    #   the charge the man carries, on him (the suicide-bomber behaviour).
    put({"SimTransform": {"Position": [244.0, 336.4, 0.5], "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": 618},
         "EntityInfo": {"Name": "Demolition charge", "ForceId": "Friend"}, "ChargeTimer": {"DetonateAt": 12}})

    # ── life around: civilians (bare = their routine), a dog, a herd, birds, SUMO traffic ─────────────────────────
    for name, tkb, at in (("Passer-by", 1101, (262, 372)), ("Shopper", 1102, (268, 366)), ("Old man", 1104, (232, 384))):
        put(man(name, "Neutral", (*at, 0), 90, tkb=tkb))
    put(animals("Yard dog", (366, 340), "Dog", 1, (6, 4), 7101))
    put(animals("Cattle", (215, 235), "Cattle", 6, (15, 6), 7102))
    put(animals("Birds", (262, 300), "BirdFlock", 20, (18, 18), 7103))
    put({"TkbIdentity": {"TkbType": 8850}, "TrafficDemand": {"TargetCars": 1000, "Seed": 1, "StepSeconds": 0.5,
                                                             "TargetPedestrians": 0, "CarSpawnPerStep": 40}})

    # ── rules: an objective with progress, a ticket counter, a score, a welcome message — Blue only ──────────────
    def objective(kind, title, params, key=None):
        g = put({"TkbIdentity": {"TkbType": 8860}, "EntityInfo": {"Name": title, "ForceId": "Friend"}, "Behavior": beh(kind, params)})
        if key:
            ents[g]["RefKey"] = {"Value": key}
        return g

    objective("HoldArea", "Hold the house",
              {"common": {"title": "Hold the house", "subject": team, "visibleSides": BLUE_ONLY},
               "area": {"circle": {"center": [291.4, 384.4, 0], "radius": 20}}, "minMen": 1, "maxEnemy": 0, "holdSeconds": 120},
              key="HoldHouse")
    objective("Counter", "Blue tickets",
              {"common": {"title": "Blue tickets", "subject": team, "scored": False, "visibleSides": BLUE_ONLY},
               "start": 20, "deaths": [{"side": "Friend", "per": -1}]}, key="BlueTickets")
    scores = objective("Points", "All players", {"common": {"title": "All players", "group": "score", "scored": False}}, key="Scores")
    objective("MemberEvaluation", "Blue score",
              {"common": {"title": "Blue score", "subject": team, "parent": scores, "group": "score", "scored": False, "visibleSides": BLUE_ONLY},
               "library": "macros/game/score", "members": "side", "aggregate": "points"})
    put({"TkbIdentity": {"TkbType": 8861}, "EntityInfo": {"Name": "Welcome", "ForceId": "Friend"},
         "Behavior": beh("Trigger", {"common": {"title": "Welcome", "subject": team},
                                     "when": {"$type": "ScenarioTime", "fromSeconds": 2},
                                     "do": [{"act": "Message", "args": {"toKind": "Side", "toValue": FRIEND, "seconds": 10,
                                             "text": "Welcome to the player station range. Take Rifleman 1 or 2, hold the house, watch the road."}}]})})

    header = {"terrainName": "demo-city",
              "description": ("What: a small test range for an external PLAYER STATION (docs/DESIGN_Player_Join.md D-9): two Blue riflemen "
                              "marked Playable ('Rifleman 1', 'Rifleman 2') beside an enterable house with glazed windows; a Blue AI squad; a "
                              "Red squad 150 m south that engages at once; an empty HMMWV (free driver seat), a motorcycle (driver + firing "
                              "pillion), a riding horse (rider firing seat); a flamethrower burning a timber shed and a car; an 8 kg "
                              "charge breaching a timber hut's wall at 12 s (a walkable doorway); civilians, a dog, cattle, birds, SUMO traffic. Rules for Blue: 'Hold the "
                              "house' (2 min, progress), Blue tickets (20, -1 per man down), a per-man score, a welcome message at 2 s. "
                              "A free Playable unit runs as AI until taken. Generated by scripts/gen-showcase-player-station.py. "
                              "Check: scripts/scenario-regression.sh player-station")}
    h = {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0"}
    h.update(header)
    return {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1}, "header": h, "entities": ents}, playables


def checks():
    """docs/DESIGN_Scenario_Regression.md — conditions only (AI on both sides: no golden). What a player station needs to see in the
    first minute: the welcome message, the objective live, fire, the charge's breach burst, men down on both sides."""
    return {
        "$doc": "docs/DESIGN_Scenario_Regression.md — the player-station range (scripts/gen-showcase-player-station.py): within 60 s the "
                "welcome trigger fires, 'Hold the house' is live, the free Playable riflemen hold their post as AI, the flamer spends fuel, "
                "both sides fire and lose men, Blue's tickets fall. Possession is proven over HTTP (POST/DELETE /entities/{id}/possess), not here.",
        "runFor": 60,
        "golden": False,
        "expect": [
            {"name": "the welcome message is sent to Blue", "on": "field", "who": "Welcome", "path": "ObjectiveStatus.State",
             "op": "==", "value": "Achieved", "before": 5},
            {"name": "the house objective is live for Blue", "on": "field", "who": "Hold the house", "path": "ObjectiveStatus.State",
             "op": "==", "value": "Active", "before": 5},
            {"name": "a free Playable rifleman holds his post as AI", "on": "task", "who": "Rifleman 1", "is": "CombatPosture", "before": 5},
            {"name": "the flamer spends fuel", "on": "field", "who": "Flamer", "path": "WeaponAmmo.Ammo0", "op": "<", "value": 90, "before": 10},
            {"name": "Red fires at Blue", "on": "shot", "by": "*Red 1-", "n": 3},
            {"name": "Blue fires at Red", "on": "shot", "by": "*Blue 1-", "n": 1},
            {"name": "a Red man goes down", "on": "field", "who": "*Red 1-", "path": "Health.Current", "op": "==", "value": 0},
            {"name": "Blue's tickets fall with its losses", "on": "field", "who": "Blue tickets", "path": "ObjectiveStatus.Value",
             "op": "<", "value": 20, "after": 5},   # ⚠ not from t = 0: the counter reads 0 until it starts at 20 (measured: a bare check passed at 0.25 s)
        ],
    }


if __name__ == "__main__":
    doc, playables = scenario()
    full = os.path.join(ROOT, OUT)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w") as f:
        f.write(readable(json.dumps(doc, indent=1)))
        f.write("\n")
    with open(os.path.join(os.path.dirname(full), "checks.json"), "w") as f:
        json.dump(checks(), f, indent=2)
        f.write("\n")
    print(f"wrote {OUT} + checks.json: {len(doc['entities'])} entities; Playable: {playables}")
