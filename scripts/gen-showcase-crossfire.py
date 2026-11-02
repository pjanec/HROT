#!/usr/bin/env python3
"""Writes the SHOWCASE "Crossfire" scenario (docs/SHOWCASE_Scenarios.md §2) and the macros it uses.

  python3 scripts/gen-showcase-crossfire.py

Domination on demo-city: three capture points, ticket pools, a comeback boost, supply drops, bot respawn waves — ALL data (objectives,
triggers, macros, variants) on the rules S1/S2/S3 machinery plus the S6 v1 pieces (DESIGN_Scenario_Rules.md §00.11: timed capture by side,
the Counter measure, AddToCounter, trigger whileTrue). Nothing compiled per scenario (R-4112). Edit this script, not the files.

  scenarios/showcase-crossfire/scenario.json
  macros/game/bot/scenario.json            one soldier who advances 250 m ahead of where he spawns (a respawn-wave bot)
  macros/game/supply-crate/scenario.json   a supply crate (an obstacle) — dropped by the supply-drop triggers
"""
import json, math, os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")


def rot(deg):
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


def man(name, force, at, facing, commander=None, designation="Wingman", bind_side=False):
    e = {"SimTransform": {"Position": list(at), "Rotation": rot(facing)}, "Health": {"Current": 100, "Max": 100},
         "TkbIdentity": {"TkbType": 2002}, "EntityInfo": {"Name": name, "ForceId": force}, "TargetMemory": {"Entries": []}}
    if commander:
        e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": designation}
    if bind_side:
        e["ParamBindings"] = {"Bindings": {"EntityInfo.ForceId": "side"}}
    return e


def unit(tkb, name, force, at, facing=0, commander=None, behavior=None, key=None):
    e = {"SimTransform": {"Position": list(at), "Rotation": rot(facing)}, "TkbIdentity": {"TkbType": tkb},
         "EntityInfo": {"Name": name, "ForceId": force}}
    if commander:
        e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": "Wingman"}
    if behavior:
        e["Behavior"] = {"Name": behavior[0], "Params": json.dumps(behavior[1]), "Origin": "Superior"}
    if key:
        e["RefKey"] = {"Value": key}
    return e


# ════════════════════════════════════════ the map's places (demo-city, the streets x / y ≈ 400, 525, 650) ════════════════════════════════════════

POINTS = {"A": [400, 400, 0], "B": [525, 400, 0], "C": [650, 400, 0]}
POINT_R = 15
BLUE_BASE, RED_BASE = [525, 150, 0], [525, 650, 0]          # 250 m south / north of B — the bot macro advances 250 m ahead
CRATE_SPOTS = [[400, 275, 0], [650, 275, 0], [400, 525, 0], [650, 525, 0]]
DROPS = [180, 360, 540, 720]                                  # 4 drops, 3 min apart (the first at 3:00 — the 2:30-3:30 range needs SR-101 ③)
ROUND = 900


def macro_bot():
    ids, ents = Ids("cf0b0000"), {}
    g = ids()
    ents[g] = man("Bot", "Friend", (0, 0, 0), 0, bind_side=True)
    ents[g]["ParamBindings"]["Bindings"]["EntityInfo.Name"] = "name"
    ents[g]["Behavior"] = {"Name": "AdvanceTo", "Params": json.dumps({"Objective": [0, 250, 0], "Speed": 3, "ArrivalRadius": 6}), "Origin": "Superior"}
    ents[g]["Sop"] = {"Name": "BasicInfantrySop", "Params": "{}", "Origin": "Superior"}
    write("macros/game/bot/scenario.json", doc({
        "Kind": "macro",
        "description": "What: SHOWCASE Crossfire (docs/SHOWCASE_Scenarios.md §2) - a respawn-wave BOT: one soldier of side 'side' who advances 250 m ahead of his anchor (along its heading; the expander turns the point with the instance) and fights what he meets (BasicInfantrySop). Judge: inserted at a team's base facing the centre, he runs AdvanceTo towards the middle capture point.",
        "Anchor": {"Position": [0, 0, 0], "Heading": 0},
        "Params": [{"Name": "side", "TypeId": "ForceId", "Default": "Friend"}, {"Name": "name", "TypeId": "string", "Default": "Bot"}],
    }, ents))


