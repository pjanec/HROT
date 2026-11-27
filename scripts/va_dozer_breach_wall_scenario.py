#!/usr/bin/env python3
"""CE-11072 (S3) — the breachable-wall acceptance scenario (docs/designs/engineering/DESIGN_Breachable_Obstacle_Walls.md §6, §8 V-8, V-9).

⚠ DRAFT until it passes: the checks are written as checks.draft.json (the nightly runs only checks.json).
Writes scenarios/demos/navigation-and-terrain/va-dozer-breach-wall on the SYNTHETIC terrain 'te-osm-bridge' (a 200 x 200 m input set: a two-way road
along y 100 with a SUMO net, flat banks at 10 m, a river at x 90..110 under a deck). A 12 m SANDBAG feature wall (1 m high) stands across the road on the
west bank at x 50. A bulldozer is ordered to clear a point of it on the eastbound lane; a vehicle handed to SUMO (a road convoy of one, Mode Traffic)
drives east along the road. Checks key on arrival and state, never on a time.

Run:  python3 scripts/va_dozer_breach_wall_scenario.py
"""
import json
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "scenarios", "demos", "navigation-and-terrain")
P = "b1107200-0000-4000-8000-0000000000"          # entity ids: P + two hex digits
T = "b1107200-0000-4000-9000-0000000000"          # task ids
EAST = [0, 0, 0, 1]
DOZER, CAR, WALL, DEMAND = 112, 2001, 8818, 8850
ROAD_Y = 100.0
EAST_LANE_Y = 98.25                                # the eastbound lane's centre (right-hand traffic: south of the centre line)
WALL_X, WALL_Y0, WALL_Y1, WALL_H = 50.0, 94.0, 106.0, 1.0
CUT = (WALL_X, EAST_LANE_Y)                        # the dozer's point of the wall: centred on the eastbound lane
CAR_START, CAR_GOAL = (15.0, EAST_LANE_Y), (180.0, EAST_LANE_Y)
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
    e[f"{P}01"] = {"TkbIdentity": {"TkbType": WALL},
                   "TerrainFeature": {"Kind": 2, "Template": "sandbags", "HeadingDeg": 0, "Pad": WALL_H, "Seed": 0},
                   "SimTransform": {"Position": [WALL_X, WALL_Y0, 0], "Rotation": [0, 0, 0, 1]},
                   "EditablePolyline": {"Points": [{"X": 0, "Y": 0}, {"X": 0, "Y": WALL_Y1 - WALL_Y0}], "Version": 0}}
    e[f"{P}02"] = {"SimTransform": {"Position": [WALL_X - 14, EAST_LANE_Y, 0], "Rotation": EAST}, "TkbIdentity": {"TkbType": DOZER},
                   "EntityInfo": {"Name": "Dozer", "ForceId": "Friend"}, "Health": {"Current": 1000, "Max": 1000}, "TargetMemory": {"Entries": []},
                   "MissionPlan": plan(task("HoldStance", {"stance": "Standing"}, ("TimerElapsed", "20")),
                                       task("ClearObstacle", {"At": [CUT[0], CUT[1], 0], "PushSeconds": 3, "Speed": 4}),
                                       task("MoveToLocation", {"speed": 3, "arrivalRadius": 3, "target": [WALL_X + 14, EAST_LANE_Y + 6, 0]}),
                                       task("HoldStance", {"stance": "Standing"})),
                   "Sop": {"Name": "InertSop", "Params": {}, "Origin": "Superior"}}
    e[f"{P}03"] = {"SimTransform": {"Position": [CAR_START[0], CAR_START[1], 0], "Rotation": EAST}, "TkbIdentity": {"TkbType": CAR},
                   "EntityInfo": {"Name": "Car", "ForceId": "Neutral"},
                   "Behavior": {"Name": "PlatformConvoy", "Params": {"Destination": [CAR_GOAL[0], CAR_GOAL[1], 0], "Rank": 0, "Gap": 15, "Mode": "Traffic", "Speed": 8},
                                "Origin": "Superior"}}
    e[f"{P}04"] = {"TkbIdentity": {"TkbType": DEMAND},
                   "TrafficDemand": {"TargetCars": 0, "Seed": 1, "StepSeconds": 0.5, "TargetPedestrians": 0, "CarSpawnPerStep": 0}}
    description = (
        "What: CE-11072 (docs/designs/engineering/DESIGN_Breachable_Obstacle_Walls.md §6) — on the synthetic terrain te-osm-bridge (a two-way road along "
        "y 100 with a SUMO net), a 12 m SANDBAG feature wall, 1 m high, stands across the road on the west bank at x 50 (y 94..106). A scenario wall closes "
        "the lanes it crosses edge to edge (CE-11070), so 'Car' — a vehicle handed to SUMO (a road convoy of one, Mode Traffic) driving east to (180, 98.25) — "
        "cannot pass it. After 20 s 'Dozer' is ordered to clear the wall at (50, 98.25), the eastbound lane's centre: its blade cuts a hull-wide Passage "
        "(>= 4.2 m), the rest of the wall stands, the eastbound lane re-opens, and the car drives on. "
        "Judge: the car never passes the wall line before the dozer reaches it; the dozer cuts and drives through; the car reaches the east bank.")
    checks = {
        "$doc": "docs/DESIGN_Scenario_Regression.md §3 — CE-11072 va-dozer-breach-wall (see scenario.json's header). Keys on arrival and state, never a time. "
                "⚠ The breach itself has no cue in the grammar (§9 asks for one): the cut is approximated by the dozer standing at the wall, then by it and the car beyond the wall line.",
        "runFor": 150, "golden": False, "anchors": {},
        "expect": [
            {"name": "the dozer reaches the wall", "on": "near", "who": "Dozer", "point": [WALL_X - 3, EAST_LANE_Y], "within": 3},
            {"name": "the car never passes the wall line before the dozer reaches it", "never": {
                "on": "field", "who": "Car", "path": "SimTransform.Position.0", "op": ">", "value": WALL_X - 2}, "before": "the dozer reaches the wall"},
            {"name": "the dozer drives through its cut", "on": "near", "who": "Dozer", "point": [WALL_X + 14, EAST_LANE_Y + 6], "within": 4,
             "after": "the dozer reaches the wall"},
            {"name": "the car is handed to SUMO", "on": "field", "who": "Car", "path": "NavigationIntent.Mode", "op": "==", "value": "Traffic"},
            {"name": "the car passes the wall through the cut", "on": "field", "who": "Car", "path": "SimTransform.Position.0", "op": ">", "value": WALL_X + 5,
             "after": "the dozer reaches the wall"},
            {"name": "the car reaches the east bank (SUMO ends a shadow up to 25 m short)", "on": "near", "who": "Car", "point": list(CAR_GOAL), "within": 25},
        ],
    }
    folder = os.path.join(ROOT, "va-dozer-breach-wall")
    os.makedirs(folder, exist_ok=True)
    scenario = {"header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "te-osm-bridge", "description": description},
                "entities": e}
    for name, doc in (("scenario.json", scenario), ("checks.draft.json", checks)):
        with open(os.path.join(folder, name), "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")
    print("wrote", folder)


if __name__ == "__main__":
    main()
