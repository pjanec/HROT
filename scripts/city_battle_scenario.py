#!/usr/bin/env python3
"""CE-6010 (city battle B-0) — generates scenarios/showcases/city-battle/scenario.json, the battle's SKELETON.

📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4c (as built), §3 (phases), §4 (forces), §4a (macros).
The forces are the city-battle macros (scripts/city_battle_macros.py); positions follow the overview SVG on the real town
(assets/terrain-plans/city-battle.plan.json). ⭐ EVERY place is in the ANCHORS table below, named by role — re-anchoring to a redrawn town edits only that.
The scripted top is INTENT ONLY, through the built objectives and triggers (Order / InsertMacro / Message / EndExercise).

Run: python3 scripts/city_battle_scenario.py            (writes the scenario)
"""
import json, math, os, random

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
OUT = os.path.join(ROOT, 'scenarios', 'showcases', 'city-battle', 'city-battle', 'scenario.json')   # ⚠ its own leaf: a folder holding a scenario.json hides the features/ beside it (ScenarioFolderIndex never enters a scenario folder)
TERRAIN = os.path.join(ROOT, 'assets', 'terrain', 'city-battle')

FACE_E = [0, 0, -0.7071068, 0.7071068]   # a macro anchor: its line north-south, its support element to the WEST (behind an attacker)
FACE_W = [0, 0, 0.7071068, 0.7071068]    # a macro anchor: its support element to the EAST (behind a defender)
ID = [0, 0, 0, 1]

def keys(tag):
    """A company instance's args: its three platoons' RefKeys (the company macro binds them into the nested platoons' `key`, CE-6010)."""
    return {'p1': f'{tag}1', 'p2': f'{tag}2', 'p3': f'{tag}3'}


def platoon_refs(tag):
    """The "@Key" references to a company's three platoons — resolved against the live world when the trigger fires (SR-188)."""
    return [f'@{tag}{i}' for i in (1, 2, 3)]


class Scn:
    def __init__(self, seed):
        self.ents = {}
        self.n = 0
        self.rng = random.Random(seed)

    def gid(self, block):
        self.n += 1
        return 'cb010000-%04x-4000-8000-%012d' % (block, self.n)

    def add(self, block, e, key=None):
        g = self.gid(block)
        if key:
            e['RefKey'] = {'Value': key}
        self.ents[g] = e
        return g


def at(x, y, z=0.0, rot=ID):
    return {'Position': [round(x, 2), round(y, 2), z], 'Rotation': rot}


def unit(tkb, name, side, x, y, rot=ID, hp=100, extra=None):
    e = {'SimTransform': at(x, y, rot=rot), 'TkbIdentity': {'TkbType': tkb}, 'EntityInfo': {'Name': name, 'ForceId': side}}
    if hp:
        e['Health'] = {'Current': hp, 'Max': hp}
        e['TargetMemory'] = {'Entries': []}
    if extra:
        e.update(extra)
    return e


def instance(macro, x, y, rot, args):
    return {'SimTransform': at(x, y, rot=rot), 'TkbIdentity': {'TkbType': 8813}, 'EntityInfo': {'Name': args.get('name', macro)},
            'MacroInstance': {'Macro': macro, 'Args': args}}


def obstacle(tkb, name, x, y, yaw_deg, l, w, h):
    a = math.radians(yaw_deg) / 2
    return {'SimTransform': at(x, y, rot=[0, 0, round(math.sin(a), 7), round(math.cos(a), 7)]), 'TkbIdentity': {'TkbType': tkb},
            'EntityInfo': {'Name': name}, 'ObstacleShape': {'Length': l, 'Width': w, 'Height': h}}


