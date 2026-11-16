#!/usr/bin/env python3
"""The stochastic-unit FEATURE-TEST scenarios (R-4277, design DESIGN_Stochastic_Units_And_Resolution.md §23.6): the nine of ST-0 ... ST-2.
Writes scenarios/demos/aggregates/<name>/{scenario.json,checks.json}; golden.json is CAPTURED (scripts/scenario-regression.sh's runner, --verify-capture).
Re-runnable: `python3 scripts/gen-agg-scenarios.py [name ...]`.

Units are authored with ONLY `AggregateUnit { Resolution: Stochastic }` — agg-backend's spawn-time fill (CE-8016, StochasticUnitFillSystem) gives each its slot table
and holdings from its TKB composite (305 platoon = 30 men, 302 squad = 4, 306 company, 301 tank platoon = tracked holdings). Enums are written by NAME.
"""
import copy, json, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenario_json_readable import readable

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "scenarios", "demos", "aggregates")
BLUE, RED = "Friend", "Hostile"
COMPANY, PLATOON, SQUAD, TANK_PLATOON, ARTILLERY_BATTERY = 306, 305, 302, 301, 304
MAN_TYPES = [2002, 207, 212, 2002]     # a squad's leader and three men, as cc-battalion authors them


class Scn:
    def __init__(self, sid, prefix):
        self.sid, self.prefix, self.n, self.ents = sid, prefix, 0, {}

    def guid(self):
        self.n += 1
        return f"{self.prefix}-0000-4000-8000-{self.n:012d}"

    def unit(self, name, force, at, tkb, commander=None, designation="Wingman", behavior=None, aggregate=None, extra=None, facing=None):
        g = self.guid()
        e = {"SimTransform": {"Position": [at[0], at[1], 0], "Rotation": [0, 0, 0, 1] if facing is None else facing},
             "TkbIdentity": {"TkbType": tkb}, "EntityInfo": {"Name": name, "ForceId": force}}
        if aggregate is not None:
            e["AggregateUnit"] = dict({"Resolution": "Stochastic"}, **aggregate)
        if commander:
            e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": designation}
        if behavior:
            e["Behavior"] = {"Name": behavior[0], "Params": behavior[1], "Origin": "Superior"}
        if extra:
            e.update(extra)
        self.ents[g] = e
        return g

    def physical_squad(self, name, force, at, commander):
        sq = self.unit(name, force, at, SQUAD, commander)
        for i, t in enumerate(MAN_TYPES):
            g = self.guid()
            self.ents[g] = {"SimTransform": {"Position": [at[0] - 3 + 2 * i, at[1] - 2, 0], "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": t},
                            "EntityInfo": {"Name": f"{name} {'Leader' if i == 0 else 'Man %d' % i}", "ForceId": force}, "Health": {"Current": 100, "Max": 100},
                            "TargetMemory": {"Entries": []},
                            "UnitSubordinate": {"CommanderGuid": sq, "Designation": "SquadLeader" if i == 0 else "Wingman"}}
        return sq

    def physical_platoon(self, name, force, at, commander, squads=2):
        pl = self.unit(name, force, at, PLATOON, commander)
        for i in range(squads):
            self.physical_squad(f"{name} Squad {i + 1}", force, (at[0] + 14 * i, at[1] - 14), pl)
        return pl

    def objective(self, name, force, kind, params):
        return self.unit_raw(name, force, 8860, (kind, params))

    def trigger(self, name, force, params):
        return self.unit_raw(name, force, 8861, ("Trigger", params))

    def unit_raw(self, name, force, tkb, behavior):
        g = self.guid()
        self.ents[g] = {"TkbIdentity": {"TkbType": tkb}, "EntityInfo": {"Name": name, "ForceId": force},
                        "Behavior": {"Name": behavior[0], "Params": behavior[1], "Origin": "Superior"}}
        return g


