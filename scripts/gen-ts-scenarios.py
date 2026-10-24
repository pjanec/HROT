#!/usr/bin/env python3
"""Temperament & Stress §6 — the three demos (CE-2272, R-2034, R-2075). Writes scenarios/ts-*/scenario.json.
📄 docs/DESIGN_Temperament_And_Stress.md §6."""
import json, sys

import scenario_builder
from scenario_builder import Scn

scenario_builder.ROOT = sys.argv[1] if len(sys.argv) > 1 else "/home/user/HROT"

CHECK = "Check: python3 scripts/utility-demo-check.py --launch {sid}. 📄 docs/DESIGN_Temperament_And_Stress.md §6"
STAY = {"Fire": "FireAtWill", "Reactions": "StayOnTask", "SetBy": "Operator", "ForceLevel": "Lethal"}


def posted(x, y, watch):
    """A man told to hold his spot and fight from it (the shape il-idle-life's Watchman and pc-urban-ambush's posted men use)."""
    return {"Name": "CombatPosture", "Origin": "Superior", "Params": json.dumps({
        "Advance": {"Objective": [x, y, 0], "ArrivalRadius": 1}, "Stay": True, "Post": {"Point": [x, y, 0], "Radius": 2.0},
        "watchTowards": [watch[0], watch[1], 0]})}   # ⚠ three coordinates — 📐 measured: two ⇒ the order is refused (ParseFailed)


# ── ts-fire-discipline: two fire teams under the same fire — regulars and militia ─────────────────────────────────────
#   ⭐ The fire: one automatic rifle per team, 120 m south, firing at a point 10 m BEHIND its team at 4 m height — every round passes
#   over the team's heads (no hits; a near miss is judged flat, within 3 m of the round's line: CombatConstants.NearMissRadius), one
#   round every COOLDOWN s for ROUNDS rounds. The same fire on both teams; only their temperament differs.
COOLDOWN, ROUNDS = 1.3, 40
s = Scn("ts-fire-discipline", "a1040001")
for team, x0, temper in (("Regular", 270, None), ("Militia", 340, {"obedience": 0.3, "nerve": 0.3})):
    for i in range(4):
        x, y = x0 + 1.5 * i, 300   # 📐 1.5 m apart: every man within 3 m of the rounds' line (2 m put the end men on its edge)
        g = s.man(f"{team} {i + 1}", "Friend", (x, y, 0), 180, tkb=2002, nid=(10801 if team == "Regular" else 10811) + i,
                  behavior=posted(x, y, (x, 150)))
        s.ents[g]["Roe"] = dict(STAY)   # held on their spot: the SOP's take-cover reaction is refused — a break is not
        if temper: s.ents[g]["Temperament"] = dict(temper)
    s.man(f"Gunner {team}", "Hostile", (x0 + 2.25, 180, 0), 0, tkb=2009, nid=10821 if team == "Regular" else 10822,
          behavior={"Name": "FireAtPoint", "Origin": "Superior", "Params": json.dumps(
              {"x": x0 + 2.25, "y": 310, "z": 4, "mount": 0, "rounds": ROUNDS, "cooldownSeconds": COOLDOWN})},
          sop={"Name": "InertSop", "Params": "{}", "Origin": "Superior"})
s.write("ts-fire-discipline", "basic-desert",
        "What: Temperament & Stress §6 (CE-2272, R-2034) - on open basic-desert two fire teams of four hold their spots facing south "
        "(CombatPosture, stay, a post; ROE StayOnTask): 'Regular 1-4' (Friend riflemen, the default temperament - obedience 0.8, nerve 0.5) "
        f"at x 270-274.5 and 'Militia 1-4' (Friend riflemen, obedience 0.3, nerve 0.3) at x 340-344.5, y 300. Each is under the SAME fire: "
        f"'Gunner Regular' / 'Gunner Militia' (Hostile automatic riflemen, InertSop) 120 m south (inside a rifleman's 150 m sight) fire {ROUNDS} rounds, one every {COOLDOWN} s, "
        "at a point 10 m behind the team, 4 m up - every round passes over the team's heads (near misses, no hits). All immortal. "
        "Judge (100 s): (1) no regular breaks and the regulars fire back; (2) at least half the militia break (the Break reaction - "
        "down, no fire); (3) every man who broke recovers once the fire stops and is back on his task by the end; (4) printed per man: "
        "his peak stress, when he broke and recovered. " + CHECK.format(sid="ts-fire-discipline"))


