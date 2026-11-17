#!/usr/bin/env python3
"""The stochastic-unit FEATURE-TEST scenarios (R-4277, design DESIGN_Stochastic_Units_And_Resolution.md §23.6): all twenty, ST-0 ... ST-5, mixed and F-14's agg-defend-fires.
Writes scenarios/demos/aggregates/<name>/{scenario.json,checks.json}; golden.json is CAPTURED (scripts/scenario-regression.sh's runner, --verify-capture).
Re-runnable: `python3 scripts/gen-agg-scenarios.py [name ...]`; `--probe <name>` writes an ST-5 scenario's MEASURING rows (which network ids its split children get) instead of its own.

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
    # R-8352: on FLAT ground (measured z 0) - F-19 put the old Blue spot (300,200) on basic-desert's 10 m ridge, out of Red's sight (no Blue slot ever down)
    s.unit("Blue Stochastic Platoon", BLUE, (300, 300), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 300)), "SearchRadius": 30}))
    s.unit("Red Stochastic Platoon", RED, (300, 500), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 500)), "SearchRadius": 30}))
    desc = ("What: on basic-desert a BLUE and a RED stochastic platoon (30 men each, from the TKB composite) face each other 200 m apart on flat ground, both defending. They fire on each other "
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
OBSERVER = {"SquadCognitiveState": {}}   # the contact pool N-2 writes into - authored before CE-8021 provisioned it (F-11); agg-break-contact keeps it (redundant now, golden unchanged)


OPTICS = {"Count": 1, "Items": [{"Type": 0, "Count": 1, "Operational": 1, "Ammo": 0, "Kind": "Sensor", "Mobility": "NoMobility", "Sensor": "Optics", "Immobile": 0}]}


def steps_to_contact(range_m, men=10, r50=400.0):
    """N-1 off the page: the level adds P(r) = 0.9 * 2^-(r/R50)^2 each 1 s step, R50 = the sensor's (eye 400 m, optics 1200 m) * sqrt(men/9) for a target standing still; contact at level 1."""
    r50 = r50 * math.sqrt(men / 9.0)
    p = 0.9 * 2 ** (-((range_m / r50) ** 2))
    return math.ceil(1.0 / p) if p > 1e-9 else None


def agg_detect_by_range():
    s = Scn("agg-detect-by-range", "a9000007")
    # (tag, range, y, the observer's sensor): 7 km apart - beyond MaxRange 6000, so each observer sees only its own target.
    # R-8331: the naked eye stops at the soldiers' TKB VisionRange (agg-backend R-8006, 150 m for a rifle squad's men), so the curve is read
    # INSIDE it by eye (100 m) and at mid range through OPTICS (R50 1200 m, not capped): 2100 m; the far eye-only pair is beyond any reach.
    pairs = [("near", 100, 200, "eye"), ("mid", 2100, 7200, "optics"), ("far", 2500, 14200, "eye")]
    rows = []
    for tag, rng, y, sensor in pairs:
        # both DEFEND in place: the near pair fights at 100 m and, order-less, each SOP would break contact - a mover whose position repeated only to
        # ~2 cm between fresh runs (the F-13 start-frame jitter, re-seen 2026-11-16), so the golden would not be SAME
        dfd = lambda x: ("StochasticUnitDefend", {"Position": pt((x, y)), "SearchRadius": 30})
        s.unit(f"Blue Observer {tag}", BLUE, (1000, y), SQUAD, aggregate={}, extra={"UnitHoldings": OPTICS} if sensor == "optics" else None, behavior=dfd(1000))
        s.unit(f"Red Target {tag}", RED, (1000 + rng, y), SQUAD, aggregate={}, behavior=dfd(1000 + rng))
        n = steps_to_contact(rng, r50=1200.0 if sensor == "optics" else 400.0) if not (sensor == "eye" and rng > 150) else None
        if n is not None and n <= 60:
            rows.append(field(f"a contact on the {tag} target ({rng} m, {sensor}) appears inside N-1's window ({n} steps +- 3 s)", f"Blue Observer {tag}", "UnitDetections.Pairs.0.Reported", "==", 1, after=max(0, n - 3), before=n + 4))
            rows.append(field(f"  ... the {tag} contact lands in the observer's own contact pool (N-2; no authored pool, CE-8021)", f"Blue Observer {tag}", "SquadCognitiveState.Contacts.Count", ">", 0, after=max(0, n - 3), before=n + 4))
        else:
            rows.append(never(f"no contact on the {tag} target ({rng} m, {sensor}) - beyond the sensors' reach", {"on": "field", "who": f"Blue Observer {tag}", "path": "UnitDetections.Pairs.0.Reported", "op": "==", "value": 1}))
    desc = ("What: on berlin three pairs of stochastic squads, 7 km apart (beyond any sensor), each a BLUE observer and a RED target standing still in the open: 100 m (eyes only), "
            "2100 m (the observer carries OPTICS) and 2500 m (eyes only). Judge: N-1's curve P(r) = 0.9 * 2^-(r / (R50 * sqrt(men / 9)))^2 per second (R50: eye 400 m, optics 1200 m) accumulates into a level; "
            "at level 1 a contact is reported into the observer's own contact pool. The 100 m and 2100 m contacts appear inside the window the curve predicts (the step counts are in the row names); "
            "the 2500 m target, beyond the naked eye's reach (the soldiers' own vision range), is never contacted. Fail: a contact far out of the window, or beyond the eye's reach. Check: scripts/scenario-regression.sh agg-detect-by-range")
    checks = {"$doc": "§23.6 ST-2. Windows computed offline from UnitDetection.StepChance (PMax 0.9, EyeR50 400 m / OpticsR50 1200 m, Signature sqrt(men/9), a target standing still): steps = ceil(1/P(r)). "
                      "R-8331: the naked eye is capped at the soldiers' TKB VisionRange (agg-backend R-8006, CE-8009) - 150 m for these men - so the mid range is read through an authored Optics holding (not capped); "
                      "the pool row reads SquadCognitiveState, which the spawn fill now provisions (CE-8021) - the observers author none.",
              "runFor": 90, "golden": {"every": 15}, "expect": rows}
    write(s, "berlin", desc, checks)


def agg_sees_physical():
    s = Scn("agg-sees-physical", "a9000008")
    co = s.unit("Blue Platoon", BLUE, (300, 100), PLATOON)
    # R-8334: OPTICS at 700 m, on flat berlin. The naked eye now stops at the soldiers' VisionRange (150 m, R-8006) while the resolution manager
    # splits a stochastic unit with a physical enemy inside 600 m (400 m band + 200 m lead; forced at 300 m, C-7) - so an eye-only observer is
    # physical before any body is in its sight (finding F-20). On basic-desert the unit's line runs into the ground (finding F-19).
    s.unit("Blue Stochastic Squad", BLUE, (300, 140), SQUAD, co, aggregate={}, extra={"UnitHoldings": OPTICS})
    s.physical_squad("Red Squad", RED, (300, 840), None)
    desc = ("What: on berlin (flat) a 'Blue Stochastic Squad' (memberless, carrying OPTICS) under a physical 'Blue Platoon' commander watches a PHYSICAL 'Red Squad' (four men) standing 700 m north - "
            "outside the 600 m band in which the resolution manager would split the observer. Judge: (1) the stochastic observer detects the physical squad (its level book holds a reported contact, "
            "and its own contact pool holds it); (2) it reports SPOT up the chain - the platoon commander hears it; (3) the physical squad never contacts the stochastic unit (it has no body to see); "
            "(4) the observer stays stochastic. Fail: no contact, no SPOT, the Red squad's contact pool ever holds the Blue unit, or the observer splits. Check: scripts/scenario-regression.sh agg-sees-physical")
    checks = {"$doc": "§23.6 ST-2 (N-3, agg-backend CE-8018). Optics R50 1200 m x one man's signature sqrt(1/9) = 400 m: P(700 m) = 0.9 x 2^-(1.75^2) = 0.11 a step, about 10 steps to contact "
                      "(measured: reported at 9.25 s, the SPOT at 12.25 s). R-8334: optics at 700 m on berlin - the eye-only / 250 m / basic-desert version cannot pass after agg-backend's merge "
                      "(F-19: the unit's line from z + 2 m runs into basic-desert's ground; F-20: an eye-only observer is split before it can see a body). No authored contact pool (CE-8021 provisions it).",
              "runFor": 60, "golden": {"every": 15}, "expect": [
        field("the stochastic observer holds a reported contact on the physical squad", "Blue Stochastic Squad", "UnitDetections.Pairs.0.Reported", "==", 1, before=30),
        field("  ... in its own contact pool (N-2)", "Blue Stochastic Squad", "SquadCognitiveState.Contacts.Count", ">", 0, before=30),
        field("the platoon commander hears a SPOT", "Blue Platoon", "CommandReportHistory.Items.0.Report.Kind", "==", "Spot", before=50),
        never("the physical squad never contacts the stochastic unit", {"on": "field", "who": "Red Squad", "path": "SquadCognitiveState.Contacts.Count", "op": ">", "value": 0}),
        never("the observer stays stochastic (700 m: outside the split band)", {"on": "field", "who": "Blue Stochastic Squad", "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}),
    ]}
    write(s, "berlin", desc, checks)


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



# ───────────────────────────────────────────── ST-3 ─────────────────────────────────────────────
def slot_table(n, ammo, role="Rifleman"):
    """An AUTHORED slot table (so the spawn fill leaves it alone): n whole riflemen with `ammo` rounds each."""
    return {"Count": n, "Chunk": 0, "Slots": [{"Health": 25.0, "Stress": 0.0, "Ammo": ammo, "Role": role, "CompositeSlot": k + 1} for k in range(n)]}


BROKEN = 0.75      # AggregateUnits.BrokenAt = TemperamentOf.TrainedNerve: a standing slot with Stress >= this has broken (fled / collapsed)
LIVE = {"CompositeSlot": [">", 0]}


def agg_platoon_duel():
    s = Scn("agg-platoon-duel", "a9000010")
    # R-8352: on FLAT ground (y 260-600, measured z 0). F-19 (agg-backend R-4286) now stands a stochastic unit on the terrain: the old Blue spot (300,200)
    # is on basic-desert's 10 m ridge (crest 10.x at y 210), and Red's line from 2 m to Blue's 1 m point passes under the crest - Red never fired (measured)
    s.unit("Blue Stochastic Platoon", BLUE, (300, 300), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 300)), "SearchRadius": 30}))
    s.unit("Red Stochastic Squad", RED, (300, 600), SQUAD, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 600)), "SearchRadius": 30}))
    desc = ("What: on basic-desert a BLUE stochastic platoon (30 men) and a RED stochastic squad (4 men - the weaker side) face each other 300 m apart on flat ground, both defending; they fire on each other "
            "(agg-backend's phase A). Judge: (1) the fire reaches BOTH sides (a Blue man is hit; most of the Red squad is down); (2) suppression rises on both (AggregateUnit.SuppressedShare > 0); (3) the weaker side breaks first - a Red slot's "
            "stress reaches the break level before any Blue slot's does. NOT judged: 'losses inside the C-8 band' - no C-8 band applies to this fight (R-8381): agg-backend's ladder (CE-8009, rung 1 PASS) "
            "measures 9 v 9 riflemen at 100 / 200 / 275 m, and this duel is a 3-to-1 fight at 300 m, the rifle's exact reach that R-8009 keeps out of the rung; no band is invented. Check: scripts/scenario-regression.sh agg-platoon-duel")
    checks = {"$doc": "§23.6 ST-3. No C-8 band row (R-8381, §23.6h): CE-8009's rung 1 (STATUS_AggBackend.md §5) bands 9 v 9 standard riflemen at 100 / 200 / 275 m; this duel is 30 v 10 slots at 300 m - another force ratio, and the range R-8009 excludes as the M4's exact reach - so no band of the ladder applies and none is invented. What is encoded: losses on both sides, suppression, and who breaks first. "
                      "Slot health is 25 when whole (UnitSplits.SlotFullHealth); 'down' = Health < 0.5 (UnitSlotTable.DownBelow); 'broken' = Stress >= 0.75 (AggregateUnits.BrokenAt). A Red squad is 10 slots; "
                      "measured (re-authored 2026-11-17, R-8344, after CE-8009's measured rifle rate and body zones): a Blue man is hit at 3.25 s but none goes down in 180 s; Red has 8 down at 11.25 s, all 10 at 14.25 s, four broken at 7.25 s; Blue never breaks.",
              "runFor": 60, "golden": {"every": 10}, "expect": [
        field("the fight reaches the Blue platoon (a man is hit)", "Blue Stochastic Platoon", "UnitSlotTable.Slots.*.Health", "<", 24.9, before=30, where=LIVE),
        field("the fight reaches the Red squad (most of it is down)", "Red Stochastic Squad", "UnitSlotTable.Slots.*.Health", "<", 0.5, before=30, where=LIVE, elements=8),
        field("Blue is suppressed", "Blue Stochastic Platoon", "AggregateUnit.SuppressedShare", ">", 0, before=30),
        field("Red is suppressed hard", "Red Stochastic Squad", "AggregateUnit.SuppressedShare", ">", 0.5, before=30),
        field("the weaker side (Red) breaks: four slots at the break level", "Red Stochastic Squad", "UnitSlotTable.Slots.*.Stress", ">=", BROKEN, before=30, where=LIVE, elements=4),
        never("the stronger side (Blue) has not broken when Red has", {"on": "field", "who": "Blue Stochastic Platoon", "path": "UnitSlotTable.Slots.*.Stress", "op": ">=", "value": BROKEN, "where": LIVE}),
    ]}
    write(s, "basic-desert", desc, checks)


