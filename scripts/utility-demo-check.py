#!/usr/bin/env python3
"""Utility AI demo scenarios — live acceptance over the cluster debug HTTP API (CE-3069).

Each scenario is a demo AND an E2E test: it loads, runs, reads the unit's utility decisions where they are made (the
Brain, perspective "Scenario") with GET /entities/{id}/utility, and exits 0 on PASS, 1 on FAIL.

    HROT_DEBUG_API_PORT=8111 xvfb-run -a dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode all
    python3 scripts/utility-demo-check.py ua-posture
    python3 scripts/utility-demo-check.py --launch ua-threat-ranking   # starts and stops its own fresh cluster

Several scenarios may run in one cluster process (CE-295 fixed; CE-3075: a scenario naming no terrain unloads the
previous one); the script verifies the loaded cast.

Design and the expected behaviour of each scenario: docs/DESIGN_Utility_AI_Demo_Scenarios.md §4.
How to run, what to watch, what a failure means: docs/RUNBOOK_Utility_AI_Demos.md.
"""
import argparse, json, os, signal, subprocess, sys, time, urllib.error, urllib.request

OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))   # never through a proxy (RUNBOOK §2)
BASE = "http://localhost:8111"                                            # the HOSTNAME — 127.0.0.1 404s (RUNBOOK §2)


def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with OPENER.open(req, timeout=60) as r:
            raw = r.read()
            if not raw:   # the host died while answering (the headers were already sent) — see the cluster log
                return {"http": r.status, "body": "(empty reply — did the cluster process exit? see its log)"}
            return json.loads(raw)
    except urllib.error.HTTPError as e:
        return {"http": e.code, "body": e.read().decode()[:400]}
    except (urllib.error.URLError, ConnectionError, TimeoutError) as e:   # nothing listening (yet)
        return {"http": 0, "body": str(e)}


def data(resp):
    return resp.get("data") if isinstance(resp, dict) else None


class Check:
    def __init__(self):
        self.failures = []

    def ok(self, cond, what):
        print(("  PASS  " if cond else "  FAIL  ") + what)
        if not cond:
            self.failures.append(what)
        return cond


REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUNNER = os.path.join(REPO, "Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll")


def scenario_names(name):
    with open(os.path.join(REPO, "scenarios", name, "scenario.json")) as fh:
        doc = json.load(fh)
    return sorted(e.get("EntityInfo", {}).get("Name") for e in doc["entities"].values() if e.get("EntityInfo", {}).get("Name"))


def load(name):
    resp = call("POST", "/scenario/load/live", {"name": name, "waitForReady": True})
    if "http" in resp:
        sys.exit(f"load failed: {resp}")
    call("POST", "/perspective", {"name": "Scenario"})
    # ⭐ Prove the world IS this scenario: before CE-295 a second live load answered ok:true and changed nothing, so a
    #   check could report on the previous scenario's cast. Kept as a guard.
    want = scenario_names(name)
    got = wait_for(lambda: (n := sorted(x for x in ids_by_name())) == want and n, 30)
    if got is None:
        sys.exit(f"the loaded world is not '{name}' (want {want}, have {sorted(ids_by_name())}) — a second live load in one "
                 f"process was a silent no-op before CE-295. Restart the cluster, or run with --launch.")