def write(s, terrain, description, checks):
    d = os.path.join(OUT, s.sid)
    os.makedirs(d, exist_ok=True)
    doc = {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
           "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": terrain, "description": description},
           "entities": s.ents}
    with open(os.path.join(d, "scenario.json"), "w") as f:
        f.write(readable(json.dumps(doc, indent=2)) + "\n")
    with open(os.path.join(d, "checks.json"), "w") as f:
        json.dump(checks, f, indent=2)
        f.write("\n")
    print("wrote", s.sid, len(s.ents), "entities")


def field(name, who, path, op, value, **kw):
    return dict({"name": name, "on": "field", "who": who, "path": path, "op": op, "value": value}, **kw)


def task(name, who, is_, **kw):
    return dict({"name": name, "on": "task", "who": who, "is": is_}, **kw)


def never(name, cue, **kw):
    cue = dict(cue); cue.pop("name", None)
    return dict({"name": name, "never": cue}, **kw)


def pt(p):
    return [p[0], p[1], 0]


# ───────────────────────────────────────────── ST-0 ─────────────────────────────────────────────

def agg_platoon_under_company():
    s = Scn("agg-platoon-under-company", "a9000001")
    co = s.unit("Blue Company", BLUE, (240, 405), COMPANY,
                behavior=("CompanyDefend", {"Position": pt((300, 400)), "Threat": pt((700, 400)), "Frontage": 160.0, "Depth": 220.0}))
    s.unit("Blue Stochastic Platoon", BLUE, (290, 440), PLATOON, co, aggregate={})
    s.objective("Blue holds the line", BLUE, "HoldArea", {
        "common": {"title": "Blue holds the line", "subject": co},
        "area": {"circle": {"center": pt((300, 400)), "radius": 150}}, "minMen": 20, "maxEnemy": 0, "holdSeconds": 20})
    desc = ("What: a physical 'Blue Company' (CompanyDefend at (300,400), facing east) over ONE subordinate, a STOCHASTIC 'Blue Stochastic Platoon' "
            "(AggregateUnit Stochastic, no members - its slot table is filled from the TKB composite: 30 men), on demo-city. A HoldArea objective (radius 150 m around the position, 20 men) "
            "is subject to the company. Judge: (1) the company's SITREP shows the stochastic platoon Green; (2) the objective's census counts the stochastic platoon's men "
            "(there is no other man in the area, so 20 can only be met by the 30 slot-table men); (3) the company's Defend order reaches the stochastic platoon as the generic play "
            "(StochasticUnitDefend), not PlatoonDefend. Fail: the platoon reads Unknown / is not counted / is given a physical play. Check: scripts/scenario-regression.sh agg-platoon-under-company")
    checks = {"$doc": "§23.6 ST-0. The platoon is the company's only subordinate, so its picture row is Subordinates.0. The objective reads 'Achieved' with 'n/20 men' in its Reason once the census counts the 30 slot-table men inside the area.",
              "runFor": 90, "golden": {"every": 15}, "expect": [
        task("the company runs CompanyDefend", "Blue Company", "CompanyDefend", before=10),
        task("the stochastic platoon is ordered by KIND: it runs the generic Defend play", "Blue Stochastic Platoon", "StochasticUnitDefend", before=30),
        never("the stochastic platoon is never given a physical play", {"on": "task", "who": "Blue Stochastic Platoon", "is": "PlatoonDefend"}),
        field("the SITREP shows the stochastic platoon Green", "Blue Company", "CommandPicture.Subordinates.0.Strength", "==", "Green", before=60),
        field("the objective's census counts the stochastic platoon's men (>= 20 inside the area)", "Blue holds the line", "ObjectiveStatus.State", "==", "Achieved", before=80),
    ]}
    write(s, "demo-city", desc, checks)


