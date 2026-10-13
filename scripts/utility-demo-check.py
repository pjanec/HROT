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


# ── U3 (CE-3082/3083) — one decision, three riflemen; since CE-3160 (R-266) all three run the ONE C#-built CombatPosture ──────
#   R-197. The SAME Health edits on every host ⇒ the same fight/defend class at every step; the exact winners are printed side by
#   side (a defensive winner depends on the EQS answers where each unit stands — U1's measured caveat — and the HSM switches one
#   tick later, behaviors' G4 note). Hosts present in the scenario are checked; a missing one is reported, not failed.

def run_three_hosts(c, timeout):
    ids = ids_by_name()
    hosts = {n: ids[n] for n in ("Rifleman A", "Rifleman B", "Rifleman C") if n in ids}
    print(f"    hosts: {sorted(hosts)}")
    if not c.ok(len(hosts) >= 2 and "Rifleman A" in hosts, "at least two riflemen are loaded"):
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
#   docs/DESIGN_Building_Interiors.md §3j "5d-3 / 5d-4 as built". House A (bt-range, SW corner (100,100)): front (104.2,100), 0.9 m (real width since the tiled bake, CE-3111)
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


# ── bt-grenade-posture / bt-mortar-roof (CE-1032, buildings Stage 6) — warheads: blast and fragments ──────────────────────────
#   docs/DESIGN_Building_Interiors.md §3k. Premises in bt-range/premises.json (DemoPremisesTests). The burst is read back from
#   GET /combat/detonations — what AreaEffectSystem decided — and the damage from each unit's Health.

def detonations():
    # ⚠ the area effect runs where damage is assessed (SimHost): read its log there, then give the checks their perspective back
    call("POST", "/perspective", {"name": "SimHost"})
    try:
        return (data(call("GET", "/combat/detonations?last=10")) or {}).get("detonations") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})


def effect_on(d, nid):
    return next((e for e in d.get("effects") or [] if e.get("entity") == nid), None)


def run_grenade_posture(c, timeout):
    ids = ids_by_name()
    standing, prone = ids.get("Standing"), ids.get("Prone")
    if not c.ok(None not in (ids.get("Thrower"), standing, prone), "the Thrower, Standing and Prone are loaded"):
        return
    call("POST", "/sim/play", {})
    dets = wait_for(lambda: (x := detonations()) and x, timeout + 10)   # the throw (~1.5 s) + the 4.5 s fuze
    if not c.ok(dets is not None, "the grenade burst (GET /combat/detonations)"):
        return
    d = dets[0]
    c.ok("M67" in d.get("warheadSource", ""), f"its warhead is the M67's — {d.get('warheadSource', '')[:60]}")
    b = d.get("burst") or [0, 0, 0]
    c.ok(abs(b[1] - 35) < 2.0 and b[2] < 0.5, f"it burst on the ground ~5 m in front of the Low Wall — {b}")
    es, ep = effect_on(d, standing), effect_on(d, prone)
    if not c.ok(es is not None and ep is not None, "both men were in reach"):
        return
    c.ok(es["fragmentExposure"] >= 0.5, f"the STANDING man is exposed over the wall — exposure {es['fragmentExposure']:.2f} (chest and head)")
    c.ok(ep["fragmentExposure"] <= 0.1 and ep["stance"] == "Prone", f"the PRONE man is not — exposure {ep['fragmentExposure']:.2f}, {ep['stance']}")
    wait_for(lambda: (h := health(standing)) is not None and h < 99, 5)   # ⚠ a predicate, not the value: Health 0 is falsy
    hs, hp = health(standing), health(prone)
    c.ok(hs is not None and hs < 99, f"the standing man is hurt — Health {hs}")
    c.ok(hp is not None and hp >= 99, f"the prone man is not — Health {hp}")