def agg_shelled_unit():
    s = Scn("agg-shelled-unit", "a9000011")
    bat = s.unit("Blue Battery", BLUE, (-3800, 0), ARTILLERY_BATTERY, behavior=("ArtilleryUnitStrike", {"Location": pt((340, 300)), "Control": "TimeOnTarget", "Sheaf": "Parallel", "Rounds": 6}))
    for i in range(3):
        s.unit(f"M109 {i + 1}", BLUE, (-3500, -40 + 40 * i), 107, bat, designation="SquadLeader")
    s.unit("Red Platoon In The Open", RED, (340, 300), PLATOON, aggregate={})
    s.unit("Red Platoon Clear", RED, (340, 900), PLATOON, aggregate={})   # 600 m from the aim point: no round of the sheaf can reach it
    desc = ("What: on indirect-fire-range a physical 'Blue Battery' (three M109s, 3.8 km back) is ordered ArtilleryUnitStrike, 6 rounds on (340,300), TimeOnTarget. A STOCHASTIC 'Red Platoon In The Open' "
            "(30 men) stands on the aim point; a second stochastic 'Red Platoon Clear' stands 600 m north of it, clear of the sheaf. Judge: (1) the guns fire and bursts land on the aim point; (2) the area sink "
            "hits the platoon in the open - a slot is down - and suppresses it; (3) the platoon 600 m away is never hit: not one slot of it loses health, it is never suppressed. Fail: no hit on the target, or a hit "
            "(health, suppression) on the platoon the rounds missed. Check: scripts/scenario-regression.sh agg-shelled-unit")
    checks = {"$doc": "§23.6 ST-3. The mission is the ArtilleryUnitStrike of ua-indirect-fire (a physical battery fires on a point; no observer needed). 'Never hit by a round that missed its footprint' = the clear platoon's slots all "
                      "stay whole (Health 25, none below 24.9) and its SuppressedShare stays 0 for the whole run.",
              "runFor": 150, "golden": {"every": 25}, "expect": [
        {"name": "the guns fire bursts on the aim point", "on": "blast", "by": "*M109", "n": 3, "where": {"burst.0": [">", 240], "burst.1": ["<", 400]}},
        field("the platoon in the open takes a hit (a slot is down)", "Red Platoon In The Open", "UnitSlotTable.Slots.*.Health", "<", 0.5, before=145, where=LIVE),
        field("it is suppressed", "Red Platoon In The Open", "AggregateUnit.SuppressedShare", ">", 0, before=145),
        never("the platoon the rounds missed loses no health", {"on": "field", "who": "Red Platoon Clear", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 24.9, "where": LIVE}),
        never("the platoon the rounds missed is never suppressed", {"on": "field", "who": "Red Platoon Clear", "path": "AggregateUnit.SuppressedShare", "op": ">", "value": 0}),
    ]}
    write(s, "indirect-fire-range", desc, checks)


def agg_prepared_defence():
    s = Scn("agg-prepared-defence", "a9000012")
    rows = []
    for tag, level, y in (("Prepared", "Prepared", 200), ("Fortified", "Fortified", 7200)):
        # R-8343: the defenders DEFEND in place - order-less, their SOP breaks contact under fire (CE-8021) and they leave before a man is down;
        # occupying where they stand they keep the authored level (R-8342)
        s.unit(f"Red {tag} Defender", RED, (1000, y), PLATOON, aggregate={"Prepared": level}, behavior=("StochasticUnitDefend", {"Position": pt((1000, y))}))
        s.unit(f"Blue Attacker {tag}", BLUE, (1000, y - 350), COMPANY, aggregate={}, behavior=("StochasticUnitAttack", {"Position": pt((1000, y))}))
    desc = ("What: on berlin two identical fights 7 km apart (out of each other's sight): a BLUE stochastic platoon (30 men) attacks a RED stochastic platoon (30 men) 500 m north of it. "
            "One Red defender has been in position long enough to be PREPARED (>= 600 s), the other FORTIFIED (>= 3600 s) - authored as the level they would have reached (no play: the cover factor is read from "
            "AggregateUnit.Prepared, 0.45 / 0.25; each defender DEFENDS in place and keeps its authored level, R-8342; the Defend play's own time-in-position ladder is its rail's, R-8306). "
            "Judge: the Fortified defender HOLDS LONGER - the Prepared one has lost half its men (15) by 70 s, the Fortified one has not. Check: scripts/scenario-regression.sh agg-prepared-defence")
    checks = {"$doc": "§23.6 ST-3. The levels are authored: StochasticUnitDefend stamps Hasty at occupation and climbs by sim time (600 s / 3600 s), so a play-driven version would need a 3700 s run; the combat reads "
                      "AggregateUnit.Prepared directly (StochasticCombat.CoverFactor: 0.45 Prepared, 0.25 Fortified). The attackers are COMPANIES (90 men) against 30-man defenders so the losses are not one or two slots. "
                      "Re-authored 2026-11-17 (R-8343) after agg-backend's CE-8009 (personal weapons at the measured 0.4 rounds a man a second, hits through the body's zones and armour): the "
                      "order-less defenders broke contact under fire (our SOP, CE-8021) before a man was down, so they now DEFEND in place. Measured: fire opens at ~54 s (300 m); the first men "
                      "fall within a second on both defenders, so a small loss count does not separate them - the half-strength line does: the Prepared defender has 15 down at 65.25 s, "
                      "the Fortified one at 73.25 s.",
              "runFor": 90, "golden": {"every": 15}, "expect": [
        field("the Fortified defender is fought: a slot is down", "Red Fortified Defender", "UnitSlotTable.Slots.*.Health", "<", 0.5, before=90, where=LIVE),
        field("the Prepared defender has lost half its men (15) by 70 s", "Red Prepared Defender", "UnitSlotTable.Slots.*.Health", "<", 0.5, before=70, where=LIVE, elements=15),
        never("the Fortified defender has NOT lost half its men by 70 s (it holds longer)", {"on": "field", "who": "Red Fortified Defender", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 15}, before=70),
        field("both defenders keep the level they were authored with", "Red Fortified Defender", "AggregateUnit.Prepared", "==", "Fortified", atEnd=True),
    ]}
    write(s, "berlin", desc, checks)


def agg_out_of_ammo():
    s = Scn("agg-out-of-ammo", "a9000013")
    co = s.unit("Blue Company", BLUE, (1000, 100), COMPANY, behavior=("CompanyAttack", {"Objective1": pt((1000, 500)), "Objective2": pt((1000, 560)), "DeadlineSeconds": 300.0, "Radius": 40}))
    s.unit("Blue Stochastic Squad", BLUE, (1000, 200), SQUAD, co, aggregate={"Echelon": 0}, extra={"UnitSlotTable": slot_table(8, 12), "UnitHoldings": {"Count": 0, "Items": []}})
    # R-8333: the target DEFENDS (StochasticUnitDefend in place): with its own contact pool (CE-8021) an order-less unit under fire is sent back by its SOP
    # (BreakContact, ~300 m), out of the riflemen's reach, and the squad stops with rounds left - measured; a defender is what the SOP spares
    s.unit("Red Unarmed Platoon", RED, (1000, 500), PLATOON, aggregate={"Echelon": 1, "Prepared": "Fortified"}, extra={"UnitSlotTable": slot_table(30, 0), "UnitHoldings": {"Count": 0, "Items": []}},
           behavior=("StochasticUnitDefend", {"Position": pt((1000, 500)), "SearchRadius": 30}))
    s.unit("Red Spare Squad", RED, (1000, 9000), SQUAD, aggregate={})   # a FILLED unit: the fill is what teaches the engine each role's personal weapon (KillerVictimTkb.PersonalSoldiers)
    desc = ("What: on berlin a BLUE stochastic squad (8 riflemen, 12 rounds each - an authored slot table, so the spawn fill leaves it) under a physical 'Blue Company' fires at a RED stochastic platoon 300 m "
            "north (30 men, Fortified, DEFENDING in place, with NO ammunition: it cannot fire back). Judge: the squad fires until its rounds are spent, then stops (no slot holds a round, it fires no more), and the company's SITREP shows its "
            "ammunition Black (StaffComposerSystem.AmmoOf over UnitAmmunition.TryShare). Check: scripts/scenario-regression.sh agg-out-of-ammo")
    checks = {"$doc": "§23.6 ST-3. The squad's slot table is AUTHORED (8 riflemen, Ammo 12: 96 rounds) because a TKB basic load lasts too long to spend in a scenario; a spare, filled stochastic squad far away is what teaches the "
                      "engine each role's personal weapon (KillerVictimTkb.PersonalSoldiers is filled by the first spawn fill). The Red platoon is authored with 0 rounds, so it cannot fire back, and DEFENDS in place (R-8333: an order-less one breaks contact under fire now that it has a contact pool, CE-8021); the company attacks it (CompanyAttack asks "
                      "the squad for SITREPs). Measured (2026-11-17, after CE-8009's measured rate 0.4 rounds a man a second): the first round goes at 1.25 s, every slot is empty at 12.25 s, the SITREP ammunition reads Black at 18.25 s (the report's delay).",
              "runFor": 60, "golden": {"every": 10}, "expect": [
        field("it fires: rounds leave the slots", "Blue Stochastic Squad", "UnitSlotTable.Slots.*.Ammo", "<", 12, before=5, where=LIVE),
        field("it fires until the rounds are spent: every slot is empty", "Blue Stochastic Squad", "UnitSlotTable.Slots.*.Ammo", "==", 0, before=30, where=LIVE, elements=8),
        field("and then it fires no more: no firing line at the end", "Blue Stochastic Squad", "FireAllocation.Count", "==", 0, atEnd=True),
        field("the SITREP ammunition reads Black at its commander", "Blue Company", "CommandPicture.Subordinates.0.Ammo", "==", "Black", before=40),
        never("the squad is not destroyed (the Red platoon has no ammunition)", {"on": "field", "who": "Blue Stochastic Squad", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE}),
    ]}
    write(s, "berlin", desc, checks)