def agg_refused_order():
    s = Scn("agg-refused-order", "a9000002")
    co = s.unit("Blue Company", BLUE, (240, 405), COMPANY)
    pl = s.unit("Blue Stochastic Battery", BLUE, (260, 440), ARTILLERY_BATTERY, co, aggregate={})
    s.trigger("Strike with the battery", BLUE, {"common": {"title": "Strike with the battery"}, "when": {"$type": "ScenarioTime", "fromSeconds": 10},
              "do": [{"act": "Order", "args": {"target": pl, "behavior": "Order.Strike", "params": {"Location": pt((300, 900)), "Rounds": 3}}}]})
    desc = ("What: on demo-city a physical 'Blue Company' (no play) over a STOCHASTIC 'Blue Stochastic Battery' (an artillery battery: three M109 holdings from its TKB composite). At 10 s a "
            "superior-origin order Strike (3 rounds on (300,900)) reaches the battery - the (class, resolution) table has no Strike row for a stochastic unit (a physical battery runs ArtilleryUnitStrike). "
            "Judge: (1) the order is refused and the company hears PROGRESS TaskFailed; (2) nothing else happens: the battery runs no play and does not move. Fail: no TaskFailed, or the battery starts a StochasticUnit play. Check: scripts/scenario-regression.sh agg-refused-order")
    checks = {"$doc": "§23.6 ST-0. A scenario Order verb checks the order against the PHYSICAL class table before it publishes (a rifle squad has no Strike - 'no behaviour' - so no event would exist); "
                      "an ARTILLERY BATTERY does (ArtilleryUnitStrike), so the order is published and the receiver's stochastic table refuses it (B-6, B-8). The company hears the refusal as a CommandReportHistory row "
                      "(it holds no CommandPicture - it ordered nothing).",
              "runFor": 40, "golden": {"every": 10}, "expect": [
        field("the company hears PROGRESS TaskFailed", "Blue Company", "CommandReportHistory.Items.0.Report.Subtype", "==", 12, after=10, before=30),
        never("the battery runs no stochastic play", {"on": "task", "who": "Blue Stochastic Battery", "is": ["StochasticUnitDefend", "StochasticUnitMove", "StochasticUnitAttack", "StochasticUnitBreakContact"]}),
        field("the battery never moves", "Blue Stochastic Battery", "AggregateUnit.Move", "==", "Stopped", atEnd=True),
    ]}
    write(s, "demo-city", desc, checks)


def agg_recorded():
    s = Scn("agg-recorded", "a9000003")
    s.unit("Blue Stochastic Platoon", BLUE, (300, 200), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 200)), "SearchRadius": 30}))
    s.unit("Red Stochastic Platoon", RED, (300, 400), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 400)), "SearchRadius": 30}))
    desc = ("What: on basic-desert a BLUE and a RED stochastic platoon (30 men each, from the TKB composite) face each other 200 m apart, both defending. They fire on each other "
            "(agg-backend's phase A), so their slot tables CHANGE during the run (hits, stress, ammunition). Judge: the run is recorded; the review harness (--verify-review-at) seeks through it and "
            "every component - above all UnitSlotTable and UnitHoldings - equals live at every keyframe, and live comes back exact. Check: scripts/scenario-regression.sh agg-recorded "
            "and: dotnet <runner> --mode editor --headless --verify agg-recorded --verify-review-at 30")
    checks = {"$doc": "§23.6 ST-0, 'a run recorded and replayed shows the same slot table at every keyframe'. The replay comparison is the editor tier's review check (DESIGN_Scenario_Regression §10-3, "
                      "`--verify-review-at T`: REVIEW vs LIVE at each second, then AFTER vs LIVE[T], every component), not a checks.json row; these rows pin that the slot tables really CHANGE in the run "
                      "(so the comparison is not vacuous).",
              "runFor": 60, "golden": {"every": 10}, "expect": [
        field("the fight reaches the Blue platoon (a slot is down)", "Blue Stochastic Platoon", "UnitSlotTable.Slots.*.Health", "<=", 0, before=55, where={"CompositeSlot": [">", 0]}),
        field("the fight reaches the Red platoon (a slot is down)", "Red Stochastic Platoon", "UnitSlotTable.Slots.*.Health", "<=", 0, before=55, where={"CompositeSlot": [">", 0]}),
    ]}
    write(s, "basic-desert", desc, checks)