def run_mortar_roof(c, timeout):
    ids = ids_by_name()
    down, up, yard = ids.get("Downstairs"), ids.get("Upstairs"), ids.get("Courtyard")
    if not c.ok(None not in (down, up, yard, ids.get("Mortar Roof"), ids.get("Mortar Yard")), "both mortars and the three men are loaded"):
        return
    call("POST", "/sim/play", {})
    dets = wait_for(lambda: (x := detonations()) and len(x) >= 2 and x, timeout + 20)   # two bombs, ~12 s of flight each
    if not c.ok(dets is not None, f"both bombs burst — {len(detonations())}"):
        return
    roof = next((d for d in dets if (d.get("burst") or [0, 0, 0])[2] > 5.5), None)
    yard_burst = next((d for d in dets if (d.get("burst") or [0, 0, 9])[2] < 0.5), None)
    c.ok(roof is not None, f"one burst on House A's roof (z ≈ 6.1) — {[d.get('burst') for d in dets]}")
    c.ok(yard_burst is not None, "one burst in the courtyard")
    if roof is not None:
        e = effect_on(roof, down)
        c.ok(e is None or (e["fragmentExposure"] == 0 and e["blastBarrier"] < 0.1),
             f"the roof burst reaches the man downstairs only through the slabs — {e and (e['fragmentExposure'], e['blastBarrier'])}")
    wait_for(lambda: (h := health(yard)) is not None and h < 80, 5)   # ⚠ a predicate, not the value: Health 0 is falsy
    hy = health(yard)
    c.ok(hy is not None and hy < 80, f"the man in the open courtyard is badly hurt — Health {hy}")
    hd = health(down)
    c.ok(hd is not None and hd >= 90, f"the man downstairs is spared — Health {hd}")


# ── bt-window-duel (CE-3136 P-8, D10) — the window duel: PeekAndFire both sides ──────────────────────────────────────────────
#   docs/DESIGN_Peek_And_Fire.md §2, §8, P-8. Premises in bt-range/premises.json (DemoPremisesTests). A (upstairs in House A)
#   rotates its windows; B (in the street, behind Van 1) steps out to fire, and when its cover is used up suppresses and bounds
#   to Van 2. ⭐ NO HTTP WRITE: the duel plays out on its own.

def run_window_duel(c, timeout):
    ids = ids_by_name()
    a, b = ids.get("Window Rifleman"), ids.get("Street Rifleman")
    if not c.ok(None not in (a, b, ids.get("Van 1"), ids.get("Van 2")), "both riflemen and both vans are loaded"):
        return
    a0, b0 = ammo(a)[0], ammo(b)[0]
    call("POST", "/sim/play", {})

    spots, b_spots = set(), set()
    # House A's window firing positions, both storeys (bt-range's cover database) — at a window, not on the way between two
    windows = [(102.1, 100.9, 0), (109.1, 103.6, 0), (102.1, 100.9, 3), (107.1, 100.9, 3), (105.4, 107.1, 3)]
    def watch():
        pa, pb = position(a), position(b)
        if pa:
            for w in windows:
                if abs(pa[2] - w[2]) < 1 and ((pa[0] - w[0]) ** 2 + (pa[1] - w[1]) ** 2) ** 0.5 <= 0.75:
                    spots.add(w)
        if pb: b_spots.add((round(pb[0]), round(pb[1])))
        return pa and pb
    def fired(nid, start):
        n = ammo(nid)[0]
        return n is not None and start is not None and n < start

    # ① both expose and fire (aimed or a blind burst — each side's ammunition falls)
    both = wait_for(lambda: watch() and fired(a, a0) and fired(b, b0), timeout * 2)
    c.ok(both is not None, f"both fire — A ammo {a0}→{ammo(a)[0]}, B ammo {b0}→{ammo(b)[0]}")

    # ② A uses at least two windows (the heat / exposure count moves it on); ③ B bounds to Van 2's cover
    van2 = (113.0, 76.0)
    def bounded():
        watch()
        pb = position(b)
        return pb and ((pb[0] - van2[0]) ** 2 + (pb[1] - van2[1]) ** 2) ** 0.5 <= 4.0 and pb
    two_windows = lambda: watch() and len(spots) >= 2
    c.ok(wait_for(two_windows, timeout * 4, every=0.25) is not None,
         f"A fires from at least two windows — {sorted(spots)}")
    c.ok(wait_for(bounded, timeout * 4, every=0.25) is not None, f"B bounds to Van 2's cover — B was at {sorted(b_spots)[:12]}")

    ha, hb = health(a), health(b)
    print(f"    health: A {ha}, B {hb}; ammo: A {ammo(a)[0]}, B {ammo(b)[0]}")


