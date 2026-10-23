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
import argparse, json, math, os, re, signal, subprocess, sys, time, urllib.error, urllib.request

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


def _off_grid(since):
    """⭐ Urban Battle §6 (2026-10-20) — the WARN lines the cluster logged since byte `since` for entities placed outside the terrain's grids
    (an entity there can be neither seen, nor hit, nor found — measured: a mortar 800 m past demo-city's edge was invisible to everyone)."""
    path = os.path.join("/tmp", f"utility-demo-cluster-{BASE.rsplit(':', 1)[-1]}.log")
    try:
        with open(path, errors="replace") as fh:
            fh.seek(since)
            text = fh.read()
    except OSError:
        return []
    lines = [l.strip() for l in text.splitlines() if "is outside the collider grid" in l]
    # the grid RE-FITS to the resident terrain during the load (SpatialGridFit, CE-3018; 300 cells): judge each position against the last fit
    fits = re.findall(r"collider grid origin \((-?[\d.]+),(-?[\d.]+)\) cell ([\d.]+) m", text)
    if not fits:
        return lines
    ox, oy, cell = (float(v) for v in fits[-1])
    lo_x, lo_y, hi_x, hi_y = ox, oy, ox + 300 * cell, oy + 300 * cell
    out = []
    for l in lines:
        m = re.search(r"at \((-?[\d.]+), (-?[\d.]+)\)", l)
        if not m or not (lo_x <= float(m.group(1)) <= hi_x and lo_y <= float(m.group(2)) <= hi_y):
            out.append(l)
    return out


def _log_size():
    try:
        return os.path.getsize(os.path.join("/tmp", f"utility-demo-cluster-{BASE.rsplit(':', 1)[-1]}.log"))
    except OSError:
        return 0


def load(name):
    log_at = _log_size()
    resp = call("POST", "/scenario/load/live", {"name": name, "waitForReady": True})
    if "http" in resp and resp.get("http") == 504 and "did not reach a settled" in str(resp.get("body")):
        # ⭐ Urban Battle §6 — a big terrain (demo-city: 541 entities, ~29 s to OperatingLive, measured 2026-10-20) can outlast the
        #   endpoint's fixed 30 s wait. Keep waiting on /status for OperatingLive with a steady entity count — never for ever.
        prev, steady, end = None, 0, time.time() + 120
        while time.time() < end and steady < 3:
            st = data(call("GET", "/status")) or {}
            count = st.get("entityCount")
            steady = steady + 1 if st.get("clusterState") == "OperatingLive" and count == prev else 0
            prev = count
            time.sleep(1.0)
        if steady < 3:
            sys.exit(f"load failed: {resp} — and no settled OperatingLive within 120 s more (last status {st})")
        print(f"   (the load outlasted the endpoint's 30 s wait; settled at {prev} entities)")
    elif "http" in resp:
        sys.exit(f"load failed: {resp}")
    call("POST", "/perspective", {"name": "Scenario"})
    wait_for(lambda: False, 2, every=1.0)
    off = _off_grid(log_at)
    if off:
        # ⚠ a WARNING, not a refusal (user, 2026-10-20): off-map artillery is normal (indirect-fire-range's guns stand 2-5 km out). Off the
        #   grids an entity cannot be SEEN or collide; whether it is HEARD is Architect Question 95 §B. Say so, so a check that relies on
        #   seeing it is not misread.
        print(f"   ⚠ {len(off)} entit{'y' if len(off) == 1 else 'ies'} outside the terrain's grids (not seen, no collision there):\n     "
              + "\n     ".join(off[:8]))
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


def launch(port, mode="all"):
    """Start a fresh ClusterRunner --mode <mode> (all by default) for this run; returns the process."""
    if not os.path.exists(RUNNER):
        sys.exit(f"build it first: dotnet build Hrot/Runner/Hrot.ClusterRunner ({RUNNER} missing)")
    # ⭐ a merge does not rebuild the runner: 📌 2026-10-15 a live ua-air-strike ran a 10:08 binary after a 13:03 merge of the new fragment
    #   law and "failed" ④ on the old law. Refuse a runner older than the newest engine source unless told otherwise.
    # the newest assembly in the runner's bin is the build moment — MSBuild leaves the runner's OWN dll untouched when only a
    # dependency changed, so its mtime alone reports a fresh build as stale
    built = max(os.path.getmtime(os.path.join(os.path.dirname(RUNNER), f)) for f in os.listdir(os.path.dirname(RUNNER)) if f.endswith(".dll"))
    stale = newest_source_after(built)
    if stale:
        msg = f"the runner ({RUNNER}) is older than {stale} — rebuild: dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore"
        if os.environ.get("HROT_ALLOW_STALE_RUNNER") != "1":
            sys.exit(msg + "  (or HROT_ALLOW_STALE_RUNNER=1)")
        print("   ⚠ " + msg)
    log = open(os.path.join("/tmp", f"utility-demo-cluster-{port}.log"), "w")
    env = dict(os.environ, HROT_DEBUG_API_PORT=str(port))
    proc = subprocess.Popen(["xvfb-run", "-a", "dotnet", RUNNER, "--mode", mode], cwd=REPO, env=env,
                            stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    if not wait_for(lambda: data(call("GET", "/status")), 180, every=1):
        stop(proc)
        sys.exit(f"the cluster did not answer on {BASE} within 180 s — see {log.name}")
    print(f"   launched ClusterRunner --mode {mode} (pid {proc.pid}, log {log.name})")
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
    # ⭐ CE-2233 (CIV-0, Civilians CV-8) — the civilian is NEUTRAL: seen and remembered (a soldier sees civilians), but no danger
    #   (ThreatDanger 0 ⇒ score 0) and never the pick
    if "Civilian Near" in names:
        c.ok(scores[names.index("Civilian Near")] == 0, f"the neutral civilian scores 0 (no danger): {scores[names.index('Civilian Near')]}")
    c.ok((d.get("winner") or {}).get("name") != "Civilian Near", "the neutral civilian is never the winner")
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

    # ① the squad: its five members, the merged pool holds the three hostiles. ⭐ CE-2125 (2026-10-11) migrated the scenario to a
    #   body-less "Squad" commander — the leader is a member now (Leader + four riflemen); ⛔ SUPERSEDED: squad(leader), 4 members
    #   (the check went red on that, not on the behaviour — measured 2026-10-16: every man remembered all three hostiles).
    cmd = ids.get("Squad") or leader
    sq = wait_for(lambda: (x := squad(cmd)) and x.get("memberCount", 0) >= 5 and x.get("contactCount", 0) >= 3 and x, timeout)
    if not c.ok(sq is not None, f"the squad forms (5 members) and its merged pool holds the three hostiles — {squad(cmd).get('memberCount')} / {squad(cmd).get('contactCount')}"):
        return

    # ② every member is assigned a target; the fire is spread; no target has more than two
    def assigned():
        x = squad(cmd)
        a = [((m.get("assignment") or {}).get("name")) for m in x.get("members", []) if m.get("networkId") in riflemen]
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
    # ⭐ CE-2344 (R-2066 E, 4e8ac7075) — in a LULL (no round at the squad for 10 s) the running ReactToContact takes Rescue directly, with no
    #   utility pass, so no decision is recorded. This Hostile's rifle is empty: the squad is never fired on, so it is always the lull path
    #   (📐 bisected 2026-10-22). The Rescue is read from the drill the reaction runs (kindName CasualtyCare), or from the decision.
    d = wait_for(lambda: ((x := decision(utility(sq), "Squad contact response")) and x.get("winner") == "Rescue" and x)
                         or ((m := maneuver(sq)) and m.get("kindName") == "CasualtyCare" and m), 15)
    c.ok(d is not None, f"Rescue wins — the decision, or the lull's direct Rescue (CE-2344) "
                        f"({decision(utility(sq), 'Squad contact response') or (maneuver(sq) or {}).get('kindName')})")
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


def task_name(nid):
    return ((data(call("GET", f"/entities/{nid}/state")) or {}).get("behavior") or {}).get("name")


def run_sl0_platoon_march(c, timeout):
    """⭐ CE-2262 SL-0 (Building Clearing §7, CB-16) — the first NESTED hierarchy: a body-less PLATOON (305) marching three SQUADS
    (302) 110 m north up the open street. Each effective squad gets an ORDER and fulfils it its own way (Order.Move -> its class's
    InfantrySquadMove, a SquadMarch of its men); the squad whose men are all dead gets nothing, and the platoon's centre ignores it."""
    ids = ids_by_name()
    pl, sq = ids.get("Platoon"), {n: ids.get(n) for n in ("Alpha", "Bravo", "Charlie")}
    men = {n: [ids.get(f"{n} {k}") for k in range(1, 5)] for n in ("Alpha", "Bravo")}
    if not c.ok(pl and None not in sq.values() and None not in men["Alpha"] + men["Bravo"], "the platoon, three squads and their men are loaded"):
        return
    goal = (200.0, 150.0)
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(pl) == "SquadMarch", 10), f"① the platoon runs SquadMarch (task: {task_name(pl)})")
    squad_tasks = ("InfantrySquadMove", "InfantrySquadHold")
    ok = wait_for(lambda: all(task_name(sq[n]) in squad_tasks for n in ("Alpha", "Bravo")), 15)
    c.ok(ok, f"② Alpha and Bravo each fulfil the platoon's order their own way: {[task_name(sq[n]) for n in ('Alpha', 'Bravo')]}")
    c.ok(task_name(sq["Charlie"]) not in squad_tasks,
         f"③ Charlie, every man dead, is given nothing (task: {task_name(sq['Charlie'])})")
    def near(n):
        return all((p := position(m)) and ((p[0] - goal[0]) ** 2 + (p[1] - goal[1]) ** 2) ** 0.5 <= 40 for m in men[n])
    arrived = wait_for(lambda: near("Alpha") or near("Bravo"), 200, every=2)
    c.ok(arrived, "④ the platoon's men reach the objective area (every man of a squad within 40 m)")
    pp, cp = position(pl), position(sq["Charlie"])
    c.ok(pp and cp and pp[1] - cp[1] > 40,
         f"⑤ the platoon stands with its effective squads, not pulled back to the dead one (platoon y={pp and round(pp[1])}, Charlie y={cp and round(cp[1])})")


def squad_reports(unit=None, last=256):
    q = f"/squad/reports?last={last}" + (f"&unit={unit}" if unit else "")
    return (data(call("GET", q)) or {}).get("reports") or []


def run_cc_platoon_bound(c, timeout):
    """⭐ CE-2294 CC-D (Command Chain §1, §8) — the first two-level command chain. A body-less PLATOON (305) runs PlatoonBound over
    Alpha and Bravo: Bravo moves to the support point; only when Bravo's staff reports SET (3 s after its task ends) is Alpha ordered
    to the objective. Three armed neutrals north of it turn hostile: Alpha's staff sends ONE SPOT, the platoon's SOP (BasicPlatoonSop,
    its TKB default) reacts with PlatoonHold — the task pauses, both squads hold. The enemy goes down, the SPOT says lost, the hold
    ends, and the task RESUMES in its leg: Alpha is ordered on and reports SET, and the task completes."""
    ids = ids_by_name()
    pl, al, br = ids.get("Platoon"), ids.get("Alpha"), ids.get("Bravo")
    enemies = [ids.get(f"Enemy {i}") for i in (1, 2, 3)]
    if not c.ok(pl and al and br and None not in enemies, "the platoon, Alpha, Bravo and three (neutral) enemies are loaded"):
        return
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(pl) == "PlatoonBound", 10), f"the platoon runs PlatoonBound (task: {task_name(pl)})")
    moved = wait_for(lambda: task_name(br) == "InfantrySquadMove", 15)
    c.ok(moved and task_name(al) != "InfantrySquadMove",
         f"② Bravo is ordered to the support point first; Alpha waits (Bravo: {task_name(br)}, Alpha: {task_name(al)})")

    def progress_from(u, after=None):
        return next((r for r in reversed(squad_reports()) if r.get("from") == u and r.get("kind") == "Progress"
                     and r.get("subtype") == "Set" and r.get("verdict") == "folded" and (after is None or r["seenAt"] > after)), None)
    bset = wait_for(lambda: progress_from(br), 240, every=1)
    if not c.ok(bset is not None, "① Bravo reaches the support point and its staff reports SET"):
        return
    lag = bset["seenAt"] - bset["observedAt"]
    c.ok(2.9 <= lag <= 3.6, f"① …into the platoon's picture {lag:.2f} s after Bravo's task ended (3 s + a frame)")
    a_order = wait_for(lambda: (o := data(call("GET", f"/entities/{al}/order")) or {}).get("task") and o, 15)
    since = ((a_order or {}).get("task") or {}).get("since")
    c.ok(since is not None and since >= bset["seenAt"] - 0.05,
         f"② only THEN is Alpha ordered (its task {((a_order or {}).get('task') or {}).get('behavior')} since {since}, Bravo's SET seen at {bset['seenAt']:.2f})")

    for e in enemies:
        call("POST", f"/entities/{e}/attribute", {"patchJson": {"Affiliation": "FORCE_OPPOSING"}})
    spot = wait_for(lambda: next((r for r in reversed(squad_reports(unit=al)) if r.get("from") == al and r.get("kind") == "Spot"
                                  and (r.get("count") or 0) > 0), None), 90, every=1)
    if not c.ok(spot is not None, "③ Alpha's staff SPOTs the enemy"):
        return
    time.sleep(4)
    first = [r for r in squad_reports(unit=al) if r.get("from") == al and r.get("kind") == "Spot" and r["seenAt"] <= spot["seenAt"] + 4]
    c.ok(len(first) == 1 and spot.get("size") in ("Team", "Individual"),
         f"③ ONE SPOT for the element, not one per contact (count {spot.get('count')}, size {spot.get('size')}; SPOTs in its first 4 s: {len(first)})")

    held = wait_for(lambda: task_name(pl) == "PlatoonHold", 15)
    c.ok(held, f"④ the platoon's SOP reacts: PlatoonHold (task: {task_name(pl)})")
    both = wait_for(lambda: task_name(al) == "InfantrySquadHold" and task_name(br) == "InfantrySquadHold", 15)
    c.ok(both, f"④ …both squads hold (Alpha: {task_name(al)}, Bravo: {task_name(br)})")
    st = data(call("GET", f"/entities/{pl}/state")) or {}
    print(f"    platoon state while holding: behavior={st.get('behavior')}, paused={st.get('pausedTask') or st.get('paused')}")

    t_kill = sim_time() or 0
    for e in enemies:
        set_health(e, 0)
    resumed = wait_for(lambda: task_name(pl) == "PlatoonBound", 90, every=1)
    c.ok(resumed, f"④ the element is gone (a lost SPOT), the hold ends and the task RESUMES (task: {task_name(pl)})")
    re_ordered = wait_for(lambda: task_name(al) == "InfantrySquadMove", 15)
    c.ok(re_ordered and task_name(br) != "InfantrySquadMove",
         f"④ …in its leg: Alpha is ordered on to the objective, Bravo (behind the plan) is not (Alpha: {task_name(al)}, Bravo: {task_name(br)})")
    aset = wait_for(lambda: progress_from(al, after=t_kill), 240, every=1)
    c.ok(aset is not None, "the assault squad reaches the objective and reports SET")
    done = wait_for(lambda: task_name(pl) != "PlatoonBound", 15)
    c.ok(done, f"…and the platoon's task completes (task now: {task_name(pl)})")

    pic = data(call("GET", f"/entities/{pl}/reports")) or {}
    subs = {s.get("networkId"): s for s in (pic.get("picture") or {}).get("subordinates", [])}
    log = squad_reports(unit=pl)
    c.ok(al in subs and br in subs and all(r.get("observedAt") is not None and r.get("seenAt") is not None for r in log) and len(log) >= 4,
         f"⑤ every report is on the debug HTTP with ObservedAt and arrival ({len(log)} in the log; rows: "
         f"{[(s.get('name'), s.get('progress'), s.get('lastSeq')) for s in subs.values()]})")
    kinds = sorted({(r.get("kind"), r.get("subtype")) for r in log}, key=str)
    print(f"    report kinds seen: {kinds}")


def _look(nid):
    """LD-I — the look block of /entities/{id}/state, with compass bearings added (0 = north, clockwise; the state's yaws are
    0 = +X east, counter-clockwise)."""
    lk = (data(call("GET", f"/entities/{nid}/state")) or {}).get("look") or {}
    for k in ("bodyYawDeg", "eyeYawDeg"):
        if lk.get(k) is not None:
            lk[k.replace("YawDeg", "Bearing")] = (90.0 - lk[k]) % 360.0
    return lk


def _off(a, b):
    return abs((a - b + 540.0) % 360.0 - 180.0)


def _remembers(nid, who, visual_only=True):
    for x in (data(call("GET", f"/entities/{nid}/memory")) or {}).get("slots") or []:
        if x.get("networkId") == who and (not visual_only or (int(x.get("modalities") or 0) & 1)):   # SensorModality.Visual = 1 (a bitmask)
            return True
    return False


def run_ld_sentry_surprise(c, timeout, control=False):
    """⭐ CE-2298 LD-5 (Look Direction §3.4) — an order points the eyes and the cone follows them. A sentry (200° field of view,
    HOLDING, his sector set to watch north over POST /entities/{id}/sector, starting faced north-east) on bt-range; hostile A crosses 110 m north of him (the clear east strip); hostile B walks up
    behind him to 40 m, then is ordered to fire. ① the lasting look turns his BODY north · ② A seen (visual) · ③ B NOT in his memory
    while it walks · ④ B's shot: he turns to the sound and sees B. ⑤ The control run (`ld-sentry-surprise-360`, 360°) sees B while
    it walks — the cone is what hid him."""
    ids = ids_by_name()
    s, a, b = ids.get("Sentry"), ids.get("Hostile A"), ids.get("Hostile B")
    if not c.ok(s and a and b, "the sentry and hostiles A and B are loaded"):
        return
    sec = call("POST", f"/entities/{s}/sector", {"watch": 0})
    c.ok(isinstance(sec, dict) and (data(sec) or {}).get("watch") == 0, f"the operator gives the sentry his sector: watch north ({json.dumps(data(sec) or sec)[:160]})")
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(s) == "PersonHold", 10), f"the sentry holds (task: {task_name(s)})")
    if not control:   # (the control sees B at once and turns to him — that is its point, so ① is not asked of it)
        turned = wait_for(lambda: (lk := _look(s)).get("bodyBearing") is not None and _off(lk["bodyBearing"], 0.0) <= 10.0 and lk, 8)
        lk = _look(s)
        c.ok(bool(turned), f"① a lasting look turns his body north within 8 s (body {lk.get('bodyBearing')}, eyes {lk.get('eyeBearing')}, source {lk.get('source')})")
    seen_a = wait_for(lambda: _remembers(s, a), 20)
    if not seen_a:
        ps, pa = position(s), position(a)
        md = data(call("GET", f"/entities/{s}/memory")) or {}
        ent = data(call("GET", f"/entities/{s}")) or {}
        comps = ent.get("components") or ent
        rec = {"perception": next((v for k, v in (comps.items() if isinstance(comps, dict) else []) if "PerceptionReceptor" in k), None)}
        print(f"    ② diagnostic: sentry {ps}, A {pa}, A's task {task_name(a)}, sentry memory "
              f"{[(x.get('networkId'), x.get('modalities'), x.get('freshness')) for x in md.get('slots') or []]}, receptor {rec.get('perception') or rec.get('receptor')}")
    c.ok(seen_a, "② hostile A, crossing in front of him, is SEEN (visual)")
    arrived = wait_for(lambda: (p := position(b)) is not None and p[1] >= 38.0, 40, every=1)
    seen_b = _remembers(s, b) if control else _remembers(s, b, visual_only=False)   # control: SEEN · demo: not in memory at all
    if control:
        c.ok(seen_b, f"⑤ the 360° control SEES B while it walks up behind him (B arrived: {bool(arrived)})")
        return
    c.ok(bool(arrived) and not seen_b, f"③ B walks up behind him to 40 m UNSEEN (arrived: {bool(arrived)}, in his memory: {seen_b})")
    _if_order(b, "Attack", {"Target": s})
    heard = wait_for(lambda: (lk := _look(s)).get("eyeBearing") is not None and _off(lk["eyeBearing"], 180.0) <= 80.0 and lk, 10, every=0.2)
    c.ok(bool(heard), f"④ B fires: his eyes go to the sound behind him (look: {_look(s)})")
    c.ok(wait_for(lambda: _remembers(s, b), 6, every=0.2), "④ …and B is SEEN")


def run_ld_sentry_surprise_360(c, timeout):
    run_ld_sentry_surprise(c, timeout, control=True)


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
    shots, track, tick, seen_at, timed = {}, {atgm: [], hmg: []}, 0, {}, {atgm: [], hmg: []}
    end = time.time() + timeout + 150
    while time.time() < end:
        for x in _if_shots_all():
            shots[x.get("seq")] = x
            seen_at.setdefault(x.get("seq"), tick)
        for t in (atgm, hmg):
            pt = position(t)
            if pt:
                track[t].append(tuple(round(v) for v in pt[:2]))
                timed[t].append((tick, pt[0], pt[1]))
        tick += 1
        if tick % 10 == 0:   # what the ATGM technical's sensors answer and why its weapon holds, every ~10 s
            st = ((data(call("GET", f"/entities/{atgm}/weapons")) or {}).get("status") or {})
            md = data(call("GET", f"/entities/{atgm}/memory")) or {}
            tst = ((data(call("GET", f"/entities/{tank}/weapons")) or {}).get("status") or {})
            tmd = data(call("GET", f"/entities/{tank}/memory")) or {}
            hst = ((data(call("GET", f"/entities/{hmg}/weapons")) or {}).get("status") or {})
            print(f"      HMG @{tick}s at {track[hmg][-1] if track[hmg] else None} weapon {hst.get('reason')} {hst.get('progress')}/{hst.get('needed')} health {health(hmg)}")
            print(f"      TANK @{tick}s weapon {tst.get('reason')} · knows {[(m.get('networkId'), round(m.get('freshness') or 0, 1)) for m in (tmd.get('slots') or [])][:3]} · health {health(tank)}")
            print(f"      ATGM @{tick}s at {track[atgm][-1] if track[atgm] else None} weapon {st.get('reason')} {st.get('progress')}/{st.get('needed')}"
                  f" knows {[(m.get('networkId'), round(m.get('freshness') or 0, 1)) for m in (md.get('slots') or [])][:3]}")
            for sn in (data(call("GET", f"/entities/{atgm}/sensors")) or {}).get("sensors") or []:
                ans = sn.get("answer") or {}
                print("        sensor", {k: sn.get(k) for k in ("kind", "template", "status")},
                      {k: (v if not isinstance(v, list) else v[:3]) for k, v in ans.items()} if isinstance(ans, dict) else ans)
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

    def scooted(tech, owner_names):
        # ① shoot AND scoot: fires from two points — or, after its first shot, drives ≥ 5 m from where it fired (the tank may kill it first)
        xs = [x for k, v in by.items() if any(n in str(k) for n in owner_names) for x in v if x.get("muzzle")]
        if not xs:
            return None
        x0 = min(xs, key=lambda x: x.get("seq") or 0)
        m, t0 = x0["muzzle"], seen_at.get(x0.get("seq"), 0)
        away = [(round(px), round(py)) for (tk, px, py) in timed[tech] if tk > t0 and ((px - m[0]) ** 2 + (py - m[1]) ** 2) ** 0.5 >= 5]
        return away[:1]
    a_sc, h_sc = scooted(atgm, ("ATGM", "Kornet")), scooted(hmg, ("HMG", "M2"))
    # ⭐ the design's ending (Urban Armour Tactics row ua-technical-scoot): the tank ranks the ATGM technical first and KILLS it — so a
    #   technical that fired and was then destroyed before it could drive away has done its part (measured 2026-10-16: killed ~2 s after its
    #   first Kornet in one run, scooted to a new point in the next)
    a_killed = bool(a_pts) and _dead(atgm)
    c.ok(len(a_pts) >= 2 or bool(a_sc) or a_killed,
         f"① the ATGM technical shoots and scoots (or is killed by the tank after its shot) — fired from {a_pts}, then drove to {a_sc}, dead {_dead(atgm)}")
    c.ok(len(h_pts) >= 2 or bool(h_sc), f"① the HMG technical shoots and scoots — fired from {h_pts}, then drove to {h_sc}")
    tank_shots = by.get("Tank", [])

    def nearest(tech, x):
        # which technical a round was AIMED at: its aim point within 8 m of where that technical was (a mounted gun or crew is its own entity)
        a = x.get("aim") or x.get("end")
        tk = seen_at.get(x.get("seq"), 0)
        ps = [(abs(k - tk), px, py) for (k, px, py) in timed[tech]]
        if not a or not ps:
            return False
        _, px, py = min(ps)
        return ((a[0] - px) ** 2 + (a[1] - py) ** 2) ** 0.5 <= 8
    first = None
    if tank_shots:
        x = tank_shots[0]
        first = "ATGM Technical" if nearest(atgm, x) else "HMG Technical" if nearest(hmg, x) else name.get(x.get("target"))
    c.ok(first == "ATGM Technical", f"② the tank's first round goes at the ATGM technical — {first} ({len(tank_shots)} rounds)")
    print(f"    health: tank {health(tank)} · ATGM technical {health(atgm)} · HMG technical {health(hmg)}")