# ───────────────────────────────────────────── ST-4 ─────────────────────────────────────────────

GAP = 600            # metres between the two lines
YS = (350, 500, 650)


def agg_battalion_fight(rows=None):
    s = Scn("agg-battalion-fight", "a9000014")
    hq_b = s.unit("Blue Battalion HQ", BLUE, (40, 500), COMPANY, behavior=("CompanyAttack", {"Objective1": pt((100 + GAP, 500)), "Objective2": pt((100 + GAP + 100, 500)), "DeadlineSeconds": 900.0, "Radius": 60}))
    hq_r = s.unit("Red Battalion HQ", RED, (100 + GAP + 60, 500), COMPANY, behavior=("CompanyDefend", {"Position": pt((100 + GAP, 500)), "Threat": pt((100, 500)), "Frontage": 300.0, "Depth": 150.0}))
    for i, y in enumerate(YS):
        s.unit(f"Red Company {i + 1}", RED, (100 + GAP, y), COMPANY, hq_r, aggregate={})
    for i, y in enumerate(YS):
        s.unit(f"Blue Company {i + 1}", BLUE, (100, y), COMPANY, hq_b, aggregate={})
    for side, force, hq, x in (("Blue", BLUE, hq_b, 100), ("Red", RED, hq_r, 100 + GAP)):
        for k, minmen in (CENSUS_PROBES if rows == "probe" else [("270", 270)]):
            s.objective(f"{side} Battalion Census" if rows != "probe" else f"{side} census {k}", force, "HoldArea", {"common": {"title": f"{side} census {k}", "subject": hq},
                        "area": {"circle": {"center": pt((x, 500)), "radius": 300}}, "minMen": minmen, "maxEnemy": 0, "holdSeconds": 1})
    if rows == "probe":
        probe = []
        for side, hq in (("Blue", "Blue Battalion HQ"), ("Red", "Red Battalion HQ")):
            for i in range(3):
                co = f"{side} Company {i + 1}"
                for n in (5, 15, 30, 45, 60, 75, 85, 90):
                    probe.append(field(f"{co} dead x{n}", co, "UnitSlotTable.Slots.*.Health", "<", 0.5, where=LIVE, elements=n))
                for n in (10, 30, 60):
                    probe.append(field(f"{co} broken x{n}", co, "UnitSlotTable.Slots.*.Stress", ">=", BROKEN, where=LIVE, elements=n))
                for c in ("Green", "Amber", "Red", "Black", "ColourUnknown"):
                    probe.append(field(f"{hq} sees {co} {c}", hq, f"CommandPicture.Subordinates.{i}.Strength", "==", c, atEnd=True))
                    probe.append(field(f"{hq} sees {co} {c} ever", hq, f"CommandPicture.Subordinates.{i}.Strength", "==", c))
                for pg in ("Set", "PhaseLine", "ObjectiveSecure", "TaskFailed"):
                    probe.append(field(f"{hq} progress of {co} {pg}", hq, f"CommandPicture.Subordinates.{i}.Progress", "==", pg))
        write(s, "berlin", "probe", {"runFor": 1000, "golden": False, "expect": probe})
        return
    desc = ("What: on berlin a BLUE battalion HQ (a physical unit, no body: CompanyAttack on (700,500)) over THREE stochastic companies (90 men each, from the TKB composite) attacks a RED battalion HQ (CompanyDefend on (700,500), facing west) "
            "over three stochastic companies, the two lines 600 m apart. Judge: (1) every company is ordered by KIND - Red's three run the generic Defend, the BASE OF FIRE (Blue Company 2) the generic Attack with HoldBack (support by fire on its 250 m support position, CE-8302); no stochastic company is given a physical play; "
            "(2) the census agrees with the slot tables: a HoldArea objective round each HQ's three companies (300 m) needs 270 men = 3 x 90 slot-table men present, and reads Achieved; (3) each company's SITREP colour agrees with its slot table "
            "(the base of fire wiped out reads Black at its HQ; each Red company's colour lands in the band its slot table gives - Green under 10 of 90 down); (4) the fight runs to an end: the base of fire is lost (PROGRESS TaskFailed at its HQ) on its way in, inside the defenders' rifle reach (~290 m, short of its 250 m support position), so it never reports Set and the assault (Blue Company 1) is never committed. "
            "Fail: a physical play on a stochastic unit, a census that does not equal the slot tables, a colour that disagrees with them, or no end. Check: scripts/scenario-regression.sh agg-battalion-fight")
    expect = [
        task("the Blue HQ runs CompanyAttack", "Blue Battalion HQ", "CompanyAttack", before=10),
        task("the Red HQ runs CompanyDefend", "Red Battalion HQ", "CompanyDefend", before=10),
        task("the base of fire (Company 2) is ordered SupportByFire by KIND: the generic Attack play", "Blue Company 2", "StochasticUnitAttack", before=10),
        field("the census: the Blue HQ's three companies hold 270 slot-table men in the area", "Blue Battalion Census", "ObjectiveStatus.State", "==", "Achieved", before=20),
        field("the census: the Red HQ's three companies hold 270 slot-table men in the area", "Red Battalion Census", "ObjectiveStatus.State", "==", "Achieved", before=20),
        field("the HQ hears the base of fire is lost (PROGRESS TaskFailed): the fight has run to an end", "Blue Battalion HQ", "CommandPicture.Subordinates.1.Progress", "==", "TaskFailed", before=440),
        field("the base of fire's slot table agrees: every slot is down", "Blue Company 2", "UnitSlotTable.Slots.*.Health", "<", 0.5, atEnd=True, where=LIVE, elements=90),
        field("... and its SITREP colour at the HQ is Black", "Blue Battalion HQ", "CommandPicture.Subordinates.1.Strength", "==", "Black", atEnd=True),
        field("the assault company, never committed, has lost nobody: its SITREP colour is Green", "Blue Battalion HQ", "CommandPicture.Subordinates.0.Strength", "==", "Green", atEnd=True),
        never("... and its slot table agrees (no slot down)", {"on": "field", "who": "Blue Company 1", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE}),
        # R-8332: Red's three rows are judged by the colour BANDS (Green >= 90 % present = fewer than 10 of 90 down; Amber >= 70 % = fewer than 28 down) - the
        # SITREP and the slot table must land in the SAME band. The HQ's roster lists the companies 3, 2, 1 (measured: the only Green row, Subordinates.0, is the
        # only company with fewer than 10 down - company 3).
        field("Red's SITREP colours: company 3, untouched by the base of fire, reads Green", "Red Battalion HQ", "CommandPicture.Subordinates.0.Strength", "==", "Green", atEnd=True),
        never("... and its slot table agrees (fewer than 10 of 90 down: >= 90 % present)", {"on": "field", "who": "Red Company 3", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 10}),
        # R-8345 (re-authored 2026-11-17 after CE-8009): the base of fire now costs Red companies 1 and 2 five to nine men each - Green, not Amber
        field("Red's SITREP colours: company 2, in the base of fire's path, reads Green", "Red Battalion HQ", "CommandPicture.Subordinates.1.Strength", "==", "Green", atEnd=True),
        field("Red's SITREP colours: company 1 reads Green", "Red Battalion HQ", "CommandPicture.Subordinates.2.Strength", "==", "Green", atEnd=True),
        field("... and the fire told on company 2 (at least 5 down)", "Red Company 2", "UnitSlotTable.Slots.*.Health", "<", 0.5, atEnd=True, where=LIVE, elements=5),
        field("... and on company 1 (at least 5 down)", "Red Company 1", "UnitSlotTable.Slots.*.Health", "<", 0.5, atEnd=True, where=LIVE, elements=5),
        never("... and the slot table agrees with Green (fewer than 10 of 90 down) - company 1", {"on": "field", "who": "Red Company 1", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 10}),
        never("... and with Green - company 2", {"on": "field", "who": "Red Company 2", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 10}),
    ] + [never(f"no physical play on {c}", {"on": "task", "who": c, "is": ["PlatoonAttack", "PlatoonDefend", "CompanyAttack", "CompanyDefend", "PlatoonSupportByFire"]})
         for c in ("Blue Company 1", "Blue Company 2", "Blue Company 3", "Red Company 1", "Red Company 2", "Red Company 3")]
    checks = {"$doc": "§23.6 ST-4. 'A side broken' is the measured end: the Blue BASE OF FIRE (Blue Company 2, roster slot 1 - CompanyAttack's roles are slot 0 assault, 1 base of fire, 2 reserve; relabelled 2026-11-17, CE-8302) is destroyed within seconds of contact (all 90 slots down by 335 s at GAP 1500 / 335 s at 600), "
                      "its HQ hears TaskFailed and reads it Black. It dies at ~290 m (335 s), on its way to its 250 m support position (CE-8302 moved that position from 100 m of the posts; this run is unchanged, golden SAME), so it never reports Set and the assault (Blue Company 1, Subordinates.0) is never committed; the reserve (3) waits at its 400 m stand-off, out of reach, for the 900 s deadline. After agg-backend's CE-8009 round of 2026-11-17 (personal weapons at the measured 0.4 rounds a man a second, hits through the body's zones, R-8005 re-fitted) the base of fire costs Red companies 1 and 2 five to nine men each (Green; it was ten or more - Amber - on 2026-11-16, R-8332), company 3 none (R-8345). The base of fire is down 5 at 329 s, all 90 at 346 s; it never reports Set, so the assault is still never ordered. The census is the HoldArea objective of ObjectiveCensus (a stochastic unit under a physical subject's roster counts its Present). ROWS",
              "runFor": 450, "golden": {"every": 50}, "expect": expect}
    write(s, "berlin", desc, checks)


CENSUS_PROBES = [("270", 270), ("271", 271)]