# ───────────────────────────────────────────── ST-1 ─────────────────────────────────────────────

def agg_march_open():
    s = Scn("agg-march-open", "a9000004")
    s.unit("Blue Stochastic Platoon", BLUE, (100, 300), PLATOON, aggregate={}, behavior=("StochasticUnitMove", {"Location": pt((2100, 300))}))
    # M-2: 30 men on foot, a column: v = FootMps 1.5 x 0.9 (off road, foot) = 1.35 m/s over 2000 m; the column is ColumnDepth = 145 m long (the tail runs past the end at the last pace)
    pred = (2000 + 145) / 1.35
    lo, hi = pred * 0.9, pred * 1.1
    desc = ("What: on berlin (flat, empty) a dismounted STOCHASTIC 'Blue Stochastic Platoon' (30 men) is ordered to move 2 km east, (100,300) to (2100,300), by the generic Move play. "
            f"Judge: it marches as a column (Formation Column), its route is the straight 2000 m leg, and its TAIL arrives within +-10 % of M-2's predicted time: (2000 + 145 m column) / (1.5 m/s x 0.9) = {pred:.0f} s, "
            f"so between {lo:.0f} s and {hi:.0f} s. Fail: it arrives outside that window, or not as a column. Check: scripts/scenario-regression.sh agg-march-open (~6 min)")
    checks = {"$doc": f"§23.6 ST-1. Prediction offline from UnitSpeedModel (FootMps 1.5, OffRoadFoot 0.9, column factor 1) and ColumnDepth for 30 slots (145 m, measured in the probe): ({2000}+145)/1.35 = {pred:.1f} s. The play is started at t = 0.1 s.",
              "runFor": math.ceil(hi) + 10, "golden": False, "expect": [
        field("it marches as a column", "Blue Stochastic Platoon", "AggregateUnit.Formation", "==", "Column", before=60),
        field("the route is the 2000 m leg", "Blue Stochastic Platoon", "UnitRoute.Length", "==", 2000, before=60),
        field("its tail arrives within +-10 % of the predicted time", "Blue Stochastic Platoon", "AggregateUnit.Move", "==", "Arrived", after=round(lo), before=round(hi)),
    ]}
    write(s, "berlin", desc, checks)



WHEELED_HOLDINGS = {"Count": 1, "Items": [{"Type": 102, "Count": 8, "Operational": 8, "Ammo": 0, "Kind": "Vehicle", "Mobility": "Wheeled", "Sensor": "NoSensor", "Immobile": 0}]}