# ── ua-rpg-window (Urban Armour V6; Exposure Cycle §7 — the AT side is a composition, no new node) — an RPG ambush from a window ──────
#   bt-range: two IFVs (Bradley) on BasicVehicleSop in the street ~50 m south of House A (⚠ at 23 m the IFV drove under the window and the
#   RPG man's steep shot met his own wall — CE-2218; one IFV dies in 2 rockets before a 3rd exposure, so two give the relocation a chance); an RPG man (Insurgent 2003) at
#   House A's upper window on WindowDuel shape A with exposuresPerPosition 1 (PeekAndFire: prone under the sill, up to fire, another window after each rocket),
#   knowing where the IFV is. ① the RPG fires at the IFV and hurts it · ② it fires from two windows (≥ 3 m apart) · ③ the IFV fires back.
def run_rpg_window(c, timeout):
    ids = ids_by_name()
    ifv, ifv2, rpg = ids.get("IFV"), ids.get("IFV 2"), ids.get("RPG Man")
    if not c.ok(None not in (ifv, ifv2, rpg), "both IFVs and the RPG man are loaded"):
        return
    call("POST", "/sim/play", {})
    shots, track = {}, []
    end = time.time() + timeout + 60
    while time.time() < end:
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        pt = position(rpg)
        if pt:
            track.append(tuple(round(v, 1) for v in pt[:3]))
        st = ((data(call("GET", f"/entities/{ifv}/weapons")) or {}).get("status") or {})
        md = data(call("GET", f"/entities/{ifv}/memory")) or {}
        print(f"      @{len(track)}s IFV weapon {st} · knows {[(m.get('networkId'), round(m.get('freshness') or 0, 1)) for m in (md.get('slots') or [])][:3]}"
              f" · RPG health {health(rpg)} · IFV health {health(ifv)}")
        rst = ((data(call("GET", f"/entities/{rpg}/weapons")) or {}).get("status") or {})
        rmd = data(call("GET", f"/entities/{rpg}/memory")) or {}
        print(f"        RPG weapon {rst} · knows {[(m.get('networkId'), round(m.get('freshness') or 0, 1), m.get('position') or m.get('pos')) for m in (rmd.get('slots') or [])][:3]}")
        if (_dead(ifv) and _dead(ifv2)) or _dead(rpg):
            break
        time.sleep(1.0)
    name = {v: k for k, v in ids.items()}
    xs = sorted(shots.values(), key=lambda x: x.get("seq") or 0)
    print("    shots by shooter:", dict(__import__("collections").Counter(name.get(x.get("shooter"), x.get("shooter")) for x in xs)))
    mine = [x for x in xs if x.get("shooter") == rpg]
    for x in mine[:6]:
        print("      RPG", {k: x.get(k) for k in ("muzzle", "aim", "end", "hit", "outcome", "hitFace", "penetration", "crossings")})
    stops = []
    for q in track:
        if not stops or sum((a - b) ** 2 for a, b in zip(q, stops[-1])) ** 0.5 > 1.5:
            stops.append(q)
    print(f"    RPG man path (1.5 m steps): {stops[:20]}")
    pts = []
    for x in mine:
        m = x.get("muzzle")
        if m and all(((m[0] - q[0]) ** 2 + (m[1] - q[1]) ** 2) ** 0.5 > 3 for q in pts):
            pts.append((round(m[0], 1), round(m[1], 1)))
    c.ok(len(mine) >= 1 and (_hurt(ifv, 500) or _hurt(ifv2, 500)), f"① the RPG fires at an IFV and hurts it — {len(mine)} rocket(s), IFV health {health(ifv)} · IFV 2 {health(ifv2)}")
    # ② the relocation: fires from a second window — or MOVES to one (≥ 3 m along the same floor) after he fired (an AT man fires
    #   and moves: exposuresPerPosition 1 — the IFVs' fire may kill him before a second rocket)
    z0 = stops[0][2] if stops else 0
    moved = [q for q in stops[1:] if abs(q[2] - z0) < 1.0 and ((q[0] - stops[0][0]) ** 2 + (q[1] - stops[0][1]) ** 2) ** 0.5 >= 3]
    c.ok(len(pts) >= 2 or (len(mine) >= 1 and moved), f"② the RPG relocates to another window after he fires — fired from {pts}, after {len(mine)} rocket(s) moved to {moved[:1]}")
    back = [x for x in xs if x.get("shooter") in (ifv, ifv2)]
    c.ok(len(back) >= 1, f"③ the IFV fires back — {len(back)} round(s), at {sorted({name.get(x.get('target'), x.get('target')) for x in back}, key=str)}")
    print(f"    health: IFV {health(ifv)} · IFV 2 {health(ifv2)} · RPG man {health(rpg)}")


def run_weather_regions(c, timeout):
    """⭐ CE-4035 F-01 — ua-weather-regions: the IFV drives east through the fog bank and the overcast region by itself."""
    ids = ids_by_name()
    ifv = ids.get("IFV (drive east)")
    if not c.ok(ifv is not None, "the IFV is loaded"):
        return
    x0 = (position(ifv) or [0, 0, 0])[0]
    call("POST", "/sim/play", {})
    ok = wait_for(lambda: (position(ifv) or [0, 0, 0])[0] > 360, timeout)
    c.ok(ok, f"the IFV drives east through both regions by itself (x {x0:.0f} -> {(position(ifv) or [0, 0, 0])[0]:.0f})")


# ── ua-weapon-systems (Weapon Systems §10, WS-6) — a sentry gun holds a gate, a soldier sets up an M2, a soldier picks up a Javelin ─────
#   basic-desert (x, y 0…600 — ⚠ y −4 is off the map: an M2 at y 0 put its boarding point there and every walk to it failed
#   FailedUnreachable), three vignettes south of the y 210 ridge. ① "Sentry Gun" (608, automatic) on WeaponSystemHold; an "Intruder" walks at it from 90 m:
#   the gun fires on its own and hurts him. ② "M2 Gunner" on SetUpWeapon with a STOWED "M2 HMG" 8 m off: he walks to it, takes its
#   Gunner seat, and it is Deployed (DeploySeconds 20). ③ "Javelin Man" on CarryWeapon with a stowed "Javelin" 8 m off: he carries it.
def run_weapon_systems(c, timeout):
    ids = ids_by_name()
    sentry, intruder = ids.get("Sentry Gun"), ids.get("Intruder")
    m2, gunner, jav, jman = ids.get("M2 HMG"), ids.get("M2 Gunner"), ids.get("Javelin"), ids.get("Javelin Man")
    if not c.ok(None not in (sentry, intruder, m2, gunner, jav, jman), "the sentry gun, the intruder, the M2 and its gunner, the Javelin and its man are loaded"):
        return
    deploy = lambda n: (comps(n).get("WeaponSystemState") or {}).get("Deploy")
    print(f"    start: M2 deploy {deploy(m2)} · Javelin deploy {deploy(jav)} · sentry deploy {deploy(sentry)}")
    print(f"    gunner BehaviorState {json.dumps(comps(gunner).get('BehaviorState'))[:300]} · M2 PassengerBuffer {json.dumps(comps(m2).get('PassengerBuffer'))[:200]}")
    call("POST", "/sim/play", {})
    import struct
    for i in range(10):   # the gunner's first 2 s: his walk's destination and the navigation's answer
        time.sleep(0.2)
        g = comps(gunner)
        lo = g.get("LocomotionChannel") or {}
        pb = lo.get("Params") or []
        dest = struct.unpack("<3f", bytes(pb[:12])) if len(pb) >= 12 else None
        print(f"      +{i*0.2:.1f}s gunner Loco act {lo.get('ActiveAction')} inst {lo.get('ActionInstanceId')} {lo.get('Status')} dest {tuple(round(v, 2) for v in dest) if dest else None}"
              f" · Nav {json.dumps(g.get('NavigationStatus'))[:200]} · ActionStatus {json.dumps(g.get('ActionStatus'))[:200]}")
    shots, manned, deployed, carried, t = {}, None, None, None, 0
    end = time.time() + timeout + 60
    while time.time() < end:
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        t += 1
        if manned is None and _aboard(gunner) is not None:
            manned = t
        if deployed is None and str(deploy(m2)) in ("2", "Deployed"):
            deployed = t
        if carried is None and _aboard(jav) is not None:
            carried = t
        if t % 10 == 0:
            gst = data(call("GET", f"/entities/{gunner}/state")) or {}
            print(f"      @{t}s M2 {deploy(m2)} gunner aboard {_aboard(gunner)} at {position(gunner)} behaviour {gst.get('behavior')} · Javelin aboard {_aboard(jav)} · intruder {health(intruder)}")
        if manned and deployed and carried and _hurt(intruder, 100):
            break
        time.sleep(1.0)
    name = {v: k for k, v in ids.items()}
    print("    shots by shooter:", dict(__import__("collections").Counter(name.get(x.get("shooter"), x.get("shooter")) for x in shots.values())))
    at_him = [x for x in shots.values() if x.get("shooter") == sentry]
    c.ok(len(at_him) >= 1 and _hurt(intruder, 100), f"① the sentry gun fires on its own and hurts the intruder — {len(at_him)} round(s), his health {health(intruder)}")
    c.ok(manned is not None, f"② the gunner mans the M2 — aboard after {manned} s ({_aboard(gunner)})")
    c.ok(deployed is not None, f"② the M2 is set up — Deployed after {deployed} s (now {deploy(m2)})")
    c.ok(carried is not None, f"③ the Javelin man carries the Javelin — after {carried} s ({_aboard(jav)})")
    # ⭐ CE-4035 G-11 — the manned M2 fires on the man walking into its arc (watched for up to 90 s more)
    intruder2 = ids.get("Intruder 2")
    if intruder2 is not None:
        end2 = time.time() + 90
        while time.time() < end2 and not any(x.get("shooter") in (m2, gunner) for x in shots.values()):
            for x in _if_shots_all():
                shots[x.get("seq")] = x
            time.sleep(1.0)
        by_m2 = [x for x in shots.values() if x.get("shooter") in (m2, gunner)]
        c.ok(len(by_m2) >= 1, f"④ the manned M2 fires on Intruder 2 — {len(by_m2)} round(s), his health {health(intruder2)}")


def run_m2_engage(c, timeout):
    """⭐ CE-4035 G-11 — ua-m2-engage (split out of ua-weapon-systems, whose sentry gun shot every intruder first): the gunner sets up the
    M2 and, on his type's default SOP, engages the intruder walking into its arc. The rounds are the M2's (or his, through it)."""
    ids = ids_by_name()
    m2, gunner, intruder = ids.get("M2 HMG"), ids.get("M2 Gunner"), ids.get("Intruder")
    if not c.ok(None not in (m2, gunner, intruder), "the M2, its gunner and the intruder are loaded"):
        return
    deploy = lambda n: (comps(n).get("WeaponSystemState") or {}).get("Deploy")
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: str(deploy(m2)) in ("2", "Deployed"), 60), f"① the gunner sets up the M2 — {deploy(m2)} · aboard {_aboard(gunner)}")
    shots, end = {}, time.time() + timeout + 90
    while time.time() < end:
        for x in _if_shots_all():
            shots[x.get("seq")] = x
        if any(x.get("shooter") == m2 for x in shots.values()) or not _alive_in_fight(intruder):
            break
        time.sleep(1.0)
    name = {v: k for k, v in ids.items()}
    print("    shots by shooter:", dict(__import__("collections").Counter(name.get(x.get("shooter"), x.get("shooter")) for x in shots.values())))
    by_m2 = [x for x in shots.values() if x.get("shooter") == m2]
    c.ok(len(by_m2) >= 1, f"② the manned M2 fires on the intruder — {len(by_m2)} round(s), his health {health(intruder)}")


def _alive_in_fight(nid):
    h = health(nid)
    return h is None or h > 0


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


# ── ua-hill-attack (CE-2230 H-3; hill-attack DESIGN §0, R-2022) — the platoon's hull-down attack over the basic-desert ridge ─────────
#   basic-desert: a platoon of four M1s south of the ridge (crest y 210, 10 m, x 230..320), baseline y 140, firing line y 180, against two
#   idle T-72s north of it (y 380..395) inside the target area. The commander (PlatoonHillAttack) sends a wave (a pair, by index parity) to
#   its firing slots; each tank creeps up its lane, STOPS the first tick it sees its target (hull-down: R-246), fires ONE round, reverses
#   into its baseline slot; the other pair goes; they alternate until both T-72s are dead.
#   ① every shot is fired from south of the crest (hull-down, never over the top) · ② one round per exposure · ③ never more than a pair
#   up at once (③b and a pair comes up TOGETHER — H-5) · ⑥ a pair never comes up where the last one fired (H-6) · ④ every tank that went up came back down REVERSING (nose still north) · ⑤ both T-72s die.
HILL_CREST_Y, HILL_UP_Y, HILL_HOME_Y = 210.0, 170.0, 160.0


def run_hill_attack(c, timeout):
    ids = ids_by_name()
    tanks = [ids.get(f"Tank {i}") for i in range(1, 5)]
    enemies = [ids.get("Enemy Tank 1"), ids.get("Enemy Tank 2")]
    if not c.ok(None not in (*tanks, *enemies), "the four tanks and both T-72s are loaded"):
        return
    call("POST", "/sim/play", {})
    shots, track, tick = {}, {t: [] for t in tanks}, 0
    end = time.time() + timeout + 400
    while time.time() < end:
        for x in _if_shots_all():
            shots.setdefault(x.get("seq"), (x, tick))
        for t in tanks:
            comps = (data(call("GET", f"/entities/{t}")) or {}).get("Components") or {}
            tr = comps.get("SimTransform") or {}
            p, q = tr.get("Position"), tr.get("Rotation")
            if p and q:
                # heading of the hull's +X axis from the quaternion (z-up yaw)
                yaw = math.atan2(2 * (q[3] * q[2] + q[0] * q[1]), 1 - 2 * (q[1] ** 2 + q[2] ** 2))
                track[t].append((tick, p[0], p[1], yaw))
        tick += 1
        if tick % 15 == 0:
            print(f"      @{tick}s y {[round(track[t][-1][2]) if track[t] else None for t in tanks]} · shots {len(shots)} · T-72 health {[health(e) for e in enemies]}")
        if all(_dead(e) for e in enemies):
            break
        time.sleep(1.0)
    name = {v: k for k, v in ids.items()}
    mine = sorted((x for x, _ in shots.values() if x.get("shooter") in tanks), key=lambda x: x.get("seq") or 0)
    print("    shots by shooter:", dict(__import__("collections").Counter(name.get(x.get("shooter")) for x in mine)))
    for x in mine[:12]:
        print("      shot", name.get(x.get("shooter")), "at", name.get(x.get("target"), x.get("target")), "from", [round(v, 1) for v in (x.get("muzzle") or [])],
              "outcome", x.get("outcome"))
    # ① hull-down: every friendly round leaves from south of the crest
    over = [(name.get(x.get("shooter")), round(x["muzzle"][1], 1)) for x in mine if x.get("muzzle") and x["muzzle"][1] >= HILL_CREST_Y]
    c.ok(bool(mine) and not over, f"① every round is fired from below the crest (y < {HILL_CREST_Y:.0f}) — {len(mine)} rounds, over the top {over}")

    # exposures: a run of samples at y >= HILL_UP_Y
    def exposures(t):
        out, cur = [], None
        for (tk, x, y, yaw) in track[t]:
            if y >= HILL_UP_Y and cur is None:
                cur = [tk, tk]
            elif y >= HILL_UP_Y:
                cur[1] = tk
            elif cur is not None:
                out.append(tuple(cur)); cur = None
        if cur is not None:
            out.append(tuple(cur))
        return out
    exp = {t: exposures(t) for t in tanks}
    print("    exposures (tick spans):", {name.get(t): v for t, v in exp.items()})
    # ② one round per exposure — shots attributed by the tick they were first seen (±1 for the poll)
    per = []
    for t in tanks:
        for (a, b) in exp[t]:
            n = sum(1 for x, tk in shots.values() if x.get("shooter") == t and a - 1 <= tk <= b + 1)
            per.append((name.get(t), a, n))
    c.ok(bool(per) and all(n <= 1 for (_, _, n) in per), f"② one round per exposure — {per}")
    # ③ never more than a pair up at once
    up_at = {}
    for t in tanks:
        for (tk, x, y, yaw) in track[t]:
            if y >= HILL_UP_Y:
                up_at.setdefault(tk, set()).add(name.get(t))
    crowd = max((len(v) for v in up_at.values()), default=0)
    c.ok(crowd <= 2, f"③ at most a pair on the slope at once — most at once {crowd}")
    # ③b (CE-2230 H-5, R-2022 "another pair comes up the hill") — the pair crests TOGETHER: an exposure's start has a partner's
    #    within 6 s (a wave whose partner is dead or had no slot goes alone)
    starts = sorted((a, name.get(t)) for t in tanks for (a, _) in exp[t])
    paired = [(a, n) for (a, n) in starts if any(n2 != n and abs(a2 - a) <= 6 for (a2, n2) in starts)]
    print("    exposure starts:", starts)
    c.ok(crowd == 2 and len(paired) * 2 >= len(starts), f"③b the pairs crest together — {len(paired)} of {len(starts)} exposures with a partner within 6 s")
    # ④ every exposure ends back home, reversing: while falling back the nose still points north-ish (within 90 deg of +Y: the rear never faces the enemy)
    backs = []
    for t in tanks:
        rows = track[t]
        for (a, b) in exp[t]:
            after = [r for r in rows if r[0] > b]
            home = next((r for r in after if r[2] <= HILL_HOME_Y), None)
            down = [r for r in after if home is None or r[0] <= home[0]]
            nose_north = all(abs(math.remainder(r[3] - math.pi / 2, 2 * math.pi)) < math.radians(90) for r in down) if down else None   # never the rear to the enemy (a diagonal reverse home turns the nose off north)
            backs.append((name.get(t), a, home is not None, nose_north))
    finished = [b for b in backs if b[1] < tick - 30]   # an exposure in the last 30 s may still be backing down
    c.ok(bool(finished) and all(h and n for (_, _, h, n) in finished), f"④ every tank backs into its slot, nose north — {backs}")
    # ⑥ (CE-2230 H-6, R-2023 — user 2026-10-16: "the new tank pair appears at a different location than the previous one") — where
    #    each exposure CRESTED (x at its highest point, fired or not), grouped into waves by start (within 6 s), each wave against the
    #    TWO waves before it (📐 a pair once came back to its own spot two waves later, the wave between having fired nothing)
    crests = []
    for t in tanks:
        for (a, b) in exp[t]:
            span = [r for r in track[t] if a <= r[0] <= b]
            if span:
                crests.append((a, round(max(span, key=lambda r: r[2])[1], 1)))
    crests.sort()
    waves_x = []
    for a, x in crests:
        if not waves_x or a - waves_x[-1][0] > 6:
            waves_x.append([a, []])
        waves_x[-1][1].append(x)
    near = lambda k, j: min(abs(a - b) for a in waves_x[k][1] for b in waves_x[j][1])
    last = [near(k, k - 1) for k in range(1, len(waves_x))]
    before = [near(k, k - 2) for k in range(2, len(waves_x))]
    print("    crest points by wave (x):", [w[1] for w in waves_x], "· nearest to the last pair:", [round(g, 1) for g in last],
          "· to the pair before:", [round(g, 1) for g in before])
    c.ok(len(waves_x) >= 2 and all(g >= 8 for g in last) and all(g >= 5 for g in before),
         f"⑥ each pair comes up away from where the last pair did (≥ 8 m; the one before ≥ 5 m) — {[round(g, 1) for g in last]} / {[round(g, 1) for g in before]}")
    c.ok(all(_dead(e) for e in enemies), f"⑤ both T-72s are dead — health {[health(e) for e in enemies]} · tanks {[health(t) for t in tanks]}")


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
    # ⭐ CE-4035 G-5 — ① and ② play by themselves: the file gives the observer his ObserveFire (he waits for the tubes to be manned) and
    #   the battery its time-on-target Strike, so a viewer who just loads the scenario sees them. The check only watches those two.
    target, p2 = (340, 300), (720, -195)
    t0, _ = _if_mark()
    call("POST", "/sim/play", {})
    call("POST", "/sim/timescale", {"scale": 4})
    comps = lambda n: ((data(call("GET", f"/entities/{n}")) or {}).get("Components") or {})
    boarded = wait_for(lambda: all("IsEmbarkedTag" in comps(g) for g in gunners), 60)
    if not c.ok(boarded, "the three gunners man their tubes"):
        return

    # ② time on target (the observer's ① runs while the battery does ② – ④)
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


# ── CE-2173 slice 3c — the air assault: a doors-off UH-60 lands a squad at an LZ; it dismounts and carries on with its order ────
#   docs/designs/air-attack/DESIGN_Air_Operations.md §9 (B8, B10, B11): the SOP rides, dismounts on Unloading, the task resumes.

def run_air_assault(c, timeout):
    ids = ids_by_name()
    heli, sq = ids.get("UH-60 (doors off)"), ids.get("Air assault squad")
    men = [ids.get("Leader")] + [ids.get(f"Rifleman {i}") for i in range(1, 6)]
    if not c.ok(heli is not None and sq is not None and None not in men, "the UH-60, the squad and its six men are loaded"):
        return
    c.ok(all(_aboard(m) for m in men), "all six start aboard")
    lz, objective = (190.0, 340.0), (200.0, 195.0)
    call("POST", "/sim/play", {})
    landed_at, all_out_at, left_at, timeline, next_sample = None, None, None, [], 0.0
    out = {}
    end = time.time() + timeout * 4
    while time.time() < end:
        t = sim_time() or 0.0
        ph = str((comps(heli).get("NavigationStatus") or {}).get("Phase"))
        hp = position(heli)
        if landed_at is None and hp and flat(hp, lz) <= 10.0 and ph in ("Unloading", "Landed", "Loading"):
            landed_at = t
        for m in men:
            if m not in out and not _aboard(m):
                out[m] = t
        if all_out_at is None and len(out) == len(men):
            all_out_at = t
        if landed_at is not None and left_at is None and hp and flat(hp, lz) > 30.0:
            left_at = t
        if time.time() >= next_sample:
            next_sample = time.time() + 2.0
            timeline.append((round(t, 1), ph, tuple(round(v) for v in hp) if hp else None, sum(1 for m in men if _aboard(m))))
        if all_out_at is not None and left_at is not None:
            break
        time.sleep(0.25)
    for row in timeline[:40]:
        print("      lz @", row)
    c.ok(landed_at is not None, f"① the helicopter lands at the LZ {lz} (Unloading within 10 m) — at {landed_at}")
    early = [m for m, at in out.items() if landed_at is None or at < landed_at - 0.5]
    c.ok(not early, f"① nobody leaves it in the air — {len(early)} out before the landing")
    c.ok(all_out_at is not None, f"② all six dismount at the LZ — {len(out)} out")
    c.ok(left_at is not None and all_out_at is not None and left_at >= all_out_at,
         f"③ the helicopter lifts off only once the squad is out — left {left_at}, last out {all_out_at}")
    # ④ the squad resumes its order: it marches on to the objective
    def centre():
        ps = [position(m) for m in men]
        ps = [p for p in ps if p]
        return (sum(p[0] for p in ps) / len(ps), sum(p[1] for p in ps) / len(ps)) if ps else None
    arrived = wait_for(lambda: (x := centre()) and flat(x, objective) <= 15.0 and x, timeout * 3, every=1)
    c.ok(arrived is not None, f"④ the squad marches on to its objective {objective} (centre within 15 m) — centre {centre()}")

# ── CE-2173 slice 3c — the rescue round trip: in, SquadRecover (the medic takes a downed pilot and keeps him), SquadEmbark, out ──
#   docs/designs/air-attack/DESIGN_Air_Operations.md §9j.

def run_air_rescue(c, timeout):
    ids = ids_by_name()
    heli, sq, pilot = ids.get("UH-60 (doors off)"), ids.get("Rescue squad"), ids.get("Downed pilot")
    men = [ids.get(n) for n in ("Leader", "Medic", "Rifleman 1", "Rifleman 2", "Rifleman 3")]
    if not c.ok(None not in (heli, sq, pilot) and None not in men, "the UH-60, the squad, its five men and the downed pilot are loaded"):
        return
    lz = (190.0, 340.0)
    # ⭐ IsEmbarkedTag.Vehicle reads as an ENTITY ref ("[0, v1]"), not a network id — take the helicopter's from a man aboard at start
    ref = next((_aboard(m) for m in men if _aboard(m)), None)
    if not c.ok(ref is not None and all(_aboard(m) == ref for m in men), f"all five start aboard the UH-60 ({ref})"):
        return
    call("POST", "/sim/play", {})
    landed_at, out_at, taken_at, taker, loaded_at, back_at, left_at = None, None, None, None, None, None, None
    timeline, next_sample = [], 0.0
    end = time.time() + timeout * 6
    while time.time() < end:
        t = sim_time() or 0.0
        ph = str((comps(heli).get("NavigationStatus") or {}).get("Phase"))
        hp = position(heli)
        on = _aboard(pilot)
        if landed_at is None and hp and flat(hp, lz) <= 12.0 and ph in ("Unloading", "Landed", "Loading"):
            landed_at = t
        if out_at is None and landed_at is not None and not any(_aboard(m) for m in men):
            out_at = t
        if taken_at is None and on and on != ref:   # carried by a man (an entity ref, like the helicopter's)
            taken_at, taker = t, on
        if loaded_at is None and on == ref:
            loaded_at = t
        if back_at is None and out_at is not None and all(_aboard(m) == ref for m in men):
            back_at = t
        if back_at is not None and left_at is None and hp and flat(hp, lz) > 30.0:
            left_at = t
        if time.time() >= next_sample:
            next_sample = time.time() + 2.0
            timeline.append((round(t, 1), ph, tuple(round(v) for v in hp) if hp else None,
                             sum(1 for m in men if _aboard(m) == ref), ("heli" if on == ref else ("carried" if on else "ground")), health(pilot)))
        if left_at is not None:
            break
        time.sleep(0.25)
    for row in timeline[:80]:
        print("      rescue @", row)
    c.ok(landed_at is not None and out_at is not None, f"① it lands at the LZ and the squad dismounts — landed {landed_at}, out {out_at}")
    c.ok(taken_at is not None, f"② a squad man takes the downed pilot (SquadRecover) — at {taken_at}, by {taker} (Medic {ids.get('Medic')})")
    c.ok(loaded_at is not None, f"③ ★ the pilot is seated in the UH-60 (LoadCasualty) — at {loaded_at}")
    c.ok(back_at is not None, f"④ all five men are back aboard — at {back_at}")
    c.ok(left_at is not None and back_at is not None and left_at >= back_at, f"⑤ ★ it lifts off with them all aboard — left {left_at}")
    c.ok((health(pilot) or 0) > 0, f"the pilot is alive ({health(pilot)} HP)")

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
    # ⭐ the drill ORDERS a man no sooner than MinGapShare (0.8) × the interval after the last; the exit read here is his descent's own
    #   start, which trails the order by his walk to the door — so a gap can read a little under the floor (📐 sweep 2026-10-22:
    #   [0.78 … 1.06], one 0.65). "One at a time" is the claim: no two out together, which is what 0.0 meant (2026-10-16).
    c.ok(gaps and min(gaps) >= 0.5, f"① one man at a time — gaps {gaps}")
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
    # ⚠ read from the brain node's REPLICATED copy at sub-second polling: the model opens it 3 s after its stamp
    #   (DescentModelsTests.B7_*); backend measured 3.96 s live over HTTP (HANDOFF_Backend_To_Behaviors_Live_Rerun_Results) ⇒ allow 4.5 s
    c.ok(canopy_at is not None and canopy_at - spawned_at <= 4.5, f"② the canopy open by ~3 s (≤ 4.5 s seen over the wire) — {None if canopy_at is None else round(canopy_at - spawned_at, 2)} s")
    jp = position(jet)
    c.ok((health(jet) or 0) <= 0 and jp is not None and jp[2] < 20, f"③ the jet is down — health {health(jet)}, at {jp}")
    pp = position(pilot)
    near = min(flat(pp, position(e)) for e in patrol if position(e)) if pp else 0
    time.sleep(6)
    pp2 = position(pilot)
    st = data(call("GET", f"/entities/{pilot}/state")) or {}
    c.ok(not _descent(pilot) and near >= 100, f"④ ★ the pilot is down, {near:.0f} m from the nearest of the patrol")
    c.ok(pp2 is not None and flat(pp, pp2) < 2.0, f"④ ★ and stays down — moved {flat(pp, pp2) if pp2 else None} m in 6 s; behaviour {(st.get('behavior') or {}).get('name')}, stance {st.get('stance')}")