def agg_mixed_company_attack():
    s = Scn("agg-mixed-company-attack", "a9000015")
    co = s.unit("Blue Company", BLUE, (240, 405), COMPANY)
    s.unit("Blue Stochastic Platoon", BLUE, (260, 405), PLATOON, co, aggregate={})
    s.physical_platoon("Blue 1st Platoon", BLUE, (240, 360), co)
    s.physical_platoon("Blue 2nd Platoon", BLUE, (210, 450), co)
    s.objective("Blue holds Objective 1", BLUE, "HoldArea", {
        "common": {"title": "Blue holds Objective 1", "subject": co},
        "area": {"circle": {"center": pt((425, 425)), "radius": 80}}, "minMen": 25, "maxEnemy": 0, "holdSeconds": 10})
    s.trigger("Blue Company attacks", BLUE, {"common": {"title": "Blue Company attacks"}, "when": {"$type": "ScenarioTime", "fromSeconds": 10},
              "do": [{"act": "Order", "args": {"target": co, "behavior": "CompanyAttack", "params": {"Objective1": pt((425, 425)), "Objective2": pt((645, 445)), "DeadlineSeconds": 300.0, "Radius": 35}}}]})
    desc = ("What: on demo-city a physical 'Blue Company' (CompanyAttack at 10 s: Objective 1 (425,425), then Objective 2 (645,445)) with THREE platoons: one STOCHASTIC (30 men from the TKB composite, no bodies; first on the roster, so the "
            "company gives it the assault) and two PHYSICAL (two squads of four men each: a base of fire and a reserve). No enemy: the routing is what is tested. Judge: every order reaches the right KIND of unit - the stochastic "
            "platoon runs the generic StochasticUnitAttack, the physical ones their physical plays (PlatoonSupportByFire, PlatoonDefend), never the other way round - and the attack completes: the stochastic platoon reaches Objective 1, "
            "and the HoldArea objective there (80 m, 25 men - only the stochastic platoon's 30 slot-table men can meet it) reads Achieved. Fail: a platoon given the wrong kind of play, or the objective never held. Check: scripts/scenario-regression.sh agg-mixed-company-attack")
    checks = {"$doc": "§23.6 mixed. No enemy on purpose: a stochastic observer cannot yet target a physical unit (F-10) and a physical weapon cannot hit a stochastic unit before the handover (ST-5, C-7), so a fight would judge the engine, "
                      "not the routing. CompanyAttack gives the assault to the first platoon on its roster, the base of fire to the second and holds the third: the stochastic platoon is created first so the physical play PlatoonAttack "
                      "is not exercised here (the cc-battalion scenarios are its rails) - the physical platoons' plays are pinned as measured.",
              "runFor": 230, "golden": False, "expect": [
        task("the company runs CompanyAttack", "Blue Company", "CompanyAttack", before=20),
        task("the stochastic platoon is ordered by KIND: the generic Attack play", "Blue Stochastic Platoon", "StochasticUnitAttack", before=100),
        task("the 1st physical platoon is the base of fire (its own physical play)", "Blue 1st Platoon", "PlatoonSupportByFire", before=20),
        task("the 2nd physical platoon holds (its own physical play)", "Blue 2nd Platoon", "PlatoonDefend", before=20),
        never("the stochastic platoon is never given a physical play", {"on": "task", "who": "Blue Stochastic Platoon", "is": ["PlatoonAttack", "PlatoonDefend", "PlatoonSupportByFire", "CompanyAttack"]}),
        never("a physical platoon is never given a stochastic play", {"on": "task", "who": "Blue 1st Platoon", "is": ["StochasticUnitAttack", "StochasticUnitDefend", "StochasticUnitMove"]}),
        never("... nor the other", {"on": "task", "who": "Blue 2nd Platoon", "is": ["StochasticUnitAttack", "StochasticUnitDefend", "StochasticUnitMove"]}),
        {"name": "the stochastic platoon reaches Objective 1", "on": "near", "who": "Blue Stochastic Platoon", "point": pt((425, 425)), "within": 80, "before": 225},
        field("the objective is held (25 men inside)", "Blue holds Objective 1", "ObjectiveStatus.State", "==", "Achieved", before=225),
    ]}
    write(s, "demo-city", desc, checks)


# ───────────────────────────────────────────── ST-5 ─────────────────────────────────────────────
# The resolution manager (agg-backend CE-8012 / CE-8013, wired in CgfLogicPack with our policy seam, CE-8300). ⚠ A split's children carry NO name
# (BusResolutionSwitch writes EntityInfo { ForceId } only - finding F-22), so a check addresses each by its NETWORK ID (WorldUnits.Resolve takes a
# number): the scenario's units are numbered from 1000 in file order, the children after them in slot order - measured per scenario, deterministic
# (the goldens repeat). Each child's identity is checked against SV-4's derivation, uuid5(<unit EntityGuid>, "child:<composite slot>") (R-8335).
import uuid


def child_guid(unit_guid, composite_slot):
    return str(uuid.uuid5(uuid.UUID(unit_guid), f"child:{composite_slot}"))


def child_name(unit_name, composite_slot):
    """F-22 (agg-backend R-4286): a split child is named <unit>/<composite slot> - stable across splits (R-8349: the rows name him, not his network id)."""
    return f"{unit_name}/{composite_slot}"


def child_names(unit_name, slots):
    """The `later` list: the children appear during the run, so the start-of-run cast check must not refuse their names."""
    return [child_name(unit_name, k) for k in slots]


def child_rows(unit_name, unit_guid, n, centre, within, after, label, slots=None, before=None):
    slots = slots or list(range(1, n + 1))     # the composite slots of the UP slots, in table order (a down slot gets no child)
    rows = []
    for k in range(n):
        rows.append(field(f"{label}: {child_name(unit_name, slots[k])} carries the stable key uuid5(unit, child:{slots[k]})", child_name(unit_name, slots[k]), "EntityGuid.Value", "==", child_guid(unit_guid, slots[k]), after=after))
    if centre is None:
        return rows
    for k in range(n):
        r = {"name": f"{label}: {child_name(unit_name, slots[k])} stands within {within} m of the unit's place", "on": "near", "who": child_name(unit_name, slots[k]), "point": pt(centre), "within": within, "after": after}
        if before is not None:
            r["before"] = before
        rows.append(r)
    return rows


def squad_guid(platoon_guid, s):
    """CE-8307 (R-8385): a platoon's split creates its SQUADS, keyed by their DIRECT index child:<s> and named <platoon>/<s>; a squad's men are
    keyed by their leaf in the squad, child:<n> under the squad's own guid, and named <platoon>/<s>/<n>."""
    return child_guid(platoon_guid, s)


def squad_men(platoon_name, s, leaves=range(1, 11)):
    return [child_name(child_name(platoon_name, s), n) for n in leaves]


def squad_rows(platoon_name, platoon_guid, after, label, squads=(1, 2, 3), before=None, centre=None, within=None):
    """One squad child per direct subordinate: its stable key, and (with centre) its place."""
    rows = []
    for k in squads:
        r = field(f"{label}: the squad {child_name(platoon_name, k)} carries the stable key uuid5(platoon, child:{k})", child_name(platoon_name, k), "EntityGuid.Value", "==", child_guid(platoon_guid, k), after=after)
        if before is not None:
            r["before"] = before
        rows.append(r)
        if centre is not None:
            n = {"name": f"{label}: {child_name(platoon_name, k)} stands within {within} m of the platoon's place", "on": "near", "who": child_name(platoon_name, k), "point": pt(centre), "within": within, "after": after}
            if before is not None:
                n["before"] = before
            rows.append(n)
    return rows


def men_rows(platoon_name, platoon_guid, s, leaves, after, label, before=None):
    rows = []
    for n in leaves:
        man = child_name(child_name(platoon_name, s), n)
        r = field(f"{label}: {man} carries the stable key uuid5(squad {s}, child:{n})", man, "EntityGuid.Value", "==", child_guid(squad_guid(platoon_guid, s), n), after=after)
        if before is not None:
            r["before"] = before
        rows.append(r)
    return rows


def resolution_policy(s, args):
    """A SetResolutionPolicy at t = 1 s (the author's path, R-8326; a ScenarioTime trigger from 0 s is armed at 0.27 s and never fires - measured) - on the World Environment."""
    return s.trigger("Resolution policy", BLUE, {"common": {"title": "Resolution policy"}, "when": {"$type": "ScenarioTime", "fromSeconds": 1},
                     "do": [{"act": "SetResolutionPolicy", "args": args}]})