# ── R-e (CE-2152 C-c) — casualty care: the squad covers, the medic brings a man who is down to cover ───────────────────
#   docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.8.2. Reads GET /entities/{squad}/squad ▸ maneuver (O-a, CE-2157).

def maneuver(nid):
    return (squad(nid) or {}).get("maneuver") or {}


def comps(nid):
    return (data(call("GET", f"/entities/{nid}")) or {}).get("Components") or {}


def flat(a, b):
    return ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5


def _care_cast(casualty_name):
    ids = ids_by_name()
    sq, cas, medic = ids.get("Squad"), ids.get(casualty_name), ids.get("Medic")
    names = ["Leader", "Medic", "Rifleman 1", "Rifleman 2", "Rifleman 3", "Rifleman 4"]
    members = {n: ids.get(n) for n in names}
    return ids, sq, cas, medic, members


def _care_after_down(c, timeout, sq, cas, medic, members, casualty_name, t_down, contact=True):
    """③–⑨ of §7.8.2, from the moment the man is down."""
    medic_from = position(medic)
    coverers = [nid for n, nid in members.items() if n not in ("Medic", casualty_name)]
    ammo0 = {nid: ammo(nid)[0] for nid in coverers}
    first_fire, medic_moved_at = {}, None
    care = None
    end = time.time() + timeout * 2
    while time.time() < end:
        m = maneuver(sq)
        if m.get("kindName") == "CasualtyCare" and m.get("care"):
            care = m["care"]
        for nid in coverers:
            a = ammo(nid)[0]
            if nid not in first_fire and a is not None and ammo0[nid] is not None and a < ammo0[nid]:
                first_fire[nid] = sim_time()
        p = position(medic)
        if medic_moved_at is None and p and medic_from and flat(p, medic_from) > 1.0:
            medic_moved_at = sim_time()
        if "IsEmbarkedTag" in comps(cas):
            break
        time.sleep(0.25)
    c.ok(care is not None, "the squad runs the care drill (maneuver kindName CasualtyCare)")
    if care is None:
        return
    c.ok((care.get("medic") or {}).get("name") == "Medic", f"the Medic goes, nobody else (care.medic = {care.get('medic')})")
    print(f"    coverers' first round at {first_fire}; the medic moved at {medic_moved_at}")
    if contact:
        c.ok(medic_moved_at is not None and all(nid in first_fire and first_fire[nid] <= medic_moved_at for nid in coverers),
             "covering fire BEFORE the medic moves: every coverer's rounds went down before he was 1 m out")
    else:   # nothing left to fight: nobody to suppress — the coverers hold (§7.5.2)
        c.ok(medic_moved_at is not None, "with nothing left to fight the medic goes without waiting for covering fire")
    c.ok("IsEmbarkedTag" in comps(cas), "he takes the man (the casualty carries IsEmbarkedTag)")
    m = maneuver(sq)
    cover = (m.get("care") or {}).get("coverPoint")
    laid = wait_for(lambda: "IsEmbarkedTag" not in comps(cas) and position(cas), timeout)
    c.ok(laid is not None, "he puts him down")
    if cover and laid:
        print(f"    cover point {cover}, casualty at {laid}, medic at {position(medic)}")
        c.ok(flat(laid, cover) <= 2.5, "the man lies within 2.5 m of the cover point")
    c.ok((health(cas) or 0) > 0, f"he is alive ({health(cas)} HP)")
    # ⚠ the route keeps the LAST drill when none runs (active false), so "ended" is either another drill or this one inactive
    back = wait_for(lambda: (x := maneuver(sq)) and (x.get("kindName") != "CasualtyCare" or not x.get("active")) and x, 10)
    c.ok(back is not None, f"the squad goes back to the fight, or ends the reaction when nothing is left "
                           f"(drill now {back and back.get('kindName')}, active {back and back.get('active')})")
    m = maneuver(sq)
    slot = next((x for x in m.get("members", []) if x.get("networkId") == cas), None)
    o = (slot or {}).get("order")
    c.ok(o is None or (o.get("issuedAt") or 0) < (t_down or 0), "the man down got no order after he went down (R-a)")


