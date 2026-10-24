#!/usr/bin/env python3
"""AQ102 §5 — the five reflex scenarios (CE-2355). Writes scenarios/rf-*/scenario.json."""
import json, math, os, sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else "/home/user/HROT"


def rot(deg):  # compass heading (0 = north, clockwise) → quaternion about Z (forward = +X)
    yaw = math.radians(90 - deg)
    return [0, 0, round(math.sin(yaw / 2), 7), round(math.cos(yaw / 2), 7)]


class Scn:
    def __init__(self, sid, prefix):
        self.ents, self.n, self.prefix, self.nid = {}, 0, prefix, None

    def guid(self):
        self.n += 1
        return f"{self.prefix}-0000-4000-8000-{self.n:012d}"

    def man(self, name, force, at, facing, tkb=2004, health=100000, behavior=None, sop=None, commander=None, designation=None, nid=0):
        g = self.guid()
        e = {"SimTransform": {"Position": list(at), "Rotation": rot(facing)},
             "Health": {"Current": health, "Max": health},
             "NetworkIdentity": {"Value": nid},
             "TkbIdentity": {"TkbType": tkb},
             "EntityInfo": {"Name": name, "ForceId": force},
             "TargetMemory": {"Entries": []}}
        if behavior: e["Behavior"] = behavior
        if sop: e["Sop"] = sop
        if commander: e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": designation or "Wingman"}
        self.ents[g] = e
        return g

    def squad(self, name, force, at, nid, sop=None):
        g = self.guid()
        e = {"SimTransform": {"Position": list(at), "Rotation": [0, 0, 0, 1]},
             "NetworkIdentity": {"Value": nid}, "TkbIdentity": {"TkbType": 302},
             "EntityInfo": {"Name": name, "ForceId": force}}
        if sop: e["Sop"] = sop
        self.ents[g] = e
        return g

    def car(self, name, at, heading=90, nid=0):
        g = self.guid()
        self.ents[g] = {"SimTransform": {"Position": list(at), "Rotation": rot(heading)},
                        "NetworkIdentity": {"Value": nid}, "TkbIdentity": {"TkbType": 8806},
                        "EntityInfo": {"Name": name},
                        "ObstacleShape": {"Length": 4.5, "Width": 1.8, "Height": 1.5}}
        return g

    def write(self, sid, terrain, description):
        doc = {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
               "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": terrain,
                          "description": description},
               "entities": self.ents}
        d = os.path.join(ROOT, "scenarios", sid)
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "scenario.json"), "w") as f:
            json.dump(doc, f, indent=2)
            f.write("\n")
        print("wrote", sid, len(self.ents), "entities")


CHECK = "Check: python3 scripts/utility-demo-check.py --launch {sid}. 📄 docs/blueprints/Architect_Question_102_Responsive_Fighting_Reflexes.md §5"

# ── S1 rf-car-duel: 1 v 1 among six parked cars on open desert ─────────────────────────────────────────────
s = Scn("rf-car-duel", "a1020001")
for i, x in enumerate((262, 277, 292)): s.car(f"Blue Car {i + 1}", (x, 360, 0), nid=10210 + i)
for i, x in enumerate((268, 283, 298)): s.car(f"Red Car {i + 1}", (x, 400, 0), nid=10220 + i)
s.man("Blue", "Friend", (277, 357, 0), 0, nid=10201)
s.man("Red", "Hostile", (283, 403, 0), 180, nid=10202)
s.write("rf-car-duel", "basic-desert",
        "What: AQ102 S1 (CE-2355) - on open basic-desert, 'Blue' (Friend grenadier, TKB 2004) behind 'Blue Car 2' at (277,360) and 'Red' "
        "(Hostile grenadier) behind 'Red Car 2' at (283,400), ~43 m apart; three parked cars each side, nothing else. No orders: each runs his "
        "type's default SOP (all immortal, so the fight lasts). Judge (90 s): (1) neither stays hidden doing nothing (no move > 1 m, no shot) "
        "for more than 12 s while the other is alive; (2) each fires from at least two distinct spots, never the same spot twice running; "
        "(3) at least one flank or firing-position move (a car he did not start behind); (4) a grenade over cover once they are within 25 m. "
        + CHECK.format(sid="rf-car-duel"))

