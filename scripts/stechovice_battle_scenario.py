#!/usr/bin/env python3
"""The Stechovice battle - generates scenarios/showcases/stechovice-battle/scenario.json (a SHOW: two battles + an air war on the real OSM terrain).

Run: python3 scripts/stechovice_battle_scenario.py            (writes the scenario and prints the placement table)

What it reuses (nothing re-implemented):
  * the city-battle PLATOON kits - macros/city-battle/rifle-platoon (Blue, 2002 men) and militia-platoon (Red, 2003 men), read from the
    committed macro files (scripts/city_battle_macros.py writes them): a body-less platoon 305 commanding squads 302 of 6 men with the
    squad roles. Only the first THREE squads are taken (user: "leader + 3 squads"), placed on a line across their facing.
  * the proven air pieces - Blue HQ 305 SupportCommand owning an Apache (AirPlatformAttack) and an A-10 (AirPlatformStrike) as in
    scenarios/demos/air/sam-site; ZU-23 technical 110 / radar SAM 114 on their default SOPs as in ua-air-defence / sam-site.
  * the proven ground orders - PlatoonDefend / PlatoonAttack on the platoon entity (pc-defend-house), MoveToLocation + BasicVehicleSop for
    the vehicles (mf-at-vs-vehicle), 2006 AT gunners (the city-battle roadblock's AT team).
Terrain: "Stechovice" (assets/terrain-input/Stechovice, imported on first use). Every place is read against that INPUT set (heights, water,
buildings) and checked below; absolute z comes from the 30 m DEM (aircraft are given an absolute z: ground + AGL).
"""
import json
import math
import os
import struct

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
OUT = os.path.join(ROOT, 'scenarios', 'showcases', 'stechovice-battle', 'scenario.json')
INPUT = os.path.join(ROOT, 'assets', 'terrain-input', 'Stechovice')
MACROS = os.path.join(ROOT, 'macros', 'city-battle')
F, H, N = 'Friend', 'Hostile', 'Neutral'
# ⚠ MEASURED (GET /terrain/levels on the loaded terrain): the scenario frame of an imported terrain has its ORIGIN AT THE SOUTH-WEST CORNER
# (terrain.json frame.corner), so scenario (x, y) = input-set local (the manifest's bounds -2021..2045 / -2128..2176) + (2021, 2128).
# Everything in this script is written in INPUT-SET local metres (what the heights / water / buildings files use); W() converts at the edge.
OX, OY = 2021.0, 2128.0


def W(x, y):
    return x + OX, y + OY


# -- the terrain input: heights / water / buildings (to check every placement) ------------------------------------------------------

class Ground:
    def __init__(self):
        meta = json.load(open(os.path.join(INPUT, 'heights', 'heights.json')))
        self.cols, self.rows, self.cs = meta['cols'], meta['rows'], meta['cellSize']
        self.ox, self.oy = meta['originX'], meta['originY']
        raw = open(os.path.join(INPUT, 'heights', 'heights.f32'), 'rb').read()
        self.h = struct.unpack('<%df' % (self.cols * self.rows), raw)
        self.water = []
        for f in json.load(open(os.path.join(INPUT, 'water', 'water.geojson')))['features']:
            g = f['geometry']
            for p in ([g['coordinates']] if g['type'] == 'Polygon' else g['coordinates']):
                self.water.append(p[0])
        self.houses = []
        for f in json.load(open(os.path.join(INPUT, 'buildings', 'buildings.geojson')))['features']:
            g = f['geometry']
            self.houses.append(g['coordinates'][0] if g['type'] == 'Polygon' else g['coordinates'][0][0])

    def z(self, x, y):
        fx, fy = (x - self.ox) / self.cs, (y - self.oy) / self.cs
        i = max(0, min(self.cols - 2, int(math.floor(fx))))
        j = max(0, min(self.rows - 2, int(math.floor(fy))))
        tx, ty = fx - i, fy - j

        def g(a, b):
            return self.h[(self.rows - 1 - b) * self.cols + a]
        return (g(i, j) * (1 - tx) * (1 - ty) + g(i + 1, j) * tx * (1 - ty) + g(i, j + 1) * (1 - tx) * ty + g(i + 1, j + 1) * tx * ty)

    def slope(self, x, y, d=20.0):
        return math.degrees(math.atan2(math.hypot(self.z(x + d, y) - self.z(x - d, y), self.z(x, y + d) - self.z(x, y - d)), 2 * d))

    @staticmethod
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

    def in_water(self, x, y):
        return any(self.inside((x, y), p) for p in self.water)

    def in_house(self, x, y):
        for p in self.houses:
            xs = [q[0] for q in p]
            ys = [q[1] for q in p]
            if min(xs) <= x <= max(xs) and min(ys) <= y <= max(ys) and self.inside((x, y), p):
                return True
        return False


