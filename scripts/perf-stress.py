#!/usr/bin/env python3
"""CE-2408 — the STRESS gate: ~1000 thinking soldiers + a handful of objectives + N SUMO cars on demo-city, one node per process.

User (2026-11-05): "we need artificial stress test with 1000 thinking entities and with some objectives and around 5000 sumo cars to set a
real target - 30 hz frames to keep looking for improvements."

    dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore
    python3 scripts/perf-stress.py                       # 1000 men, 5000 cars, 120 s of play after the fill
    python3 scripts/perf-stress.py --men 400 --cars 1000 --seconds 60
    python3 scripts/perf-stress.py --write-only          # just (re)write scenarios/perf-stress/scenario.json

⭐ The number that matters per node is its WORK PER FRAME — the sum of its systems' recent averages (the scheduler's own profiler, GET
/diagnostics/architecture): ≤ 33.3 ms ⇒ that node alone holds 30 Hz on one machine. On a shared VM the nodes compete for cores, so the
achieved frames per wall second is printed too but is NOT the target. Design: docs/DESIGN_Evaluation_Cost_And_Cadence.md §11.

Shape (deterministic): two sides on demo-city's streets (x / y ≈ 275, 400, 525, 650 inside the 800 m box), squads of 8 (TKB 302 squad +
TKB 2002 men, the Crossfire soldier) each on SquadDefend at a point on its street; high health so the fight stays a constant load; six
capture points (HoldArea) + two Counters + a kick-off Trigger — a FIXED rule set (no per-man score macros, so rules do not scale with men);
one traffic entity (TKB 8850, TrafficDemand) with CarSpawnPerStep raised so the cars fill in seconds, not minutes.
"""
import argparse, json, math, os, shutil, subprocess, sys, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib.util
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenario_json_readable import readable  # CE-4295 — params written as the objects they hold
import cluster_launch  # CE-3427 — node control in Python: the only path on Windows (no setsid / xvfb-run / /proc there)
_spec = importlib.util.spec_from_file_location("udc", os.path.join(os.path.dirname(os.path.abspath(__file__)), "utility-demo-check.py"))
udc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(udc)

NAME = "perf-stress"
STREETS = (275, 400, 525, 650)          # demo-city's street lines inside the playable 800 m box (see gen-showcase-crossfire.py)
POINTS = [(400, 400), (525, 400), (650, 400), (400, 525), (525, 525), (650, 525)]
TARGET_MS = 1000.0 / 30.0
NODES = (("orchestrator", 8300), ("cgf", 8301), ("simhost", 8302), ("traffic", 8304))


def rot(deg):
    yaw = math.radians(90 - deg)
    return [0, 0, round(math.sin(yaw / 2), 7), round(math.cos(yaw / 2), 7)]


class Ids:
    def __init__(self, prefix): self.prefix, self.n = prefix, 0
    def __call__(self):
        self.n += 1
        return f"{self.prefix}-0000-4000-8000-{self.n:012d}"


