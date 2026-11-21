#!/usr/bin/env python3
"""CE-4405 rail — no commander of a generated scale-rig scenario has more than UnitRoster.Capacity (16) subordinates, and every
unit has a commander that exists (the engine drops the 17th subordinate, so the ORBAT showed 16 squads per force and the rest at the root).
Run: python3 scripts/test_scale_rig_roster.py   (no terrain needed: the terrain reader is replaced by a fake road set)."""
import collections
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("scale_rig", os.path.join(HERE, "scale-rig.py"))
sr = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sr)
road = [((i * 37.0) % 3000, (i * 91.0) % 3000) for i in range(500)]
sr.terrain_info = lambda t: ((0, 0, 3000, 3000), None, road)

ROSTER = 16
failed = 0
for men, vehicles, aircraft, sides in ((1000, 0, 0, 2), (1000, 40, 40, 2), (8000, 40, 20, 2), (1000, 0, 0, 1), (80, 0, 0, 2)):
    ents = sr.scenario("t", men, vehicles, aircraft, 300.0, 1, sides)["entities"]
    members = collections.Counter()
    forces = [g for g, e in ents.items() if e.get("TkbIdentity", {}).get("TkbType") == 305 and "UnitSubordinate" not in e]
    for g, e in ents.items():
        u = e.get("UnitSubordinate")
        if u:
            if u["CommanderGuid"] not in ents:
                print(f"FAIL {men}/{sides}: {e['EntityInfo']['Name']} has a commander that does not exist"); failed += 1
            members[u["CommanderGuid"]] += 1
    over = {ents[g]["EntityInfo"]["Name"]: n for g, n in members.items() if n > ROSTER}
    roots = [ents[g]["EntityInfo"]["Name"] for g in forces]
    squads = sum(1 for e in ents.values() if e.get("TkbIdentity", {}).get("TkbType") == 302)
    orphans = [e["EntityInfo"]["Name"] for e in ents.values() if e.get("TkbIdentity", {}).get("TkbType") in (302, 2001, 405) and "UnitSubordinate" not in e]
    ok = not over and not orphans
    print(("ok  " if ok else "FAIL"), f"men={men} vehicles={vehicles} aircraft={aircraft} sides={sides}: {squads} squads, max roster {max(members.values())}, over={over}, orphans={len(orphans)}")
    failed += 0 if ok else 1
sys.exit(1 if failed else 0)
