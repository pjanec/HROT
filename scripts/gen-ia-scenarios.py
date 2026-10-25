#!/usr/bin/env python3
"""AQ104 K (CE-2371) — the area / in-building demos, written FIRST as a measured baseline. Writes scenarios/ia-*/scenario.json.

Each scenario authors TODAY's closest orders; the build steps of AQ104 §5 swap them for the approved ones (marked ⏭ below):
  ia-house-hunt   — lone attacker (Attack order) v lone defender of House A (a one-man SquadDefend — ⏭ A: DefendArea)
  ia-area-clear   — a fire team clears an area holding House A and House B (✅ B: ClearArea, CE-2374)
  ia-area-defend  — a lone man defends the area around House A (✅ L: a one-man SquadDefend over the area — coverage posts and the
                    guard round, CE-2373; ⏭ A: the mapper's own one-man command for a lone man's DefendArea)
"""
import json, sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else "/home/user/HROT"

from scenario_builder import Scn  # noqa: E402 — the shared kit
import scenario_builder
scenario_builder.ROOT = ROOT

CHECK = "Check: python3 scripts/utility-demo-check.py --launch {sid}. 📄 docs/blueprints/Architect_Question_104_Area_And_Building_Combat.md §4 K"
HOUSE_A = (105.6, 104.0, 0)   # House A (house-2f at (100,100), two storeys; windows measured at x 102.1-109.1, y 100.9-107.1)
HOUSE_B = (156.0, 105.5, 0)   # House B (house-2f at (160,100), rotated 90)


def attack(target_nid):
    return {"Name": "CombatPosture", "Params": json.dumps({"target": target_nid, "assault": True}), "Origin": "Superior"}


def defend_squad(s, name, force, at, nid, radius=8):
    q = s.squad(name, force, at, nid, sop={"Name": "BasicSquadSop", "Params": "{}", "Origin": "Superior"})
    s.ents[q]["Behavior"] = {"Name": "SquadDefend", "Params": json.dumps({"Position": list(at), "SearchRadius": radius, "Speed": 2}),
                             "Origin": "Superior"}
    return q


# ── ia-house-hunt: one attacker, one defender, House A (two storeys) ────────────────────────────────────────
s = Scn("ia-house-hunt", "a1040001")
s.car("Car", (104, 82, 0), nid=10410)
dq = defend_squad(s, "Defender Squad", "Hostile", HOUSE_A, 10420)
s.man("Defender", "Hostile", (107.1, 100.9, 3), 180, tkb=2002, health=100, commander=dq, designation="SquadLeader", nid=10421)
s.man("Attacker", "Friend", (106, 79.5, 0), 0, tkb=2002, health=100, behavior=attack(10421), nid=10401)
s.write("ia-house-hunt", "bt-range",
        "What: AQ104 K (CE-2371) - on bt-range, 'Defender' (Hostile rifleman, the one man of 'Defender Squad' - SquadDefend on House A, "
        "the house-defence play; ⏭ AQ104 A: a lone man's DefendArea) starts upstairs at the south window (107.1,100.9, z 3); 'Attacker' "
        "(Friend rifleman) behind 'Car' at (106,79.5) has the Attack order on him (CombatPosture target + assault, C-5). Both mortal (100). "
        "Judge (240 s): (1) the attacker gets inside House A; (2) once he is inside, never 30 s with neither man firing while both are "
        "alive; (3) the attacker reaches the defender's storey within 60 s of entering; (4) the fight is decided (one man down) or still "
        "being fought at the end; printed: who fired from where, the storeys over time. " + CHECK.format(sid="ia-house-hunt"))

# ── ia-area-clear: a fire team clears an area holding two houses ─────────────────────────────────────────────
s = Scn("ia-area-clear", "a1040002")
AREA = ((HOUSE_A[0] + HOUSE_B[0]) / 2, (HOUSE_A[1] + HOUSE_B[1]) / 2, 0)   # midway: both houses within 32 m
bq = s.squad("Blue Team", "Friend", (130, 62, 0), 10440)
s.ents[bq]["Behavior"] = {"Name": "ClearArea", "Params": json.dumps({"Centre": list(AREA), "Radius": 32, "Speed": 2}), "Origin": "Superior"}
for i, x in enumerate((126, 129, 132, 135)):
    s.man(f"Blue {i + 1}", "Friend", (x, 60, 0), 0, tkb=2002, health=100, commander=bq,
          designation="SquadLeader" if i == 0 else "Wingman", nid=10441 + i)
r1 = defend_squad(s, "Red A", "Hostile", HOUSE_A, 10450)
s.man("Red 1", "Hostile", (107.1, 100.9, 3), 180, tkb=2002, health=100, commander=r1, designation="SquadLeader", nid=10451)
r2 = defend_squad(s, "Red B", "Hostile", HOUSE_B, 10460)
s.man("Red 2", "Hostile", HOUSE_B, 180, tkb=2002, health=100, commander=r2, designation="SquadLeader", nid=10461)
s.write("ia-area-clear", "bt-range",
        "What: AQ104 K (CE-2371) - on bt-range, 'Blue Team' (Friend, 'Blue 1-4') at (130,60) is to CLEAR the area holding House A "
        "(105.6,104) and House B (156,105.5) - AQ104 B: ClearArea (centre midway, 32 m; CE-2374), building after building, the nearest "
        "first. One defender in each house ('Red 1' upstairs in A, 'Red 2' in B - each a one-man SquadDefend). All mortal. Judge (420 s): "
        "(1) both houses are entered; (2) both defenders are down, or the team is still clearing; (3) no house is left once entered with a "
        "live known enemy in it. " + CHECK.format(sid="ia-area-clear"))

# ── ia-area-defend: a lone man defends the area round House A ────────────────────────────────────────────────
s = Scn("ia-area-defend", "a1040003")
s.car("Car 1", (96, 96, 0), heading=0, nid=10480)
s.car("Car 2", (115, 96, 0), heading=0, nid=10481)
gq = defend_squad(s, "Guard Post", "Hostile", (105.6, 97, 0), 10470, radius=25)   # ⏭ A: the mapper makes this for a lone man
guard = s.man("Guard", "Hostile", (104.5, 103.5, 0), 180,   # starts INSIDE House A (a defender is in place, not caught walking in the open) tkb=2002, health=100000, commander=gq, designation="SquadLeader", nid=10471)
s.man("Blue South", "Friend", (106, 5, 0), 0, tkb=2002, health=100, behavior=attack(10471), nid=10472)
s.man("Blue East", "Friend", (195, 98, 0), 270, tkb=2002, health=100, behavior=attack(10471), nid=10473)
s.write("ia-area-defend", "bt-range",
        "What: AQ104 K (CE-2371) - on bt-range, 'Guard' (Hostile rifleman, immortal) defends the area round House A (centre (105.6,97), "
        "~25 m) - AQ104 L (CE-2373): the one man of 'Guard Post' (SquadDefend over the area): posts chosen for COVERAGE, a guard round "
        "between them while nothing is known (⏭ AQ104 A: the order straight on the man) - two parked cars by the centre;'Blue South' (from (106,5)) and 'Blue East' (from (195,98)), ~90 m out so the guard posts before they arrive, attack him (mortal). "
        "Judge (240 s): (1) the guard sees each attacker before he is within 30 m of the centre; (2) the guard fires first at each; "
        "(3) printed: where the guard stood over time and what he could see. " + CHECK.format(sid="ia-area-defend"))
