#!/usr/bin/env python3
"""Writes the SHOWCASE "Bridgehead" scenario (docs/SHOWCASE_Scenarios.md §1) and the macros it uses.

  python3 scripts/gen-showcase-bridgehead.py

Everything the scenario does is DATA — objectives, triggers, macros, variants — nothing compiled per scenario (R-4112).
Re-run after editing; the files are generated, edit this script, not them.

  scenarios/showcase-bridgehead/scenario.json   the exercise (cqb-village)
  macros/forces/red-qrf/scenario.json           an 8-man Red platoon (2 squads), key @RedQRF — inserted by the counter-attack trigger
  macros/forces/red-outpost/scenario.json       a 2-man Red outpost that defends its anchor — placed at one of 3 places (variant)
  macros/inject/civilian-car/scenario.json      a civilian car + driver that drives 150 m along its heading — the checkpoint inject
"""
import json, math, os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")


def rot(deg):  # compass heading (0 = north, clockwise) → quaternion about Z (forward = +X), as scripts/scenario_builder.py
    yaw = math.radians(90 - deg)
    return [0, 0, round(math.sin(yaw / 2), 7), round(math.cos(yaw / 2), 7)]


def write(path, doc):
    full = os.path.join(ROOT, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w") as f:
        json.dump(doc, f, indent=1)
        f.write("\n")


def doc(header, ents):
    h = {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0"}
    h.update(header)
    return {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1}, "header": h, "entities": ents}


class Ids:
    def __init__(self, prefix):
        self.prefix, self.n = prefix, 0

    def __call__(self):
        self.n += 1
        return f"{self.prefix}-0000-4000-8000-{self.n:012d}"


def man(name, force, at, facing, commander=None, designation="Wingman", health=100, bind_side=False):
    e = {"SimTransform": {"Position": list(at), "Rotation": rot(facing)},
         "Health": {"Current": health, "Max": health},
         "TkbIdentity": {"TkbType": 2002},
         "EntityInfo": {"Name": name, "ForceId": force},
         "TargetMemory": {"Entries": []}}
    if commander:
        e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": designation}
    if bind_side:
        e["ParamBindings"] = {"Bindings": {"EntityInfo.ForceId": "side"}}
    return e


def unit(tkb, name, force, at, facing=0, commander=None, behavior=None, key=None, bind_side=False):
    e = {"SimTransform": {"Position": list(at), "Rotation": rot(facing)},
         "TkbIdentity": {"TkbType": tkb},
         "EntityInfo": {"Name": name, "ForceId": force}}
    if commander:
        e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": "Wingman"}
    if behavior:
        e["Behavior"] = {"Name": behavior[0], "Params": json.dumps(behavior[1]), "Origin": "Superior"}
    if key:
        e["RefKey"] = {"Value": key}
    if bind_side:
        e["ParamBindings"] = {"Bindings": {"EntityInfo.ForceId": "side"}}
    return e


def squad_of(ents, ids, name, force, at, facing, men, commander=None, behavior=None, key=None, bind_side=False, spacing=1.6):
    sq = ids()
    ents[sq] = unit(302, name, force, at, facing, commander, behavior, key, bind_side)
    x0 = at[0] - spacing * (men - 1) / 2
    for i in range(men):
        role = "SquadLeader" if i == 0 else ("MachineGunner" if i == 1 else "Wingman")
        label = "Leader" if i == 0 else ("MG" if i == 1 else f"Rifleman {i - 1}")
        ents[ids()] = man(f"{name} {label}", force, (round(x0 + i * spacing, 2), at[1] - 1, 0), facing, sq, role, bind_side=bind_side)
    return sq


# ════════════════════════════════════════ the macros ════════════════════════════════════════

def macro_red_qrf():
    ids, ents = Ids("b41d0b00"), {}
    pl = ids()
    ents[pl] = unit(305, "Red QRF", "Hostile", (0, 0, 0), 0, key="RedQRF", bind_side=True)
    squad_of(ents, ids, "Red QRF Squad 1", "Hostile", (-4, 0, 0), 0, 4, commander=pl, bind_side=True)
    squad_of(ents, ids, "Red QRF Squad 2", "Hostile", (4, -6, 0), 0, 4, commander=pl, bind_side=True)
    write("macros/forces/red-qrf/scenario.json", doc({
        "Kind": "macro",
        "description": "What: SHOWCASE Bridgehead (docs/SHOWCASE_Scenarios.md §1) - the Red quick-reaction force: a body-less platoon 'Red QRF' (key @RedQRF) of two 4-man squads, standing idle round its anchor, waiting for an order. Judge: placed or inserted, 8 men + 2 squads + the platoon appear round the anchor, all of side 'side'; @RedQRF resolves to the platoon (an Order step names it).",
        "Anchor": {"Position": [0, 0, 0], "Heading": 0},
        "Params": [{"Name": "side", "TypeId": "ForceId", "Default": "Hostile"}],
    }, ents))


def macro_red_outpost():
    ids, ents = Ids("b41d0a00"), {}
    sq = ids()
    ents[sq] = unit(302, "Red Outpost", "Hostile", (0, 0, 0), 180,
                    behavior=("SquadDefend", {"Position": [0, 0, 0], "SearchRadius": 8, "Speed": 2}), bind_side=True)
    ents[ids()] = man("Red Outpost Leader", "Hostile", (-1.5, 0, 0), 180, sq, "SquadLeader", bind_side=True)
    ents[ids()] = man("Red Outpost Rifleman", "Hostile", (1.5, 0, 0), 180, sq, "Wingman", bind_side=True)
    write("macros/forces/red-outpost/scenario.json", doc({
        "Kind": "macro",
        "description": "What: SHOWCASE Bridgehead (docs/SHOWCASE_Scenarios.md §1) - a 2-man outpost (a squad running SquadDefend at the anchor, facing the anchor's heading). Judge: wherever it is placed, the squad's SquadDefend Position is the instance's anchor (the expander relocates the point inside the params) and both men stand there, of side 'side'.",
        "Anchor": {"Position": [0, 0, 0], "Heading": 180},
        "Params": [{"Name": "side", "TypeId": "ForceId", "Default": "Hostile"}],
    }, ents))


def macro_civilian_car():
    ids, ents = Ids("b41d0c00"), {}
    car, driver = ids(), ids()
    ents[car] = {"SimTransform": {"Position": [0, 0, 0], "Rotation": rot(90)},
                 "TkbIdentity": {"TkbType": 1002},
                 "EntityInfo": {"Name": "Civilian car", "ForceId": "Neutral"},
                 "RefKey": {"Value": "CivCar"},
                 "PassengerBuffer": {"Count": 1, "Passengers": [driver], "Seats": [0]},
                 "Behavior": {"Name": "MoveToLocation", "Params": json.dumps({"target": [150, 0, 0], "speed": 8, "arrivalRadius": 3}), "Origin": "Superior"}}
    ents[driver] = {"SimTransform": {"Position": [0, 0, 0], "Rotation": rot(90)},
                    "Health": {"Current": 100, "Max": 100},
                    "TkbIdentity": {"TkbType": 1101},
                    "EntityInfo": {"Name": "Civilian driver", "ForceId": "Neutral"},
                    "RefKey": {"Value": "CivDriver"},
                    "IsEmbarkedTag": {"Vehicle": car, "Seat": 0}}
    write("macros/inject/civilian-car/scenario.json", doc({
        "Kind": "macro",
        "description": "What: SHOWCASE Bridgehead (docs/SHOWCASE_Scenarios.md §1) - the checkpoint INJECT: a civilian car (key @CivCar) with its Neutral driver (key @CivDriver) that drives 150 m along the anchor's heading at 8 m/s and stops. Judge: inserted at a point facing east, the car moves ~150 m east and stops; nobody in it is armed.",
        "Anchor": {"Position": [0, 0, 0], "Heading": 90},
    }, ents))


# ════════════════════════════════════════ the scenario ════════════════════════════════════════

CROSSROADS = [198, 182, 0]          # Main Street (y 184..196) × the Cross Road (x 194..206), with H3 and H5 inside
CROSSROADS_R = 42
PHASE_LINE = [125, 95, 0]           # phase line RED: the open ground in front of OBJ, past the support-line berm
PHASE_LINE_R = 40
OUTPOST_PLACES = [{"at": [65, 114, 0], "heading": 180},   # behind the low wall
                  {"at": [167, 86, 0], "heading": 180},   # behind the berm
                  {"at": [110, 104, 0], "heading": 180}]  # by the wreck
QRF_EDGES = [{"at": [322, 120, 0]}, {"at": [8, 120, 0]}]  # east OR west (variant)
CAR_FROM = [8, 190, 0]              # Main Street's west end; it drives 150 m east, stopping ~40 m short of the crossroads


def state_is(ref, lo, hi=None):
    return {"$type": "OnEntity", "ref": ref, "predicate": {"$type": "PropertyMatch", "componentType": "ObjectiveStatus", "propertyPath": "State",
            "operator": "Equals", "predicate": {"$type": "Numeric", "minValue": lo, "maxValue": lo if hi is None else hi}}}


def score_between(ref, lo, hi):
    return {"$type": "OnEntity", "ref": ref, "predicate": {"$type": "PropertyMatch", "componentType": "ObjectiveStatus", "propertyPath": "Score",
            "operator": "Equals", "predicate": {"$type": "Numeric", "minValue": lo, "maxValue": hi}}}


def time_from(s, to=None):
    t = {"$type": "ScenarioTime", "fromSeconds": s}
    if to is not None:
        t["toSeconds"] = to
    return t


def all_of(*c):
    return {"$type": "Compound", "operator": "And", "conditions": list(c)}


def any_of(*c):
    return {"$type": "Compound", "operator": "Or", "conditions": list(c)}


def msg(text, to="All", seconds=8):
    # ⚠ "Side" (Blue only) needs the side's wire number in toValue — the HUD that filters it is ui's; "All" until then
    return {"act": "Message", "args": {"toKind": to, "text": text, "seconds": seconds}}


def scenario():
    ids, ents = Ids("b41d6e00"), {}

    # ── Blue: a body-less platoon of three squads in the assembly area 400 m … here ~90 m south of phase line RED (the map is 330 × 232)
    blue = ids()
    ents[blue] = unit(305, "Blue Platoon", "Friend", (125, 4, 0), 0, key="BluePlatoon",
                      behavior=("SquadMarch", {"Objective": PHASE_LINE, "Shape": 1, "MaxLagMetres": 40, "ArrivalRadius": 12}))
    for i, x in enumerate((105, 125, 145), 1):
        squad_of(ents, ids, f"Blue Squad {i}", "Friend", (x, 10, 0), 0, 4, commander=blue)

    # ── Red: the crossroads' defenders (6) in H3 and H5, the outpost (2, one of three places), the QRF's start (east or west)
    red = ids()
    ents[red] = unit(305, "Red Defenders", "Hostile", (200, 214, 0), 180, key="RedDefenders",
                     behavior=("PlatoonDefend", {"Position": CROSSROADS, "Left": 135, "Right": 225, "MaxRange": 160, "Speed": 2,
                                                 "Squads": [{"Squad": 1, "Point": [165, 160, 0], "SearchRadius": 10},
                                                            {"Squad": 2, "Point": [230, 204, 0], "SearchRadius": 10}]}))
    squad_of(ents, ids, "Red Squad H3", "Hostile", (165, 160, 0), 180, 3, commander=red)
    squad_of(ents, ids, "Red Squad H5", "Hostile", (230, 204, 0), 180, 3, commander=red)

    outpost = ids()
    ents[outpost] = {"SimTransform": {"Position": OUTPOST_PLACES[0]["at"], "Rotation": rot(180)},
                     "TkbIdentity": {"TkbType": 8813}, "EntityInfo": {"Name": "Red outpost"},
                     "MacroInstance": {"Macro": "forces/red-outpost", "Args": {"side": "Hostile"}},
                     "Positions": OUTPOST_PLACES}
    qrf_from = ids()
    ents[qrf_from] = {"SimTransform": {"Position": QRF_EDGES[0]["at"], "Rotation": [0, 0, 0, 1]},
                      "TkbIdentity": {"TkbType": 8803}, "EntityInfo": {"Name": "QRF start", "ForceId": "Hostile"},
                      "RefKey": {"Value": "QrfFrom"},
                      "EditablePolyline": {"Points": [{"X": -6, "Y": -6}, {"X": -6, "Y": 6}, {"X": 6, "Y": 6}, {"X": 6, "Y": -6}], "Version": 0},
                      "MapOverlayStyle": {"FillColor": [255, 0, 0, 60], "BorderColor": [255, 80, 80, 255], "LineThickness": 2, "IsClosed": True},
                      "Positions": QRF_EDGES}

    # ── villagers on Main Street, each present with probability 0.7 (variant)
    for i, at in enumerate([(150, 190), (160, 188), (185, 192), (212, 189), (240, 191), (255, 188)], 1):
        g = ids()
        ents[g] = {"SimTransform": {"Position": [at[0], at[1], 0], "Rotation": rot(0)}, "Health": {"Current": 100, "Max": 100},
                   "TkbIdentity": {"TkbType": 1101}, "EntityInfo": {"Name": f"Villager {i}", "ForceId": "Neutral"},
                   "Behavior": {"Name": "CivilianHsm", "Params": json.dumps({"WanderRadius": 6, "PauseMinSeconds": 10, "PauseMaxSeconds": 30}), "Origin": "Superior"},
                   "Presence": 0.7}

    # ── the evaluation tree (§1.2): mission 40 % · conduct 30 % · members 30 % (rules S4's MemberEvaluation)
    def objective(kind, title, params, key=None):
        g = ids()
        ents[g] = {"TkbIdentity": {"TkbType": 8860}, "EntityInfo": {"Name": title, "ForceId": "Friend"},
                   "Behavior": {"Name": kind, "Params": json.dumps(params), "Origin": "Superior"}}
        if key:
            ents[g]["RefKey"] = {"Value": key}
        return g

    def common(title, parent=None, **kw):
        c = {"title": title, "subject": blue}
        if parent:
            c["parent"] = parent
        c.update(kw)
        return c

    root = objective("WeightedMean", "Bridgehead", {"common": common("Bridgehead"), "passScore": 0.6}, key="Bridgehead")
    mission = objective("WeightedMean", "Mission", {"common": common("Mission", root, weight=0.4), "passScore": 0.6})
    conduct = objective("WeightedMean", "Conduct", {"common": common("Conduct", root, weight=0.3, required=True), "passScore": 0.6})   # ⭐ §1.6 line 4: No harm fails the TREE
    reach = objective("ReachArea", "Reach phase line RED", {"common": common("Reach phase line RED", mission, deadlineSeconds=900, weight=1),
                      "area": {"circle": {"center": PHASE_LINE, "radius": PHASE_LINE_R}}, "minMen": 4}, key="PhaseLineRed")
    clear = objective("ClearAreaObjective", "Clear the crossroads", {"common": common("Clear the crossroads", mission, required=True, startActive=False, weight=1),
                      "area": {"circle": {"center": CROSSROADS, "radius": CROSSROADS_R}}, "enemySide": "Hostile", "maxEnemy": 0, "forSeconds": 30},
                      key="ClearCrossroads")
    hold = objective("HoldArea", "Hold the crossroads", {"common": common("Hold the crossroads", mission, required=True, startActive=False, weight=1),
                     "area": {"circle": {"center": CROSSROADS, "radius": CROSSROADS_R}}, "minMen": 6, "maxEnemy": 0, "holdSeconds": 600},
                     key="HoldCrossroads")
    objective("NoHarm", "No harm to civilians", {"common": common("No harm to civilians", conduct, required=True), "protected": "Neutral", "maxCount": 0})
    objective("NoHarm", "No fratricide", {"common": common("No fratricide", conduct, required=True), "protected": "Friend", "maxCount": 0})
    # ⭐ rules S4 (behaviors, DESIGN_Scenario_Rules §00.11 S4 E-1): the per-member standards — one evaluation macro per member of the platoon
    #   (SR-81: by key / designation / class, else Default) from the shipped library, rolled up the chain under "Members"
    objective("MemberEvaluation", "Members", {"common": common("Members", root, weight=0.3), "library": "macros/evaluation/infantry"})

    # ── the triggers (§1.3) — data, each a Trigger entity
    def trigger(title, when, do, **kw):
        g = ids()
        p = {"common": {"title": title, "subject": blue}, "when": when, "do": do}
        p.update(kw)
        ents[g] = {"TkbIdentity": {"TkbType": 8861}, "EntityInfo": {"Name": title, "ForceId": "Friend"},
                   "Behavior": {"Name": "Trigger", "Params": json.dumps(p), "Origin": "Superior"}}
        return g

    trigger("Briefing", time_from(5), [msg("Seize the crossroads, hold it, set up a checkpoint. Civilians in the village.", seconds=12)])
    trigger("Phase 2 starts", state_is(reach, 2), [
        {"act": "SetObjectiveActive", "args": {"target": clear, "active": True}},
        {"act": "Order", "args": {"target": blue, "behavior": "PlatoonAttack",
                                  "params": {"Position": CROSSROADS, "SearchRadius": 35, "Preset": "AttackPosition", "Speed": 2}}},
        msg("Phase line RED - assault the crossroads")])
    trigger("Phase 3 starts", state_is(clear, 2), [
        {"act": "SetObjectiveActive", "args": {"target": hold, "active": True}},
        {"act": "Order", "args": {"target": blue, "behavior": "PlatoonDefend",
                                  "params": {"Position": CROSSROADS, "Left": 45, "Right": 315, "MaxRange": 150, "Speed": 2}}},
        msg("Crossroads clear - hold and set up a checkpoint")])
    trigger("Counter-attack", state_is(hold, 1), [
        {"act": "InsertMacro", "args": {"macro": "forces/red-qrf", "at": "@QrfFrom", "heading": 0, "params": {"side": "Hostile"}}},
        {"act": "Order", "args": {"target": "@RedQRF", "behavior": "PlatoonAttack",
                                  "params": {"Position": CROSSROADS, "SearchRadius": 35, "Preset": "AttackPosition", "Speed": 3}}},
        msg("Enemy moving towards you")], delay=120)
    trigger("Checkpoint inject", state_is(hold, 1), [
        {"act": "InsertMacro", "args": {"macro": "inject/civilian-car", "at": CAR_FROM, "heading": 90}},
        msg("Vehicle approaching the checkpoint fast")], delay=180)
    trigger("Ease off", all_of(score_between(root, -1, 0.3499), time_from(1200)), [
        {"act": "SetSkillAmplifier", "args": {"side": "Hostile", "tiers": -1}},
        msg("Red eased", to="Instructor")], holdFor=120, repeat=True, cooldown=600, maxFires=2)
    trigger("Push harder", all_of(score_between(root, 0.8001, 1), time_from(0, 1500)), [
        {"act": "SetSkillAmplifier", "args": {"side": "Hostile", "tiers": 1}},
        msg("Red pushed harder", to="Instructor")], holdFor=60)
    trigger("End", any_of(state_is(hold, 2), time_from(2700)), [
        msg("Exercise complete", to="All"),
        {"act": "EndExercise", "args": {"reason": "the crossroads is held, or 45 min"}}])
    # ⭐ Rules_Runnable ③ (SR-116): the instructor's End — RefKey "instructor-end", fired by hand (RuleControl Fire; ui's End button); its `when` never
    #   holds by itself. The same EndExercise as the scenario's own end; the training verdict (SR-122) decides who passed
    ie = trigger("Instructor end", time_from(1e9), [msg("The instructor ended the exercise", to="All"),
                                                     {"act": "EndExercise", "args": {"reason": "Instructor ended"}}])
    ents[ie]["RefKey"] = {"Value": "instructor-end"}

    write("scenarios/showcase-bridgehead/scenario.json", doc({
        # ⭐ P-7 (DESIGN_Node_Features.md §11, F-27): this scenario's feature preset — the scenario layer under the node / cluster overrides (ui's lists, PH-8)
        "features": {"game.tickets": "off", "game.capture-points": "off", "game.round-timer": "off", "game.score-popups": "off", "game.bounty-marker": "off", "game.scoreboard": "off", "hud.kill-feed": "off"},
        "terrainName": "cqb-village",
        "description": (
            "What: SHOWCASE 'Bridgehead' (docs/SHOWCASE_Scenarios.md §1) - military training, AI on both sides (the trainees possess Blue men when "
            "they join). 'Blue Platoon' (3 squads x 4) marches to phase line RED (the open ground at (125,95)); a 2-man Red outpost stands at one of "
            "three places (variant); 6 Red defenders hold the crossroads (198,182) from H3 and H5; villagers (each 0.7) on Main Street. The phases are "
            "a CHAIN of objectives: Reach phase line RED -> (trigger) Clear the crossroads switched on + Blue ordered PlatoonAttack -> (trigger) Hold the "
            "crossroads switched on + Blue ordered PlatoonDefend -> 120 s later the Red QRF (8, macro forces/red-qrf) is inserted at @QrfFrom (east or "
            "west, variant) and ordered PlatoonAttack; 180 s after Hold a civilian car (macro inject/civilian-car) drives in along Main Street. The ease-off "
            "director cuts Red one tier when the Bridgehead score stays < 0.35 past 20 min (at most twice, 10 min apart); End = Hold achieved or 45 min. "
            "Judge: (1) Clear is Inactive until Reach is Achieved, Hold Inactive until Clear is Achieved; (2) the QRF exists ~120 s after Hold activated, "
            "at the variant's edge, running PlatoonAttack; (3) one variant twice = the same outpost place, QRF edge and villagers (rail "
            "ShowcaseBridgeheadTests); (4) the ease-off fires at most twice and Red's SideTiers reads -1 after it; (5) no Blue round hits a Blue man. "
            "Generated by scripts/gen-showcase-bridgehead.py."),
    }, ents))


if __name__ == "__main__":
    macro_red_qrf()
    macro_red_outpost()
    macro_civilian_car()
    scenario()
    print("wrote scenarios/showcase-bridgehead + macros/forces/red-qrf, forces/red-outpost, inject/civilian-car")