# ── S2 rf-car-squads: a Blue squad v a Red squad, the same ground ────────────────────────────────────────────
s = Scn("rf-car-squads", "a1020002")
for i, x in enumerate((250, 262, 277, 292, 304)): s.car(f"Blue Car {i + 1}", (x, 360, 0), nid=10260 + i)
for i, x in enumerate((256, 268, 283, 298, 310)): s.car(f"Red Car {i + 1}", (x, 400, 0), nid=10270 + i)
bq = s.squad("Blue Squad", "Friend", (277, 356, 0), 10240)
rq = s.squad("Red Squad", "Hostile", (283, 404, 0), 10250)
for i, x in enumerate((250, 262, 277, 292, 304)):
    s.man(f"Blue {i + 1}", "Friend", (x, 357, 0), 0, tkb=2004 if i % 2 == 0 else 2002, commander=bq,
          designation="SquadLeader" if i == 2 else "Wingman", nid=10241 + i)
for i, x in enumerate((256, 268, 283, 298, 310)):
    s.man(f"Red {i + 1}", "Hostile", (x, 403, 0), 180, tkb=2004 if i % 2 == 0 else 2002, commander=rq,
          designation="SquadLeader" if i == 2 else "Wingman", nid=10251 + i)
s.write("rf-car-squads", "basic-desert",
        "What: AQ102 S2 (CE-2355) - on open basic-desert, 'Blue Squad' (Friend, 'Blue 1-5' behind five parked cars at y=360) and 'Red Squad' "
        "(Hostile, 'Red 1-5' behind five cars at y=400), ~43 m apart; no orders, the squads' default SOP (all immortal). Judge (120 s): (1) a "
        "base of fire holds while others move; (2) at least 70 % of the movers' sampled path is out of every enemy's sight; (3) the moving "
        "side ends within 15 m of an enemy flank (an assault) or the other side breaks off. " + CHECK.format(sid="rf-car-squads"))

# ── S3 rf-house-obstacle: House A on bt-range between two men ────────────────────────────────────────────────
s = Scn("rf-house-obstacle", "a1020003")
s.car("Blue Car", (105.5, 88, 0), nid=10310)
s.car("Red Car", (105.5, 121, 0), nid=10311)
bg = s.man("Blue", "Friend", (105.5, 85.5, 0), 0, nid=10301)
rg = s.man("Red", "Hostile", (105.5, 123.5, 0), 180, nid=10302)
s.ents[bg]["TargetMemory"] = {"Entries": [{"Entity": rg, "PosX": 105.5, "PosY": 123.5, "PosZ": 0, "Score": 500, "Tick": 0, "Modality": 1}]}
s.ents[rg]["TargetMemory"] = {"Entries": [{"Entity": bg, "PosX": 105.5, "PosY": 85.5, "PosZ": 0, "Score": 500, "Tick": 0, "Modality": 1}]}
s.write("rf-house-obstacle", "bt-range",
        "What: AQ102 S3 (CE-2355) - on bt-range, House A (front door south, back door north) stands between 'Blue' (Friend grenadier) behind "
        "'Blue Car' at (105.5,88) and 'Red' (Hostile grenadier) behind 'Red Car' at (105.5,121); each starts knowing where the other is (as bt-window-duel); no orders, default SOP (immortal). Judge "
        "(120 s): (1) neither idles hidden more than 12 s; (2) one of them flanks: around House A or through its two doors; the route taken is "
        "printed (through / around); (3) neither stays in the house's dead ground longer than 20 s. " + CHECK.format(sid="rf-house-obstacle"))

