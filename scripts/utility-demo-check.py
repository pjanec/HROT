#!/usr/bin/env python3
"""Utility AI demo scenarios — live acceptance over the cluster debug HTTP API (CE-3069).

Each scenario is a demo AND an E2E test: it loads, runs, reads the unit's utility decisions where they are made (the
Brain, perspective "Scenario") with GET /entities/{id}/utility, and exits 0 on PASS, 1 on FAIL.

    HROT_DEBUG_API_PORT=8111 xvfb-run -a dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode all
    python3 scripts/utility-demo-check.py ua-posture
    python3 scripts/utility-demo-check.py --launch ua-threat-ranking   # starts and stops its own fresh cluster

Several scenarios may run in one cluster process (CE-295 fixed); the script verifies the loaded cast. ⚠ CE-3075: a
scenario naming no terrain keeps the previous terrain — use --launch for those.

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
            return json.loads(r.read())
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
    """Start a fresh ClusterRunner --mode all (CE-295: one live load per process); returns the process."""
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

    # Hysteresis: Health held still ⇒ the winner does not flicker.
    _, d0 = winner_of(rifle)
    time.sleep(5)
    _, d1 = winner_of(rifle)
    c.ok(d0 and d1 and d0.get("switchCount") == d1.get("switchCount"),
         f"no flicker while Health is held (switchCount {d0 and d0.get('switchCount')} → {d1 and d1.get('switchCount')})")
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


SCENARIOS = {"ua-posture": run_posture, "ua-threat-ranking": run_threat_ranking}


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