def macro_crate():
    ids, ents = Ids("cf0c0000"), {}
    ents[ids()] = {"SimTransform": {"Position": [0, 0, 0], "Rotation": rot(0)}, "TkbIdentity": {"TkbType": 8809},
                   "EntityInfo": {"Name": "Supply crate"}, "ObstacleShape": {"Length": 1.2, "Width": 1.2, "Height": 1.0}}
    write("macros/game/supply-crate/scenario.json", doc({
        "Kind": "macro",
        "description": "What: SHOWCASE Crossfire (docs/SHOWCASE_Scenarios.md §2) - a supply crate (an obstacle, 1.2 m) dropped by a supply-drop trigger. Judge: inserted at a spot, one 'Supply crate' appears there.",
        "Anchor": {"Position": [0, 0, 0], "Heading": 0},
    }, ents))


# ════════════════════════════════════════ conditions / actions ════════════════════════════════════════

def on_status(ref, field, lo, hi):
    return {"$type": "OnEntity", "ref": ref, "predicate": {"$type": "PropertyMatch", "componentType": "ObjectiveStatus", "propertyPath": field,
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


def alive_of_side_at_most(force_number, n):
    """At most n men of the side (ForceId number) alive — a respawn wave's condition."""
    side = {"$type": "PropertyMatch", "componentType": "EntityInfo", "propertyPath": "ForceId", "operator": "Equals",
            "predicate": {"$type": "Numeric", "minValue": force_number, "maxValue": force_number}}
    alive = {"$type": "PropertyMatch", "componentType": "Health", "propertyPath": "Current", "operator": "Equals",
             "predicate": {"$type": "Numeric", "minValue": 0.001, "maxValue": 1e9}}
    return {"$type": "CountMatching", "predicate": all_of(side, alive), "min": 0, "max": n, "withComponent": "Health"}


def msg(text, seconds=6, to="All"):
    return {"act": "Message", "args": {"toKind": to, "text": text, "seconds": seconds}}


def add(target, amount):
    return {"act": "AddToCounter", "args": {"target": target, "amount": amount}}


FRIEND, HOSTILE = 1, 2


def scenario():
    ids, ents = Ids("cf6e0000"), {}

    def team(name, force, base, facing, sq_dx):
        pl = ids()
        ents[pl] = unit(305, f"{name} Team", force, base, facing, key=f"{name}Team",
                        behavior=("PlatoonAttack", {"Position": POINTS["B"], "SearchRadius": 30, "Preset": "AttackPosition", "Speed": 3}))
        for s, dx in enumerate((-sq_dx, sq_dx), 1):
            sq = ids()
            at = (base[0] + dx, base[1], 0)
            ents[sq] = unit(302, f"{name} Squad {s}", force, at, facing, commander=pl)
            for i in range(4):
                role = "SquadLeader" if i == 0 else ("MachineGunner" if i == 1 else "Wingman")
                ents[ids()] = man(f"{name} {s}-{i + 1}", force, (at[0] - 3 + 2 * i, at[1], 0), facing, sq, role)
        return pl

    blue = team("Blue", "Friend", BLUE_BASE, 0, 10)
    red = team("Red", "Hostile", RED_BASE, 180, 10)

    def objective(kind, title, params, key=None, force="Friend"):
        g = ids()
        ents[g] = {"TkbIdentity": {"TkbType": 8860}, "EntityInfo": {"Name": title, "ForceId": force},
                   "Behavior": {"Name": kind, "Params": json.dumps(params), "Origin": "Superior"}}
        if key:
            ents[g]["RefKey"] = {"Value": key}
        return g

    # ── the three capture points (G-1: by side, more men for 10 s) and the three counters (G-2)
    pts = {}
    for p, at in POINTS.items():
        pts[p] = objective("HoldArea", f"Point {p}", {"common": {"title": f"Point {p}", "subject": blue, "scored": False},
                           "area": {"circle": {"center": at, "radius": POINT_R}}, "minMen": 1, "capture": True, "captureSeconds": 10},
                           key=f"Point{p}")
    blue_t = objective("Counter", "Blue tickets", {"common": {"title": "Blue tickets", "subject": blue, "scored": False}, "start": 150,
                       "deaths": [{"side": "Friend", "per": -1}]}, key="BlueTickets")
    red_t = objective("Counter", "Red tickets", {"common": {"title": "Red tickets", "subject": red, "scored": False}, "start": 150,
                      "deaths": [{"side": "Hostile", "per": -1}]}, key="RedTickets", force="Hostile")
    lead = objective("Counter", "Blue lead", {"common": {"title": "Blue lead", "subject": blue, "scored": False}, "start": 0, "floor": -1000,
                     "deaths": [{"side": "Hostile", "per": 1}, {"side": "Friend", "per": -1}]}, key="Lead")

    def trigger(title, when, do, force="Friend", **kw):
        g = ids()
        p = {"common": {"title": title, "subject": blue}, "when": when, "do": do}
        p.update(kw)
        ents[g] = {"TkbIdentity": {"TkbType": 8861}, "EntityInfo": {"Name": title, "ForceId": force},
                   "Behavior": {"Name": "Trigger", "Params": json.dumps(p), "Origin": "Superior"}}
        return g

    trigger("Kick-off", time_from(3), [msg("Take the points!", 5)])

    # ── the drain: each held point takes 1 enemy ticket every 10 s (G-4 whileTrue); sudden death arms a second set (double)
    double = []
    for p in POINTS:
        for owner, victim, sign, label in ((FRIEND, red_t, +1, "Red"), (HOSTILE, blue_t, -1, "Blue")):
            trigger(f"{p} drains {label}", on_status(pts[p], "Owner", owner, owner), [add(victim, -1), add(lead, sign)],
                    repeat=True, whileTrue=True, cooldown=10)
            double.append(trigger(f"{p} drains {label} (sudden death)", on_status(pts[p], "Owner", owner, owner), [add(victim, -1), add(lead, sign)],
                                  repeat=True, whileTrue=True, cooldown=10, **{"common": {"title": f"{p} drains {label} (sudden death)", "subject": blue,
                                                                                         "startActive": False}}))
    trigger("Sudden death", time_from(720), [msg("Sudden death - points count double", 6)] +
            [{"act": "SetObjectiveActive", "args": {"target": t, "active": True}} for t in double])

    # ── supply drops: 4 at most, 3 min apart, each at one of 4 spots (a variant per drop — CrateSpot1..4 markers with Positions)
    for i, t in enumerate(DROPS, 1):
        g = ids()
        ents[g] = {"SimTransform": {"Position": CRATE_SPOTS[(i - 1) % 4], "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": 8803},
                   "EntityInfo": {"Name": f"Crate spot {i}"}, "RefKey": {"Value": f"CrateSpot{i}"},
                   "EditablePolyline": {"Points": [{"X": -4, "Y": -4}, {"X": -4, "Y": 4}, {"X": 4, "Y": 4}, {"X": 4, "Y": -4}], "Version": 0},
                   "MapOverlayStyle": {"FillColor": [255, 200, 0, 60], "BorderColor": [255, 200, 0, 255], "LineThickness": 2, "IsClosed": True},
                   "Positions": [{"at": s} for s in CRATE_SPOTS[(i - 1) % 4:] + CRATE_SPOTS[:(i - 1) % 4]]}
        trigger(f"Crate {i} announced", time_from(t - 30), [msg("Supply drop in 30 s", 6)])
        trigger(f"Crate drop {i}", time_from(t), [{"act": "InsertMacro", "args": {"macro": "game/supply-crate", "at": f"@CrateSpot{i}", "heading": 0}},
                                                  msg("Supply crate down", 5)])

    # ── the comeback boost (G-7): the team 40 behind gets its bots one tier sharper, never twice within 2 min; back to 0 when it recovers
    trigger("Comeback Red", on_status(lead, "Value", 40, 1e6), [{"act": "SetSkillAmplifier", "args": {"side": "Hostile", "tiers": 1}},
            msg("Reinforcements: Red's squads are sharper", 6)], force="Hostile", repeat=True, cooldown=120, holdFor=30)
    trigger("Comeback Red over", on_status(lead, "Value", -1e6, 19.999), [{"act": "SetSkillAmplifier", "args": {"side": "Hostile", "tiers": 0}}],
            force="Hostile", repeat=True, cooldown=120)
    trigger("Comeback Blue", on_status(lead, "Value", -1e6, -40), [{"act": "SetSkillAmplifier", "args": {"side": "Friend", "tiers": 1}},
            msg("Reinforcements: Blue's squads are sharper", 6)], repeat=True, cooldown=120, holdFor=30)
    trigger("Comeback Blue over", on_status(lead, "Value", -19.999, 1e6), [{"act": "SetSkillAmplifier", "args": {"side": "Friend", "tiers": 0}}],
            repeat=True, cooldown=120)

    # ── respawn waves for bots (G-5): while a team has fewer than 10 men up, one bot every 15 s at its base, facing the centre
    trigger("Blue respawn", alive_of_side_at_most(FRIEND, 9),
            [{"act": "InsertMacro", "args": {"macro": "game/bot", "at": BLUE_BASE, "heading": 0, "params": {"side": "Friend", "name": "Blue bot"}}}],
            repeat=True, whileTrue=True, cooldown=15)
    trigger("Red respawn", alive_of_side_at_most(HOSTILE, 9),
            [{"act": "InsertMacro", "args": {"macro": "game/bot", "at": RED_BASE, "heading": 180, "params": {"side": "Hostile", "name": "Red bot"}}}],
            force="Hostile", repeat=True, whileTrue=True, cooldown=15)

    # ── the end: a team out of tickets, a domination (all three points for 60 s), or 15 min
    trigger("Domination Blue", all_of(*[on_status(pts[p], "Owner", FRIEND, FRIEND) for p in POINTS]),
            [msg("Blue dominates!", 8), {"act": "EndExercise", "args": {"reason": "Blue held all three points for 60 s"}}], holdFor=60)
    trigger("Domination Red", all_of(*[on_status(pts[p], "Owner", HOSTILE, HOSTILE) for p in POINTS]),
            [msg("Red dominates!", 8), {"act": "EndExercise", "args": {"reason": "Red held all three points for 60 s"}}], holdFor=60, force="Hostile")
    trigger("End", any_of(on_status(blue_t, "Value", -1e6, 0), on_status(red_t, "Value", -1e6, 0), time_from(ROUND)),
            [msg("Round over", 8), {"act": "EndExercise", "args": {"reason": "tickets or time"}}])

    write("scenarios/showcase-crossfire/scenario.json", doc({
        "terrainName": "demo-city",
        "description": (
            "What: SHOWCASE 'Crossfire' (docs/SHOWCASE_Scenarios.md §2) - team domination, bots on both sides (players join on the Deploy screen). "
            "Blue (2 squads x 4) from the south base (525,150), Red from the north (525,650), both ordered PlatoonAttack on point B. Three capture "
            "points A (400,400), B (525,400), C (650,400): a side captures with more men inside for 10 s (HoldArea capture, captureSeconds). Tickets: "
            "150 each (Counter), -1 per own man down, -1 per 10 s for each point the enemy holds (whileTrue drain triggers; doubled from 12:00, "
            "sudden death). 'Blue lead' = Blue minus Red; 40 behind => that side's bots +1 tier (never twice within 2 min). Supply crates at 3, 6, 9, "
            "12 min, each at one of 4 spots (variant). A team with fewer than 10 men up gets a bot every 15 s at its base (game/bot). End: tickets 0, "
            "domination (all three points 60 s) or 15 min. Judge: (1) a point's Owner flips only after 10 s of a majority; (2) while a point is held "
            "the other side's tickets fall ~1 per 10 s; (3) four crate drops, 3 min apart, each at one of the 4 spots; (4) the comeback raises only the "
            "losing side, at most once per 2 min; (5) bots respawn at their base. Generated by scripts/gen-showcase-crossfire.py."),
    }, ents))


if __name__ == "__main__":
    macro_bot()
    macro_crate()
    scenario()
    print("wrote scenarios/showcase-crossfire + macros/game/bot, game/supply-crate")