# ── S4 rf-wall-hugger: three window defenders, one attacker who fires then hugs the wall ─────────────────────
s = Scn("rf-wall-hugger", "a1020004")
# ⭐ OBJ is x 115..125 × y 153..161, front north (Platoon Attack/Defend §12.1 as built) — 📐 the first cut placed this cast ~10 m east of it
rq = s.squad("Red Squad", "Hostile", (120, 157, 0), 10450, sop={"Name": "BasicSquadSop", "Params": "{}", "Origin": "Superior"})
for i, (x, y) in enumerate(((117, 155), (120, 155), (123, 155))):
    s.man(f"Red {i + 1}", "Hostile", (x, y, 0), 180, tkb=2002, commander=rq,
          designation="SquadLeader" if i == 0 else "Wingman", nid=10451 + i)
s.ents[rq]["Behavior"] = {"Name": "SquadDefend", "Params": json.dumps({"Position": [120, 157, 0], "SearchRadius": 10, "Speed": 2}),
                          "Origin": "Superior"}
s.man("Shooter", "Friend", (120, 122, 0), 0, tkb=2004, nid=10401,
      behavior={"Name": "CombatPosture", "Params": json.dumps({"stay": True, "post": {"point": [120, 122, 0], "radius": 2}}),
                "Origin": "Superior"},
      sop={"Name": "InertSop", "Params": "{}", "Origin": "Superior"})
hug = s.man("Hugger", "Friend", (119, 152.3, 0), 0, tkb=2004, nid=10402,
            behavior={"Name": "HoldStance", "Params": json.dumps({"stance": "Crouched"}), "Origin": "Superior"},
            sop={"Name": "InertSop", "Params": "{}", "Origin": "Superior"})
for k, e in list(s.ents.items()):   # every defender saw the Hugger arrive under the wall
    if e.get("EntityInfo", {}).get("Name", "").startswith("Red "):
        e["TargetMemory"] = {"Entries": [{"Entity": hug, "PosX": 119, "PosY": 152.3, "PosZ": 0, "Score": 500, "Tick": 0, "Modality": 1}]}
s.write("rf-wall-hugger", "cqb-village",
        "What: AQ102 S4 (CE-2355) - on cqb-village, 'Red Squad' (Hostile, 'Red 1-3', default squad SOP) defends the OBJ house (house-3f, "
        "footprint x 115-125 / y 153-161, front north) with SquadDefend. 'Shooter' (Friend grenadier, immortal) stands in the open at (120,122) and fires "
        "at the windows (CombatPosture, stay); 'Hugger' (Friend grenadier, immortal, crouched, InertSop) is against the south wall at "
        "(119,152.3), between the back door and the window - every defender saw him get there (he is in their memory). Judge (90 s): (1) a defender shot at ducks within 2 s; (2) "
        "ONE defender moves to an opening that sees the Hugger; (3) a grenade is dropped at the Hugger; (4) no defender's look follows the "
        "Hugger while he is out of that defender's sight. " + CHECK.format(sid="rf-wall-hugger"))

# ── S5 rf-grenade-reaction: one M67 lands among four men ──────────────────────────────────────────────────────
s = Scn("rf-grenade-reaction", "a1020005")
s.man("Thrower", "Hostile", (280, 365, 0), 0, tkb=2004, nid=10501,
      behavior={"Name": "FireAtPoint", "Params": json.dumps({"x": 280, "y": 380, "z": 0, "mount": 1, "rounds": 1, "cooldownSeconds": 30}),
                "Origin": "Superior"})
for i, (x, y) in enumerate(((278, 381), (282, 381), (278, 379), (282, 379))):
    s.man(f"Blue {i + 1}", "Friend", (x, y, 0), 180, tkb=2002, nid=10511 + i)
