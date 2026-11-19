#!/usr/bin/env python3
"""CE-3490 (docs/designs/air-attack/DESIGN_Breath_Attack.md §5) — writes the breath-attack scenarios under scenarios/fun/dragons/:
dr-dragon-attack (BA-S2: the strafe), dr-dragon-village (BA-S2: the hover on a timber house, never on brick), dr-dragon-holds (BA-S3: a civilian
holds the breath; the ROE forbids it); and under scenarios/demos/air/ ah-flame-attack (BA-9: the flame helicopter, 412 — the breath on men, rockets on armour). Each gets scenario.json + checks.json (R-4277); goldens are captured by scenario-regression.sh."""
import json, os

ROOT = os.path.join(os.path.dirname(__file__), '..', 'scenarios', 'fun', 'dragons')
Q = [0, 0, 0, 1]

def uid(s, n): return f"d3490{s:03d}-0000-4000-8000-{n:012d}"

def dragon(name, at, behavior, roe_incendiary=None, tkb=411):
    roe = {"Fire": "FireAtWill", "Reactions": "StayOnTask", "SetBy": "Operator", "ForceLevel": "Lethal"}
    if roe_incendiary: roe["Incendiary"] = roe_incendiary
    return {"SimTransform": {"Position": at, "Rotation": Q}, "TkbIdentity": {"TkbType": tkb},
            "EntityInfo": {"Name": name, "ForceId": "Friend"}, "TargetMemory": {"Entries": []},
            "Sop": {"Name": "InertSop", "Params": {}, "Origin": "Superior"}, "Roe": roe, "Behavior": behavior}

def man(name, at, force="Hostile"):
    return {"SimTransform": {"Position": at, "Rotation": Q}, "Health": {"Current": 100, "Max": 100}, "TkbIdentity": {"TkbType": 2008},
            "EntityInfo": {"Name": name, "ForceId": force}, "TargetMemory": {"Entries": []},
            "Sop": {"Name": "InertSop", "Params": {}, "Origin": "Superior"},
            "Behavior": {"Name": "HoldStance", "Params": {"stance": "Standing"}, "Origin": "Superior"}}

def house(name, at, template):
    return {"TkbIdentity": {"TkbType": 8818}, "TerrainFeature": {"Kind": 1, "Template": template, "HeadingDeg": 0, "Pad": 0, "Seed": 0},
            "SimTransform": {"Position": at, "Rotation": Q}, "EntityInfo": {"Name": name, "ForceId": "Neutral"}}

def attack(target): return {"Name": "AirPlatformAttack", "Params": {"Target": target}, "Origin": "Superior"}
def strike(loc, kind): return {"Name": "AirPlatformStrike", "Params": {"Location": loc, "TargetKind": kind}, "Origin": "Superior"}

def write(name, desc, entities, checks, root=None):
    d = os.path.join(root or ROOT, name)
    os.makedirs(d, exist_ok=True)
    s = {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
         "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "basic-desert", "description": desc},
         "entities": entities}
    with open(os.path.join(d, 'scenario.json'), 'w') as f: json.dump(s, f, indent=1); f.write('\n')
    with open(os.path.join(d, 'checks.json'), 'w') as f: json.dump(checks, f, indent=2); f.write('\n')

def field(who, path, op, value, **kw): return dict({"on": "field", "who": who, "path": path, "op": op, "value": value}, **kw)
DOC = "docs/DESIGN_Scenario_Regression.md §3 — CE-3490 (docs/designs/air-attack/DESIGN_Breath_Attack.md §5): "

