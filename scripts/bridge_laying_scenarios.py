#!/usr/bin/env python3
"""CE-4692 BL-S1 / BL-S2 — the bridge-laying feature scenarios (docs/designs/engineering/DESIGN_Bridge_Laying.md §5).

Writes, under scenarios/demos/engineering/:
  bl-placed-bridge          a scenario-placed laid bridge (TKB 8825) over a river: a tank and a squad ordered to the far bank reach it
  bl-placed-bridge-absent   the SAME river and orders with no bridge: neither reaches it (the 'second variant' of the handoff)
  bl-blow-the-bridge        a demolition charge on the deck: the bridge is gone, the tank parked on it is destroyed, a tank ordered across afterwards
                            does not reach the far bank

Stage: basic-desert (600 x 600 m, the plain at 0 under every feature). A RIVER band (an authored Water feature, kind 9) at y 60..72 across the
whole map: nothing crosses it but a deck. The bridge sits at (80, 66) heading north: an 18 m span, so its deck ends (y 57 and 75) stand on the
banks. Every check keys on STATE and arrival, never on a time: the obstacle bake is wall-clock paced (R-244).

Run:  python3 scripts/bridge_laying_scenarios.py
"""
import json
import math
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "scenarios", "demos", "engineering")
P = "b4692000-0000-4000-8000-0000000000"          # entity ids: P + two hex digits
T = "b4692000-0000-4000-9000-0000000000"          # task ids

RIVER_Y0, RIVER_Y1 = 60.0, 72.0
BRIDGE_AT = (80.0, 66.0)
NORTH = [0, 0, math.sin(math.pi / 4), math.cos(math.pi / 4)]   # heading +Y (the deck runs bank to bank)
IDENT = [0, 0, 0, 1]

ASSAULT_BRIDGE, M1, RIFLEMAN, SQUAD, CHARGE, FEATURE, OBJECTIVE = 8825, 100, 2002, 302, 618, 8818, 8860
_task = [0]


def tid():
    _task[0] += 1
    return f"{T}{_task[0]:02x}"


def task(name, params, trigger=("BehaviorFinished", "")):
    return {"taskId": tid(), "executingEngine": "", "behaviorName": name, "behaviorParams": params,
            "triggers": [{"type": trigger[0], "params": trigger[1]}]}


def plan(*tasks):
    return {"PlanData": {"activeTaskId": "00000000-0000-0000-0000-000000000000", "tasks": list(tasks)}}


def river():
    return {"TkbIdentity": {"TkbType": FEATURE},
            "TerrainFeature": {"Kind": 9, "Template": "", "HeadingDeg": 0, "Pad": 0, "Seed": 0},
            "SimTransform": {"Position": [-5, RIVER_Y0, 0], "Rotation": IDENT},
            "EditablePolyline": {"Points": [{"X": 0, "Y": 0}, {"X": 610, "Y": 0}, {"X": 610, "Y": RIVER_Y1 - RIVER_Y0}, {"X": 0, "Y": RIVER_Y1 - RIVER_Y0}], "Version": 0},
            "EntityInfo": {"Name": "River", "ForceId": "Neutral"}}


def bridge():
    return {"SimTransform": {"Position": [BRIDGE_AT[0], BRIDGE_AT[1], 0], "Rotation": NORTH},
            "TkbIdentity": {"TkbType": ASSAULT_BRIDGE},
            "EntityInfo": {"Name": "Bridge", "ForceId": "Friend"}}


def tank(name, at, *tasks, z=0.0):
    return {"SimTransform": {"Position": [at[0], at[1], z], "Rotation": NORTH}, "TkbIdentity": {"TkbType": M1},
            "EntityInfo": {"Name": name, "ForceId": "Friend"}, "Health": {"Current": 1000, "Max": 1000},
            "TargetMemory": {"Entries": []}, "MissionPlan": plan(*tasks), "Sop": {"Name": "InertSop", "Params": {}, "Origin": "Superior"}}


def move(target, speed, radius):
    return task("MoveToLocation", {"speed": speed, "arrivalRadius": radius, "target": [target[0], target[1], 0]})


