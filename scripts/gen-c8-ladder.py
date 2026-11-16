#!/usr/bin/env python3
"""CE-8009 (C-8, RV-3) — the PHYSICAL rungs of the stochastic units' calibration ladder: 9 v 9 riflemen in the open at 100 / 200 / 275 m.

Writes scenarios/tests/c8-ladder/c8-open-<range>/scenario.json + checks.json. Each side is a body-less squad (the TKB InfantrySquad, its SOP by
default) and nine standard riflemen (Infantry_Rifleman, 200) abreast, 5 m apart, facing the other side across open ground (basic-desert). The checks are the
MEASUREMENT, not a verdict: for k = 1…9, the first time k men of a side are DOWN (INCAPACITATED: Health below 30 % — the soldier's Gen.Injury line, out of the fight; not dead), so the run's report gives each side's whole loss curve: the loss-exchange ratio at the end and the time to 50 % losses (k = 5).
Run them with scripts/c8-ladder.py (30 seeds a rung, HROT_RUN_SEED). 📄 docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md C-8.
"""
import json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenario_builder import Scn, ROOT  # noqa: E402

# ⭐ R-8009 — the long rung is 275 m, not 300 m: 300 m is the rifle's EXACT reach, so every pair but the one directly opposite (abreast,
# 5 m apart) stood just beyond it and the physical men had to step into range first (measured live: first hits at 11 s after a 14 m step);
# a stochastic unit, measured centre to centre, has no such edge. 275 m keeps the farthest pair (40 m apart sideways, ~278 m) inside.
RANGES = (100, 200, 275)
RUN_FOR = 120
# ⭐ the STANDARD rifleman (Infantry_Rifleman, NedTkbCatalog): sees 500 m, his M4 reaches 300 m, 25 HP (armour 5 × 5). ⛔ Not the urban
# soldier (2002): he sees only 150 m, so 200 / 300 m were rungs without a fight (CE-8020).
SOLDIER = 200
SOLDIER_HEALTH = 25
# ⭐ DOWN = INCAPACITATED (out of the fight), the physical injury model's own line: InjuryDefDto.IncapacitatedBelow (0.3 of Max), the soldier's
# Gen.Injury. ⛔ NOT dead: an incapacitated man bleeds out over BleedOutSeconds (120 s), so "health below 2 %" measured when the wounded DIED —
# losses 50–90 s after a fight that was over by ~20 s (measured live, 2026-11-16).
INCAPACITATED_BELOW = 0.3
DOWN_BELOW = INCAPACITATED_BELOW * SOLDIER_HEALTH
X0, Y0 = 300.0, 300.0


def side(s, name, force, y, facing):
    sq = s.squad(f"{name} Squad", force, (X0, y, 0), 0)
    for i in range(9):
        x = X0 - 20 + 5 * i
        s.man(f"{name} {i + 1}", force, (x, y, 0), facing, tkb=SOLDIER, health=SOLDIER_HEALTH, commander=sq,
              designation="SquadLeader" if i == 4 else "Wingman")


def checks():
    rows = []
    for who in ("Blue", "Red"):
        for k in range(1, 10):
            rows.append({"name": f"{who} down {k}", "on": "field", "who": f"*{who} ", "path": "Health.Current",
                         "op": "<", "value": DOWN_BELOW, "quantifier": f"atLeast:{k}"})
    return {"$doc": "CE-8009 (C-8) — a MEASUREMENT: each row's first time is when that many men of a side are down; scripts/c8-ladder.py "
                    "reads them over 30 seeds. Not a verdict: a row that never holds is a side that lost fewer.",
            "runFor": RUN_FOR, "golden": False, "expect": rows}


def main():
    for r in RANGES:
        sid = f"c8-open-{r}"
        s = Scn(sid, f"c8{r:03d}000")
        s.root = ROOT
        side(s, "Blue", "Friend", Y0, 0)          # facing north
        side(s, "Red", "Hostile", Y0 + r, 180)    # facing south
        folder = os.path.join(ROOT, "scenarios", "tests", "c8-ladder", sid)
        os.makedirs(folder, exist_ok=True)
        doc = {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
               "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "basic-desert",
                          "description": (f"What: CE-8009 (C-8 ladder, rung 1) - on basic-desert, 9 Blue and 9 Red standard riflemen (Infantry_Rifleman) in two "
                                          f"squads, abreast 5 m apart, face each other {r} m apart across open ground and fight it out (their SOPs, "
                                          f"no orders). Judge: a MEASUREMENT - the checks record the first time 1..9 men of each side are down "
                                          f"(incapacitated: Health below 30 %, out of the fight); scripts/c8-ladder.py runs 30 seeds and compares the loss-exchange ratio and the time "
                                          f"to 50 % losses with the stochastic resolution's.")},
               "entities": s.ents}
        with open(os.path.join(folder, "scenario.json"), "w") as f:
            json.dump(doc, f, indent=1)
        with open(os.path.join(folder, "checks.json"), "w") as f:
            json.dump(checks(), f, indent=2)
        print("wrote", folder)


if __name__ == "__main__":
    main()