def run_casualty_care(c, timeout, casualty_name="Rifleman 3"):
    ids, sq, cas, medic, members = _care_cast(casualty_name)
    if not c.ok(None not in (sq, cas, medic) and None not in members.values() and ids.get("Hostile"), "the squad, its Medic and the hostile are loaded"):
        return
    call("POST", "/trace/observe", {"networkId": sq, "on": True})
    call("POST", "/sim/play", {})
    pool = wait_for(lambda: (x := squad(sq)) and x.get("contactCount", 0) >= 1 and x, timeout)
    if not c.ok(pool is not None, "the squad's pool holds the hostile (Rifleman 1 sees him past the tower)"):
        return
    set_health(cas, 25)
    t_down = sim_time()
    down = wait_for(lambda: (comps(sq).get("RecentSenses") or {}).get("SquadMemberDown", 0) > 0 and comps(sq)["RecentSenses"], 10)
    c.ok(down is not None, "the squad knows a man is down (SquadMemberDown on the commander)")
    d = wait_for(lambda: (x := decision(utility(sq), "Squad contact response")) and x.get("winner") == "Rescue" and x, 15)
    c.ok(d is not None, f"Rescue wins the squad's decision ({decision(utility(sq), 'Squad contact response')})")
    if casualty_name == "Leader":
        newl = wait_for(lambda: any(((comps(n).get("UnitSubordinate") or {}).get("Designation") == "SquadLeader")
                                     for k, n in members.items() if k != "Leader") and True, 5)
        c.ok(newl is not None, "the next man takes command (R-a)")
    _care_after_down(c, timeout, sq, cas, medic, members, casualty_name, t_down)


def run_casualty_care_leader(c, timeout):
    run_casualty_care(c, timeout, casualty_name="Leader")


def run_casualty_care_heavy(c, timeout):
    ids, sq, cas, medic, members = _care_cast("Rifleman 3")
    hostiles = [ids.get(f"Hostile {i}") for i in (1, 2, 3)]
    if not c.ok(None not in (sq, cas, medic) and None not in hostiles, "the squad, its Medic and three hostiles are loaded"):
        return
    call("POST", "/trace/observe", {"networkId": sq, "on": True})
    call("POST", "/sim/play", {})
    if not c.ok(wait_for(lambda: (x := squad(sq)) and x.get("contactCount", 0) >= 1 and x, timeout) is not None, "the squad is in contact"):
        return
    set_health(cas, 25)
    t_down = sim_time()
    t_end = (sim_time() or 0) + 30
    rescued_early, sent = False, False
    while (sim_time() or 0) < t_end:
        m = maneuver(sq)
        if m.get("kindName") == "CasualtyCare": rescued_early = True
        if any(((x.get("order") or {}).get("kind") == "CarryCasualty") for x in m.get("members", [])): sent = True
        time.sleep(0.5)
    u = decision(utility(sq), "Squad contact response")
    print(f"    after 30 s under fire: {u}")
    c.ok(not rescued_early, "under heavy fire the squad does NOT go for him for 30 s")
    # ⚠ not "the Medic stays put": the winning drill (Assault, measured) moves the whole squad, the Medic too
    c.ok(not sent, "nobody is sent for him (no CarryCasualty order)")
    c.ok("IsEmbarkedTag" not in comps(cas), "nobody carries the man yet")
    for h in hostiles:
        set_health(h, 0)
    go = wait_for(lambda: maneuver(sq).get("kindName") == "CasualtyCare", 20)
    c.ok(go is not None, "with the enemy down the squad goes for him at once (empty pool ⇒ Rescue)")
    _care_after_down(c, timeout, sq, cas, medic, members, "Rifleman 3", t_down, contact=False)


# ── F-e (CE-2158) — fire and movement: in contact the squad closes by halves, one suppressing, the other rushing to cover ───
#   docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.8.3 / §7.6.1. Reads GET /entities/{squad}/squad ▸ maneuver (O-a).

