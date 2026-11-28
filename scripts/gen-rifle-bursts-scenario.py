#!/usr/bin/env python3
"""CE-11033 (docs/DESIGN_Weapon_Layers.md 5l) - the feature scenario `rifle-bursts`: rifles fire short bursts near, single aimed rounds far.
Writes scenarios/<category>/rifle-bursts/scenario.json (the checks.json and golden.json are authored beside it)."""
import sys
from scenario_builder import Scn
import scenario_builder
scenario_builder.ROOT = sys.argv[1] if len(sys.argv) > 1 else "/home/user/HROT"

s = Scn("rifle-bursts", "a1103300")
# Near pair: 40 m apart.  Far pair: 200 m apart, 400 m south of the near pair (out of each other's sight).
def man(name, force, at, facing, foe_at, seed=None):
    g = s.man(name, force, at, facing, tkb=2002)
    # stays put, watches the foe, asks a deliberate 1 s interval (the single aimed round's pace); InertSop = no other tactics
    s.ents[g]["Behavior"] = {"Name": "CombatPosture", "Params": {"Advance": {"Objective": list(at), "ArrivalRadius": 1, "CooldownSeconds": 1},
                                                                "Stay": True, "Post": {"Point": list(at), "Radius": 2.0}, "watchTowards": list(foe_at)},
                             "Origin": "Superior"}
    s.ents[g]["Sop"] = {"Name": "InertSop", "Params": {}, "Origin": "Superior"}
    return g

# near 40 m (seen: 2-3 rounds) · mid 120 m (seen, standing: 1-2) · supp 200 m beyond the 150 m sight (each knows where the other is: suppressing fire 3-5)
pairs = [("Near Blue", (100, 100, 0), "Near Red", (140, 100, 0), False),
         ("Mid Blue", (100, 300, 0), "Mid Red", (220, 300, 0), False),
         ("Supp Blue", (100, 500, 0), "Supp Red", (300, 500, 0), True)]
for bn, bp, rn, rp, seed in pairs:
    b = man(bn, "Friend", bp, 90, rp)
    r = man(rn, "Hostile", rp, 270, bp)
    if seed:   # as ua-grenade-or-rifle: the foe is in memory from t = 0, not seen
        for me, foe in ((b, r), (r, b)):
            f = s.ents[foe]["SimTransform"]["Position"]
            s.ents[me]["TargetMemory"] = {"Entries": [{"Entity": foe, "PosX": f[0], "PosY": f[1], "PosZ": 0, "Score": 500, "Tick": 0, "Modality": 1}]}
s.write("rifle-bursts", "basic-desert",
        "What: CE-11033 (R-11012, DESIGN_Weapon_Layers 5l) - on open basic-desert three rifle pairs, all immortal, no orders (they stand and fight): 'Near' 40 m apart, "
        "'Mid' 120 m apart (both inside the 150 m sight), 'Supp' 200 m apart (beyond sight: each only knows where the other is). Judge (60 s): (1) the Near "
        "men fire SHORT BURSTS of 2-3 rounds, each round counted 1..N under one burst id; (2) the Mid men fire 1-2 rounds, never longer; (3) the Supp "
        "men fire SUPPRESSING bursts of 3-5 rounds, every round planned; (4) all three pairs fire. Check: scripts/scenario-regression.sh rifle-bursts.")
