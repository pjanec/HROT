#!/usr/bin/env python3
"""The command-chain battalion test (CE-4615; docs/designs/group-maneuvers/DESIGN_Command_Chain_Battalion_Test.md) — a battalion slice with
REAL squads on demo-city. Writes scenarios/demos/platoons-and-vehicles/cc-battalion{,-stall,-collapse}/scenario.json.

Blue (west): a battalion HQ (SupportCommand) with a 2-tube 81 mm section and its crews, over a company of three platoons (assault /
base of fire / reserve, 2 squads x 4) and two IFVs on the start line. The company is ordered CompanyAttack at t = 30 s:
Objective 1 = the office cluster (425, 425), Objective 2 = the shop column (645, 445).
Red (east): a battalion HQ (SupportCommand, Reserve = its reserve platoon's slot, CE-4589) over a company on CompanyDefend from t = 0 — a
LINE facing west at Objective 1 (two blocks across the street, the company reserve behind, its fall-back on Objective 2) — its own reserve
platoon held back north-east, and a 2-man outpost forward on the axis.
⭐ R-2096 (lane decision): CompanyDefend builds a LINE (blocks across the threat), not two blocks in depth — so "block 1" is the line at
Objective 1 and "block 2" its depth / fall-back at Objective 2; every CompanyDefend feature the chain needs (a LOST block counter-attacked,
Red => Reinforce, the fall-back) is kept.
End: Blue holds Objective 2 for 120 s (HoldArea) · Red below 30 % of its men (Strength, CE-4614) · t = 15 min.

    python3 scripts/gen-chain-scenarios.py
"""
import copy
import json
import os

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "scenarios", "demos", "platoons-and-vehicles")

# ── the coordinates (measured on demo-city's houses, 2026-11-14: offices at x 411-444 / y 411-502, shops at x 639 / y 416-486) ──
OBJ1 = (425.0, 425.0)          # the office cluster — Red's line, Blue's Objective 1
OBJ2 = (645.0, 445.0)          # the shop column — Red's depth / fall-back, Blue's Objective 2
BLUE_START = (240.0, 405.0)    # Blue's company assembles here (185 m short of Objective 1 — the design's start line ≈ 150 m; R-2097 rev. 2: at 275 m the bounding assault (≈ 0.8 m/s, measured) reached its assault position only after the deadline's bypass)
START_LINE_X = 205.0           # the IFVs overwatch from the rear of the assembly (at 70 m they killed the outpost within 10 s, measured)
OUTPOST = (380.0, 410.0)       # Red's outpost, 45 m forward of its line on the axis
RED_RESERVE = (720.0, 620.0)   # Red's battalion reserve, north-east, out of the fight
BLUE_HQ = (60.0, 400.0)
RED_HQ = (760.0, 640.0)
DEADLINE = 300.0               # CompanyAttack's tempo (R-2097 rev. 2: the lean 180 s bypassed Objective 1 at 270 s, before the assault could arrive)
FRONTAGE, DEPTH = 160.0, 220.0  # the Red line: blocks at y ≈ 385 / 465, its reserve at x ≈ 535, the fall-back at x ≈ 645

EAST = [0, 0, 0, 1]
WEST = [0, 0, 1, 0]
CHECK = "Check: python3 scripts/utility-demo-check.py --launch {name}. 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Battalion_Test.md (CE-4615)"


class Scn:
    def __init__(self):
        self.ents = {}
        self.n = 0

    def add(self, e):
        self.n += 1
        g = f"cc461500-0000-4000-8000-{self.n:012d}"
        self.ents[g] = e
        return g


def at(x, y, z=0.0, rot=EAST):
    return {"Position": [round(x, 2), round(y, 2), z], "Rotation": rot}


def ent(tkb, name, side, x, y, rot=EAST, commander=None, designation="Wingman", behavior=None, man=False, extra=None):
    e = {"SimTransform": at(x, y, rot=rot), "TkbIdentity": {"TkbType": tkb}, "EntityInfo": {"Name": name, "ForceId": side}}
    if man:
        e["Health"] = {"Current": 100, "Max": 100}
        e["TargetMemory"] = {"Entries": []}
    if commander:
        e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": designation}
    if behavior:
        e["Behavior"] = {"Name": behavior[0], "Params": behavior[1], "Origin": "Superior"}
    if extra:
        e.update(extra)
    return e