def run_weather_sensing(c, timeout, night=False):
    """⭐ CE-4035 G-3 / G-10 — ua-weather-sensing (noon, one pair in a 60 m fog bank) and ua-night-sensing (00:30, both clear): two identical
    observer / target pairs; each target walks to its observer from 220 m. The distance at which each observer FIRST sees his target
    (visual memory) is the measurement — PerceptionWeather caps sight by the visibility (S2-1) and scales it by the light (S2-1n)."""
    ids = ids_by_name()
    labels = ("Clear", "Second") if night else ("Clear", "Fog")
    pairs = [(ids.get(f"Observer {l}"), ids.get(f"Target {l}")) for l in labels]
    if not c.ok(all(None not in pr for pr in pairs), "the two observer / target pairs are loaded"):
        return
    seen = {}
    call("POST", "/sim/timescale", {"scale": 2})
    call("POST", "/sim/play", {})
    end = time.time() + timeout + 120
    while time.time() < end and len(seen) < len(pairs):
        for (o, t), l in zip(pairs, labels):
            if l not in seen and _remembers(o, t):
                po, pt = position(o), position(t)
                seen[l] = flat(po, pt) if po and pt else None
        if all((flat(position(o) or (0, 0), position(t) or (0, 0)) < 20) for o, t in pairs):
            break
        time.sleep(0.2)
    print(f"    first seen at: {seen}")
    if night:
        for l in labels:
            c.ok(seen.get(l) is not None and seen[l] <= 75, f"① at night '{l}' is first seen well inside 150 m (≈ 40 m expected) — {seen.get(l)}")
    else:
        c.ok(seen.get("Clear") is not None and seen["Clear"] >= 120, f"① clear: seen at about the full 150 m — {seen.get('Clear')}")
        c.ok(seen.get("Fog") is not None and seen["Fog"] <= 65, f"② in the fog bank: seen only at 60 m or less — {seen.get('Fog')}")


def run_shootdown_eject(c, timeout):
    """⭐ CE-4035 G-1 — ua-shootdown-eject: the A-10 has NO eject task; the Stinger and the ZU-23 under its route shoot it, and the pilot
    ejects by EjectionSystem alone (below 20 % health, or a wreck — CE-3215). 🔒 User: "the pilot ejecting from a destroyed aircraft
    falling down should be some kind of natural behavior"."""
    ids = ids_by_name()
    jet, aa = ids.get("A-10"), [ids.get("Stinger Gunner"), ids.get("ZU-23 Technical")]
    if not c.ok(jet is not None and None not in aa, "the A-10 and the air defence are loaded"):
        return
    # ⭐ the shots are the WEAPONS' (the seat presets spawn them at load): the launcher and the ZU-23-2
    weapons = lambda: [x for x in (ids_by_name().get("Stinger (MANPADS)"), ids_by_name().get("ZU-23-2")) if x is not None]
    h0 = health(jet)
    before = {r.get("networkId") for r in data(call("GET", "/entities")) or []}
    call("POST", "/sim/play", {})
    pilot, spawned_at, canopy_at, hit_at, timeline, next_sample = None, None, None, None, [], 0.0
    end = time.time() + timeout + 180
    while time.time() < end:
        t = sim_time() or 0.0
        if hit_at is None and (health(jet) or 0) < (h0 or 0):
            hit_at = t
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
            jp, pp = position(jet), position(pilot) if pilot else None
            timeline.append((round(t, 1), tuple(round(v) for v in jp) if jp else None, health(jet),
                             tuple(round(v) for v in pp) if pp else None, _phase(_descent(pilot)) if pilot and _descent(pilot) else None))
        if pilot and spawned_at is not None and t - spawned_at > 90:
            break
        if pilot is None and t > 240:
            break
        time.sleep(0.25)
    for row in timeline[:90]:
        print("      shootdown @", row)
    shooters = aa + weapons()
    every = _if_shots_all()
    print(f"    every shot {len(every)}: " + ", ".join(f"{x.get('shooter')}->{x.get('target')}:{x.get('outcome')}" for x in every[:20]) + f" · weapons {weapons()}")
    shots = [x for x in every if x.get("shooter") in shooters]
    c.ok(bool(shots), f"① the air defence fires — {len(shots)} shots")
    c.ok(hit_at is not None, f"① …and hits the A-10 — health {h0} → {health(jet)}")
    c.ok(pilot is not None and (hit_at is None or spawned_at >= hit_at), f"② ★ one Pilot (206) ejects with no order, after the hit — {spawned_at} s (hit {hit_at} s)")
    if pilot is None:
        return
    c.ok(canopy_at is not None and canopy_at - spawned_at <= 4.5, f"③ the canopy open by ~3 s (≤ 4.5 s over the wire) — {None if canopy_at is None else round(canopy_at - spawned_at, 2)} s")
    pp = position(pilot)
    time.sleep(6)
    pp2 = position(pilot)
    c.ok(not _descent(pilot) and pp2 is not None and pp is not None and flat(pp, pp2) < 2.0,
         f"④ the pilot is down and stays down — at {pp}, moved {flat(pp, pp2) if pp and pp2 else None} m in 6 s")


def run_drone_shelter(c, timeout):
    """⭐ ua-drone-shelter (Urban Armour §8; Air Ops §9i Dive; Exposure Cycle §8 ShelterFromAir) — two FPV drones on two M1s: the tank by
    the garage deck hears the rotors and backs under it, so its drone's dive meets the slab; the tank in the open is hit from above."""
    ids = ids_by_name()
    deck_tank, open_tank = ids.get("Tank by the deck"), ids.get("Tank in the open")
    drone_a, drone_b = ids.get("Drone A"), ids.get("Drone B")
    if not c.ok(None not in (deck_tank, open_tank, drone_a, drone_b), "the two tanks and the two drones are loaded"):
        return
    h0 = {t: health(t) for t in (deck_tank, open_tank)}
    call("POST", "/sim/play", {})
    timeline, next_sample, under_at, spent_a = [], 0.0, None, None
    end = time.time() + timeout + 60
    while time.time() < end:
        t = sim_time() or 0.0
        pa = position(deck_tank)
        if under_at is None and pa and 240 < pa[0] < 290 and 40 < pa[1] < 80:
            under_at = t
        if spent_a is None and (health(drone_a) or 0) <= 0:
            spent_a = (t, position(deck_tank))
        if time.time() >= next_sample:
            next_sample = time.time() + 1.0
            beh = [((data(call("GET", f"/entities/{e}/state")) or {}).get("behavior") or {}).get("name") for e in (deck_tank, open_tank)]
            timeline.append((round(t, 1), *[tuple(round(v) for v in position(e)) if position(e) else None for e in (drone_a, deck_tank, drone_b, open_tank)],
                             health(deck_tank), health(open_tank), *beh))
        if (health(drone_a) or 0) <= 0 and (health(drone_b) or 0) <= 0:
            time.sleep(2)
            break
        time.sleep(0.25)
    for row in timeline[:150]:
        print("      drone @", row)
    pa = position(deck_tank)
    c.ok(under_at is not None, f"① the tank by the deck backs under it (x 240–290, y 40–80) — at {under_at}, now {pa}")
    c.ok((health(drone_a) or 0) <= 0, f"① its drone is spent — health {health(drone_a)}")
    sp = spent_a[1] if spent_a else None
    under = sp is not None and 240 < sp[0] < 290 and 40 < sp[1] < 80
    c.ok(under and (health(deck_tank) or 0) >= (h0[deck_tank] or 0) * 0.9,
         f"① ★ the deck took the warhead — the tank under it when its drone was spent ({sp}), {h0[deck_tank]} → {health(deck_tank)}")
    c.ok((health(drone_b) or 0) <= 0, f"② the drone on the tank in the open is spent — health {health(drone_b)}")
    c.ok((health(open_tank) or 0) < (h0[open_tank] or 0), f"② ★ the tank in the open is hit from above — {h0[open_tank]} → {health(open_tank)}")


# ── CIV-0 (CE-2233) — the safety gate: a soldier never engages a NEUTRAL ────────────────────────────────────────────────
#   docs/DESIGN_Civilians_And_Crowd_Control.md §5c (the demo table) / §5b (as-built). Cast: a rifleman (Friend); an unarmed
#   Neutral civilian; an ARMED Neutral nearest of all (were the gate missing, he is the top pick: armed, near, in sight); a
#   Hostile farther off. The armed bodies have no brain (TKB 2002 has no default behaviour), so only the rifleman fires.
#   ⭐ The rifleman's mission: CombatPosture (the fight) → Sentry (MinDanger 0.5 — the neutrals' danger is 0, so it keeps watching)
#   → CombatPosture again once something dangerous appears (the side flip). ⛔ Not a Sop component: a scenario with one shows an
#   empty Scenario perspective (CE-2243, measured on ua-universal-soldier too).
#   PASS = ① he SEES both neutrals (memory) — the gate, not blindness, spares them · ② the Hostile dies · ③ neither neutral
#   loses health, sampled for the whole run · ④ the neutrals score 0 in the threat ranking · ⑤ with only neutrals left he
#   fires no round for 8 s · ⑥ the armed neutral's side flips to Hostile ⇒ he is engaged (the gate reads the side LIVE).

def _memory_ids(nid):
    return {x.get("networkId") for x in (data(call("GET", f"/entities/{nid}/memory")) or {}).get("slots") or []}


def run_civ_neutral_gate(c, timeout):
    ids = ids_by_name()
    rifle, civ, armed, hostile = (ids.get(n) for n in ("Rifleman", "Civilian", "Armed Neutral", "Hostile"))
    if not c.ok(None not in (rifle, civ, armed, hostile), "the rifleman, the civilian, the armed neutral and the hostile are loaded"):
        return
    # ⓪ PRECONDITION — the cast stands where the scenario put it. ⚠ Measured 2026-10-17 (CE-2242): on some fresh loads every entity
    #   lands ~900 km out ((901880, 5038392) — a projected coordinate, outside the perception grid), nobody sees anybody, and every
    #   later line would fail for a reason that is not CIV-0's. Report THAT, and stop.
    p0 = position(rifle)
    if not c.ok(p0 is not None and 0 <= p0[0] <= 1000 and 0 <= p0[1] <= 1000,
                f"⓪ the cast is on the terrain where the scenario put it (rifleman at {p0}; ⛔ off-terrain = CE-2242, a load race, not CIV-0)"):
        return
    full = {n: health(n) for n in (civ, armed)}
    low = dict(full)
    zero_scores, ranked_seen = True, False

    def sample():
        nonlocal zero_scores, ranked_seen
        for n in (civ, armed):
            h = health(n)
            if h is not None and low[n] is not None:
                low[n] = min(low[n], h)
        d = decision(utility(rifle), RANKING)
        for r in (d or {}).get("ranked") or []:
            if r.get("networkId") in (civ, armed) or r.get("name") in ("Civilian", "Armed Neutral"):
                ranked_seen = True
                zero_scores = zero_scores and (r.get("score") or 0) == 0

    call("POST", "/trace/observe", {"networkId": rifle, "on": True})
    call("POST", "/sim/play", {})

    seen = wait_for(lambda: (sample(), {civ, armed} <= _memory_ids(rifle))[1], timeout)
    c.ok(seen is not None, "① the rifleman sees (remembers) both neutrals")
    # ② OUT OF THE FIGHT, not "health 0": an incapacitated man bleeds out and is not shot again (Injury.IsInTheFight, I4) —
    #   measured live 2026-10-17: the Hostile sat at 1.1 HP, incapacitated, the rifleman rightly holding fire
    th = ((data(call("GET", f"/entities/{hostile}")) or {}).get("Components") or {}).get("InjuryThresholds") or {}
    down_below = (th.get("IncapacitatedBelow") or 0.25) * 100
    down = wait_for(lambda: (sample(), (h := health(hostile)) is not None and h < down_below)[1], timeout * 2)
    c.ok(down is not None, f"② the Hostile is put out of the fight (health {health(hostile)} < {down_below:.0f})")
    c.ok(low[civ] == full[civ], f"③ the unarmed civilian is never hit: {full[civ]} → lowest {low[civ]}")
    c.ok(low[armed] == full[armed], f"③ the ARMED neutral is never hit: {full[armed]} → lowest {low[armed]}")
    c.ok(ranked_seen and zero_scores, f"④ the neutrals are ranked and score 0 (ranked: {ranked_seen})")

    # ⑤ measured live 2026-10-17: the burst at the falling Hostile runs on for a few rounds after he drops below the threshold — so
    #   SETTLE first (ammo unchanged for 3 s), then nothing for 8 s; and his weapon's target is never a neutral, sampled all along.
    targets = set()

    def weapon_target():
        t = ((data(call("GET", f"/entities/{rifle}/weapons")) or {}).get("status") or {}).get("target")
        if t is not None:
            targets.add(t)

    last = {"a": ammo(rifle)[0], "t": time.time()}

    def settled():
        sample(); weapon_target()
        a = ammo(rifle)[0]
        if a != last["a"]:
            last["a"], last["t"] = a, time.time()
        return time.time() - last["t"] >= 3

    c.ok(wait_for(settled, 30) is not None, f"⑤ his fire stops once the Hostile is down (rounds now {ammo(rifle)[0]})")
    a0, _ = ammo(rifle)
    t_end = time.time() + 8
    while time.time() < t_end:
        sample(); weapon_target()
        time.sleep(0.5)
    a1, _ = ammo(rifle)
    c.ok(a0 is not None and a0 == a1, f"⑤ with only neutrals left he fires nothing for 8 s (rounds {a0} → {a1})")
    c.ok(not (targets & {civ, armed}), f"⑤ his weapon never takes a neutral as its target (targets seen: {sorted(targets)})")
    c.ok(low[civ] == full[civ] and low[armed] == full[armed], "⑤ …and both neutrals are still unhurt")

    # ⑥ through the ONE write path that reaches the owner and the wire (EntityWriteRouter, CE-3221) — ⛔ not /component: that is a
    #   StructEdit of this node's world, never sent (measured live 2026-10-17: "ok", and nobody else saw the flip)
    r = call("POST", f"/entities/{armed}/attribute", {"patchJson": {"Affiliation": "FORCE_OPPOSING"}})
    route = ((data(r) or {}).get("write") or {}).get("route") if "http" not in r else None
    if not c.ok(route in ("direct", "requested"), f"⑥ the armed neutral's side flips to Hostile (route {route}; {r if 'http' in r else ''})"):
        return
    hit = wait_for(lambda: (h := health(armed)) is not None and full[armed] is not None and h < full[armed], timeout)
    c.ok(hit is not None, f"⑥ ★ once Hostile he is engaged at once — health {full[armed]} → {health(armed)}")
    c.ok(health(civ) == full[civ], f"⑥ …and the unarmed civilian is still unhurt ({health(civ)})")


# ── CE-2235 CIV-1 — the civilian's routine, alarm and casualty (Civilians §5c, §5d) ───────────────────────────────────

_CIV_MODES = {0: "None", 1: "Routine", 2: "Alarmed", 3: "Panic", 4: "Shelter", 5: "Inside", 6: "Cower", 7: "Casualty",
              8: "Protest", 9: "Riot", 10: "Dispersing", 11: "ArmedHostile", 12: "Gather"}


def civ_mode(nid):
    """The CivilianHsm's mode as the brain (CGF) holds it — CivilianState is brain-local, never replicated."""
    call("POST", "/perspective", {"name": "CGF"})
    try:
        m = (((data(call("GET", f"/entities/{nid}")) or {}).get("Components") or {}).get("CivilianState") or {}).get("Mode")
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    return _CIV_MODES.get(m, m) if isinstance(m, int) else m


def run_civ_routine_alarm(c, timeout):
    ids = ids_by_name()
    rifle, hostile = ids.get("Rifleman"), ids.get("Hostile")
    near = [ids.get(f"Near Civilian {i}") for i in (1, 2)]
    far = [ids.get(f"Far Civilian {i}") for i in (1, 2, 3, 4)]
    civs = near + far
    if not c.ok(None not in [rifle, hostile] + civs, "the rifleman, the hostile, 2 near and 4 far civilians are loaded"):
        return
    p0 = position(rifle)
    if not c.ok(p0 is not None and 0 <= p0[0] <= 1000 and 0 <= p0[1] <= 1000,
                f"⓪ the cast is on the terrain where the scenario put it (rifleman at {p0}; off-terrain = CE-2242)"):
        return
    start = {n: position(n) for n in civs}
    far_alarmed = set()
    call("POST", "/sim/play", {})

    routine = wait_for(lambda: all(civ_mode(n) == "Routine" for n in civs), 30)
    c.ok(routine is not None, f"① all 6 civilians run CivilianHsm from their type's default and start in Routine "
                              f"({[civ_mode(n) for n in civs]})")

    # ② the first shot, then: every civilian within hearing alarmed within 2 s; none beyond it, sampled all run
    a0 = (ammo(rifle) or (None,))[0]
    shot = wait_for(lambda: (a := (ammo(rifle) or (None,))[0]) is not None and a0 is not None and a < a0, timeout)
    if not c.ok(shot is not None, "② the rifleman opens fire on the Hostile"):
        return
    t_shot = sim_time() or 0.0

    def far_sample():
        for n in far:
            if civ_mode(n) not in ("Routine", None):
                far_alarmed.add(n)

    # ⭐ CE-2236 — more shots take him past PanicAt (Panic, then Cower: test-town's buildings are solid); a 0.2 s poll can miss
    #   the alarm on the way, so "alarmed or beyond" — the in-process twin samples every frame and asserts Alarmed itself
    frightened = ("Alarmed", "Panic", "Shelter", "Cower")
    alarmed = wait_for(lambda: (far_sample(), all(civ_mode(n) in frightened for n in near))[1], 20, every=0.2)
    t_alarm = sim_time() or 0.0
    c.ok(alarmed is not None and t_alarm - t_shot <= 2.5,
         f"② both near civilians (40 m from the shooter) are Alarmed or beyond {t_alarm - t_shot:.1f} s of sim time after the "
         f"first shot ({[civ_mode(n) for n in near]})")
    th = (comps(hostile).get("InjuryThresholds") or {})
    down_below = (th.get("IncapacitatedBelow") or 0.3) * 100
    wait_for(lambda: (far_sample(), (h := health(hostile)) is not None and h < down_below)[1], timeout * 2)

    # ③ quiet again: each alarmed man calms — back to his routine, or ⭐ CIV-3 (CE-2237, §5f) straight on to GATHER at the man who
    #   fell (Routine → Gather on an incident within AttractMetres). 📐 sweep 2026-10-22: live both were Gather for good; the
    #   in-process twin, sampling every frame, still sees Routine (inferred: a frame between). Afraid (Alarmed and beyond) is the failure.
    calm = wait_for(lambda: (far_sample(), all(civ_mode(n) in ("Routine", "Gather") for n in near))[1], 90)
    c.ok(calm is not None, f"③ once the firing stops, both near civilians calm — Routine, or gathering at the man who fell (CIV-3) "
                           f"({[civ_mode(n) for n in near]})")
    far_sample()
    c.ok(not far_alarmed, f"② …and none of the 4 far civilians (150+ m, beyond hearing) was ever alarmed ({sorted(far_alarmed)})")
    moved = [n for n in far if start[n] and (p := position(n)) and flat(p, start[n]) > 1.0]
    c.ok(len(moved) >= 3, f"① the far civilians live their routine: {len(moved)} of 4 walked from where they started")

    # ④ wounded ⇒ a casualty: he lies where he is and stays there
    victim = near[0]
    call("POST", "/perspective", {"name": "CGF"})   # ⭐ Health is Brain group: CGF owns it, so the patch is the owner's write
    patched = set_health(victim, 40)
    call("POST", "/perspective", {"name": "Scenario"})
    c.ok(patched, "④ the near civilian's health is set to 40 on its owner")
    cas = wait_for(lambda: civ_mode(victim) == "Casualty", 10, every=0.2)
    c.ok(cas is not None, f"④ wounded (40 %), he is a Casualty ({civ_mode(victim)})")
    pa = position(victim)
    time.sleep(4)
    pb = position(victim)
    c.ok(pa and pb and flat(pa, pb) < 0.5 and civ_mode(victim) == "Casualty",
         f"④ …and stays one, where he fell (moved {flat(pa, pb) if pa and pb else None} m in 4 s)")



# ── CE-2236 CIV-2 — panic, shelter, inside, cower (Civilians §5c, §5e) ─────────────────────────────────────────────────

def comps_on(perspective, nid):
    """The entity's components as `perspective`'s node holds them; None when that node does not have it."""
    call("POST", "/perspective", {"name": perspective})
    try:
        d = data(call("GET", f"/entities/{nid}"))
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    return (d or {}).get("Components") if d else None


def cowering_on(perspective, nid):
    return ((comps_on(perspective, nid) or {}).get("EffectState") or {}).get("Cowering") == 1


def _footprint_a():
    # bt-range House A: house-2f (10 x 8 m) placed with its south-west corner at (100, 100), rotation 0
    return (100.0, 100.0, 110.0, 108.0)


def _in_a(p):
    x0, y0, x1, y1 = _footprint_a()
    return p is not None and x0 < p[0] < x1 and y0 < p[1] < y1


def run_civ_shelter(c, timeout):
    ids = ids_by_name()
    rifle, hostile = ids.get("Rifleman"), ids.get("Hostile")
    civs = [ids.get(f"Civilian {i}") for i in (1, 2, 3, 4)]
    if not c.ok(None not in [rifle, hostile] + civs, "the rifleman, the hostile and 4 civilians are loaded"):
        return
    front, back = "bt-range/House A/front", "bt-range/House A/back"
    d = wait_for(lambda: (x := doors_by_key()) and all((x.get(k) or {}).get("runtimeId") for k in (front, back)) and x, timeout)
    if not c.ok(d is not None, "House A's doors exist as entities"):
        return
    c.ok(d[front]["state"] == "Closed" and d[back]["state"] == "Closed",
         f"⓪ House A's doors are Closed, not locked (TerrainObjects) — front {d[front]['state']}, back {d[back]['state']}")
    bdoors = {k: v["state"] for k, v in d.items() if k.startswith("bt-range/House B/") and not k.endswith("/hall")}
    c.ok(bdoors and all(v == "Locked" for v in bdoors.values()), f"⓪ House B's outer doors are Locked — {bdoors}")
    call("POST", "/sim/play", {})
    routine = wait_for(lambda: all(civ_mode(n) == "Routine" for n in civs), 30)
    c.ok(routine is not None, f"① the 4 civilians start in Routine ({[civ_mode(n) for n in civs]})")

    seen, inside_at, gone_at, in_b = {n: set() for n in civs}, {}, {}, set()
    def watch():
        now = sim_time() or 0.0
        for n in civs:
            if n in gone_at:
                continue
            m = civ_mode(n)
            if m is None and comps_on("CGF", n) is None:
                gone_at[n] = now
                continue
            seen[n].add(m)
            p = position(n)
            if m == "Inside" and n not in inside_at:
                inside_at[n] = (now, p)
            if p is not None and 160 <= p[0] <= 168 and 100 <= p[1] <= 110:
                in_b.add(n)
        return len(gone_at) == len(civs)

    a0 = (ammo(rifle) or (None,))[0]
    shot = wait_for(lambda: (watch(), (a := (ammo(rifle) or (None,))[0]) is not None and a0 is not None and a < a0)[1], timeout, every=0.25)
    if not c.ok(shot is not None, "① the rifleman opens fire, 28 m from the civilians"):
        return
    done = wait_for(watch, timeout * 3, every=0.25)
    # ⭐ Panic can last a few frames (the shelter query answers fast) — a 0.25 s poll misses it; Shelter, Inside and Cower are reached
    #   ONLY through Panic in the machine (§5e), so any of them proves it. The in-process twin samples every frame.
    panicked = ("Panic", "Shelter", "Inside", "Cower")
    c.ok(all(seen[n] & set(panicked) for n in civs), f"① each civilian panics ({ {n: sorted(x for x in seen[n] if x) for n in civs} })")
    c.ok(all(n in inside_at for n in civs), f"② each reaches Inside ({sorted(inside_at)})")
    c.ok(all(_in_a(inside_at[n][1]) for n in inside_at),
         f"② …standing inside House A's footprint ({ {n: inside_at[n][1] for n in inside_at} })")
    st = {k: (doors_by_key().get(k) or {}).get("state") for k in (front, back)}
    c.ok("Open" in st.values(), f"② a door of House A was opened to let them in — {st}")
    c.ok(not in_b, f"③ none enters the locked House B ({sorted(in_b)})")
    c.ok(done is not None, f"④ each deletes himself after hiding ({len(gone_at)} of 4 gone)")
    waits = {n: round(gone_at[n] - inside_at[n][0], 1) for n in civs if n in gone_at and n in inside_at}
    c.ok(waits and all(18.0 <= w <= 23.0 for w in waits.values()), f"④ …about 20 s (the scenario's hide time) after going in — {waits}")
    for host in ("SimHost", "IG"):
        left = [n for n in civs if comps_on(host, n) is not None]
        c.ok(not left, f"④ …and is gone on {host} too ({left})")


def run_civ_cower(c, timeout):
    ids = ids_by_name()
    rifle, hostile = ids.get("Rifleman"), ids.get("Hostile")
    civs = [ids.get(f"Civilian {i}") for i in (1, 2, 3)]
    if not c.ok(None not in [rifle, hostile] + civs, "the rifleman, the hostile and 3 civilians are loaded"):
        return
    call("POST", "/sim/play", {})
    routine = wait_for(lambda: all(civ_mode(n) == "Routine" for n in civs), 30)
    c.ok(routine is not None, f"① the 3 civilians start in Routine ({[civ_mode(n) for n in civs]})")
    cower = wait_for(lambda: all(civ_mode(n) == "Cower" for n in civs), timeout, every=0.25)
    if not c.ok(cower is not None, f"① under fire, with no building to enter (test-town's are solid), each cowers ({[civ_mode(n) for n in civs]})"):
        return
    for host in ("CGF", "SimHost", "IG"):
        c.ok(all(cowering_on(host, n) for n in civs), f"① EffectState.Cowering is set on {host} ({[cowering_on(host, n) for n in civs]})")
    pa = {n: position(n) for n in civs}
    time.sleep(1.5)
    still = {n: round(flat(pa[n], position(n)), 2) for n in civs if pa[n] and position(n)}
    c.ok(all(civ_mode(n) == "Cower" for n in civs) and all(v < 0.3 for v in still.values()),
         f"① …and stands still where he is (moved {still} m in 1.5 s)")

    th = (comps(hostile).get("InjuryThresholds") or {})
    down_below = (th.get("IncapacitatedBelow") or 0.3) * 100
    wait_for(lambda: (h := health(hostile)) is not None and h < down_below, timeout * 2)
    over = wait_for(lambda: all(civ_mode(n) not in ("Cower", "Panic") for n in civs), 60, every=0.25)
    c.ok(over is not None, f"② the firing over, each leaves Cower as his fear fades ({[civ_mode(n) for n in civs]})")
    for host in ("CGF", "SimHost", "IG"):
        c.ok(not any(cowering_on(host, n) for n in civs), f"② …and Cowering is cleared on {host}")


