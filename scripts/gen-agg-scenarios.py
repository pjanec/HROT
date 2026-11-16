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
    s.unit("Blue Stochastic Platoon", BLUE, (300, 200), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 200)), "SearchRadius": 30}))
    s.unit("Red Stochastic Squad", RED, (300, 500), SQUAD, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 500)), "SearchRadius": 30}))
    desc = ("What: on basic-desert a BLUE stochastic platoon (30 men) and a RED stochastic squad (4 men - the weaker side) face each other 300 m apart, both defending; they fire on each other "
            "(agg-backend's phase A). Judge: (1) losses on BOTH sides (a slot of each is down); (2) suppression rises on both (AggregateUnit.SuppressedShare > 0); (3) the weaker side breaks first - a Red slot's "
            "stress reaches the break level before any Blue slot's does. NOT judged: 'losses inside the C-8 band' - the band needs the physical runs (agg-backend CE-8009, not built), so that row waits; "
            "no band is invented. Check: scripts/scenario-regression.sh agg-platoon-duel")
    checks = {"$doc": "§23.6 ST-3. The C-8 band row (the stochastic loss rate vs the physical Monte Carlo) waits for CE-8009; what is checkable without it is encoded: losses on both sides, suppression, and who breaks first. "
                      "Slot health is 25 when whole (UnitSplits.SlotFullHealth); 'down' = Health < 0.5 (UnitSlotTable.DownBelow); 'broken' = Stress >= 0.75 (AggregateUnits.BrokenAt). A Red squad is 10 slots; "
                      "measured: by 3.25 s eight of its slots are down and four have broken, while the platoon loses one or two slots and never breaks.",
              "runFor": 60, "golden": {"every": 10}, "expect": [
        field("the fight reaches the Blue platoon (a slot is down)", "Blue Stochastic Platoon", "UnitSlotTable.Slots.*.Health", "<", 0.5, before=30, where=LIVE),
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
        s.unit(f"Red {tag} Defender", RED, (1000, y), PLATOON, aggregate={"Prepared": level})
        s.unit(f"Blue Attacker {tag}", BLUE, (1000, y - 350), COMPANY, aggregate={}, behavior=("StochasticUnitAttack", {"Position": pt((1000, y))}))
    desc = ("What: on berlin two identical fights 7 km apart (out of each other's sight): a BLUE stochastic platoon (30 men) attacks a RED stochastic platoon (30 men) 500 m north of it. "
            "One Red defender has been in position long enough to be PREPARED (>= 600 s), the other FORTIFIED (>= 3600 s) - authored as the level they would have reached (no play: the cover factor is read from "
            "AggregateUnit.Prepared, 0.45 / 0.25; the Defend play's own time-in-position ladder is its rail's, R-8306). Judge: the Fortified defender loses fewer men than the Prepared one "
            "(its down-slot count stays below the Prepared one's). Check: scripts/scenario-regression.sh agg-prepared-defence")
    checks = {"$doc": "§23.6 ST-3. The levels are authored: StochasticUnitDefend stamps Hasty at occupation and climbs by sim time (600 s / 3600 s), so a play-driven version would need a 3700 s run; the combat reads "
                      "AggregateUnit.Prepared directly (StochasticCombat.CoverFactor: 0.45 Prepared, 0.25 Fortified). The attackers are COMPANIES (90 men) against 30-man defenders so the losses are not one or two slots. "
                      "Measured at 90 s: the Prepared defender has 6 slots down (7 damaged), the Fortified one 5 - the order the rows pin; both sides' fight is over by then.",
              "runFor": 90, "golden": {"every": 15}, "expect": [
        field("the Fortified defender is fought: a slot is down", "Red Fortified Defender", "UnitSlotTable.Slots.*.Health", "<", 0.5, atEnd=True, where=LIVE),
        field("the Prepared defender loses at least 6 men", "Red Prepared Defender", "UnitSlotTable.Slots.*.Health", "<", 0.5, atEnd=True, where=LIVE, elements=6),
        never("the Fortified defender never loses 6 men (it loses fewer than the Prepared one)", {"on": "field", "who": "Red Fortified Defender", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 6}),
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
                      "the squad for SITREPs). Measured: the first round goes at 1.25 s, every slot is empty at 6.25 s, the SITREP ammunition reads Red at 3.25 s and Black at 18.25 s (the report's delay).",
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
            "over three stochastic companies, the two lines 600 m apart. Judge: (1) every company is ordered by KIND - Red's three run the generic Defend, the assault company the generic Attack; no stochastic company is given a physical play; "
            "(2) the census agrees with the slot tables: a HoldArea objective round each HQ's three companies (300 m) needs 270 men = 3 x 90 slot-table men present, and reads Achieved; (3) each company's SITREP colour agrees with its slot table "
            "(the assault company wiped out reads Black at its HQ; each Red company's colour lands in the band its slot table gives - Green under 10 of 90 down, Amber under 28); (4) the fight runs to an end: the assault company is lost (PROGRESS TaskFailed at its HQ) and the others hold. "
            "Fail: a physical play on a stochastic unit, a census that does not equal the slot tables, a colour that disagrees with them, or no end. Check: scripts/scenario-regression.sh agg-battalion-fight")
    expect = [
        task("the Blue HQ runs CompanyAttack", "Blue Battalion HQ", "CompanyAttack", before=10),
        task("the Red HQ runs CompanyDefend", "Red Battalion HQ", "CompanyDefend", before=10),
        task("the assault company is ordered by KIND: the generic Attack play", "Blue Company 2", "StochasticUnitAttack", before=10),
        field("the census: the Blue HQ's three companies hold 270 slot-table men in the area", "Blue Battalion Census", "ObjectiveStatus.State", "==", "Achieved", before=20),
        field("the census: the Red HQ's three companies hold 270 slot-table men in the area", "Red Battalion Census", "ObjectiveStatus.State", "==", "Achieved", before=20),
        field("the HQ hears the assault company is lost (PROGRESS TaskFailed): the fight has run to an end", "Blue Battalion HQ", "CommandPicture.Subordinates.1.Progress", "==", "TaskFailed", before=440),
        field("the assault company's slot table agrees: every slot is down", "Blue Company 2", "UnitSlotTable.Slots.*.Health", "<", 0.5, atEnd=True, where=LIVE, elements=90),
        field("... and its SITREP colour at the HQ is Black", "Blue Battalion HQ", "CommandPicture.Subordinates.1.Strength", "==", "Black", atEnd=True),
        field("the base-of-fire company has lost nobody: its SITREP colour is Green", "Blue Battalion HQ", "CommandPicture.Subordinates.0.Strength", "==", "Green", atEnd=True),
        never("... and its slot table agrees (no slot down)", {"on": "field", "who": "Blue Company 1", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE}),
        # R-8332: Red's three rows are judged by the colour BANDS (Green >= 90 % present = fewer than 10 of 90 down; Amber >= 70 % = fewer than 28 down) - the
        # SITREP and the slot table must land in the SAME band. The HQ's roster lists the companies 3, 2, 1 (measured: the only Green row, Subordinates.0, is the
        # only company with fewer than 10 down - company 3).
        field("Red's SITREP colours: company 3, untouched by the assault, reads Green", "Red Battalion HQ", "CommandPicture.Subordinates.0.Strength", "==", "Green", atEnd=True),
        never("... and its slot table agrees (fewer than 10 of 90 down: >= 90 % present)", {"on": "field", "who": "Red Company 3", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 10}),
        field("Red's SITREP colours: company 2, in the assault's path, reads Amber", "Red Battalion HQ", "CommandPicture.Subordinates.1.Strength", "==", "Amber", atEnd=True),
        field("Red's SITREP colours: company 1 reads Amber", "Red Battalion HQ", "CommandPicture.Subordinates.2.Strength", "==", "Amber", atEnd=True),
        field("... and company 2's slot table agrees (at least 10 down)", "Red Company 2", "UnitSlotTable.Slots.*.Health", "<", 0.5, atEnd=True, where=LIVE, elements=10),
        field("... and company 1's slot table agrees (at least 10 down)", "Red Company 1", "UnitSlotTable.Slots.*.Health", "<", 0.5, atEnd=True, where=LIVE, elements=10),
        never("... neither falls below Amber (28 down: < 70 % present) - company 1", {"on": "field", "who": "Red Company 1", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 28}),
        never("... neither falls below Amber - company 2", {"on": "field", "who": "Red Company 2", "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 28}),
    ] + [never(f"no physical play on {c}", {"on": "task", "who": c, "is": ["PlatoonAttack", "PlatoonDefend", "CompanyAttack", "CompanyDefend", "PlatoonSupportByFire"]})
         for c in ("Blue Company 1", "Blue Company 2", "Blue Company 3", "Red Company 1", "Red Company 2", "Red Company 3")]
    checks = {"$doc": "§23.6 ST-4. 'A side broken' is the measured end: the Blue assault company (Blue Company 2) is destroyed within seconds of contact (all 90 slots down by 335 s at GAP 1500 / 335 s at 600), "
                      "its HQ hears TaskFailed and reads it Black; the base-of-fire company (1) Sets and the reserve (3) never engage. After agg-backend's calibration (CE-8009 R-8005: focused hits, broken men still firing at 0.6) the assault costs Red companies 1 and 2 ten or more men each (Amber), company 3 under ten (Green) - measured 2026-11-16; reverting 5d8a30150 alone restores the old all-Green outcome (R-8332). The census is the HoldArea objective of ObjectiveCensus (a stochastic unit under a physical subject's roster counts its Present). ROWS",
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


def child_rows(first_id, unit_guid, n, centre, within, after, label, slots=None, before=None):
    slots = slots or list(range(1, n + 1))     # the composite slots of the UP slots, in table order (a down slot gets no child)
    rows = []
    for k in range(n):
        rows.append(field(f"{label}: child {k} (slot {slots[k]}) carries the stable key uuid5(unit, child:{slots[k]})", str(first_id + k), "EntityGuid.Value", "==", child_guid(unit_guid, slots[k]), after=after))
    if centre is None:
        return rows
    for k in range(n):
        r = {"name": f"{label}: child {k} stands within {within} m of the unit's place", "on": "near", "who": str(first_id + k), "point": pt(centre), "within": within, "after": after}
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
    # the merge needs the enemy beyond band + margin (default 400 + 100 = 500 m) - INSIDE the 600 m split trigger, so a patrol walking through 500-600 m
    # made the platoon merge and split again every ~63 s (finding F-23, measured): the author's policy puts the merge past the trigger (R-8336)
    resolution_policy(s, {"mergeMargin": 250})
    first = 1008
    rows = [
        never("the platoon stays stochastic while the patrol is beyond the band (400 m + 200 m lead)", {"on": "field", "who": U, "path": "AggregateUnit.Splitting", "op": "!=", "value": "NotSplitting"}, before="band"),
        field("the patrol inside the band starts the process: Planning (a step of its own - not forced)", U, "AggregateUnit.Splitting", "==", "Planning", after="band", before="band+5"),
        field("... then Materialising (the children created, held)", U, "AggregateUnit.Splitting", "==", "Materialising", after="band", before="band+10"),
        field("the handover: the platoon is Physical", U, "AggregateUnit.Resolution", "==", "Physical", after="band", before="band+20"),
        field("the slot table is untouched by the split (30 men up)", U, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=30, atEnd=True),
        never("the patrol never comes within the forced range (300 m): this is the planned path, not C-7", {"on": "near", "who": "Red Patrol Leader", "point": pt((300, 300)), "within": 300}),
    ] + child_rows(first, pl, 30, (300, 295), 20, "handover", "split", before="handover+1")
    desc = ("What: on berlin a STOCHASTIC 'Blue Stochastic Platoon' (30 men from the TKB composite, defending at (300,300)) and a PHYSICAL 'Red Patrol' (four men) marching from 700 m north to 200 m. "
            "Judge: (1) while the patrol is beyond the resolution manager's band (400 m + 200 m lead) the platoon stays stochastic; (2) when the patrol enters it, the platoon de-aggregates as a PROCESS - "
            "Planning, Materialising (the men created and held), one-tick handover - and is Physical; (3) each of the 30 men carries the stable key uuid5(<platoon>, child:<slot>) (the same unit split twice "
            "gives the same men); (4) the men stand where the platoon stood - the fallback posts, rows of 12 across its heading, within 20 m. Fail: a split before the band, a missing or re-keyed man, or a man far "
            "from the platoon. Check: scripts/scenario-regression.sh agg-split-on-contact")
    checks = {"$doc": "§23.6 ST-5. Bands: ResolutionBands.Default (400 m + 200 m lead, forced 300 m). The posts are UnitSplits.FallbackPosts (D-P5): D-P3's planned posts are STOPPED (R-8330), so 'sensible positions' "
                      "= the fallback rows round the unit. A rifle platoon (type 305) has no vehicles - the 'soldiers and vehicles' half of the Judge line is not exercised. The children carry no name (F-22): "
                      "they are addressed by network id (measured: the children are 1008-1037 in slot order) (R-8335). The author's policy sets the merge margin to 250 m (merge beyond 650 m, past the 600 m trigger; F-23, R-8336).",
              "runFor": 150, "golden": {"every": 25},
              "anchors": {"band": {"on": "near", "who": "*Red Patrol", "point": pt((300, 300)), "within": 600},
                          "handover": {"on": "field", "who": U, "path": "AggregateUnit.Resolution", "op": "==", "value": "Physical"}},
              "expect": rows}
    if probe:
        checks["golden"] = False
        probe_ids(checks, 1000, 1045, [field("phys", U, "AggregateUnit.Resolution", "==", "Physical"), field("stoch after 75", U, "AggregateUnit.Resolution", "==", "Stochastic", after=75)]
                  + [{"name": f"patrol within {r} after 75", "on": "near", "who": "*Red Patrol", "point": pt((300, 300)), "within": r, "after": 75} for r in (250, 350, 450, 550, 650)]
                  + [{"name": f"patrol beyond {r} after 75", "never": {"on": "near", "who": "*Red Patrol", "point": pt((300, 300)), "within": r}, "after": 75} for r in (250, 350, 450, 550)]
                  + [field("red man dead", "*Red Patrol", "Health.Current", "<=", 0)])
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
    first, n = 1030, 10            # measured: cqb-village's terrain entities (doors, records) take the ids before the children
    rows = [
        field("the squad in the house de-aggregates: Physical", U, "AggregateUnit.Resolution", "==", "Physical", before=20),
        field("its slot table is whole (10 men up)", U, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=n, atEnd=True),
    ] + child_rows(first, sq, n, house, 12, "split", "house", before="split+1")
    for k in range(n):
        rows.append(field(f"house: child {k} stands on the fallback line across the squad's heading (y <= the house's {house[1]} + 1 m)", str(first + k), "SimTransform.Position.1", "<=", house[1] + 1, after="split", before="split+1"))
        rows.append(field(f"house: child {k} ... (y >= {house[1]} - 1 m)", str(first + k), "SimTransform.Position.1", ">=", house[1] - 1, after="split", before="split+1"))
    desc = ("What: on cqb-village a STOCHASTIC 'Blue Stochastic Squad' (10 men from the TKB composite) holds the village's three-storey house 'OBJ' at (125,161) (StochasticUnitDefend); a PHYSICAL 'Red Squad' "
            "stands 480 m north - inside the resolution manager's band, so the squad de-aggregates. Judge, AS BUILT: the squad splits into its 10 men, each with its stable key, and each man is placed (at the handover) on the "
            "FALLBACK posts - one line of 10, 2 m apart, across the squad's heading through the house's centre (UnitSplits.FallbackPosts), within 12 m of it. ⛔ NOT judged: the men at the house's window "
            "and door posts (§16 / D-P3): the planners' plan / assign split is STOPPED (R-8330), so no window post exists to be judged. Check: scripts/scenario-regression.sh agg-squad-holds-house")
    checks = {"$doc": "§23.6 ST-5, judged as built: D-P3 (planned posts from SquadDefend's window / door survey) is STOPPED (R-8330, design §23.5c), so a split squad's men stand on UnitSplits.FallbackPosts - "
                      "a line across the heading (heading 0: along x) through the unit's position, rows of <= 12, width max(Width, 2 m x men). When D-P3 lands, the window / door rows replace the line rows. "
                      "Children by network id (no name, F-22; R-8335): measured 1030-1039.",
              "runFor": 40, "golden": {"every": 10}, "expect": rows,
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


def agg_merge_back(probe=False):
    s = Scn("agg-merge-back", "a9000018")
    U = "Blue Stochastic Platoon"
    pl = s.unit(U, BLUE, (300, 300), PLATOON, aggregate={}, behavior=("StochasticUnitDefend", {"Position": pt((300, 300)), "SearchRadius": 30}),
                extra={"UnitSlotTable": merged_slot_table()})
    s.unit("Blue Spare Squad", BLUE, (300, -9000), SQUAD, aggregate={})   # a FILLED unit far away: teaches the personal weapons (F-16)
    sq = s.physical_squad("Red Patrol", RED, (300, 750), None)            # 450 m: inside the band, outside the forced range
    s.ents[sq]["Behavior"] = {"Name": "SquadMarch", "Params": {"Objective": pt((300, 1500)), "Shape": 0, "Spacing": 4}, "Origin": "Superior"}
    resolution_policy(s, {"quietSeconds": 20, "mergeMargin": 250})
    n = 27
    UP = [k + 1 for k in range(30) if k not in (3, 4, 5)]      # the composite slots that get a child (4-6 are dead); measured ids 1009-1035, then 1036-1062
    cut = lambda name, at: {"name": name, "at": at, "set": {"who": U, "component": "AggregateUnit", "patch": {"Pin": "PinPhysical"}}}
    rows = [
        field("contact: the platoon splits (the patrol 450 m off)", U, "AggregateUnit.Resolution", "==", "Physical", before=15),
        field("contact ends (the patrol walks away past the band + margin, 650 m, and 20 s pass): the men merge back", U, "AggregateUnit.Resolution", "==", "Stochastic", after=20, before=240),
        field("the merge kept every loss (the table at the end is the merged one - a split never writes it): the 3 dead slots are down", U, "UnitSlotTable.Slots.*.Health", "<", 0.5, where=LIVE, elements=3, atEnd=True),
        never("... never a 4th (no man lost or invented by the round trip)", {"on": "field", "who": U, "path": "UnitSlotTable.Slots.*.Health", "op": "<", "value": 0.5, "where": LIVE, "elements": 4}),
        field("... and the 2 hurt men come back hurt (health 12.5 of 25)", U, "UnitSlotTable.Slots.*.Health", "==", 12.5, where=LIVE, elements=2, atEnd=True),
        field("... and 27 men up: none lost, none invented by the round trip", U, "UnitSlotTable.Slots.*.Health", ">=", 0.5, where=LIVE, elements=27, atEnd=True),
        never("... never 28 up (a dead man is never re-created)", {"on": "field", "who": U, "path": "UnitSlotTable.Slots.*.Health", "op": ">=", "value": 0.5, "where": LIVE, "elements": 28}),
        field("the instructor pins it Physical (t = 250 s): it splits AGAIN at once", U, "AggregateUnit.Resolution", "==", "Physical", after=250, before=255),
        never("no second split before the pin: the merge lies past the split trigger (the author's 250 m margin)", {"on": "field", "who": U, "path": "AggregateUnit.Splitting", "op": "==", "value": "Materialising"}, after=30, before=250),
    ] + child_rows(1009, pl, n, None, 0, 0, "first split", UP) + child_rows(1036, pl, n, None, 0, 250, "the pin's split: the SAME key", UP)
    desc = ("What: on berlin a STOCHASTIC 'Blue Stochastic Platoon' (30 slots, 3 dead and 2 hurt - an authored table) holds (300,300); a PHYSICAL 'Red Patrol' 450 m north marches away north. "
            "The author's SetResolutionPolicy sets the quiet time to 20 s and the merge margin to 250 m (merge beyond 650 m - past the 600 m split trigger). Judge: (1) the platoon splits while the patrol is near "
            "(27 men: the dead are not created); (2) once the patrol is past 650 m and 20 s are quiet the men merge back - Stochastic; (3) the slot table keeps every loss and every man: exactly 3 down (never a 4th), 27 up, the 2 hurt at "
            "half health; (4) the instructor's pin (PinPhysical at 250 s) splits it again and the SAME 27 identities come back (uuid5(<platoon>, child:<slot>)). Fail: a merge in contact, a loss healed or "
            "invented, or a different key on the second split. Check: scripts/scenario-regression.sh agg-merge-back")
    checks = {"$doc": "§23.6 ST-5. Merge (§16b, D-P7): out of the band + margin and quiet for the quiet time (and UNOBSERVED); then BusResolutionSwitch.Merge sums each child back (X-4) - a slot with no child is down. "
                      "The margin is raised to 250 m by the author's policy so the merge lies beyond the split trigger (with the default 100 m it does not: finding F-23). The second split is the instructor's pin, a do-set of "
                      "AggregateUnit.Pin = PinPhysical (SV-14). Children by network id (F-22, R-8335). ⚠ Measured: at the end only 20-22 slots read exactly 25 - a few whole men come back "
                      "slightly hurt (>= 13 of 25) from their time as soldiers; the source is not traced (the patrol walks away beyond rifle reach), so the rows judge the losses (3 down, never a 4th, 27 up, the 2 hurt still hurt), not exact health.",
              "runFor": 280, "golden": {"every": 40}, "do": [cut("the instructor pins it Physical", 250)],
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
    ]
    desc = ("What: on berlin two identical STOCHASTIC platoons (30 men, defending), 7 km apart. A PHYSICAL 'Red Squad Forced' (four armed men) stands 250 m from the first - inside C-7's forced range (300 m); "
            "a 'Red Squad Planned' stands 450 m from the second - inside the band, outside the forced range. Judge: (1) the first platoon hands over at once, skipping the planning time (its Planning stage is never "
            "seen), before any stochastic step fights it (no stochastic fire line, no slot hurt); (2) the control platoon goes through Planning first, then hands over. Fail: a stochastic step on the forced one, or "
            "the forced one planning. Check: scripts/scenario-regression.sh agg-forced-handover")
    checks = {"$doc": "§23.6 ST-5, C-7: ResolutionManagerSystem.Decide - an enemy within ForcedRange (300 m) skips straight from NotSplitting to Materialising in the same step (goto case Planning), so Planning "
                      "is never at a step boundary; with the enemy in the band but not forced the stage is held one step. The stochastic fire phase targets only stochastic units, so 'before any stochastic "
                      "step fights it' is encoded as: the platoon writes no fire line and loses no health while stochastic.",
              "runFor": 30, "golden": {"every": 10}, "expect": rows}
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


GENERATORS = {"agg-march-mounted": lambda: agg_march_mounted(381.25, 350, 60 / 3.6), "agg-platoon-under-company": agg_platoon_under_company, "agg-refused-order": agg_refused_order, "agg-recorded": agg_recorded, "agg-march-open": agg_march_open,
              "agg-route-dropped": agg_route_dropped, "agg-detect-by-range": agg_detect_by_range, "agg-sees-physical": agg_sees_physical, "agg-break-contact": agg_break_contact,
              "agg-platoon-duel": agg_platoon_duel, "agg-shelled-unit": agg_shelled_unit, "agg-prepared-defence": agg_prepared_defence, "agg-out-of-ammo": agg_out_of_ammo,
              "agg-battalion-fight": agg_battalion_fight, "agg-mixed-company-attack": agg_mixed_company_attack,
              "agg-split-on-contact": agg_split_on_contact, "agg-squad-holds-house": agg_squad_holds_house, "agg-merge-back": agg_merge_back,
              "agg-forced-handover": agg_forced_handover, "agg-defend-fires": agg_defend_fires}

if __name__ == "__main__":
    args = sys.argv[1:]
    probe = "--probe" in args   # ST-5: write the measuring rows (which network ids are the children) instead of the scenario's own
    names = [a for a in args if a != "--probe"] or list(GENERATORS)
    for n in names:
        GENERATORS[n](probe=True) if probe else GENERATORS[n]()