def hold(seconds):
    return task("HoldStance", {"stance": "Standing"}, ("TimerElapsed", str(seconds)))


def park():
    return task("HoldStance", {"stance": "Standing"})


def squad(entities, squad_id, at, objective):
    entities[squad_id] = {"SimTransform": {"Position": [at[0], at[1], 0], "Rotation": NORTH}, "TkbIdentity": {"TkbType": SQUAD},
                          "EntityInfo": {"Name": "Squad", "ForceId": "Friend"},
                          "Behavior": {"Name": "SquadMarch", "Params": {"Objective": [objective[0], objective[1], 0], "Shape": 0, "Spacing": 4}, "Origin": "Superior"},
                          "Sop": {"Name": "BasicSquadSop", "Params": {}, "Origin": "Superior"}}
    names = ["Leader", "Rifleman 1", "Rifleman 2", "Rifleman 3"]
    for i, n in enumerate(names):
        entities[f"{P}{0x10 + i:02x}"] = {
            "SimTransform": {"Position": [at[0] - 6 + 4 * i, at[1] - 4, 0], "Rotation": NORTH}, "Health": {"Current": 100, "Max": 100},
            "TkbIdentity": {"TkbType": RIFLEMAN}, "EntityInfo": {"Name": n, "ForceId": "Friend"}, "TargetMemory": {"Entries": []},
            "UnitSubordinate": {"CommanderGuid": squad_id, "Designation": "SquadLeader" if i == 0 else "Wingman"}}


def objective(name, behavior, params):
    return {"TkbIdentity": {"TkbType": OBJECTIVE}, "EntityInfo": {"Name": name, "ForceId": "Friend"},
            "Behavior": {"Name": behavior, "Params": params, "Origin": "Superior"}}


def write(name, description, entities, checks):
    folder = os.path.join(ROOT, name)
    os.makedirs(folder, exist_ok=True)
    scenario = {"header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "basic-desert", "description": description},
                "entities": entities}
    with open(os.path.join(folder, "scenario.json"), "w", encoding="utf-8") as f:
        json.dump(scenario, f, indent=2, ensure_ascii=False)
        f.write("\n")
    with open(os.path.join(folder, "checks.json"), "w", encoding="utf-8") as f:
        json.dump(checks, f, indent=2, ensure_ascii=False)
        f.write("\n")


FAR_BANK = (80.0, 110.0)
SQUAD_START, SQUAD_GOAL = (64.0, 22.0), (64.0, 108.0)
SQUAD_ID = f"{P}02"


