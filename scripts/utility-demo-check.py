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
import argparse, json, math, os, signal, subprocess, sys, time, urllib.error, urllib.request

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
    #   ⭐ WS-4 (W-I, Weapon Systems §4d): a host's seat presets spawn their own entities (a technical's M2 and its gunner) — so an
    #   entity the file does not name is allowed when it sits in a seat (IsEmbarkedTag); anything else is the previous world.
    want = scenario_names(name)
    # ⭐ CE-2176 IF-4 — a SECOND cluster left running on the same DDS domain doubles the world: every name twice, two SimHosts
    #   materialising it, and orders landing on a copy whose roster another node owns. The name guard below cannot see it
    #   (it reads names into a dict), so count them.
    rows = data(call("GET", "/entities")) or []
    names = [r.get("name") for r in rows if r.get("name") in want]
    twice = sorted({n for n in names if names.count(n) > 1})
    if twice:
        sys.exit(f"the loaded world has {len(twice)} scenario name(s) twice ({twice[:5]}) — another ClusterRunner on the same DDS "
                 f"domain? (ps -eo pid,args | grep [H]rot.ClusterRunner)")
    got = wait_for(lambda: (n := sorted(x for x in ids_by_name())) and set(want) <= set(n) and not _strangers(want) and n, 30)
    if got is None:
        sys.exit(f"the loaded world is not '{name}' (want {want} + seated presets, have {sorted(ids_by_name())}; strangers "
                 f"{_strangers(want)}) — a second live load in one process was a silent no-op before CE-295. Restart the cluster, "
                 f"or run with --launch.")


def _strangers(want):
    """Loaded entities the scenario does not name and that are not seated in a host (a preset sits in its seat)."""
    out = []
    for r in data(call("GET", "/entities")) or []:
        n, nid = r.get("name"), r.get("networkId")
        if not n or n in want:
            continue
        comps = (data(call("GET", f"/entities/{nid}")) or {}).get("Components") or {}
        if "IsEmbarkedTag" not in comps:
            out.append(n)
    return out