GROUND = Ground()


class Roads:
    """The SUMO net's drivable lanes (roads/roads.net.xml, the same input-set local metres): where a vehicle starts ON a road, facing along it."""
    DRIVABLE = ('highway.motorway', 'highway.trunk', 'highway.primary', 'highway.secondary', 'highway.tertiary', 'highway.unclassified',
                'highway.residential', 'highway.service', 'highway.living_street')   # ⛔ no track: SUMO closes it to cars and the road router cannot route from it (live: FailedUnreachable)

    def __init__(self):
        import xml.etree.ElementTree as ET
        self.lanes = []   # [(edge id, [(x, y)...])]
        for e in ET.parse(os.path.join(INPUT, 'roads', 'roads.net.xml')).getroot().findall('edge'):
            if e.get('function') == 'internal' or (e.get('type') or '').split('|')[0] not in self.DRIVABLE:
                continue
            lane = e.find('lane')
            self.lanes.append((e.get('id'), [tuple(map(float, q.split(',')[:2])) for q in lane.get('shape').split()]))

    def snap(self, x, y):
        """The nearest point on a drivable lane to (x, y), and the lane's compass bearing there."""
        best = None
        for _, pts in self.lanes:
            for (ax, ay), (bx, by) in zip(pts, pts[1:]):
                dx, dy = bx - ax, by - ay
                L2 = dx * dx + dy * dy
                if L2 < 1e-6:
                    continue
                t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / L2))
                px, py = ax + t * dx, ay + t * dy
                d = math.hypot(x - px, y - py)
                if best is None or d < best[0]:
                    best = (d, px, py, math.degrees(math.atan2(dx, dy)) % 360)
        return best[1], best[2], best[3], best[0]


ROADS = Roads()


def on_road(x, y, toward=None):
    """(x, y) snapped onto the nearest drivable road, and a bearing along it — the direction closer to `toward` (a bearing), if given."""
    rx, ry, b, d = ROADS.snap(x, y)
    if toward is not None and abs((b - toward + 180) % 360 - 180) > 90:
        b = (b + 180) % 360
    return rx, ry, b


# -- scenario building blocks ------------------------------------------------------------------------------------------------------

def quat(bearing):
    """Facing a compass bearing (0 = north, clockwise): a rotation about z by 90 - bearing — the engine's yaw 0 is +X (east), counter-clockwise
    (thumbnails.py heading_deg, measured on the Rifleman). ⛔ Was -bearing: every unit faced 90° off (the village tank, told "west", spawned
    facing NORTH, spent 4 s turning round and the watchdog's no-progress rule failed its move — CE-5048)."""
    a = math.radians(90 - bearing) / 2
    return [0, 0, round(math.sin(a), 7), round(math.cos(a), 7)]


class Scn:
    def __init__(self):
        self.ents = {}
        self.n = 0
        self.placed = []   # (name, side, x, y, z, slope, water, house) - the placement table

    def gid(self, tag=0x1000):
        self.n += 1
        return '57ec%04x-0000-4000-8000-%012d' % (tag, self.n)

    def add(self, e, tag=0x1000, check=True):
        g = self.gid(tag)
        self.ents[g] = e
        if check and 'SimTransform' in e:
            x, y, z = e['SimTransform']['Position']
            self.placed.append((e['EntityInfo']['Name'], x - OX, y - OY, z))
        return g


def unit(tkb, name, side, x, y, bearing=0, hp=100, air_agl=None, extra=None):
    z = GROUND.z(x, y)
    if air_agl:
        z += air_agl
    x, y = W(x, y)
    e = {'SimTransform': {'Position': [round(x, 2), round(y, 2), round(z, 1)], 'Rotation': quat(bearing)},
         'TkbIdentity': {'TkbType': tkb}, 'EntityInfo': {'Name': name, 'ForceId': side}}
    if hp:
        e['Health'] = {'Current': hp, 'Max': hp}
        e['TargetMemory'] = {'Entries': []}
    if extra:
        e.update(extra)
    return e


def behaviour(name, params):
    return {'Name': name, 'Params': params, 'Origin': 'Superior'}