# ── CE-2237 CIV-3 — crowds: onlookers gather, fear spreads (Civilians §5c, §5f) ────────────────────────────────────────

def civ_mind(nid):
    """The civilian's CivilianState as the brain (CGF) holds it — brain-local, never replicated."""
    return (comps_on("CGF", nid) or {}).get("CivilianState") or {}


def run_civ_gathering(c, timeout):
    ids = ids_by_name()
    casualty = ids.get("Casualty")
    onlookers = [ids.get(f"Onlooker {i}") for i in range(1, 13)]
    if not c.ok(None not in [casualty] + onlookers, "the casualty and 12 onlookers are loaded"):
        return
    call("POST", "/sim/play", {})
    cas = wait_for(lambda: civ_mode(casualty) == "Casualty", 30)
    c.ok(cas is not None, f"⓪ the hurt civilian (health 40) is a Casualty — the incident ({civ_mode(casualty)})")
    focus = position(casualty)
    t0 = sim_time() or 0.0
    gathered = set()

    def mean_dist():
        ds = [flat(p, focus) for n in onlookers if (p := position(n))]
        return sum(ds) / len(ds) if ds else 1e9

    def watch():
        for n in onlookers:
            if civ_mode(n) == "Gather":
                gathered.add(n)
        return mean_dist() < 15.0
    close = wait_for(watch, 90, every=0.5)
    t_close = (sim_time() or 0.0) - t0
    c.ok(len(gathered) == len(onlookers), f"① every onlooker (30–60 m out, calm) is drawn to it — Gather ({len(gathered)} of 12)")
    c.ok(close is not None and t_close <= 60.0, f"② their mean distance to the casualty falls below 15 m within 60 s "
                                                 f"({mean_dist():.1f} m at {t_close:.0f} s)")
    # ⭐ the mean falls under 15 m while the 60 m ones are still walking (measured: 25 s) — let the last ones arrive first
    arrived = wait_for(lambda: all((p := position(n)) and flat(p, focus) <= 11.0 for n in onlookers), 60, every=0.5)
    c.ok(arrived is not None, "④ every onlooker arrives (within 11 m of him)")
    minds = {n: civ_mind(n) for n in onlookers}
    crowd_ids = [m.get("CrowdId") or 0 for m in minds.values()]
    best = max(set(crowd_ids), key=crowd_ids.count)
    c.ok(best != 0 and crowd_ids.count(best) >= 8, f"③ one crowd holds at least 8 of them (crowd ids {crowd_ids})")
    ring = [round(flat(position(n), focus), 1) for n in onlookers if position(n)]
    c.ok(all(2.5 <= r <= 11.0 for r in ring), f"④ …standing round it on a ring, not on top of him ({sorted(ring)} m)")


def run_civ_contagion(c, timeout):
    ids = ids_by_name()
    rifle, hostile = ids.get("Rifleman"), ids.get("Hostile")
    civs = [ids.get(f"Civilian {i}") for i in range(1, 16)]
    if not c.ok(None not in [rifle, hostile] + civs, "the rifleman, the hostile and 15 civilians are loaded"):
        return
    frightened = ("Panic", "Shelter", "Inside", "Cower")
    heard, panicked, modes0, crowd = set(), set(), {}, {}
    last = [-1.0]

    def watch():
        # ⭐ CE-2246 — ONE read per civilian per sweep (CivilianState carries the mode), and a trace each sim-second shaped like the
        #   twin's, so a live miss can be compared hop by hop with the in-process run
        row = []
        call("POST", "/perspective", {"name": "CGF"})   # ⭐ one switch per sweep, not three calls per civilian: measured ~4 s a sweep
        try:
            seen = {n: ((data(call("GET", f"/entities/{n}")) or {}).get("Components") or {}) for n in civs}
        finally:
            call("POST", "/perspective", {"name": "Scenario"})
        for n in civs:
            cs = seen[n]
            m = cs.get("CivilianState") or {}
            if (m.get("HeardAt") or 0) > 0:
                heard.add(n)
            mode = m.get("Mode")
            mode = _CIV_MODES.get(mode, mode) if isinstance(mode, int) else mode
            modes0.setdefault(n, mode)
            crowd[n] = m.get("CrowdId") or 0
            if mode in frightened:
                panicked.add(n)
            x = ((cs.get("SimTransform") or {}).get("Position") or [None])[0] if isinstance((cs.get("SimTransform") or {}).get("Position"), list) \
                else ((cs.get("SimTransform") or {}).get("Position") or {}).get("X")
            row.append(f"{civs.index(n) + 1}:{mode}{'h' if n in heard else ''}{m.get('Fear') or 0:.2f}/c{m.get('CrowdId') or 0}/x{x if x is None else int(x)}")
        t = sim_time() or 0.0
        if t - last[0] >= 1.0:
            last[0] = t
            print(f"    t={t:6.1f} " + " ".join(row))
        return False
    a0 = (ammo(rifle) or (None,))[0]   # ⭐ before play: the fight is over in seconds, faster than one sweep of 15 civilians
    call("POST", "/sim/play", {})
    one = wait_for(lambda: (watch(), len(set(crowd.values()) - {0}) == 1 and all(crowd.values()))[1], 20, every=0.2)
    c.ok(one is not None, f"⓪ they stand as ONE crowd along the street ({[crowd.get(n) for n in civs]})")
    shot = wait_for(lambda: (watch(), (a := (ammo(rifle) or (None,))[0]) is not None and a0 is not None and a < a0)[1], timeout, every=0.25)
    if not c.ok(shot is not None, "① the rifleman opens fire at the west end"):
        return
    wait_for(watch, 40, every=0.25)
    deaf = [n for n in civs if n not in heard]
    c.ok(len(heard) >= 5, f"① the near ones hear it ({len(heard)} of 15 heard: {sorted(heard)})")
    c.ok(len(deaf) >= 4, f"⓪ the far ones (100+ m from the fire) hear nothing ({len(deaf)} never heard: {deaf})")
    caught = [n for n in deaf if n in panicked]
    c.ok(len(caught) >= 3, f"② fear reaches the ones who heard nothing: ≥ 3 of them panic, from the crowd ({caught}; "
                           f"modes now { {n: civ_mode(n) for n in deaf} })")


# ── CE-2240 CIV-3 — the protest and its gestures (Civilians §5c, §5g) ─────────────────────────────────────────────────

_GESTURES = {0: "None", 1: "HandsUp", 2: "FistRaised", 3: "HoldSign", 4: "Waving", 5: "Throwing", 6: "Warning"}


def gesture_on(perspective, nid):
    g = ((comps_on(perspective, nid) or {}).get("Gesture") or {}).get("Kind")
    return _GESTURES.get(g, g) if isinstance(g, int) else (g or "None")


def run_civ_protest(c, timeout):
    ids = ids_by_name()
    civs = [ids.get(f"Protester {i}") for i in range(1, 13)]
    if not c.ok(None not in civs, "12 protesters are loaded"):
        return
    call("POST", "/sim/play", {})
    t0 = sim_time() or 0.0
    seen = {h: {"FistRaised": set(), "HoldSign": set()} for h in ("CGF", "SimHost", "IG")}
    bad = set()

    def watch():
        for n in civs:
            m = civ_mode(n)
            if m == "Riot":
                bad.add((n, "Riot"))
            side = (comps(n).get("EntityInfo") or {}).get("ForceId")
            if side not in (None, "Neutral", 0):
                bad.add((n, f"side {side}"))
            for h in seen:
                g = gesture_on(h, n)
                if g in seen[h]:
                    seen[h][g].add(n)
        return False
    protest = wait_for(lambda: (watch(), all(civ_mode(n) == "Protest" for n in civs))[1], 30, every=0.5)
    c.ok(protest is not None, f"① the crowd reaches Protest ({[civ_mode(n) for n in civs]})")
    wait_for(lambda: (watch(), (sim_time() or 0.0) - t0 >= 120.0)[1], 400, every=0.5)
    crowd = [civ_mind(n).get("CrowdId") or 0 for n in civs]
    best = max(set(crowd) - {0}, key=crowd.count, default=0)
    c.ok(best != 0 and crowd.count(best) >= 10, f"① …standing as one crowd round the point ({crowd})")
    for h in seen:
        c.ok(len(seen[h]["FistRaised"]) >= 3 and len(seen[h]["HoldSign"]) >= 3,
             f"② on {h} members show FistRaised ({len(seen[h]['FistRaised'])}) and HoldSign ({len(seen[h]['HoldSign'])}) — ≥ 3 of each")
    c.ok(not bad, f"③ in 120 s nobody turns Hostile and nobody riots ({sorted(bad)})")


# ── CE-2238 CIV-4 part 1 — the riot, the blow, the side flip (Civilians §5c, §5i) ──────────────────────────────────────────

def run_civ_melee(c, timeout):
    """test-town: an instigator protester 6 m from a policeman. ⓪ he riots · ① his blow knocks the policeman down
    (EffectState.KnockedDownUntil ahead of sim time) on CGF, SimHost and IG · ② the rioter is Hostile on CGF, SimHost and IG."""
    ids = ids_by_name()
    cop, rioter = ids.get("Policeman"), ids.get("Rioter")
    if not c.ok(None not in (cop, rioter), "the policeman and the rioter are loaded"):
        return
    hosts = ("CGF", "SimHost", "IG")
    down, hostile = set(), set()

    def watch():
        t = sim_time() or 0.0
        for h in hosts:
            cc = comps_on(h, cop) or {}
            if ((cc.get("EffectState") or {}).get("KnockedDownUntil") or 0) > t:
                down.add(h)
            side = ((comps_on(h, rioter) or {}).get("EntityInfo") or {}).get("ForceId")
            if side in ("Hostile", 2):
                hostile.add(h)
        return len(down) == len(hosts) and len(hostile) == len(hosts)
    call("POST", "/sim/play", {})
    riot = wait_for(lambda: civ_mode(rioter) == "Riot", 30, every=0.25)
    c.ok(riot is not None, f"⓪ the instigator riots ({civ_mode(rioter)})")
    wait_for(watch, timeout + 30, every=0.25)
    c.ok(len(down) == len(hosts), f"① his blow knocks the policeman down on every node (seen down on {sorted(down)})")
    c.ok(len(hostile) == len(hosts), f"② the blow was a hostile act: the rioter is Hostile on every node ({sorted(hostile)})")


# ── AW-20 — a neutral in the line or the spread holds the shot (Civilians §5c, §5h) ───────────────────────────────────────

def run_armed_shield(c, timeout):
    """test-town: a rifleman 60 m from an insurgent, four civilians standing in the line between them and two beside him. ① the
    rifleman is seen holding with a civilian beside the line, inside his spread ('hold: neutral on line'); ② he fires once it is clear and the
    insurgent goes down (out of the fight); ③ no civilian is hit (sampled from play). The twin (PostureScenarioTests.AW20_ArmedShield_…) checks every frame."""
    ids = ids_by_name()
    rifle, ins = ids.get("Rifleman"), ids.get("Insurgent")
    civs = [ids.get(f"Civilian {i}") for i in range(1, 7)]
    if not c.ok(None not in [rifle, ins] + civs, "the rifleman, the insurgent and 6 civilians are loaded"):
        return
    a0 = (ammo(rifle) or (None,))[0]   # ⭐ before play
    held, hurt = [0], set()

    def watch():
        reason = ((data(call("GET", f"/entities/{rifle}/weapons")) or {}).get("status") or {}).get("reason") or ""
        if reason == "hold: neutral on line":
            held[0] += 1
        for n in civs:
            h = health(n)
            if h is not None and h < 100:
                hurt.add(n)
        return False
    call("POST", "/sim/play", {})
    fired = wait_for(lambda: (watch(), (a := (ammo(rifle) or (None,))[0]) is not None and a0 is not None and a < a0)[1],
                     timeout + 60, every=0.25)
    c.ok(held[0] > 0, f"① the rifleman holds his fire while a civilian stands in the line ({held[0]} samples 'hold: neutral on line')")
    c.ok(fired is not None, "② …and fires once the line is clear")
    th = (comps(ins).get("InjuryThresholds") or {})
    down_below = (th.get("IncapacitatedBelow") or 0.3) * 100   # ⭐ OUT OF THE FIGHT, not health 0 (Injury, I4): he is not shot again
    down = wait_for(lambda: (watch(), (h := health(ins)) is not None and h < down_below)[1], 120, every=0.5)
    c.ok(down is not None, f"② the insurgent is engaged and goes down — out of the fight (health {health(ins)}, below {down_below:.0f})")
    wait_for(watch, 5, every=0.5)
    c.ok(not hurt, f"③ no civilian is hit ({sorted(hurt)}; health {[health(n) for n in civs]})")


# ── CE-2238 part 2 CIV-4 — the stone (Civilians §5c, §5i.2) ─────────────────────────────────────────────────────────────

def run_civ_riot_stone(c, timeout):
    """test-town: an instigator and two protesters inside a stone source, a policeman 12 m off. ⓪ the instigator riots · ① he fills
    his hands at the source (hands' ammo above 0) · ② he throws (the ammo drops) · ③ the side follows the ACT: on CGF no protester is
    Hostile without his act stamped, and every thrower is Hostile on CGF, SimHost and IG within 3 s of his act (sampled). The twin
    (PostureScenarioTests.CE2238_RiotStone_…) checks every frame."""
    ids = ids_by_name()
    cop = ids.get("Policeman")
    names = ("Instigator", "Protester 1", "Protester 2")
    civs = {n: ids.get(n) for n in names}
    if not c.ok(cop is not None and None not in civs.values(), "the policeman, the instigator and two protesters are loaded"):
        return
    hosts = ("CGF", "SimHost", "IG")
    inst = civs["Instigator"]
    peak, thrown = [0], [False]
    acted, everywhere, bad = {}, {}, []

    def watch():
        t = sim_time() or 0.0
        a = (comps_on("CGF", inst) or {}).get("WeaponState", {}).get("Ammo") or 0
        if a > peak[0]: peak[0] = a
        if peak[0] > 0 and a < peak[0]: thrown[0] = True
        for n, nid in civs.items():
            cg = comps_on("CGF", nid) or {}
            act = ((cg.get("CivilianState") or {}).get("HostileActAt") or 0) > 0
            if act and n not in acted: acted[n] = t
            side = (cg.get("EntityInfo") or {}).get("ForceId")
            if side in ("Hostile", 2) and not act: bad.append(f"{n}@{t:.1f}")
            if n not in everywhere and all(((comps_on(h, nid) or {}).get("EntityInfo") or {}).get("ForceId") in ("Hostile", 2) for h in hosts):
                everywhere[n] = t
        return thrown[0] and "Instigator" in everywhere
    call("POST", "/sim/play", {})
    riot = wait_for(lambda: civ_mode(inst) == "Riot", 30, every=0.25)
    c.ok(riot is not None, f"⓪ the instigator riots ({civ_mode(inst)})")
    wait_for(watch, timeout + 40, every=0.25)
    c.ok(peak[0] > 0, f"① he fills his hands at the stone source (up to {peak[0]} in hand)")
    c.ok(thrown[0], "② he throws (his hands' ammo drops)")
    c.ok(not bad, f"③ no one Hostile without his act ({bad[:3]})")
    late = [n for n, t in acted.items() if n not in everywhere or everywhere[n] - t > 3.0]
    c.ok("Instigator" in acted and not late, f"③ every thrower Hostile on every node within 3 s (acted {acted}, everywhere {everywhere})")


# ── CE-2241 ① CV-13a — a covered hostile raises his hands, and the AI holds (Civilians §5c, §5h.3) ──────────────────────

def run_civ_surrender(c, timeout):
    """test-town: an instigator who strikes a policeman (Hostile, §5i) and a rifleman 40 m off. ① the rioter is Hostile and the rifleman
    covers him · ② his hands go up (Gesture HandsUp) on CGF, SimHost and IG · ③ the rifleman is seen holding ('hold: hands up') · ④ from
    the first raised-hands sample, for 10 s, the rifleman spends no round and the rioter is not hurt · ⑤ he stays Hostile. The twin
    (PostureScenarioTests.CE2241_CivSurrender_…) checks every frame."""
    ids = ids_by_name()
    cop, rioter, rifle = ids.get("Policeman"), ids.get("Rioter"), ids.get("Rifleman")
    if not c.ok(None not in (cop, rioter, rifle), "the policeman, the rioter and the rifleman are loaded"):
        return
    hosts = ("CGF", "SimHost", "IG")
    up, held = set(), [0]

    def side():
        return ((comps(rioter) or {}).get("EntityInfo") or {}).get("ForceId")

    def watch():
        for h in hosts:
            if gesture_on(h, rioter) == "HandsUp":
                up.add(h)
        reason = ((data(call("GET", f"/entities/{rifle}/weapons")) or {}).get("status") or {}).get("reason") or ""
        if reason == "hold: hands up":
            held[0] += 1
        return len(up) == len(hosts) and held[0] > 0
    call("POST", "/sim/play", {})
    hostile = wait_for(lambda: side() in ("Hostile", 2), 60, every=0.25)
    c.ok(hostile is not None, f"① the rioter struck: Hostile ({side()})")
    first = wait_for(lambda: gesture_on("CGF", rioter) == "HandsUp", timeout + 60, every=0.1)
    c.ok(first is not None, f"② covered, he raises his hands (mode {civ_mode(rioter)})")
    a0, h0 = (ammo(rifle) or (None,))[0], health(rioter)
    wait_for(watch, 10, every=0.25)
    c.ok(len(up) == len(hosts), f"② his hands are up on every node ({sorted(up)})")
    c.ok(held[0] > 0, f"③ the rifleman holds at him ({held[0]} samples 'hold: hands up')")
    t_end = (sim_time() or 0.0) + 10.0
    wait_for(lambda: (watch(), (sim_time() or 0.0) >= t_end)[1], 40, every=0.25)
    a1, h1 = (ammo(rifle) or (None,))[0], health(rioter)
    c.ok(a0 is not None and a1 == a0, f"④ no round for 10 s from the hands going up (ammo {a0} → {a1})")
    c.ok(h0 is not None and h1 is not None and h1 >= h0, f"④ …and he is not hurt (health {h0} → {h1})")
    c.ok(side() in ("Hostile", 2), f"⑤ his side stays Hostile ({side()})")


# ── CE-2239 A6 CV-8/CV-11 — the force level and the police line (Civilians §5c, §5j) ──────────────────────────────────────

def run_civ_police_line(c, timeout):
    """test-town: three policemen (shield, baton, launcher) placed with NO task — so on their default, HoldTheLine under a NonLethal ROE —
    12 m from an instigator and four protesters around a stone source. ⓪ the riot starts · ① the police use force, and only
    non-lethal force (their only rounds are stun, gas and baton rounds; a mount's ammo drops) · ② a rioter is stunned, irritated or
    knocked down · ③ the riot breaks up: after force, no civilian riots for 10 s · ④ nobody dies. (A rifleman in the line would COVER the
    rioters and they would surrender instead — §5h.3; his cap is railed, §5j.1.) The twin (PostureScenarioTests.CE2239_PoliceLine_…)
    checks every frame."""
    ids = ids_by_name()
    police = [ids.get(n) for n in ("Police shield", "Police baton", "Police launcher")]
    civs = [ids.get(n) for n in ("Instigator", "Protester 1", "Protester 2", "Protester 3", "Protester 4")]
    if not c.ok(None not in police + civs, "three policemen and five civilians are loaded"):
        return
    p0 = [(ammo(n) or (None,))[0] for n in police]
    effected, dead, forced = set(), set(), [None]

    def watch():
        t = sim_time() or 0.0
        for n in civs:
            fx = (comps(n) or {}).get("EffectState") or {}
            if any((fx.get(k) or 0) > t for k in ("StunnedUntil", "IrritatedUntil", "KnockedDownUntil")):
                effected.add(n)
        for n in civs + police:
            h = health(n)
            if h is not None and h <= 0:
                dead.add(n)
        if forced[0] is None and (effected or any(a0 is not None and (a := (ammo(n) or (None,))[0]) is not None and a < a0
                                                  for n, a0 in zip(police, p0))):
            forced[0] = t
        return False
    call("POST", "/sim/play", {})
    riot = wait_for(lambda: (watch(), any(civ_mode(n) == "Riot" for n in civs))[1], 40, every=0.25)
    c.ok(riot is not None, f"⓪ the riot starts ({[civ_mode(n) for n in civs]})")
    wait_for(lambda: (watch(), forced[0] is not None)[1], timeout + 60, every=0.25)
    c.ok(forced[0] is not None, f"① the police use force (at {forced[0]}; ammo {p0} → {[(ammo(n) or (None,))[0] for n in police]})")
    start = [None]

    def calm():
        watch()
        t = sim_time() or 0.0
        if any(civ_mode(n) == "Riot" for n in civs):
            start[0] = None
            return False
        start[0] = start[0] if start[0] is not None else t
        return t - start[0] >= 10.0
    calmed = wait_for(calm, timeout + 90, every=0.5)
    c.ok(effected, f"② a rioter is stunned, irritated or knocked down ({sorted(effected)})")
    c.ok(calmed is not None, f"③ the riot breaks up: no civilian riots for 10 s ({[civ_mode(n) for n in civs]})")
    c.ok(not dead, f"④ nobody dies ({sorted(dead)})")


# ── CE-2238 part 3 AW-8/AW-9 — a lying throwable picked up, the canister thrown back (Civilians §5c, §5i.3) ─────────────────

def clouds_on(perspective):
    """{networkId: (x, y, SourceNetworkId)} for every gas / smoke cloud `perspective`'s node holds."""
    call("POST", "/perspective", {"name": perspective})
    try:
        out = {}
        for r in data(call("GET", "/entities?component=AreaEffectCloud")) or []:
            nid = r.get("networkId")
            cs = comps(nid) or {}
            c, pos = cs.get("AreaEffectCloud") or {}, (cs.get("SimTransform") or {}).get("Position") or [0, 0, 0]
            out[nid] = (pos[0], pos[1], c.get("SourceNetworkId") or 0)
        return out
    finally:
        call("POST", "/perspective", {"name": "Scenario"})


def run_armed_gas_throwback(c, timeout):
    """test-town: a policeman puts ONE tear-gas canister down in front of a riot (FireAtPoint, mount 2), then holds the line; a
    HARDENED rioter (PanicAt 1.5 — the gas irritates him, it does not turn him) picks it up and throws it back. ⓪ a cloud sits on the
    canister · ① he holds it (HeldFuze burning) and that cloud lets go of its source · ③ he is irritated while he holds it · ② thrown, a
    new cloud rises nearer the policeman. The twin (PostureScenarioTests.CE2238_GasThrowback_…) checks every frame."""
    ids = ids_by_name()
    cop, rioter = ids.get("Policeman"), ids.get("Hardened rioter")
    if not c.ok(None not in (cop, rioter), "the policeman and the hardened rioter are loaded"):
        return
    cx, cy = ((comps(cop) or {}).get("SimTransform") or {}).get("Position", [90, 200, 0])[:2]
    first, sat, letgo, held, irritated, second = [None], [False], [False], [False], [False], [None]

    def watch():
        t = sim_time() or 0.0
        cl = clouds_on("CGF")
        for nid, (x, y, src) in cl.items():
            if first[0] is None:
                first[0] = (nid, math.hypot(x - cx, y - cy))
            if nid == first[0][0]:
                if src:
                    sat[0] = True
                elif sat[0] and held[0]:
                    letgo[0] = True
            elif held[0] and second[0] is None:
                second[0] = (nid, math.hypot(x - cx, y - cy))
        cs = comps_on("CGF", rioter) or {}
        if ((cs.get("HeldFuze") or {}).get("BurnSeconds") or 0) > 0:
            held[0] = True
            if ((cs.get("EffectState") or {}).get("IrritatedUntil") or 0) > t:
                irritated[0] = True
        return second[0] is not None
    call("POST", "/sim/play", {})
    wait_for(watch, timeout + 60, every=0.25)
    c.ok(sat[0], f"⓪ a cloud sits on the canister where it lies ({first[0]})")
    c.ok(held[0], "① the hardened rioter picks the canister up (it burns on in his hands)")
    c.ok(letgo[0], "① picked up, the first cloud lets go of its source and drifts")
    c.ok(irritated[0], "③ he is irritated while he holds it")
    c.ok(second[0] is not None and first[0] is not None and second[0][1] < first[0][1],
         f"② thrown, a new cloud rises nearer the policeman (first {first[0]}, second {second[0]})")


def laid_down_on(perspective, nid):
    """Has `perspective`'s node his weapons laid down — the replicated WeaponAmmo bit (B-10), or the brain's own mask."""
    cs = comps_on(perspective, nid) or {}
    return bool(((cs.get("WeaponAmmo") or {}).get("LaidDown") or 0) or ((cs.get("LaidDownWeapons") or {}).get("Mounts") or 0))