def launch(port):
    """Start a fresh ClusterRunner --mode all for this run; returns the process."""
    if not os.path.exists(RUNNER):
        sys.exit(f"build it first: dotnet build Hrot/Runner/Hrot.ClusterRunner ({RUNNER} missing)")
    # ⭐ a merge does not rebuild the runner: 📌 2026-10-15 a live ua-air-strike ran a 10:08 binary after a 13:03 merge of the new fragment
    #   law and "failed" ④ on the old law. Refuse a runner older than the newest engine source unless told otherwise.
    stale = newest_source_after(os.path.getmtime(RUNNER))
    if stale:
        msg = f"the runner ({RUNNER}) is older than {stale} — rebuild: dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore"
        if os.environ.get("HROT_ALLOW_STALE_RUNNER") != "1":
            sys.exit(msg + "  (or HROT_ALLOW_STALE_RUNNER=1)")
        print("   ⚠ " + msg)
    log = open(os.path.join("/tmp", f"utility-demo-cluster-{port}.log"), "w")
    env = dict(os.environ, HROT_DEBUG_API_PORT=str(port))
    proc = subprocess.Popen(["xvfb-run", "-a", "dotnet", RUNNER, "--mode", "all"], cwd=REPO, env=env,
                            stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    if not wait_for(lambda: data(call("GET", "/status")), 180, every=1):
        stop(proc)
        sys.exit(f"the cluster did not answer on {BASE} within 180 s — see {log.name}")
    print(f"   launched ClusterRunner --mode all (pid {proc.pid}, log {log.name})")
    return proc


def newest_source_after(t):
    """The first engine .cs / .csproj file modified after time t (the runner's build), or None."""
    for top in ("FDP", "Hrot"):
        for root, dirs, files in os.walk(os.path.join(REPO, top)):
            dirs[:] = [d for d in dirs if d not in ("bin", "obj", ".git") and not d.endswith(".Tests")]
            for f in files:
                if f.endswith((".cs", ".csproj")) and os.path.getmtime(os.path.join(root, f)) > t:
                    return os.path.relpath(os.path.join(root, f), REPO)
    return None


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
    overlaps, overlap, longest = [], 0.0, 0.0
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
        moving, speed_of = {}, {}
        for i, nid in enumerate(members):
            p = position(nid)
            if p and nid in last_pos and last_t is not None and t > last_t:
                speed_of[nid] = flat(p, last_pos[nid]) / (t - last_t)
                moving[nid] = speed_of[nid] > 0.5
            if p: last_pos[nid] = p
        # ⭐ CE-2163 — the halves are the drill's own elements (its members' roles), not the order the names are listed in, and the
        #   measure is the LONGEST overlap, as ua-bounding-overwatch's ① (§7.8.3 "as ① above"). By name order it read 2.4 s live;
        #   by the drill's elements the same run's overlap is 0.0 s.
        roles = {x.get("networkId"): x.get("role") for x in m.get("members", [])}
        groups = {}
        for nid in members: groups.setdefault(roles.get(nid), []).append(nid)
        both = len(groups) >= 2 and sum(1 for g in groups.values() if any(moving.get(n) for n in g)) >= 2
        if both and last_t is not None:
            overlaps.append((round(t, 1), m.get("phaseName"),
                             [(names[members.index(n)], roles.get(n), round(speed_of.get(n, 0), 1)) for n in members if moving.get(n)]))
            both_up += t - last_t
        overlap = overlap + (t - last_t) if (both and last_t is not None) else 0.0
        longest = max(longest, overlap)
        last_t = t
        time.sleep(0.5)
    hs = sorted(history.values(), key=lambda h: h.get("t") or 0)
    print(f"    history: {[(h.get('t'), h.get('phaseName'), h.get('event')) for h in hs]}")
    print(f"    rushes: {rush_lengths} cover {rush_cover}; both halves moving for {both_up:.1f} s in all, {longest:.1f} s at most at a time")
    for o in overlaps: print(f"    both halves moving at t={o[0]} ({o[1]}): {o[2]}")
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
    # ⚠ 1.5 s, not §7.8.3's 1 s: positions are polled every 0.5 s over HTTP, and a man settling the last metre counts as moving
    c.ok(longest <= 1.5, f"⑥ never both halves up for more than a moment ({longest:.1f} s at most at a time)")


# ── F-d (CE-2158) — fire and movement IN PAIRS: inside the moving half one buddy rushes while the other fires, then the other ──
#   docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.6.4 / §7.8.3. The same cast as ua-fire-and-movement, `Pairs` on.

def run_fire_and_movement_pairs(c, timeout):
    ids = ids_by_name()
    sq = ids.get("Squad")
    names = ["Leader", "Rifleman 1", "Rifleman 2", "Rifleman 3", "Rifleman 4", "Rifleman 5"]
    members = [ids.get(n) for n in names]
    hostiles = [ids.get("Hostile 1"), ids.get("Hostile 2")]
    if not c.ok(sq and None not in members and None not in hostiles, "the squad, six men and two hostiles are loaded"):
        return
    target = (200.0, 285.0)
    def centre():
        ps = [p for p in (position(m) for m in members) if p]
        return (sum(p[0] for p in ps) / len(ps), sum(p[1] for p in ps) / len(ps)) if ps else None
    call("POST", "/trace/observe", {"networkId": sq, "on": True})
    c0 = centre()
    d0 = flat(c0, target) if c0 else None
    call("POST", "/sim/play", {})
    m = wait_for(lambda: (x := maneuver(sq)) and x.get("kindName") == "FireAndMovement" and x, 20)
    if not c.ok(m is not None, "the squad runs the drill (maneuver kindName FireAndMovement)"):
        return
    rush_polls, max_up, buddy_fires, handovers = 0, 0, 0, 0
    issued = {}          # (member, issuedAt) -> (rush length, metres it brings him nearer the objective)
    phase_ups = {}       # (phase, phase start) -> list of the rushing sets seen in it, in order
    history = {}
    end = (sim_time() or 0) + 120
    while (sim_time() or 0) < end:
        m = maneuver(sq)
        if m.get("kindName") != "FireAndMovement" and history:
            break
        for h in m.get("history", []):
            history.setdefault((h.get("t"), h.get("phase")), h)
        phase = m.get("phaseName") or ""
        if phase.startswith("Rush"):
            orders = {x.get("networkId"): (x.get("order") or {}) for x in m.get("members", [])}
            up = frozenset(n for n, o in orders.items() if o.get("kind") == "AdvanceTo")
            for n in up:
                o = orders[n]
                if (n, o.get("issuedAt")) not in issued and o.get("point") and (at := position(n)):
                    issued[(n, o.get("issuedAt"))] = (round(flat(at, o["point"]), 1), round(flat(at, target) - flat(o["point"], target), 1))
            suppress = sum(1 for o in orders.values() if o.get("kind") == "Suppress")
            if up:
                rush_polls += 1
                max_up = max(max_up, len(up))
                if suppress >= 4: buddy_fires += 1   # the base of fire is 3: a 4th means a buddy fires for his partner
                key = (phase, m.get("phaseSince"))
                seen = phase_ups.setdefault(key, [])
                if not seen or seen[-1] != up: seen.append(up)
        time.sleep(0.5)
    for sets in phase_ups.values():
        handovers += sum(1 for a, b in zip(sets, sets[1:]) if not (a & b))
    hs = sorted(history.values(), key=lambda h: h.get("t") or 0)
    print(f"    history: {[(h.get('t'), h.get('phaseName'), h.get('event')) for h in hs]}")
    print(f"    rush polls {rush_polls}; most men rushing at once {max_up}; polls where a buddy fires for the rusher {buddy_fires}; "
          f"A→B hand-overs inside a rush {handovers}; rushing sets per rush {[[len(s) for s in v] for v in phase_ups.values()]}")
    print(f"    rushes (length, gain toward the objective): {list(issued.values())}")
    c.ok(rush_polls >= 4, f"① the squad rushes ({rush_polls} polls in a rush)")
    c.ok(0 < max_up <= 2, f"② never the whole half up: at most one buddy of each pair rushes ({max_up} at once, a half is 3)")
    c.ok(buddy_fires >= 1, f"③ the buddy who stays down fires for the one who is up ({buddy_fires} polls with ≥ 4 suppressing)")
    c.ok(handovers >= 1, f"④ inside one rush the A buddies go, then the B buddies ({handovers} hand-overs)")
    c1 = centre()
    gained = (d0 - flat(c1, target)) if (c1 and d0) else 0
    c.ok(gained >= 20, f"⑤ it closes the distance (the squad's centre is {gained:.0f} m nearer)")


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
    overlaps = []       # (t, phase, [(name, role, speed, metres from his order point)]) — every poll where both halves moved
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
        moving, speed_of = {}, {}
        for i, nid in enumerate(members):
            p = position(nid)
            if p and nid in last_pos and last_t is not None and t > last_t:
                speed_of[nid] = flat(p, last_pos[nid]) / (t - last_t)
                moving[nid] = speed_of[nid] > 0.5
            if p: last_pos[nid] = p
        # ⭐ the halves are the drill's own elements (its members' roles), not the order the names are listed in
        roles = {x.get("networkId"): x.get("role") for x in m.get("members", [])}
        groups = {}
        for nid in members: groups.setdefault(roles.get(nid), []).append(nid)
        both = len(groups) >= 2 and sum(1 for g in groups.values() if any(moving.get(n) for n in g)) >= 2
        if both and last_t is not None:
            who = [(names[members.index(n)], roles.get(n), round(speed_of.get(n, 0), 1),
                    round(flat(last_pos[n], orders[n]["point"]), 1) if orders.get(n, {}).get("point") else None)
                   for n in members if moving.get(n)]
            overlaps.append((round(t, 1), m.get("phaseName"), who))
        overlap = overlap + (t - last_t) if (both and last_t is not None) else 0.0
        if overlap > longest: longest, longest_at = overlap, t
        last_t = t
        time.sleep(0.5)
    hs = sorted(history.values(), key=lambda h: h.get("t") or 0)
    swaps = [h for h in hs if h.get("event") in ("DefiladeReached", "Timeout", "BoundComplete")]
    print(f"    history: {[(h.get('t'), h.get('phaseName'), h.get('event')) for h in hs]}")
    print(f"    bounds {bounds}, to cover {covered}; contact at {contact_at}, suppress at {suppress_at}, rounds after {fire_after}")
    for o in overlaps: print(f"    both halves moving at t={o[0]} ({o[1]}): {o[2]}")
    # ⚠ 1.5 s, not §7.8.3's 1 s: positions are polled every 0.5 s over HTTP, and a man settling the last metre counts as moving
    c.ok(longest <= 1.5, f"① one half moves, the other watches (both moving at most {longest:.1f} s at a time"
                         + (f", ending t={longest_at:.1f})" if longest > 0 else ")"))
    time.sleep(1.5)   # the drill ended: the replicated positions this node reads lag the commander's by a moment
    for (nid, _), rec in cover_orders.items():
        p = position(nid)
        if p: rec[1] = min(rec[1], flat(p, rec[0]))
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



def run_squad_danger_crossing_contact(c, timeout):
    """⭐ CE-2159 X-d `-contact` (Squad Tactics §7.8.4, D6) — the same march and road; a hostile stands north-east beyond the road,
    out of sight (150 m vision) from the near side and in sight from the far side, so contact comes DURING the crossing: the
    more urgent contact reaction replaces the crossing, the split squad fights where it stands (both sides of the road fire), and
    the assault closes on the far contact by fire and movement."""
    ids = ids_by_name()
    sq, hostile = ids.get("Squad"), ids.get("Hostile")
    names = ["Leader"] + [f"Rifleman {i}" for i in range(1, 7)]
    members = [ids.get(n) for n in names]
    if not c.ok(sq and hostile and None not in members, "the squad, its seven men and the hostile are loaded"):
        return
    call("POST", "/trace/observe", {"networkId": sq, "on": True})
    ammo0 = {m: ammo(m)[0] for m in members}
    call("POST", "/sim/play", {})
    s = wait_for(lambda: (x := danger_sensor(sq)) and areas_of(x) and x, 30)
    if not c.ok(s is not None, "the commander's route sensor lists the road ahead"):
        return
    area = areas_of(s)[0]
    started = wait_for(lambda: (x := maneuver(sq)) and x.get("kindName") == "DangerAreaCrossing" and x.get("active", True) and x, 120)
    if not c.ok(started is not None, "① the crossing drill starts (maneuver kindName DangerAreaCrossing)"):
        return
    near, far = area["nearHandle"], area["farHandle"]
    dx, dy = far[0] - near[0], far[1] - near[1]
    L = (dx * dx + dy * dy) ** 0.5 or 1.0
    def along(p):
        return ((p[0] - near[0]) * dx + (p[1] - near[1]) * dy) / L
    t0 = sim_time() or 0
    left, side_at_contact, task_after = None, {}, None
    while (sim_time() or 0) < t0 + 120:
        m = maneuver(sq) or {}
        if m.get("kindName") != "DangerAreaCrossing" or not m.get("active", True):
            left = (m.get("kindName"), sim_time())
            for nid in members:
                p = position(nid)
                if p: side_at_contact[nid] = "far" if along(p) > 0 else "near"
            break
        time.sleep(0.5)
    c.ok(left is not None, f"② the crossing is replaced before it ends (next drill: {left})")
    if left is None:
        return
    task_after = wait_for(lambda: (n := (((data(call("GET", f"/entities/{sq}/state")) or {}).get("behavior") or {}).get("name"))) == "ReactToContact" and n, 10)
    c.ok(task_after == "ReactToContact", f"③ … by the contact reaction (the commander's task: {task_after})")
    sides = sorted(set(side_at_contact.values()))
    print(f"    sides at contact: {[(names[members.index(n)], v) for n, v in side_at_contact.items()]}")
    c.ok(sides == ["far", "near"], f"④ the squad was split across the road when contact came ({sides})")
    def fired_on(side):
        return [n for n in members if side_at_contact.get(n) == side and (ammo(n)[0] or 0) < (ammo0[n] or 0)]
    t1 = sim_time() or 0
    kinds = []
    while (sim_time() or 0) < t1 + 40:
        k = (maneuver(sq) or {}).get("kindName")
        if k and (not kinds or kinds[-1] != k): kinds.append(k)
        if fired_on("far") and fired_on("near") and "FireAndMovement" in kinds: break
        time.sleep(1)
    print(f"    fired: {[names[members.index(n)] for n in fired_on('far') + fired_on('near')]}; drills after the crossing: {kinds}")
    # ⚠ the hostile has no task of its own (as in every squad demo): "both sides" is the two halves of the split squad (D6)
    c.ok(len(fired_on("far")) > 0, f"⑤ the far side fires ({len(fired_on('far'))} men)")
    c.ok(len(fired_on("near")) > 0, f"⑥ the near side fires too — each side keeps its element ({len(fired_on('near'))} men)")
    c.ok("FireAndMovement" in kinds, "⑦ the assault closes on the far contact by fire and movement (§7.6.3)")



def run_squad_march_loose(c, timeout):
    """⭐ CE-2159 (Squad Tactics §6.1a) — a squad strung out on a diagonal marches ~110 m east in file: the followers close up on
    the lead's trail while it walks, so the lead keeps a march pace instead of holding for stragglers again and again."""
    ids = ids_by_name()
    sq = ids.get("Squad")
    names = ["Leader"] + [f"Rifleman {i}" for i in range(1, 7)]
    members = [ids.get(n) for n in names]
    if not c.ok(sq and None not in members, "the squad and its seven men are loaded"):
        return
    goal = (175.0, 60.0)
    call("POST", "/sim/play", {})
    t0 = sim_time() or 0
    def front():
        best = None
        for nid in members:
            p = position(nid)
            if p and (best is None or p[0] > best[0]): best = p
        return best
    x0 = (front() or [0])[0]
    arrived = None
    samples = []
    while (sim_time() or 0) < t0 + 200:
        f = front()
        t = sim_time() or 0
        if f: samples.append((t, f[0]))
        if f and abs(f[0] - goal[0]) <= 6 and arrived is None: arrived = t; break
        time.sleep(1)
    dist = goal[0] - x0
    pace = dist / ((arrived or (sim_time() or 0)) - t0 or 1)
    print(f"    front from x={x0:.0f}: arrived at {arrived} ({pace:.2f} m/s); samples every 10: {[(round(t), round(x)) for t, x in samples[::10]]}")
    c.ok(arrived is not None, "① the lead reaches the objective")
    c.ok(pace >= 0.9, f"② at a march pace (≥ 0.9 m/s of the asked 1.5; measured {pace:.2f})")
    end = wait_for(lambda: all((p := position(n)) and ((p[0] - goal[0]) ** 2 + (p[1] - goal[1]) ** 2) ** 0.5 <= 35 for n in members), 60)
    c.ok(end, "③ … and the file closes up behind it (every man within 35 m)")


def run_squad_bound_open_ground(c, timeout):
    """⭐ CE-2161 (Squad Tactics §7.7.7 D3; CE-3081 OpenGround) — a squad marching with OpenGround on leaves Shed Row's cover for the
    ~100 m open field to (175, 60): the commander's route sensor lists the field as OpenGround, the crossing choice picks BOUND, the
    squad crosses by bounding overwatch (one half moves, the other watches) and ends beyond the field's far side, then marches on."""
    ids = ids_by_name()
    sq = ids.get("Squad")
    names = ["Leader"] + [f"Rifleman {i}" for i in range(1, 7)]
    members = [ids.get(n) for n in names]
    if not c.ok(sq and None not in members, "the squad and its seven men are loaded"):
        return
    call("POST", "/trace/observe", {"networkId": sq, "on": True})
    ammo0 = {m: ammo(m)[0] for m in members}
    call("POST", "/sim/play", {})
    s = wait_for(lambda: (x := danger_sensor(sq)) and any(a.get("kind") == "OpenGround" for a in areas_of(x)) and x, 30)
    if not c.ok(s is not None, "① the commander's route sensor lists the open field ahead (kind OpenGround)"):
        return
    area = next(a for a in areas_of(s) if a.get("kind") == "OpenGround")
    print(f"    area: {area}; sensor kinds: {(s.get('settings') or {}).get('kinds')}")
    c.ok(area["distanceAlongRoute"] > 0, f"   … ahead of the squad, which starts in cover ({area['distanceAlongRoute']:.1f} m along)")
    started = wait_for(lambda: (x := maneuver(sq)) and x.get("kindName") == "BoundingOverwatch" and x.get("active", True) and x, 60)
    if not c.ok(started is not None, "② the crossing choice picks BOUND (maneuver kindName BoundingOverwatch)"):
        return
    near, far = area["nearHandle"], area["farHandle"]
    dx, dy = far[0] - near[0], far[1] - near[1]
    L = (dx * dx + dy * dy) ** 0.5 or 1.0
    def along(p):
        return ((p[0] - near[0]) * dx + (p[1] - near[1]) * dy) / L
    t0 = sim_time() or 0
    last_pos, last_t, overlap, longest = {}, None, 0.0, 0.0
    history = {}
    while (sim_time() or 0) < t0 + 240:
        t = sim_time() or 0
        m = maneuver(sq) or {}
        if m.get("kindName") != "BoundingOverwatch" or not m.get("active", True):
            break
        for h in m.get("history", []):
            history.setdefault((h.get("t"), h.get("phase")), h)
        roles = {x.get("networkId"): x.get("role") for x in m.get("members", [])}
        moving = {}
        for nid in members:
            p = position(nid)
            if p and nid in last_pos and last_t is not None and t > last_t:
                moving[nid] = flat(p, last_pos[nid]) / (t - last_t) > 0.5
            if p: last_pos[nid] = p
        groups = {}
        for nid in members: groups.setdefault(roles.get(nid), []).append(nid)
        both = len(groups) >= 2 and sum(1 for g in groups.values() if any(moving.get(n) for n in g)) >= 2
        overlap = overlap + (t - last_t) if (both and last_t is not None) else 0.0
        longest = max(longest, overlap)
        last_t = t
        time.sleep(0.5)
    t_end = sim_time() or 0
    hs = sorted(history.values(), key=lambda h: h.get("t") or 0)
    print(f"    bound history: {[(h.get('t'), h.get('phaseName'), h.get('event')) for h in hs]}")
    time.sleep(1.5)   # the replicated positions this node reads lag the commander's by a moment
    alongs = {names[members.index(n)]: round(along(p) - L, 1) for n in members if (p := position(n))}
    beyond = [k for k, v in alongs.items() if v >= -2.0]
    print(f"    after {t_end - t0:.0f} s, metres past the far side: {alongs}")
    c.ok(t_end - t0 < 240, f"③ the bound ends ({t_end - t0:.0f} s)")
    c.ok(longest <= 1.5, f"④ one half moves, the other watches (both halves moving at most {longest:.1f} s at a time)")
    # the last bound carries ONE element to beyond the far side; the overwatching one is a bound behind by design (§7.6)
    c.ok(len(beyond) >= len(members) // 2, f"⑤ the last bound ends beyond the field's far side ({len(beyond)} of {len(members)})")
    gathered = wait_for(lambda: all((p := position(n)) and along(p) >= L - 15.0 for n in members), 60, every=1)
    final = {names[members.index(n)]: round(along(p) - L, 1) for n in members if (p := position(n))}
    print(f"    then, metres past the far side: {final}")
    c.ok(gathered, "⑥ … and the march gathers the whole squad at the far side (every man within 15 m of it)")
    c.ok(all((ammo(n)[0] or 0) >= (ammo0[n] or 0) for n in members), "⑦ watching, not shooting: nobody's ammo dropped")


# ── ua-air-strike (CE-2173 slice 5; docs/designs/air-attack/DESIGN_Air_Operations.md §9c) — a plane bombs a point ─────────────
#   The A-10 is ordered AirPlatformStrike on the Target Technical from 3.8 km out: it flies straight at the target at 300 m, its weapon
#   brain releases ONE Mk-82 on the cue (where a bomb dropped now lands has reached the target), the bomb falls and bursts on the target,
#   and the A-10 flies on out. The Far Technical, 125 m away, is outside the lethal burst (holed at most). ⭐ NO HTTP WRITE but play.

def run_air_strike(c, timeout):
    ids = ids_by_name()
    plane, target, far = ids.get("A-10"), ids.get("Target Technical"), ids.get("Far Technical")
    if not c.ok(None not in (plane, target, far), "the A-10 and both technicals are loaded"):
        return
    aim = position(target)
    call("POST", "/sim/play", {})
    dets = wait_for(lambda: (x := detonations()) and x, timeout + 60)   # ~26 s to the release, ~8 s of fall
    if not c.ok(dets is not None, "a bomb burst (GET /combat/detonations)"):
        return
    d = dets[0]
    b = d.get("burst") or [9e9, 9e9, 0]
    miss = flat(b, aim) if aim else 9e9
    print(f"    the burst at {b}, the target at {aim}: {miss:.1f} m off; warhead {d.get('warheadSource', '')[:60]}")
    # ⭐ CE-2180 — the bomb's trip from the brain's cue to the SimHost's fire, and how far it was flown forward to make it up
    bombs = [x for x in _if_shots({plane}) if x.get("raisedAt") is not None]
    if bombs:
        x = bombs[0]
        print(f"    the release: raised {x.get('raisedAt')} s (brain), fired {x.get('firedAt')} s (SimHost) — trip {x.get('tripSeconds')} s, "
              f"caught up {x.get('caughtUpSeconds')} s ({x.get('caughtUpFrames')} of {x.get('owedFrames')} cue steps owed; the SimHost frame dt {x.get('frameDt')})")
    c.ok(miss <= 25.0, f"① the bomb burst on the target (within 25 m) — {miss:.1f} m")
    c.ok(len(dets) == 1, f"② ONE bomb per pass — {len(dets)} bursts")
    wait_for(lambda: (h := health(target)) is not None and h < 100, 5)   # ⚠ a predicate, not the value: Health 0 is falsy
    ht, hf = health(target), health(far)
    c.ok(ht is not None and ht < 100, f"③ the target is hit — Health {ht}")
    # ⭐ AE5 / CE-3203 (DESIGN_Armour_Hit_Model §7.7): a Mk-82's fragments HOLE a pickup at ~120 m (by design — "holed and mobile"), so
    #   ④ is "not destroyed", not "untouched". 📌 backend measured 3 of 4 live runs below Health 100 at 123 m (RESUME_Backend_Lane §000).
    c.ok(hf is not None and hf > 0, f"④ the far technical (≈123 m) is not destroyed — Health {hf}")
    p0 = position(plane)
    time.sleep(3)
    p1 = position(plane)
    c.ok(p0 is not None and p1 is not None and flat(p1, aim) > flat(p0, aim) and p1[2] > 100,
         f"⑤ the A-10 flies on out, still in the air — {p1}")


# ── ua-air-attack (CE-2173 slice 6b; docs/designs/air-attack/DESIGN_Air_Operations.md §9c-§9f) — the air showcase, four vignettes ──────
#   ① the Bomber A-10 strikes a fortification POINT: a heavy kind ⇒ the bomb (LevelBomb) · ② the Gunship A-10 attacks the lead of a column:
#   an entity ⇒ its gun first (GunRun), the lead destroyed · ③④ the Apache's running fire on a position: its rockets, then its gun on the
#   move, breaking off short of the point · ⑤ the doors-off UH-60 orbits a truck while its door gunners fire (DoorGunOrbit, §9f); the
#   check shoots the firing door's gunner (Health 0 over HTTP) ⇒ that gun falls silent and the orbit turns the other door in.
#   The targets are bare Toyotas (106): nothing shoots back. ⭐ The only HTTP writes are play and the gunner's health.

def _crew(heli_name="M134 minigun"):
    """The door guns (named M134 minigun) and the door gunners by seat ({8: id, 10: id}) — the passenger list holds LOCAL entity ids, so
    the gunners are found as the riflemen whose IsEmbarkedTag sits in seat 8 or 10."""
    rows = data(call("GET", "/entities")) or []
    guns = {r["networkId"] for r in rows if r.get("name") == heli_name}
    seats = {}
    for r in rows:
        if r.get("name") == "Rifleman":
            tag = (comps(r["networkId"]) or {}).get("IsEmbarkedTag") or {}
            if tag.get("Seat") in (8, 10):
                seats[tag["Seat"]] = r["networkId"]
    return guns, seats


def run_air_attack(c, timeout):
    ids = ids_by_name()
    bomber, gunship, apache, uh60 = (ids.get(n) for n in ("Bomber A-10", "Gunship A-10", "Apache", "Door Gunship UH-60"))
    lead, ptruck, otruck = ids.get("Column Lead"), ids.get("Position Truck"), ids.get("Orbit Truck")
    if not c.ok(None not in (bomber, gunship, apache, uh60, lead, ptruck, otruck), "the four aircraft and their targets are loaded"):
        return
    guns, gunners = _crew()
    print(f"    the door guns {sorted(guns)}, the door gunners by seat {gunners}")
    bomb_point, apache_point, orbit_at = (-400.0, -550.0), (500.0, 400.0), position(otruck)   # every target inside the collider grid (±750 m)
    call("POST", "/sim/play", {})
    shots = {}   # seq → shot (the SimHost's shot log is a ring of 64: collect it as it fills)
    apache_closest, ring, bearings, track = 9e9, [], [], []
    down_at, down_seq, down, dead_at = None, None, None, None
    end = time.time() + timeout + 150
    while time.time() < end:
        t = sim_time() or 0.0
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        pa, pu = position(apache), position(uh60)
        if pa:
            apache_closest = min(apache_closest, flat(pa, apache_point))
        if pu and orbit_at and flat(pu, orbit_at) < 600:
            ring.append((t, flat(pu, orbit_at)))
            bearings.append((t, math.atan2(pu[1] - orbit_at[1], pu[0] - orbit_at[0])))
            track.append((t, pu))
        if dead_at is None and (health(otruck) or 1) <= 0:
            dead_at = t
        door = [x for x in shots.values() if x.get("shooter") in guns]
        # the gunner is shot once the orbit has gone a QUARTER turn counter-clockwise with his gun firing (a "before" to compare against)
        if door and down_at is None and _turn(bearings, None, None) > math.pi / 2:
            first = max(door, key=lambda x: x.get("seq") or 0).get("shooter")
            down = gunners.get(8)   # counter-clockwise ⇒ the LEFT door (seat 8's gun) fires first (§9f)
            print(f"    the door guns opened at {t:.1f} s (shooter {first}); shooting the left door gunner {down}")
            if down:
                set_health(down, 0)
            down_at, down_seq = t, max((x.get("seq") or 0) for x in door)
        done = all((h := health(n)) is not None and h <= 0 for n in (lead, otruck)) and _if_bursts({bomber}) and apache_closest < 2000
        if done and down_at is not None and t > down_at + 20:
            break
        time.sleep(0.5)

    bursts = _if_bursts({bomber})
    b = (bursts[0].get("burst") if bursts else None) or [9e9, 9e9, 0]
    c.ok(bursts and flat(b, bomb_point) <= 25, f"① the Bomber's bomb burst on the fortification point (≤ 25 m) — {flat(b, bomb_point):.1f} m")
    g = [x for x in shots.values() if x.get("shooter") == gunship]
    c.ok(len(g) >= 10, f"② the Gunship flew a gun run — {len(g)} rounds")
    c.ok((health(lead) or 0) <= 0, f"② the column lead is destroyed — Health {health(lead)}")
    a = [x for x in shots.values() if x.get("shooter") == apache]
    kinds = sorted({x.get("weaponIndex") for x in a})
    c.ok(len(kinds) >= 2, f"③④ the Apache fired its rockets AND its gun — mounts {kinds}, {len(a)} rounds")
    c.ok(apache_closest >= 300, f"④ the Apache broke off short of the position — closest {apache_closest:.0f} m")
    # on the circle: past the run-in, and not the 10 s reversal turn after the gunner falls (a 180° turn at speed cuts inside —
    # measured 224 m on a 360 m circle) — the same window the reversal check skips
    settled = [r for t, r in ring[len(ring) // 3:] if down_at is None or not (down_at <= t <= down_at + 10)]
    c.ok(settled and 250 <= min(settled) and max(settled) <= 550,
         f"⑤ the UH-60 orbits the truck — {min(settled or [0]):.0f}…{max(settled or [0]):.0f} m ({len(settled)} samples)")
    # the attack's own window: ON the circle (not the run-in), until the truck dies (the attack then ends and the helicopter holds,
    # by design) or 30 s after the gunner fell
    tail = [(t, pu) for t, pu in track if 250 <= flat(pu, orbit_at) <= 550 and (down_at is None or t <= down_at + 30) and (dead_at is None or t < dead_at)]
    speeds = sorted(flat(p1, p0) / (t1 - t0) for (t0, p0), (t1, p1) in zip(tail, tail[4:]) if t1 - t0 > 1.0)
    slow = speeds[len(speeds) // 10] if speeds else 0.0
    c.ok(slow > 20, f"⑤ … never hovering (W4) — 10th-percentile speed {slow:.1f} m/s over ≥ 1 s")
    door = [x for x in shots.values() if x.get("shooter") in guns]
    before = {x.get("shooter") for x in door if (x.get("seq") or 0) <= (down_seq or 0)}
    after = {x.get("shooter") for x in door if down_seq is not None and (x.get("seq") or 0) > down_seq + 5}
    misses = sorted(flat(x["end"], orbit_at) for x in door if x.get("end") and orbit_at)
    hits = sum(1 for x in door if x.get("hit") == otruck)
    print(f"    the door-gun rounds: {len(door)}, {hits} hit the truck; their ends from it: median {misses[len(misses) // 2] if misses else 0:.1f} m")
    ds = sorted(door, key=lambda x: x.get("seq") or 0)
    for x in ds[len(ds) // 2:len(ds) // 2 + 2] + sorted(g, key=lambda x: x.get("seq") or 0)[:2]:   # mid-orbit door shots
        print("      shot", {k: x.get(k) for k in ("shooter", "weaponIndex", "muzzle", "aim", "end", "hit", "outcome", "penetration", "damage")})
    stops = [(round(t0, 1), round(flat(p1, p0) / (t1 - t0), 1)) for (t0, p0), (t1, p1) in zip(tail, tail[4:]) if t1 - t0 > 1.0]
    print(f"    the UH-60's speed over its orbit (t, m/s), slowest: {sorted(stops, key=lambda v: v[1])[:5]}")
    c.ok(bool(before), f"⑤ its door guns fired — shooters {sorted(before)}")
    c.ok(down is not None, f"⑤ the left door gunner found and shot — {down}")
    c.ok(bool(after) and not (after & before), f"⑤ … his gun fell silent and the other door's gun took over — after {sorted(after)}")
    turn_before = _turn(bearings, None, down_at)
    turn_after = _turn(bearings, (down_at or 0) + 10, None)
    c.ok(turn_before > 0 > turn_after, f"⑤ … the orbit reversed — {turn_before:+.2f} rad before, {turn_after:+.2f} after")
    c.ok((health(otruck) or 0) <= 0, f"⑤ the orbited truck is destroyed — Health {health(otruck)}")


# ── ua-air-defence (CE-2191; docs/designs/air-attack/DESIGN_Air_Operations.md §7e/§7f) — the ground fires at aircraft, aircraft evade ──
#   An Apache attacks a truck beside a Stinger gunner; a doors-off UH-60 circles a truck beside a ZU-23 technical. Every ground unit
#   stands ON basic-desert's 600 m terrain (off it the perception grid sees nobody — CE-2194).

def run_air_defence(c, timeout):
    ids = ids_by_name()
    apache, uh60 = ids.get("Apache"), ids.get("Door Gunship UH-60")
    gunner, tech = ids.get("Stinger Gunner"), ids.get("ZU-23 Technical")
    launcher, zu23 = ids.get("Stinger (MANPADS)"), ids.get("ZU-23-2")
    if not c.ok(None not in (apache, uh60, gunner, tech, launcher, zu23), f"the aircraft, the gunner, the technical and their weapons are loaded — launcher {launcher}, ZU-23 {zu23}"):
        return
    call("POST", "/sim/play", {})
    shots, warned, warn, track, evade = {}, None, None, [], []
    gunner_dead = None
    end = time.time() + timeout + 120
    while time.time() < end:
        t = sim_time() or 0.0
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        call("POST", "/perspective", {"name": "CGF"})
        comps = (data(call("GET", f"/entities/{apache}")) or {}).get("Components") or {}
        mw = comps.get("MissileWarning")
        if mw and warned is None:
            warned, warn = t, mw
        pa = position(apache)
        if pa:
            track.append((t, pa))
        if gunner_dead is None and (health(gunner) or 0) <= 0:
            gunner_dead = t
        call("POST", "/perspective", {"name": "Scenario"})
        missiles = [x for x in shots.values() if x.get("shooter") == launcher]
        guns = [x for x in shots.values() if x.get("shooter") == zu23]
        if warned is not None and t > warned + 20 and len(guns) >= 20:
            break
        time.sleep(0.5)

    missiles = [x for x in shots.values() if x.get("shooter") == launcher]   # the gunner's own entry is his rifle
    guns = [x for x in shots.values() if x.get("shooter") in (zu23, tech)]
    by = {}
    for x in shots.values():
        by.setdefault(x.get("shooter"), []).append(x)
    name = {v: k for k, v in ids.items()}
    print("    shots by shooter:", {name.get(s, s): len(v) for s, v in by.items()})
    for x in missiles[:2] + guns[:2]:
        print("      shot", {k: x.get(k) for k in ("shooter", "weaponIndex", "ammo", "muzzle", "aim", "end", "hit", "outcome")})
    print(f"    the gunner died at {gunner_dead}")
    c.ok(len(missiles) >= 1, f"① the Stinger gunner fired at an aircraft — {len(missiles)} missile(s)")
    c.ok(warned is not None, f"① the Apache was warned of the missile — {warn}")
    if warned is not None:
        before = [p for t, p in track if warned - 3 <= t < warned]
        after = [p for t, p in track if warned + 2 <= t <= warned + 6]
        print(f"    the Apache around the warning: before {before[-1:] if before else None} after {after[:1]} … {after[-1:]}")
    c.ok(len(guns) >= 10, f"② the ZU-23 fired at an aircraft — {len(guns)} rounds")
    print(f"    health: Apache {health(apache)} · UH-60 {health(uh60)} · gunner {health(gunner)} · technical {health(tech)}")


# ── ua-tank-corner (Urban Armour V7; docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §4) — the tank's exposure cycle ─────
#   One M1 on its default SOP (BasicVehicleSop: a sensed threat ⇒ React VehicleFightFromCover, no designated target) against a Javelin team
#   and a rifleman north of it in test-town (⚠ not an RPG / NLAW man: a PG-7V's ~300 mm and a direct NLAW's 500 mm cannot pass an
#   M1's 900 mm front, so either reads as harmless as a rifle — only a top attack on its 60 mm roof is a real danger):
#   ① it fires · ② its first rounds go at the AT gunner, not the rifleman (danger to me ranks the AT weapon first) · ③ it fires from more
#   than one point (an exposure is short and a point burns) · ④ between exposures it withdraws (it does not sit in the open).
def run_tank_corner(c, timeout):
    ids = ids_by_name()
    tank, rpg, rifle = ids.get("Tank"), ids.get("AT Gunner"), ids.get("Rifleman")
    if not c.ok(None not in (tank, rpg, rifle), "the tank, the AT gunner (a Javelin — top attack on the 60 mm roof, the one AT weapon a front-on M1 must fear) and the rifleman are loaded"):
        return
    p_rpg, p_rifle = position(rpg), position(rifle)
    call("POST", "/sim/play", {})
    shots, track = {}, []
    end = time.time() + timeout + 150
    while time.time() < end:
        t = sim_time() or 0.0
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        pt = position(tank)
        if pt:
            track.append((t, pt))
        mine = [x for x in shots.values() if x.get("shooter") == tank]
        if len(mine) >= 6 or (health(rpg) or 0) <= 0 and (health(rifle) or 0) <= 0:
            break
        time.sleep(0.5)
    by = {}
    for x in shots.values():
        by.setdefault(x.get("shooter"), []).append(x)
    name = {v: k for k, v in ids.items()}
    print("    shots by shooter:", {name.get(s, s): len(v) for s, v in by.items()})
    name0 = {v: k for k, v in ids.items()}
    print("    shots by shooter:", dict(__import__("collections").Counter(name0.get(x.get("shooter"), x.get("shooter")) for x in shots.values())))
    mine = sorted((x for x in shots.values() if x.get("shooter") == tank), key=lambda x: x.get("seq") or 0)
    for x in mine[:6]:
        print("      tank shot", {k: x.get(k) for k in ("seq", "weaponIndex", "ammo", "muzzle", "aim", "hit", "outcome")})
    c.ok(len(mine) >= 1, f"① the tank fired — {len(mine)} round(s)")
    def nearer(x):
        a = x.get("aim") or x.get("end")
        if not a or not p_rpg or not p_rifle:
            return None
        d = lambda p: ((a[0] - p[0]) ** 2 + (a[1] - p[1]) ** 2) ** 0.5
        return "rpg" if d(p_rpg) <= d(p_rifle) else "rifle"
    first = [nearer(x) for x in mine[:2]]
    c.ok(bool(first) and first[0] == "rpg", f"② the first rounds go at the AT gunner, not the rifleman (danger to my armour) — {first}")
    muzzles = [tuple(x.get("muzzle") or ()) for x in mine]
    points = []
    for m in muzzles:
        if m and all(((m[0] - q[0]) ** 2 + (m[1] - q[1]) ** 2) ** 0.5 > 4 for q in points):
            points.append(m)
    c.ok(len(points) >= 2 or (health(rpg) or 0) <= 0, f"③ it fires from more than one point (or the AT gunner is down first) — {[(round(p[0]), round(p[1])) for p in points]}")
    far = max((((p[0] - track[0][1][0]) ** 2 + (p[1] - track[0][1][1]) ** 2) ** 0.5 for t, p in track), default=0.0) if track else 0.0
    c.ok(len(track) > 2 and far > 5, f"④ the tank moves through hide and firing points — furthest {far:.0f} m from its start")
    stops = []
    for t, p in track:
        if not stops or ((p[0] - stops[-1][1][0]) ** 2 + (p[1] - stops[-1][1][1]) ** 2) ** 0.5 > 3:
            stops.append((round(t, 1), (round(p[0]), round(p[1]))))
    print(f"    tank path (3 m steps): {stops[:24]}")
    print(f"    health: tank {health(tank)} · AT gunner {health(rpg)} · rifleman {health(rifle)}")


# ── ua-ifv-standoff (Urban Armour V7; Exposure Cycle §4, CE-2196) — a 25 mm IFV out-ranges RPG men; up close an RPG kills it ─────────
#   basic-desert, two vignettes. ① "IFV Standoff" (BasicVehicleSop) 340 m from two RPG men advancing on it (RPG reach 300 m): no hide
#   answers in the open, so it fights from where it stands (CE-2196) — its 25 mm kills them before they close to their reach.
#   (⚠ held still at 340 m, they were never SEEN — a man not moving or firing is too faint at that range; a moving one is not.)
#   ⚠ The vignette runs along y 40: at y 100 the IFV sat on the 2.5 m compound wall (x 380–440, y 100–160) and the men walked in
#   along it, hidden all the way. ② "IFV Close" with an RPG man 80 m
#   off, on an attack order at it: the RPG hits it (Bradley side 80 mm vs a PG-7V's ~300 mm).
# health() may be None (no Health / not read) — never fold it with `or`, a 0 is falsy
def _dead(e):
    h = health(e)
    return h is not None and h <= 0


def _hurt(e, full):
    h = health(e)
    return h is not None and h < full


def run_ifv_standoff(c, timeout):
    ids = ids_by_name()
    far, near = ids.get("IFV Standoff"), ids.get("IFV Close")
    a1, a2, rc = ids.get("RPG Man A1"), ids.get("RPG Man A2"), ids.get("RPG Man C")
    if not c.ok(None not in (far, near, a1, a2, rc), "both IFVs and the three RPG men are loaded"):
        return
    call("POST", "/sim/play", {})
    shots, closest, died_at, timeline, next_sample = {}, 1e9, {}, [], 0.0
    end = time.time() + timeout + 150
    while time.time() < end:
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        pf = position(far)
        if time.time() >= next_sample:   # what the stand-off IFV does, every 2 s: where, how fast, which behaviour, what it knows, its gun's reason
            next_sample = time.time() + 2.0
            st = data(call("GET", f"/entities/{far}/state")) or {}
            md = data(call("GET", f"/entities/{far}/memory")) or {}
            mem = [(x.get("networkId"), round(x.get("freshness") or 0, 2), x.get("modalities")) for x in md.get("slots") or []]
            spd = [round((data(call("GET", f"/entities/{m}/state")) or {}).get("speed") or 0, 1) for m in (a1, a2)]
            ws = ((data(call("GET", f"/entities/{far}/weapons")) or {}).get("status") or {})
            dists = [round(((pf[0] - pm[0]) ** 2 + (pf[1] - pm[1]) ** 2) ** 0.5) for pm in (position(a1), position(a2)) if pf and pm]
            timeline.append((tuple(round(v) for v in pf[:2]) if pf else None, round(st.get("speed") or 0, 1), (st.get("behavior") or {}).get("name"),
                             mem, ws.get("reason"), ws.get("target"), dists, spd))
        for m in (a1, a2):
            pm = position(m)
            if pf and pm and m not in died_at:
                d = ((pf[0] - pm[0]) ** 2 + (pf[1] - pm[1]) ** 2) ** 0.5
                closest = min(closest, d)
                if _dead(m):
                    died_at[m] = round(d)
        if all(_dead(m) for m in (a1, a2)) and _hurt(near, 500):
            break
        time.sleep(0.5)
    by = {}
    for x in shots.values():
        by.setdefault(x.get("shooter"), []).append(x)
    name = {v: k for k, v in ids.items()}
    print("    shots by shooter:", {name.get(s, s): len(v) for s, v in by.items()})
    for x in by.get(far, [])[:4]:
        print("      stand-off shot", {k: x.get(k) for k in ("target", "weaponIndex", "muzzle", "aim", "end", "hit", "outcome", "sigma", "penetration")})
    print("    stand-off targets:", {name.get(t, t): n for t, n in __import__("collections").Counter(x.get("target") for x in by.get(far, [])).items()})
    hurt = [m for m in (a1, a2) if _hurt(m, 100)]
    c.ok(len(by.get(far, [])) >= 1 and hurt, f"① the stand-off IFV fires and hits the RPG men — {len(by.get(far, []))} rounds, hurt {[name.get(m) for m in hurt]}")
    rockets_at_far = sum(len(by.get(m, [])) for m in (a1, a2))
    c.ok(not _hurt(far, 500), f"① the RPG men never hurt it — {rockets_at_far} rocket(s) fired at it, health {health(far)}")
    for row in timeline[:40]:
        print("      ifv @", row)
    print(f"    closest approach {closest:.0f} m · distance at death {({name.get(m): d for m, d in died_at.items()})}")
    c.ok(len(by.get(rc, [])) >= 1 and _hurt(near, 500), f"② up close the RPG hits the other IFV — {len(by.get(rc, []))} rocket(s), its health {health(near)}")
    print(f"    health: RPG men A1 {health(a1)} · A2 {health(a2)} · C {health(rc)}")


# ── ua-javelin-lock (Urban Armour V7; Exposure Cycle §4 budget, Missiles & Ammo M4 "the lock IS the aim time") ────────────────────────
#   test-town, two vignettes. A Javelin locks in 4 s of continuous sight (the clock restarts on lost sight). ① "Tank Cycling" on
#   the exposure cycle (LandPlatformAttack): its exposure budget is AimBudgetFactor × the quickest dangerous threat's aim time, so it is back in cover before
#   "Javelin A" (attack order on it) completes a lock. ② "Tank Open", idle on the open north strip 260 m from "Javelin B" (isolated: at (380, 160) Javelin B was in the cycling tank's
#   view, and once its unseen Javelin A expired from memory it turned on B): locked and hit — DIRECT at
#   this range (CE-2201: the top attack only fits beyond ~607 m; test-town is 400 m across). ⚠ The cycling tank starts IN its hide
#   (251, 110): started at (250, 70) in Javelin A's view, it was locked and hit while driving to the hide (40 m at 5 m/s, ~8 s) — the
#   budget then held its first real exposure to ~2 s (lock 1.6 / 4 s). Javelin A holds (no assault): on an assault order it walked
#   inside its 65 m minimum range. ⇒ Javelin A holds at (100, 225) (an advance objective on its own spot) — ua-tank-corner's geometry,
#   where the cycle's firing points are the block's west corner (~(224, 115), measured); (200, 200) left the cycle no firing point that
#   saw him. The hide (251, 110) is out of his sight; the tank starts knowing where he is (a seeded
#   memory slot) and an ATTACK order on him (LandPlatformAttack — the SOP reacts to a fresh sense, not to a seeded memory, so on its SOP
#   it idled). ① is refused as "not exercised" unless Javelin A was seen aiming at least once.
def run_javelin_lock(c, timeout):
    ids = ids_by_name()
    cyc, opn, ja, jb = ids.get("Tank Cycling"), ids.get("Tank Open"), ids.get("Javelin A"), ids.get("Javelin B")
    if not c.ok(None not in (cyc, opn, ja, jb), "both tanks and both Javelin teams are loaded"):
        return
    call("POST", "/sim/play", {})
    shots, trace, next_sample = {}, [], 0.0
    end = time.time() + timeout + 150
    while time.time() < end:
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        if time.time() >= next_sample and not _dead(cyc):   # the cycle against the lock, every second
            next_sample = time.time() + 1.0
            pc = position(cyc)
            md = data(call("GET", f"/entities/{cyc}/memory")) or {}
            knows = [round(x.get("freshness") or 0) for x in md.get("slots") or [] if x.get("networkId") == ja]
            st = ((data(call("GET", f"/entities/{ja}/weapons")) or {}).get("status") or {})
            mine = ((data(call("GET", f"/entities/{cyc}/weapons")) or {}).get("status") or {})
            sp = round((data(call("GET", f"/entities/{cyc}/state")) or {}).get("speed") or 0, 1)
            if st.get("reason") == "aiming" or (mine.get("reason") or "") not in ("", "hold: not seen"):
                print("      tank gun @", len(trace), {"speed": sp, "reason": mine.get("reason"), "progress": mine.get("progress"),
                                                     "needed": mine.get("needed"), "target": mine.get("target")})
            if len(trace) % 3 == 0:   # what the cycle's EQS sensors answer (hide / firing point), every 3 s
                for sn in (data(call("GET", f"/entities/{cyc}/sensors")) or {}).get("sensors") or []:
                    ans = sn.get("answer") or {}
                    print("      sensor @", len(trace), {k: sn.get(k) for k in ("kind", "template", "status", "partId")},
                          {k: (v if not isinstance(v, list) else v[:3]) for k, v in ans.items()} if isinstance(ans, dict) else ans)
            trace.append((len(trace),
                          tuple(round(v) for v in pc[:2]) if pc else None, knows, st.get("reason"), st.get("progress"), st.get("needed")))
        if _hurt(opn, 3000) and _dead(ja):
            break
        time.sleep(0.5)
    by = {}
    for x in shots.values():
        by.setdefault(x.get("shooter"), []).append(x)
    name = {v: k for k, v in ids.items()}
    print("    shots by shooter:", {name.get(s, s): len(v) for s, v in by.items()})
    for x in (by.get(ja, []) + by.get(jb, []))[:6]:
        print("      javelin shot", {k: x.get(k) for k in ("shooter", "target", "outcome", "hit", "hitPart", "hitFace", "penetration", "end")})
    b_hits = [x for x in by.get(jb, []) if x.get("hit") == opn]
    top = [x for x in b_hits if str(x.get("hitFace") or "").lower().startswith("top")]
    c.ok(b_hits and _hurt(opn, 3000), f"② the tank left in the open is locked and hit — {len(by.get(jb, []))} launch(es), {len(b_hits)} hit(s), health {health(opn)}")
    # ⭐ CE-2201 (backend, as built): the top attack flies only where it fits — the full profile ≥ ~727 m, a capped apex 607–727 m, DIRECT
    #   below. test-town is 400 m across, so here the Javelin flies direct (a side / front hit); the roof needs basic-desert's ranges.
    print(f"    ② the face it met — {[x.get('hitFace') for x in b_hits]} (direct inside ~607 m, CE-2201)")
    a_hits = [x for x in by.get(ja, []) if x.get("hit") == cyc]
    c.ok(len(by.get(cyc, [])) >= 1, f"① the cycling tank fights — {len(by.get(cyc, []))} round(s)")
    aimed = [row for row in trace if row[3] == "aiming"]
    best = max((row[4] or 0 for row in aimed), default=0)
    c.ok(aimed, f"① exercised — Javelin A saw and aimed at the cycling tank in {len(aimed)} sampled second(s), best lock {best:.1f} / 4 s")
    c.ok(not a_hits and not _hurt(cyc, 3000), f"① its short exposures deny Javelin A a lock — {len(by.get(ja, []))} launch(es), {len(a_hits)} hit(s), health {health(cyc)}")
    print(f"    health: Tank Cycling {health(cyc)} · Tank Open {health(opn)} · Javelin A {health(ja)} · Javelin B {health(jb)}")
    for row in trace[:60]:
        print("      cycle s", row)


# ── ua-technical-scoot (Urban Armour V7; Exposure Cycle §6 M9 technicals, D-J Auto ⇒ Scoot) — shoot and scoot ───────────────────────
#   test-town: an M1 on BasicVehicleSop in the south street (200, 120) — ⚠ started in its hide south of the 4-storey block, no
#   firing point of theirs saw it and nobody fired; at the crossing (200, 200) the ATGM technical was ~104 m off and never fired; an ATGM technical (105, a Kornet) behind the 20 m block at (140, 285) and an HMG
#   technical (104, an M2) east of the 40 m tower at (340, 262), each hidden from the tank, each on an attack order at the tank (LandPlatformAttack — a technical's 5 mm hull
#   resolves Auto withdraw to SCOOT: a new hide every time). ① each technical fires from two points (≥ 5 m apart) · ② the tank's first
#   round goes at the ATGM technical (the danger to its armour, V1).
def run_technical_scoot(c, timeout):
    ids = ids_by_name()
    tank, atgm, hmg = ids.get("Tank"), ids.get("ATGM Technical"), ids.get("HMG Technical")
    if not c.ok(None not in (tank, atgm, hmg), "the tank and both technicals are loaded"):
        return
    call("POST", "/sim/play", {})
    shots, track = {}, {atgm: [], hmg: []}
    end = time.time() + timeout + 150
    while time.time() < end:
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        for t in (atgm, hmg):
            pt = position(t)
            if pt:
                track[t].append(tuple(round(v) for v in pt[:2]))
        if _dead(atgm) and _dead(hmg):
            break
        time.sleep(1.0)
    name = {v: k for k, v in ids.items()}
    by = {}
    for x in sorted(shots.values(), key=lambda x: x.get("seq") or 0):
        by.setdefault(name.get(x.get("shooter"), x.get("shooter")), []).append(x)
    print("    shots by shooter:", {k: len(v) for k, v in by.items()})
    for t in (atgm, hmg):
        stops = []
        for q in track[t]:
            if not stops or ((q[0] - stops[-1][0]) ** 2 + (q[1] - stops[-1][1]) ** 2) ** 0.5 > 3:
                stops.append(q)
        print(f"    {name.get(t)} path (3 m steps): {stops[:20]}")

    def points(owner_names):
        pts = []
        for k, xs in by.items():
            if any(n in str(k) for n in owner_names):
                for x in xs:
                    m = x.get("muzzle")
                    if m and all(((m[0] - q[0]) ** 2 + (m[1] - q[1]) ** 2) ** 0.5 > 5 for q in pts):
                        pts.append((round(m[0]), round(m[1])))
        return pts
    a_pts, h_pts = points(("ATGM", "Kornet")), points(("HMG", "M2"))
    c.ok(len(a_pts) >= 2, f"① the ATGM technical fires from two points — {a_pts}")
    c.ok(len(h_pts) >= 2, f"① the HMG technical fires from two points — {h_pts}")
    tank_shots = by.get("Tank", [])
    first = name.get(tank_shots[0].get("target")) if tank_shots else None
    c.ok(first == "ATGM Technical", f"② the tank's first round goes at the ATGM technical — {first} ({len(tank_shots)} rounds)")
    print(f"    health: tank {health(tank)} · ATGM technical {health(atgm)} · HMG technical {health(hmg)}")


# ── ua-tank-ammo (Urban Armour V7; Missiles & Ammo M8 — the weapon brain picks the round by the target) ────────────────────────────
#   basic-desert: one M1 (APFSDS / HEAT-MP / HE loadout) on BasicVehicleSop, north of the ridge (⚠ south of it, at y 80, the crest at
#   y 210 hid every target and it never fired), against an enemy T-72 (240 m), an HMG technical (230 m) and two riflemen (230 m,
#   advancing on it — held still at that range they were never seen). ⚠ A bare pickup (106) here took 22 rounds straight through it
#   (no hit, every round detonating 230 m beyond). The enemy tank is IDLE (no brain), so the M1 fights the three in turn. ① it fires at all three · ② the round differs by
#   target (read from each shot's penetration — the kinetic APFSDS against the tank, a lower-penetration HE / HEAT at the soft ones).
def run_tank_ammo(c, timeout):
    ids = ids_by_name()
    tank, t72, pickup = ids.get("Tank"), ids.get("Enemy Tank"), ids.get("Technical")
    riflemen = [ids.get("Rifleman 1"), ids.get("Rifleman 2")]
    if not c.ok(None not in (tank, t72, pickup, *riflemen), "the tank, the T-72, the technical and the riflemen are loaded"):
        return
    call("POST", "/sim/play", {})
    shots = {}
    end = time.time() + timeout + 200
    while time.time() < end:
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        if all(_dead(e) for e in (t72, pickup, *riflemen)):
            break
        time.sleep(0.5)
    name0 = {v: k for k, v in ids.items()}
    print("    shots by shooter:", dict(__import__("collections").Counter(name0.get(x.get("shooter"), x.get("shooter")) for x in shots.values())))
    mine = sorted((x for x in shots.values() if x.get("shooter") == tank), key=lambda x: x.get("seq") or 0)
    if mine:
        print("    a shot's keys:", sorted(mine[0].keys()))
    name = {v: k for k, v in ids.items()}
    by_target = {}
    for x in mine:
        by_target.setdefault(name.get(x.get("target"), x.get("target")), []).append(x)
    for t, xs in by_target.items():
        print(f"    at {t}: {len(xs)} rounds, penetration {sorted({round(x.get('penetration') or 0) for x in xs})}, weapon {sorted({x.get('weaponIndex') for x in xs})}")
        print("      outcomes", dict(__import__("collections").Counter(x.get("outcome") for x in xs)))
        for x in xs[:3]:
            print("      e.g.", {k: x.get(k) for k in ("muzzle", "aim", "end", "hit", "outcome", "penetration", "crossings")})
    at_tank = [x for x in shots.values() if x.get("target") == tank or x.get("hit") == tank]
    print("    rounds at the tank:", dict(__import__("collections").Counter((name.get(x.get("shooter")), x.get("outcome"), x.get("hitPart"), x.get("hitFace")) for x in at_tank)))
    for x in [x for x in at_tank if x.get("hit") == tank][:3]:
        print("      hit on tank", {k: x.get(k) for k in ("shooter", "penetration", "damage", "arrivingDamage", "arrivingPenetration", "hitPart", "hitFace", "crossings")})
    soft = [x for x in mine if x.get("target") != t72]
    if soft:
        print("      a soft-target round (all keys):", {k: v for k, v in soft[0].items() if k not in ("crossings",)})
    md = data(call("GET", f"/entities/{tank}/memory")) or {}
    print("    tank memory:", [(name.get(x.get("networkId"), x.get("networkId")), round(x.get("freshness") or 0, 1), x.get("modalities"), x.get("alive")) for x in md.get("slots") or []])
    hit = [n for n in ("Enemy Tank", "Technical", "Rifleman 1", "Rifleman 2") if n in by_target]
    c.ok(len(hit) >= 3, f"① it fires at the tank, the technical and the men — {hit}")
    pen = lambda n: max((x.get("penetration") or 0 for x in by_target.get(n, [])), default=0)
    soft = max(pen("Technical"), pen("Rifleman 1"), pen("Rifleman 2"))
    c.ok(pen("Enemy Tank") > 0 and pen("Enemy Tank") > soft, f"② a different round for the tank — penetration {pen('Enemy Tank'):.0f} vs {soft:.0f} at the soft targets")
    print(f"    health: tank {health(tank)} · T-72 {health(t72)} · technical {health(pickup)} · riflemen {[health(r) for r in riflemen]}")


def _turn(bearings, start, stop):
    total, last = 0.0, None
    for t, b in bearings:
        if (start is not None and t < start) or (stop is not None and t > stop):
            continue
        if last is not None:
            total += math.remainder(b - last, 2 * math.pi)
        last = b
    return total


def _if_shots_all():
    call("POST", "/perspective", {"name": "SimHost"})
    try:
        return (data(call("GET", "/combat/shots?last=64")) or {}).get("shots") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})


# ── ua-indirect-fire (CE-2176 IF-4; docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §6) — mortars, artillery, the observer ───────
#   ① the observer, 700 m north, calls the mortar section's fire on the enemy squad behind the ridge block: one tube adjusts, then all
#   three fire for effect · ② the M109 battery, three guns at three ranges, time on target on the trucks in the open · ③ a Strike into
#   the walled courtyard flies the HIGH arc (its time of flight ≫ ②'s at the same range) · ④ a Strike 400 m from friends is refused
#   until it says danger close. Every order over HTTP (POST /entities/{id}/order), the way an operator gives it.

def _if_bursts(shooters, after_seq=-1):
    call("POST", "/perspective", {"name": "SimHost"})
    try:
        ds = (data(call("GET", "/combat/detonations?last=64")) or {}).get("detonations") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    return [d for d in ds if d.get("shooter") in shooters and (d.get("seq") or 0) > after_seq]


def _if_shots(shooters):
    call("POST", "/perspective", {"name": "SimHost"})
    try:
        ss = (data(call("GET", "/combat/shots?last=64")) or {}).get("shots") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    return [x for x in ss if x.get("shooter") in shooters]


def _if_mark():
    # the newest burst and shot sequence numbers so far (a phase looks only at what comes after its mark)
    call("POST", "/perspective", {"name": "SimHost"})
    try:
        ds = (data(call("GET", "/combat/detonations?last=1")) or {}).get("detonations") or []
        ss = (data(call("GET", "/combat/shots?last=1")) or {}).get("shots") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    return (max([d.get("seq") or 0 for d in ds] + [-1]), max([x.get("seq") or 0 for x in ss] + [-1]))


def _if_shots_since(shooters, seq):
    return [x for x in _if_shots(shooters) if (x.get("seq") or 0) > seq]


def _geo_origin(scenario):
    """The (lat, lon, alt) origin of the scenario's terrain (its terrain.json), or None."""
    try:
        with open(os.path.join(REPO, "scenarios", scenario, "scenario.json")) as f:
            terrain = json.load(f)["header"].get("terrainName")
        with open(os.path.join(REPO, "Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Terrain", terrain, "terrain.json")) as f:
            o = json.load(f).get("origin")
        return (o["lat"], o["lon"], o.get("alt", 0.0)) if o else None
    except (OSError, KeyError, TypeError, ValueError):
        return None


def geo_of(scenario, x, y):
    """⭐ CE-2176 IF-4 — an order's Location is a GEODETIC point [lat, lon] (PickableGeoPoint) once the cluster has a geo transform —
    and every terrain declares an origin. A local [y, x] there reads as latitude -195°: WGS84Transform.ToCartesian throws and the
    order is dropped without a word. This is that transform's inverse (local ENU → ECEF → geodetic, WGS84), so the point the brain
    gets back is the local one asked for."""
    o = _geo_origin(scenario)
    if o is None:
        return [y, x]
    a, f = 6378137.0, 1.0 / 298.257223563
    e2 = f * (2.0 - f)
    lat0, lon0, h0 = math.radians(o[0]), math.radians(o[1]), o[2]
    n0 = a / math.sqrt(1.0 - e2 * math.sin(lat0) ** 2)
    x0 = (n0 + h0) * math.cos(lat0) * math.cos(lon0)
    y0 = (n0 + h0) * math.cos(lat0) * math.sin(lon0)
    z0 = (n0 * (1.0 - e2) + h0) * math.sin(lat0)
    e, nn, u = x, y, 0.0   # local X = east, Y = north, Z = up
    sl, cl, so, co = math.sin(lat0), math.cos(lat0), math.sin(lon0), math.cos(lon0)
    X = x0 - so * e - sl * co * nn + cl * co * u
    Y = y0 + co * e - sl * so * nn + cl * so * u
    Z = z0 + cl * nn + sl * u
    lon = math.atan2(Y, X)
    p_ = math.hypot(X, Y)
    lat = math.atan2(Z, p_ * (1.0 - e2))
    for _ in range(8):   # Bowring-style fixed point, converges to well under a millimetre
        n = a / math.sqrt(1.0 - e2 * math.sin(lat) ** 2)
        lat = math.atan2(Z + e2 * n * math.sin(lat), p_)
    return [math.degrees(lat), math.degrees(lon)]


def _if_order(nid, kind, params):
    r = call("POST", f"/entities/{nid}/order", {"kind": kind, "params": params})
    ok = isinstance(r, dict) and "http" not in r and (data(r) or {}).get("accepted", True) is not False
    print(f"    order {kind} → {nid}: {json.dumps(data(r) if ok else r)[:200]}")
    return ok


def run_indirect_fire(c, timeout):
    ids = ids_by_name()
    gunners = [ids.get(f"Mortar Gunner {i}") for i in (1, 2, 3)]
    tubes = [ids.get(f"81mm Tube {i}") for i in (1, 2, 3)]
    guns = [ids.get(f"M109 {i}") for i in (1, 2, 3)]
    sec, bat, obs = ids.get("Mortar Section"), ids.get("Artillery Battery"), ids.get("Observer")
    if not c.ok(None not in gunners + tubes + guns + [sec, bat, obs, ids.get("Friendly 1")], "the mortar section, the battery, the observer are loaded"):
        return
    call("POST", "/sim/play", {})
    call("POST", "/sim/timescale", {"scale": 4})
    comps = lambda n: ((data(call("GET", f"/entities/{n}")) or {}).get("Components") or {})
    boarded = wait_for(lambda: all("IsEmbarkedTag" in comps(g) for g in gunners), 60)
    if not c.ok(boarded, "the three gunners man their tubes"):
        return

    # ① the observer calls the mortars (it runs while the battery does ② – ④)
    target = (340, 300)
    t0, _ = _if_mark()
    c.ok(_if_order(obs, "ObserveFire", {"Location": geo_of("ua-indirect-fire", *target[:2]), "Battery": sec, "Sheaf": "Parallel", "Rounds": 2}),
         "① the observer takes the ObserveFire order")

    # ② time on target
    p2 = (720, -195)
    c.ok(_if_order(bat, "Strike", {"Location": geo_of("ua-indirect-fire", *p2[:2]), "Control": "TimeOnTarget", "Sheaf": "Parallel", "Rounds": 1}), "② the battery takes the Strike")
    b2 = wait_for(lambda: (x := _if_bursts(guns, t0)) and len({d.get("shooter") for d in x}) >= 3 and x, timeout + 120)
    if c.ok(b2 is not None, f"② every gun's round burst — {len(_if_bursts(guns, t0))}"):
        first = {}
        for d in sorted(b2, key=lambda d: d.get("tick") or 0):
            first.setdefault(d.get("shooter"), d)
        ticks = [d.get("tick") or 0 for d in first.values()]
        miss = max(flat(d.get("burst") or [9e9, 9e9], p2) for d in first.values())
        print(f"    ② first-round ticks {ticks}, the farthest burst {miss:.0f} m from the point")
        c.ok(max(ticks) - min(ticks) <= 60, f"② the three first rounds land within a second (time on target) — {max(ticks) - min(ticks)} ticks")
        c.ok(miss <= 150, f"② on the trucks (≤ 150 m: dispersion + the parallel sheaf) — {miss:.0f} m")
    tof2 = [(x.get("endTick") or 0) - (x.get("tick") or 0) for x in _if_shots(guns) if x.get("endTick")]
    t3, s3 = _if_mark()

    # ③ the courtyard: the low arc cannot clear the walls ⇒ high
    p3 = (900, 150)
    c.ok(_if_order(bat, "Strike", {"Location": geo_of("ua-indirect-fire", *p3[:2]), "Rounds": 1}), "③ the battery takes the courtyard Strike")
    b3 = wait_for(lambda: (x := _if_bursts(guns, t3)) and x, timeout + 240)
    if c.ok(b3 is not None and bool(tof2), "③ a round burst in or by the courtyard"):
        tof3 = [(x.get("endTick") or 0) - (x.get("tick") or 0) for x in _if_shots_since(guns, s3) if x.get("endTick")]
        lo, hi = sum(tof2) / len(tof2), (max(tof3) if tof3 else 0)
        print(f"    ② low-arc flights {tof2} ticks · ③ flights {tof3} ticks")
        c.ok(hi >= 1.5 * lo, f"③ the courtyard round flew the HIGH arc — {hi} vs {lo:.0f} ticks")

    # ④ danger close
    pd = (1000, 800)
    _, s4 = _if_mark()
    c.ok(_if_order(bat, "Strike", {"Location": geo_of("ua-indirect-fire", *pd[:2]), "Rounds": 1}), "④ the battery takes the Strike near the friends")
    time.sleep(25)
    c.ok(not _if_shots_since(guns, s4), "④ refused — no gun fires with friends 400 m from the point")
    t4b, _ = _if_mark()
    c.ok(_if_order(bat, "Strike", {"Location": geo_of("ua-indirect-fire", *pd[:2]), "Rounds": 1, "DangerClose": True}), "④ resent with danger close")
    b4 = wait_for(lambda: (x := _if_bursts(guns, t4b)) and x, timeout + 120)
    c.ok(b4 is not None, "④ with danger close the battery fires")

    # ① back to the observer: adjusting rounds from ONE tube, then every tube fires for effect
    def ffe():
        x = _if_bursts(tubes, t0)
        return x if len({d.get("shooter") for d in x}) >= 3 else None
    b1 = wait_for(ffe, timeout + 600, every=2)
    allb = sorted(_if_bursts(tubes, t0), key=lambda d: d.get("tick") or 0)
    print(f"    ① {len(allb)} mortar bursts: " + ", ".join(f"{d.get('shooter')}@{flat(d.get('burst') or [0, 0], target):.0f}m" for d in allb))
    if not c.ok(b1 is not None, "① every tube fired — the observer called fire for effect"):
        return
    k = next(i for i, d in enumerate(allb) if len({e.get("shooter") for e in allb[: i + 1]}) >= 2)
    c.ok(k >= 1 and len({d.get("shooter") for d in allb[:k]}) == 1, f"① {k} adjusting round(s) from ONE tube first")
    effect = allb[k:]
    near = [d for d in effect if flat(d.get("burst") or [9e9, 9e9], target) <= 120]
    c.ok(len(near) >= 3, f"① the fire for effect lands on the enemy squad (≤ 120 m) — {len(near)}/{len(effect)}")



# ── CE-1118 H-2 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §8) — the airborne demos ─────────────────────────────────────────────
#   ua-fast-rope-roof (test-town): an SF squad in a doors-off UH-60 ropes onto the flat roof [40..90]×[230..280], 12 m up, from a Rope
#   hold 12 m above it — SquadFastRope, one man per rope at a time, then a ring ON the roof; the helicopter leaves only after the last
#   man is down. ua-airborne-drop (basic-desert): a stick of 8 paratroopers from 400 m on the Drop leg along y = 300 — SquadAirborneExit
#   at each man's release point, each with his landing point (Descent.Target), then the squad assembles. ua-eject: an A-10's mission
#   ejects its pilot after 40 s; he comes down under a round canopy and evades into cover, away from a hostile patrol.

def _descent(nid):
    return (comps(nid) or {}).get("Descent")


def _phase(d):
    p = (d or {}).get("Phase")
    return p if isinstance(p, str) else {0: "Exit", 1: "Grip", 2: "Slide", 3: "Brake", 4: "FreeFalling", 5: "Deploying", 6: "Canopy",
                                         7: "Flare", 8: "Landing", 9: "Landed", 10: "Falling"}.get(p, str(p))


def _aboard(nid):
    return ((comps(nid) or {}).get("IsEmbarkedTag") or {}).get("Vehicle")


def run_fast_rope_roof(c, timeout):
    ids = ids_by_name()
    heli, men = ids.get("UH-60 (doors off)"), [ids.get(f"SF operator {i}") for i in range(1, 9)]
    if not c.ok(heli is not None and None not in men, "the UH-60 and its eight SF operators are loaded"):
        return
    c.ok(all(_aboard(m) for m in men), "all eight start aboard")
    call("POST", "/sim/play", {})
    first, landed, left_at, timeline, next_sample, hover = None, {}, None, [], 0.0, None
    end = time.time() + timeout + 120
    while time.time() < end:
        t = sim_time() or 0.0
        ph = (comps(heli).get("NavigationStatus") or {}).get("Phase")
        hp = position(heli)
        if str(ph) in ("Roping", "10") and hover is None and hp:
            hover = hp
        for m in men:
            d = _descent(m)
            if d and first is None:
                first = t
            pm = position(m)
            if m not in landed and first is not None and not d and not _aboard(m) and pm:
                landed[m] = (t, pm)
        if hover and hp and left_at is None and flat(hp, hover) > 20:
            left_at = t
        if time.time() >= next_sample:
            next_sample = time.time() + 1.0
            timeline.append((round(t, 1), str(ph), tuple(round(v) for v in hp) if hp else None,
                             [(_phase(_descent(m)) if _descent(m) else ("aboard" if _aboard(m) else "down")) for m in men]))
        if len(landed) == 8 and left_at is not None and time.time() > end - timeout - 60:
            break
        if len(landed) == 8 and left_at is not None:
            time.sleep(8)   # the ring
            break
        time.sleep(0.25)
    for row in timeline[:60]:
        print("      rope @", row)
    c.ok(first is not None, "① the squad went down the ropes (a Descent seen)")
    if first is None:
        return
    last = max((v[0] for v in landed.values()), default=None)
    c.ok(len(landed) == 8, f"① all eight down — {len(landed)}")
    c.ok(last is not None and last - first <= 15.0, f"① ★ eight down in ≤ 15 s of roping — {(last - first) if last else None} s")
    at = [position(m) for m in men]
    on_roof = [p for p in at if p and 40 <= p[0] <= 90 and 230 <= p[1] <= 280 and p[2] > 10]
    c.ok(len(on_roof) == 8, f"② ★ all eight ON the roof, none on the ground — {len(on_roof)}; {[tuple(round(v, 1) for v in p) for p in at if p]}")
    apart = all(flat(a, b) > 1.0 for i, a in enumerate(at) for b in at[i + 1:] if a and b)
    c.ok(apart, "② in a ring (no two men within 1 m)")
    c.ok(left_at is not None and last is not None and left_at >= last, f"③ ★ the helicopter left only after the last landing — left {left_at}, last down {last}")


def run_airborne_drop(c, timeout):
    ids = ids_by_name()
    heli, men = ids.get("UH-60 (doors off)"), [ids.get(f"Paratrooper {i}") for i in range(1, 9)]
    if not c.ok(heli is not None and None not in men, "the UH-60 and its eight paratroopers are loaded"):
        return
    call("POST", "/sim/play", {})
    jumped, targets, timeline, next_sample = {}, {}, [], 0.0
    end = time.time() + timeout + 240
    while time.time() < end:
        t = sim_time() or 0.0
        for m in men:
            d = _descent(m)
            if d and m not in jumped:
                # ⭐ the exit time is the descent's own: its first phase began at the door (PhaseSince). The poll loop takes ~2 s
                #   for eight reads, so the sample time alone cannot resolve a 1 s stick interval (live 2026-10-16: gaps read 0.0)
                since = d.get("PhaseSince")
                exit_t = since if since is not None and _phase(d) not in ("Canopy", "Landed") else t
                jumped[m] = (exit_t, position(heli))
                tg = d.get("Target")
                if tg:
                    targets[m] = tuple(tg) if isinstance(tg, list) else (tg.get("X"), tg.get("Y"), tg.get("Z"))
        if time.time() >= next_sample:
            next_sample = time.time() + 2.0
            hp = position(heli)
            timeline.append((round(t, 1), tuple(round(v) for v in hp) if hp else None,
                             [(_phase(_descent(m)) if _descent(m) else ("aboard" if _aboard(m) else "down")) for m in men],
                             (maneuver(ids.get("Airborne squad")) or {}).get("kind")))
        if len(jumped) == 8 and all(not _descent(m) for m in men):
            # ⭐ the walk to the rally: a round canopy holds a man 40 s on the ground (LandingSeconds — out of the harness), then the
            #   stick's ends walk ~140 m at 3 m/s. Wait for the squad to stand together, up to 150 s, rather than a fixed sleep.
            walk_from = sim_time() or t
            def _spread():
                pts = [p for p in (position(m) for m in men) if p]
                mid = (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts))
                return max(flat(p, mid) for p in pts)
            wait_for(lambda: _spread() <= 20, 150, every=2)
            print(f"      assembled after {round((sim_time() or 0) - walk_from, 1)} s of walking (spread {_spread():.1f} m)")
            break
        time.sleep(0.25)
    for row in timeline[:80]:
        print("      drop @", row)
    c.ok(len(jumped) == 8, f"① the whole stick jumped — {len(jumped)}")
    times = sorted(v[0] for v in jumped.values())
    gaps = [round(b - a, 2) for a, b in zip(times, times[1:])]
    c.ok(gaps and min(gaps) >= 0.7, f"① one man at a time — gaps {gaps}")
    land = {m: position(m) for m in men}
    miss = {m: round(flat(land[m], targets[m]), 1) for m in men if m in targets and land[m]}
    print("    landing misses (m):", list(miss.values()))
    c.ok(len(miss) == 8 and max(miss.values()) <= 150, f"② ★ all eight down within 150 m of their points — {list(miss.values())}")
    pts = [p for p in land.values() if p]
    mid = (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)) if pts else (0, 0)
    spread = max(flat(p, mid) for p in pts) if pts else 9e9
    c.ok(spread <= 20, f"③ ★ the squad stands assembled — every man within 20 m of the middle ({spread:.1f} m)")
    c.ok(not any(_aboard(m) for m in men) and (position(heli) or (0, 0, 0))[0] > 450,
         f"④ the helicopter flies on, empty — at {position(heli)}")


def run_eject(c, timeout):
    ids = ids_by_name()
    jet, patrol = ids.get("A-10"), [ids.get(f"Patrol {i}") for i in (1, 2, 3)]
    if not c.ok(jet is not None and None not in patrol, "the A-10 and the patrol are loaded"):
        return
    before = {r.get("networkId") for r in data(call("GET", "/entities")) or []}
    call("POST", "/sim/play", {})
    pilot, spawned_at, canopy_at, timeline, next_sample = None, None, None, [], 0.0
    end = time.time() + timeout + 180
    while time.time() < end:
        t = sim_time() or 0.0
        if pilot is None:
            for r in data(call("GET", "/entities")) or []:
                if r.get("networkId") not in before and (comps(r["networkId"]).get("TkbIdentity") or {}).get("TkbType") == 206:
                    pilot, spawned_at = r["networkId"], t
                    break
        else:
            d = _descent(pilot)
            if canopy_at is None and d and _phase(d) in ("Canopy", "Flare", "Landing", "Landed"):
                canopy_at = t
        if time.time() >= next_sample:
            next_sample = time.time() + 2.0
            jp = position(jet)
            pp = position(pilot) if pilot else None
            st = data(call("GET", f"/entities/{pilot}/state")) or {} if pilot else {}
            timeline.append((round(t, 1), tuple(round(v) for v in jp) if jp else None, health(jet),
                             tuple(round(v) for v in pp) if pp else None, _phase(_descent(pilot)) if pilot and _descent(pilot) else None,
                             (st.get("behavior") or {}).get("name"), st.get("stance")))
        if pilot and spawned_at is not None and t - spawned_at > 120:
            break
        time.sleep(0.25)
    for row in timeline[:90]:
        print("      eject @", row)
    c.ok(pilot is not None, "① one Pilot (206) spawned")
    if pilot is None:
        return
    c.ok(canopy_at is not None and canopy_at - spawned_at <= 3.5, f"② the canopy open by ~3 s — {None if canopy_at is None else round(canopy_at - spawned_at, 2)} s")
    jp = position(jet)
    c.ok((health(jet) or 0) <= 0 and jp is not None and jp[2] < 20, f"③ the jet is down — health {health(jet)}, at {jp}")
    pp = position(pilot)
    near = min(flat(pp, position(e)) for e in patrol if position(e)) if pp else 0
    time.sleep(6)
    pp2 = position(pilot)
    st = data(call("GET", f"/entities/{pilot}/state")) or {}
    c.ok(not _descent(pilot) and near >= 100, f"④ ★ the pilot is down, {near:.0f} m from the nearest of the patrol")
    c.ok(pp2 is not None and flat(pp, pp2) < 2.0, f"④ ★ and stays down — moved {flat(pp, pp2) if pp2 else None} m in 6 s; behaviour {(st.get('behavior') or {}).get('name')}, stance {st.get('stance')}")


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
             "ua-fire-and-movement-pairs": run_fire_and_movement_pairs,
             "ua-bounding-overwatch": run_bounding_overwatch,
             # ⭐ CE-2159 X-d — the squad danger crossing (Squad Tactics §7.8.4)
             "ua-squad-danger-crossing": run_squad_danger_crossing,
             "ua-squad-danger-crossing-contact": run_squad_danger_crossing_contact,
             "ua-squad-march-loose": run_squad_march_loose,
             # ⭐ CE-2161 — the D3 Bound choice over open ground (CE-3081)
             "ua-squad-bound-open-ground": run_squad_bound_open_ground,
             # ⭐ CE-2173 slice 5 — a plane bombs a point (Air Operations §9c)
             "ua-air-strike": run_air_strike,
             "ua-air-attack": run_air_attack,
             "ua-air-defence": run_air_defence,
             "ua-tank-corner": run_tank_corner,
             "ua-javelin-lock": run_javelin_lock,
             "ua-technical-scoot": run_technical_scoot,
             "ua-ifv-standoff": run_ifv_standoff,
             "ua-tank-ammo": run_tank_ammo,
             # ⭐ CE-2176 IF-4 — mortars, artillery and the forward observer (Indirect Fire §6)
             "ua-indirect-fire": run_indirect_fire,
             # ⭐ CE-1118 H-2 — airborne insertion (Airborne Insertion §8)
             "ua-fast-rope-roof": run_fast_rope_roof,
             "ua-airborne-drop": run_airborne_drop,
             "ua-eject": run_eject}


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