# ── ts-aggression: two identical riflemen, the same fight, one bold and one cautious ──────────────────────────────────
#   ⭐ Two MIRRORED duels 300 m apart (no shared enemy, so neither draws the other's fire): each man ordered to attack an enemy rifleman
#   who holds a spot behind a car 100 m north; a car 8 m in front of each attacker is his nearest cover. Obedience 0.3, so character shows
#   (TS-2: effective bravery 0.78 / 0.22 ⇒ AggressionDamp / CautionDamp 0.72).
s = Scn("ts-aggression", "a1040002")
for who, x0, risk, nid in (("Bold", 200, 0.9, 10831), ("Cautious", 500, 0.1, 10832)):
    s.car(f"{who} Cover", (x0, 308, 0), heading=90, nid=nid + 20)
    s.car(f"{who} Enemy Cover", (x0, 397, 0), heading=90, nid=nid + 30)
    g = s.man(who, "Friend", (x0, 300, 0), 0, tkb=2002, nid=nid,
              behavior={"Name": "CombatPosture", "Origin": "Superior", "Params": json.dumps(
                  {"Advance": {"Objective": [x0, 395, 0], "ArrivalRadius": 3}})})
    s.ents[g]["Temperament"] = {"riskAcceptance": risk, "obedience": 0.3}
    s.man(f"{who} Enemy", "Hostile", (x0, 400, 0), 180, tkb=2002, nid=nid + 10, behavior=posted(x0, 400, (x0, 300)),
          sop={"Name": "InertSop", "Params": "{}", "Origin": "Superior"})
s.write("ts-aggression", "basic-desert",
        "What: Temperament & Stress §6 (CE-2271, R-2034/R-2075) - two mirrored duels on open basic-desert, 300 m apart: 'Bold' (Friend "
        "rifleman, bravery 0.9) at (200,300) and 'Cautious' (Friend rifleman, bravery 0.1), both obedience 0.3 so character shows, each "
        "ordered to attack (CombatPosture, advance to 5 m short of his enemy) 'Bold Enemy' / 'Cautious Enemy' (Hostile riflemen holding "
        "a spot behind a car 100 m north, InertSop); a car 8 m in front of each attacker is his nearest cover. All immortal. Judge (90 s): "
        "(1) the bold man's posture is AdvanceAndAttack for more of the time than the cautious man's; (2) the cautious man takes cover or "
        "suppresses for more of the time than the bold man; (3) the bold man ends nearer his enemy; (4) printed: each man's posture "
        "time shares and the damping modifiers in his utility trace. " + CHECK.format(sid="ts-aggression"))


# ── ts-house-defenders: three insurgents of three characters hold the CQB house ───────────────────────────────────────
#   ⭐ bc-house-defended's house and squad (BC-5, CE-2261: the six-man Veteran squad clears the CQB house on bt-range from its front
#   door), its two defenders recast and a third added in closet E (bc-house-clear's spot) — each the Insurgent type (2003: obedience
#   0.3 ± his own spread), held on his spot (CombatPosture, stay; ROE StayOnTask, so only a break moves him).
import copy, os  # noqa: E402
base = json.load(open(os.path.join(scenario_builder.ROOT, "scenarios", "bc-house-defended", "scenario.json")))
s = Scn("ts-house-defenders", "a1040003")
s.ents = copy.deepcopy(base["entities"])
cast = {"Insurgent 1": ("Fanatic", {"resolve": 1.0, "nerve": 0.9}),
        "Insurgent 2": ("Coward", {"nerve": 0.1})}
for g, e in s.ents.items():
    name = e.get("EntityInfo", {}).get("Name")
    if name in cast:
        e["EntityInfo"]["Name"], e["Temperament"] = cast[name][0], cast[name][1]
        e["TkbIdentity"]["TkbType"] = 2003
waverer = copy.deepcopy(next(e for e in s.ents.values() if e.get("EntityInfo", {}).get("Name") == "Coward"))
waverer["EntityInfo"]["Name"], waverer["Temperament"] = "Waverer", {"resolve": 0.2}
waverer["SimTransform"]["Position"] = [53.3, 100.7, 0]
waverer["NetworkIdentity"]["Value"] = 12399
waverer["Behavior"]["Params"] = json.dumps({"Advance": {"Objective": [53.3, 100.7, 0], "ArrivalRadius": 1}, "Stay": True,
                                            "Post": {"Point": [53.3, 100.7, 0], "Radius": 1.0}})
s.ents["a1040003-0000-4000-8000-000000000099"] = waverer
s.write("ts-house-defenders", base["header"]["terrainName"],
        "What: Temperament & Stress §6 (CE-2272, R-2034) - bc-house-defended's six-man Veteran squad clears the CQB test house on bt-range "
        "from its front door against three insurgents (the Insurgent type, obedience 0.3), each held on his spot (CombatPosture, stay; ROE "
        "StayOnTask - only a break moves him): the 'Fanatic' (resolve 1, nerve 0.9) at the east end of the hallway facing the door, the "
        "'Coward' (nerve 0.1) in room A, the 'Waverer' (resolve 0.2) in closet E. Judge (240 s): (1) the Coward breaks (to ground, or away "
        "to cover farther from the squad); (2) the Waverer, once broken with a rifle on him, surrenders (hands up, weapons down); (3) the "
        "Fanatic never breaks or surrenders and fires on the squad; (4) printed per insurgent: his peak stress, when he broke, what he did. "
        + CHECK.format(sid="ts-house-defenders"))