def run_armed_surrender(c, timeout):
    """test-town: an enemy soldier whose first task is Surrender {Seconds: 20} and whose second is to attack the rifleman 40 m off; the
    rifleman's first task is to attack him, his second CombatPosture. ① he lays his rifle down and raises his hands on CGF, SimHost and IG
    (AW-5) · ② while he is surrendered the rifleman spends no round and he is not hurt (AW-21: out of the fight) · ③ after 20 s he takes the
    rifle up and lowers his hands (the feint) · ④ armed again, the rifleman engages him. The twin
    (PostureScenarioTests.CE2241_ArmedSurrender_…) checks every frame."""
    ids = ids_by_name()
    enemy, rifle = ids.get("Enemy Soldier"), ids.get("Rifleman")
    if not c.ok(None not in (enemy, rifle), "the enemy soldier and the rifleman are loaded"):
        return
    hosts = ("CGF", "SimHost", "IG")
    seen = set()

    def surrendered(h):
        return gesture_on(h, enemy) == "HandsUp" and laid_down_on(h, enemy)

    def everywhere():
        for h in hosts:
            if surrendered(h):
                seen.add(h)
        return len(seen) == len(hosts)
    call("POST", "/sim/play", {})
    first = wait_for(lambda: surrendered("CGF"), timeout, every=0.1)
    c.ok(first is not None, "① he lays his rifle down and raises his hands")
    t_up = sim_time() or 0.0
    a0, h0 = (ammo(rifle) or (None,))[0], health(enemy)
    wait_for(everywhere, 10, every=0.25)
    c.ok(len(seen) == len(hosts), f"① …on every node ({sorted(seen)})")
    rose = wait_for(lambda: not surrendered("CGF"), 40, every=0.25)
    t_down = sim_time() or 0.0
    a1, h1 = (ammo(rifle) or (None,))[0], health(enemy)
    c.ok(a0 is not None and a1 == a0, f"② no round while he is surrendered (ammo {a0} → {a1})")
    c.ok(h0 is not None and h1 is not None and h1 >= h0, f"② …and he is not hurt (health {h0} → {h1})")
    c.ok(rose is not None and 15.0 <= t_down - t_up <= 25.0,
         f"③ after ~20 s he takes the rifle up, hands down ({t_down - t_up:.1f} s; gesture {gesture_on('CGF', enemy)}, laid {laid_down_on('CGF', enemy)})")
    fired = wait_for(lambda: (ammo(rifle) or (None,))[0] not in (None, a1), 30, every=0.25)
    c.ok(fired is not None, f"④ armed again, the rifleman engages him (ammo {a1} → {(ammo(rifle) or (None,))[0]})")


def run_armed_draw_aim(c, timeout):
    """test-town: a covert insurgent (a civilian, a pick-up pistol HIDDEN, seat 3) goes about his routine for 8 s, draws the pistol
    (StationWeapon Hands, 10 s), then attacks the rifleman (PersonAttack). ① drawn (seat 1), he is still Neutral and the rifleman spends
    no round while he is · ② he turns Hostile on CGF, SimHost and IG only once he aims (≥ 8 s after the draw, AW-16) · ③ the rifleman
    engages him. The twin (PostureScenarioTests.CE2238_DrawAim_…) checks every frame."""
    ids = ids_by_name()
    man, rifle, pistol = ids.get("Covert insurgent"), ids.get("Rifleman"), ids.get("Pistol")
    if not c.ok(None not in (man, rifle, pistol), "the insurgent, his pistol and the rifleman are loaded"):
        return
    hosts = ("CGF", "SimHost", "IG")

    def seat():
        return ((comps_on("CGF", pistol) or {}).get("IsEmbarkedTag") or {}).get("Seat")

    def side(h):
        return ((comps_on(h, man) or {}).get("EntityInfo") or {}).get("ForceId")
    c.ok(seat() == 3, f"the pistol starts hidden (seat {seat()})")
    a0 = (ammo(rifle) or (None,))[0]
    call("POST", "/sim/play", {})
    drawn = wait_for(lambda: seat() == 1, timeout + 15, every=0.1)
    t_draw = sim_time() or 0.0
    side_at_draw = side("CGF")
    c.ok(drawn is not None and side_at_draw in ("Neutral", 0), f"① drawn (seat {seat()}), he is still Neutral ({side_at_draw})")
    hostile = wait_for(lambda: side("CGF") in ("Hostile", 2), 30, every=0.1)
    t_hostile = sim_time() or 0.0
    a1 = (ammo(rifle) or (None,))[0]
    c.ok(a0 is not None and a1 == a0, f"① no round while he is Neutral (ammo {a0} → {a1})")
    c.ok(hostile is not None and t_hostile - t_draw >= 8.0,
         f"② Hostile only once he aims — not at the draw ({t_hostile - t_draw:.1f} s after it)")
    every = wait_for(lambda: all(side(h) in ("Hostile", 2) for h in hosts), 10, every=0.25)
    c.ok(every is not None, f"② …on every node ({[side(h) for h in hosts]})")
    fired = wait_for(lambda: (ammo(rifle) or (None,))[0] not in (None, a1), 30, every=0.25)
    c.ok(fired is not None, f"③ the rifleman engages him (ammo {a1} → {(ammo(rifle) or (None,))[0]})")


def run_armed_blend_back(c, timeout):
    """test-town: a civilian with a rifle HIDDEN draws it and aims at patrol A from behind a row of civilians (the act — A sees it),
    conceals it, walks along the line, then steps off it; patrol B (another unit, 170 m off at the act) comes up meanwhile. ① A holds
    while a civilian is in the line and fires at him once it is clear — his rifle still hidden (A saw it) · ② B spends no round at him
    while he is in the fight. The twin (PostureScenarioTests.CE2238_BlendBack_…) checks every frame."""
    ids = ids_by_name()
    man, a, b, rifle = ids.get("Insurgent"), ids.get("Patrol A"), ids.get("Patrol B"), ids.get("Rifle")
    if not c.ok(None not in (man, a, b, rifle), "the insurgent, his rifle and both patrols are loaded"):
        return

    def seat():
        return ((comps_on("CGF", rifle) or {}).get("IsEmbarkedTag") or {}).get("Seat")

    def side():
        return ((comps_on("CGF", man) or {}).get("EntityInfo") or {}).get("ForceId")
    a0, b0 = (ammo(a) or (None,))[0], (ammo(b) or (None,))[0]
    call("POST", "/sim/play", {})
    hostile = wait_for(lambda: side() in ("Hostile", 2), timeout, every=0.1)
    c.ok(hostile is not None, f"he aims at patrol A — the act flips him ({side()})")
    concealed = wait_for(lambda: seat() == 3, 15, every=0.1)
    c.ok(concealed is not None, f"he conceals the rifle again (seat {seat()})")
    fired = wait_for(lambda: (ammo(a) or (None,))[0] not in (None, a0), 90, every=0.25)
    c.ok(fired is not None and seat() == 3,
         f"① patrol A fires at him once the line is clear, the rifle still hidden (ammo {a0} → {(ammo(a) or (None,))[0]}, seat {seat()})")
    b1 = (ammo(b) or (None,))[0]
    c.ok(b0 is not None and b1 == b0, f"② patrol B spends no round at him (ammo {b0} → {b1})")



# ── CE-2239 part 3 B2 — the security forces' escalation ladder: warn, then non-lethal, then lethal (Civilians §5c, §5k) ─────

def weapon_status(nid):
    """The weapon row of nid's ActionStatus on the Brain node: (reason, target networkId)."""
    st = (data(call("GET", f"/entities/{nid}/weapons")) or {}).get("status") or {}
    return st.get("reason") or "", st.get("target")


def posture_kind(nid):
    k = ((comps_on("CGF", nid) or {}).get("WeaponPosture") or {}).get("Kind")
    return {0: "AtEase", 1: "Ready", 2: "Aimed"}.get(k, k) if isinstance(k, int) else (k or "AtEase")


def run_civ_escalation(c, timeout):
    """test-town: a police line under a LETHAL ceiling (the ladder grades it) — a rifleman (a rifle in his hands, stun grenades), a baton
    man and a launcher man — an instigator and three protesters around a stone source, and a Hostile GUNMAN who draws a hidden rifle,
    holds it at ease 6 s, then fires it into the air. ⓪ the riot starts · ① drawn and at ease, he is WARNED (a policeman shows the
    Warning cue) and the police rifle stays silent · ② every police rifle round goes at the gunman, and at least one does · ③ he is
    hit · ④ the unarmed rioters get non-lethal force only: one is stunned, irritated or down, none dies. The twin
    (PostureScenarioTests.CE2239_Escalation_…) checks every frame."""
    ids = ids_by_name()
    police = [ids.get(n) for n in ("Police rifleman", "Police baton", "Police launcher")]
    civs = [ids.get(n) for n in ("Instigator", "Protester 1", "Protester 2", "Protester 3")]
    gun, his_rifle, cop_rifle = ids.get("Gunman"), ids.get("Gunman's rifle"), ids.get("Police rifle")
    if not c.ok(None not in police + civs + [gun, his_rifle, cop_rifle], "the police line, four rioters and the gunman are loaded"):
        return
    st = {"drawn": False, "warned": False, "riot": False, "hit": None, "at_ease_rounds": 0, "at_him": 0, "elsewhere": [],
          "effected": set(), "dead": set(), "rifle": (ammo(cop_rifle) or (None,))[0]}

    def watch():
        t = sim_time() or 0.0
        if any(civ_mode(n) == "Riot" for n in civs):
            st["riot"] = True
        st["drawn"] |= ((comps_on("CGF", his_rifle) or {}).get("IsEmbarkedTag") or {}).get("Seat") == 1
        at_ease = st["drawn"] and posture_kind(gun) == "AtEase"
        if at_ease and any(gesture_on("CGF", p) == "Warning" for p in police):
            st["warned"] = True
        a = (ammo(cop_rifle) or (None,))[0]
        _, tgt = weapon_status(police[0])
        if a is not None and st["rifle"] is not None and a < st["rifle"]:
            if at_ease:
                st["at_ease_rounds"] += 1
            if gun in (tgt, st.get("prev")):   # the round left between two 0.25 s samples: his target on either side of it
                st["at_him"] += 1
            else:
                st["elsewhere"].append(tgt)
        st["prev"] = tgt
        st["rifle"] = a if a is not None else st["rifle"]
        for n in civs:
            fx = (comps(n) or {}).get("EffectState") or {}
            if any((fx.get(k) or 0) > t for k in ("StunnedUntil", "IrritatedUntil", "KnockedDownUntil")):
                st["effected"].add(n)
            h = health(n)
            if h is not None and h <= 0:
                st["dead"].add(n)
        hg = health(gun)   # ③/② count a hit only from the first police rifle round at him on (non-lethal force may hit him before)
        if st["at_him"] == 0:
            st["hp0"] = hg
        elif st["hit"] is None and hg is not None and st.get("hp0") is not None and hg < st["hp0"]:
            st["hit"] = t
        return False
    call("POST", "/sim/play", {})
    wait_for(lambda: (watch(), st["hit"] is not None and (sim_time() or 0) - st["hit"] >= 3.0)[1], timeout + 120, every=0.25)
    c.ok(st["riot"], "⓪ the riot starts")
    c.ok(st["warned"] and st["at_ease_rounds"] == 0,
         f"① drawn and at ease, he is warned and the police rifle stays silent (warned {st['warned']}, rounds {st['at_ease_rounds']})")
    c.ok(st["at_him"] > 0 and not st["elsewhere"], f"② every police rifle round goes at the gunman ({st['at_him']} at him, else {st['elsewhere']})")
    c.ok(st["hit"] is not None, f"③ the gunman is hit (at {st['hit']}, health {health(gun)})")
    c.ok(st["effected"] and not st["dead"],
         f"④ the unarmed rioters get non-lethal force only (effected {sorted(st['effected'])}, dead {sorted(st['dead'])})")


def run_civ_escalation_warn(c, timeout):
    """test-town: ua-civ-escalation's line and riot under a WARN ceiling. For 40 s: no police round and no baton blow at all (every
    police mount's ammo kept, no rioter stunned, irritated, down or hurt) — the Warning cue only, and rounds held as 'hold: warning'.
    The twin (PostureScenarioTests.CE2239_EscalationWarn_…) checks every frame."""
    ids = ids_by_name()
    police = [ids.get(n) for n in ("Police rifleman", "Police baton", "Police launcher")]
    civs = [ids.get(n) for n in ("Instigator", "Protester 1", "Protester 2", "Protester 3", "Gunman")]
    cop_rifle = ids.get("Police rifle")
    if not c.ok(None not in police + civs + [cop_rifle], "the police line, four rioters and the gunman are loaded"):
        return
    mounts = police + [cop_rifle]
    a0 = [(ammo(n) or (None,))[0] for n in mounts]
    st = {"riot": False, "cue": False, "held": 0, "effected": set()}

    def watch():
        t = sim_time() or 0.0
        if any(civ_mode(n) == "Riot" for n in civs):
            st["riot"] = True
        if any(gesture_on("CGF", p) == "Warning" for p in police):
            st["cue"] = True
        if any(weapon_status(p)[0] == "hold: warning" for p in police):
            st["held"] += 1
        for n in civs:
            fx = (comps(n) or {}).get("EffectState") or {}
            if any((fx.get(k) or 0) > t for k in ("StunnedUntil", "IrritatedUntil", "KnockedDownUntil")):
                st["effected"].add(n)
        return (sim_time() or 0.0) >= 40.0
    call("POST", "/sim/play", {})
    wait_for(watch, timeout + 90, every=0.5)
    a1 = [(ammo(n) or (None,))[0] for n in mounts]
    hp = [health(n) for n in civs]
    c.ok(st["riot"], "the riot starts")
    c.ok(a1 == a0, f"no police round at all (ammo {a0} → {a1})")
    c.ok(not st["effected"] and all(h is not None and h >= 100 for h in hp),
         f"no blow, no stun — nobody hurt by the police (effected {sorted(st['effected'])}, health {hp})")
    c.ok(st["cue"], "the Warning cue is shown")
    c.ok(st["held"] > 0, f"rounds held as 'hold: warning' ({st['held']} samples)")


def run_armed_self_defence(c, timeout):
    """test-town: ua-civ-escalation's line on its DEFAULT NonLethal ceiling, the same riot, and a gunman (NEUTRAL until his act) who draws and AIMS his
    rifle at the police rifleman. ① lethal fire (the police rifle) goes at THAT man only, and does · ② he is hit · ③ every other rioter
    gets non-lethal force only: one is stunned, irritated or down, none dies. The twin (PostureScenarioTests.CE2239_SelfDefence_…)
    checks every frame."""
    ids = ids_by_name()
    civs = [ids.get(n) for n in ("Instigator", "Protester 1", "Protester 2", "Protester 3")]
    gun, officer, rifle = ids.get("Gunman"), ids.get("Police rifleman"), ids.get("Police rifle")
    if not c.ok(None not in civs + [gun, officer, rifle], "the police rifleman, four rioters and the gunman are loaded"):
        return
    st = {"hit": None, "at_him": 0, "elsewhere": [], "effected": set(), "dead": set(), "rifle": (ammo(rifle) or (None,))[0]}

    def watch():
        t = sim_time() or 0.0
        a = (ammo(rifle) or (None,))[0]
        _, tgt = weapon_status(officer)
        if a is not None and st["rifle"] is not None and a < st["rifle"]:
            if gun in (tgt, st.get("prev")):   # the round left between two 0.25 s samples: his target on either side of it
                st["at_him"] += 1
            else:
                st["elsewhere"].append(tgt)
        st["prev"] = tgt
        st["rifle"] = a if a is not None else st["rifle"]
        for n in civs:
            fx = (comps(n) or {}).get("EffectState") or {}
            if any((fx.get(k) or 0) > t for k in ("StunnedUntil", "IrritatedUntil", "KnockedDownUntil")):
                st["effected"].add(n)
            h = health(n)
            if h is not None and h <= 0:
                st["dead"].add(n)
        hg = health(gun)   # ③/② count a hit only from the first police rifle round at him on (non-lethal force may hit him before)
        if st["at_him"] == 0:
            st["hp0"] = hg
        elif st["hit"] is None and hg is not None and st.get("hp0") is not None and hg < st["hp0"]:
            st["hit"] = t
        return False
    call("POST", "/sim/play", {})
    wait_for(lambda: (watch(), st["hit"] is not None and (sim_time() or 0) - st["hit"] >= 3.0)[1], timeout + 120, every=0.25)
    c.ok(st["at_him"] > 0 and not st["elsewhere"], f"① lethal fire at the man who aims, only ({st['at_him']} at him, else {st['elsewhere']})")
    c.ok(st["hit"] is not None, f"② he is hit (at {st['hit']}, health {health(gun)})")
    c.ok(st["effected"] and not st["dead"],
         f"③ every other rioter gets non-lethal force only (effected {sorted(st['effected'])}, dead {sorted(st['dead'])})")


def run_armed_celebration(c, timeout):
    """test-town: a police patrol (a rifle, a Lethal ceiling) 24 m from a wedding party; a Neutral guest fires his rifle into the air
    (8 rounds), then aims it at the patrol. ① he is WARNED (the Warning cue — at once, his rifle is in his hands) and while he fires into
    the air the patrol spends no round · ② once
    he aims at it, the patrol's rifle fires at him · ③ no other guest is hurt. The twin (PostureScenarioTests.CE2239_Celebration_…)
    checks every frame."""
    ids = ids_by_name()
    guests = [ids.get(f"Guest {i}") for i in range(1, 7)]
    man, his_rifle, cop, cop_rifle = ids.get("Celebrant"), ids.get("Celebrant's rifle"), ids.get("Police patrol"), ids.get("Patrol rifle")
    if not c.ok(None not in guests + [man, his_rifle, cop, cop_rifle], "the patrol, the celebrant and six guests are loaded"):
        return
    his0, cop0 = (ammo(his_rifle) or (None,))[0], (ammo(cop_rifle) or (None,))[0]
    st = {"volley": False, "warned": False, "rounds_volley": 0, "aimed": None, "at_him": 0, "cop": cop0}

    def watch():
        t = sim_time() or 0.0
        wp = (comps_on("CGF", man) or {}).get("WeaponPosture") or {}
        if st["aimed"] is None and wp.get("Kind") in (2, "Aimed") and wp.get("Target") not in (None, 0, "0") \
                and ((comps_on("CGF", man) or {}).get("EntityInfo") or {}).get("ForceId") in ("Hostile", 2):
            st["aimed"] = t
        h = (ammo(his_rifle) or (None,))[0]
        if st["aimed"] is None and h is not None and his0 is not None and h < his0:
            st["volley"] = True
        if st["aimed"] is None and gesture_on("CGF", cop) == "Warning":   # armed in his hands from the start: warned at once (EL-3)
            st["warned"] = True
        a = (ammo(cop_rifle) or (None,))[0]
        if a is not None and st["cop"] is not None and a < st["cop"]:
            if st["aimed"] is None:
                st["rounds_volley"] += 1
            elif weapon_status(cop)[1] == man:
                st["at_him"] += 1
        st["cop"] = a if a is not None else st["cop"]
        return False
    call("POST", "/sim/play", {})
    wait_for(lambda: (watch(), st["at_him"] > 0)[1], timeout + 60, every=0.25)
    hp = [health(n) for n in guests]
    c.ok(st["volley"] and st["warned"], f"① he fires into the air and the patrol warns (volley {st['volley']}, warned {st['warned']})")
    c.ok(st["rounds_volley"] == 0, f"① …and spends no round ({st['rounds_volley']})")
    c.ok(st["aimed"] is not None and st["at_him"] > 0, f"② once he aims at the patrol, it fires at him (aimed at {st['aimed']}, {st['at_him']} rounds)")
    c.ok(all(h is not None and h >= 100 for h in hp), f"③ no other guest is hurt ({hp})")


# ── CE-2241 ② B7 CV-14 — the security forces provoke a crowd (Civilians §5c, §5m) ─────────────────────────────────────────────

_PROVOCATION_NEVER = (2, 5, 7, 10)   # the protesters authored Attitude −1: their threshold is above 1 (§5m PV-3)


def run_civ_provocation(c, timeout):
    """test-town: ua-civ-protest's twelve protesters, each with a hidden Attitude (four at −1), and three policemen 60 m off who walk to
    15 m of the crowd, hold the line there facing it for 60 s, BatonCharge its front rank for 12 s, then hold the line again. ① before the police come within 30 m nobody riots ·
    ② after the batons at least one and fewer than all riot · ③ every Hostile civilian rioted first, on CGF, SimHost and IG alike, and
    one is · ④ no protester whose attitude is −1 riots. The twin (PostureScenarioTests.CE2241_Provocation_…) checks every frame."""
    ids = ids_by_name()
    civs = {i: ids.get(f"Protester {i}") for i in range(1, 13)}
    police = [ids.get(n) for n in ("Police shield", "Police baton 1", "Police baton 2")]
    if not c.ok(None not in list(civs.values()) + police, "twelve protesters and three policemen are loaded"):
        return
    rioted, blows, near_at = {}, [None], [None]
    posts = {police[0]: (194.0, 259.0), police[1]: (194.0, 265.0), police[2]: (194.0, 271.0)}   # each one's MoveToLocation point
    closest = {n: 1e9 for n in police}

    def xy(n):
        p = ((comps(n) or {}).get("SimTransform") or {}).get("Position") or [0, 0, 0]
        return p[0], p[1]

    def watch():
        t = (sim_time() or 0.0)
        where = {i: xy(n) for i, n in civs.items()}
        for n in police:
            px, py = xy(n)
            closest[n] = min(closest[n], ((px - posts[n][0]) ** 2 + (py - posts[n][1]) ** 2) ** 0.5)
            if near_at[0] is None and any(((x - px) ** 2 + (y - py) ** 2) ** 0.5 <= 30.0 for x, y in where.values()):
                near_at[0] = t
        for i, n in civs.items():
            m = civ_mind(n)
            if _CIV_MODES.get(m.get("Mode"), m.get("Mode")) == "Riot":
                rioted.setdefault(i, t)
            if blows[0] is None and (((comps(n) or {}).get("EffectState") or {}).get("KnockedDownUntil") or 0) > t:
                blows[0] = t
        return False
    call("POST", "/sim/play", {})
    t0 = sim_time() or 0.0
    wait_for(lambda: (watch(), (sim_time() or 0.0) - t0 >= 170.0)[1], timeout + 400, every=0.5)
    first = min(rioted.values()) if rioted else None
    c.ok(near_at[0] is not None and (first is None or first >= near_at[0]),
         f"① nobody riots before the police are within 30 m (police near at {near_at[0]}, first riot at {first})")
    # ⭐ CE-4041 — every policeman reaches his post: 📌 two of the three spawned inside Block B, their moves failed unreachable every
    #   few frames and they never moved; ① and ② still passed on the third man alone
    c.ok(all(d <= 3.0 for d in closest.values()), f"① every policeman reaches his post (closest m: { {k: round(v, 1) for k, v in closest.items()} })")
    c.ok(blows[0] is not None, f"② the baton charge knocks a protester down (at {blows[0]})")
    c.ok(1 <= len(rioted) < len(civs), f"② a FRACTION riots: {len(rioted)} of {len(civs)} ({sorted(rioted)})")
    hostile = {}
    for h in ("CGF", "SimHost", "IG"):
        hostile[h] = {i for i, n in civs.items() if ((comps_on(h, n) or {}).get("EntityInfo") or {}).get("ForceId") in ("Hostile", 2)}
    c.ok(hostile["CGF"] and all(hostile[h] <= set(rioted) for h in hostile),
         f"③ every Hostile civilian rioted first, and one is ({ {h: sorted(v) for h, v in hostile.items()} })")
    c.ok(hostile["CGF"] == hostile["SimHost"] == hostile["IG"], f"③ …the same on CGF, SimHost and IG")
    c.ok(not set(rioted) & set(_PROVOCATION_NEVER), f"④ no protester of attitude −1 riots ({sorted(set(rioted) & set(_PROVOCATION_NEVER))})")


# ── CE-2238 part 5 B5 — the bomber sets off his worn charge (Civilians §5c, §5l) ─────────────────────────────────────────────

def run_armed_bomber(c, timeout):
    """test-town: a civilian with a charge HIDDEN walks into a crowd of ten and sets it off (DetonateCharge); a rifleman 30 m off. ① until the
    blast he stays Neutral and the rifleman spends no round · ② the vest bursts: it is gone and he is dead · ③ the casualties follow the
    burst: everyone within 3 m of it is hurt, nobody beyond 25 m is (the rifleman unhurt). The twin (PostureScenarioTests.CE2238_Bomber_…)
    checks every frame against the warhead's own radii."""
    ids = ids_by_name()
    civs = [ids.get(f"Civilian {i}") for i in range(1, 11)]
    man, vest, rifleman = ids.get("Bomber"), ids.get("Vest"), ids.get("Rifleman")
    if not c.ok(None not in civs + [man, vest, rifleman], "the bomber, his vest, ten civilians and the rifleman are loaded"):
        return
    a0 = (ammo(rifleman) or (None,))[0]
    st = {"burst": None, "at": None, "side": [], "rounds": 0}

    def pos(n):
        p = ((comps_on("CGF", n) or {}).get("SimTransform") or {}).get("Position") or [0, 0, 0]
        return p[0], p[1]

    def watch():
        t = sim_time() or 0.0
        if st["burst"] is None:
            hb = health(man)
            if hb is None or hb >= 100:   # the blast is his first hurt (the spent vest's teardown is the twin's to check)
                st["at"] = pos(man)
                st["side"].append(((comps_on("CGF", man) or {}).get("EntityInfo") or {}).get("ForceId"))
                a = (ammo(rifleman) or (None,))[0]
                if a is not None and a0 is not None:
                    st["rounds"] = max(st["rounds"], a0 - a)
            else:
                st["burst"] = t
        return st["burst"] is not None and t - st["burst"] >= 2.0
    call("POST", "/sim/play", {})
    wait_for(watch, timeout + 90, every=0.25)
    c.ok(st["rounds"] == 0 and all(s in ("Neutral", 0, None) for s in st["side"]),
         f"① until the blast he stays Neutral and the rifleman spends no round (rounds {st['rounds']}, sides {sorted(set(map(str, st['side'])))})")
    hb = health(man)
    c.ok(st["burst"] is not None and hb is not None and hb <= 0, f"② the vest bursts and he is dead (at {st['burst']}, health {hb})")
    bx, by = st["at"] or (0, 0)
    rows = []
    for n in civs + [rifleman]:
        x, y = pos(n)
        rows.append((n, ((x - bx) ** 2 + (y - by) ** 2) ** 0.5, health(n)))
    near = [r for r in rows if r[1] <= 3.0]
    far = [r for r in rows if r[1] > 25.0]
    c.ok(near and all(h is not None and h < 100 for _, _, h in near), f"③ everyone within 3 m is hurt ({[(n, round(d, 1), h) for n, d, h in near]})")
    c.ok(far and all(h is not None and h >= 100 for _, _, h in far), f"③ nobody beyond 25 m is ({[(n, round(d, 1), h) for n, d, h in far]})")


# ── CE-2275 — sq-defend-house (Platoon §12.2 stage B; Defend & Attack §8c): a Red squad defends OBJ on cqb-village ─────────────
#   ⭐ the squad's hold is the operator's (Roe HoldFire, SetBy Operator, authored); its sector is set over HTTP while the cluster is
#   still paused, as an operator would (POST /entities/{id}/sector, arc from where the commander stands); a Blue fire team walks in
#   from the south. ① the posts land in OBJ on more than one storey · ③ no Red round before the first Blue enters the sector, then
#   Red fires · ④ a man under fire moves to his alternate (PROGRESS Displacing). ② (the AR's longest view) needs VisibilityFan — not built.