def crossing(name, with_bridge):
    _task[0] = 0
    e = {f"{P}00": river()}
    if with_bridge:
        e[f"{P}01"] = bridge()
    e[f"{P}03"] = tank("Tank", (80.0, 20.0), hold(5), move(FAR_BANK, 5, 5), park())
    squad(e, SQUAD_ID, SQUAD_START, SQUAD_GOAL)
    e[f"{P}20"] = objective("Squad reaches the far bank", "ReachArea",
                            {"common": {"title": "Squad reaches the far bank", "subject": SQUAD_ID, "deadlineSeconds": 110},
                             "area": {"circle": {"center": [SQUAD_GOAL[0], SQUAD_GOAL[1], 0], "radius": 15}}, "minMen": 4})
    state = "Achieved" if with_bridge else "Failed"
    rows = [
        {"name": f"the squad's objective 'reach the far bank' ends {state}", "on": "field", "who": "Squad reaches the far bank",
         "path": "ObjectiveStatus.State", "op": "==", "value": state, "atEnd": True},
    ]
    if with_bridge:
        rows += [
            {"name": "the tank reaches the far bank", "on": "near", "who": "Tank", "point": list(FAR_BANK), "within": 6},
            {"name": "the tank crossed on the deck (never in the river off the bridge's strip)", "never": {
                "on": "field", "who": "Tank", "path": "SimTransform.Position.1", "op": ">", "value": RIVER_Y0,
                "also": [{"path": "SimTransform.Position.1", "op": "<", "value": RIVER_Y1}, {"path": "SimTransform.Position.0", "op": "<", "value": 77}]}},
            {"name": "the tank drove over the deck, up off the river bed", "on": "field", "who": "Tank", "path": "SimTransform.Position.2", "op": ">", "value": 0.8},
        ]
    else:
        rows += [
            {"name": "the tank never reaches the far bank", "never": {"on": "near", "who": "Tank", "point": list(FAR_BANK), "within": 6}},
            {"name": "no squad man ever stands north of the river", "never": {"on": "field", "who": "*Rifleman", "path": "SimTransform.Position.1", "op": ">", "value": RIVER_Y1}},
            {"name": "the leader never stands north of the river", "never": {"on": "field", "who": "Leader", "path": "SimTransform.Position.1", "op": ">", "value": RIVER_Y1}},
        ]
    what = ("What: CE-4692 BL-S1 (docs/designs/engineering/DESIGN_Bridge_Laying.md §5) — on basic-desert a RIVER (an authored Water feature, y 60-72 across the whole "
            "map) parts the south from the north. " +
            ("A LAID BRIDGE (an assault span, TKB 8825: a walkable-deck static obstacle, 18 m between its deck ends, 4 m wide, 1.2 m above the banks, a ramp at "
             "each end) is placed by the scenario at (80, 66), heading north, so its deck ends stand on the banks. Every node bakes it into its own terrain — "
             "the navmesh, the floor, sight and fire — before it is Open (held Constructing until the bake commits). " if with_bridge else
             "There is NO bridge: this is the SAME scenario as bl-placed-bridge with the bridge left out — the control. ") +
            "'Tank' (an M1 at (80, 20)) is ordered to (80, 110) on the far bank; 'Squad' (a leader and three riflemen at (64, 22)) marches to (64, 108). "
            "Judge: " + ("the tank reaches the far bank driving over the deck (up at deck height, never in the river beside it); the squad's objective "
                         "'reach the far bank' ends Achieved (all four men inside 15 m of (64, 108)). Fail: either stops at the river." if with_bridge else
                         "neither the tank nor any squad man ever stands north of the river; the squad's objective 'reach the far bank' ends Failed at its 110 s "
                         "deadline. Fail: anyone crosses (the river is not a barrier, or a deck that does not exist carries them).") +
            f" Check: scripts/scenario-regression.sh {name}")
    checks = {"$doc": f"docs/DESIGN_Scenario_Regression.md §3 — CE-4692 {name} (see scenario.json's header). Keys on arrival and the objective's STATE, never a "
                      "time: the obstacle bake is wall-clock paced (R-244). ⚠ No golden: that pacing makes the tick of the bake machine-dependent.",
              "runFor": 120, "golden": False, "anchors": {}, "expect": rows}
    write(name, what, e, checks)