def agg_march_mounted(route_length=None, depth=None, speed=None):
    s = Scn("agg-march-mounted", "a9000005")
    s.unit("Blue Mounted Platoon", BLUE, (10, 200), PLATOON, aggregate={"Posture": "Mounted"}, behavior=("StochasticUnitMove", {"Location": pt((200, 390))}),
           extra={"UnitHoldings": WHEELED_HOLDINGS})
    window = ""
    arrive = []
    if route_length:
        pred = (route_length + depth) / speed
        lo, hi = pred * 0.9, pred * 1.1
        window = f" Its tail arrives within +-10 % of M-2's predicted time: ({route_length:.0f} m route + {depth:.0f} m column) / {speed:.2f} m/s = {pred:.0f} s, so between {lo:.0f} s and {hi:.0f} s."
        arrive = [field("its tail arrives within +-10 % of the predicted time", "Blue Mounted Platoon", "AggregateUnit.Move", "==", "Arrived", after=round(lo), before=round(hi))]
    desc = ("What: on test-town (two crossing streets, a forest 'North Wood' at x 40-160, y 300-380) a MOUNTED stochastic 'Blue Mounted Platoon' (30 men, 8 wheeled HMMWV holdings) is ordered from (10,200) to "
            "(200,390) by the generic Move play - the straight line would cross the wood. Judge: it keeps to the roads (the route's backend is the road graph's / the hybrid planner, not a straight leg), "
            "never enters the wood, and arrives." + window + " Fail: the route crosses the wood, or the arrival is out of the window. Check: scripts/scenario-regression.sh agg-march-mounted")
    checks = {"$doc": "§23.6 ST-1. The design's 'mounted company' is a mounted PLATOON here: no TKB composite carries a company's vehicles, so the holdings (8 wheeled HMMWV, type 102) are authored - the fill keeps authored holdings "
                      "and fills only the slot table. Wheeled in woods = M-1 forbids (UnitSpeedModel.WoodsWheeled); the prediction is computed offline from the measured route (the road polyline: UnitRoute.Length) "
                      "with UnitSpeedModel (the TKB top speed of type 102 for BaseSpeed, factor 1 on the road). NO golden: a captured one differed once in five runs (the unit's position one 16.7 m/s step off from t = 15 s - "
                      "a start-frame jitter, finding F-13), so the conditions judge it.",
              "runFor": 150, "golden": False, "expect": [
        field("it routes by the roads (a planner's route, not the straight leg)", "Blue Mounted Platoon", "UnitRoute.Backend", "!=", "", before=10),
        never("it never enters the wood (west half)", {"on": "near", "who": "Blue Mounted Platoon", "point": [70, 340, 0], "within": 45}),
        never("it never enters the wood (east half)", {"on": "near", "who": "Blue Mounted Platoon", "point": [130, 340, 0], "within": 45}),
        ] + arrive}
    write(s, "test-town", desc, checks)


def agg_route_dropped():
    s = Scn("agg-route-dropped", "a9000006")
    co = s.unit("Blue Company", BLUE, (60, 300), COMPANY)
    s.unit("Blue Stochastic Platoon", BLUE, (100, 300), PLATOON, co, aggregate={}, behavior=("StochasticUnitMove", {"Location": pt((900, 300))}))
    cut = lambda n, t: {"name": n, "at": t, "set": {"who": "Blue Stochastic Platoon", "component": "AggregateUnit", "patch": {"Move": "RouteFailed"}}}
    desc = ("What: on berlin a stochastic 'Blue Stochastic Platoon' (30 men) marches 800 m east under the generic Move play. The check CUTS its route four times - at 10, 20, 30 and 40 s the "
            "follower's state is set to RouteFailed (the planner dropped the route). Judge: the play re-requests the route after each of the first three cuts (the unit is OnRoute again) and, "
            "at the fourth (more than the 3 re-requests R-8308 allows), gives up: the unit stays RouteFailed and the company hears PROGRESS TaskFailed. Fail: a fourth re-request, or no TaskFailed. "
            "Check: scripts/scenario-regression.sh agg-route-dropped")
    checks = {"$doc": "§23.6 ST-1. Over open ground the follower always finds a route (an infantry unit falls back to the straight leg), so the drop is injected as a do-set of AggregateUnit.Move = RouteFailed - "
                      "the state the follower itself writes when the planner returns null - at 10/20/30/40 s.",
              "runFor": 70, "golden": {"every": 10},
              "do": [cut("cut 1", 10), cut("cut 2", 20), cut("cut 3", 30), cut("cut 4", 40)],
              "expect": [
        field("re-requested after cut 1: a fresh route (the head's distance restarts)", "Blue Stochastic Platoon", "AggregateUnit.PathDistanceM", "<", 6, after=10.1, before=14),
        field("re-requested after cut 2", "Blue Stochastic Platoon", "AggregateUnit.PathDistanceM", "<", 6, after=20.1, before=24),
        field("re-requested after cut 3", "Blue Stochastic Platoon", "AggregateUnit.PathDistanceM", "<", 6, after=30.1, before=34),
        field("it marches again after cut 3 (OnRoute)", "Blue Stochastic Platoon", "AggregateUnit.Move", "==", "OnRoute", after=31, before=39),
        never("no fourth re-request: the head's distance never restarts after cut 4", {"on": "field", "who": "Blue Stochastic Platoon", "path": "AggregateUnit.PathDistanceM", "op": "<", "value": 6}, after=41),
        field("the fourth cut ends the order: the unit stays RouteFailed", "Blue Stochastic Platoon", "AggregateUnit.Move", "==", "RouteFailed", atEnd=True),
        field("the company hears PROGRESS TaskFailed", "Blue Company", "CommandReportHistory.Items.0.Report.Subtype", "==", 12, after=40, before=65),
    ]}
    write(s, "berlin", desc, checks)