def area(name, side, cx, cy, pts, key=None, colour=(0, 0, 255, 40)):
    return {'EditablePolyline': {'Points': [{'X': px, 'Y': py} for px, py in pts], 'Version': 0}, 'SimTransform': at(cx, cy),
            'TkbIdentity': {'TkbType': 8803}, 'EntityInfo': {'Name': name, 'ForceId': side},
            'MapOverlayStyle': {'FillColor': list(colour), 'BorderColor': [255, 255, 255, 255], 'LineThickness': 2, 'IsClosed': True}}


def line(name, side, pts):
    cx, cy = pts[0]
    return {'EditablePolyline': {'Points': [{'X': px - cx, 'Y': py - cy} for px, py in pts], 'Version': 0}, 'SimTransform': at(cx, cy),
            'TkbIdentity': {'TkbType': 8801}, 'EntityInfo': {'Name': name, 'ForceId': side},
            'MapOverlayStyle': {'FillColor': [0, 0, 0, 0], 'BorderColor': [0, 0, 0, 255], 'LineThickness': 3, 'IsClosed': False}}


def trigger(title, side, when, do, extra=None):
    p = {'common': {'title': title}, 'when': when, 'do': do}
    if extra:
        p.update(extra)
    return {'TkbIdentity': {'TkbType': 8861}, 'EntityInfo': {'Name': title, 'ForceId': side},
            'Behavior': {'Name': 'Trigger', 'Params': p, 'Origin': 'Superior'}}


def at_time(seconds):
    return {'$type': 'ScenarioTime', 'fromSeconds': seconds}


def achieved(objective):
    return {'$type': 'OnEntity', 'ref': objective, 'predicate': {'$type': 'PropertyMatch', 'componentType': 'ObjectiveStatus', 'propertyPath': 'State',
                                                                 'operator': 'Equals', 'predicate': {'$type': 'Numeric', 'minValue': 2, 'maxValue': 2}}}


def earlier(condition, seconds):
    """§3 "starts when — the earlier of the two": the condition OR the fallback time."""
    return {'$type': 'Compound', 'operator': 'Or', 'conditions': [condition, at_time(seconds)]}


def order(target, behavior, params):
    return {'act': 'Order', 'args': {'target': target, 'behavior': behavior, 'params': params}}


def msg(text, seconds=8):
    return {'act': 'Message', 'args': {'toKind': 'All', 'text': text, 'seconds': seconds}}


# ── the town, read from the terrain (civilians stand on streets, never inside a house or the river) ──────────────────────