def agg_split_on_contact(probe=False):
    s = Scn("agg-split-on-contact", "a9000016")
    U = "Blue Stochastic Platoon"
    pl = s.unit(U, BLUE, (300, 300), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 300)), "SearchRadius": 30}))
    sq = s.physical_squad("Red Patrol", RED, (300, 1000), None)
    s.ents[sq]["Behavior"] = {"Name": "SquadMarch", "Params": {"Objective": pt((300, 500)), "Shape": 0, "Spacing": 4}, "Origin": "Superior"}
    # F-23 (agg-backend R-4286): the merge line is now band + lead + margin (400 + 200 + 200 = 800 m), past the 600 m trigger, so the author's
    # mergeMargin 250 workaround (R-8336) is gone (R-8350): the scenario runs on the default bands
    # ⭐ CE-8307 (R-8382, re-judged): the split goes ONE level - the platoon becomes its 3 stochastic SQUADS, then each squad (the patrol inside its band too) its 10 men
    rows = [
        never("the platoon stays stochastic while the patrol is beyond the band (400 m + 200 m lead)", {"on": "field", "who": U, "path": "AggregateUnit.Splitting", "op": "!=", "value": "NotSplitting"}, before="band"),
        field("the patrol inside the band starts the process: Planning (a step of its own - not forced)", U, "AggregateUnit.Splitting", "==", "Planning", after="band", before="band+5"),
        field("... then Materialising (the children created, held)", U, "AggregateUnit.Splitting", "==", "Materialising", after="band", before="band+10"),
        field("the handover: the platoon is Physical", U, "AggregateUnit.Resolution", "==", "Physical", after="band", before="band+20"),
        field("CE-8307: its roster is its 3 squads - not 30 men", U, "UnitRoster.Count", "==", 3, after="handover", before="handover+3"),
        field("the slot table is untouched by the split (30 men up)", U, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=30, atEnd=True),
        never("the patrol never comes within the forced range (300 m): this is the planned path, not C-7", {"on": "near", "who": "Red Patrol Leader", "point": pt((300, 300)), "within": 300}),
        never("F-23: with the patrol parked 200 m off (inside the 800 m merge line, its men in contact) the platoon never merges back", {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Stochastic"}, after="handover+1"),
    ] + squad_rows(U, pl, "handover", "split", before="handover+3", centre=(300, 295), within=25) + [
        field(f"CE-8307: the squad {child_name(U, k)} is a STOCHASTIC unit at the handover", child_name(U, k), "AggregateUnit.Echelon", "==", 0, after="handover", before="handover+3") for k in (1, 2, 3)
    ] + [
        field(f"... {child_name(U, k)} carries its share: its 10 slots up", child_name(U, k), "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=10, after="handover", before="handover+3") for k in (1, 2, 3)
    ] + [
        field(f"the cascade (D-P7): the patrol inside its band too, {child_name(U, k)} splits into its men", child_name(U, k), "AggregateUnit.Resolution", "==", "Physical", after="handover", before="handover+15") for k in (1, 2, 3)
    ] + men_rows(U, pl, 1, range(1, 11), "handover", "squad 1's men")
    desc = ("What: on berlin a STOCHASTIC 'Blue Stochastic Platoon' (30 men in 3 squads from the TKB composite, defending at (300,300)) and a PHYSICAL 'Red Patrol' (four men) marching from 700 m north to 200 m. "
            "Judge: (1) while the patrol is beyond the resolution manager's band (400 m + 200 m lead) the platoon stays stochastic; (2) when the patrol enters it, the platoon de-aggregates as a PROCESS - "
            "Planning, Materialising, one-tick handover - and is Physical; (3) it splits ONE level (X-5, D-P7): into its 3 SQUADS, each a stochastic unit carrying its 10 slots, named <platoon>/<s> with the stable key "
            "uuid5(<platoon>, child:<s>), standing where the platoon stood, and the platoon's roster is the 3 squads; (4) the patrol being inside their band too, each squad then splits into its 10 men, "
            "named <platoon>/<s>/<n> with the key uuid5(<squad>, child:<n>); (5) with the patrol parked 200 m off the platoon never merges back (the merge line is 800 m). Fail: a split before the band, "
            "the platoon splitting straight into 30 men, a missing or re-keyed child, or a merge in contact. Check: scripts/scenario-regression.sh agg-split-on-contact")
    checks = {"$doc": "§23.6 ST-5 (§23.6g), re-judged for CE-8307 (DESIGN_Split_One_Level.md): the split creates the composite's DIRECT subordinates. Bands: ResolutionBands.Default (400 m + 200 m lead, forced 300 m, "
                      "merge beyond band + lead + 200 m = 800 m, F-23). The squads stand at the centroid of their men's fallback posts (D-P5: rows of 12 across the heading). A rifle platoon (305) has no vehicles. "
                      "Children by NAME (F-22, R-8349; R-8385): squads <platoon>/<s>, men <platoon>/<s>/<n>, listed in `later`.",
              "runFor": 150, "golden": {"every": 25}, "later": child_names(U, range(1, 4)) + [m for k in (1, 2, 3) for m in squad_men(U, k)],
              "anchors": {"band": {"on": "near", "who": "*Red Patrol", "point": pt((300, 300)), "within": 600},
                          "handover": {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}},
              "expect": rows}
    if probe:
        probe_ids(checks, 1000, 1045, [field("phys", U, "AggregateUnit.Resolution", "==", "Physical")])
    write(s, "berlin", desc, checks)


def agg_platoon_splits_into_squads(probe=False):
    """CE-8307 (R-8382 ... R-8386): a stochastic platoon splits into its 3 stochastic SQUADS, then each squad into its men as the enemy closes."""
    s = Scn("agg-platoon-splits-into-squads", "a9000024")
    U = "Blue Stochastic Platoon"
    pl = s.unit(U, BLUE, (300, 300), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 300)), "SearchRadius": 30}))
    sq = s.physical_squad("Red Patrol", RED, (300, 1000), None)
    s.ents[sq]["Behavior"] = {"Name": "SquadMarch", "Params": {"Objective": pt((300, 450)), "Shape": 0, "Spacing": 4}, "Origin": "Superior"}
    S = [child_name(U, k) for k in (1, 2, 3)]
    rows = [
        field("the patrol inside the band: the platoon hands over - Physical", U, "AggregateUnit.Resolution", "==", "Physical", after="band", before="band+20"),
        field("ONE level (X-5): its roster is its 3 SQUADS", U, "UnitRoster.Count", "==", 3, after="handover", before="handover+3"),
        task("X-2 for a platoon (R-8386): its Defend order re-resolves to PlatoonDefend", U, "PlatoonDefend", after="handover", before="handover+5"),
        field("the platoon's own table is untouched by the split and the cascade (30 men up)", U, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=30, atEnd=True),
    ] + squad_rows(U, pl, "handover", "split", before="handover+3") + [
        r for k, n in zip((1, 2, 3), S) for r in (
            field(f"{n} is born a STOCHASTIC squad (echelon 0)", n, "AggregateUnit.Echelon", "==", 0, after="handover", before="handover+3"),
            field(f"... {n} carries its share - 10 slots, every one up (none lost, none invented)", n, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=10, after="handover", before="handover+3"),
            never(f"... {n} never more than 10 slots", {"on": "field", "who": n, "path": "UnitSlotTable.Slots.*.Health", "op": ">=", "value": 0.5, "where": LIVE, "elements": 11}),
            field(f"the cascade (D-P7): {n} splits into its men as the patrol closes", n, "AggregateUnit.Resolution", "==", "Physical", after="handover", before="handover+20"),
            field(f"... and {n}'s roster is its 10 men", n, "UnitRoster.Count", "==", 10, after="handover", before="handover+25"),
        )
    ] + [r for k in (1, 2, 3) for r in men_rows(U, pl, k, (1, 10), "handover", f"squad {k}'s men")]
    desc = ("What: on berlin a STOCHASTIC 'Blue Stochastic Platoon' (TKB 305: 3 squads of 10, defending at (300,300)) and a PHYSICAL 'Red Patrol' (four men) marching from 700 m north to 150 m. "
            "Judge (CE-8307, X-5 / D-P7 'one level at a time'): (1) when the patrol enters the band the platoon hands over and becomes a physical COMMAND unit whose roster is its 3 squads - never 30 men; "
            "(2) each squad is a stochastic unit named <platoon>/<s> with the stable key uuid5(<platoon>, child:<s>), carrying exactly its 10 slots (the counts conserved); (3) the platoon's Defend order "
            "re-resolves to PlatoonDefend (it now commands squads); (4) as the patrol closes, each squad splits in turn into its men, named <platoon>/<s>/<n> with the key uuid5(<squad>, child:<n>), its roster "
            "the 10 men; (5) the platoon's own slot table keeps 30 men up. Fail: a flat split into 30 men, a squad with the wrong slots, a missing or re-keyed child. Check: scripts/scenario-regression.sh agg-platoon-splits-into-squads")
    checks = {"$doc": "CE-8307 (DESIGN_Split_One_Level.md §7). Bands: ResolutionBands.Default. The squads are created HELD (Constructing) and released at the platoon's handover; the manager visits them on its next "
                      "steps with the same bands, so with the patrol inside 600 m they split at once (D-P7 'quickly'). Children by NAME (F-22, R-8385): squads <platoon>/<s>, men <platoon>/<s>/<n>; "
                      "the rows pin the first and last man of each squad.",
              "runFor": 90, "golden": {"every": 15}, "later": S + [m for k in (1, 2, 3) for m in squad_men(U, k)],
              "anchors": {"band": {"on": "near", "who": "*Red Patrol", "point": pt((300, 300)), "within": 600},
                          "handover": {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}},
              "expect": rows}
    if probe:
        checks["golden"] = False
    write(s, "berlin", desc, checks)