s.write("rf-grenade-reaction", "basic-desert",
        "What: AQ102 S5 (CE-2355) - on open basic-desert, 'Thrower' (Hostile grenadier) at (280,365) throws ONE M67 at (280,380), the middle of "
        "'Blue 1-4' (Friend riflemen, no orders, immortal) standing 2 m apart and facing him. Judge: (1) every Blue man who sees or hears it "
        "either leaves its lethal radius before the burst or is prone when it bursts; (2) one man with enough fuse left picks it up and "
        "throws it back. " + CHECK.format(sid="rf-grenade-reaction"))

# ── S6 rf-pinned-in-open: a lone man pinned in the open, his squad down, a gunman at a ground-floor window ──────────
s = Scn("rf-pinned-in-open", "a1020006")
gq = s.squad("Red Window", "Hostile", (120, 157, 0), 10650)
s.ents[gq]["Behavior"] = {"Name": "SquadDefend", "Origin": "Superior", "Params": json.dumps({
    "Position": [120, 157, 0], "SearchRadius": 8, "Speed": 2,
    # the south ground-floor window: OBJ is house-3f at (125,161) rotated 180° ⇒ x 115..125 × y 153..161; its local back wall (window at
    #   5.5 m from the (10,8) end) is the south face at y 153, the window x ≈ 119.9-121.1 — 📐 the first two cuts put the post ~10 m east,
    #   OUTSIDE the house, and its rounds stopped in OBJ's east wall
    "Posts": [{"point": [120.5, 153.6, 0], "watchTowards": [120.5, 104, 0], "window": True}]})}
s.man("Gunman", "Hostile", (120.5, 154.5, 0), 180, tkb=2002, commander=gq, designation="SquadLeader", nid=10651)
bq = s.squad("Blue Squad", "Friend", (120.5, 100, 0), 10600)
s.ents[bq]["Behavior"] = {"Name": "BoundingOverwatch", "Origin": "Superior",
                          "Params": json.dumps({"Objective": [120.5, 150, 0], "BoundMetres": 30})}
s.man("Pinned", "Friend", (120.5, 104, 0), 0, tkb=2002, commander=bq, designation="Wingman", nid=10601)
# his squad mates, already dead — 📐 the first cut had them incapacitated 92 m out: the Gunman never saw Pinned (the village houses in
#   the way), so there was no fire, a lull, and the squad's casualty row sent Pinned to carry one 83 m: valid, but not the case
for i, (x, y) in enumerate(((116.5, 102), (124.5, 102), (120.5, 98))):
    g = s.man(f"Down {i + 1}", "Friend", (x, y, 0), 0, tkb=2002, health=100000, commander=bq,
              designation="SquadLeader" if i == 0 else "Wingman", nid=10602 + i)
    s.ents[g]["Health"] = {"Current": 0, "Max": 100000}
s.car("Far Car", (140.5, 106, 0), heading=0, nid=10610)   # cover 20 m off: out of his 10 m crawl, inside a 30 m search
s.write("rf-pinned-in-open", "cqb-village",
        "What: AQ102 S6 (CE-2362) - on cqb-village, 'Gunman' (Hostile rifleman, immortal) holds the south ground-floor window of the OBJ house "
        "(house-3f, x 115-125 / y 153-161) at (120.5,153.6); 'Pinned' (Friend rifleman, immortal) is the last fighting man of 'Blue Squad' - 'Down 1-3' "
        "are dead beside him - out in the open south at (120.5,104), ~50 m from the window, the squad ordered to bound to the house; 'Far Car' at "
        "(140.5,106) is the only cover, 20 m off. Judge (120 s): (1) Pinned drops prone under fire and returns fire; (2) pinned, he does not "
        "stay on one spot: a 2 m+ move at least every 15 s, alternating sides; (3) after ~30 s pinned and alone he crawls to the car; "
        "(4) the aim: the Gunman's shots at a prone man are aimed at a prone chest (not over him) and Pinned's shots at the window reach "
        "the opening (not the wall under it) - printed per shooter (aim height, ends, crossings, hits). " + CHECK.format(sid="rf-pinned-in-open"))