_DH_ORIGIN, _DH_LEFT, _DH_RIGHT, _DH_RANGE = (120.9, 157.0), 150.0, 210.0, 90.0


def _dh_in_sector(p):
    dx, dy = p[0] - _DH_ORIGIN[0], p[1] - _DH_ORIGIN[1]
    if (dx * dx + dy * dy) ** 0.5 > _DH_RANGE:
        return False
    b = math.degrees(math.atan2(dx, dy)) % 360.0
    return _DH_LEFT <= b <= _DH_RIGHT


def run_sq_defend_house(c, timeout):
    """⭐ CE-2275 — sq-defend-house (DESIGN_Platoon_Attack_Defend_Clear §12.2 B; DESIGN_Defend_And_Attack_Position §8c)."""
    ids = ids_by_name()
    reds = [ids.get(n) for n in ["Red Leader"] + [f"Red Rifleman {i}" for i in range(1, 7)]]
    blues = [ids.get(f"Blue {i}") for i in range(1, 5)]
    sq = ids.get("Red Squad")
    if not c.ok(sq is not None and None not in reds + blues, f"the Red squad (seven men) and the Blue fire team are loaded (squad {sq}, reds {reds}, blues {blues})"):
        return
    sec = call("POST", f"/entities/{sq}/sector", {"left": _DH_LEFT, "right": _DH_RIGHT, "maxRange": _DH_RANGE})
    c.ok(isinstance(sec, dict) and data(sec) is not None, f"the operator gives the squad its sector south ({json.dumps(data(sec) or sec)[:200]})")
    call("POST", "/sim/play", {})
    posting = wait_for(lambda: (m := maneuver(sq)) and m.get("phaseName") == "Posting" and m, 60)
    if not c.ok(posting is not None, f"the squad reaches OBJ and starts posting ({json.dumps(maneuver(sq))[:300]})"):
        return
    wait_for(lambda: False, 12, every=1.0)   # the two post asks (≤ 5 s each) and the re-issue
    m = maneuver(sq)
    print(f"    maneuver: {json.dumps(m)[:1200]}")
    for nid in reds[:2]:
        print(f"    order {nid}: {json.dumps(data(call('GET', f'/entities/{nid}/order')))[:700]}")
    posts = [((x.get("order") or {}).get("kind"), (x.get("order") or {}).get("point")) for x in m.get("members", [])]
    storeys = sorted({round(pt[2] / 3.0) for k, pt in posts if k == "Watch" and pt})
    windows = []
    for nid in reds:
        post = ((((data(call("GET", f"/entities/{nid}/order")) or {}).get("task") or {}).get("params") or {}).get("post") or {})
        if post.get("window"):
            windows.append((nid, post.get("point"), post.get("setBack"), post.get("frame")))
    print(f"    posts {posts}; window posts {windows}")
    c.ok(len(storeys) >= 2, f"① the posts hold more than one storey of OBJ (storeys {storeys})")
    roof = []
    for nid in reds:
        post = ((((data(call("GET", f"/entities/{nid}/order")) or {}).get("task") or {}).get("params") or {}).get("post") or {})
        if post.get("roof"):
            roof.append((nid, post.get("point")))
    # ⭐ CE-2275 D-2g (DA-5, DA-22, DA-47) — the roof is the ask after the windows; a roof man hides prone behind the parapet
    c.ok(len(roof) >= 1 and all(pt and pt[2] >= 5.0 for _, pt in roof), f"① the roof is manned, by roof posts (D-2g): {roof}")
    # ⭐ CE-2275 D-2l (DA-77, R-2059) — a Regular squad fights a window from BESIDE ITS FRAME, never set back (only Veteran+ sets
    #   back); a window with no frame within 2.5 m stays a sill post (DA-84). ⛔ SUPERSEDED `2026-10-19`: "set back (a Regular squad, DA-6)".
    c.ok(len(windows) >= 1 and any(fr for *_, fr in windows) and not any(sb for _, _, sb, _ in windows),
         f"① window posts at the frame, none set back (a Regular squad, D-2l): {windows}")

    first_in, red_shots, blue_shots, seen, queued = [None], {}, {}, set(), set()
    t0 = sim_time() or 0.0
    while (sim_time() or 0.0) - t0 < 150.0:
        t = sim_time() or 0.0
        if first_in[0] is None and any((p := position(b)) and _dh_in_sector(p) for b in blues):
            first_in[0] = t
        for x in _if_shots(set(reds) | set(blues)):
            k = x.get("seq")
            if k in seen:
                continue
            seen.add(k)
            (red_shots if x.get("shooter") in reds else blue_shots).setdefault(x.get("shooter"), []).append(t)
        for r in (((data(call("GET", f"/entities/{sq}/reports")) or {}).get("outbox") or {}).get("queued") or []):
            queued.add(json.dumps({k: r.get(k) for k in ("kind", "kindName", "subtype", "subtypeName")}))
        wait_for(lambda: False, 1.0, every=1.0)
    early = [(s, ts) for s, ts in red_shots.items() if first_in[0] is None or min(ts) < first_in[0] - 0.5]
    print(f"    first Blue in the sector at {first_in[0]}; Red shots: { {s: (len(ts), round(min(ts), 1)) for s, ts in red_shots.items()} }; "
          f"Blue shots: { {s: (len(ts), round(min(ts), 1)) for s, ts in blue_shots.items()} }")
    c.ok(first_in[0] is not None, "the Blue fire team walks into the sector")
    c.ok(not early, f"③ no Red round before the first Blue enters the sector ({early})")
    c.ok(len(red_shots) >= 2, f"③ …then Red fires: {len(red_shots)} shooters")
    # a squad with no superior addresses its reports to nobody, so no inbox logs them — its OUTBOX is where they show (sampled above)
    seen_reports = list(queued) + [json.dumps(r) for r in squad_reports(sq)]
    print(f"    reports queued or logged: {sorted(queued)[:12]}")
    c.ok(any("Set" in r for r in seen_reports), "PROGRESS Set (every man at his post)")
    c.ok(any("Displacing" in r for r in seen_reports), "④ a man under fire moves to his alternate (PROGRESS Displacing)")

# ── CE-2276 E-1 — pc-defend-dry (Platoon §12.2 stage A; Defend & Attack §8d): PlatoonDefend's decisions on FAKED reports ─────────
#   A Red platoon (305, its TKB default SOP BasicPlatoonSop) over two line squads (OBJ, H1) and a reserve (H4) on cqb-village, the EA
#   south of OBJ; no enemy. The squads' PROGRESS Set and the SPOTs are faked over POST /entities/{squad}/reports (CC-13) — a real Set is
#   held by CE-2304 (the interior move stall). ① every squad SquadDefends its point; the line holds fire, the reserve returns it ·
#   ② a SPOT outside the EA changes nothing · ③ one inside opens every line man's fire within ~1 s (the chain, PC-6); the reserve
#   keeps ReturnFire · ④ the SOP's hold row never pauses the defence (DA-55).

def _roe(nid):
    return ((data(call("GET", f"/entities/{nid}/state")) or {}).get("roeEffective") or {}).get("fire")


def run_pc_defend_dry(c, timeout):
    """⭐ CE-2276 E-1 — pc-defend-dry (DESIGN_Platoon_Attack_Defend_Clear §12.2 A; DESIGN_Defend_And_Attack_Position §8d)."""
    ids = ids_by_name()
    pl = ids.get("Red Platoon")
    names = {9310: "Red Squad 1", 9320: "Red Squad 2", 9330: "Red Reserve"}
    sq = {k: ids.get(n) for k, n in names.items()}
    men = {k: [ids.get(f"{n} Leader")] + [ids.get(f"{n} Rifleman {i}") for i in (1, 2, 3)] for k, n in names.items()}
    line = men[9310] + men[9320]
    if not c.ok(pl and None not in sq.values() and None not in line + men[9330],
                f"the platoon, its three squads and their twelve men are loaded (platoon {pl}, squads {sq})"):
        return
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(pl) == "PlatoonDefend", 15), f"the platoon runs PlatoonDefend (task: {task_name(pl)})")
    ordered = wait_for(lambda: all(task_name(s) == "SquadDefend" for s in sq.values()), 20)
    c.ok(ordered, f"① every squad is ordered to SquadDefend its point ({ {k: task_name(s) for k, s in sq.items()} })")
    held = wait_for(lambda: all(_roe(m) == "HoldFire" for m in line) and all(_roe(m) == "ReturnFire" for m in men[9330]), 10)
    c.ok(held, f"① the line holds fire, the reserve returns it (line {[_roe(m) for m in line]}, reserve {[_roe(m) for m in men[9330]]})")

    for k in (9310, 9320):
        r = call("POST", f"/entities/{sq[k]}/reports", {"kind": "Progress", "subtype": "Set"})
        c.ok(isinstance(r, dict) and (data(r) or {}).get("sent"), f"a faked PROGRESS Set from {names[k]} ({json.dumps(data(r) or r)[:160]})")
    wait_for(lambda: False, 3, every=1.0)
    call("POST", f"/entities/{sq[9310]}/reports", {"kind": "Spot", "at": [120, 260, 0], "count": 3, "size": "Team"})
    wait_for(lambda: False, 5, every=1.0)
    c.ok(all(_roe(m) == "HoldFire" for m in line), f"② a SPOT north of OBJ — outside the EA — changes nothing (line {[_roe(m) for m in line]})")

    t0 = sim_time() or 0.0
    call("POST", f"/entities/{sq[9320]}/reports", {"kind": "Spot", "at": [120, 60, 0], "count": 3, "size": "Team"})
    opened = wait_for(lambda: all(_roe(m) == "FireAtWill" for m in line), 10, every=0.2)
    dt = (sim_time() or 0.0) - t0
    c.ok(opened and dt <= 1.5, f"③ a SPOT inside the EA opens every line man's fire in {dt:.2f} s sim (line {[_roe(m) for m in line]})")
    c.ok(all(_roe(m) == "ReturnFire" for m in men[9330]), f"③ …the reserve still only returns fire ({[_roe(m) for m in men[9330]]})")
    st = data(call("GET", f"/entities/{pl}/state")) or {}
    c.ok(task_name(pl) == "PlatoonDefend" and not st.get("pausedTask"),
         f"④ the platoon's SOP did not pause the defence (task {task_name(pl)}, paused {st.get('pausedTask')})")
    c.ok(all(task_name(s) == "SquadDefend" for s in sq.values()), f"④ …no squad was pulled off to hold ({ {k: task_name(s) for k, s in sq.items()} })")



# ── CE-2277 DA-F — pc-attack-dry (Platoon §12.2 stage A; Defend & Attack §8f): PlatoonAttack's decisions on FAKED reports ──────
def _order(nid):
    return ((data(call("GET", f"/entities/{nid}/order")) or {}).get("task") or {})


def run_pc_attack_dry(c, timeout):
    """⭐ CE-2277 — pc-attack-dry (DESIGN_Platoon_Attack_Defend_Clear §12.2 A; DESIGN_Defend_And_Attack_Position §8f, AT-1…AT-12).
    A Blue platoon south of OBJ attacks the area round OBJ (radius 20 — OBJ is its only building, so the sweep finds it). No enemy:
    the squads move for real, the phase reports are faked. ① the base of fire goes up first, the others wait · ② its Set ⇒ the
    assault and the reserve bound · ③ the assault's Set ⇒ the lift (ReturnFire) and ClearBuilding on OBJ · ④ BuildingClear ⇒ every
    squad consolidates (SquadDefend) · ⑤ all Set ⇒ the run ends."""
    ids = ids_by_name()
    pl = ids.get("Blue Platoon")
    sq = [ids.get(f"Blue Squad {i}") for i in (1, 2, 3)]
    if not c.ok(pl and None not in sq, f"the platoon and its three squads are loaded (platoon {pl}, squads {sq})"):
        return
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(pl) == "PlatoonAttack", 15), f"the platoon runs PlatoonAttack (task: {task_name(pl)})")
    support = wait_for(lambda: next((s for s in sq if task_name(s) == "SquadDefend"), None), 20)
    if not c.ok(support is not None, f"① one squad is ordered to SquadDefend — the base of fire ({ {s: task_name(s) for s in sq} })"):
        return
    o = _order(support)
    print(f"    support order: {json.dumps(o)[:400]}")
    others = [s for s in sq if s != support]
    wait_for(lambda: False, 3, every=1.0)
    c.ok(all(task_name(s) in (None, "", "None") for s in others),
         f"① …the assault and the reserve wait for it ({ {s: task_name(s) for s in others} })")
    c.ok(wait_for(lambda: _roe(support) == "FireAtWill", 10), f"① …and the base of fire fires at will (ROE {_roe(support)})")

    call("POST", f"/entities/{support}/reports", {"kind": "Progress", "subtype": "Set"})
    bound = wait_for(lambda: all(task_name(s) == "BoundingOverwatch" for s in others), 15)
    c.ok(bound, f"② its Set ⇒ the assault and the reserve bound ({ {s: task_name(s) for s in others} })")
    assault = wait_for(lambda: next((s for s in others if (_order(s).get("params") or {})), None), 5) or others[0]
    print(f"    bound orders: { {s: json.dumps(_order(s).get('params'))[:200] for s in others} }")
    # the assault is the one whose bound point is nearer OBJ (40 m vs the reserve's 100 m)
    def dist(s):
        prm = _order(s).get("params") or {}
        pt = prm.get("objective") or prm.get("Objective") or [0, 0, 0]
        return math.hypot(pt[0] - 125, pt[1] - 161)
    assault = min(others, key=dist)
    reserve = next(s for s in others if s != assault)
    c.ok(dist(assault) < 60 < dist(reserve), f"② …the assault to the flank point 40 m off, the reserve behind ({dist(assault):.0f} m, {dist(reserve):.0f} m)")

    call("POST", f"/entities/{assault}/reports", {"kind": "Progress", "subtype": "Set"})
    cleared = wait_for(lambda: task_name(assault) == "ClearBuilding", 15)
    c.ok(cleared, f"③ the assault's Set ⇒ it clears OBJ (task {task_name(assault)}, params {json.dumps(_order(assault).get('params'))[:200]})")
    c.ok(wait_for(lambda: _roe(support) == "ReturnFire", 10), f"③ …the fire lifts: the base of fire only returns fire (ROE {_roe(support)})")

    call("POST", f"/entities/{assault}/reports", {"kind": "Progress", "subtype": "BuildingClear"})
    held = wait_for(lambda: all(task_name(s) == "SquadDefend" for s in sq), 20)
    c.ok(held, f"④ BuildingClear ⇒ every squad consolidates round OBJ ({ {s: task_name(s) for s in sq} })")
    for s in sq:
        call("POST", f"/entities/{s}/reports", {"kind": "Progress", "subtype": "Set"})
    ended = wait_for(lambda: task_name(pl) != "PlatoonAttack", 15)
    c.ok(ended, f"⑤ every squad Set ⇒ the attack is won and ends (platoon task {task_name(pl)})")



# ── CE-2277 + CE-2276 — pc-defend-house (Platoon §12.2 stage C): a real attack on a real defence, AI on both sides ───────────────
def run_pc_defend_house(c, timeout):
    """⭐ CE-2277 / CE-2276 — pc-defend-house (DESIGN_Platoon_Attack_Defend_Clear §12.2 C; DESIGN_Defend_And_Attack_Position §8d, §8f).
    Red (pc-defend-dry's cast) defends OBJ, H1 and H4; Blue (pc-attack-dry's cast) attacks the area round OBJ (radius 35: OBJ, H1, H3).
    Nothing is faked. ⛔ The winner is NOT asserted: ① both plays run · ② Blue's base of fire goes up and Red opens fire on what it
    spots inside its EA · ③ both sides fire · ④ Blue reaches its assault (ClearBuilding) or breaks off (BreakContact) — the log says
    which, with every phase change on both sides."""
    ids = ids_by_name()
    rpl, bpl = ids.get("Red Platoon"), ids.get("Blue Platoon")
    red = {n: ids.get(n) for n in ("Red Squad 1", "Red Squad 2", "Red Reserve")}
    blue = {n: ids.get(f"Blue Squad {i}") for i, n in enumerate(("B1", "B2", "B3"), 1)}
    if not c.ok(rpl and bpl and None not in list(red.values()) + list(blue.values()),
                f"both platoons and their squads are loaded (red {red}, blue {blue})"):
        return
    call("POST", "/sim/timescale", {"scale": 4})
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(rpl) == "PlatoonDefend" and task_name(bpl) == "PlatoonAttack", 15),
         f"① Red runs PlatoonDefend, Blue PlatoonAttack ({task_name(rpl)}, {task_name(bpl)})")
    red_men = {v for k, v in ids.items() if k.startswith("Red Squad") or k.startswith("Red Reserve")}
    blue_men = {v for k, v in ids.items() if k.startswith("Blue Squad")}
    log, last = [], {}
    seen = {"support": False, "redHeld": False, "redFire": False, "assault": False, "broke": False}
    end = time.time() + min(timeout, 600)
    while time.time() < end:
        t = sim_time() or 0.0
        for name, nid in list(red.items()) + list(blue.items()) + [("Red Platoon", rpl), ("Blue Platoon", bpl)]:
            now = (task_name(nid), _roe(nid))
            if last.get(name) != now:
                last[name] = now
                log.append(f"{t:7.1f}  {name:12s} {now[0]} · {now[1]}")
        tasks = [last[n][0] for n in blue]
        seen["support"] |= "SquadDefend" in tasks
        seen["assault"] |= "ClearBuilding" in tasks or "FireAndMovement" in tasks
        seen["broke"] |= "BreakContact" in tasks
        seen["redHeld"] |= any(last[n][1] == "HoldFire" for n in ("Red Squad 1", "Red Squad 2"))   # the line holds first (DA-7)…
        seen["redFire"] |= seen["redHeld"] and any(last[n][1] == "FireAtWill" for n in ("Red Squad 1", "Red Squad 2"))   # …then opens
        if seen["assault"] or seen["broke"] or task_name(bpl) != "PlatoonAttack":
            break
        time.sleep(1.0)
    print("    timeline (sim s, squad, task · ROE):")
    for line in log:
        print("      " + line)
    call("POST", "/perspective", {"name": "SimHost"})   # the shots are recorded where the rounds fly
    try:
        shots = (data(call("GET", "/combat/shots?last=256")) or {}).get("shots") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    sides = {}
    for sh in shots:
        who = sh.get("shooter")
        side = "Red" if who in red_men else "Blue" if who in blue_men else "?"
        sides[side] = sides.get(side, 0) + 1
    c.ok(seen["support"], "② Blue's base of fire goes up (a Blue squad SquadDefends)")
    c.ok(seen["redFire"], "② Red's line opens fire (FireAtWill) on what it spots inside its EA")
    c.ok(len(shots) > 0, f"③ shots are fired (by side {sides}, {len(shots)} in the last 256)")
    c.ok(seen["assault"] or seen["broke"], f"④ Blue reaches its assault ({seen['assault']}) or breaks off ({seen['broke']}) — the winner is not asserted")



# ── Urban Battle §6 L2 — uc-convoy-hit (demo-city): the trigger man, the IED, the carrier's reaction (CE-2306, CE-2307) ──────────
def _detonations():
    call("POST", "/perspective", {"name": "SimHost"})
    try:
        return (data(call("GET", "/combat/detonations?last=64")) or {}).get("detonations") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})


def run_uc_convoy_hit(c, timeout):
    """⭐ Urban Battle §6 L2 — uc-convoy-hit (DESIGN_Urban_Battle_Demo.md §6; Defend & Attack §8e.1; Squad Tactics §5b.1). An APC with a
    squad aboard drives north up Avenue E; a placed charge lies on the road at y 200, its trigger man on the west sidewalk. Nothing is
    faked. ① the APC drives its route · ② the charge goes off only with the APC in its kill zone · ③ the carrier, hit with troops
    aboard, pushes through or halts and its squad dismounts — the log says which · ④ the branch completes (the push-through carries it
    on; a dismounted squad fights)."""
    ids = ids_by_name()
    apc, sq, ied, trig = ids.get("Convoy APC"), ids.get("Convoy Squad"), ids.get("IED"), ids.get("Trigger man")
    men = [ids.get(f"Convoy {n}") for n in ("Leader", "Rifleman 1", "Rifleman 2", "Rifleman 3", "Rifleman 4")]
    if not c.ok(None not in (apc, sq, ied, trig) and None not in men, f"the convoy, the charge and the trigger man are loaded ({apc}, {sq}, {ied}, {trig})"):
        return
    ied_at = position(ied)
    before = len(_detonations())
    call("POST", "/sim/timescale", {"scale": 2})
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(apc) == "FollowRoute" and (position(apc) or (0, 0))[1] > 35, 30),
         f"① the APC drives its route north (task {task_name(apc)}, at {position(apc)})")
    print(f"    trigger man's task: {task_name(trig)}; the charge at {ied_at}")
    det, at, t_det, track = None, None, None, []
    end = time.time() + timeout
    while time.time() < end:
        pa = position(apc)
        track.append((sim_time(), pa, task_name(apc)))
        ds = _detonations()
        if len(ds) > before:
            det, at, t_det = ds[-1], pa, sim_time()
            break
        if pa and pa[1] > 260:
            break
        time.sleep(0.25)
    if not c.ok(det is not None, f"② the charge goes off ({len(_detonations()) - before} new detonations; the APC reached {track[-1][1] if track else None})"):
        for t, pa, tk in track[-12:]:
            print(f"      {t}  {pa}  {tk}")
        print(f"    trigger man: {json.dumps(data(call('GET', f'/entities/{trig}/state')))[:600]}")
        return
    d = math.hypot(at[0] - ied_at[0], at[1] - ied_at[1]) if at and ied_at else 999
    c.ok(d <= 15, f"② …with the APC in its kill zone ({d:.1f} m from the charge at t {t_det:.1f}; detonation {json.dumps(det)[:200]})")
    branch = wait_for(lambda: "push" if task_name(apc) == "PushThrough"
                      else "dismount" if any("IsEmbarkedTag" not in comps(m) for m in men) else None, 15)
    hp = (comps(apc).get("Health") or {})
    c.ok(branch is not None, f"③ the carrier reacts: {branch} (APC task {task_name(apc)}, health {hp}, squad {task_name(sq)})")
    if branch == "push":
        aboard = all("IsEmbarkedTag" in comps(m) for m in men)
        c.ok(aboard, f"③ …its squad stays aboard while it pushes (squad task {task_name(sq)}; aboard {[('IsEmbarkedTag' in comps(m)) for m in men]})")
        far = wait_for(lambda: (position(apc) or (0, 0))[1] > ied_at[1] + 100, 60)
        c.ok(far, f"④ it pushes through, 100 m past the charge (at {position(apc)})")
        c.ok(wait_for(lambda: task_name(apc) == "FollowRoute", 60), f"④ …then its route resumes (task {task_name(apc)})")
    elif branch == "dismount":
        out = wait_for(lambda: all("IsEmbarkedTag" not in comps(m) for m in men if comps(m)), 20)
        c.ok(out, "④ the squad is out")
        # the squad's own task resumes (SquadHold); each man fights in his own CombatPosture — so "fights" is their rounds
        def their_shots():
            call("POST", "/perspective", {"name": "SimHost"})
            try:
                return [x for x in (data(call("GET", "/combat/shots?last=256")) or {}).get("shots") or [] if x.get("shooter") in men]
            finally:
                call("POST", "/perspective", {"name": "Scenario"})
        fired = wait_for(lambda: their_shots(), 60)
        c.ok(fired, f"④ …and fights: the dismounted men fire ({len(fired or [])} rounds; men's tasks {[task_name(m) for m in men]})")
    print(f"    the carrier: {hp}, capabilities {(comps(apc).get('ActorCapabilityState') or {}).get('Capabilities')} (the MilitaryApc loses CanMove on any hit, "
          f"CE-3092 — its branch is the halt; the Bradley keeps it above 60 % — its branch is the push)")



def run_car_bomb(c, timeout):
    """⭐ CE-4035 G-6 — ua-car-bomb: TriggerIed + the civilian car's boot charge (ST-9) as ONE composition, never measured before. A civilian
    car drives to a checkpoint; its driver watches his own boot charge and sets it off with the APC in its 15 m kill zone. Nothing is
    faked. ① the car drives up and nobody fires at it · ② the charge bursts AT THE CAR, set off by the driver, the APC within 15 m ·
    ③ the soldiers nearest it are hurt."""
    ids = ids_by_name()
    car, drv, chg, apc = ids.get("Car"), ids.get("Driver"), ids.get("Car charge"), ids.get("Checkpoint APC")
    men = [ids.get(f"Checkpoint Soldier {i}") for i in (1, 2, 3)]
    if not c.ok(None not in (car, drv, chg, apc) and None not in men, f"the car, its driver and charge, the checkpoint are loaded"):
        return
    h0 = {m: health(m) for m in men}
    before = len(_detonations())
    call("POST", "/sim/timescale", {"scale": 2})
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: (position(car) or (-999, 0))[0] > 160, 30), f"① the car drives east (at {position(car)}; driver {task_name(drv)})")
    det, at, track = None, None, []
    end = time.time() + timeout
    while time.time() < end:
        pc = position(car)
        track.append((sim_time(), pc, task_name(car), task_name(drv)))
        ds = _detonations()
        if len(ds) > before:
            det, at = ds[-1], pc
            break
        time.sleep(0.25)
    for row in track[-8:]:
        print(f"      {row}")
    shots = _if_shots(men + [apc])
    c.ok(not [x for x in shots if det is None or (x.get("tick") or 0) < (det.get("tick") or 1e18)], f"① nobody fired at the car before the blast ({len(shots)} shots)")
    if not c.ok(det is not None, f"② the charge goes off ({len(_detonations()) - before} new detonations)"):
        print(f"    driver: {json.dumps(data(call('GET', f'/entities/{drv}/state')))[:600]}")
        return
    burst = det.get("burst") or det.get("at") or det.get("position")
    pa = position(apc)
    print(f"    detonation {json.dumps(det)[:300]}; the car at {at}; the APC at {pa}")
    c.ok(burst is not None and at is not None and flat(burst, at) <= 6, f"② …at the car ({flat(burst, at) if burst and at else None} m from it)")
    c.ok(at is not None and pa is not None and flat(at, pa) <= 15, f"② …with the APC in the kill zone ({flat(at, pa) if at and pa else None} m)")
    c.ok(det.get("shooter") in (drv, chg, car), f"② …set off by the driver (shooter {det.get('shooter')}, driver {drv})")
    time.sleep(3)
    hurt = [m for m in men if (health(m) or 0) < (h0[m] or 0)]
    c.ok(bool(hurt), f"③ the soldiers nearest it are hurt — {[(m, health(m)) for m in men]}")