# ───────────────────────────────────────────── ST-2 ─────────────────────────────────────────────
OBSERVER = {"SquadCognitiveState": {}}   # the contact pool N-2 writes into (nothing provisions it for a memberless unit - finding F-9c)


def steps_to_contact(range_m, men=10):
    """N-1 off the page: the level adds P(r) = 0.9 * 2^-(r/R50)^2 each 1 s step, R50 = 400 m (the eye) * sqrt(men/9) for a target standing still; contact at level 1."""
    r50 = 400.0 * math.sqrt(men / 9.0)
    p = 0.9 * 2 ** (-((range_m / r50) ** 2))
    return math.ceil(1.0 / p) if p > 1e-9 else None


def agg_detect_by_range():
    s = Scn("agg-detect-by-range", "a9000007")
    pairs = [("near", 150, 200), ("mid", 700, 7200), ("far", 2500, 14200)]   # 7 km apart: beyond MaxRange 6000, so each observer sees only its own target
    rows = []
    for tag, rng, y in pairs:
        s.unit(f"Blue Observer {tag}", BLUE, (1000, y), SQUAD, aggregate={}, extra=OBSERVER)
        s.unit(f"Red Target {tag}", RED, (1000 + rng, y), SQUAD, aggregate={})
        n = steps_to_contact(rng)
        if n is not None and n <= 60:
            rows.append(field(f"a contact on the {tag} target ({rng} m) appears inside N-1's window ({n} steps +- 3 s)", f"Blue Observer {tag}", "UnitDetections.Pairs.0.Reported", "==", 1, after=max(0, n - 3), before=n + 4))
        else:
            rows.append(never(f"no contact on the {tag} target ({rng} m) - beyond the sensors' reach", {"on": "field", "who": f"Blue Observer {tag}", "path": "UnitDetections.Pairs.0.Reported", "op": "==", "value": 1}))
    desc = ("What: on berlin three pairs of stochastic squads, 7 km apart (beyond any sensor), each a BLUE observer (4+ men, eyes only) and a RED target standing still in the open: 150 m, 700 m and 2500 m. "
            "Judge: N-1's curve P(r) = 0.9 * 2^-(r / (400 m * sqrt(men / 9)))^2 per second accumulates into a level; at level 1 a contact is reported. The 150 m and 700 m contacts appear inside the window "
            "the curve predicts (the step counts are in the row names), the 2500 m target is never contacted. Fail: a contact far out of the window, or beyond the eye's reach. Check: scripts/scenario-regression.sh agg-detect-by-range")
    checks = {"$doc": "§23.6 ST-2. Windows computed offline from UnitDetection.StepChance (PMax 0.9, EyeR50 400 m, Signature sqrt(men/9), a target standing still): steps = ceil(1/P(r)). Read from "
                      "UnitDetections.Pairs.0.Reported (the observer's own level book); the contact-pool write (SquadCognitiveState.Contacts) is the same moment but needs SquadCognitiveState on the observer.",
              "runFor": 90, "golden": {"every": 15}, "expect": rows}
    write(s, "berlin", desc, checks)