def squad(s, name, side, x, y, commander, men=4, rot=EAST, soldier=2002):
    sq = s.add(ent(302, name, side, x, y, rot, commander))
    roles = ["SquadLeader", "MachineGunner", "Wingman", "Wingman", "Wingman"]
    for i in range(men):
        label = "Leader" if i == 0 else f"Rifleman {i}"
        s.add(ent(soldier, f"{name} {label}", side, x + (i - (men - 1) / 2) * 2.0, y - 2.0, rot, sq, roles[min(i, 4)], man=True))
    return sq


def platoon(s, name, side, x, y, commander, squads=2, men=4, rot=EAST, soldier=2002, behavior=None):
    pl = s.add(ent(305, name, side, x, y, rot, commander, behavior=behavior))
    for k in range(squads):
        squad(s, f"{name} Squad {k + 1}", side, x, y + (k - (squads - 1) / 2) * 14.0, pl, men, rot, soldier)
    return pl


def circle(c, r):
    return {"circle": {"center": [c[0], c[1], 0], "radius": r}}


def objective(name, side, behavior, params):
    return {"TkbIdentity": {"TkbType": 8860}, "EntityInfo": {"Name": name, "ForceId": side},
            "Behavior": {"Name": behavior, "Params": params, "Origin": "Superior"}}


def trigger(title, side, when, do):
    return {"TkbIdentity": {"TkbType": 8861}, "EntityInfo": {"Name": title, "ForceId": side},
            "Behavior": {"Name": "Trigger", "Params": {"common": {"title": title}, "when": when, "do": do}, "Origin": "Superior"}}


def at_time(seconds):
    return {"$type": "ScenarioTime", "fromSeconds": seconds}


def achieved(objective_guid):
    return {"$type": "OnEntity", "ref": objective_guid, "predicate": {"$type": "PropertyMatch", "componentType": "ObjectiveStatus", "propertyPath": "State",
                                                                     "operator": "Equals", "predicate": {"$type": "Numeric", "minValue": 2, "maxValue": 2}}}


def line(name, side, pts):
    cx, cy = pts[0]
    return {"EditablePolyline": {"Points": [{"X": px - cx, "Y": py - cy} for px, py in pts], "Version": 0}, "SimTransform": at(cx, cy),
            "TkbIdentity": {"TkbType": 8801}, "EntityInfo": {"Name": name, "ForceId": side},
            "MapOverlayStyle": {"FillColor": [0, 0, 0, 0], "BorderColor": [0, 0, 0, 255], "LineThickness": 3, "IsClosed": False}}


# variant → (Red block squads, Red block men, Red company reserve squads, its men, the description's "proves")
VARIANTS = {
    "cc-battalion": dict(block_squads=2, block_men=4, res_squads=2, res_men=4,
                         proves="the whole chain end to end: the outpost SPOTs and withdraws, Blue's base of fire Sets, the assault goes in, "
                                "fires are asked and land on the reported point, a Red block is LOST and counter-attacked, Red goes RED and asks "
                                "for reinforcement, the battalion sends its reserve, and Blue takes Objective 2 or Red falls below 30 %"),
    "cc-battalion-stall": dict(block_squads=3, block_men=4, res_squads=2, res_men=4,
                               proves="Blue's TEMPO against a line one squad stronger per block: fires at half the deadline, the reserve committed "
                                      "at the deadline, the bypass at 1.5x — each AFTER the Set or failure that precedes it"),
    "cc-battalion-collapse": dict(block_squads=1, block_men=3, res_squads=1, res_men=3,
                                  proves="Red's chain: a thin line is LOST => the weak company reserve counter-attacks; the company goes RED => "
                                         "REQUEST Reinforce => the battalion HQ sends its reserve platoon (CompanyDefend / PlatoonDefend by echelon)"),
}