# ── dr-dragon-attack — BA-S2, the strafe ──────────────────────────────────────────────────────────────────────────────
ents = {}
ents[uid(1, 1)] = dragon("Dragon", [100, 300, 60], attack(uid(1, 11)))
for i in range(4): ents[uid(1, 11 + i)] = man(f"Squad Man {i + 1}", [400, 288 + 8 * i, 0])
for i in range(2): ents[uid(1, 21 + i)] = man(f"Far Man {i + 1}", [400 + 6 * i, 520, 0])
write("dr-dragon-attack",
      "What: the dragon's STRAFE (CE-3490 BA-1…BA-3, docs/designs/air-attack/DESIGN_Breath_Attack.md). On basic-desert a 'Dragon' (TKB 411) 60 m up at (100, 300) "
      "is ordered AirPlatformAttack on 'Squad Man 1'; the squad (four riflemen, Hostile, holding) stands in a north-south row at x 400, y 288…312; 'Far Man 1/2' stand "
      "200 m north at (400…406, 520). Judge: the dragon breathes; it comes down below 20 m to breathe; the squad men burn and Squad Man 1 dies; after the first man "
      "burns the dragon climbs back above 50 m (out and round for another pass); the far men are never hurt.",
      ents,
      {"$doc": DOC + "the strafe — low over the squad, burns it, climbs out between passes, never touches the far men.", "runFor": 150, "golden": {"every": 10},
       "anchors": {"lit": field("Squad Man 1", "Health.Current", "<", 100)},
       "expect": [
           {"name": "the dragon breathes", **field("Dragon", "WeaponAmmo.Ammo0", "<", 600), "before": 60},
           {"name": "it comes down low to breathe", **field("Dragon", "SimTransform.Position.2", "<", 20), "before": 60},
           {"name": "Squad Man 1 burns", **field("Squad Man 1", "Health.Current", "<", 100), "before": 60},
           {"name": "a second squad man burns", **field("*Squad Man ", "Health.Current", "<", 100, quantifier="atLeast:2"), "before": 120},
           {"name": "Squad Man 1 dies", **field("Squad Man 1", "Health.Current", "<=", 0), "before": 140},
           {"name": "it climbs back out above 50 m", **field("Dragon", "SimTransform.Position.2", ">", 50), "after": "lit"},
           {"name": "the far men are never hurt", "never": field("*Far Man ", "Health.Current", "<", 100)},
       ]})

# ── dr-dragon-village — BA-S2, the hover ──────────────────────────────────────────────────────────────────────────────
ents = {}
ents[uid(2, 1)] = house("Timber House", [300, 300, 0], "timber-house")
ents[uid(2, 2)] = house("Brick Shed", [300, 420, 0], "shed")
ents[uid(2, 11)] = man("Hut Man", [306.5, 305, 0])
ents[uid(2, 12)] = man("Shed Man", [303, 422.5, 0])
ents[uid(2, 21)] = dragon("Dragon", [100, 300, 40], strike([304, 303, 0], "Building"))
ents[uid(2, 22)] = dragon("Brick Dragon", [100, 420, 40], strike([304, 423, 0], "Building"))
write("dr-dragon-village",
      "What: the dragon's HOVER on a building (CE-3490 BA-4, BA-5). On basic-desert a 'Timber House' (timber-house template, SW corner at (300, 300)) with 'Hut Man' "
      "inside, and a 'Brick Shed' (shed template, brick, at (300, 420)) with 'Shed Man' inside. 'Dragon' (40 m up at (100, 300)) is ordered AirPlatformStrike "
      "(Building) on the timber house; 'Brick Dragon' (at (100, 420)) on the brick shed. Judge: the Dragon breathes on the timber house from a low hover and the house "
      "burns — Hut Man burns from the building's fire and dies; the Brick Dragon never breathes (its strike fails at once: brick does not burn) and Shed Man is never hurt.",
      ents,
      {"$doc": DOC + "the hover on a timber house; a brick one is never breathed on.", "runFor": 120, "golden": {"every": 10},
       "expect": [
           {"name": "the dragon breathes on the timber house", **field("Dragon", "WeaponAmmo.Ammo0", "<", 600), "before": 60},
           {"name": "it hovers low to breathe", **field("Dragon", "SimTransform.Position.2", "<", 15), "before": 60},
           {"name": "the man in the timber house burns", **field("Hut Man", "Health.Current", "<", 100), "before": 80},
           {"name": "the man in the timber house dies", **field("Hut Man", "Health.Current", "<=", 0), "before": 118},
           {"name": "the brick dragon never breathes", "never": field("Brick Dragon", "WeaponAmmo.Ammo0", "<", 600)},
           {"name": "the man in the brick shed is never hurt", "never": field("Shed Man", "Health.Current", "<", 100)},
       ]})