def run_fire_and_movement(c, timeout):
    ids = ids_by_name()
    sq = ids.get("Squad")
    names = ["Leader", "Rifleman 1", "Rifleman 2", "Rifleman 3", "Rifleman 4", "Rifleman 5"]
    members = [ids.get(n) for n in names]
    hostiles = [ids.get("Hostile 1"), ids.get("Hostile 2")]
    if not c.ok(sq and None not in members and None not in hostiles, "the squad, six men and two hostiles are loaded"):
        return
    target = (200.0, 285.0)
    def centre():
        ps = [position(m) for m in members]
        ps = [p for p in ps if p]
        return (sum(p[0] for p in ps) / len(ps), sum(p[1] for p in ps) / len(ps)) if ps else None
    call("POST", "/trace/observe", {"networkId": sq, "on": True})
    c0 = centre()
    d0 = flat(c0, target) if c0 else None
    call("POST", "/sim/play", {})
    m = wait_for(lambda: (x := maneuver(sq)) and x.get("kindName") == "FireAndMovement" and x, 20)
    if not c.ok(m is not None, "the squad runs the drill (maneuver kindName FireAndMovement)"):
        return
    # ⚠ the drill starts before the pool holds the hostiles: until then the base of fire HOLDS (nothing to suppress, §7.6.1)
    def split():
        k = sorted(((x.get("order") or {}).get("kind") or "-") for x in maneuver(sq).get("members", []))
        return k if k.count("Suppress") >= 2 and (k.count("Hold") + k.count("AdvanceTo")) >= 2 else None
    kinds = wait_for(split, 15)
    print(f"    orders once in contact: {kinds}")
    c.ok(kinds is not None, "① the halves split: one half suppresses, the other holds or rushes")
    history, rush_lengths, rush_cover, both_up, last_pos, last_t = {}, [], [], 0.0, {}, None
    seen_orders = set()
    end = (sim_time() or 0) + 90
    while (sim_time() or 0) < end:
        t = sim_time() or 0
        m = maneuver(sq)
        if m.get("kindName") != "FireAndMovement" and history:
            break
        for h in m.get("history", []):
            history.setdefault((h.get("t"), h.get("phase")), h)
        for x in m.get("members", []):
            o = x.get("order") or {}
            key = (x.get("networkId"), o.get("issuedAt"))
            if o.get("kind") == "AdvanceTo" and key not in seen_orders:
                seen_orders.add(key)
                at = position(x.get("networkId"))
                if at and o.get("point"):
                    rush_lengths.append(round(flat(at, o["point"]), 1))
                    rush_cover.append(bool(o.get("cover")))
        moving = {}
        for i, nid in enumerate(members):
            p = position(nid)
            if p and nid in last_pos and last_t is not None and t > last_t:
                moving[i] = flat(p, last_pos[nid]) / (t - last_t) > 0.5
            if p: last_pos[nid] = p
        half_a = any(moving.get(i) for i in range(0, 3)); half_b = any(moving.get(i) for i in range(3, 6))
        if half_a and half_b and last_t is not None: both_up += t - last_t
        last_t = t
        time.sleep(0.5)
    hs = sorted(history.values(), key=lambda h: h.get("t") or 0)
    print(f"    history: {[(h.get('t'), h.get('phaseName'), h.get('event')) for h in hs]}")
    print(f"    rushes: {rush_lengths} cover {rush_cover}; both halves moving for {both_up:.1f} s")
    rushes = [h for h in hs if (h.get("phaseName") or "").startswith("Rush")]
    swaps = [h for h in hs if (h.get("phaseName") or "").startswith("Cover") and h.get("event") in ("DefiladeReached", "Timeout")]
    c.ok(rushes and all(h.get("event") in ("ShotFired", "Timeout") for h in rushes),
         "② a rush starts on 'Covering!' (ShotFired) — or the covering timeout")
    c.ok(sum(1 for h in rushes if h.get("event") == "ShotFired") >= 1, "② at least one rush started on real covering fire")
    c.ok(rush_lengths and max(rush_lengths) <= 17.0, f"③ every rush ≤ 17 m (longest {max(rush_lengths) if rush_lengths else None})")
    c.ok(len(swaps) >= 3, f"④ ≥ 3 swaps in 90 s ({len(swaps)}; {sum(1 for h in swaps if h.get('event') == 'DefiladeReached')} on 'Set!')")
    c1 = centre()
    gained = (d0 - flat(c1, target)) if (c1 and d0) else 0
    c.ok(gained >= 30, f"⑤ it closes the distance (the squad's centre is {gained:.0f} m nearer)")
    c.ok(both_up <= 1.0, f"⑥ never both halves up for more than 1 s ({both_up:.1f} s)")