def scenario(men, cars, spawn_per_step):
    ids, ents = Ids("5e550000"), {}
    squads = max(2, men // 8)

    def unit(tkb, name, force, at, facing=0, commander=None, behavior=None, key=None):
        e = {"SimTransform": {"Position": list(at), "Rotation": rot(facing)}, "TkbIdentity": {"TkbType": tkb},
             "EntityInfo": {"Name": name, "ForceId": force}}
        if commander: e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": "Wingman"}
        if behavior: e["Behavior"] = {"Name": behavior[0], "Params": json.dumps(behavior[1]), "Origin": "Superior"}
        if key: e["RefKey"] = {"Value": key}
        return e

    teams = {}
    for force, name in (("Friend", "Blue"), ("Hostile", "Red")):
        g = ids(); teams[force] = g
        ents[g] = unit(305, f"{name} Force", force, (400, 400, 0), key=f"{name}Force")

    # squads spread along the street lines: Blue on the x-streets (vertical), Red on the y-streets (horizontal) — they meet at the crossings
    per_side = squads // 2
    for side, (force, name, facing) in enumerate((("Friend", "Blue", 0), ("Hostile", "Red", 90))):
        for s in range(per_side):
            street = STREETS[s % len(STREETS)]
            along = 60 + (s // len(STREETS)) * (680 / max(1, math.ceil(per_side / len(STREETS))))
            at = (street, along, 0) if side == 0 else (along, street, 0)
            goal = POINTS[(s + side) % len(POINTS)]
            sq = ids()
            ents[sq] = unit(302, f"{name} Squad {s + 1}", force, at, facing,
                            behavior=("SquadDefend", {"Position": [goal[0], goal[1], 0], "SearchRadius": 12, "Speed": 3}))
            for i in range(8):
                role = "SquadLeader" if i == 0 else ("MachineGunner" if i == 1 else "Wingman")
                off = (0, -7 + 2 * i) if side == 0 else (-7 + 2 * i, 0)
                ents[ids()] = {"SimTransform": {"Position": [at[0] + off[0], at[1] + off[1], 0], "Rotation": rot(facing)},
                               "Health": {"Current": 100000, "Max": 100000}, "TkbIdentity": {"TkbType": 2002},
                               "EntityInfo": {"Name": f"{name} {s + 1}-{i + 1}", "ForceId": force}, "TargetMemory": {"Entries": []},
                               "UnitSubordinate": {"CommanderGuid": sq, "Designation": role}}

    def objective(kind, title, params, force="Friend"):
        g = ids()
        ents[g] = {"TkbIdentity": {"TkbType": 8860}, "EntityInfo": {"Name": title, "ForceId": force},
                   "Behavior": {"Name": kind, "Params": json.dumps(params), "Origin": "Superior"}}
        return g

    blue = teams["Friend"]
    for k, (x, y) in enumerate(POINTS):
        objective("HoldArea", f"Point {k + 1}", {"common": {"title": f"Point {k + 1}", "subject": blue, "scored": False},
                  "area": {"circle": {"center": [x, y, 0], "radius": 15}}, "minMen": 1, "capture": True, "captureSeconds": 10})
    for force, side in (("Friend", "Blue"), ("Hostile", "Red")):
        objective("Counter", f"{side} losses", {"common": {"title": f"{side} losses", "subject": teams[force], "scored": False}, "start": 0,
                  "floor": -100000, "deaths": [{"side": force, "per": -1}]}, force=force)
    g = ids()
    ents[g] = {"TkbIdentity": {"TkbType": 8861}, "EntityInfo": {"Name": "Kick-off", "ForceId": "Friend"},
               "Behavior": {"Name": "Trigger", "Origin": "Superior", "Params": json.dumps({
                   "common": {"title": "Kick-off", "subject": blue}, "when": {"$type": "ScenarioTime", "fromSeconds": 3},
                   "do": [{"act": "Message", "args": {"toKind": "All", "text": "Stress run", "seconds": 5}}]})}}

    if cars > 0:
        ents[ids()] = {"TkbIdentity": {"TkbType": 8850}, "TrafficDemand": {"TargetCars": cars, "Seed": 1, "StepSeconds": 0.5,
                                                                         "TargetPedestrians": 0, "CarSpawnPerStep": spawn_per_step}}
    return {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
            "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "demo-city",
                       "description": f"What: the CE-2408 stress load — {squads * 8} soldiers in {squads} squads (two sides, SquadDefend on demo-city's "
                                      f"streets, high health), six capture points, two counters, a kick-off trigger and {cars} SUMO cars. "
                                      "Judge: each node's work per frame against 33.3 ms (30 Hz). Generated by scripts/perf-stress.py."},
            "entities": ents}


def write(men, cars, spawn):
    folder = os.path.join(udc.REPO, "scenarios", NAME)
    os.makedirs(folder, exist_ok=True)
    doc = scenario(men, cars, spawn)
    with open(os.path.join(folder, "scenario.json"), "w") as fh:
        fh.write(readable(json.dumps(doc, indent=1)))   # ⭐ CE-4295
    return folder, len(doc["entities"])


def node(cmd, *args):
    if cluster_launch.WINDOWS:
        return cluster_launch.node(cmd, *args)
    return subprocess.run([os.path.join(udc.REPO, "scripts/cluster-node.sh"), cmd, *map(str, args)], capture_output=True, text=True, timeout=200).stdout


def fetch(port, route, timeout):
    """GET with a long timeout (a full /entities dump from a node doing < 1 frame/s outlasts cluster-node.sh's 20 s) — the JSON reply text."""
    return cluster_launch.get(port, route, timeout=timeout)


def log_path(mode, port):
    return cluster_launch.log_path(mode, port) if cluster_launch.WINDOWS else f"/tmp/n-{mode}-{port}.log"


def node_pid(port):
    """The node's process id: the launcher's own pid on Windows; on Linux the dotnet process whose environment serves PORT (/proc)."""
    if cluster_launch.WINDOWS:
        return cluster_launch.pid(port)
    for p in subprocess.run(["pgrep", "-f", "ClusterRunner[.]dll --mode"], capture_output=True, text=True).stdout.split():
        try:
            cmd = open(f"/proc/{p}/cmdline").read().replace("\0", " ")
            envs = open(f"/proc/{p}/environ").read().split("\0")
        except OSError:
            continue
        if cmd.startswith("dotnet ") and f"HROT_DEBUG_API_PORT={port}" in envs:
            return int(p)
    return None


def get(port, route):
    try:
        return json.loads(node("get", port, route) or "{}").get("data") or {}
    except json.JSONDecodeError:
        return {}



def trace_nodes(folder, nodes, seconds=15):
    """CPU-trace each node's process for SECONDS (dotnet-trace, sampled thread time, speedscope) into FOLDER/<mode>.nettrace —
    started in the background; shared by perf-stress.py and scale-rig.py (CE-3427)."""
    os.makedirs(folder, exist_ok=True)
    tool = shutil.which("dotnet-trace") or os.path.expanduser("~/.dotnet/tools/dotnet-trace")
    env = dict(os.environ)
    if not cluster_launch.WINDOWS:
        env.setdefault("DOTNET_ROOT", "/usr/local/dotnet")
    for mode, port in nodes:
        pid = node_pid(port)
        if pid:
            subprocess.Popen([tool, "collect", "-p", str(pid), "--profile", "dotnet-sampled-thread-time", "--duration", f"00:00:00:{seconds:02d}",
                              "--format", "speedscope", "-o", os.path.join(folder, f"{mode}.nettrace")], env=env,
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print(f"  tracing {', '.join(m for m, _ in nodes)} for {seconds} s into {folder}")

def census():
    """Each node's entities grouped by component set — what an entity count is MADE of (a world twice as big must say of what)."""
    import collections
    for mode, port in NODES:
        if mode == "orchestrator": continue
        # ⚠ its own curl with a long timeout: cluster-node.sh's 20 s is too short for a full dump from a node doing < 1 frame/s
        raw = fetch(port, "/entities", 240)
        try: ents = json.loads(raw or "{}").get("data")
        except json.JSONDecodeError: ents = None
        if not isinstance(ents, list):
            print(f"  census {mode}: no entity list"); continue
        groups = collections.Counter()
        sample = {}
        for e in ents:
            key = tuple(sorted(e.get("components") or []))
            groups[key] += 1
            sample.setdefault(key, e.get("name") or "")
        print(f"  census {mode}: {len(ents)} entities in {len(groups)} component sets")
        out = os.environ.get("PERF_CENSUS_DIR")
        if out:   # the full sets (untruncated) + one sample dump per set, for a diff between runs
            os.makedirs(out, exist_ok=True)
            full = []
            for key, n in groups.most_common():
                nid = next((e.get("networkId") for e in ents if tuple(sorted(e.get("components") or [])) == key and e.get("networkId")), None)
                dump = None
                if nid and n >= 20:
                    r = fetch(port, f"/entities/{nid}", 60)
                    try: dump = json.loads(r or "{}").get("data")
                    except json.JSONDecodeError: pass
                full.append({"count": n, "components": list(key), "sampleName": sample[key], "sampleNetworkId": nid, "sample": dump})
            json.dump(full, open(os.path.join(out, f"{mode}.json"), "w"), indent=1)
        for key, n in groups.most_common(12):
            print(f"      {n:6d}  '{sample[key][:24]}'  [{len(key)}] {', '.join(key)[:170]}")


def node_work(port):
    """(the FRAME's work in ms, frames executed, top systems, the BACKGROUND work in ms per run) — the node's own profiler.
    ⭐ CE-3427 — a system of an Asynchronous module (EqsModule, the path solver: SlowBackground) runs on its own thread, OFF the frame:
    its ms are per run, not per frame, and summing them into the frame made SimHost's "frame" 157 ms when its main thread did ≈ 35 ms.
    Its rows are listed with a "[bg] " prefix."""
    d = get(port, "/diagnostics/architecture")
    rows = []
    for s in d.get("subsystems") or []:
        background = {m.get("name") for m in s.get("modules") or [] if m.get("runMode") == "Asynchronous"}
        for sy in s.get("systems") or []:
            bg = sy.get("module") in background
            rows.append((sy.get("recentAvgMs") or 0.0, sy.get("executions") or 0,
                         f"{'[bg] ' if bg else ''}{s.get('subsystem')}/{sy.get('systemName')}", bg))
    # a group's time already contains its members' — count only leaf systems (a group row has members listed under it by name)
    groups = ("Group", "TogglableSimulationGroup", "TogglableInputGroup", "TogglablePostSimulationGroup")
    leaf = [r for r in rows if not any(r[2].endswith(g) for g in groups)]
    frame = sum(r[0] for r in leaf if not r[3])
    background = sum(r[0] for r in leaf if r[3])
    return frame, max((r[1] for r in rows), default=0), sorted((r[:3] for r in leaf), reverse=True), background


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # the tables print ≤ ⛔ ✅ — a Windows console is cp1252
    ap = argparse.ArgumentParser()
    ap.add_argument("--men", type=int, default=1000)
    ap.add_argument("--cars", type=int, default=5000)
    ap.add_argument("--spawn-per-step", type=int, default=200, help="TrafficDemand.CarSpawnPerStep (SumoSharp's default is 5)")
    ap.add_argument("--fill-seconds", type=float, default=40.0, help="sim seconds to wait for the cars to fill before measuring")
    ap.add_argument("--seconds", type=float, default=60.0, help="sim seconds measured after the fill")
    ap.add_argument("--write-only", action="store_true")
    ap.add_argument("--show", default="", help="comma-separated system names (substring) always printed per node, whatever their rank — for an A/B of one system")
    ap.add_argument("--census", action="store_true", help="after measuring, count each node's entities by component set (GET /entities) — what the entity count is made of")
    ap.add_argument("--trace", metavar="DIR", help="also CPU-trace the cgf / simhost / traffic processes for 15 s at the start of the measured window (dotnet-trace, speedscope)")
    a = ap.parse_args()
    folder, n = write(a.men, a.cars, a.spawn_per_step)
    print(f"wrote {folder}/scenario.json — {n} authored entities")
    if a.write_only:
        return 0
    node("stop-all")
    try:
        for mode, port in NODES:
            print("  " + (node("start", mode, port, 0, 0).strip() or f"{mode} {port}?"))
        load = json.loads(node("post", NODES[0][1], "/scenario/load/live", json.dumps({"name": NAME, "waitForReady": True, "startPaused": False})) or "{}")
        print(f"  load: ok={load.get('ok')} {((load.get('data') or {}).get('target'))} {load.get('error') or ''}")
        node("post", NODES[0][1], "/sim/play")

        def alive():
            return {mode: bool(get(port, "/status")) for mode, port in NODES}

        def dead_report(which):
            for mode in which:
                port = dict(NODES)[mode]
                log = log_path(mode, port)
                lines = [l.rstrip() for l in open(log, errors="ignore")] if os.path.exists(log) else []
                hits = [l for l in lines if any(k in l for k in ("Unhandled exception", "Exception:", "Aborted"))][:3]
                print(f"  ⛔ {mode} stopped answering — {log}:")
                for h in hits: print(f"        {h[:200]}")

        def sim():
            return max(((get(p, "/status") or {}).get("simTime") or 0.0) for _, p in NODES)

        def wait(seconds):
            t0, w0, last, still = sim(), time.time(), -1.0, time.time()
            while sim() - t0 < seconds:
                time.sleep(2)
                now = sim()
                if now > last: last, still = now, time.time()
                if time.time() - still > 120:        # the sim has not moved for 2 minutes of wall: stop waiting
                    return False
            return True

        ok = wait(a.fill_seconds)
        down = [m for m, up in alive().items() if not up]
        if down or not ok:
            dead_report(down)
            print("  ⛔ INVALID RUN — " + ("a node died" if down else "the sim did not advance") + " during the fill; no verdict")
            return 2
        s1, w1 = sim(), time.time()
        f1 = {p: node_work(p)[1] for _, p in NODES}
        if a.trace:
            trace_nodes(a.trace, NODES[1:])
        ok = wait(a.seconds)
        s2, w2 = sim(), time.time()
        down = [m for m, up in alive().items() if not up]
        if down or not ok:
            dead_report(down)
            print("  ⛔ INVALID RUN — " + ("a node died" if down else "the sim did not advance") + " while measuring; no verdict")
            return 2
        print(f"\n== {a.men} men, {a.cars} cars target — measured {s2 - s1:.1f} s of sim in {w2 - w1:.1f} s of wall (real-time factor {(s2 - s1) / max(1e-6, w2 - w1):.2f})")
        worst = 0.0
        for mode, port in NODES:
            work, frames, top, _ = node_work(port)
            st = get(port, "/status")
            fps = (frames - f1[port]) / max(1e-6, w2 - w1)
            if mode != "orchestrator": worst = max(worst, work)
            flag = "✅" if work <= TARGET_MS else "⛔"
            print(f"  {flag} {mode:<12} work/frame {work:7.1f} ms (target ≤ {TARGET_MS:.1f})   achieved {fps:5.1f} frames/s   entities {st.get('entityCount')}")
            shown = top[:6] + [t for t in top[6:] if any(w and w in t[2] for w in a.show.split(","))]
            for ms, _, name in shown:
                print(f"        {ms:7.2f}  {name}")
        print(f"\n  worst node work/frame {worst:.1f} ms ⇒ {'MEETS' if worst <= TARGET_MS else 'MISSES'} the 30 Hz target")
        if a.census:
            census()
    finally:
        node("stop-all")
    return 0


if __name__ == "__main__":
    sys.exit(main())