# ── dr-dragon-holds — BA-S3, the civilian and the ROE ─────────────────────────────────────────────────────────────────
ents = {}
ents[uid(3, 1)] = dragon("Held Dragon", [100, 200, 60], attack(uid(3, 11)))
for i in range(3): ents[uid(3, 11 + i)] = man(f"Held Man {i + 1}", [400, 188 + 12 * i, 0])
ents[uid(3, 19)] = man("Civilian", [400, 189.5, 0], force="Neutral")
ents[uid(3, 2)] = dragon("Control Dragon", [100, 450, 60], attack(uid(3, 21)))
for i in range(3): ents[uid(3, 21 + i)] = man(f"Control Man {i + 1}", [400, 438 + 12 * i, 0])
ents[uid(3, 3)] = dragon("Forbidden Dragon", [100, 700, 60], attack(uid(3, 31)), roe_incendiary="IncendiaryForbidden")
for i in range(3): ents[uid(3, 31 + i)] = man(f"Forbidden Man {i + 1}", [400, 688 + 12 * i, 0])
write("dr-dragon-holds",
      "What: the breath's release gates on a dragon (CE-3490 BA-6, F-9 Protocol III). On basic-desert three dragons (TKB 411, 60 m up at x 100) each attack the first "
      "man of a row of three hostile riflemen at x 400: 'Held Dragon' the Held Men (y 188…212) with a neutral 'Civilian' standing 1.5 m beside Held Man 1 (400, 189.5); 'Control Dragon' the "
      "same row with no civilian (y 438…462); 'Forbidden Dragon' (its ROE forbids incendiaries) the Forbidden Men (y 688…712). Judge: the Held Dragon holds its breath while the civilian "
      "is inside the flame it would throw (DESIGN_Breath_Attack §7: the hold is the burn cone) and the civilian is never hurt; the Control Dragon burns its row and Control Man 1 dies; the "
      "Forbidden Dragon never breathes and its men are never hurt.",
      ents,
      {"$doc": DOC + "a civilian in the flame holds the breath; the control row burns; an ROE forbidding incendiaries never breathes.", "runFor": 150, "golden": {"every": 10},
       "expect": [
           {"name": "the control dragon breathes", **field("Control Dragon", "WeaponAmmo.Ammo0", "<", 600), "before": 60},
           {"name": "Control Man 1 dies", **field("Control Man 1", "Health.Current", "<=", 0), "before": 140},
           {"name": "the civilian is never hurt", "never": field("Civilian", "Health.Current", "<", 100)},
           {"name": "the forbidden dragon never breathes", "never": field("Forbidden Dragon", "WeaponAmmo.Ammo0", "<", 600)},
           {"name": "the forbidden men are never hurt", "never": field("*Forbidden Man ", "Health.Current", "<", 100)},
       ]})
# ── ah-flame-attack — BA-9, the flame helicopter ─────────────────────────────────────────────────────────────────────
AIR = os.path.join(os.path.dirname(__file__), '..', 'scenarios', 'demos', 'air')
ents = {}
ents[uid(4, 1)] = dragon("Flame Helicopter", [100, 300, 60], attack(uid(4, 11)), tkb=412)
for i in range(4): ents[uid(4, 11 + i)] = man(f"Squad Man {i + 1}", [400, 288 + 8 * i, 0])
ents[uid(4, 2)] = dragon("Armour Hunter", [-1200, 560, 60], attack(uid(4, 21)), tkb=412)
ents[uid(4, 21)] = {"SimTransform": {"Position": [400, 560, 0], "Rotation": Q}, "TkbIdentity": {"TkbType": 103},
                    "EntityInfo": {"Name": "T-72", "ForceId": "Hostile"}}
write("ah-flame-attack",
      "What: the FLAME HELICOPTER (TKB 412 — the AH-64 airframe with a chin flame projector and its Hydra-70 pod; CE-3490 BA-9, "
      "docs/designs/air-attack/DESIGN_Breath_Attack.md §7). On basic-desert 'Flame Helicopter', 60 m up at (100, 300), is ordered AirPlatformAttack on "
      "'Squad Man 1' of a row of four hostile riflemen at x 400, y 288…312; 'Armour Hunter', the same type 60 m up at (-1200, 560), on a hostile 'T-72' at "
      "(400, 560). Judge: the Flame Helicopter strafes low and breathes, two squad men burn and Squad Man 1 dies, and it climbs back above 50 m; the Armour "
      "Hunter never breathes on the tank (an armoured hull does not burn) and fires its rockets at it instead.",
      ents,
      {"$doc": DOC + "the flame helicopter — its breath on men, its rockets on armour.", "runFor": 120, "golden": {"every": 10},
       "anchors": {"lit": field("Squad Man 1", "Health.Current", "<", 100)},
       "expect": [
           {"name": "the flame helicopter breathes", **field("Flame Helicopter", "WeaponAmmo.Ammo0", "<", 600), "before": 60},
           {"name": "it comes down low to breathe", **field("Flame Helicopter", "SimTransform.Position.2", "<", 20), "before": 60},
           {"name": "a second squad man burns", **field("*Squad Man ", "Health.Current", "<", 100, quantifier="atLeast:2"), "before": 90},
           {"name": "Squad Man 1 dies", **field("Squad Man 1", "Health.Current", "<=", 0), "before": 110},
           {"name": "it climbs back out above 50 m", **field("Flame Helicopter", "SimTransform.Position.2", ">", 50), "after": "lit"},
           {"name": "the armour hunter fires its rockets", **field("Armour Hunter", "WeaponAmmo.Ammo1", "<", 38), "before": 90},
           {"name": "the armour hunter never breathes on the tank", "never": field("Armour Hunter", "WeaponAmmo.Ammo0", "<", 600)},
       ]}, root=AIR)
print("written")
