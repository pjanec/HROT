#!/usr/bin/env python3
"""CE-5057 / CE-11063 A-4 — the OSM-bridge feature scenario (docs/DESIGN_Terrain_OSM_Bridges.md §6).

Writes scenarios/tests/te-osm-bridge on the SYNTHETIC terrain 'te-osm-bridge' (assets/terrain-input/te-osm-bridge — a 200 x 200 m input set: a road
along y 100 over a 6 m dip with a river in it, x 90..110; the crossing edge carries OSM's bridge=yes, so the importer makes it a DECK from bank to bank,
x 80..120, at the banks' height). A tank and a squad ordered to the far bank cross ON the deck. Checks key on arrival and state, never on a time.

Run:  python3 scripts/osm_bridge_scenarios.py
"""
import json
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "scenarios", "tests")
P = "b5057000-0000-4000-8000-0000000000"          # entity ids: P + two hex digits
T = "b5057000-0000-4000-9000-0000000000"          # task ids
EAST = [0, 0, 0, 1]                                # heading +X (along the road, bank to bank)
M1, RIFLEMAN, SQUAD, OBJECTIVE = 100, 2002, 302, 8860
ROAD_Y = 100.0
RIVER_X0, RIVER_X1 = 90.0, 110.0
WEST_BANK, EAST_BANK = (30.0, ROAD_Y), (170.0, ROAD_Y)
BANK_Z = 10.0                                      # the banks' (and the deck's) height; the river bed is at 4
_task = [0]


def tid():
    _task[0] += 1
    return f"{T}{_task[0]:02x}"


def task(name, params, trigger=("BehaviorFinished", "")):
    return {"taskId": tid(), "executingEngine": "", "behaviorName": name, "behaviorParams": params,
            "triggers": [{"type": trigger[0], "params": trigger[1]}]}


def plan(*tasks):
    return {"PlanData": {"activeTaskId": "00000000-0000-0000-0000-000000000000", "tasks": list(tasks)}}


def main():
    e = {}
    e[f"{P}01"] = {"SimTransform": {"Position": [WEST_BANK[0], WEST_BANK[1], 0], "Rotation": EAST}, "TkbIdentity": {"TkbType": M1},
                   "EntityInfo": {"Name": "Tank", "ForceId": "Friend"}, "Health": {"Current": 1000, "Max": 1000}, "TargetMemory": {"Entries": []},
                   "MissionPlan": plan(task("HoldStance", {"stance": "Standing"}, ("TimerElapsed", "5")),
                                       task("MoveToLocation", {"speed": 5, "arrivalRadius": 5, "target": [EAST_BANK[0], EAST_BANK[1], 0]}),
                                       task("HoldStance", {"stance": "Standing"})),
                   "Sop": {"Name": "InertSop", "Params": {}, "Origin": "Superior"}}
    squad_id = f"{P}02"
    start, goal = (WEST_BANK[0] - 12, ROAD_Y), (EAST_BANK[0] + 10, ROAD_Y)
    e[squad_id] = {"SimTransform": {"Position": [start[0], start[1], 0], "Rotation": EAST}, "TkbIdentity": {"TkbType": SQUAD},
                   "EntityInfo": {"Name": "Squad", "ForceId": "Friend"},
                   "Behavior": {"Name": "SquadMarch", "Params": {"Objective": [goal[0], goal[1], 0], "Shape": 0, "Spacing": 4}, "Origin": "Superior"},
                   "Sop": {"Name": "BasicSquadSop", "Params": {}, "Origin": "Superior"}}
    for i, n in enumerate(["Leader", "Rifleman 1", "Rifleman 2", "Rifleman 3"]):
        e[f"{P}{0x10 + i:02x}"] = {"SimTransform": {"Position": [start[0] - 4, start[1] - 6 + 4 * i, 0], "Rotation": EAST},
                                   "Health": {"Current": 100, "Max": 100}, "TkbIdentity": {"TkbType": RIFLEMAN},
                                   "EntityInfo": {"Name": n, "ForceId": "Friend"}, "TargetMemory": {"Entries": []},
                                   "UnitSubordinate": {"CommanderGuid": squad_id, "Designation": "SquadLeader" if i == 0 else "Wingman"}}
    e[f"{P}20"] = {"TkbIdentity": {"TkbType": OBJECTIVE}, "EntityInfo": {"Name": "Squad reaches the far bank", "ForceId": "Friend"},
                   "Behavior": {"Name": "ReachArea", "Origin": "Superior",
                                "Params": {"common": {"title": "Squad reaches the far bank", "subject": squad_id, "deadlineSeconds": 170},
                                           "area": {"circle": {"center": [goal[0], goal[1], 0], "radius": 15}}, "minMen": 4}}}
    description = (
        "What: CE-5057 / CE-11063 A-4 (docs/DESIGN_Terrain_OSM_Bridges.md §6) — the synthetic terrain te-osm-bridge (its committed INPUT SET, imported on "
        "first use): a road along y 100 crosses a 6 m dip with a river in it (x 90-110). The crossing road edge carries OSM's bridge=yes, so the importer "
        "makes it ONE DECK from bank to bank (x 80-120) at the banks' height (10 m) — not the authored 1.5 m above the river. A TANK (from the west bank, "
        "ordered to the east bank) and a SQUAD (marching to the far bank, a ReachArea objective) cross on the deck. "
        "Judge: the tank reaches the east bank having never dropped toward the river bed over the river; the squad's objective ends Achieved.")
    checks = {
        "$doc": "docs/DESIGN_Scenario_Regression.md §3 — CE-5057 te-osm-bridge (see scenario.json's header). Keys on arrival and state, never a time.",
        "runFor": 180, "golden": False, "anchors": {},
        "expect": [
            {"name": "the tank reaches the east bank", "on": "near", "who": "Tank", "point": list(EAST_BANK), "within": 6},
            {"name": "the tank crossed ON the deck (never below the banks' height over the river)", "never": {
                "on": "field", "who": "Tank", "path": "SimTransform.Position.0", "op": ">", "value": RIVER_X0,
                "also": [{"path": "SimTransform.Position.0", "op": "<", "value": RIVER_X1},
                         {"path": "SimTransform.Position.2", "op": "<", "value": BANK_Z - 1.5}]}},
            {"name": "the squad's objective 'reach the far bank' ends Achieved", "on": "field", "who": "Squad reaches the far bank",
             "path": "ObjectiveStatus.State", "op": "==", "value": "Achieved", "atEnd": True},
        ],
    }
    folder = os.path.join(ROOT, "te-osm-bridge")
    os.makedirs(folder, exist_ok=True)
    scenario = {"header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "te-osm-bridge", "description": description},
                "entities": e}
    for name, doc in (("scenario.json", scenario), ("checks.json", checks)):
        with open(os.path.join(folder, name), "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")
    print("wrote", folder)


if __name__ == "__main__":
    main()