def build(name, v):
    s = Scn()
    F, H = "Friend", "Hostile"
    # ── Blue ──
    bhq = s.add(ent(306, "Blue Battalion HQ", F, *BLUE_HQ, behavior=("SupportCommand", {})))
    bco = s.add(ent(306, "Blue Company", F, *BLUE_START, commander=bhq))   # ordered CompanyAttack at t = 30 s (the trigger below)
    platoon(s, "Blue 1st Platoon", F, BLUE_START[0], BLUE_START[1] - 40, bco)   # slot 0: the assault
    platoon(s, "Blue 2nd Platoon", F, BLUE_START[0] + 20, BLUE_START[1], bco)   # slot 1: the base of fire
    platoon(s, "Blue 3rd Platoon", F, BLUE_START[0] - 30, BLUE_START[1] + 40, bco)   # slot 2: the reserve
    battery = s.add(ent(304, "Blue Mortar Section", F, BLUE_HQ[0] - 20, BLUE_HQ[1] - 30, commander=bhq))
    for i in range(2):
        tx = BLUE_HQ[0] - 26 + 12 * i
        tube = s.add(ent(610, f"Blue Mortar Tube {i + 1}", F, tx, BLUE_HQ[1] - 30))
        s.add(ent(2002, f"Blue Mortar {i + 1} Gunner", F, tx - 2, BLUE_HQ[1] - 30, commander=battery,
                  designation="SquadLeader" if i == 0 else "Wingman", behavior=("ManWeapon", {"weapon": tube}), man=True))
        s.add(ent(2002, f"Blue Mortar {i + 1} Loader", F, tx - 2, BLUE_HQ[1] - 33, commander=battery, man=True))
    for i, dy in enumerate((-12, 12)):   # the IFVs overwatch from the start line (their own SOP; no commander — R-2096)
        s.add(ent(101, f"Blue IFV {i + 1}", F, START_LINE_X, BLUE_START[1] + dy, man=False,
                  extra={"TargetMemory": {"Entries": []}, "Sop": {"Name": "BasicVehicleSop", "Params": {}, "Origin": "Superior"}}))
    # ── Red ──
    rhq = s.add(ent(306, "Red Battalion HQ", H, *RED_HQ, rot=WEST, behavior=("SupportCommand", {"Reserve": 2})))   # slot 1 = its reserve
    rco = s.add(ent(306, "Red Company", H, OBJ1[0] + 110, OBJ1[1], rot=WEST, commander=rhq,
                    behavior=("CompanyDefend", {"Position": [OBJ1[0], OBJ1[1], 0], "Threat": [BLUE_START[0], BLUE_START[1], 0],
                                                "Frontage": FRONTAGE, "Depth": DEPTH})))
    reserve = s.add(ent(305, "Red Battalion Reserve", H, *RED_RESERVE, rot=WEST, commander=rhq))   # slot 1: the battalion's reserve (CE-4589)
    for k in range(2):
        squad(s, f"Red Battalion Reserve Squad {k + 1}", H, RED_RESERVE[0], RED_RESERVE[1] + (k - 0.5) * 14, reserve, 4, WEST, 2003)
    outpost = s.add(ent(302, "Red Outpost", H, *OUTPOST, rot=WEST, commander=rhq))
    for i in range(2):
        s.add(ent(2003, f"Red Outpost {'Leader' if i == 0 else 'Rifleman 1'}", H, OUTPOST[0], OUTPOST[1] + 3 * i, WEST, outpost,
                  "SquadLeader" if i == 0 else "Wingman", man=True))
    platoon(s, "Red 1st Platoon", H, OBJ1[0], OBJ1[1] - FRONTAGE / 4, rco, v["block_squads"], v["block_men"], WEST, 2003)   # block (south)
    platoon(s, "Red 2nd Platoon", H, OBJ1[0], OBJ1[1] + FRONTAGE / 4, rco, v["block_squads"], v["block_men"], WEST, 2003)   # block (north)
    platoon(s, "Red 3rd Platoon", H, OBJ1[0] + DEPTH / 2, OBJ1[1], rco, v["res_squads"], v["res_men"], WEST, 2003)         # company reserve
    # ── the scripted top: phase lines, objectives, the attack order, the end ──
    s.add(line("PL 1", F, [(OBJ1[0], OBJ1[1] - 150), (OBJ1[0], OBJ1[1] + 150)]))
    s.add(line("PL 2", F, [(OBJ2[0], OBJ2[1] - 150), (OBJ2[0], OBJ2[1] + 150)]))
    pl1 = s.add(objective("Blue reaches PL 1", F, "ReachArea", {"common": {"title": "Blue reaches PL 1", "subject": bco, "deadlineSeconds": 420},
                                                               "area": circle(OBJ1, 60), "minMen": 4}))
    pl2 = s.add(objective("Blue reaches PL 2", F, "ReachArea", {"common": {"title": "Blue reaches PL 2", "subject": bco, "deadlineSeconds": 720},
                                                               "area": circle(OBJ2, 60), "minMen": 4}))
    hold = s.add(objective("Blue holds Objective 2", F, "HoldArea", {"common": {"title": "Blue holds Objective 2", "subject": bco, "required": True},
                                                                    "area": circle(OBJ2, 60), "minMen": 4, "maxEnemy": 0, "holdSeconds": 120}))
    broken = s.add(objective("Red below 30 %", F, "Strength", {"common": {"title": "Red below 30 %", "subject": rhq}, "threshold": 0.3}))
    # ⭐ R-2101 (lane decision) — the outpost's withdrawal: Defend & Attack §4.2's OP play is designed, not built (CE-2435), so the scenario
    #   carries the rule — Blue closing within 100 m of the outpost (a condition, never a clock) orders it back behind the line
    close = s.add(objective("Blue closes on the outpost", H, "ReachArea", {"common": {"title": "Blue closes on the outpost", "subject": bco, "scored": False},
                                                                         "area": circle(OUTPOST, 100), "minMen": 1}))
    s.add(trigger("The outpost withdraws", H, achieved(close),
                  [{"act": "Order", "args": {"target": outpost, "behavior": "Order.Move",   # back behind the line (a MOVE is accepted while it runs; a
                                             "params": {"Location": [OBJ1[0] + 60, OBJ1[1], 0], "Speed": 4}}}]))   #  one-shot BreakContact ended unseen)
    s.add(trigger("Blue Company attacks", F, at_time(30),
                  [{"act": "Message", "args": {"toKind": "All", "text": "Blue Company attacks: Objective 1, then Objective 2", "seconds": 8}},
                   {"act": "Order", "args": {"target": bco, "behavior": "CompanyAttack",
                                             "params": {"Objective1": [OBJ1[0], OBJ1[1], 0], "Objective2": [OBJ2[0], OBJ2[1], 0],
                                                        "DeadlineSeconds": DEADLINE,
                                                        # R-2097 rev. 3 — the objective is the BLOCK (3–4 buildings within 35 m), not the
                                                        #   default 80 m (≈ 8): clearing every building first ran past the 1.5 × bypass (run 6)
                                                        "Radius": 35}}}]))
    s.add(trigger("The end", F, {"$type": "Compound", "operator": "Or", "conditions": [achieved(hold), achieved(broken), at_time(900)]},
                  [{"act": "EndExercise", "args": {"reason": "Objective 2 held, Red below 30 %, or 15 min"}}]))
    doc = {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
           "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "demo-city", "description": (
               f"What: CE-4615 (the command chain on a battalion slice, real squads, ~{sum(1 for e in s.ents.values() if e.get('Health'))} men) - on demo-city, "
               "'Blue Battalion HQ' (SupportCommand, 2 x 81 mm) over 'Blue Company' (three platoons of 2 x 4, 2 IFVs on the start line), ordered "
               f"CompanyAttack at 30 s on Objective 1 ({OBJ1[0]:.0f},{OBJ1[1]:.0f}) then Objective 2 ({OBJ2[0]:.0f},{OBJ2[1]:.0f}), deadline {DEADLINE:.0f} s; "
               "'Red Battalion HQ' (SupportCommand, its reserve platoon held back) over 'Red Company' (CompanyDefend: a line facing west at "
               "Objective 1, its depth and fall-back at Objective 2) and a 2-man 'Red Outpost' forward. No traffic, no civilians. "
               f"Judge: {v['proves']} — each arrow found in /squad/reports and the order journal IN ORDER and CAUSED by the one before. "
               "End: Blue holds Objective 2 for 120 s, Red below 30 %, or 15 min. " + CHECK.format(name=name))},
           "entities": s.ents}
    return doc


def main():
    for name, v in VARIANTS.items():
        out = os.path.join(OUT, name)
        os.makedirs(out, exist_ok=True)
        with open(os.path.join(out, "scenario.json"), "w") as fh:
            json.dump(build(name, v), fh, indent=1)
        print("wrote", out)


if __name__ == "__main__":
    main()