def agg_platoon_merges_back(probe=False, PIN=60):
    """CE-8307 (R-8383, R-8390): a platoon split down to men (squads, then men) is pinned back stochastic: the whole tree merges in ONE step and the
    platoon's table keeps every loss it was authored with."""
    s = Scn("agg-platoon-merges-back", "a9000025")
    U = "Blue Stochastic Platoon"
    pl = s.unit(U, BLUE, (300, 300), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 300)), "SearchRadius": 30}),
                extra={"UnitSlotTable": merged_slot_table()})
    s.unit("Blue Spare Squad", BLUE, (300, -9000), SQUAD, aggregate={})   # a FILLED unit far away: teaches the personal weapons (F-16)
    sq = s.physical_squad("Red Patrol", RED, (300, 800), None)
    s.ents[sq]["Behavior"] = {"Name": "SquadMarch", "Params": {"Objective": pt((300, 750)), "Shape": 0, "Spacing": 4}, "Origin": "Superior"}   # parks 450 m off: in the band, out of rifle reach
    pin = {"name": "the instructor pins it Stochastic", "at": PIN, "set": {"who": U, "component": "AggregateUnit", "patch": {"Pin": "PinStochastic"}}}
    MEN1 = [1, 2, 3, 7, 8, 9, 10]          # squad 1: leaves 4-6 are dead (authored) - never created
    rows = [
        field("the patrol inside the band: the platoon splits into its squads", U, "AggregateUnit.Resolution", "==", "Physical", before=15),
        field("... its roster: 3 squads", U, "UnitRoster.Count", "==", 3, before=20),
        field("squad 1 carries its LOSSES: 3 of its 10 slots down", child_name(U, 1), "UnitSlotTable.Slots.*.Health", "<", 0.5, where=LIVE, elements=3, before=20),
        field("... and its hurt man (leaf 10, health 12.5)", child_name(U, 1), "UnitSlotTable.Slots.*.Health", "==", 12.5, where=LIVE, elements=1, before=20),
        field("squad 2 carries ITS hurt man (the platoon's leaf 11 is its leaf 1)", child_name(U, 2), "UnitSlotTable.Slots.0.Health", "==", 12.5, before=20),
        field("each squad then splits into its men (squad 1: 7 - the dead are never created)", child_name(U, 1), "UnitRoster.Count", "==", 7, before=40),
        field(f"the instructor pins the platoon Stochastic (t = {PIN} s): the WHOLE tree merges in one step", U, "AggregateUnit.Resolution", "==", "Stochastic", after=PIN, before=PIN + 3),
        never("... and no squad survives the merge", {"on": "field", "who": child_name(U, 1), "path": "AggregateUnit.Echelon", "op": "==", "value": 0}, after=PIN + 3),
        field("the merge kept every loss: exactly 3 slots down", U, "UnitSlotTable.Slots.*.Health", "<", 0.5, where=LIVE, elements=3, atEnd=True),
        never("... never a 4th (no man lost by the two-level round trip)", {"on": "field", "who": U, "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 4}),
        field("... the 2 hurt men come back hurt (12.5 of 25)", U, "UnitSlotTable.Slots.*.Health", "==", 12.5, where=LIVE, elements=2, atEnd=True),
        field("... and 27 men up", U, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=27, atEnd=True),
        never("... never 28 up (a dead man is never re-created)", {"on": "field", "who": U, "path": "UnitSlotTable.Slots.*.Health", "op": ">=", "value": 0.5, "where": LIVE, "elements": 28}),
    ] + squad_rows(U, pl, 0, "split") + men_rows(U, pl, 1, MEN1, 0, "squad 1's men")
    desc = ("What: on berlin a STOCHASTIC 'Blue Stochastic Platoon' (3 squads of 10; an authored table with LOSSES: the platoon's leaves 4-6 dead, 10-11 hurt at half health) holds (300,300); a PHYSICAL "
            "'Red Patrol' parks 450 m north - inside the band, outside rifle reach. Judge (CE-8307): (1) the platoon splits into its 3 squads, each carrying its share of the losses - squad 1 its 3 dead and its hurt "
            "man, squad 2 its hurt man as its own leaf 1; (2) each squad splits into its men, the dead never created (squad 1: 7 men); (3) the instructor pins the platoon Stochastic at "
            f"{PIN} s and the WHOLE tree merges back in one step - men into squads, squads into the platoon; (4) the platoon's table keeps every loss: exactly 3 down, the 2 hurt still at 12.5, 27 up. "
            "Fail: a loss healed or invented, a squad left over, a man re-created. Check: scripts/scenario-regression.sh agg-platoon-merges-back")
    checks = {"$doc": "CE-8307 (DESIGN_Split_One_Level.md §5, R-8390): the instructor's pin (SV-14) merges a split child (and its men) before the parent, in one manager step; the automatic leaves-first "
                      "merge is agg-merge-back's. The share renumbers a squad's slots to its own leaves (R-8383). ⚠ The men stand 450 m from the patrol (out of rifle reach) so no loss is expected while physical; "
                      "the rows judge the losses (3 down, never a 4th, 2 hurt, 27 up).",
              "runFor": PIN + 15, "golden": {"every": 15}, "do": [pin],
              "later": [child_name(U, k) for k in (1, 2, 3)] + squad_men(U, 1, MEN1) + squad_men(U, 2) + squad_men(U, 3), "expect": rows}
    if probe:
        checks["golden"] = False
    write(s, "berlin", desc, checks)


def probe_ids(checks, lo=1000, hi=1080, extra=()):
    """--probe: which network ids are a split's children (measured once, then the rows are written with them)."""
    checks["golden"] = False
    checks["expect"] = [field(f"id {k} is a child", str(k), "AggregateChild.Held", "==", 0) for k in range(lo, hi)] + list(extra)


def agg_squad_holds_house(probe=False):
    s = Scn("agg-squad-holds-house", "a9000017")
    U = "Blue Stochastic Squad"
    house = (125, 161)                       # cqb-village's "OBJ" (house-3f), the building pc-defend-house's platoon holds
    sq = s.unit(U, BLUE, house, SQUAD, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt(house), "SearchRadius": 10}))
    s.physical_squad("Red Squad", RED, (house[0], house[1] + 480), None)     # inside the band (600 m), outside the forced range (300 m)
    n = 10
    MEN = "*" + U + "/"                      # every child (F-22: <squad>/<composite slot>); the squad's own name has no '/'
    FP = {"x": (115, 125), "y": (153, 161)}  # OBJ's footprint: house-3f (10 x 8 m) placed by its SW corner at (125, 161), turned 180 deg
    def men(name, path, op, value, q, **kw):
        r = {"name": name, "on": "field", "who": MEN, "path": path, "op": op, "value": value, "quantifier": q}
        r.update(kw)
        return r
    at_split = {"after": "split", "before": "split+0.5"}
    rows = [
        field("the squad in the house de-aggregates: Physical", U, "AggregateUnit.Resolution", "==", "Physical", before=20),
        field("D-P3: the squad's own plan is Ready before its men are created (the manager waited for it)", U, "PlannedPosts.Ready", "==", 1, before="split"),
        field("... with a post entry for each of the 10 live slots", U, "PlannedPosts.Count", "==", n, before="split"),
        field("its slot table is whole (10 men up)", U, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=n, atEnd=True),
    ] + child_rows(U, sq, n, None, 0, "split", "house", before="split+1") + [
        men(f"D-P3: at the handover EVERY man stands inside the house (x >= {FP['x'][0]} - 0.5)", "SimTransform.Position.0", ">=", FP["x"][0] - 0.5, "all", **at_split),
        men(f"... (x <= {FP['x'][1]} + 0.5)", "SimTransform.Position.0", "<=", FP["x"][1] + 0.5, "all", **at_split),
        men(f"... (y >= {FP['y'][0]} - 0.5)", "SimTransform.Position.1", ">=", FP["y"][0] - 0.5, "all", **at_split),
        men(f"... (y <= {FP['y'][1]} + 0.5) - not on the fallback line, which runs 2 m apart across the heading out to x 134", "SimTransform.Position.1", "<=", FP["y"][1] + 0.5, "all", **at_split),
        men("D-P3: at least 5 men are posted on the upper storey (z >= 2.5)", "SimTransform.Position.2", ">=", 2.5, "atLeast:5", **at_split),
        men("D-P3: at least 5 men man the wall that faces the enemy (the north wall, y >= 160: its windows and door)", "SimTransform.Position.1", ">=", 160, "atLeast:5", **at_split),
        task("X-2 (X2-F): within 3 s of the handover the squad runs SquadDefend - its Defend order re-resolved at Physical", U, "SquadDefend", after="split", before="split+3"),
        men("X-2 (DP3-D 2): the men START on their planned posts - 3 s after the handover every man is still inside the house (no walk to a surveyed post)", "SimTransform.Position.1", "<=", FP["y"][1] + 0.5, "all", after="split+3", before="split+3.5"),
        men("they hold the position: at 20 s every man is still at the house (y <= 166 - within 5 m of its north wall: under fire SquadDefend displaces men, F-X2-6)", "SimTransform.Position.1", "<=", FP["y"][1] + 5, "all", after=20, before=20.5),
        men("... (x <= 125.5)", "SimTransform.Position.0", "<=", FP["x"][1] + 0.5, "all", after=20, before=20.5),
    ]
    desc = ("What: on cqb-village a STOCHASTIC 'Blue Stochastic Squad' (10 men from the TKB composite) holds the village's three-storey house 'OBJ' (StochasticUnitDefend); a PHYSICAL 'Red Squad' "
            "stands 480 m north - inside the resolution manager's band, so the squad de-aggregates. Judge (D-P3, end to end): while the squad is Planning, SquadDefend's OWN plan runs over its slot table and asks the "
            "house's window / door / roof posts; the manager waits for that plan, then creates the 10 men (each with its stable key, named <squad>/<slot>) ON those posts: every man inside the house, "
            "at least 5 on the upper storey, at least 5 at the wall facing the enemy; and they still hold the house 20 s later. Fail: a man on the fallback line outside the house, the men created "
            "before the plan is Ready, or a missing / re-keyed man. Check: scripts/scenario-regression.sh agg-squad-holds-house")
    checks = {"$doc": "§23.6 ST-5 (§23.6g), D-P3 LIVE end to end (DESIGN_DP3_Planned_Posts.md §9): agg-behaviors' UnitPostPlanningSystem writes PlannedPosts (138) during Planning; agg-backend's "
                      "manager (DP3-B) waits for Ready (or 10 s) and Materialise puts the k-th live slot on post k. OBJ = house-3f (10 x 8 m) placed by its SW corner at (125,161) turned 180 deg "
                      "=> footprint x 115-125, y 153-161, storeys at z 0 / 3 / roof 6. Measured at the handover (2.25 s; plan Ready at 0.25 s): all 10 men 0.5 m inside an outer wall, 5 at z 3, 5 at z 0, 5 on the "
                      "north (enemy-facing) wall; no man on the roof (the plan's survey picked none). The rows read the men, not PlannedPosts' arrays (a fixed buffer is not in the entity dump). "
                      "Children by NAME <squad>/<composite slot> (F-22, R-8349), listed in `later`. Supersedes the fallback-line rows (R-8337 -> R-8351).",
              "runFor": 40, "golden": {"every": 10}, "later": child_names(U, range(1, n + 1)), "expect": rows,
              "anchors": {"split": {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}}}
    if probe:
        probe_ids(checks)
    write(s, "cqb-village", desc, checks)


def merged_slot_table():
    """A 30-man platoon's table with LOSSES: slots 4-6 down (dead), 10-11 hurt (half health) - authored so the merge's honesty is checkable."""
    t = slot_table(30, 200)
    for k in (3, 4, 5):
        t["Slots"][k]["Health"] = 0.0
    for k in (9, 10):
        t["Slots"][k]["Health"] = 12.5
    return t