# ── ts-three-defenders: the same three characters on open ground, under a pressure that always comes ──────────────────────
#   ⭐ ts-house-defenders' pressure is the clearing, and the clearing stalls at the door (Building Clearing BC-5 "half there": Blue
#   fired 1 round in 480 s) — 📐 measured 2026-10-24: in 240 s the Fanatic peaked at stress 0.06 and the Waverer at 0.00. Here each
#   insurgent holds a spot with a car 12 m behind him (his retreat cover), an automatic rifle 120 m south fires over his head
#   (ts-fire-discipline's near misses), and the Coward and the Waverer each have a rifleman 55 m off who engages them (the rifle on him
#   that TS-6's surrender needs: Covering.By). The Fanatic has only the gunner, so he fights. Obedience authored 0.3 for all three —
#   no type spread, so each break and surrender threshold is the arithmetic in the description.
#   ⚠ the columns at x 270 / 340 / 400 — 📐 measured: at x 200 the Fanatic recorded NO near miss in 40 rounds (his gunner stands 1.2 m
#   higher; the ground between takes the rounds) while the same fire at x 400 broke the Waverer; ts-fire-discipline's 270 / 340 work.
s = Scn("ts-three-defenders", "a1040004")
for k, (who, x0, temper, covered) in enumerate((
        ("Fanatic", 270, {"resolve": 1.0, "nerve": 0.9, "obedience": 0.3}, False),
        ("Coward", 340, {"nerve": 0.1, "obedience": 0.3}, True),
        ("Waverer", 400, {"resolve": 0.0, "obedience": 0.3}, True))):
    s.car(f"{who} Cover", (x0, 312, 0), heading=90, nid=10871 + k)
    g = s.man(who, "Hostile", (x0, 300, 0), 180, tkb=2003, nid=10841 + k, behavior=posted(x0, 300, (x0, 180)))
    s.ents[g]["Roe"] = dict(STAY)
    s.ents[g]["Temperament"] = dict(temper)
    s.man(f"Gunner {who}", "Friend", (x0, 180, 0), 0, tkb=2009, nid=10851 + k,
          behavior={"Name": "FireAtPoint", "Origin": "Superior", "Params": json.dumps(
              {"x": x0, "y": 310, "z": 4, "mount": 0, "rounds": ROUNDS, "cooldownSeconds": COOLDOWN})},
          sop={"Name": "InertSop", "Params": "{}", "Origin": "Superior"})
    if covered:
        # ⚠ posted, watching him — 📐 measured: an InertSop rifleman never raises his rifle (AtEase, 150 rounds), so nobody covers
        r = s.man(f"Rifleman {who}", "Friend", (x0 + 20, 250, 0), 0, tkb=2002, nid=10861 + k,
                  behavior=posted(x0 + 20, 250, (x0, 300)))
        s.ents[r]["Roe"] = dict(STAY)
s.write("ts-three-defenders", "basic-desert",
        "What: Temperament & Stress §6 (CE-2272, R-2034) - three insurgents (the Insurgent type, obedience authored 0.3) hold their spots "
        "on open basic-desert, 60-70 m apart, facing south (CombatPosture, stay; ROE StayOnTask - only a break moves him), a car 12 m behind "
        "each: the 'Fanatic' (resolve 1, nerve 0.9 - effective nerve 0.78) at x 270, the 'Coward' (nerve 0.1 - effective 0.22) at x 340, "
        "the 'Waverer' (resolve 0 - effective 0.15, under the 0.3 that surrenders) at x 400. Over each, 'Gunner <name>' (Friend automatic "
        f"rifleman, InertSop) 120 m south fires {ROUNDS} rounds, one every {COOLDOWN} s, 10 m behind him and 4 m up (near misses); the "
        "Coward and the Waverer each also have 'Rifleman <name>' (Friend rifleman, posted 55 m off watching him), who engages him - the rifle on him. All "
        "immortal. Judge (120 s): (1) the Coward breaks and runs (Break - to ground at cover farther from the fire), he does not surrender "
        "(effective resolve 0.5); (2) the Waverer breaks with a rifle on him and surrenders (hands up, weapons down); (3) the Fanatic never "
        "breaks or surrenders and fires back; (4) printed per insurgent: his peak stress, when he broke, what he did, the rounds he fired. "
        + CHECK.format(sid="ts-three-defenders"))