# ── F-e (CE-2158) — bounding overwatch: one half watches (suppresses on contact) while the other bounds to cover ───────────
#   docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.8.3. Reads GET /entities/{squad}/squad ▸ maneuver (O-a).

def run_bounding_overwatch(c, timeout):
    ids = ids_by_name()
    sq = ids.get("Squad")
    names = ["Leader", "Rifleman 1", "Rifleman 2", "Rifleman 3", "Rifleman 4", "Rifleman 5"]
    members = [ids.get(n) for n in names]
    hostile = ids.get("Hostile")
    if not c.ok(sq and None not in members and hostile, "the squad, six men and the hostile are loaded"):
        return
    call("POST", "/trace/observe", {"networkId": sq, "on": True})
    ammo0 = {m: ammo(m)[0] for m in members}
    call("POST", "/sim/play", {})
    m = wait_for(lambda: (x := maneuver(sq)) and x.get("kindName") == "BoundingOverwatch" and x, 20)
    if not c.ok(m is not None, "the squad runs the drill (maneuver kindName BoundingOverwatch)"):
        return
    history, last_pos, last_t, overlap, longest, longest_at = {}, {}, None, 0.0, 0.0, None
    fired_before_contact, contact_at, suppress_at, fire_after = False, None, None, None
    bounds, covered = 0, 0
    seen = set()
    cover_orders = {}   # (member, issuedAt) -> [point, closest he got]
    end = (sim_time() or 0) + 150
    while (sim_time() or 0) < end:
        t = sim_time() or 0
        m = maneuver(sq)
        if m.get("kindName") != "BoundingOverwatch" or not m.get("active", True):
            break
        for h in m.get("history", []):
            history.setdefault((h.get("t"), h.get("phase")), h)
        orders = {x.get("networkId"): (x.get("order") or {}) for x in m.get("members", [])}
        for nid, o in orders.items():
            key = (nid, o.get("issuedAt"))
            if key in seen: continue
            seen.add(key)
            for (other, at), rec in cover_orders.items():   # a newer order to another point supersedes his cover order
                if other == nid and at != o.get("issuedAt") and rec[0] != o.get("point"): rec.append("superseded")
            if o.get("kind") == "AdvanceTo":
                bounds += 1
                if o.get("cover") and o.get("point"):
                    covered += 1; cover_orders[key] = [o["point"], 1e9]
        for (nid, _), rec in cover_orders.items():
            p = position(nid)
            if p: rec[1] = min(rec[1], flat(p, rec[0]))
        sqd = squad(sq) or {}
        # contact = the first man who sees him (the squad's merged pool lags a man's own memory by a tick or two)
        if contact_at is None and (sqd.get("contactCount", 0) >= 1
                                   or any((data(call("GET", f"/entities/{nid}/memory")) or {}).get("count", 0) > 0 for nid in members)):
            contact_at = t
        if contact_at is None and any((ammo(nid)[0] or 0) < (ammo0[nid] or 0) for nid in members):
            fired_before_contact = True
        if contact_at is not None and suppress_at is None and sum(1 for o in orders.values() if o.get("kind") == "Suppress") >= 2:
            suppress_at = t
        if suppress_at is not None and fire_after is None and any((ammo(nid)[0] or 0) < (ammo0[nid] or 0) for nid in members):
            fire_after = t
        moving = {}
        for i, nid in enumerate(members):
            p = position(nid)
            if p and nid in last_pos and last_t is not None and t > last_t:
                moving[i] = flat(p, last_pos[nid]) / (t - last_t) > 0.5
            if p: last_pos[nid] = p
        both = any(moving.get(i) for i in range(0, 3)) and any(moving.get(i) for i in range(3, 6))
        overlap = overlap + (t - last_t) if (both and last_t is not None) else 0.0
        if overlap > longest: longest, longest_at = overlap, t
        last_t = t
        time.sleep(0.5)
    hs = sorted(history.values(), key=lambda h: h.get("t") or 0)
    swaps = [h for h in hs if h.get("event") in ("DefiladeReached", "Timeout", "BoundComplete")]
    print(f"    history: {[(h.get('t'), h.get('phaseName'), h.get('event')) for h in hs]}")
    print(f"    bounds {bounds}, to cover {covered}; contact at {contact_at}, suppress at {suppress_at}, rounds after {fire_after}")
    # ⚠ 1.5 s, not §7.8.3's 1 s: positions are polled every 0.5 s over HTTP, and a man settling the last metre counts as moving
    c.ok(longest <= 1.5, f"① one half moves, the other watches (both moving at most {longest:.1f} s at a time"
                         + (f", ending t={longest_at:.1f})" if longest > 0 else ")"))
    reached = [rec[1] for rec in cover_orders.values() if "superseded" not in rec]
    print(f"    cover bounds: closest each mover got to his point {[round(r, 1) for r in reached]}")
    c.ok(covered >= 2, f"② bounds go to cover where there is cover ({covered} of {bounds} bound orders were cover points)")
    c.ok(reached and sum(1 for r in reached if r <= 1.5) >= 0.8 * len(reached),
         f"② … and end there (≥ 80 % of cover bounds within 1.5 m of the point)")
    set_share = sum(1 for h in swaps if h.get("event") == "DefiladeReached") / len(swaps) if swaps else 0
    c.ok(swaps and set_share >= 0.8, f"③ the swap is 'set', not the clock ({set_share:.0%} of {len(swaps)} swaps on DefiladeReached)")
    c.ok(not fired_before_contact, "④ nobody fires before contact")
    c.ok(contact_at is not None and suppress_at is not None and suppress_at - contact_at <= 3.5,
         f"⑤ on contact the watchers suppress within 3 s (contact {contact_at}, suppress {suppress_at})")
    c.ok(suppress_at is not None and fire_after is not None and fire_after - suppress_at <= 5.5, "⑤ … and their rounds go down within 5 s")