def sop(name):
    return {'Name': name, 'Params': {}, 'Origin': 'Superior'}


def squad_no(name):
    """1..6 for 'Squad N ...' (a squad or one of its men), else 0 (the platoon)."""
    return int(name.split()[1]) if name.startswith('Squad ') else 0


def playable(name, side):
    """⭐ Player Join D-9 (docs/DESIGN_Player_Join.md) — every soldier is a slot a Player may take (user, 2026-11-24: "make all the soldiers playable");
    a free one runs as AI until taken. The slot name is the unit's name shortened to FixedString32 (31 UTF-8 bytes)."""
    slot = name.replace('Riverbank ', 'River ').replace('Squad ', 'Sq')
    assert len(slot.encode('utf-8')) <= 31, slot
    return {'Side': side, 'SlotName': slot}


def platoon(s, macro, prefix, side, tag, x, y, bearing, behav, commander=None, squad_at=None):
    """A city-battle platoon kit (3 of its 6 squads) with its centre at (x, y), the line facing `bearing`. Returns the platoon's guid.
    squad_at: optional [(x, y) x3] - where each squad stands instead of the line (houses: a squad per free spot between them)."""
    doc = json.load(open(os.path.join(MACROS, macro, 'scenario.json')))
    text_prefix = next(iter(doc['entities'])).split('-')[0]          # the macro's own guid prefix (c6009a00 / c6009b00)
    fx, fy = math.sin(math.radians(bearing)), math.cos(math.radians(bearing))      # forward
    rx, ry = fx and math.cos(math.radians(bearing)), -math.sin(math.radians(bearing))  # right
    rx, ry = math.cos(math.radians(bearing)), -math.sin(math.radians(bearing))
    lead = None
    doc_squad_origin = {squad_no(e0['EntityInfo']['Name']): e0['SimTransform']['Position'][:2] for e0 in doc['entities'].values()
                        if e0['TkbIdentity']['TkbType'] == 302}
    for g0, e0 in doc['entities'].items():
        name0 = e0['EntityInfo']['Name']
        if any(name0.startswith('Squad %d' % k) for k in (4, 5, 6)):
            continue
        e = json.loads(json.dumps(e0).replace(text_prefix, prefix))
        e.pop('ParamBindings', None)
        g = g0.replace(text_prefix, prefix)
        lx, ly = e0['SimTransform']['Position'][0], e0['SimTransform']['Position'][1]
        px, py = x + lx * rx + ly * fx, y + lx * ry + ly * fy    # macro local x = across the line, y = ahead of it
        sq = squad_no(name0)
        if squad_at and sq:                                      # a squad (and its men) moved to its own spot, keeping its shape
            sl = doc_squad_origin[sq]
            lx2, ly2 = lx - sl[0], ly - sl[1]
            px, py = squad_at[sq - 1][0] + lx2 * rx + ly2 * fx, squad_at[sq - 1][1] + lx2 * ry + ly2 * fy
        wx, wy = W(px, py)
        e['SimTransform'] = {'Position': [round(wx, 2), round(wy, 2), round(GROUND.z(px, py), 1)], 'Rotation': quat(bearing)}
        e['EntityInfo'] = {'Name': tag if e0['TkbIdentity']['TkbType'] == 305 else tag.replace(' Platoon', '') + ' ' + name0, 'ForceId': side}
        if e0['TkbIdentity']['TkbType'] not in (302, 305):      # a MAN (not the squad / platoon record): a Player may deploy into him
            e['Playable'] = playable(e['EntityInfo']['Name'], side)
        if e0['TkbIdentity']['TkbType'] == 305:
            e['RefKey'] = {'Value': tag.replace(' ', '')}
            lead = g
            if behav:
                e['Behavior'] = behav
            if commander:
                e['UnitSubordinate'] = {'CommanderGuid': commander, 'Designation': 'Wingman'}
        s.ents[g] = e
        s.placed.append((e['EntityInfo']['Name'], px, py, e['SimTransform']['Position'][2]))
    return lead