def agg_sees_physical():
    s = Scn("agg-sees-physical", "a9000008")
    co = s.unit("Blue Platoon", BLUE, (300, 100), PLATOON)
    s.unit("Blue Stochastic Squad", BLUE, (300, 140), SQUAD, co, aggregate={}, extra=OBSERVER)
    s.physical_squad("Red Squad", RED, (300, 390), None)
    desc = ("What: on basic-desert a 'Blue Stochastic Squad' (memberless) under a physical 'Blue Platoon' commander watches a PHYSICAL 'Red Squad' (four men) standing 250 m north. "
            "Judge: (1) the stochastic observer detects the physical squad (its level book holds a reported contact); (2) it reports SPOT up the chain - the platoon commander hears it; "
            "(3) the physical squad never contacts the stochastic unit (it has no body to see). Fail: no contact, no SPOT, or the Red squad's contact pool ever holds the Blue unit. Check: scripts/scenario-regression.sh agg-sees-physical")
    checks = {"runFor": 60, "golden": {"every": 15}, "expect": [
        field("the stochastic observer holds a reported contact on the physical squad", "Blue Stochastic Squad", "UnitDetections.Pairs.0.Reported", "==", 1, before=30),
        field("the platoon commander hears a SPOT", "Blue Platoon", "CommandReportHistory.Items.0.Report.Kind", "==", "Spot", before=50),
        never("the physical squad never contacts the stochastic unit", {"on": "field", "who": "Red Squad", "path": "SquadCognitiveState.Contacts.Count", "op": ">", "value": 0}),
    ]}
    write(s, "basic-desert", desc, checks)


def agg_break_contact():
    s = Scn("agg-break-contact", "a9000009")
    s.unit("Blue Stochastic Squad", BLUE, (300, 300), SQUAD, aggregate={}, extra=OBSERVER)
    s.unit("Red Stochastic Squad", RED, (300, 500), SQUAD, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 500)), "SearchRadius": 30}), extra=OBSERVER)
    desc = ("What: on berlin a 'Blue Stochastic Squad' with NO order stands at (300,300); a 'Red Stochastic Squad' defending 200 m north at (300,500) fires on it (agg-backend's phase A). "
            "Judge: fired on while not defending, Blue's SOP (StochasticUnitSop) orders BreakContact: it withdraws about 300 m from where it stands, away from the contact, and arrives. "
            "Fail: it stands and is shot, or it withdraws toward the enemy. Check: scripts/scenario-regression.sh agg-break-contact")
    checks = {"runFor": 400, "golden": {"every": 50}, "expect": [
        task("the SOP orders BreakContact", "Blue Stochastic Squad", "StochasticUnitBreakContact", before=60),
        field("it withdraws about 300 m south - x stays within 30 m of its own (300)", "Blue Stochastic Squad", "AggregateUnit.Destination.0", ">", 270, after=2, before=60),
        field("  ... and x within 30 m", "Blue Stochastic Squad", "AggregateUnit.Destination.0", "<", 330, after=2, before=60),
        field("  ... y within 30 m of 0 (300 m south of where it stood, away from the contact at y = 500)", "Blue Stochastic Squad", "AggregateUnit.Destination.1", "<", 30, after=2, before=60),
        field("  ... y not past -30 m", "Blue Stochastic Squad", "AggregateUnit.Destination.1", ">", -30, after=2, before=60),
        field("it arrives", "Blue Stochastic Squad", "AggregateUnit.Move", "==", "Arrived", after=60, before=390),
    ]}
    write(s, "berlin", desc, checks)


GENERATORS = {"agg-march-mounted": lambda: agg_march_mounted(381.25, 350, 60 / 3.6), "agg-platoon-under-company": agg_platoon_under_company, "agg-refused-order": agg_refused_order, "agg-recorded": agg_recorded, "agg-march-open": agg_march_open,
              "agg-route-dropped": agg_route_dropped, "agg-detect-by-range": agg_detect_by_range, "agg-sees-physical": agg_sees_physical, "agg-break-contact": agg_break_contact}

if __name__ == "__main__":
    names = sys.argv[1:] or list(GENERATORS)
    for n in names:
        GENERATORS[n]()