def run_squad_danger_crossing(c, timeout):
    """⭐ CE-2159 X-d (Squad Tactics §7.8.4) — a marching squad meets the north-south road: security watches the near side, the
    rest cross by pairs on new lanes, the far side watches, security crosses last, the squad reforms and marches on."""
    ids = ids_by_name()
    sq = ids.get("Squad")
    names = ["Leader"] + [f"Rifleman {i}" for i in range(1, 7)]
    members = [ids.get(n) for n in names]
    if not c.ok(sq and None not in members, "the squad and its seven men are loaded"):
        return
    call("POST", "/trace/observe", {"networkId": sq, "on": True})
    ammo0 = {m: ammo(m)[0] for m in members}
    call("POST", "/sim/play", {})
    s = wait_for(lambda: (x := danger_sensor(sq)) and areas_of(x) and x, 30)
    if not c.ok(s is not None, "the commander's route sensor lists the road ahead"):
        return
    area = areas_of(s)[0]
    print(f"    area: {area}")
    started = wait_for(lambda: (x := maneuver(sq)) and x.get("kindName") == "DangerAreaCrossing" and x.get("active", True) and x, 120)
    if not c.ok(started is not None, "① the crossing drill starts (maneuver kindName DangerAreaCrossing)"):
        return
    t0 = sim_time() or 0
    near, far = started and area["nearHandle"], area["farHandle"]
    dx, dy = far[0] - near[0], far[1] - near[1]
    L = (dx * dx + dy * dy) ** 0.5 or 1.0
    ux, uy = dx / L, dy / L
    def along(p):
        return (p[0] - near[0]) * ux + (p[1] - near[1]) * uy
    elements = {}
    history, inside_max, first_cross_t, security_entered_early = {}, 0, None, False
    crossed_at = {}
    reform_t = None
    end = t0 + 150
    while (sim_time() or 0) < end:
        t = sim_time() or 0
        m = maneuver(sq)
        for h in m.get("history", []):
            history.setdefault((h.get("t"), h.get("phase")), h)
        for x in m.get("members", []):
            elements[x.get("networkId")] = x.get("role")
        phase = m.get("phaseName")
        inside = 0
        for nid in members:
            p = position(nid)
            if not p: continue
            a = along(p)
            if 0 < a < L: inside += 1
            if a >= L and nid not in crossed_at: crossed_at[nid] = t
            if 0 < a and first_cross_t is None: first_cross_t = t
        inside_max = max(inside_max, inside)
        if phase == "Reform" and reform_t is None: reform_t = t
        if m.get("kindName") != "DangerAreaCrossing" or not m.get("active", True):
            break
        time.sleep(0.5)
    hs = sorted(history.values(), key=lambda h: h.get("t") or 0)
    print(f"    history: {[(h.get('t'), h.get('phaseName'), h.get('event')) for h in hs]}")
    print(f"    crossed at: {[(names[members.index(n)], round(v, 1)) for n, v in sorted(crossed_at.items(), key=lambda kv: kv[1])]}")
    ev = [(h.get("phaseName"), h.get("event")) for h in hs]
    c.ok(("CrossElement", "DefiladeReached") in ev, "② security set first (SetSecurity → CrossElement on DefiladeReached)")
    c.ok(inside_max <= 2, f"③ never more than one pair inside the road strip (most at once: {inside_max})")
    c.ok(("CollapseSecurity", "DefiladeReached") in ev, "④ the far side set before security leaves (FarSideCover → CollapseSecurity on DefiladeReached)")
    sec = members[max(1, len(members) // 2):]   # the back half secures (DangerAreaCrossingManeuver.ComputePartitionInputs)
    last = sorted(crossed_at.items(), key=lambda kv: kv[1])[-len(sec):] if crossed_at else []
    c.ok(len(crossed_at) == len(members) and set(n for n, _ in last) == set(sec),
         f"⑤ everyone crossed, security last ({len(crossed_at)} of {len(members)} across)")
    c.ok(reform_t is not None, f"⑥ the squad reforms beyond the road (Reform at {reform_t})")
    resumed = wait_for(lambda: ((data(call("GET", f"/entities/{sq}/state")) or {}).get("behavior") or {}).get("name") == "SquadMarch", 20)
    c.ok(resumed, "⑥ … and the march goes on")
    c.ok(all((ammo(n)[0] or 0) >= (ammo0[n] or 0) for n in members), "⑦ watching, not shooting: nobody's ammo dropped")
    c.ok(reform_t is not None and reform_t - t0 <= 120, f"⑧ the crossing took ≤ 120 s ({(reform_t or 0) - t0:.0f} s)")


SCENARIOS = {"ua-posture": run_posture, "ua-threat-ranking": run_threat_ranking, "ua-danger-crossing": run_danger_crossing,
             # CE-3079 B7 — the same cast and the same acceptance, the rifleman's task the BLUEPRINT DangerCrossingBp (H7)
             "ua-danger-crossing-bp": run_danger_crossing,
             "ua-fire-distribution": run_fire_distribution,
             "ua-weapon-choice": run_weapon_choice,
             "ua-three-hosts": run_three_hosts,
             "ua-attack-approach": run_attack_approach,
             "bt-doors": run_doors,
             "bt-grenade-posture": run_grenade_posture,
             "bt-mortar-roof": run_mortar_roof,
             "bt-window-duel": run_window_duel,
             # ⭐ CE-2152 C-c R-e — casualty care (Squad Tactics §7.8.2)
             "ua-casualty-care": run_casualty_care,
             "ua-casualty-care-heavy": run_casualty_care_heavy,
             "ua-casualty-care-leader": run_casualty_care_leader,
             # ⭐ CE-2158 F-e — fire and movement (Squad Tactics §7.8.3)
             "ua-fire-and-movement": run_fire_and_movement,
             "ua-bounding-overwatch": run_bounding_overwatch,
             # ⭐ CE-2159 X-d — the squad danger crossing (Squad Tactics §7.8.4)
             "ua-squad-danger-crossing": run_squad_danger_crossing}


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