# -- THE PLACES - every coordinate of the show, named by role (terrain-local metres, x east, y north) ---------------------------------
RIVER_Y = -560.0                                  # the riverbank battle's centre line (the Vltava runs north-south; water x -1245..-1110 here)
# ⭐ chosen by a line-of-sight search over the DEM + the woods polygons (all 9 Blue-squad x Red-squad pairs see each other, ~200 m across the water):
#   on the west bank the ground beyond x -1300 is a wooded cliff (nobody sees out of it), so Red holds the low west terrace, not the plateau.
PLACES = {
    'river-blue': (-1054.0, RIVER_Y),             # Blue platoon, the east bank terrace (~204 m, slope < 8 deg)
    'river-blue-ifv': (-1038.0, RIVER_Y - 38),    # Blue IFV in support
    # ⭐ 44 m south of the first pick: measured with the live engine's sight (/terrain/query — buildings included, which the DEM + woods search
    #   below cannot see): its ±20 m spread sees 8 of 9 lines to the Blue spread; the first pick sat behind house walls (2 of 3 posts blind)
    'river-red': (-1256.0, RIVER_Y - 44),         # Red platoon, the west terrace
    'river-red-technical': (-1243.0, RIVER_Y - 58),
    'zu23': (-260.0, 290.0),                      # Red ZU-23 on the hill north of the village (~330 m, open ground)
    'village-red': (-335.0, 140.0),               # Red platoon holding the houses of the village centre
    'village-at': (-225.0, 150.0),                # the anti-tank team (east edge of the village, covering the attack road)
    'village-red-technical': (-120.0, 110.0),    # on the village's E-W road
    'sam': (-310.0, 300.0),                       # Red radar SAM on the same hill (~328 m)
    'village-blue': (230.0, 150.0),               # Blue platoon's start, east of the village
    'village-blue-tank': (352.0, 57.0), 'village-blue-ifv': (340.0, 51.0),   # on the road (the column's head; the rest queue east of it)
    'village-objective': (-325.0, 135.0),         # where Blue's PlatoonAttack goes
    # ⭐ CE-4694 — the BRIDGE and the Blue BRIDGEHEAD (user, 2026-11-25: "blue holds bridgehead"). The bridge spans the river on y -538 (scenario 1590) from the west-bank
    #   road (x -1259) to the east terrace (x -1093), 8 m wide; its west end lands on the west-bank secondary road (a ramp, the road flat across since CE-4694 ②)
    'bridge-west': (-1259.0, -538.0), 'bridge-east': (-1093.0, -538.0),
    'bridgehead': (-1266.0, -540.0),              # the bridgehead's centre: the west-bank road at the bridge's west end
    'bridgehead-tank': (-1268.0, -525.0),         # where Blue's 2nd riverbank tank crosses to (the west road, north of the bridge's foot)
    'village-stop': (-110.0, 113.0),              # where the tank / IFV stop (on the road) to support by fire
    'blue-hq': (420.0, 40.0),
    'apache': (520.0, 330.0), 'a10-start': (-1900.0, -300.0),
}
AGL_APACHE, AGL_A10 = 60.0, 450.0


def P(role, dx=0.0, dy=0.0):
    x, y = PLACES[role]
    return x + dx, y + dy