def blow():
    _task[0] = 0
    e = {f"{P}00": river(), f"{P}01": bridge()}
    # the tank that parks ON the deck (its centre at the bridge's centre)
    e[f"{P}03"] = tank("Tank On Bridge", (80.0, 30.0), hold(5), move(BRIDGE_AT, 3, 1), park())
    # the tank ordered across AFTER the bridge has gone
    e[f"{P}04"] = tank("Second Tank", (60.0, 20.0), hold(5), move((60.0, 20.0), 3, 2), hold(70), move((60.0, 110.0), 5, 5), park())
    # the charge man: waits on the far bank, walks onto the deck near its north end, sets the 8 kg charge off
    e[f"{P}05"] = {"SimTransform": {"Position": [84, 100, 0], "Rotation": IDENT}, "Health": {"Current": 100, "Max": 100},
                   "TkbIdentity": {"TkbType": RIFLEMAN}, "EntityInfo": {"Name": "Charge Man", "ForceId": "Friend"}, "TargetMemory": {"Entries": []},
                   "MissionPlan": plan(hold(35), move((80.0, 73.0), 1.5, 0.4), task("DetonateCharge", {"Seconds": 1}), park())}
    e[f"{P}06"] = {"SimTransform": {"Position": [84, 100, 0], "Rotation": IDENT}, "TkbIdentity": {"TkbType": CHARGE},
                   "EntityInfo": {"Name": "Demolition Charge", "ForceId": "Friend"}, "IsEmbarkedTag": {"Vehicle": f"{P}05", "Seat": 3}}
    e[f"{P}20"] = objective("Blow the bridge", "DestroyTargets", {"common": {"title": "Blow the bridge"}, "targets": [f"{P}01"]})
    e[f"{P}21"] = objective("The tank on the bridge is lost", "DestroyTargets", {"common": {"title": "The tank on the bridge is lost"}, "targets": [f"{P}03"]})
    rows = [
        {"name": "the charge bursts", "on": "blast", "by": "Charge Man"},
        {"name": "the tank parked on the deck, up at deck height", "on": "field", "who": "Tank On Bridge", "path": "SimTransform.Position.2", "op": ">", "value": 0.8,
         "before": "the charge bursts"},
        {"name": "the objective 'blow the bridge' ends Achieved (the bridge is gone)", "on": "field", "who": "Blow the bridge",
         "path": "ObjectiveStatus.State", "op": "==", "value": "Achieved", "atEnd": True},
        {"name": "the objective 'the tank on the bridge is lost' ends Achieved (the tank on it is destroyed)", "on": "field", "who": "The tank on the bridge is lost",
         "path": "ObjectiveStatus.State", "op": "==", "value": "Achieved", "atEnd": True},
        {"name": "the tank on the bridge is destroyed", "on": "field", "who": "Tank On Bridge", "path": "Health.Current", "op": "<=", "value": 0, "after": "the charge bursts"},
        {"name": "the second tank never reaches the far bank", "never": {"on": "near", "who": "Second Tank", "point": [60, 110], "within": 6}},
        {"name": "the second tank never stands north of the river", "never": {"on": "field", "who": "Second Tank", "path": "SimTransform.Position.1", "op": ">", "value": RIVER_Y1}},
    ]
    what = ("What: CE-4692 BL-S2 (docs/designs/engineering/DESIGN_Bridge_Laying.md §5) — on basic-desert a RIVER (an authored Water feature, y 60-72 across the "
            "whole map) is spanned by a scenario-placed LAID BRIDGE (an assault span, TKB 8825, 600 hp) at (80, 66), heading north. 'Tank On Bridge' (an M1) "
            "drives onto the deck and parks at its centre. 'Charge Man' waits 35 s on the far bank, walks onto the deck near its north end (80, 73) and sets off "
            "his 8 kg demolition charge: the blast is measured to the DECK (not the entity's centre) and drops the span; every man or vehicle on the span as it "
            "falls goes into the river — the tank is destroyed (there is no water model: nothing swims, Q-3); the deck is no floor at once, the navmesh and the "
            "road follow at the rebake; a fallen laid bridge leaves NO rubble. 'Second Tank' waits at (60, 20) for 70 s, then is ordered to (60, 110) on the far "
            "bank: with the bridge gone there is no way across. Judge: the charge bursts; the objective 'blow the bridge' ends Achieved; the objective 'the tank "
            "on the bridge is lost' ends Achieved; the second tank never stands north of the river. Fail: the bridge survives, the tank on it survives, or the "
            "second tank crosses. Check: scripts/scenario-regression.sh bl-blow-the-bridge")
    checks = {"$doc": "docs/DESIGN_Scenario_Regression.md §3 — CE-4692 bl-blow-the-bridge (see scenario.json's header). The bridge's removal is judged by its "
                      "DestroyTargets objective (the objective's state, Scenario Rules), the tank's loss by its own and by Health. ⚠ No golden: the obstacle "
                      "bake's wall-clock pacing (R-244) makes the tick of the rebake machine-dependent.",
              "runFor": 130, "golden": False, "anchors": {}, "expect": rows}
    write("bl-blow-the-bridge", what, e, checks)


if __name__ == "__main__":
    crossing("bl-placed-bridge", True)
    crossing("bl-placed-bridge-absent", False)
    blow()
    print("wrote", ROOT)