# ── Urban Battle §6 L7 — uc-request-chain (demo-city): spotter → mortar → heard launch → SPOT → REQUEST → HQ → armed search ────────
def run_uc_request_chain(c, timeout):
    """⭐ Urban Battle §6 L7 — uc-request-chain (DESIGN_Urban_Battle_Demo.md §6; Command Chain §8d / §8d-a; Indirect Fire §3.11a;
    Air Operations). Nothing is faked and nothing is sent by time: ① the insurgent spotter calls his mortar on the platoon he sees
    (CE-2308) · ② a Blue man hears the launch past 1 km ⇒ his squad's staff SPOTs IndirectFire (CE-2309) · ③ the platoon asks the HQ
    for FireSupport (CE-2310) · ④ the HQ sends its free AH-64 on an armed search of the point (CE-2312, CE-2311) — and not before ③."""
    ids = ids_by_name()
    hq, heli, pl, spot = ids.get("Blue HQ"), ids.get("Apache"), ids.get("Blue Platoon"), ids.get("Spotter")
    tubes = [ids.get("Mortar Tube 1"), ids.get("Mortar Tube 2")]
    crew = [ids.get("Mortar Man 1"), ids.get("Mortar Man 2")]
    squads = [ids.get("Blue Squad 1"), ids.get("Blue Squad 2")]
    if not c.ok(None not in (hq, heli, pl, spot) and None not in tubes + crew + squads, f"the HQ, its Apache, the platoon and the mortar cell are loaded"):
        return
    mortar_at = position(tubes[0])
    call("POST", "/sim/timescale", {"scale": 2})
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(hq) == "SupportCommand" and task_name(pl) == "PlatoonDefend" and task_name(spot) == "SpotForFire", 30),
         f"the plays run (HQ {task_name(hq)}, platoon {task_name(pl)}, spotter {task_name(spot)}, Apache {task_name(heli)})")
    shooters = set(tubes + crew)
    log, seen = [], {}
    end = time.time() + timeout
    while time.time() < end:
        t = sim_time() or 0.0
        if "rounds" not in seen and any(d.get("shooter") in shooters for d in _detonations()):
            seen["rounds"] = t; log.append(f"{t:7.1f} mortar rounds land")
        reps = squad_reports(last=256)
        if "spot" not in seen:
            r = next((r for r in reps if r.get("kind") == "Spot" and "IndirectFire" in str(r.get("equipment")) and r.get("from") in squads), None)
            if r: seen["spot"] = t; seen["spotAt"] = r.get("at"); log.append(f"{t:7.1f} SPOT IndirectFire from {r.get('from')} at {r.get('at')}")
        if "ask" not in seen:
            r = next((r for r in reps if r.get("kind") == "Request" and r.get("subtype") == "FireSupport" and r.get("from") == pl), None)
            if r: seen["ask"] = t; log.append(f"{t:7.1f} REQUEST FireSupport from the platoon at {r.get('at')}")
        tk = task_name(heli)
        if tk and "heli" not in seen:
            seen["heli"] = t; seen["heliTask"] = tk; log.append(f"{t:7.1f} Apache ordered: {tk} {json.dumps(_order(heli).get('params'))[:200]}")
        if "heli" in seen and "rounds" in seen:   # ⭐ Q95 E3 — off the terrain the rounds fly 2.3 km: the chain can finish first
            break
        time.sleep(1.0)
    print("    timeline:")
    for line in log:
        print("      " + line)
    c.ok("rounds" in seen, "① the spotter's mortar fires on the platoon")
    c.ok("spot" in seen, "② a Blue squad's staff SPOTs IndirectFire (the heard launch)")
    if "spot" in seen and seen.get("spotAt") and mortar_at:
        d = math.hypot(seen["spotAt"][0] - mortar_at[0], seen["spotAt"][1] - mortar_at[1])
        print(f"    the SPOT's estimate is {d:.0f} m from the tubes")
    c.ok("ask" in seen, "③ the platoon asks the HQ for FireSupport")
    c.ok(seen.get("heliTask") == "AirArmedSearch", f"④ the HQ sends the Apache on an armed search ({seen.get('heliTask')})")
    c.ok("ask" in seen and "heli" in seen and seen["heli"] >= seen["ask"] - 0.5, "④ …and only after the request (nothing sent without a report)")



def run_uc_request_chain_dry(c, timeout):
    """⭐ Urban Battle §6 L7 (dry) — uc-request-chain-dry: L7's cast with the one report the live rung cannot get yet FAKED — a SPOT
    IndirectFire from Blue Squad 1 at the tubes (📄 NOTE_Behaviors_To_Backend_Launch_Not_Heard_Live.md) — so the rest runs live: ① the
    platoon asks the HQ for FireSupport (CE-2310) · ② the HQ sends its free AH-64 on an armed search of the point (CE-2312, CE-2311),
    not before ① · ③ the Apache flies to the point."""
    ids = ids_by_name()
    hq, heli, pl, sq1 = ids.get("Blue HQ"), ids.get("Apache"), ids.get("Blue Platoon"), ids.get("Blue Squad 1")
    tube = ids.get("Mortar Tube 1")
    if not c.ok(None not in (hq, heli, pl, sq1, tube), "the HQ, its Apache, the platoon and the mortar are loaded"):
        return
    at = position(tube)
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(hq) == "SupportCommand" and task_name(pl) == "PlatoonDefend", 30),
         f"the plays run (HQ {task_name(hq)}, platoon {task_name(pl)}, Apache {task_name(heli)})")
    c.ok(task_name(heli) in (None, "", "None"), f"the Apache waits: nothing is sent without a report (task {task_name(heli)})")
    r = call("POST", f"/entities/{sq1}/reports", {"kind": "Spot", "at": [at[0], at[1], 0], "count": 2, "size": "Team", "equipment": "IndirectFire"})
    c.ok(isinstance(r, dict) and (data(r) or {}).get("sent"), f"a faked SPOT IndirectFire from Blue Squad 1 ({json.dumps(data(r) or r)[:200]})")
    t0 = sim_time() or 0.0
    ask = wait_for(lambda: next((x for x in squad_reports(last=128) if x.get("kind") == "Request" and x.get("subtype") == "FireSupport"
                                 and x.get("from") == pl), None), 30)
    c.ok(ask is not None, f"① the platoon asks the HQ for FireSupport ({json.dumps(ask)[:220] if ask else None})")
    sent = wait_for(lambda: task_name(heli) == "AirArmedSearch", 30)
    o = _order(heli)
    print(f"    Apache order: {json.dumps(o)[:400]}")
    c.ok(sent, f"② the HQ sends the Apache on an armed search ({task_name(heli)}, {(sim_time() or 0) - t0:.1f} s after the SPOT)")
    p0 = position(heli)
    d0 = math.hypot(p0[0] - at[0], p0[1] - at[1]) if p0 else 0
    closer = wait_for(lambda: (p := position(heli)) and math.hypot(p[0] - at[0], p[1] - at[1]) < d0 - 300, min(timeout, 240))
    p1 = position(heli)
    c.ok(closer, f"③ the Apache flies to the point ({d0:.0f} m → {math.hypot(p1[0] - at[0], p1[1] - at[1]) if p1 else 0:.0f} m)")
    def its_shots():
        call("POST", "/perspective", {"name": "SimHost"})
        try:
            return [x for x in (data(call("GET", "/combat/shots?last=256")) or {}).get("shots") or [] if x.get("shooter") == heli]
        finally:
            call("POST", "/perspective", {"name": "Scenario"})
    crew = [ids.get("Mortar Man 1"), ids.get("Mortar Man 2"), ids.get("Mortar Ammo Bearer")]
    def health():
        return [(comps(m).get("Health") or {}).get("Current") for m in crew]
    # its rockets do not enter the shot log (only gun rounds do) — so an attack is its rounds OR the cell's losses
    fired = wait_for(lambda: its_shots() or any((h or 100) < 100 for h in health()), min(timeout, 300), every=2.0)
    hurt = health()
    print(f"    the Apache's memory: {json.dumps([(s_.get('networkId'), s_.get('anonymous')) for s_ in (data(call('GET', f'/entities/{heli}/memory')) or {}).get('slots', [])])[:300]} (crew {crew})")
    c.ok(fired, f"④ the Apache attacks what it finds there ({len(fired) if isinstance(fired, list) else 'rockets'}; targets {sorted({x.get('target') for x in fired or []})}; "
                f"crew health {hurt}; Apache task {task_name(heli)})")



def run_uc_a10_zu23_dry(c, timeout):
    """⭐ Urban Battle §6 L4 (dry) — uc-a10-zu23-dry: L7's cast + a ZU-23 beside the tubes and an A-10 on its orbit under the HQ. Two SPOTs
    faked (AirDefence at the ZU-23, then IndirectFire at the tubes): ① the platoon relays the air defence up and asks FireSupport
    (CE-2310) · ② the HQ sends the A-10 at the ZU-23 FIRST (HQ-7, CE-2312) · ③ the AH-64 waits while the air defence stays reported."""
    ids = ids_by_name()
    hq, heli, jet, pl, sq1 = ids.get("Blue HQ"), ids.get("Apache"), ids.get("A-10"), ids.get("Blue Platoon"), ids.get("Blue Squad 1")
    tube, zu = ids.get("Mortar Tube 1"), ids.get("ZU-23")
    if not c.ok(None not in (hq, heli, jet, pl, sq1, tube, zu), "the HQ, its Apache and A-10, the platoon, the mortar and the ZU-23 are loaded"):
        return
    at, aa = position(tube), position(zu)
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(hq) == "SupportCommand" and task_name(jet) == "FollowRoute", 30),
         f"the plays run (HQ {task_name(hq)}, A-10 {task_name(jet)} on its orbit, Apache {task_name(heli)})")
    call("POST", f"/entities/{sq1}/reports", {"kind": "Spot", "at": [aa[0], aa[1], 0], "count": 1, "size": "Individual", "equipment": "Vehicle, AirDefence"})
    wait_for(lambda: False, 4, every=1.0)
    call("POST", f"/entities/{sq1}/reports", {"kind": "Spot", "at": [at[0], at[1], 0], "count": 2, "size": "Team", "equipment": "IndirectFire"})
    ask = wait_for(lambda: next((x for x in squad_reports(last=128) if x.get("kind") == "Request" and x.get("subtype") == "FireSupport"
                                 and x.get("from") == pl), None), 30)
    c.ok(ask is not None, "① the platoon asks the HQ for FireSupport")
    relayed = next((x for x in squad_reports(last=128) if x.get("kind") == "Spot" and x.get("from") == pl and "AirDefence" in str(x.get("equipment"))), None)
    c.ok(relayed is not None, f"① …having relayed the air defence up ({json.dumps(relayed)[:200] if relayed else None})")
    struck = wait_for(lambda: task_name(jet) not in (None, "", "FollowRoute"), 30)
    o = _order(jet)
    print(f"    A-10 order: {json.dumps(o)[:400]}")
    c.ok(struck, f"② the HQ sends the A-10 at the air defence first (A-10 task {task_name(jet)})")
    wait_for(lambda: False, 15, every=1.0)
    c.ok(task_name(heli) in (None, "", "None"), f"③ the Apache waits while the air defence stays reported (task {task_name(heli)})")



def run_uc_reinforce_dry(c, timeout):
    """⭐ Urban Battle §6 L6 (dry) — uc-reinforce-dry: L7's cast + a Blue reserve squad under the HQ. Faked: Blue Squad 2's men are killed (the platoon
    is Red: one squad of two fighting) and an enemy element is SPOTted 100 m off ⇒ ① the platoon asks the HQ to be reinforced (CE-2310 HQ-5) · ② the HQ sends its free ground unit — the
    reserve, never the requester — to defend at the platoon (CE-2312 HQ-9)."""
    ids = ids_by_name()
    hq, pl, res = ids.get("Blue HQ"), ids.get("Blue Platoon"), ids.get("Blue Reserve")
    sq = [ids.get("Blue Squad 1"), ids.get("Blue Squad 2")]
    if not c.ok(None not in (hq, pl, res) and None not in sq, "the HQ, the platoon, its squads and the reserve are loaded"):
        return
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(hq) == "SupportCommand" and task_name(pl) == "PlatoonDefend", 30), f"the plays run (reserve {task_name(res)})")
    wait_for(lambda: False, 3, every=1.0)
    # the staff's strength is REAL (StaffComposerSystem.StrengthOf: fighting members / members) — so a squad is taken out of the fight
    call("POST", "/perspective", {"name": "CGF"})   # Health is the brain group's: CGF owns it
    try:
        for n in ("Leader", "Rifleman 1", "Rifleman 2", "Rifleman 3"):
            set_health(ids.get(f"Blue Squad 2 {n}"), 0)
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    call("POST", f"/entities/{sq[0]}/reports", {"kind": "Spot", "at": [8, 110, 0], "count": 6, "size": "SquadSize"})
    ask = wait_for(lambda: next((x for x in squad_reports(last=128) if x.get("kind") == "Request" and x.get("subtype") == "Reinforce"
                                 and x.get("from") == pl), None), 40)
    c.ok(ask is not None, f"① the platoon asks to be reinforced ({json.dumps(ask)[:220] if ask else None})")
    sent = wait_for(lambda: task_name(res) in ("SquadDefend", "PlatoonDefend"), 30)
    print(f"    reserve order: {json.dumps(_order(res))[:300]}")
    c.ok(sent, f"② the HQ sends the reserve to defend at the platoon (reserve task {task_name(res)}; platoon {task_name(pl)})")
    start = position(res)
    moved = wait_for(lambda: (p := position(res)) and start and math.hypot(p[0] - start[0], p[1] - start[1]) > 10, min(timeout, 120))
    c.ok(moved, f"② …and it goes (reserve at {position(res)})")



# ── Urban Battle §1 / §6 — the FLAGSHIP: uc-urban-battle (demo-city), unscripted, judged by what must hold whatever happens ────────
def run_uc_urban_battle(c, timeout):
    """⭐ Urban Battle §1 — uc-urban-battle (DESIGN_Urban_Battle_Demo.md §1, §6): a convoy (an APC and a Bradley, a squad in each) up Avenue E on
    a market day; an IED and its covert trigger man; an insurgent fire team up the street; a covert spotter and his mortar in the north-east; a
    ZU-23 beside it; the convoy's platoon under an HQ owning an AH-64, an A-10 on its orbit and a reserve squad. NOTHING is faked or timed. The
    fight decides the story; the check asserts only what must hold whatever happens: ① the charge goes off only with a vehicle in its kill zone ·
    ② no Blue round is aimed at a civilian · ③ nothing is sent without a report — every HQ order follows a REQUEST · ④ both sides fight ·
    ⑤ CE-2347 (R-2069) — after the contact the platoon's convoy task has the squads secure (SquadDefend), remount (SquadEmbark) and the
    convoy moves on (a vehicle back on FollowRoute). The timeline is printed: what happened, in what order."""
    ids = ids_by_name()
    need = ("Blue HQ", "Apache", "A-10", "Blue Reserve", "Blue Platoon", "Convoy APC", "Convoy IFV", "Squad A", "Squad B", "IED", "Trigger man", "Spotter")
    if not c.ok(all(ids.get(n) for n in need), f"the cast is loaded ({[n for n in need if not ids.get(n)] or 'all'})"):
        return
    hq, pl, ied = ids["Blue HQ"], ids["Blue Platoon"], ids["IED"]
    assets = {n: ids[n] for n in ("Apache", "A-10", "Blue Reserve")}
    vehicles = [ids["Convoy APC"], ids["Convoy IFV"]]
    blue = {v for k, v in ids.items() if k.startswith(("Squad A", "Squad B", "Blue Reserve", "Convoy", "Apache", "A-10"))}
    red = {v for k, v in ids.items() if k.startswith(("Insurgent", "Mortar", "ZU-23"))}
    civilians = {v for k, v in ids.items() if k.startswith("Civilian")}
    ied_at = position(ied)
    call("POST", "/sim/timescale", {"scale": 2})
    call("POST", "/sim/play", {})
    c.ok(wait_for(lambda: task_name(hq) == "SupportCommand" and task_name(pl) == "PlatoonConvoy" and task_name(vehicles[0]) == "FollowRoute", 30),
         "the plays run (the HQ, the platoon's convoy, the vehicles on the route)")
    log, last, det_seen, ied_ok, first_req = [], {}, 0, None, None
    def note(t, what):
        log.append(f"{t:7.1f}  {what}")
    end = time.time() + min(timeout, 900)
    while time.time() < end:
        t = sim_time() or 0.0
        dets = _detonations()
        for d in dets:
            if (d.get("tick"), d.get("seq"), d.get("shooter")) in last:
                continue   # the detonation log is a rolling window: key its entries, never index them
            last[(d.get("tick"), d.get("seq"), d.get("shooter"))] = True
            who = next((k for k, v in ids.items() if v == d.get("shooter")), d.get("shooter"))
            note(t, f"detonation by {who} at {[round(x) for x in d.get('burst') or []][:2]} ({d.get('warhead')})")
            if d.get("shooter") == ids["Trigger man"] and ied_ok is None:
                vs = [position(v) for v in vehicles]
                near = min((math.hypot(p[0] - ied_at[0], p[1] - ied_at[1]) for p in vs if p), default=999)
                ied_ok = near <= 15
                note(t, f"  …the IED: nearest vehicle {near:.1f} m from it")
        for r in squad_reports(last=256):
            key = (r.get("serial"), r.get("from"))
            if key in last or r.get("kind") in ("Sitrep", "Progress"):
                continue
            last[key] = True
            who = next((k for k, v in ids.items() if v == r.get("from")), r.get("from"))
            note(t, f"{r.get('kind')} {r.get('subtype') or ''} from {who} {r.get('equipment') or ''} at {[round(x) for x in (r.get('at') or [])][:2]}")
            if r.get("kind") == "Request" and r.get("from") == pl and first_req is None:
                first_req = t
        for n, a in assets.items():
            tk = task_name(a)
            if last.get(("task", n)) != tk:
                last[("task", n)] = tk
                note(t, f"{n}: {tk}")
                if tk not in (None, "", "None", "FollowRoute") and first_req is None:
                    last["sent-unasked"] = (n, tk, t)
        for v in [pl] + vehicles + [ids["Squad A"], ids["Squad B"]]:
            tk = task_name(v)
            name = next(k for k, x in ids.items() if x == v)
            if last.get(("task", name)) != tk:
                last[("task", name)] = tk
                at = position(v) if name.startswith("Convoy") else None
                note(t, f"{name}: {tk}" + (f" at {[round(x) for x in at][:2]}" if at else ""))
                # ⑤ CE-2347 — secure, then remount, then the convoy on the route again
                if name.startswith("Squad") and tk == "SquadDefend" and "secured" not in last:
                    last["secured"] = t
                elif name.startswith("Squad") and tk == "SquadEmbark" and "secured" in last and "remounted" not in last:
                    last["remounted"] = t
                elif name.startswith("Convoy") and tk == "FollowRoute" and "remounted" in last and "moved-on" not in last:
                    last["moved-on"] = t
        time.sleep(1.5)
    print("    timeline (sim s):")
    for line in log:
        print("      " + line)
    call("POST", "/perspective", {"name": "SimHost"})
    try:
        shots = (data(call("GET", "/combat/shots?last=1024")) or {}).get("shots") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    by = {"Blue": sum(1 for x in shots if x.get("shooter") in blue), "Red": sum(1 for x in shots if x.get("shooter") in red)}
    at_civ = [x for x in shots if x.get("shooter") in blue and x.get("target") in civilians]
    c.ok(ied_ok is not False, f"① the charge went off only with a vehicle in its kill zone ({'it did not go off' if ied_ok is None else 'yes'})")
    c.ok(not at_civ, f"② no Blue round aimed at a civilian ({len(at_civ)} of {by['Blue']})")
    c.ok("sent-unasked" not in last, f"③ nothing sent without a report ({last.get('sent-unasked') or 'every HQ order came after a REQUEST'})")
    c.ok(by["Blue"] > 0 and by["Red"] > 0, f"④ both sides fight (rounds: {by})")
    c.ok("moved-on" in last, "⑤ after the contact the squads secure, remount and the convoy moves on "
         f"(secured {last.get('secured')}, remounted {last.get('remounted')}, on the route again {last.get('moved-on')})")


# ── ⭐ AQ96 (CE-2319 / CE-2320) — how a soldier moves under fire: docs/blueprints/Architect_Question_96_Movement_Under_Fire.md ──────
def _under_fire_samples(men, seconds, every=0.25):
    """Each man's (t, position, stance, reflex phase, last near miss or hit) every `every` s for `seconds` of sim time."""
    out = {m: [] for m in men}
    end = (sim_time() or 0) + seconds
    while (sim_time() or 0) < end:
        t = sim_time() or 0
        for m in men:
            cs = comps(m)
            p = (cs.get("SimTransform") or {}).get("Position")
            rs = cs.get("RecentSenses") or {}
            fire = max(rs.get("NearMiss") or 0, rs.get("Hit") or 0)
            out[m].append((t, tuple(p) if p else None, (cs.get("StanceIntent") or {}).get("TargetStance"),
                           (cs.get("UnderFireMove") or {}).get("Phase"), fire if fire > 0 else None))
        time.sleep(every)
    return out


def _under_fire_judge(c, name, rows, alone):
    """The AQ96 judgement over one man's samples; returns (drops, rises, rises under fire)."""
    first_fire = next((r[0] for r in rows if r[4] is not None), None)
    if not c.ok(first_fire is not None, f"{name}: rounds pass near him (a near miss or hit is recorded)"):
        return 0, 0, 0
    upright_moving, fastest, drops, rises, rises_under_fire, shifted_before_rise, still_when_down = 0, 0.0, 0, 0, 0, True, True
    prev, saw_shift, down_at = None, False, None
    for r in rows:
        if prev and r[1] and prev[1] and r[0] > prev[0]:
            v = flat(r[1], prev[1]) / (r[0] - prev[0])
            if r[0] >= first_fire + 1.0 and r[3] == "Moving":
                fastest = max(fastest, v)
                if v > 0.5 and r[2] == "Standing": upright_moving += 1
            if r[3] == "Down" and prev[3] == "Down" and down_at is not None and r[0] - down_at > 1.0 and v > 0.6:
                still_when_down = False   # (the first second down is his braking slide)
        # a drop is Moving → any down phase: covered, he can go down and start his side step within one sample
        if prev and prev[3] == "Moving" and r[3] in ("Down", "Shifting", "Crawling"): drops += 1; saw_shift = r[3] == "Shifting"; down_at = r[0]
        if r[3] == "Shifting": saw_shift = True
        if prev and prev[3] in ("Down", "Shifting") and r[3] == "Moving":
            rises += 1
            if not saw_shift: shifted_before_rise = False
            if r[4] is not None and r[0] - r[4] < 2.5: rises_under_fire += 1
        prev = r
    print(f"    {name}: first fire at {first_fire}, drops {drops}, rises {rises} ({rises_under_fire} under fire), "
          f"fastest on the move {fastest:.1f} m/s, upright-and-moving samples under fire {upright_moving}")
    c.ok(upright_moving <= 1, f"{name}: ① under fire he never runs upright ({upright_moving} samples)")
    c.ok(fastest >= 2.5, f"{name}: ① under fire he rushes (fastest {fastest:.1f} m/s ≥ 2.5; the order said 2)")
    if alone:   # in a squad not every man is near-missed while he is out in the open; the squad's count is judged instead
        c.ok(drops >= 1, f"{name}: ② a near miss in the open drops him (drops {drops})")
    c.ok(still_when_down, f"{name}: ② down, he stops")
    c.ok(shifted_before_rise, f"{name}: ④ he never rises where he dropped (a side step before each rise)")
    if alone:
        c.ok(rises_under_fire == 0, f"{name}: ③ alone, he never rises while still fired on ({rises_under_fire} rises within 2.5 s of a round)")
    return drops, rises, rises_under_fire


def run_uf_alone_under_fire(c, timeout):
    ids = ids_by_name()
    runner = ids.get("Runner")
    if not c.ok(runner is not None and ids.get("Hostile 1") is not None, "the runner and the hostiles are loaded"):
        return
    call("POST", "/sim/play", {})
    # pinned first: two immortal riflemen never stop firing, so a lone man stays down (measured live: a round a second for 50 s)
    rows = _under_fire_samples([runner], 15)[runner]
    # then the fire STOPS — both run dry — and he must wait the lapse, step aside, rise and rush on
    stopped = sim_time() or 0
    for h in ("Hostile 1", "Hostile 2"):
        call("POST", f"/entities/{ids[h]}/component", {"networkId": ids[h], "componentType": "WeaponState", "patch": {"Ammo": 0}})
    print(f"    the hostiles run dry at {stopped:.1f}")
    rows += _under_fire_samples([runner], 25)[runner]
    drops, rises, _ = _under_fire_judge(c, "Runner", rows, alone=True)
    pinned = [r for r in rows if r[0] < stopped]
    c.ok(pinned and all(r[3] in ("Down", "Crawling") for r in pinned if r[0] > (pinned[0][0] + 6)),
         "Runner: ③ pinned by steady fire he stays down (no rise before the fire stops)")
    up = next((r[0] for r in rows if r[0] > stopped and r[3] == "Moving"), None)
    c.ok(up is not None and 2.5 <= up - stopped <= 8.0,
         f"Runner: ④ after the fire stops he waits the lapse, steps aside and rises ({None if up is None else round(up - stopped, 1)} s after)")
    after = [r for r in rows if up is not None and r[0] > up + 1.0 and r[3] == "Moving" and r[1]]
    v = max((flat(b[1], a[1]) / (b[0] - a[0]) for a, b in zip(after, after[1:]) if b[0] > a[0]), default=0.0)
    c.ok(v >= 2.5, f"Runner: ④ and rushes on, crouched ({v:.1f} m/s)")


def run_uf_squad_under_fire(c, timeout):
    ids = ids_by_name()
    sq = ids.get("Squad")
    names = ["Leader", "Rifleman 1", "Rifleman 2", "Rifleman 3", "Rifleman 4", "Rifleman 5"]
    men = [ids.get(n) for n in names]
    if not c.ok(sq is not None and None not in men, "the squad and its six men are loaded"):
        return
    call("POST", "/sim/play", {})
    samples = _under_fire_samples(men, 75)
    drops = rises = covered = 0
    for n, m in zip(names, men):
        rows = samples[m]
        if not any(r[4] is not None for r in rows):
            print(f"    {n}: never fired on")
            continue
        d, r, u = _under_fire_judge(c, n, rows, alone=False)
        drops += d; rises += r; covered += u
    c.ok(drops >= 1, f"the squad: ② movers drop under fire ({drops} drops)")
    c.ok(covered >= 1, f"the squad: ③ a covered mover rises with rounds still coming ({covered} of {rises} rises)")
    hs = (maneuver(sq) or {}).get("history", [])
    c.ok(len([h for h in hs if "Rush" in (h.get("phaseName") or "")]) >= 2,
         f"the squad: ⑤ the bounds go on ({[(h.get('t'), h.get('phaseName')) for h in hs][-6:]})")