def launch(port):
    """Start a fresh ClusterRunner --mode all for this run; returns the process."""
    if not os.path.exists(RUNNER):
        sys.exit(f"build it first: dotnet build Hrot/Runner/Hrot.ClusterRunner ({RUNNER} missing)")
    log = open(os.path.join("/tmp", f"utility-demo-cluster-{port}.log"), "w")
    env = dict(os.environ, HROT_DEBUG_API_PORT=str(port))
    proc = subprocess.Popen(["xvfb-run", "-a", "dotnet", RUNNER, "--mode", "all"], cwd=REPO, env=env,
                            stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    if not wait_for(lambda: data(call("GET", "/status")), 180, every=1):
        stop(proc)
        sys.exit(f"the cluster did not answer on {BASE} within 180 s — see {log.name}")
    print(f"   launched ClusterRunner --mode all (pid {proc.pid}, log {log.name})")
    return proc


def stop(proc):
    try:
        os.killpg(proc.pid, signal.SIGTERM)
        proc.wait(timeout=30)
    except Exception:
        try: os.killpg(proc.pid, signal.SIGKILL)
        except Exception: pass


def ids_by_name():
    rows = data(call("GET", "/entities")) or []
    return {r.get("name"): r.get("networkId") for r in rows if r.get("name")}


def utility(nid):
    return data(call("GET", f"/entities/{nid}/utility")) or {}


def decision(u, name):
    for d in u.get("decisions") or []:
        if d.get("decision") == name:
            return d
    return None


def wait_for(pred, timeout, every=0.5):
    end = time.time() + timeout
    while time.time() < end:
        v = pred()
        if v:
            return v
        time.sleep(every)
    return None


def sim_time():
    return (data(call("GET", "/status")) or {}).get("simTime")


def set_health(nid, current):
    r = call("POST", f"/entities/{nid}/component", {"networkId": nid, "componentType": "Health", "patch": {"Current": current}})
    if "http" in r:
        print(f"    (health edit refused: {r})")
    return "http" not in r


# ── U1 — CombatPosture: the postures and the hysteresis ─────────────────────────────────────────────────────────────

POSTURE = "Combat posture"


def winner_of(nid):
    d = decision(utility(nid), POSTURE)
    return d and d.get("winner"), d


def run_posture(c, timeout):
    ids = ids_by_name()
    rifle = ids.get("Rifleman")
    if not c.ok(rifle is not None, "the Rifleman is loaded"):
        return
    call("POST", "/trace/observe", {"networkId": rifle, "on": True})
    call("POST", "/sim/play", {})

    first = wait_for(lambda: winner_of(rifle)[0], timeout)
    c.ok(first is not None, f"the posture decision runs (first winner: {first})")
    sup = wait_for(lambda: winner_of(rifle)[0] == "Suppress" and "Suppress", timeout)
    c.ok(sup == "Suppress", "healthy, armed, facing a MATCHED enemy ⇒ Suppress")

    FIGHT, DEFEND = ("Suppress", "AdvanceAndAttack"), ("TakeCover", "Flee")
    seen = set()

    def step(hp, want, what):
        set_health(rifle, hp)
        got = wait_for(lambda: (w := winner_of(rifle)[0]) in want and w, timeout)
        _, d = winner_of(rifle)
        ranked = d and [(r.get("option"), r.get("score")) for r in d.get("ranked", [])]
        c.ok(got is not None, f"{what} (winner {d and d.get('winner')}, ranked {ranked})")
        if d:
            seen.add(d.get("winner"))
            c.ok(bool(ranked) and ranked[0][0] == d.get("winner"), "the winner is the top of its own ranking (scores after hysteresis)")
        return d

    # ⭐ Measured live 2026-10-05: WHICH defensive posture wins depends on where the rifleman stands (the cover and retreat
    #   EQS answers), and that varies with how soon it saw the enemy — so the check asserts the class (fight / defend),
    #   and prints the run's own sequence. The exact hysteresis arithmetic is pinned by UtilityScorerTests.CE3069_*.
    step(40, DEFEND, "hurt (40 HP) ⇒ a defensive posture")
    d = step(10, DEFEND, "near death (10 HP) ⇒ a defensive posture")
    if d:
        r = {x.get("option"): x.get("score") for x in d.get("ranked", [])}
        print(f"    TakeCover {r.get('TakeCover')} vs Flee {r.get('Flee')} — the held posture's score includes +{d.get('hysteresis')}")
    step(100, FIGHT, "healed (100 HP) ⇒ back to fighting")
    step(10, DEFEND, "near death again, from the open ⇒ a defensive posture")
    step(100, FIGHT, "healed again ⇒ fighting")
    print(f"    postures reached this run: {sorted(x for x in seen if x)}")

    # Hysteresis: Health held ⇒ the winner never flips BACK (A → B → A). ⚠ One forward switch is not a flicker — the fight
    #   itself moves on (measured live 2026-10-06: switchCount 5 → 6 in 5 s after the heal). Same rule as the in-process
    #   twin PostureScenarioTests.CE3085_U1_* — sample, assert no reversal, print the sequence.
    seq = [winner_of(rifle)[0]]
    for _ in range(25):
        time.sleep(0.2)
        w = winner_of(rifle)[0]
        if w != seq[-1]:
            seq.append(w)
    flick = any(seq[i] == seq[i - 2] for i in range(2, len(seq)))
    c.ok(not flick, f"no flicker while Health is held (winners {' → '.join(str(x) for x in seq)})")
    u = utility(rifle)
    c.ok(bool((u.get("lastPass") or {}).get("options")), "the per-consideration breakdown (lastPass) is readable")


# ── U2 — ThreatRanking: who is engaged first ────────────────────────────────────────────────────────────────────────

RANKING = "Threat ranking"


def run_threat_ranking(c, timeout):
    ids = ids_by_name()
    rifle = ids.get("Rifleman")
    if not c.ok(rifle is not None, "the Rifleman is loaded"):
        return
    call("POST", "/trace/observe", {"networkId": rifle, "on": True})
    call("POST", "/sim/play", {})

    # ⭐ Settled, not first: a contact's threat score (TargetMemory freshness) starts at 0 and grows while it stays in
    #   view, so the first rankings after acquisition score every contact 0 (measured live 2026-10-05, simTime 1.1 s).
    d = wait_for(lambda: (x := decision(utility(rifle), RANKING)) and len(x.get("ranked") or []) >= 2
                 and (x["ranked"][0].get("score") or 0) > 0 and x, timeout)
    if not c.ok(d is not None, "the threat ranking settles over at least two remembered contacts"):
        return
    names = [r.get("name") for r in d.get("ranked", [])]
    scores = [r.get("score") for r in d.get("ranked", [])]
    print(f"    ranked: {list(zip(names, scores))}")
    c.ok((d.get("winner") or {}).get("name") == "Armed Near", f"the armed, visible, nearest contact ranks first (winner {d.get('winner')})")
    c.ok(scores[0] > 0, "the top score is above 0 (CE-3070: distance used to zero every contact)")
    c.ok("Armed Hidden" not in names, "the contact hidden behind the Tower is not ranked (never seen)")
    if "Civilian Near" in names and "Armed Near" in names:
        c.ok(names.index("Civilian Near") > names.index("Armed Near"), "the unarmed civilian ranks below the armed contact")
    if "Armed Far" in names and "Armed Near" in names:
        c.ok(names.index("Armed Far") > names.index("Armed Near"), "the farther armed contact ranks below the nearer")


# ── CE-3079 — the danger-area sensor: hold short of the watched crossing, cross when the watcher withdraws ──────────
#   docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.3–§10.5. ⭐ NO HTTP WRITE: the run plays out on its own (user, 2026-10-06).

def position(nid):
    comps = (data(call("GET", f"/entities/{nid}")) or {}).get("Components") or {}
    p = (comps.get("SimTransform") or {}).get("Position")
    return tuple(p) if p else None


def danger_sensor(nid):
    for s in (data(call("GET", f"/entities/{nid}/sensors")) or {}).get("sensors") or []:
        if s.get("kind") == "DangerArea":
            return s
    return None


def areas_of(sensor):
    return ((sensor or {}).get("answer") or {}).get("areas") or []


def run_danger_crossing(c, timeout):
    ids = ids_by_name()
    rifle, hostile = ids.get("Rifleman"), ids.get("Watcher")
    if not c.ok(rifle is not None and hostile is not None, "the Rifleman and the Watcher are loaded"):
        return
    call("POST", "/sim/play", {})

    # ① the sensor answers: two crossings, in route order, the second one watched
    s = wait_for(lambda: (x := danger_sensor(rifle)) and len(areas_of(x)) >= 2 and x, timeout)
    if not c.ok(s is not None, "the rifleman's danger-area sensor lists two crossings ahead"):
        return
    a = areas_of(s)
    print(f"    areas: {[(x['kind'], x['distanceAlongRoute'], x['threat']) for x in a]}")
    c.ok(a[0]["distanceAlongRoute"] < a[1]["distanceAlongRoute"], "in route order")
    c.ok(s.get("settings", {}).get("routeSource") == "ToPoint", "the sensor watches the route to the objective (route source ToPoint)")

    # ② the watched crossing rates high once the rifleman has seen the watcher; the other stays low
    watched = wait_for(lambda: (x := areas_of(danger_sensor(rifle))) and any(y["threat"] >= 0.5 for y in x) and x, timeout)
    if not c.ok(watched is not None, "a crossing is rated threatened (the watcher seen with sight of it)"):
        return
    hot = next(y for y in watched if y["threat"] >= 0.5)
    c.ok(all(y["threat"] < 0.5 for y in watched if y["featureId"] != hot["featureId"]), "the unwatched crossing stays below 0.5")

    # ③ the rifleman HOLDS at the watched crossing's near side
    near = hot["nearHandle"]
    def at_near():
        p = position(rifle)
        return p and ((p[0] - near[0]) ** 2 + (p[1] - near[1]) ** 2) ** 0.5 <= 4.0 and p
    held_at = wait_for(at_near, timeout)
    if not c.ok(held_at is not None, f"the rifleman reaches the near side of the watched crossing {near[:2]}"):
        return
    t_hold = sim_time()
    stayed = wait_for(lambda: (sim_time() or 0) - (t_hold or 0) >= 10 and at_near(), 30, every=1)
    c.ok(stayed is not None, "and holds there for at least 10 s")

    # ④ the watcher's mission moves on by itself (Sentry ends → MoveToLocation) and it withdraws out of sight
    cleared = wait_for(lambda: (x := areas_of(danger_sensor(rifle))) and all(y["threat"] < 0.4 for y in x) and x, timeout * 2)
    c.ok(cleared is not None, "the threat falls once the rifleman has watched the watcher withdraw")
    wp = position(hostile)
    print(f"    watcher now at {wp}")

    # ⑤ the rifleman crosses and arrives
    objective = (285.0, 220.0)
    arrived = wait_for(lambda: (p := position(rifle)) and ((p[0] - objective[0]) ** 2 + (p[1] - objective[1]) ** 2) ** 0.5 <= 6 and p,
                       timeout * 2)
    c.ok(arrived is not None, f"the rifleman crosses and arrives at the objective {objective}")


# ── U6 (CE-3088) — fire distribution: the leader spreads the squad's fire, ≤ 2 per target; a hurt member breaks off ─────
#   docs/DESIGN_Utility_AI_Demo_Scenarios.md §11. Reads GET /entities/{leader}/squad (G3, CE-3087).

def squad(nid):
    return data(call("GET", f"/entities/{nid}/squad")) or {}


def ammo(nid):
    w = ((data(call("GET", f"/entities/{nid}")) or {}).get("Components") or {}).get("WeaponState") or {}
    return w.get("Ammo"), w.get("MaxAmmo")


def run_fire_distribution(c, timeout):
    ids = ids_by_name()
    leader = ids.get("Leader")
    riflemen = [ids.get(f"Rifleman {i}") for i in range(1, 5)]
    hostiles = [ids.get(n) for n in ("West Hostile", "Centre Hostile", "East Hostile")]
    if not c.ok(leader is not None and None not in riflemen and None not in hostiles, "the leader, four riflemen and three hostiles are loaded"):
        return
    for r in riflemen:   # armed BEFORE the run: the posture log exists from the first decision on
        call("POST", "/trace/observe", {"networkId": r, "on": True})
    call("POST", "/sim/play", {})

    # ① the leader's squad: four members, the merged pool holds the three hostiles
    sq = wait_for(lambda: (x := squad(leader)) and x.get("memberCount") == 4 and x.get("contactCount", 0) >= 3 and x, timeout)
    if not c.ok(sq is not None, "the squad forms (4 members) and its merged pool holds the three hostiles"):
        return

    # ② every member is assigned a target; the fire is spread; no target has more than two
    def assigned():
        x = squad(leader)
        a = [((m.get("assignment") or {}).get("name")) for m in x.get("members", [])]
        return len(a) == 4 and all(a) and a
    a = wait_for(assigned, timeout)
    print(f"    assignments: {a}")
    if not c.ok(a is not None, "every member is assigned a target (the fire distribution runs, CE-3088)"):
        return
    c.ok(len(set(a)) >= 2, f"the fire is spread over {len(set(a))} targets")
    c.ok(all(a.count(t) <= 2 for t in set(a)), "no target has more than two members (the focus-fire cap)")

    # ③ a member near death breaks off — WHILE the hostiles live (600 HP each: with 100 the squad killed them in seconds and
    #   a hurt member rightly kept advancing — nobody left to flee from, measured live 2026-10-06): its own posture turns defensive (the "veto", §10.3 — a consideration, not an order)
    hurt = riflemen[3]
    set_health(hurt, 10)
    w = wait_for(lambda: (x := winner_of(hurt)[0]) in ("TakeCover", "Flee", "HoldProne") and x, 30)
    # ⭐ CE-3090 FIXED (behaviors, 2026-10-07, docs/DESIGN_Decision_Layer.md §3.3f) — on open desert there is no cover and no hidden
    #   retreat, so the wounded member HOLDS PRONE (stops, lies down, returns fire). ⛔ SUPERSEDED: reported, not asserted — TakeCover
    #   and Flee both read 0 and it kept advancing. ⚠ Read the winner while the hostiles LIVE: once they die it rightly stands up.
    _, d = winner_of(hurt)
    c.ok(w == "HoldProne", f"a member at 10 HP on open ground breaks off: winner {w or (d and d.get('winner'))} (CE-3090: HoldProne); "
                          f"ranked {d and [(r.get('option'), r.get('score')) for r in d.get('ranked', [])]}")
    # ⭐ CE-2121 — and asks its body to lie down (StanceIntent on the Brain)
    si = ((data(call("GET", f"/entities/{hurt}")) or {}).get("Components") or {}).get("StanceIntent") or {}
    c.ok(si.get("TargetStance") == "Prone" or w != "HoldProne", f"the hurt member asked to lie down (StanceIntent {si})")

    # ④ each member SPENDS rounds (its posture fires at its top threat, which the assignment biases)
    def all_fired():
        r = [ammo(m) for m in riflemen]
        return all(x[0] is not None and x[1] and x[0] < x[1] for x in r) and r
    fired = wait_for(all_fired, timeout)
    c.ok(fired is not None, f"every member spends rounds ({fired})")


# ── U5 (CE-3089) — weapon choice: the Bradley's 25 mm at the insurgent, the TOW at the T-72 ──────────────────────────────
#   docs/DESIGN_Utility_AI_Demo_Scenarios.md §12. The evidence is the TARGETS: only a TOW (800 mm) penetrates the T-72's 500 mm
#   front armour (the 25 mm's 60 mm cannot), so the tank losing health proves the TOW fired; the 25 mm is the owner's ammo.

def health(nid):
    h = ((data(call("GET", f"/entities/{nid}")) or {}).get("Components") or {}).get("Health") or {}
    return h.get("Current")


def run_weapon_choice(c, timeout):
    ids = ids_by_name()
    brad, ins, tank = ids.get("Bradley"), ids.get("Insurgent"), ids.get("T-72")
    if not c.ok(None not in (brad, ins, tank), "the Bradley, the insurgent and the T-72 are loaded"):
        return
    gun0, _ = ammo(brad)
    tank0 = health(tank)
    print(f"    start: 25 mm rounds {gun0}, T-72 health {tank0}")
    call("POST", "/sim/play", {})

    # ⚠ a 25 mm round GRAZES the tank (expected damage, ~25 HP — measured live 2026-10-06); only a TOW (2000 per hit) takes
    #   ≥ 1000 off in one go. So the evidence is the size of the drop, not that there is one.
    hit = wait_for(lambda: (h := health(tank)) is not None and tank0 is not None and tank0 - h >= 1000, timeout * 2)
    c.ok(hit is not None, f"the T-72 takes a TOW hit (≥ 1000 HP — the 25 mm only grazes it): {tank0} → {health(tank)}")
    dead = wait_for(lambda: (h := health(ins)) is not None and h <= 0, timeout * 2)   # ⚠ not "and h": 0 is falsy
    c.ok(dead is not None, "the insurgent is killed")
    gun1, _ = ammo(brad)
    c.ok(gun0 is not None and gun1 is not None and gun1 < gun0, f"the 25 mm (the owner's mount 0) spent rounds ({gun0} → {gun1})")
    c.ok(gun0 is not None and gun1 is not None and gun0 - gun1 < 60,
         f"…a burst, not the magazine into the tank ({gun0 - gun1 if gun1 is not None and gun0 is not None else '?'} rounds)")


# ── U3 (CE-3082/3083) — one decision, three hosts: BTree CombatPosture, HSM CombatPostureHsm, blueprint CombatPostureBp ──────
#   R-197. The SAME Health edits on every host ⇒ the same fight/defend class at every step; the exact winners are printed side by
#   side (a defensive winner depends on the EQS answers where each unit stands — U1's measured caveat — and the HSM switches one
#   tick later, behaviors' G4 note). Hosts present in the scenario are checked; a missing one is reported, not failed.

def run_three_hosts(c, timeout):
    ids = ids_by_name()
    hosts = {n: ids[n] for n in ("Rifleman BTree", "Rifleman HSM", "Rifleman Blueprint") if n in ids}
    print(f"    hosts: {sorted(hosts)}")
    if not c.ok(len(hosts) >= 2 and "Rifleman BTree" in hosts, "at least the BTree and one other host are loaded"):
        return
    for nid in hosts.values():
        call("POST", "/trace/observe", {"networkId": nid, "on": True})
    call("POST", "/sim/play", {})

    first = wait_for(lambda: all(winner_of(n)[0] == "Suppress" for n in hosts.values()) and True, timeout)
    c.ok(first is not None, f"every host starts at Suppress (healthy, armed, matched enemy): {[winner_of(n)[0] for n in hosts.values()]}")

    FIGHT, DEFEND = ("Suppress", "AdvanceAndAttack"), ("TakeCover", "Flee")
    for hp, want, what in ((40, DEFEND, "hurt"), (100, FIGHT, "healed"), (10, DEFEND, "near death"), (100, FIGHT, "healed again")):
        for nid in hosts.values():
            set_health(nid, hp)
        got = wait_for(lambda: all(winner_of(n)[0] in want for n in hosts.values()) and True, timeout)
        winners = {name: winner_of(nid)[0] for name, nid in hosts.items()}
        c.ok(got is not None, f"{what} ({hp} HP) ⇒ every host {'defends' if want is DEFEND else 'fights'}: {winners}")
        if len(set(winners.values())) > 1:
            print(f"    ⚠ exact winners differ at {what}: {winners}")


# ── U4 (CE-3084) — attack approach: out of sight of an identified target, the advance flanks or takes a firing position ──
#   docs/DESIGN_Utility_AI_Demo_Scenarios.md §4 U4; Decision Layer §3.3e (behaviors' G6). The hostile shows itself north of the High
#   Wall, then its own mission walks it behind the wall; the rifleman (advancing on an unarmed hostile ⇒ AdvanceAndAttack) loses
#   sight of an IDENTIFIED target ⇒ AttackApproach picks Flank or FiringPosition.

APPROACH = "Attack approach"


def run_attack_approach(c, timeout):
    ids = ids_by_name()
    rifle, hostile = ids.get("Rifleman"), ids.get("Hidden Hostile")
    if not c.ok(rifle is not None and hostile is not None, "the Rifleman and the Hidden Hostile are loaded"):
        return
    call("POST", "/trace/observe", {"networkId": rifle, "on": True})
    call("POST", "/sim/play", {})

    both = wait_for(lambda: (u := utility(rifle)) and decision(u, POSTURE) and decision(u, APPROACH) and u, timeout)
    c.ok(both is not None, "/entities/{id}/utility lists BOTH decisions (the posture and the approach nested in it)")
    adv = wait_for(lambda: winner_of(rifle)[0] == "AdvanceAndAttack" and True, timeout)
    c.ok(adv is not None, f"against an unarmed hostile the posture advances (winner {winner_of(rifle)[0]})")
    seen = set()

    def flanking():
        d = decision(utility(rifle), APPROACH)
        w = d and d.get("winner")
        if w: seen.add(w)
        return w in ("Flank", "FiringPosition") and w
    w = wait_for(flanking, timeout * 2)
    print(f"    approach winners seen: {sorted(seen)}")
    c.ok(w is not None, f"out of sight of the identified hostile ⇒ the approach flanks or takes a firing position ({w})")
    a0, _ = ammo(rifle)
    fired = wait_for(lambda: (x := ammo(rifle)[0]) is not None and a0 is not None and x < a0 and x, timeout * 2)
    c.ok(fired is not None, f"…and from there it regains sight and fires ({a0} → {fired})")


# ── bt-doors (CE-3104, buildings 5d) — a locked front door is routed round; a closed back door is opened on the way ─────
#   docs/DESIGN_Building_Interiors.md §3j "5d-3 / 5d-4 as built". House A (bt-range, SW corner (100,100)): front (104.2,100), 1.2 m
#   LOCKED by the terrain, back (107.4,108) CLOSED by the scenario's TerrainObjects section, hall (105,106) open. The Visitor
#   starts south of the locked front and is ordered into the west room. The Locksmith (5d-2) runs DoorLocksmith on the front.

def doors_by_key():
    return {d["key"]: d for d in (data(call("GET", "/doors")) or {}).get("doors") or []}


def run_doors(c, timeout):
    ids = ids_by_name()
    visitor = ids.get("Visitor")
    if not c.ok(visitor is not None, "the Visitor is loaded"):
        return
    front, back, hall = "bt-range/House A/front", "bt-range/House A/back", "bt-range/House A/hall"
    d = wait_for(lambda: (x := doors_by_key()) and all((x.get(k) or {}).get("runtimeId") for k in (front, back, hall)) and x, timeout)
    if not c.ok(d is not None, "House A's doors exist as entities"):
        return
    c.ok(d[front]["state"] == "Locked", f"the front door is Locked (terrain) — {d[front]['state']}")
    c.ok(d[back]["state"] == "Closed", f"the back door is Closed (the scenario's TerrainObjects section) — {d[back]['state']}")
    c.ok(d[hall]["state"] == "Open", f"the hall door is Open — {d[hall]['state']}")
    call("POST", "/sim/play", {})

    # the Locksmith (④) works the front door at the same time — its positions are sampled from the start
    smith = ids.get("Locksmith")
    smith_front = []
    def watch_smith():
        p = position(smith) if smith is not None else None
        if p and 103.5 <= p[0] <= 104.9 and 99.5 <= p[1] <= 101.0:
            smith_front.append(p)
        return p

    # ① the route goes round the locked front: the Visitor never enters the house by the front doorway
    went_in_front = []
    def watch():
        watch_smith()
        p = position(visitor)
        if p and 103.5 <= p[0] <= 104.9 and 99.5 <= p[1] <= 101.0:
            went_in_front.append(p)
        return p

    # ② it stops at the CLOSED back door and opens it; the door opens while the Visitor stands at it
    opened = wait_for(lambda: (watch() and doors_by_key().get(back, {}).get("state") == "Open") and position(visitor), timeout * 2)
    if not c.ok(opened is not None, "the back door is opened"):
        return
    dist = ((opened[0] - 107.4) ** 2 + (opened[1] - 108.0) ** 2) ** 0.5
    c.ok(dist <= 3.0, f"by the Visitor standing at it ({dist:.1f} m from the doorway)")

    # ③ it walks on, through the hall door, into the west room
    objective = (102.0, 104.0)
    arrived = wait_for(lambda: (p := watch()) and ((p[0] - objective[0]) ** 2 + (p[1] - objective[1]) ** 2) ** 0.5 <= 1.5 and p, timeout * 2)
    c.ok(arrived is not None, f"the Visitor arrives in the west room {objective}")
    c.ok(not went_in_front, f"never through the locked front doorway ({went_in_front[:1]})")

    # ④ 5d-2 — the Locksmith runs the curated DoorLocksmith tree (MoveToDoor → Unlock → Open → walk in): the front ends Open,
    #    and the walk after it goes through the front (the next path uses the door it opened)
    if not c.ok(smith is not None, "the Locksmith is loaded"):
        return
    opened_front = wait_for(lambda: (watch_smith() or True) and doors_by_key().get(front, {}).get("state") == "Open", timeout * 2)
    c.ok(opened_front is not None, "the Locksmith unlocks and opens the front door")
    inside = (102.0, 103.0)
    got_in = wait_for(lambda: (p := watch_smith()) and ((p[0] - inside[0]) ** 2 + (p[1] - inside[1]) ** 2) ** 0.5 <= 1.5 and p, timeout * 2)
    c.ok(got_in is not None, f"the Locksmith walks in to {inside}")
    c.ok(bool(smith_front), "through the front doorway it opened")


SCENARIOS = {"ua-posture": run_posture, "ua-threat-ranking": run_threat_ranking, "ua-danger-crossing": run_danger_crossing,
             # CE-3079 B7 — the same cast and the same acceptance, the rifleman's task the BLUEPRINT DangerCrossingBp (H7)
             "ua-danger-crossing-bp": run_danger_crossing,
             "ua-fire-distribution": run_fire_distribution,
             "ua-weapon-choice": run_weapon_choice,
             "ua-three-hosts": run_three_hosts,
             "ua-attack-approach": run_attack_approach,
             "bt-doors": run_doors}


def main():
    global BASE
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("scenario", choices=sorted(SCENARIOS))
    ap.add_argument("--port", type=int, default=8111)
    ap.add_argument("--timeout", type=float, default=60, help="seconds to wait for each expected change")
    ap.add_argument("--launch", action="store_true",
                    help="start a fresh ClusterRunner --mode all for this run and stop it after")
    a = ap.parse_args()
    BASE = f"http://localhost:{a.port}"

    proc = None
    if a.launch:
        if data(call("GET", "/status")):
            sys.exit(f"something already answers on {BASE} — stop it, or run without --launch")
        proc = launch(a.port)
    elif not data(call("GET", "/status")):
        sys.exit(f"no debug API at {BASE} — launch ClusterRunner --mode all with HROT_DEBUG_API_PORT={a.port}, or pass --launch")
    try:
        print(f"== {a.scenario}")
        load(a.scenario)
        c = Check()
        t0 = sim_time()
        SCENARIOS[a.scenario](c, a.timeout)
        print(f"== simTime {t0} → {sim_time()}")
    finally:
        if proc:
            stop(proc)
    if c.failures:
        print(f"FAIL — {len(c.failures)} check(s) failed")
        sys.exit(1)
    print("PASS")


if __name__ == "__main__":
    main()