def load_town():
    world = json.load(open(os.path.join(TERRAIN, 'city-battle-ground.world.geojson')))
    houses = [f['geometry']['coordinates'][0] for f in world['features']
              if f['properties'].get('kind') in ('building', 'wall', 'prism') and f['geometry']['type'] == 'Polygon']
    water = [f['geometry']['coordinates'][0] for f in world['features']
             if f['properties'].get('kind') == 'surface' and f['properties'].get('surface') == 'water']
    roads = json.load(open(os.path.join(TERRAIN, 'city-battle-ground.roads.json')))
    pts = []   # every 6 m along every street (its centre line, linear between the segment's end points)
    for seg in roads['segments']:
        a, b = seg['controlPoints']['p0'], seg['controlPoints']['p1']
        n = max(1, int(math.dist((a['x'], a['y']), (b['x'], b['y'])) // 6))
        for i in range(n):
            t = i / n
            pts.append((a['x'] + (b['x'] - a['x']) * t, a['y'] + (b['y'] - a['y']) * t))
    return houses, water, pts


def inside(pt, poly):
    x, y = pt
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i][0], poly[i][1]
        xj, yj = poly[j][0], poly[j][1]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi:
            c = not c
        j = i
    return c


def clear(pt, houses, water, margin=1.5):
    for poly in houses:
        xs = [p[0] for p in poly]
        ys = [p[1] for p in poly]
        if min(xs) - margin <= pt[0] <= max(xs) + margin and min(ys) - margin <= pt[1] <= max(ys) + margin:
            if inside(pt, poly) or any(inside((pt[0] + dx, pt[1] + dy), poly) for dx in (-margin, margin) for dy in (-margin, margin)):
                return False
    return not any(inside(pt, poly) for poly in water)


# ══ EVERY PLACE OF THE BATTLE, IN ONE TABLE — named by ROLE ═══════════════════════════════════════════════════════════════════════════════════
#   ⭐ The town is being redrawn (radial roads, no ring road): re-anchoring the battle = editing THIS block only. Every entity below is placed at an
#   anchor + a small local offset (a team's spread), never at a hand coordinate. (x, y) in the terrain's local metres, y north.
ANCHORS = {
    # the town
    'town-centre': (915.0, 750.0),        # what the civilian areas are measured from
    'square': (915.0, 744.0),             # the town hall on the square — the attacker's objective, the reserve's counterattack target
    # the attacker, in the west fields
    'attack-A': (200, 1000), 'attack-B': (200, 520),          # A Coy / B Coy (mech-company anchors)
    'hq-A': (140, 1000), 'hq-B': (140, 520), 'battalion-hq': (90, 760),
    'support': (110, 760),                # mortars, drone teams, engineers + bulldozer, jammer — laid out round it
    'javelin-N': (260, 960), 'javelin-C': (260, 760), 'javelin-S': (260, 560),
    'lz': (90, 1250),                     # the air-assault platoon beside its 4 UH-60s (held)
    'airfield': (60, 1420),               # the A-10 and the AH-64, parked (held)
    'wave-entry': (60, 760),              # the 2nd wave's InsertMacro point (@WaveEntry)
    # the defender
    'defence-1': (690, 950), 'defence-2': (640, 750), 'defence-3': (690, 560),   # 1 / 2 / 3 Coy (D), north → south on the main line
    'reserve': (1250, 750),               # the reserve company, east of the river
    'hill': (1400, 740),                  # the hill's mortars
    'aa-zu23': (1390, 790), 'aa-stinger-1': (1380, 700), 'aa-stinger-2': (1420, 790), 'aa-igla-1': (1360, 760), 'aa-igla-2': (1410, 650),
    'red-drones-N': (980, 900), 'red-drones-S': (980, 600),
    # the prepared positions: a roadblock where each attack road enters the main line (✕ in the overview), the outposts forward of it
    'roadblock-N': (614.0, 966.2), 'roadblock-C': (540.0, 750.0), 'roadblock-S': (600.0, 553.8),
    'outpost-N': (480, 1000), 'outpost-S': (480, 520),
    # the scripted top's places
    'plc-A': (760, 900), 'plc-B': (760, 600),                 # where A / B Coy go on to at PL C
}
# the phase lines (x of a north-south line; the attack runs west → east) and the sectors' centres
PHASE_LINES = {'PL A (town edge)': 470, 'PL B (main line)': 560, 'PL C (old town)': 760, 'PL D (river)': 1085}
SECTORS = {'Sector North (A Coy)': (600, 1050), 'Sector South (B Coy)': (600, 450)}
# the civilians' areas: (centre anchor, min radius, max radius, west of x) — the streets inside them get people
CIVILIAN_AREAS = {'west belt': ('town-centre', 230, 345, 800), 'old town': ('town-centre', 0, 220, 1085)}
# ⭐ CE-6011 (V-A, D-16) — the VARIANT FORMS (Scenario Macros §17): variant 0 = as authored (the anchor above); a run's "$variant" picks one of these
#   instead. Defender layout: forward on the main line · deeper in the town; air defence: the east hamlets · the old town's river edge.
ALTERNATIVES = {
    'defence-1': [(640, 965), (760, 930)], 'defence-2': [(600, 760), (720, 750)], 'defence-3': [(650, 545), (760, 580)],
    'reserve': [(1260, 860), (1250, 640)],
    'aa-zu23': [(1250, 880), (1235, 620)], 'aa-stinger-1': [(1265, 650), (1030, 770)], 'aa-stinger-2': [(1240, 860), (1035, 700)],
    'aa-igla-1': [(1270, 830), (1030, 840)], 'aa-igla-2': [(1225, 480), (1035, 640)],
}
ROADBLOCK_PRESENCE = 0.6   # the roadblock set: each roadblock kept with this chance
CIVILIAN_PRESENCE = 0.7    # the civilians' density: each one present with this chance (Civilian 1 always — the checks name him)


def A(role, dx=0.0, dy=0.0):
    x, y = ANCHORS[role]
    return x + dx, y + dy


def in_area(p, area):
    centre, rmin, rmax, west_of = CIVILIAN_AREAS[area]
    r = math.dist(p, ANCHORS[centre])
    return rmin <= r <= rmax and p[0] < west_of


def places(entity, role, variants):
    """⭐ CE-6011 — a one-of-N place (SR-101 ④): the role's anchor FIRST (variant 0), then its alternatives."""
    if variants and role in ALTERNATIVES:
        entity['Positions'] = [[x, y, 0] for x, y in [ANCHORS[role]] + ALTERNATIVES[role]]
    return entity


def build(civilians=200, seed=6010, variants=True):
    s = Scn(seed)
    houses, water, road_nodes = load_town()
    F, H, N = 'Friend', 'Hostile', 'Neutral'

    # ① ATTACKER (west fields) — 2 mechanised companies, the air-assault platoon (held), the support element
    a_coy = s.add(1, instance('city-battle/mech-company', *A('attack-A'), FACE_E, keys('A')))
    b_coy = s.add(1, instance('city-battle/mech-company', *A('attack-B'), FACE_E, keys('B')))
    s.ents[a_coy]['EntityInfo']['Name'] = 'A Coy (mech)'
    s.ents[b_coy]['EntityInfo']['Name'] = 'B Coy (mech)'
    s.add(1, instance('city-battle/rifle-platoon', *A('lz'), FACE_E, {'name': 'Air Assault Platoon', 'key': 'AirAssault'}))
    for i in range(4):
        s.add(2, unit(400, f'UH-60 {i + 1}', F, *A('lz', -30 + 30 * i, 80), hp=1000))
    s.add(2, unit(405, 'A-10 (held)', F, *A('airfield'), hp=1500))
    s.add(2, unit(406, 'AH-64 (held)', F, *A('airfield', 50, 0), hp=800))
    for name, role in (('Battalion HQ', 'battalion-hq'), ('A Coy HQ', 'hq-A'), ('B Coy HQ', 'hq-B')):
        s.add(3, unit(102, name, F, *A(role), hp=400))
    s.add(3, unit(102, 'Jammer vehicle', F, *A('support', 10, -30), hp=400))   # ⚠ no jammer TYPE exists — a HMMWV carries the role's name (CE-6018)
    ms = s.add(3, unit(304, 'Mortar Section', F, *A('support', 0, 30), hp=0))
    for i in range(3):
        tube = s.add(3, {'SimTransform': at(*A('support', -6 + 6 * i, 30)), 'TkbIdentity': {'TkbType': 610}, 'EntityInfo': {'Name': f'Mortar Tube {i + 1}', 'ForceId': F}})
        for j in range(2):
            s.add(3, unit(2002, f'Mortar {i + 1} Crew {j + 1}', F, *A('support', -6 + 6 * i + (j - 0.5) * 1.5, 28), extra={
                'UnitSubordinate': {'CommanderGuid': ms, 'Designation': 'SquadLeader' if i == j == 0 else 'Wingman'},
                **({'Behavior': {'Name': 'ManWeapon', 'Params': {'weapon': tube}, 'Origin': 'Superior'}} if j == 0 else {})}))
    for t, dy in ((1, -60), (2, 70)):   # drone teams: operators + FPVs on the ground until tasked
        for j in range(2):
            s.add(3, unit(2002, f'Drone Team {t} Operator {j + 1}', F, *A('support', 40 + 2 * j, dy)))
            s.add(3, unit(403, f'Blue FPV {t}-{j + 1}', F, *A('support', 45 + 3 * j, dy + 4), hp=20))
    for t, role in ((1, 'javelin-N'), (2, 'javelin-C'), (3, 'javelin-S')):
        for j in range(2):
            s.add(3, unit(2006, f'Javelin Team {t} Gunner {j + 1}', F, *A(role, 2 * j, 0)))
    eng = s.add(3, unit(302, 'Engineer Squad', F, *A('support', 65, 0), hp=0))   # the support element holds the attachments (§4)
    for j in range(6):
        s.add(3, unit(2002, f'Engineer {j + 1}', F, *A('support', 60 + 2 * j, -3), extra={'UnitSubordinate': {'CommanderGuid': eng, 'Designation': 'SquadLeader' if j == 0 else 'Wingman'}}))
    s.add(3, unit(112, 'Bulldozer', F, *A('support', 75, -20), hp=1000))
    s.add(3, area('Wave entry', F, *A('wave-entry'), [(-15, -15), (-15, 15), (15, 15), (15, -15)]), key='WaveEntry')

    # ② DEFENDER (the town) — 1/2/3 Coy north→south on the main line, the reserve east of the river
    d1 = s.add(4, places(instance('city-battle/defender-company', *A('defence-1'), FACE_W, keys('D1')), 'defence-1', variants))
    d2 = s.add(4, places(instance('city-battle/defender-company', *A('defence-2'), FACE_W, keys('D2')), 'defence-2', variants))
    d3 = s.add(4, places(instance('city-battle/defender-company', *A('defence-3'), FACE_W, keys('D3')), 'defence-3', variants))
    dr = s.add(4, places(instance('city-battle/defender-company', *A('reserve'), FACE_W, keys('DR')), 'reserve', variants))
    for g, name in ((d1, '1 Coy (D)'), (d2, '2 Coy (D)'), (d3, '3 Coy (D)'), (dr, 'Reserve Coy (D)')):
        s.ents[g]['EntityInfo']['Name'] = name
    dms = s.add(5, unit(304, 'Hill Mortar Section', H, *A('hill', 0, -40), hp=0))
    for i in range(3):
        tube = s.add(5, {'SimTransform': at(*A('hill', -6 + 6 * i, -40)), 'TkbIdentity': {'TkbType': 610}, 'EntityInfo': {'Name': f'Hill Mortar Tube {i + 1}', 'ForceId': H}})
        for j in range(2):
            s.add(5, unit(2003, f'Hill Mortar {i + 1} Crew {j + 1}', H, *A('hill', -6 + 6 * i + (j - 0.5) * 1.5, -42), extra={
                'UnitSubordinate': {'CommanderGuid': dms, 'Designation': 'SquadLeader' if i == j == 0 else 'Wingman'},
                **({'Behavior': {'Name': 'ManWeapon', 'Params': {'weapon': tube}, 'Origin': 'Superior'}} if j == 0 else {})}))
    s.add(5, places(unit(110, 'ZU-23 Technical', H, *A('aa-zu23'), hp=500, extra={'Sop': {'Name': 'BasicVehicleSop', 'Params': {}, 'Origin': 'Superior'}}),
                    'aa-zu23', variants), key='HillAA')
    for name, tkb, role in (('Stinger 1', 202, 'aa-stinger-1'), ('Stinger 2', 202, 'aa-stinger-2'), ('Igla 1', 203, 'aa-igla-1'), ('Igla 2', 203, 'aa-igla-2')):
        s.add(5, places(unit(tkb, name, H, *A(role), extra={'Sop': {'Name': 'BasicInfantrySop', 'Params': {}, 'Origin': 'Superior'}}), role, variants))
    for t, role in ((1, 'red-drones-N'), (2, 'red-drones-S')):
        for j in range(2):
            s.add(5, unit(2003, f'Red Drone Team {t} Operator {j + 1}', H, *A(role, 2 * j, 0)))
            s.add(5, unit(403, f'Red FPV {t}-{j + 1}', H, *A(role, 4 + 3 * j, 4), hp=20))
    # ③ PREPARED POSITIONS (D-3) — a roadblock macro where each attack road enters the main line (blocks + cars across the road west of the anchor,
    #    an MG nest behind sandbags each side, an AT team), the outposts forward
    for k in ('N', 'C', 'S'):
        rb = instance('city-battle/roadblock', *A(f'roadblock-{k}'), ID, {})
        rb['EntityInfo']['Name'] = f'Roadblock {k}'
        if variants:
            rb['Presence'] = ROADBLOCK_PRESENCE   # ⭐ CE-6011 — the ROADBLOCK SET: each roadblock kept or dropped WHOLE by one roll
        s.add(6, rb)
    for k, role in (('North', 'outpost-N'), ('South', 'outpost-S')):
        for j in range(2):
            s.add(6, unit(2003, f'Outpost {k} {j + 1}', H, *A(role, 0, 3 * j), rot=FACE_W))

    # ④ CIVILIANS (§3c) — on the streets of the named areas, with their own brain (CivilianHsm); a few parked cars in the old town
    pts = [p for p in road_nodes if any(in_area(p, a) for a in CIVILIAN_AREAS)] * 3   # a street point may hold up to three people (jittered ±4 m)
    s.rng.shuffle(pts)
    placed = 0
    cars = 0
    for (x, y) in pts:
        if placed >= civilians and cars >= 6:
            break
        p = (x + s.rng.uniform(-4, 4), y + s.rng.uniform(-4, 4))
        if not clear(p, houses, water):
            continue
        if cars < 6 and placed % 30 == 0 and in_area(p, 'old town'):
            s.add(7, unit(1002, f'Parked car {cars + 1}', N, p[0], p[1], hp=300))
            cars += 1
            continue
        if placed < civilians:
            civ = unit(1101 + placed % 4, f'Civilian {placed + 1}', N, p[0], p[1])
            if variants and placed > 0:   # ⭐ CE-6011 — the civilians' DENSITY
                civ['Presence'] = CIVILIAN_PRESENCE
            s.add(7, civ)
            placed += 1

    # ⑤ THE SCRIPTED TOP — intent only (§3): phase lines, sectors, objectives, the triggers and their fallbacks
    for name, x in PHASE_LINES.items():
        s.add(8, line(name, F, [(x, 1250), (x, 250)]))
    for name, (x, y) in SECTORS.items():
        s.add(8, area(name, F, x, y, [(-500, -200), (-500, 200), (500, 200), (500, -200)], colour=(0, 0, 255, 20)))
    bhq = [g for g, e in s.ents.items() if e['EntityInfo']['Name'] == 'Battalion HQ'][0]
    a_plt, b_plt = platoon_refs('A'), platoon_refs('B')
    d_plt = {d1: platoon_refs('D1'), d2: platoon_refs('D2'), d3: platoon_refs('D3'), dr: platoon_refs('DR')}
    sq = A('square')
    hall = s.add(8, {'TkbIdentity': {'TkbType': 8860}, 'EntityInfo': {'Name': 'Seize the town hall', 'ForceId': F},
                     'Behavior': {'Name': 'HoldArea', 'Params': {'common': {'title': 'Seize the town hall', 'subject': bhq, 'required': True},
                                                                'area': {'circle': {'center': [sq[0], sq[1], 0], 'radius': 40}},
                                                                'minMen': 6, 'maxEnemy': 0, 'holdSeconds': 180}, 'Origin': 'Superior'}})
    reached = {}
    for tag, name, role, deadline in (('A', 'A Coy reaches PL B', 'roadblock-N', 1020), ('B', 'B Coy reaches PL B', 'roadblock-S', 1200)):
        c = A(role)
        reached[tag] = s.add(8, {'TkbIdentity': {'TkbType': 8860}, 'EntityInfo': {'Name': name, 'ForceId': F},
                                 'Behavior': {'Name': 'ReachArea', 'Params': {'common': {'title': name, 'subject': bhq, 'deadlineSeconds': deadline},
                                                                             'area': {'circle': {'center': [c[0], c[1], 0], 'radius': 60}}, 'minMen': 10},
                                              'Origin': 'Superior'}})
    armed = {'fireIfTrueWhenArmed': True}

    def attack(plts, role, spread):
        c = A(role)
        return [order(p, 'PlatoonAttack', {'Position': [c[0], c[1] + dy, 0], 'SearchRadius': 60, 'Preset': 'AttackPosition', 'Speed': 3})
                for p, dy in zip(plts, (-spread, 0, spread))]
    # t = 0: the defenders take their positions — every platoon PlatoonDefend WHERE IT STANDS ("@D11": a variant may move it), facing the fields
    for g in (d1, d2, d3):
        s.add(9, trigger(f'{s.ents[g]["EntityInfo"]["Name"]} takes position', H, at_time(0),
                         [order(p, 'PlatoonDefend', {'Position': p, 'Left': 225, 'Right': 315, 'MaxRange': 300, 'Speed': 2}) for p in d_plt[g]], armed))
    s.add(9, trigger('Reserve Coy (D) holds east of the river', H, at_time(0),
                     [order(p, 'PlatoonDefend', {'Position': p, 'Left': 225, 'Right': 315, 'MaxRange': 300, 'Speed': 2}) for p in d_plt[dr]], armed))
    s.add(9, trigger('Lethality (tune here)', N, at_time(0),     # the CE-6008 knob (§4b) at its neutral value
                     [{'act': 'SetSkillAmplifier', 'args': {'side': 'Friend', 'tiers': 0}},
                      {'act': 'SetSkillAmplifier', 'args': {'side': 'Hostile', 'tiers': 0}}], armed))
    s.add(9, trigger('Phase 1: shaping', F, at_time(0), [msg('Phase 1 - shaping: drones up, mortars register on PL A, find the hill AA')], armed))
    # phase 2: the breach, A Coy at 5 min, B Coy at 8 min
    s.add(9, trigger('Phase 2: A Coy breach to PL B', F, at_time(300), [msg('Phase 2: A Coy breach to PL B')] + attack(a_plt, 'roadblock-N', 60)))
    s.add(9, trigger('Phase 2: B Coy breach to PL B', F, at_time(480), [msg('Phase 2: B Coy breach to PL B')] + attack(b_plt, 'roadblock-S', 60)))
    # phase 4: the urban grind — a company that REACHED PL B attacks to PL C (or at its fallback time: the earlier of the two, §3)
    s.add(9, trigger('Phase 4: A Coy to PL C', F, earlier(achieved(reached['A']), 900), [msg('Phase 4: A Coy to PL C')] + attack(a_plt, 'plc-A', 50)))
    s.add(9, trigger('Phase 4: B Coy to PL C', F, earlier(achieved(reached['B']), 1080), [msg('Phase 4: B Coy to PL C')] + attack(b_plt, 'plc-B', 50)))
    # the 2nd wave (macro) at 20 min, sent at the centre roadblock
    s.add(9, trigger('Phase 4: 2nd wave', F, at_time(1200),
                     [{'act': 'InsertMacro', 'args': {'macro': 'city-battle/mech-company', 'at': '@WaveEntry', 'heading': 90,
                                                      'params': {'side': 'Friend', **keys('W')}}},
                      msg('Phase 4 - the 2nd wave company is in')] + attack(platoon_refs('W'), 'roadblock-C', 60)))
    # phase 5: the reserve counterattacks the square — when the attacker holds it, or at 28 min
    s.add(9, trigger('Phase 5: reserve counterattack', H, earlier(achieved(hall), 1680),
                     [msg('Phase 5 - the defender reserve counterattacks over the bridges')] + attack(d_plt[dr], 'square', 40)))
    # phase 6: the end — the town hall held (HoldArea's own 3 min), or 45 min. ⚠ "one side < 30 %" is a census the rules do not count yet (CE-6018)
    s.add(9, trigger('Phase 6: end', N, earlier(achieved(hall), 2700), [{'act': 'EndExercise', 'args': {'reason': 'the town hall held, or time'}}]))

    header = {
        'subsystemType': 'Hrot.Scenario', 'schemaVersion': '1.0', 'terrainName': 'city-battle',
        'description': (
            'What: CE-6010 (city battle B-0, docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4c) - the SKELETON of the city battle on the '
            'city-battle town (1.5 km): attacker (Friend) from the west fields - A Coy and B Coy (mech-company macros: 3 platoons of 6 squads + 2 M1 + '
            '4 IFV each), the air-assault platoon beside 4 UH-60s (held), A-10 + AH-64 parked (held), mortar section, 2 drone teams, 3 Javelin teams, '
            'an engineer squad + the bulldozer, HQ and jammer HMMWVs; defender (Hostile) - 1/2/3 Coy (defender-company macros: 3 militia platoons + 2 '
            'technicals each) north to south behind the ring road, the reserve company east of the river, mortars + ZU-23 + Stinger / Igla on the hill, '
            '2 drone teams, roadblocks (concrete blocks + cars) on the three ring-road crossings with MG nests behind sandbags, AT teams, two outposts; '
            f'{civilians} civilians on the streets of the west belt and the old town + 6 parked cars. Visibility 5 km, 10:00 (daylight). '
            'The scripted top is intent only: phase lines PL A-D, two sectors, the objectives (seize the town hall, A / B Coy reach PL B), the defenders '
            'take positions at 0 s, A Coy attacks to PL B at 5 min, B Coy at 8; a company that reached PL B goes on to PL C (else at 15 / 18 min); the 2nd wave (macro) enters at 20 and attacks; the reserve counterattacks when the attacker holds the town hall (else at 28); the end when the town hall is held (else at 45). '
            'Recording is the cluster default (ON). Judge (60 s): every entity Active on every node, the defenders on their positions, the recorder writing, '
            'no fault. Check: the rail CityBattleSkeletonTests (Hrot.ClusterRunner.Integration.Tests); generated by scripts/city_battle_scenario.py.'),
        'world': {
            'ScenarioClock': {'Start': '2025-06-21T10:00:00+02:00'},
            'WeatherConditions': {'GroundTemperatureC': 15, 'LapseRateKPerKm': 6.5, 'SeaLevelPressureHpa': 1013.25, 'RelativeHumidity': 0.8,
                                  'WindCount': 0, 'CloudCount': 1, 'VisibilityM': 5000, 'FogTopM': 0, 'FogVisibilityM': 5000, 'Precipitation': 0,
                                  'PrecipitationMmPerHour': 0, 'FeatherM': 0, 'WindAltitudeM': [0] * 8, 'WindFromDeg': [0] * 8,
                                  'WindSpeedMps': [0] * 8, 'WindGustMps': [0] * 8, 'CloudBaseM': [1500, 0, 0, 0],
                                  'CloudThicknessM': [600, 0, 0, 0], 'CloudOctas': [3, 0, 0, 0], 'CloudKind': [1, 0, 0, 0]}},
    }
    return {'$meta': {'docType': 'Hrot.Scenario', 'schemaVersion': 1}, 'header': header, 'entities': s.ents}, placed


if __name__ == '__main__':
    doc, placed = build()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w') as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print(f'{OUT}: {len(doc["entities"])} entities, {placed} civilians')