def build():
    s = Scn()
    # ---- Blue chain of command: HQ (SupportCommand) over both platoons, the Apache and the A-10 -----------------------------------
    hq = s.add(unit(305, 'Blue HQ', F, *P('blue-hq'), extra={'Behavior': behaviour('SupportCommand', {})}), check=False)
    sub = lambda: {'UnitSubordinate': {'CommanderGuid': hq, 'Designation': 'Wingman'}}

    # ---- RIVERBANK: Blue (east bank) vs Red (west bank), across the Vltava ----------------------------------------------------------
    bx, by = P('river-blue')
    platoon(s, 'rifle-platoon', '57ec0b01', F, 'Riverbank Blue Platoon', bx, by, 270,
            behaviour('PlatoonDefend', {'Position': [*W(bx, by), 0], 'Left': 225, 'Right': 315, 'MaxRange': 420, 'SearchRadius': 60, 'Speed': 2}), hq)
    s.add(unit(101, 'Riverbank Blue IFV', F, *P('river-blue-ifv'), bearing=270, hp=500, extra={'Sop': sop('BasicVehicleSop')}))
    # ⭐ CE-4694 — the BRIDGE (an authored Bridge feature: deck + ramps, collapsible as a building) and the Blue BRIDGEHEAD on the west bank: a second Blue platoon dug in
    #   along the west-bank road at the bridge's west end, facing south and west (PlatoonDefend); the east-bank platoon, the IFV and the tanks support it by fire
    wx_, wy_ = P('bridge-west'); ex_, ey_ = P('bridge-east')
    s.add({'TkbIdentity': {'TkbType': 8818}, 'EntityInfo': {'Name': 'Bridge', 'ForceId': N},
           'TerrainFeature': {'Kind': 10, 'Template': '', 'HeadingDeg': 0, 'Pad': 8, 'Seed': 0},
           'SimTransform': {'Position': [*W(wx_, wy_), 0], 'Rotation': [0, 0, 0, 1]},
           'EditablePolyline': {'Points': [{'X': 0, 'Y': 0}, {'X': round(ex_ - wx_, 2), 'Y': round(ey_ - wy_, 2)}], 'Version': 0}}, check=False)
    hx_, hy_ = P('bridgehead')
    bridgehead = platoon(s, 'rifle-platoon', '57ec0b03', F, 'Bridgehead Platoon', hx_, hy_, 225,
                         behaviour('PlatoonDefend', {'Position': [*W(hx_, hy_), 0], 'Left': 180, 'Right': 300, 'MaxRange': 300, 'SearchRadius': 25, 'Speed': 2}), hq,
                         squad_at=[(-1268.0, -520.0), (-1266.0, -541.0), (-1262.0, -562.0)])
    s.add({'TkbIdentity': {'TkbType': 8860}, 'EntityInfo': {'Name': 'Bridgehead held', 'ForceId': F},
           'Behavior': behaviour('HoldArea', {'common': {'title': 'Bridgehead held', 'subject': bridgehead},
                                              'area': {'circle': {'center': [*W(hx_, hy_), 0], 'radius': 35}}, 'minMen': 2, 'maxEnemy': 0, 'holdSeconds': 60})}, check=False)
    # Red COUNTER-ATTACKS the bridgehead from its terrace (65 m south of it), to take or deny the bridge
    rx_, ry_ = P('river-red')
    platoon(s, 'militia-platoon', '57ec0a01', H, 'Riverbank Red Platoon', rx_, ry_, 0,
            behaviour('PlatoonAttack', {'Position': [*W(hx_, hy_), 0], 'SearchRadius': 40, 'Preset': 'AttackPosition', 'Speed': 3}))
    vehicle_late = ('Riverbank Red Technical', -1262.0, -680.0)   # ⭐ CE-4694 — placed with the other riverbank vehicles below (on the west road)

    # ---- VILLAGE: Blue attacks from the east, Red defends the houses ---------------------------------------------------------------
    vx, vy = P('village-red')
    platoon(s, 'militia-platoon', '57ec0a02', H, 'Village Red Platoon', vx, vy, 90,
            behaviour('PlatoonDefend', {'Position': [*W(vx, vy), 0], 'Left': 45, 'Right': 135, 'MaxRange': 300, 'SearchRadius': 60, 'Speed': 2}),
            squad_at=[(-336, 138), (-318, 162), (-354, 162)])
    for j in range(2):
        ax, ay = P('village-at', 3 * j, 0)
        s.add(unit(2006, 'Village Red AT Gunner %d' % (j + 1), H, ax, ay, bearing=90,
                   extra={'Playable': playable('Village Red AT Gunner %d' % (j + 1), H)}))
    technical = s.add(unit(104, 'Village Red Technical', H, *P('village-red-technical'), bearing=90, hp=500, extra={'Sop': sop('BasicVehicleSop')}))
    zu = s.add(unit(110, 'Red ZU-23', H, *P('zu23'), bearing=90, hp=500, extra={'Sop': sop('BasicVehicleSop')}))
    s.add(unit(114, 'Red Radar SAM', H, *P('sam'), hp=400, extra={'Sop': sop('BasicVehicleSop')}))
    ux, uy = P('village-blue')
    platoon(s, 'rifle-platoon', '57ec0b02', F, 'Village Blue Platoon', ux, uy, 270,
            behaviour('PlatoonAttack', {'Position': [*W(*P('village-objective')), 0], 'SearchRadius': 60, 'Preset': 'AttackPosition', 'Speed': 3}), hq)
    sx, sy = P('village-stop')
    s.add(unit(100, 'Village Blue Tank', F, *P('village-blue-tank'), bearing=270, hp=3000, extra={
        'Sop': sop('BasicVehicleSop'), 'Behavior': behaviour('MoveToLocation', {'speed': 6, 'arrivalRadius': 5, 'target': [*W(sx, sy), 0]})}))
    s.add(unit(101, 'Village Blue IFV', F, *P('village-blue-ifv'), bearing=270, hp=500, extra={
        'Sop': sop('BasicVehicleSop'), 'Behavior': behaviour('MoveToLocation', {'speed': 6, 'arrivalRadius': 5, 'target': [*W(sx + 12, sy - 7), 0]})}))

    # ---- MORE ARMOUR (user, 2026-11-23: "almost no vehicles ... need more vehicles") — every vehicle starts ON a road (snapped to the SUMO
    #      net), facing along it; Blue's village column queues east of its lead tank and drives to the support-by-fire line, Red's hold
    def vehicle(tkb, name, side, x, y, toward, hp, behav=None):
        rx2, ry2, b = on_road(x, y, toward)
        extra = {'Sop': sop('BasicVehicleSop')}
        if behav:
            extra['Behavior'] = behav
        return s.add(unit(tkb, name, side, rx2, ry2, bearing=b, hp=hp, extra=extra))

    def move_to(x, y):
        return behaviour('MoveToLocation', {'speed': 6, 'arrivalRadius': 5, 'target': [*W(x, y), 0]})
    # village, Blue: two more M1s, a second Bradley and a HMMWV behind the lead pair (the column's order = its arrival order)
    #   ⭐ on the residential road the lead tank takes into the village (the track it starts on is closed to cars in SUMO; a start snapped onto it
    #   was FailedUnreachable live), ~15 m apart, facing north-west along it
    for name, tkb, hp, at, stop in (('Village Blue Tank 2', 100, 3000, (300, 45), (-95, 100)),
                                    ('Village Blue Tank 3', 100, 3000, (285, 58), (-125, 118)),
                                    ('Village Blue IFV 2', 101, 500, (270, 70), (-80, 95)),
                                    ('Village Blue HMMWV', 102, 300, (255, 80), (-60, 88))):
        vehicle(tkb, name, F, *at, 300, hp, move_to(*stop))
    # village, Red: two T-72s on the village's E-W road facing the attack, an ATGM technical on the south road, a DShK technical south-east
    vehicle(103, 'Village Red T-72 1', H, -250, 146, 90, 2500)
    vehicle(103, 'Village Red T-72 2', H, -300, 160, 90, 2500)
    vehicle(105, 'Village Red ATGM Technical', H, -260, 75, 90, 500)
    vehicle(108, 'Village Red DShK Technical', H, -190, 50, 90, 500)
    # riverbank: a tank each side, on the bank roads
    vehicle(100, 'Riverbank Blue Tank', F, -1063, -580, 270, 3000)
    vehicle(103, 'Riverbank Red T-72', H, -1262, -760, 0, 2500)   # ⭐ CE-4694 — ~220 m south of the bridgehead on the west road: support by fire

    # ---- STILL MORE (user, 2026-11-24: "more armored vehicles ... more technical vehicles with machine gun on both sides") ----------------------
    # village, Blue: a 4th M1, a 3rd Bradley, a 2nd HMMWV (M2) and two HMG technicals, further back on the same road, all to the support line
    for name, tkb, hp, at, stop in (('Village Blue Tank 4', 100, 3000, (240, 92), (-140, 125)),
                                    ('Village Blue IFV 3', 101, 500, (225, 104), (-70, 110)),
                                    ('Village Blue HMG Technical 1', 104, 500, (210, 116), (-50, 100)),
                                    ('Village Blue HMG Technical 2', 104, 500, (196, 128), (-45, 80)),
                                    ('Village Blue HMMWV 2', 102, 300, (318, 38), (-65, 75))):
        vehicle(tkb, name, F, *at, 300, hp, move_to(*stop))
    # village, Red: a 3rd T-72, two HMG technicals and a 2nd DShK, holding the village roads
    vehicle(103, 'Village Red T-72 3', H, -220, 138, 90, 2500)
    vehicle(104, 'Village Red HMG Technical 1', H, -320, 166, 90, 500)
    vehicle(104, 'Village Red HMG Technical 2', H, -360, 60, 90, 500)
    vehicle(108, 'Village Red DShK Technical 2', H, -150, 40, 90, 500)
    # riverbank: a 2nd tank each side and MG technicals on both banks
    vehicle(100, 'Riverbank Blue Tank 2', F, -1070, -510, 270, 3000, move_to(*P('bridgehead-tank')))   # ⭐ CE-4694 — crosses the bridge to the bridgehead
    vehicle(104, 'Riverbank Blue HMG Technical', F, -1050, -600, 270, 500)
    vehicle(102, 'Riverbank Blue HMMWV', F, -1045, -630, 270, 300)
    # ⭐ CE-4694 — Red's riverbank vehicles support the counter-attack by fire from the west road 150-250 m SOUTH of the bridgehead (📐 70-100 m away, down the
    #   same open road, they killed 17 of the bridgehead's 18 men in 150 s — the user's story is that Blue HOLDS the bridgehead)
    vehicle(103, 'Riverbank Red T-72 2', H, -1262, -790, 0, 2500)
    vehicle(104, 'Riverbank Red HMG Technical', H, -1262, -700, 0, 500)
    vehicle(104, vehicle_late[0], H, vehicle_late[1], vehicle_late[2], 0, 500)
    vehicle(108, 'Riverbank Red DShK Technical', H, -1262, -730, 0, 500)

    # ---- TRAFFIC (user: "there are no sumo cars whatsoever") — SUMO background traffic on the terrain's road net (the same demand entity as
    #      demo-city's convoys, cv-traffic-fire); a car near the fighting is abandoned by its driver (CE-1959)
    s.add({'TkbIdentity': {'TkbType': 8850}, 'TrafficDemand': {'TargetCars': 250, 'Seed': 1, 'StepSeconds': 0.5, 'TargetPedestrians': 60,
                                                               'CarSpawnPerStep': 25}}, check=False)

    # ---- AIR: a Blue Apache rocket-runs the village technical inside the SAM's cover, a Blue A-10 strikes the hill ZU-23 ----------
    ax_, ay_ = P('apache')
    s.add(unit(406, 'Blue Apache', F, ax_, ay_, bearing=270, hp=800, air_agl=AGL_APACHE,
               extra={**sub(), 'Behavior': behaviour('AirPlatformAttack', {'Target': technical})}), check=False)
    zx, zy = P('zu23')
    s.add(unit(405, 'Blue A-10', F, *P('a10-start'), bearing=90, hp=1500, air_agl=AGL_A10,
               extra={**sub(), 'Behavior': behaviour('AirPlatformStrike', {'Location': [*W(zx, zy), 0], 'Target': zu})}), check=False)

    # ---- AIR COMBAT (user, 2026-11-24: "I would like some air combat") — the proven 2v2 dogfight (scenarios/demos/air/ac-dogfight-2v2): two Blue
    #      F-16s from the east, two Red MiG-29s from the west, in an arena above the valley; all four start inside it and merge. A close fight (MaxShotRangeM 2.5 km, as ac-dogfight-2v2) so it stays over the map, where it can be seen.
    #      ⚠ RadiusM 4000 (the demos'): a jet belongs to the arena within 1.5 × the radius (AirArenaSystem.IndexContaining) and is pulled back only while it
    #      does — 📐 with 2000 the merge ran the jets 3.8 km out (220 m/s), out of the arena, and nothing turned them back
    cz = GROUND.z(0, 0)
    s.add({'SimTransform': {'Position': [*W(0, 0), round(cz, 1)], 'Rotation': [0, 0, 0, 1]}, 'TkbIdentity': {'TkbType': 8840},
           'EntityInfo': {'Name': 'Air Arena', 'ForceId': N},
           'AirArena': {'Centre': [*W(0, 0), round(cz, 1)], 'RadiusM': 4000, 'FloorAglM': 300, 'CeilingAglM': 5000, 'MaxShotRangeM': 2500,
                        'StalemateSeconds': 90, 'LapsesOff': 0}}, check=False)
    # ⭐ each pilot on a NAMED bandit (the ac-dogfight-2v2 pattern: both pairs merge through the centre). ⛔ Not the CAP's AirArmedSearch —
    #   📐 live, F-16 2's search ran it straight south-west from the start and its fight with MiG 2 went 4–6 km south of the map, up to 10 km high
    gids = [s.gid() for _ in range(4)]   # F-16 1, F-16 2, MiG 1, MiG 2 — reserved first so each can name its bandit
    for k, dy in ((1, 400), (2, -400)):
        s.ents[gids[k - 1]] = unit(401, 'Blue F-16 %d' % k, F, 1800, dy, bearing=270, hp=400, air_agl=1500, extra={
            'SimVelocity': {'Linear': [-220.0, 0.0, 0]}, 'AirCombatState': {'Skill': 'Veteran'},
            'Behavior': behaviour('AirPlatformAttack', {'Target': gids[k + 1]})})
        s.ents[gids[k + 1]] = unit(409, 'Red MiG-29 %d' % k, H, -1800, -dy, bearing=90, hp=450, air_agl=1500, extra={
            'SimVelocity': {'Linear': [220.0, 0.0, 0]}, 'AirCombatState': {'Skill': 'Veteran'},
            'Behavior': behaviour('AirPlatformAttack', {'Target': gids[k - 1]})})

    header = {
        'subsystemType': 'Hrot.Scenario', 'schemaVersion': '1.0', 'terrainName': 'Stechovice',
        'description': (
            'What: a SHOW on the real OSM terrain Stechovice (Vltava valley + village, imported on first use) - two ground battles and an air war. '
            'Scenario coordinates (the terrain south-west corner is 0,0). RIVERBANK (y about 1570): a Blue platoon (x about 970) (leader + 3 squads, PlatoonDefend) dug in on the flat east bank with an IFV, two M1s, an HMG technical and a HMMWV fires across the river at a Red platoon '
            '(leader + 3 squads, x about 765) with three technicals (HMG, DShK) and two T-72s on the low west terrace, ~200 m across the water (the plateau behind it is a wooded cliff nobody sees out of). '
            'VILLAGE (centre x about 1690, y about 2265): a Blue platoon (leader + 3 squads, PlatoonAttack) with four M1 tanks, three Bradleys, two HMMWVs and two HMG technicals advances from the east (x about 2250-2370) into the village; '
            'a Red platoon (leader + 3 squads, PlatoonDefend) holds the houses with an AT team (2 gunners), three T-72s and six technicals (3 HMG, ATGM, 2 DShK), and a Red radar SAM and a ZU-23 sit on the hill to the north (~330 m). '
            'AIR: Blue HQ (SupportCommand) owns both platoons, a Blue Apache that attacks the village technical inside the SAM cover and a Blue A-10 that strikes the hill ZU-23 '
            'flying in from the west over the river. AIR COMBAT: two Blue F-16s from the east and two Red MiG-29s from the west, each pilot on a named bandit, merge over the valley (an arena 4 km round the centre). Every soldier is Playable (a free one runs as AI until a Player takes him). SUMO background traffic (250 cars, 60 pedestrians) runs on the terrain roads. Chain of command Blue: HQ > platoon > squad > man; Red: platoon > squad > man. Behaviours as authored, nothing scripted. '
            'Judge (checks.json): both battles trade fire and cost both sides casualties; the Blue attackers reach the village; the Apache flies over the village and the A-10 over the river; '
            'the Red air defence (ZU-23, SAM) fires at them. Generated by scripts/stechovice_battle_scenario.py; first run imports the terrain (minutes), then caches it.'),
        'world': {
            'ScenarioClock': {'Start': '2025-06-21T10:00:00+02:00'},
            'WeatherConditions': {'GroundTemperatureC': 15, 'LapseRateKPerKm': 6.5, 'SeaLevelPressureHpa': 1013.25, 'RelativeHumidity': 0.8,
                                  'WindCount': 0, 'CloudCount': 1, 'VisibilityM': 5000, 'FogTopM': 0, 'FogVisibilityM': 5000, 'Precipitation': 0,
                                  'PrecipitationMmPerHour': 0, 'FeatherM': 0, 'WindAltitudeM': [0] * 8, 'WindFromDeg': [0] * 8,
                                  'WindSpeedMps': [0] * 8, 'WindGustMps': [0] * 8, 'CloudBaseM': [1500, 0, 0, 0],
                                  'CloudThicknessM': [600, 0, 0, 0], 'CloudOctas': [3, 0, 0, 0], 'CloudKind': [1, 0, 0, 0]}},
    }
    return {'$meta': {'docType': 'Hrot.Scenario', 'schemaVersion': 1}, 'header': header, 'entities': s.ents}, s


if __name__ == '__main__':
    doc, s = build()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w') as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print('%s: %d entities' % (OUT, len(doc['entities'])))
    bad = 0
    for name, x, y, z in s.placed:
        if z > 700:
            continue
        sl, w, hs = GROUND.slope(x, y), GROUND.in_water(x, y), GROUND.in_house(x, y)
        flag = ('WATER ' if w else '') + ('HOUSE ' if hs else '') + ('STEEP ' if sl > 25 else '')
        if flag:
            bad += 1
            print('  CHECK %-34s (%8.1f,%8.1f) z %6.1f slope %4.1f  %s' % (name, x, y, z, sl, flag))
    print('placement check: %d of %d placements flagged (water / inside a building / slope > 25 deg)' % (bad, len(s.placed)))