def agg_merge_back(probe=False, PIN=250):
    s = Scn("agg-merge-back", "a9000018")
    U = "Blue Stochastic Platoon"
    pl = s.unit(U, BLUE, (300, 300), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 300)), "SearchRadius": 30}),
                extra={"UnitSlotTable": merged_slot_table()})
    s.unit("Blue Spare Squad", BLUE, (300, -9000), SQUAD, aggregate={})   # a FILLED unit far away: teaches the personal weapons (F-16)
    sq = s.physical_squad("Red Patrol", RED, (300, 850), None)            # 550 m: inside the 600 m trigger, outside the forced range
    s.ents[sq]["Behavior"] = {"Name": "SquadMarch", "Params": {"Objective": pt((300, 1700)), "Shape": 0, "Spacing": 4}, "Origin": "Superior"}
    # F-23 (agg-backend R-4286): the DEFAULT bands now merge beyond band + lead + margin (800 m) after 30 s unobserved and out of contact - the author's
    # policy (quiet 20 s, margin 250 m, R-8336) is gone (R-8350); the patrol starts 550 m off (was 450 m) so the walk to 800 m stays short
    n = 27
    UP = [k + 1 for k in range(30) if k not in (3, 4, 5)]      # the composite slots that get a child (4-6 are dead)
    cut = lambda name, at: {"name": name, "at": at, "set": {"who": U, "component": "AggregateUnit", "patch": {"Pin": "PinPhysical"}}}
    rows = [
        field("contact: the platoon splits (the patrol 550 m off)", U, "AggregateUnit.Resolution", "==", "Physical", before=15),
        field("merged back: contact ends (the patrol walks away past the merge line, 800 m, and 30 s pass unobserved), the men merge", U, "AggregateUnit.Resolution", "==", "Stochastic", after=20, before=PIN - 5),
        never("F-23: not before the patrol is past the 800 m merge line (band 400 + lead 200 + margin 200)", {"on": "near", "who": "*Red Patrol", "point": pt((300, 300)), "within": 800},
              after="merged back: contact ends (the patrol walks away past the merge line, 800 m, and 30 s pass unobserved), the men merge"),
        field("the merge kept every loss (the table at the end is the merged one - a split never writes it): the 3 dead slots are down", U, "UnitSlotTable.Slots.*.Health", "<", 0.5, where=LIVE, elements=3, atEnd=True),
        never("... never a 4th (no man lost or invented by the round trip)", {"on": "field", "who": U, "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 4}),
        field("... and the 2 hurt men come back hurt (health 12.5 of 25)", U, "UnitSlotTable.Slots.*.Health", "==", 12.5, where=LIVE, elements=2, atEnd=True),
        field("... and 27 men up: none lost, none invented by the round trip", U, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=27, atEnd=True),
        never("... never 28 up (a dead man is never re-created)", {"on": "field", "who": U, "path": "UnitSlotTable.Slots.*.Health", "op": ">=", "value": 0.5, "where": LIVE, "elements": 28}),
        field(f"the instructor pins it Physical (t = {PIN} s): it splits AGAIN at once", U, "AggregateUnit.Resolution", "==", "Physical", after=PIN, before=PIN + 5),
        never("no second split before the pin: the merge lies past the split trigger (F-23)", {"on": "field", "who": U, "path": "AggregateUnit.Splitting", "op": "==", "value": "Materialising"}, after=30, before=PIN),
    ] + squad_rows(U, pl, 0, "first split") + squad_rows(U, pl, PIN, "the pin's split: the SAME key") + [
        field("CE-8307: the platoon splits ONE level - its roster is its 3 squads", U, "UnitRoster.Count", "==", 3, before=20),
        field("... squad 1 carries its 3 dead", child_name(U, 1), "UnitSlotTable.Slots.*.Health", "<", 0.5, where=LIVE, elements=3, before=20),
    ]
    desc = ("What: on berlin a STOCHASTIC 'Blue Stochastic Platoon' (30 slots, 3 dead and 2 hurt - an authored table) holds (300,300); a PHYSICAL 'Red Patrol' 550 m north marches away north. "
            "The default resolution bands: split inside 600 m, merge beyond 800 m after 30 s unobserved with no man in contact. Judge: (1) the platoon splits while the patrol is near "
            "(CE-8307: ONE level - its 3 squads <platoon>/<s>, each carrying its share of the losses; the patrol being inside their band too, each squad then splits into its men <platoon>/<s>/<n>, the dead not created); (2) once the patrol is past 800 m and 30 s are quiet the men merge into their squads and then - leaves first - the squads into the platoon: Stochastic - and never while the patrol is inside 800 m; (3) the slot table keeps every loss and every man: exactly 3 down (never a 4th), 27 up, the 2 hurt at "
            f"half health; (4) the instructor's pin (PinPhysical at {PIN} s) splits it again and the SAME 3 squads come back (uuid5(<platoon>, child:<s>)). Fail: a merge in contact or inside 800 m, a loss healed or "
            "invented, or a different key on the second split. Check: scripts/scenario-regression.sh agg-merge-back")
    checks = {"$doc": "§23.6 ST-5 (§23.6g). Merge (§16b, D-P7, F-23): beyond band + lead + margin (400 + 200 + 200 = 800 m), quiet for 30 s, UNOBSERVED and with no man in CONTACT; then BusResolutionSwitch.Merge sums each child "
                      "back (X-4) - a slot with no child is down. No resolution policy (the R-8336 workaround is gone, R-8350). The second split is the instructor's pin, a do-set of "
                      "AggregateUnit.Pin = PinPhysical (SV-14). Children by NAME <platoon>/<composite slot> (F-22, R-8349), listed in `later`; the same names come back on the second split. ⚠ Measured (§23.6d): a few whole men come back "
                      "slightly hurt from their time as soldiers; the rows judge the losses (3 down, never a 4th, 27 up, the 2 hurt still hurt), not exact health.",
              "runFor": PIN + 30, "golden": {"every": 40}, "do": [cut("the instructor pins it Physical", PIN)], "later": child_names(U, range(1, 4)) + squad_men(U, 1, [1, 2, 3, 7, 8, 9, 10]) + squad_men(U, 2) + squad_men(U, 3),
              "expect": rows}
    if probe:
        probe_ids(checks, 1000, 1000, [field(f"phys after {a}", U, "AggregateUnit.Resolution", "==", "Physical", after=a) for a in (0, 10, 60, 120, 130, 160, 200, 240)]
                  + [field(f"stoch after {a}", U, "AggregateUnit.Resolution", "==", "Stochastic", after=a) for a in (5, 60, 120, 130, 160, 200, 240, 255)]
                  + [{"name": f"patrol within {r} after 100", "on": "near", "who": "*Red Patrol", "point": pt((300, 300)), "within": r, "after": 100} for r in (600, 650, 700)]
                  + [field(f"id {k} child after {a}", str(k), "AggregateChild.Held", "==", 0, after=a) for a in (0, 130, 250) for k in (1009, 1035, 1036, 1062, 1063, 1089)])
    write(s, "berlin", desc, checks)
    return pl


def agg_forced_handover(probe=False):
    s = Scn("agg-forced-handover", "a9000019")
    rows = []
    for tag, rng, y in (("Forced", 250, 300), ("Planned", 450, 7300)):
        U = f"Blue Platoon {tag}"
        s.unit(U, BLUE, (300, y), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, y)), "SearchRadius": 30}))
        s.physical_squad(f"Red Squad {tag}", RED, (300, y + rng), None)
    U, V = "Blue Platoon Forced", "Blue Platoon Planned"
    rows = [
        field("C-7: a physical weapon 250 m off (inside the forced range, 300 m) - the platoon hands over", U, "AggregateUnit.Resolution", "==", "Physical", before=5),
        never("... with NO planning time: the Planning stage is never seen (straight to Materialising)", {"on": "field", "who": U, "path": "AggregateUnit.Splitting", "op": "==", "value": "Planning"}),
        never("... and no stochastic step fights before it: it fires no stochastic line", {"on": "field", "who": U, "path": "FireAllocation.Count", "op": ">", "value": 0}),
        never("... nor loses a man to one", {"on": "field", "who": U, "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 24.9, "where": LIVE}),
        field("the control: a physical enemy 450 m off (inside the band, outside the forced range) - the process plans first (Planning seen)", V, "AggregateUnit.Splitting", "==", "Planning", before=5),
        field("... and hands over too", V, "AggregateUnit.Resolution", "==", "Physical", before=20),
    ] + [r for k in (1, 2, 3) for r in (
        # ⭐ CE-8307 (R-8393, review F2): the forced split materialises the WHOLE subtree held - squads, then their men - and hands every level over in one tick
        field(f"CE-8307 C-7: the squad {child_name(U, k)} hands over Physical in the SAME tick as the platoon", child_name(U, k), "AggregateUnit.Resolution", "==", "Physical", after="forced", before="forced+0.5"),
        never(f"... {child_name(U, k)} never fights stochastically (no fire line)", {"on": "field", "who": child_name(U, k), "path": "FireAllocation.Count", "op": ">", "value": 0}),
        never(f"... {child_name(U, k)} never loses a man to a stochastic step", {"on": "field", "who": child_name(U, k), "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 24.9, "where": LIVE}),
    )]
    desc = ("What: on berlin two identical STOCHASTIC platoons (30 men, defending), 7 km apart. A PHYSICAL 'Red Squad Forced' (four armed men) stands 250 m from the first - inside C-7's forced range (300 m); "
            "a 'Red Squad Planned' stands 450 m from the second - inside the band, outside the forced range. Judge: (1) the first platoon hands over at once, skipping the planning time (its Planning stage is never "
            "seen), before any stochastic step fights it (no stochastic fire line, no slot hurt); (2) the control platoon goes through Planning first, then hands over; (3) CE-8307: the forced platoon splits ONE level at a time but all at once - its 3 squads "
            "and their men are created held and hand over in the SAME tick as the platoon, so no squad ever fights stochastically in contact. Fail: a stochastic step on the forced one, "
            "the forced one planning, or a squad handed over stochastic. Check: scripts/scenario-regression.sh agg-forced-handover")
    checks = {"$doc": "§23.6 ST-5, C-7: ResolutionManagerSystem.Decide - an enemy within ForcedRange (300 m) skips straight from NotSplitting to Materialising in the same step (goto case Planning), so Planning "
                      "is never at a step boundary; with the enemy in the band but not forced the stage is held one step. The stochastic fire phase targets only stochastic units, so 'before any stochastic "
                      "step fights it' is encoded as: the platoon writes no fire line and loses no health while stochastic.",
              "runFor": 30, "golden": {"every": 10}, "expect": rows, "later": [child_name(U, k) for k in (1, 2, 3)] + [m for k in (1, 2, 3) for m in squad_men(U, k)]
                  + [child_name(V, k) for k in (1, 2, 3)] + [m for k in (1, 2, 3) for m in squad_men(V, k)],
              "anchors": {"forced": {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}}}
    if probe:
        checks["golden"] = False
    write(s, "berlin", desc, checks)


def agg_defend_fires(probe=False):
    """F-14 (R-8338): a DEFENDING stochastic squad engages an enemy that approaches it - and not before the enemy is inside its weapons' reach."""
    s = Scn("agg-defend-fires", "a9000020")
    co = s.unit("Blue Company", BLUE, (1000, 150), COMPANY, behavior=("CompanyDefend", {"Position": pt((1000, 200)), "Threat": pt((1000, 700))}))
    s.unit("Blue Stochastic Squad", BLUE, (1000, 200), SQUAD, co, aggregate={})
    s.unit("Red Stochastic Squad", RED, (1000, 700), SQUAD, aggregate={}, behavior=("StochasticUnitAttack", {"Position": pt((1000, 200))}))
    if probe:   # the measuring rows that gave the $doc's times
        rows = []
        for who in ("Blue Stochastic Squad", "Red Stochastic Squad"):
            rows += [field(f"{who} fires", who, "FireAllocation.Count", ">", 0), field(f"{who} reported", who, "UnitDetections.Pairs.0.Reported", "==", 1),
                     field(f"{who} slot down", who, "UnitSlotTable.Slots.*.Health", "<", 0.5, where=LIVE)]
            for y in (650, 600, 550, 500, 450, 400, 350):
                rows.append(field(f"{who} y<{y}", who, "SimTransform.Position.1", "<", y))
            for y in (190, 210, 250):
                rows.append(field(f"{who} y>{y}", who, "SimTransform.Position.1", ">", y))
            for n in (3, 6):
                rows.append(field(f"{who} {n} down", who, "UnitSlotTable.Slots.*.Health", "<", 0.5, where=LIVE, elements=n))
            for t in ("StochasticUnitDefend", "StochasticUnitAttack", "StochasticUnitBreakContact", "StochasticUnitHold"):
                rows.append(task(f"{who} runs {t}", who, t))
            rows.append(field(f"{who} holdings", who, "UnitHoldings.Count", ">", 0))
        write(s, "berlin", "measure", {"runFor": 400, "golden": False, "expect": rows})
        return
    B, R = "Blue Stochastic Squad", "Red Stochastic Squad"
    desc = ("What: on berlin a physical 'Blue Company' (CompanyDefend at (1000,200), facing north) over ONE stochastic 'Blue Stochastic Squad' (from the TKB composite) standing on that position; "
            "a stochastic 'Red Stochastic Squad' 500 m north ATTACKS it (StochasticUnitAttack on (1000,200)) and walks in. Judge: (1) the squad is ordered Defend by KIND and holds its position (it never "
            "breaks contact, never leaves); (2) it does NOT fire while the enemy is beyond its riflemen's reach (the M4's 300 m, the killer-victim table's MaxRange) - it has seen it long before; "
            "(3) it fires within seconds of the enemy coming inside 300 m (Red's centre past y 500) and the fire tells: a Red slot goes down. Fail: no fire after the enemy closes (finding F-14), "
            "fire before it is in reach, or the defender leaving its position. Check: scripts/scenario-regression.sh agg-defend-fires")
    checks = {"$doc": "§23.6 / F-14 (R-8338, R-8339). F-14 measured: a defending stochastic unit fires whenever a reported contact is inside its weapons' MaxRange (StochasticFirePhase.Line, "
                      "KvEntry.PHitAt = 0 past MaxRange; the rifleman's M4 Range 300 in NedTkbCatalog) - the squad that 'never fired' had been ordered to a position 400 m from the contact. "
                      "The anchor 'in reach' = Red's centre past y 500 (300 m from the squad at y 200; both on x 1000). Measured: Blue sees Red at 2.25 s (500 m), Red crosses y 500 at 211.75 s, "
                      "Blue's first fire line at 212.25 s, the first Red slot down at 212.25 s.",
              "runFor": 240, "golden": {"every": 30}, "anchors": {"in reach": {"on": "field", "who": R, "path": "SimTransform.Position.1", "op": "<", "value": 500}},
              "expect": [
        task("the company runs CompanyDefend", "Blue Company", "CompanyDefend", before=10),
        task("the squad is ordered Defend by KIND: it runs the generic Defend play", B, "StochasticUnitDefend", before=10),
        field("it sees the attacker long before it is in reach (a reported contact)", B, "UnitDetections.Pairs.0.Reported", "==", 1, before=30),
        never("it does not fire while the attacker is beyond its riflemen's 300 m", {"on": "field", "who": B, "path": "FireAllocation.Count", "op": ">", "value": 0}, before="in reach"),
        field("it FIRES once the attacker is inside 300 m (F-14)", B, "FireAllocation.Count", ">", 0, after="in reach", before="in reach+5"),
        field("... and its fire tells: a Red slot goes down", R, "UnitSlotTable.Slots.*.Health", "<", 0.5, after="in reach", before="in reach+10", where=LIVE),
        never("the defender holds: it never breaks contact", {"on": "task", "who": B, "is": ["StochasticUnitBreakContact", "PlatoonDefend", "SquadDefend"]}),
        never("... and never leaves its position (stays within 10 m of y 200)", {"on": "field", "who": B, "path": "SimTransform.Position.1", "op": ">", "value": 210}),
    ]}
    write(s, "berlin", desc, checks)


# ───────────────────────────────────────────── X-2 (order re-resolution on a switch) ─────────────────────────────────────────────

def x2_squad(s, name, at, defend_at, commander=None):
    """A stochastic SQUAD (type 302: a leader and 9 riflemen from the TKB composite) holding an authored Defend (the generic play, origin Superior)."""
    return s.unit(name, BLUE, at, SQUAD, commander, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt(defend_at), "SearchRadius": 30}))


def agg_split_keeps_order(probe=False):
    """X-2 (DESIGN_X2_Order_Reresolution.md §8): a stochastic squad under a Defend that splits keeps its order - SquadDefend, its men rostered."""
    s = Scn("agg-split-keeps-order", "a9000021")
    U = "Blue Stochastic Squad"
    x2_squad(s, U, (300, 300), (300, 300))
    s.physical_squad("Red Squad", RED, (300, 780), None)       # 480 m: inside the band (600 m), outside the forced range (300 m) and the rifles' 300 m
    MEN = "*" + U + "/"
    rows = [
        field("the patrol inside the band: the squad de-aggregates - Physical", U, "AggregateUnit.Resolution", "==", "Physical", before=20),
        task("X-2: within 3 s of the handover the squad runs SquadDefend (its Defend order, re-resolved at Physical)", U, "SquadDefend", after="split", before="split+3"),
        never("... and never the stochastic StochasticUnitDefend again", {"on": "task", "who": U, "is": "StochasticUnitDefend"}, after="split+3"),
        field("X2-B: its 10 men are in its roster (the handover assigned them)", U, "UnitRoster.Count", "==", 10, after="split", before="split+3"),
        {"name": "... every man is the squad's subordinate (a designation set: the leader SquadLeader, the rest Wingman)", "on": "field", "who": MEN, "path": "UnitSubordinate.Designation",
         "op": "!=", "value": "Undefined", "quantifier": "all", "after": "split+1", "before": "split+3"},
        {"name": "... the leader is slot 1's man", "on": "field", "who": U + "/1", "path": "UnitSubordinate.Designation", "op": "==", "value": "SquadLeader", "after": "split+1", "before": "split+3"},
    ]
    desc = ("What: on berlin a STOCHASTIC 'Blue Stochastic Squad' (10 men from the TKB composite) holds (300,300) under a Defend order (the generic StochasticUnitDefend); a PHYSICAL 'Red Squad' stands 480 m north - "
            "inside the resolution manager's band, outside its rifles' reach - so the squad de-aggregates. Judge (X-2): (1) the squad splits; (2) within 3 s of the handover it runs SquadDefend - the SAME order, "
            "re-resolved at its new resolution by the routing table - and never the stochastic play again; (3) its 10 men are in its roster, each its subordinate (the leader SquadLeader). "
            "Fail: the squad keeps the stochastic play over its men, an empty roster, or no order after the split. Check: scripts/scenario-regression.sh agg-split-keeps-order")
    checks = {"$doc": "DESIGN_X2_Order_Reresolution.md §8 (X2-A / X2-B / X2-C, R-8353 ... R-8355): BusResolutionSwitch.Handover assigns each released man (CmdAssignSubordinate -> UnitHierarchySystem, a frame later); "
                      "UnitResolutionFitSystem re-issues the order (AssignBehaviorEvent IsResume, same id) once the roster is complete; the ingress applies it two frames later. The squad holds open ground "
                      "(no house): SquadDefend plans posts from the terrain. Children by NAME <squad>/<composite slot> (F-22), listed in `later`.",
              "runFor": 30, "golden": {"every": 10}, "later": child_names(U, range(1, 11)), "expect": rows,
              "anchors": {"split": {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}}}
    if probe:
        checks["golden"] = False
    write(s, "berlin", desc, checks)


def agg_merge_keeps_order(probe=False):
    """X-2: the split squad's enemy leaves; it merges back and keeps its Defend as the stochastic play, its roster gone."""
    s = Scn("agg-merge-keeps-order", "a9000022")
    U = "Blue Stochastic Squad"
    x2_squad(s, U, (300, 300), (300, 300))
    sq = s.physical_squad("Red Patrol", RED, (300, 850), None)          # 550 m: inside the 600 m trigger, outside the forced range
    s.ents[sq]["Behavior"] = {"Name": "SquadMarch", "Params": {"Objective": pt((300, 1700)), "Shape": 0, "Spacing": 4}, "Origin": "Superior"}
    MERGE_AT = 209.25   # measured (§23.6i, editor tier): the merge lands at 209.25 s (the patrol past 800 m at ~179 s + 30 s quiet)
    rows = [
        field("contact: the squad splits (the patrol 550 m off)", U, "AggregateUnit.Resolution", "==", "Physical", before=15),
        task("... and runs its Defend as SquadDefend", U, "SquadDefend", after="split", before="split+3"),
        field("the patrol walks past the merge line (800 m) and 30 s pass quiet: the squad merges back", U, "AggregateUnit.Resolution", "==", "Stochastic", after="split+5", before=MERGE_AT + 1),
        task("X-2: after the merge it runs its Defend as the stochastic StochasticUnitDefend (the params projected onto the neutral DTO)", U, "StochasticUnitDefend", after=MERGE_AT, before=MERGE_AT + 3),
        never("... and never SquadDefend again", {"on": "task", "who": U, "is": "SquadDefend"}, after=MERGE_AT + 3),
        never("X2-B: the merge leaves no roster on the squad (F-X2-4: a kept roster drops it out of the staff's stochastic arm)", {"on": "field", "who": U, "path": "UnitRoster.Count", "op": ">=", "value": 0}, after=MERGE_AT + 1),
    ]
    desc = ("What: on berlin a STOCHASTIC 'Blue Stochastic Squad' (10 men) holds (300,300) under a Defend order; a PHYSICAL 'Red Patrol' 550 m north marches away north. Judge (X-2): (1) the squad splits "
            "while the patrol is near and runs SquadDefend; (2) once the patrol is past the 800 m merge line and 30 s are quiet the men merge back; (3) within 3 s it runs the stochastic StochasticUnitDefend - "
            "the same order - and never SquadDefend again; (4) no roster is left on it. Fail: SquadDefend on a memberless unit, no order after the merge, or a roster left behind. "
            "Check: scripts/scenario-regression.sh agg-merge-keeps-order")
    checks = {"$doc": "DESIGN_X2_Order_Reresolution.md §8 (X2-B / X2-C, R-8354 / R-8355): BusResolutionSwitch.Merge removes UnitRoster; the reconciler re-issues the order projected onto "
                      "StochasticUnitDefend's DTO (OrderReissue.Project). New rather than re-judging agg-merge-back (a PLATOON: X-2 is squad-only, X2-I).",
              "runFor": MERGE_AT + 20, "golden": {"every": 40}, "later": child_names(U, range(1, 11)), "expect": rows,
              "anchors": {"split": {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}}}
    if probe:
        checks["golden"] = False
    write(s, "berlin", desc, checks)


def agg_commanded_split(probe=False):
    """X-2 X2-H: a physical platoon (PlatoonDefend) over a stochastic squad that splits: the squad runs SquadDefend, the platoon gives it ONE order."""
    s = Scn("agg-commanded-split", "a9000023")
    P, U = "Blue Platoon", "Blue Stochastic Squad"
    pl = s.unit(P, BLUE, (300, 280), PLATOON, behavior=("PlatoonDefend", {"Position": pt((300, 300)), "Threat": pt((300, 800))}))
    s.unit(U, BLUE, (300, 300), SQUAD, pl, aggregate={})
    s.physical_squad("Red Squad", RED, (300, 780), None)
    rows = [
        task("the platoon runs PlatoonDefend", P, "PlatoonDefend", before=5),
        task("it orders its stochastic squad Defend by KIND: the squad runs the generic play", U, "StochasticUnitDefend", before=10),
        field("the enemy inside the band: the squad de-aggregates", U, "AggregateUnit.Resolution", "==", "Physical", before=30),
        task("X-2: within 3 s of the handover the squad runs SquadDefend", U, "SquadDefend", after="split", before="split+3"),
        never("... and never the stochastic play again", {"on": "task", "who": U, "is": "StochasticUnitDefend"}, after="split+3"),
    ]
    desc = ("What: on berlin a PHYSICAL 'Blue Platoon' (PlatoonDefend at (300,300), threat north) commands ONE STOCHASTIC 'Blue Stochastic Squad'; a PHYSICAL 'Red Squad' stands 480 m north. Judge (X-2, X2-H): "
            "(1) the platoon orders the squad Defend by kind (the generic StochasticUnitDefend); (2) the squad splits and within 3 s runs SquadDefend - the platoon's SAME order (its request id kept), so the platoon "
            "does not re-send it and the posted men are not restarted. Fail: the squad back on the stochastic play, or a second order after the switch. Check: scripts/scenario-regression.sh agg-commanded-split")
    checks = {"$doc": "DESIGN_X2_Order_Reresolution.md §8 (X2-H, R-8361): PlatoonDefend gives orders through CommandOrders.Route and re-sends only when Stands fails; the routed checks are resolution-blind "
                      "and match by RequestId, which the reconciler keeps (IsResume).",
              "runFor": 40, "golden": {"every": 10}, "later": child_names(U, range(1, 11)), "expect": rows,
              "anchors": {"split": {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}}}
    if probe:
        checks["golden"] = False
    write(s, "berlin", desc, checks)


GENERATORS = {"agg-march-mounted": lambda: agg_march_mounted(381.25, 350, 60 / 3.6), "agg-platoon-under-company": agg_platoon_under_company, "agg-refused-order": agg_refused_order, "agg-recorded": agg_recorded, "agg-march-open": agg_march_open,
              "agg-route-dropped": agg_route_dropped, "agg-detect-by-range": agg_detect_by_range, "agg-sees-physical": agg_sees_physical, "agg-break-contact": agg_break_contact,
              "agg-platoon-duel": agg_platoon_duel, "agg-shelled-unit": agg_shelled_unit, "agg-prepared-defence": agg_prepared_defence, "agg-out-of-ammo": agg_out_of_ammo,
              "agg-battalion-fight": agg_battalion_fight, "agg-mixed-company-attack": agg_mixed_company_attack,
              "agg-split-on-contact": agg_split_on_contact, "agg-squad-holds-house": agg_squad_holds_house, "agg-merge-back": agg_merge_back,
              "agg-forced-handover": agg_forced_handover, "agg-defend-fires": agg_defend_fires,
              "agg-split-keeps-order": agg_split_keeps_order, "agg-merge-keeps-order": agg_merge_keeps_order, "agg-commanded-split": agg_commanded_split,
              "agg-platoon-splits-into-squads": agg_platoon_splits_into_squads, "agg-platoon-merges-back": agg_platoon_merges_back}

if __name__ == "__main__":
    args = sys.argv[1:]
    probe = "--probe" in args   # ST-5: write the measuring rows (which network ids are the children) instead of the scenario's own
    names = [a for a in args if a != "--probe"] or list(GENERATORS)
    for n in names:
        GENERATORS[n](probe=True) if probe else GENERATORS[n]()
