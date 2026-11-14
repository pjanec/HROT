#!/usr/bin/env python3
"""The morale demos (CE-7015 / CE-4584, AQ101 §R C + E, R-4227) — a mortar barrage on a defending platoon. Writes
scenarios/demos/platoons-and-vehicles/morale-barrage-{green,veteran}/scenario.json.

pc-defend-dry's Red platoon (three squads of four, OBJ at (121,157) on cqb-village, facing south) + a Blue 81 mm mortar section
(uc-request-chain's battery: two tubes and their crews, ManWeapon) ~200 m south-east and a Blue spotter (SpotForFire) east of
the position, outside the line's arc. The check (utility-demo-check.py --launch morale-barrage-<x>) engages the platoon the way
pc-defend-dry's does (the squads' Set + a SPOT inside the engagement area), and the spotter's missions do the rest.
Green = Militia, nerve 0.15 · Veteran = Veteran, nerve 0.9. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md DA-67 (CE-7015).
"""
import copy, json, os

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = os.path.join(REPO, "scenarios", "demos", "platoons-and-vehicles", "pc-defend-dry", "scenario.json")
MORTAR = os.path.join(REPO, "scenarios", "demos", "weapons-and-fires", "uc-request-chain-dry", "scenario.json")
VARIANTS = {"green": ("Militia", 0.15), "veteran": ("Veteran", 0.9)}
CHECK = "Check: python3 scripts/utility-demo-check.py --launch morale-barrage-{v}. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md DA-67 (CE-7015)"


def guid(n):
    return f"c7015000-0000-4000-8000-{n:012d}"


def main():
    base = json.load(open(BASE))
    mortar = json.load(open(MORTAR))
    m_ents = {k: e for k, e in mortar["entities"].items()
              if e.get("EntityInfo", {}).get("Name", "").startswith(("Insurgent Mortar", "Mortar Tube", "Mortar Man", "Spotter"))}
    for v, (prof, nerve) in VARIANTS.items():
        doc = copy.deepcopy(base)
        for e in doc["entities"].values():
            if e.get("TkbIdentity", {}).get("TkbType") == 2002:      # the platoon's men
                e["Proficiency"] = prof
                e["Temperament"] = {"nerve": nerve}
        # the mortar section, re-keyed, Blue, moved to cqb-village: the battery at (280, 30), the spotter east of OBJ at (290, 160)
        remap = {k: guid(9800 + i) for i, k in enumerate(m_ents)}
        for k, src in m_ents.items():
            text = json.dumps(src)
            for old, new in remap.items():
                text = text.replace(old, new)
            e = json.loads(text)
            e["EntityInfo"]["ForceId"] = "Friend"
            name = e["EntityInfo"]["Name"]
            x, y, z = e["SimTransform"]["Position"]
            if name == "Spotter":
                e["SimTransform"]["Position"] = [290, 160, 0]
                e["SimTransform"]["Rotation"] = [0, 0, 1, 0]   # facing WEST, at the line (+X is forward; the source faced him south)
                e["Behavior"]["Params"]["Rounds"] = 4
            else:
                e["SimTransform"]["Position"] = [x - 740 + 280, y - 745 + 30, z]   # the cell keeps its shape, centred at (280, 30)
            doc["entities"][remap[k]] = e
        doc["header"]["description"] = (
            f"What: CE-7015 / CE-4584 (AQ101 §R, R-4227) - on cqb-village, pc-defend-dry's 'Red Platoon' (three squads of four, {prof}, nerve {nerve}) "
            "defends OBJ at (121,157) facing south; a Blue 81 mm mortar section at (280,30) fires on it, called by a Blue 'Spotter' at (290,160), "
            "east of the line's arc. The check engages the platoon (the squads' Set and a SPOT inside the engagement area, as pc-defend-dry). "
            "Judge: the bursts near-miss the line's men (UnderFire, stress rises); "
            + ("a GREEN platoon's men break and it FALLS BACK (BreakContact) while still mostly alive. " if v == "green"
               else "a VETERAN platoon's line HOLDS (no BreakContact) under the same fire. ")
            + "Report the sim times. " + CHECK.format(v=v))
        out = os.path.join(REPO, "scenarios", "demos", "platoons-and-vehicles", f"morale-barrage-{v}")
        os.makedirs(out, exist_ok=True)
        with open(os.path.join(out, "scenario.json"), "w") as fh:
            json.dump(doc, fh, indent=1)
        print("wrote", out)


if __name__ == "__main__":
    main()