# ── ⭐ AQ97 (CE-2322) — every lone soldier and civilian reacts by default; InertSop says "no response" ──────────────────────
def _task_of(nid):
    cur = (data(call("GET", f"/entities/{nid}/order")) or {}).get("current") or {}
    return cur.get("behavior"), cur.get("origin")


def _watch_tasks(names, ids, seconds, every=0.5):
    """Each man's (t, task, origin, position, civilian mode, fear) over `seconds` of sim time."""
    out = {n: [] for n in names}
    end = (sim_time() or 0) + seconds
    while (sim_time() or 0) < end:
        t = sim_time() or 0
        for n in names:
            task, origin = _task_of(ids[n])
            cs = comps(ids[n])
            civ = cs.get("CivilianState") or {}
            p = (cs.get("SimTransform") or {}).get("Position")
            out[n].append((t, task, origin, tuple(p) if p else None, civ.get("Mode"), civ.get("Fear"),
                           (cs.get("StanceIntent") or {}).get("TargetStance")))
        time.sleep(every)
    return out


def run_df_lone_soldier_reflex(c, timeout):
    ids = ids_by_name()
    if not c.ok(all(ids.get(n) for n in ("Reflex", "Inert", "Hostile")), "Reflex, Inert and the hostile are loaded"):
        return
    call("POST", "/sim/play", {})
    rows = _watch_tasks(["Reflex", "Inert"], ids, 40)
    reflex, inert = rows["Reflex"], rows["Inert"]
    # ⭐ AQ98 A (R-2070) — the cover reaction is FightFromCover now (TakeCover before 2026-10-21)
    reacted = next((r for r in reflex if r[1] in ("FightFromCover", "TakeCover", "FallBack") and r[2] == "Reaction"), None)
    print(f"    Reflex tasks: {sorted({(r[1], r[2]) for r in reflex}, key=str)}")
    print(f"    Inert tasks:  {sorted({(r[1], r[2]) for r in inert}, key=str)}")
    c.ok(reacted is not None, f"(1) Reflex reacts by himself: a cover reaction ({reacted and reacted[1]}, {reacted and round(reacted[0], 1)} s)")
    moved = max((flat(r[3], reflex[0][3]) for r in reflex if r[3] and reflex[0][3]), default=0.0)
    c.ok(moved > 2.0, f"(1) and he leaves his spot ({moved:.1f} m)")
    c.ok(all(r[1] in (None, "None") for r in inert), "(2) Inert never gets a task, whatever comes near him")
    still = max((flat(r[3], inert[0][3]) for r in inert if r[3] and inert[0][3]), default=0.0)
    c.ok(still < 1.0, f"(2) and never moves ({still:.1f} m)")


def run_df_civilian_ordered(c, timeout):
    ids = ids_by_name()
    if not c.ok(all(ids.get(n) for n in ("Ordered", "Inert civilian", "Target", "Hostile")), "the two civilians, the target and the hostile are loaded"):
        return
    call("POST", "/sim/play", {})
    rows = _watch_tasks(["Ordered", "Inert civilian"], ids, 45)
    o, i = rows["Ordered"], rows["Inert civilian"]
    print(f"    Ordered: tasks {sorted({(r[1], r[2]) for r in o}, key=str)}, modes {sorted({r[4] for r in o if r[4]}, key=str)}, "
          f"fear max {max((r[5] or 0) for r in o):.2f}")
    print(f"    Inert civilian: tasks {sorted({(r[1], r[2]) for r in i}, key=str)}, fear max {max((r[5] or 0) for r in i):.2f}")
    c.ok(max((r[5] or 0) for r in o) > 0.3 and max((r[5] or 0) for r in i) > 0.3,
         "(1) the gunfire frightens both (fear above the alarm, though neither runs his routine HSM)")
    react = [r for r in o if r[1] == "CivilianReact" and r[2] == "Reaction"]
    c.ok(bool(react), "(2) Ordered reacts on top of his order: CivilianReact")
    c.ok(any(r[4] in ("Alarmed", "Panic", "Cower", "Shelter", "Inside") for r in react) or any(r[6] in ("Crouched", "Prone") for r in react),
         f"(2) he stops and gets down, or flees ({sorted({(r[4], r[6]) for r in react}, key=str)})")
    c.ok(all(r[1] == "Idle" for r in i if r[1]), "(3) Inert civilian keeps standing on his order (Idle) however frightened")



# ── AQ102 §5 (CE-2355) — responsive fighting reflexes: duck, find him again, grenades, flank ─────────────────────────────────────
#   docs/blueprints/Architect_Question_102_Responsive_Fighting_Reflexes.md. Every judge PRINTS its number first (the baseline is the
#   point of writing them before the build), then asserts it.

def _rf_sample(names, ids, seconds, every=0.25, extra=None):
    """Each man's samples over `seconds` of sim time: (t, position, stance, ammo, task, memory {networkId: lastSeenTick})."""
    out = {n: [] for n in names}
    end = (sim_time() or 0) + seconds
    while (sim_time() or 0) < end:
        t = sim_time() or 0
        for n in names:
            cs = comps(ids[n])
            p = (cs.get("SimTransform") or {}).get("Position")
            w = cs.get("WeaponState") or {}
            task, _ = _task_of(ids[n])
            mem = {}
            for x in (data(call("GET", f"/entities/{ids[n]}/memory")) or {}).get("slots") or []:
                if x.get("networkId") is not None and not x.get("anonymous"):
                    mem[x["networkId"]] = (x.get("lastSeenTick") or 0, x.get("modalities") or 0)
            out[n].append((t, tuple(p) if p else None, (cs.get("StanceIntent") or {}).get("TargetStance"), w.get("Ammo"), task, mem,
                           cs.get("LookAtChannel")))
        if extra: extra(t)
        time.sleep(every)
    return out


def _rf_idle_gap(rows):
    """The longest stretch (s) in which the man neither moved 1 m from where it began nor fired."""
    best, start = 0.0, 0
    for i in range(1, len(rows)):
        t0, p0, _, a0, *_ = rows[start]
        t, p, _, a, *_ = rows[i]
        if (p and p0 and flat(p, p0) > 1.0) or (a is not None and a0 is not None and a < a0):
            start = i
            continue
        best = max(best, t - t0)
    return best


def _rf_fire_spots(rows, cluster=1.0):
    """Burst starts (s) and the spot (cluster index) each was fired from, in order."""
    spots, seq, last_fire = [], [], -9.0
    for i in range(1, len(rows)):
        t, p, _, a, *_ = rows[i]
        a0 = rows[i - 1][3]
        if a is None or a0 is None or a >= a0 or not p:
            continue
        if t - last_fire > 1.5:   # a new burst
            k = next((j for j, q in enumerate(spots) if flat(q, p) <= cluster), None)
            if k is None:
                spots.append(p)
                k = len(spots) - 1
            seq.append(k)
        last_fire = t
    return spots, seq


def _rf_bursts_by(shooters):
    call("POST", "/perspective", {"name": "SimHost"})
    try:
        ds = (data(call("GET", "/combat/detonations?last=64")) or {}).get("detonations") or []
    finally:
        call("POST", "/perspective", {"name": "Scenario"})
    return [d for d in ds if d.get("shooter") in shooters]


def _rf_duel(c, names, cars_of, seconds):
    ids = ids_by_name()
    if not c.ok(all(ids.get(n) for n in names), f"{', '.join(names)} are loaded"):
        return None, None
    nets = {n: ids[n] for n in names}
    call("POST", "/sim/play", {})
    rows = _rf_sample(names, ids, seconds)
    for n in names:
        gap = _rf_idle_gap(rows[n])
        spots, seq = _rf_fire_spots(rows[n])
        repeats = sum(1 for i in range(1, len(seq)) if seq[i] == seq[i - 1])
        start = rows[n][0][1]
        far = max((flat(r[1], start) for r in rows[n] if r[1] and start), default=0.0)
        print(f"    {n}: longest idle-hidden {gap:.1f} s · {len(seq)} bursts from {len(spots)} spots, {repeats} repeated · "
              f"furthest from start {far:.1f} m · tasks {sorted({r[4] for r in rows[n] if r[4]}, key=str)}")
        # ⭐ AQ102 — the task timeline: each change with what he remembers of the other (ticks since seen, modalities) and where he is
        other = [m for m in names if m != n]
        last = None
        for t, p, stance, ammo, task, mem, _ in rows[n]:
            if task == last:
                continue
            last = task
            seen = {k: v for k, v in mem.items() if other and str(k) == str(nets.get(other[0]))}
            print(f"      t={t:6.1f} {task or '-':<16} stance={stance} at={tuple(round(v, 1) for v in p) if p else None} memory-of-{other[0] if other else '?'}={seen or 'NONE'}")
        c.ok(gap <= 12.0, f"(1) {n} never idles hidden more than 12 s ({gap:.1f} s)")
        c.ok(len(spots) >= 2 and repeats == 0, f"(2) {n} fires from ≥ 2 spots, never the same twice running ({len(spots)} spots, {repeats} repeats)")
    moved = [n for n in names if max((flat(r[1], rows[n][0][1]) for r in rows[n] if r[1] and rows[n][0][1]), default=0) > 6.0]
    c.ok(bool(moved), f"(3) at least one flank or firing-position move (> 6 m from the start: {moved})")
    closest = min((flat(a[1], b[1]) for a, b in zip(rows[names[0]], rows[names[1]]) if a[1] and b[1]), default=99.0)
    nades = _rf_bursts_by(set(nets.values()))
    print(f"    closest approach {closest:.1f} m · grenade bursts by the two: {len(nades)}")
    if closest <= 25.0:
        c.ok(bool(nades), f"(4) within 25 m a grenade goes over cover ({len(nades)} bursts)")
    else:
        print("    (4) never within 25 m — the grenade judge does not apply this run")
    return rows, ids


def run_rf_car_duel(c, timeout):
    _rf_duel(c, ["Blue", "Red"], None, 90)


def run_rf_house_obstacle(c, timeout):
    rows, ids = _rf_duel(c, ["Blue", "Red"], None, 120)
    if not rows:
        return
    house = (99.5, 111.5, 98.5, 109.5)   # House A's footprint on bt-range (the window positions bound it)
    for n in ("Blue", "Red"):
        inside = any(r[1] and house[0] <= r[1][0] <= house[1] and house[2] <= r[1][1] <= house[3] for r in rows[n])
        crossed = any(r[1] and house[2] <= r[1][1] <= house[3] and not (house[0] <= r[1][0] <= house[1]) for r in rows[n])
        print(f"    {n}'s route: {'THROUGH the house' if inside else 'AROUND it' if crossed else 'stayed on his side'}")
    c.ok(any(any(r[1] and house[2] <= r[1][1] <= house[3] for r in rows[n]) for n in ("Blue", "Red")),
         "(2) one of them flanks past House A (through or around)")


def run_rf_car_squads(c, timeout):
    ids = ids_by_name()
    blue = [f"Blue {i}" for i in range(1, 6)]
    red = [f"Red {i}" for i in range(1, 6)]
    if not c.ok(all(ids.get(n) for n in blue + red), "both squads are loaded"):
        return
    call("POST", "/sim/play", {})
    rows = _rf_sample(blue + red, ids, 120, every=0.5)
    for side, men, foes in (("Blue", blue, red), ("Red", red, blue)):
        moved = [n for n in men if max((flat(r[1], rows[n][0][1]) for r in rows[n] if r[1] and rows[n][0][1]), default=0) > 8.0]
        held = [n for n in men if n not in moved]
        seen, total = 0, 0
        for n in moved:
            for i in range(1, len(rows[n])):
                total += 1
                me = ids[n]
                if any((rows[f][i][5].get(me, (0, 0))[0]) > (rows[f][i - 1][5].get(me, (0, 0))[0]) for f in foes):
                    seen += 1
        hidden = 1.0 - (seen / total) if total else 0.0
        near = min((flat(rows[m][-1][1], rows[f][-1][1]) for m in moved for f in foes if rows[m][-1][1] and rows[f][-1][1]), default=99.0)
        spent = sum((rows[n][0][3] or 0) - (rows[n][-1][3] or 0) for n in men if rows[n][0][3] is not None and rows[n][-1][3] is not None)
        print(f"    {side}: movers {moved} · held {held} · movers hidden {hidden:.0%} of samples · closest mover-enemy at the end {near:.1f} m"
              f" · rounds fired {spent} · tasks {sorted({r[4] for n in men for r in rows[n] if r[4]}, key=str)}")
    blue_moved = [n for n in blue if max((flat(r[1], rows[n][0][1]) for r in rows[n] if r[1] and rows[n][0][1]), default=0) > 8.0]
    red_moved = [n for n in red if max((flat(r[1], rows[n][0][1]) for r in rows[n] if r[1] and rows[n][0][1]), default=0) > 8.0]
    c.ok(any(0 < len(m) < 5 for m in (blue_moved, red_moved)), "(1) a side holds a base of fire while others move")
    c.ok(any(len(m) > 0 for m in (blue_moved, red_moved)), "(3) someone manoeuvres at all")


def run_rf_wall_hugger(c, timeout):
    ids = ids_by_name()
    reds = ["Red 1", "Red 2", "Red 3"]
    if not c.ok(all(ids.get(n) for n in reds + ["Shooter", "Hugger"]), "the three defenders, the Shooter and the Hugger are loaded"):
        return
    blue = ids["Hugger"]
    call("POST", "/sim/play", {})
    rows = _rf_sample(reds + ["Shooter", "Hugger"], ids, 90)
    wall = (130, 159.2)
    at_wall = rows["Hugger"][0][0]
    print(f"    Hugger at {rows['Hugger'][-1][1]} · Shooter tasks {sorted({r[4] for r in rows['Shooter'] if r[4]}, key=str)}")
    # (1) a defender ducks within 2 s of Blue's shots
    sh = rows["Shooter"]
    blue_fire = [sh[i][0] for i in range(1, len(sh)) if sh[i][3] is not None and sh[i - 1][3] is not None and sh[i][3] < sh[i - 1][3]]
    ducks = 0
    for n in reds:
        for i in range(1, len(rows[n])):
            t = rows[n][i][0]
            lower = rows[n][i][2] in ("Crouched", "Prone") and rows[n][i - 1][2] in (None, "Standing")
            if lower and any(0 <= t - bt <= 2.0 for bt in blue_fire):
                ducks += 1
    print(f"    the Shooter fired in {len(blue_fire)} samples · defender ducks within 2 s of it: {ducks} · stances "
          f"{ {n: sorted({str(r[2]) for r in rows[n]}) for n in reds} }")
    c.ok(ducks > 0, f"(1) a defender shot at ducks within 2 s ({ducks})")
    # (2) one defender moves to a new opening that sees Blue once he is under the wall
    movers = []
    if at_wall is not None:
        for n in reds:
            # ⭐ CE-2359 — the Hugger stands at the wall from the start: measure from once the posts are manned (SquadDefend moves, then
            #   posts — 📐 the first measure from t=0 counted every man's walk to his post as a hunt)
            settled = at_wall + 20.0
            before = next((r[1] for r in rows[n] if r[0] >= settled and r[1]), None)
            later = [r for r in rows[n] if r[0] >= settled + 2 and r[1] and before]
            # ⭐ CE-2359 — beyond his post's room (SquadDefendPlay.PostRadius 4 m): peeking between the windows of his own room is not a hunt
            far = max((flat(r[1], before) for r in later), default=0.0)
            if far > 4.5:
                seen_after = any(later[i][5].get(blue, (0, 0))[0] > later[i - 1][5].get(blue, (0, 0))[0] for i in range(1, len(later)))
                movers.append((n, seen_after))
            print(f"      {n}: furthest from his manned post (t ≥ {settled:.0f} s) {far:.1f} m")
    print(f"    defenders who moved beyond their room (and then saw the Hugger): {movers}")
    c.ok(any(s for _, s in movers), "(2) a defender moves to an opening that sees the Hugger")
    c.ok(len(movers) <= 1, f"(2) only ONE defender hunts ({len(movers)})")
    nades = [d for d in _rf_bursts_by({ids[n] for n in reds}) if flat(d.get("burst") or [0, 0, 0], wall) <= 8.0]
    c.ok(bool(nades), f"(3) a grenade is dropped at the Hugger ({len(nades)})")
    # (4) the look: LookAtEntity (action 11) in a sample where the defender saw NEITHER Friend ⇒ his eyes follow a man he cannot see
    shooter = ids["Shooter"]
    tracked = 0
    for n in reds:
        for i in range(1, len(rows[n])):
            look = rows[n][i][6] or {}
            if look.get("ActiveAction") != 11:
                continue
            seen = any(rows[n][i][5].get(x, (0, 0))[0] > rows[n][i - 1][5].get(x, (0, 0))[0] for x in (blue, shooter))
            if not seen:
                tracked += 1
    print(f"    samples where a defender's look followed someone he did not see: {tracked}")
    c.ok(tracked == 0, f"(4) no look follows a man while he is out of sight ({tracked})")


def run_rf_grenade_reaction(c, timeout):
    ids = ids_by_name()
    blues = [f"Blue {i}" for i in range(1, 5)]
    if not c.ok(all(ids.get(n) for n in blues + ["Thrower"]), "the Thrower and Blue 1-4 are loaded"):
        return
    call("POST", "/sim/play", {})
    rows = _rf_sample(blues + ["Thrower"], ids, 12, every=0.2)
    dets = _rf_bursts_by({ids["Thrower"]} | {ids[n] for n in blues})
    if not c.ok(bool(dets), f"the grenade burst ({len(dets)})"):
        return
    d = dets[0]
    b = d.get("burst") or [0, 0, 0]
    print(f"    burst at {b} by {'a Blue man (thrown BACK)' if d.get('shooter') != ids['Thrower'] else 'the Thrower'}")
    safe = 0
    for n in blues:
        e = effect_on(d, ids[n])
        far = e is None
        prone = e is not None and e.get("stance") == "Prone"
        print(f"    {n}: {'out of reach' if far else e.get('stance')} · exposure {e and e.get('fragmentExposure')} · moved "
              f"{max((flat(r[1], rows[n][0][1]) for r in rows[n] if r[1] and rows[n][0][1]), default=0):.1f} m")
        safe += 1 if (far or prone) else 0
        # ⭐ CE-2360 — his task timeline: did the reflex start, and when did it hand the task back?
        last, line = None, []
        for t, p, stance, _a, task, _m, _l in rows[n]:
            if (task, stance) != last:
                last = (task, stance)
                line.append(f"t={t:.1f} {task or '-'}/{stance}")
        print(f"      {' · '.join(line[:10])}")
    c.ok(safe == len(blues), f"(1) every Blue man is out of reach or prone at the burst ({safe}/{len(blues)})")
    c.ok(d.get("shooter") != ids["Thrower"] or flat(b, (280, 365)) < flat(b, (280, 380)),
         "(2) one man throws it back (the burst is the Blue man's, or nearer the Thrower)")

SCENARIOS = {"ua-posture": run_posture, "ua-threat-ranking": run_threat_ranking,
             # ⭐ CE-2233 CIV-0 — the neutral safety gate (Civilians §5c)
             "ua-civ-neutral-gate": run_civ_neutral_gate, "ua-danger-crossing": run_danger_crossing,
             # ⭐ CE-2235 CIV-1 — the civilian HSM: routine, alarm, casualty (Civilians §5c, §5d)
             "ua-civ-routine-alarm": run_civ_routine_alarm,
             # ⭐ CE-2236 CIV-2 — panic, shelter and cower (Civilians §5c, §5e)
             "ua-civ-shelter": run_civ_shelter, "ua-civ-cower": run_civ_cower,
             # ⭐ CE-2237 CIV-3 — crowds: onlookers gather, fear spreads (Civilians §5c, §5f)
             "ua-civ-gathering": run_civ_gathering, "ua-civ-contagion": run_civ_contagion,
             # ⭐ CE-2240 CIV-3 — the protest and its gestures (Civilians §5c, §5g)
             "ua-civ-protest": run_civ_protest,
             # ⭐ AW-20 — a neutral in the line or the spread holds the shot (Civilians §5c, §5h)
             "ua-armed-shield": run_armed_shield,
             # ⭐ CE-2238 CIV-4 — the riot, the blow, the side flip (Civilians §5c, §5i)
             "ua-civ-melee": run_civ_melee,
             # ⭐ CE-2238 part 2 CIV-4 — the stone (Civilians §5c, §5i.2)
             "ua-civ-riot-stone": run_civ_riot_stone,
             # ⭐ CE-2241 ① CV-13a — a covered hostile raises his hands, the AI holds (Civilians §5c, §5h.3)
             "ua-civ-surrender": run_civ_surrender,
             # ⭐ CE-2241 ① AW-5/AW-21 — a soldier lays his rifle down, hands up, then the feint (Civilians §5c, §5h.5)
             "ua-armed-surrender": run_armed_surrender,
             # ⭐ CE-2239 A6 CV-8/CV-11 — the force level and the police line (Civilians §5c, §5j)
             "ua-civ-police-line": run_civ_police_line,
             # ⭐ CE-2238 part 3 AW-8/AW-9 — a lying throwable picked up, the canister thrown back (Civilians §5c, §5i.3)
             "ua-armed-gas-throwback": run_armed_gas_throwback,
             # ⭐ CE-2238 part 4 AW-11/AW-16 — drawing is not the act, aiming at the forces is (Civilians §5c, §5h.7)
             "ua-armed-draw-aim": run_armed_draw_aim,
             # ⭐ CE-2238 part 4 AW-22 — the witness engages the hidden rifleman, the other patrol sees a civilian (Civilians §5c, §5h.7b)
             "ua-armed-blend-back": run_armed_blend_back,
             # ⭐ CE-2239 part 3 B2 — the escalation ladder: warn, then non-lethal, then lethal; a Warn ceiling holds every round (Civilians §5c, §5k)
             "ua-civ-escalation": run_civ_escalation, "ua-civ-escalation-warn": run_civ_escalation_warn,
             "ua-armed-self-defence": run_armed_self_defence, "ua-armed-celebration": run_armed_celebration,
             # ⭐ CE-2238 part 5 B5 — the bomber (Civilians §5c, §5l)
             "ua-armed-bomber": run_armed_bomber,
             # ⭐ CE-2241 ② B7 CV-14 — the security forces provoke a crowd; a fraction riots (Civilians §5c, §5m)
             "ua-civ-provocation": run_civ_provocation,
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
             # ⭐ CE-2262 SL-0 — the first nested hierarchy: a platoon of squads (Building Clearing §7)
             "sl0-platoon-march": run_sl0_platoon_march,
             "cc-platoon-bound": run_cc_platoon_bound,
             # ⭐ CE-2298 LD-5 — the look-direction demos (Look Direction §3.4)
             "ld-sentry-surprise": run_ld_sentry_surprise,
             "ld-sentry-surprise-360": run_ld_sentry_surprise_360,
             # ⭐ AQ96 (CE-2319 / CE-2320) — how a soldier moves under fire, alone and in a squad
             "uf-alone-under-fire": run_uf_alone_under_fire,
             "uf-squad-under-fire": run_uf_squad_under_fire,
             # ⭐ AQ97 (CE-2322) — default reflexes; InertSop is the explicit none
             "df-lone-soldier-reflex": run_df_lone_soldier_reflex,
             "df-civilian-ordered": run_df_civilian_ordered,
             # ⭐ AQ102 §5 (CE-2355) — responsive fighting reflexes, written first as a baseline
             "rf-car-duel": run_rf_car_duel, "rf-car-squads": run_rf_car_squads, "rf-house-obstacle": run_rf_house_obstacle,
             "rf-wall-hugger": run_rf_wall_hugger, "rf-grenade-reaction": run_rf_grenade_reaction,
             # ⭐ CE-2161 — the D3 Bound choice over open ground (CE-3081)
             "ua-squad-bound-open-ground": run_squad_bound_open_ground,
             # ⭐ CE-2173 slice 5 — a plane bombs a point (Air Operations §9c)
             "ua-air-strike": run_air_strike,
             "ua-air-attack": run_air_attack,
             "ua-air-defence": run_air_defence,
             "ua-tank-corner": run_tank_corner,
             "ua-javelin-lock": run_javelin_lock,
             "ua-technical-scoot": run_technical_scoot,
             "ua-rpg-window": run_rpg_window,
             "ua-weapon-systems": run_weapon_systems,
             "ua-weather-regions": run_weather_regions,
             "ua-ifv-standoff": run_ifv_standoff,
             "ua-tank-ammo": run_tank_ammo,
             # ⭐ CE-2230 H-3 — the hull-down hill attack on the ridge (hill-attack DESIGN §0, R-2022)
             "ua-hill-attack": run_hill_attack,
             # ⭐ CE-2176 IF-4 — mortars, artillery and the forward observer (Indirect Fire §6)
             "ua-indirect-fire": run_indirect_fire,
             "ua-shootdown-eject": run_shootdown_eject,
             "ua-weather-sensing": run_weather_sensing, "ua-night-sensing": lambda c, t: run_weather_sensing(c, t, night=True),
             # ⭐ CE-1118 H-2 — airborne insertion (Airborne Insertion §8)
             "ua-fast-rope-roof": run_fast_rope_roof,
             "ua-airborne-drop": run_airborne_drop,
             "sq-defend-house": run_sq_defend_house, "pc-defend-dry": run_pc_defend_dry, "pc-attack-dry": run_pc_attack_dry, "pc-defend-house": run_pc_defend_house, "uc-convoy-hit": run_uc_convoy_hit, "uc-convoy-hit-ifv": run_uc_convoy_hit, "uc-request-chain": run_uc_request_chain, "uc-request-chain-dry": run_uc_request_chain_dry, "uc-a10-zu23-dry": run_uc_a10_zu23_dry, "uc-reinforce-dry": run_uc_reinforce_dry, "uc-urban-battle": run_uc_urban_battle, "ua-eject": run_eject, "ua-car-bomb": run_car_bomb, "ua-m2-engage": run_m2_engage, "ua-air-assault": run_air_assault, "ua-air-rescue": run_air_rescue, "ua-drone-shelter": run_drone_shelter}


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
        # the scenario's own statement of what it shows and how to judge it (header.description — the backend handoff of 2026-10-20)
        try:
            with open(os.path.join(REPO, "scenarios", a.scenario, "scenario.json")) as fh:
                desc = (json.load(fh).get("header") or {}).get("description")
            if desc: print(f"   {desc}")
        except (OSError, ValueError):
            pass
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
