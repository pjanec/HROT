#!/usr/bin/env python3
"""scale-rig.py — the SCALE RIG (CE-4364, docs/DESIGN_Scale_Rig.md, PLAN_Large_Terrain P-0): load a terrain, spread N soldiers / vehicles /
aircraft over it on looping patrols, run it, and measure it against the large-terrain budgets (R-4192).

    dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore
    python3 scripts/scale-rig.py --terrain demo-city --men 100
    python3 scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --json out.json
    python3 scripts/scale-rig.py --terrain scale-1 --men 300 --write-only     # just (re)write scenarios/scale-rig-scale-1-300/

One process per node — orchestrator, cgf (the brain), simhost (the muscle) — through scripts/cluster-node.sh, the same machinery as
scripts/perf-stress.py (whose helpers it imports). Per node it reports: the load time (POST /scenario/load/live until every node is
Ready and OperatingLive), the peak memory (the process's peak working set), the WORK PER FRAME (the sum of its leaf systems' recent
averages — the scheduler's own profiler, GET /diagnostics/architecture) and the GARBAGE PER FRAME (the same systems' recent bytes per
execution, CE-4364), the top systems by each. The verdict row compares each with the budget:

    load ≤ 30 s warm (navmesh from HROT_NAVTILE_CACHE) · sim frame ≤ 10 ms · 0 B garbage per steady frame · ≤ 4 GB per node

The scenario is deterministic (--seed): squads of 8 men (TKB 302 + 2002, high health) each marching (SquadMarch) to a road point
--patrol/2 … --patrol metres away (still walking through the measured window), vehicles (TKB 2001) on looping road routes of four points, aircraft (TKB 405) on 2 km square orbits at 400 m AGL;
half the units Friend, half Hostile so perception works both ways. Routes are RoutePlan entities (TKB 8802) followed by FollowRoute.
The scenario folder is gitignored (scenarios/scale-rig-*/) — regenerated, never committed.
"""
import argparse
import importlib.util
import json
import math
import os
import platform
import random
import sys
import time
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("perf_stress", os.path.join(HERE, "perf-stress.py"))
ps = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ps)   # node(), get(), node_work(), rot() — one copy of the cluster machinery
REPO = ps.udc.REPO

BUDGET = {"load_s": 30.0, "frame_ms": 10.0, "garbage_b": 0.0, "memory_gb": 4.0}
NODES = (("orchestrator", 8310), ("cgf", 8311), ("simhost", 8312))


class Ids:
    def __init__(self, prefix): self.prefix, self.n = prefix, 0

    def __call__(self):
        self.n += 1
        return f"{self.prefix}-0000-4000-8000-{self.n:012d}"


def terrain_info(name):
    folder = os.path.join(REPO, "assets", "terrain", name)
    t = json.load(open(os.path.join(folder, "terrain.json"), encoding="utf-8"))
    world = json.load(open(os.path.join(folder, t["world"]), encoding="utf-8"))
    bounds = world["hrot"]["bounds"]
    grid = None
    if world["hrot"].get("heightGrid"):
        hg = os.path.join(folder, world["hrot"]["heightGrid"])
        grid = read_store(hg) if hg.endswith(".heights") else read_asc(hg)
    road = []
    net = t.get("sumoNet")
    if net:
        ax, ay = net["anchor"]
        root = ET.parse(os.path.join(folder, net["file"])).getroot()
        for e in root.iter("edge"):
            if e.get("function") == "internal":
                continue
            for lane in e.iter("lane"):
                if lane.get("allow") == "pedestrian":
                    continue
                for q in lane.get("shape").split():
                    x, y = (float(v) for v in q.split(",")[:2])
                    x, y = x - ax, y - ay
                    if bounds[0] + 20 <= x <= bounds[2] - 20 and bounds[1] + 20 <= y <= bounds[3] - 20:
                        road.append((round(x, 1), round(y, 1)))
                break   # one lane per edge is enough
    if not road:   # no net: a lattice of open points
        step = 25.0
        road = [(bounds[0] + 20 + i * step, bounds[1] + 20 + j * step)
                for i in range(int((bounds[2] - bounds[0] - 40) / step)) for j in range(int((bounds[3] - bounds[1] - 40) / step))]
    return bounds, grid, sorted(set(road))


def read_asc(path):
    with open(path, encoding="utf-8") as f:
        head = {}
        for _ in range(6):
            pos = f.tell()
            parts = f.readline().split()
            if parts and not _num(parts[0]):
                head[parts[0].lower()] = float(parts[1])
            else:
                f.seek(pos)
                break
        cols, rows, cell = int(head["ncols"]), int(head["nrows"]), head["cellsize"]
        x0 = head.get("xllcenter", head.get("xllcorner", 0) + cell / 2)
        y0 = head.get("yllcenter", head.get("yllcorner", 0) + cell / 2)
        z = [list(map(float, f.readline().split())) for _ in range(rows)]   # north row first
    return (x0, y0, cell, cols, rows, z)


def read_store(path):
    """the binary height store (CE-4365, Terrain Height §9.1): a ground(x, y) that seeks the one post it needs."""
    import struct
    f = open(path, "rb")
    magic, version, cols, rows = struct.unpack("<IiII", f.read(16))
    ox, oy, cell = struct.unpack("<ddd", f.read(24))
    tile, tx, ty = struct.unpack("<iii", f.read(12))
    head = f.tell()

    def z(x, y):
        c = min(max(int(round((x - ox) / cell)), 0), cols - 1)
        r = min(max(int(round((y - oy) / cell)), 0), rows - 1)
        t = (r // tile) * tx + (c // tile)
        f.seek(head + (t * tile * tile + (r % tile) * tile + (c % tile)) * 4)
        return struct.unpack("<f", f.read(4))[0]
    return z


def _num(s):
    try:
        float(s)
        return True
    except ValueError:
        return False


def ground(grid, x, y):
    if grid is None:
        return 0.0
    if callable(grid):
        return grid(x, y)
    x0, y0, cell, cols, rows, z = grid
    c = min(max(int(round((x - x0) / cell)), 0), cols - 1)
    r = min(max(int(round((y - y0) / cell)), 0), rows - 1)
    return z[rows - 1 - r][c]


def scenario(terrain, men, vehicles, aircraft, patrol, seed, sides=2):
    rng = random.Random(seed)
    # (the squads' march objective is ≥ patrol/2 away so they are still walking through the measured window)
    bounds, grid, road = terrain_info(terrain)
    ids, ents = Ids("5ca1e000"), {}
    cx, cy = (bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2

    def route(points, speed, agl=None):
        g = ids()
        wps = []
        for (x, y) in points:
            w = {"X": x, "Y": y, "Z": round(ground(grid, x, y), 2), "TargetSpeed": speed}
            if agl is not None:
                w["HeightAgl"] = agl
            wps.append(w)
        ents[g] = {"SimTransform": {"Position": [points[0][0], points[0][1], wps[0]["Z"]], "Rotation": [0, 0, 0, 1]},
                   "TkbIdentity": {"TkbType": 8802}, "EntityInfo": {"Name": f"route {len(ents)}", "ForceId": "Friend"},
                   "RoutePlan": {"Waypoints": wps, "IsLoop": True}}
        return g

    def near(p, radius):
        """a road point between radius/2 and radius from p (rejection sampling over the road points; the nearest one when none)."""
        for _ in range(200):
            q = road[rng.randrange(len(road))]
            if radius / 2 <= math.hypot(q[0] - p[0], q[1] - p[1]) <= radius:
                return q
        return min(road, key=lambda q: math.hypot(q[0] - p[0], q[1] - p[1]))

    teams = {}
    for force, name in (("Friend", "Blue"), ("Hostile", "Red")):
        g = ids()
        teams[force] = g
        ents[g] = {"SimTransform": {"Position": [cx, cy, 0], "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": 305},
                   "EntityInfo": {"Name": f"{name} Force", "ForceId": force}}

    squads = max(1, men // 8)
    for s in range(squads):
        force, name = (("Friend", "Blue"), ("Hostile", "Red"))[s % sides]
        a = road[rng.randrange(len(road))]
        b = near(a, patrol)
        sq = ids()
        za = ground(grid, *a)
        # the squad marches (SquadMarch, CE-2123) to a road point ≤ --patrol away; its men follow as its subordinates
        ents[sq] = {"SimTransform": {"Position": [a[0], a[1], za], "Rotation": ps.rot(0)}, "TkbIdentity": {"TkbType": 302},
                    "EntityInfo": {"Name": f"{name} Squad {s + 1}", "ForceId": force},
                    "UnitSubordinate": {"CommanderGuid": teams[force], "Designation": "Wingman"},
                    "Behavior": {"Name": "SquadMarch", "Origin": "Superior", "Params": {
                        "Objective": [b[0], b[1], round(ground(grid, *b), 2)], "Shape": 0, "Spacing": 4, "DangerAreas": False, "OpenGround": False}}}
        for i in range(8):
            x, y = a[0] + (i % 4) * 2.0, a[1] + (i // 4) * 2.0
            ents[ids()] = {"SimTransform": {"Position": [x, y, round(ground(grid, x, y), 2)], "Rotation": ps.rot(0)},
                           "Health": {"Current": 100000, "Max": 100000}, "TkbIdentity": {"TkbType": 2002},
                           "EntityInfo": {"Name": f"{name} {s + 1}-{i + 1}", "ForceId": force}, "TargetMemory": {"Entries": []},
                           "UnitSubordinate": {"CommanderGuid": sq, "Designation": "SquadLeader" if i == 0 else ("MachineGunner" if i == 1 else "Wingman")}}

    for v in range(vehicles):
        force = ("Friend", "Hostile")[v % sides]
        a = road[rng.randrange(len(road))]
        pts = [a] + [near(a, 1000.0) for _ in range(3)]
        r = route(pts, 8.0)
        ents[ids()] = {"SimTransform": {"Position": [a[0], a[1], round(ground(grid, *a), 2)], "Rotation": ps.rot(0)},
                       "Health": {"Current": 100000, "Max": 100000}, "TkbIdentity": {"TkbType": 2001},
                       "EntityInfo": {"Name": f"Vehicle {v + 1}", "ForceId": force}, "TargetMemory": {"Entries": []},
                       "UnitSubordinate": {"CommanderGuid": teams[force], "Designation": "Wingman"},
                       "Behavior": {"Name": "FollowRoute", "Params": {"routeEntityId": r, "Speed": 8.0}, "Origin": "Superior"}}

    for k in range(aircraft):
        force = ("Friend", "Hostile")[k % sides]
        x = rng.uniform(bounds[0] + 1000, bounds[2] - 1000) if bounds[2] - bounds[0] > 2500 else cx
        y = rng.uniform(bounds[1] + 1000, bounds[3] - 1000) if bounds[3] - bounds[1] > 2500 else cy
        h = 1000.0
        sq = [(x - h, y - h), (x - h, y + h), (x + h, y + h), (x + h, y - h)]
        r = route(sq, 120.0, agl=400)
        z0 = ground(grid, *sq[0]) + 400
        ents[ids()] = {"SimTransform": {"Position": [sq[0][0], sq[0][1], round(z0, 2)], "Rotation": [0, 0, 0, 1]},
                       "TkbIdentity": {"TkbType": 405}, "EntityInfo": {"Name": f"Aircraft {k + 1}", "ForceId": force},
                       "TargetMemory": {"Entries": []}, "UnitSubordinate": {"CommanderGuid": teams[force], "Designation": "Wingman"},
                       "Behavior": {"Name": "FollowRoute", "Params": {"routeEntityId": r, "Speed": 120.0}, "Origin": "Superior"}}

    return {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
            "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": terrain,
                       "description": f"What: the CE-4364 scale rig on {terrain} — {squads * 8} soldiers in {squads} squads on looping patrols "
                                      f"(≤ {patrol:g} m), {vehicles} vehicles on road loops, {aircraft} aircraft on 2 km orbits at 400 m AGL; "
                                      f"seed {seed}. Judge: load, peak memory, work and garbage per frame per node against the R-4192 budgets. "
                                      "Generated by scripts/scale-rig.py."},
            "entities": ents}


def write(name, doc):
    folder = os.path.join(REPO, "scenarios", name)
    os.makedirs(folder, exist_ok=True)
    with open(os.path.join(folder, "scenario.json"), "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=1)
        fh.write("\n")
    return folder


def _position(node):
    """The first 'Position' [x, y, z] (or {X, Y, Z}) found in an entity dump."""
    if isinstance(node, dict):
        for k, v in node.items():
            if k == "Position":
                if isinstance(v, list) and len(v) >= 2:
                    return (float(v[0]), float(v[1]))
                if isinstance(v, dict) and "X" in v:
                    return (float(v["X"]), float(v["Y"]))
            r = _position(v)
            if r:
                return r
    elif isinstance(node, list):
        for v in node:
            r = _position(v)
            if r:
                return r
    return None


def sample_ids(port, n):
    """networkIds of n movers spread over the list (entities that navigate: NavigationIntent) — the ones the rig checks move."""
    raw = ps.fetch(port, "/entities?component=NavigationIntent", 120)
    try:
        ents = json.loads(raw or "{}").get("data") or []
    except json.JSONDecodeError:
        ents = []
    ids = [e["networkId"] for e in ents if e.get("networkId")]
    step = max(1, len(ids) // max(1, n))
    return ids[::step][:n]


def positions(port, ids):
    return {i: _position(ps.get(port, f"/entities/{i}")) for i in ids}


def garbage(port):
    """(the sum of the leaf systems' recent bytes per execution, top systems) — CE-4364's profiler column."""
    d = ps.get(port, "/diagnostics/architecture")
    groups = ("Group", "TogglableSimulationGroup", "TogglableInputGroup", "TogglablePostSimulationGroup")
    rows = [(sy.get("recentAvgBytes") or 0.0, f"{s.get('subsystem')}/{sy.get('systemName')}")
            for s in d.get("subsystems") or [] for sy in s.get("systems") or []]
    leaf = [r for r in rows if not any(r[1].endswith(g) for g in groups)]
    return sum(r[0] for r in leaf), sorted(leaf, reverse=True), d.get("process") or {}


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # the tables print ≤ ⛔ ✅ — a Windows console is cp1252
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--terrain", default="demo-city")
    ap.add_argument("--men", type=int, default=100)
    ap.add_argument("--vehicles", type=int, default=0)
    ap.add_argument("--aircraft", type=int, default=0)
    ap.add_argument("--patrol", type=float, default=300.0, help="a squad's patrol: its second point within this of its first (m)")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--sides", type=int, choices=(1, 2), default=2,
                    help="2: half Friend, half Hostile — they meet and FIGHT (munitions, damage); 1: all Friend — perception and movement only")
    ap.add_argument("--warmup", type=float, default=20.0, help="sim seconds after the load before measuring")
    ap.add_argument("--seconds", type=float, default=30.0, help="sim seconds measured")
    ap.add_argument("--load-timeout", type=float, default=1800.0)
    ap.add_argument("--write-only", action="store_true")
    ap.add_argument("--json", default=None, help="also write the report as JSON here")
    ap.add_argument("--record", action="store_true", help="record the live run (default: OFF — the recorder is not what the rig measures, CE-3427)")
    ap.add_argument("--keep", action="store_true", help="leave the nodes running after the report (to inspect them; stop with perf-stress stop-all)")
    ap.add_argument("--trace", metavar="DIR", help="also CPU-trace cgf and simhost for 15 s at the start of the measured window (dotnet-trace, speedscope)")
    ap.add_argument("--queries", type=int, default=15, help="the most expensive world queries listed per node (CE-3427)")
    ap.add_argument("--runner-dll", help="Hrot.ClusterRunner.dll to run (default: HROT_RUNNER_DLL, else the repo's Release, else Debug build)")
    ap.add_argument("--pin", action="store_true",
                    help="pin each node to its own logical CPUs (orchestrator 2, cgf and simhost half the rest each) — one node per machine, approximated")
    ap.add_argument("--windowed", action="store_true",
                    help="start the nodes with their window (default --headless: a windowed node's software-GL draw on Xvfb took 3 cores idle)")
    a = ap.parse_args()
    if a.runner_dll:
        os.environ["HROT_RUNNER_DLL"] = os.path.abspath(a.runner_dll)

    name = f"scale-rig-{a.terrain}-{a.men}-{a.sides}s"
    doc = scenario(a.terrain, a.men, a.vehicles, a.aircraft, a.patrol, a.seed, a.sides)
    folder = write(name, doc)
    print(f"wrote {folder}/scenario.json — {len(doc['entities'])} authored entities")
    if a.write_only:
        return 0

    report = {"terrain": a.terrain, "sides": a.sides, "men": (a.men // 8) * 8, "vehicles": a.vehicles, "aircraft": a.aircraft, "budget": BUDGET,
              "machine": f"{os.cpu_count()} cores, {platform.system()} {platform.release()}, {platform.processor()}",
              "build": "Release" if "Release" in (ps.cluster_launch.runner_dll() if ps.cluster_launch.WINDOWS else os.environ.get("HROT_RUNNER_DLL", "")) else "Debug",
              "nodes": {}}
    if not a.windowed:
        os.environ["HROT_NODE_ARGS"] = (os.environ.get("HROT_NODE_ARGS", "") + " --headless").strip()
        os.environ.setdefault("HROT_HEADLESS_MAX_HZ", "240")   # CE-4366: an unpaced headless loop spins a core per node
    report["headless"] = not a.windowed
    if a.pin:
        ps.cluster_launch.AFFINITY.update(ps.cluster_launch.split_cpus([p for _, p in NODES]))
        report["pinned"] = {m: bin(ps.cluster_launch.AFFINITY[p]).count("1") for m, p in NODES}
        if not ps.cluster_launch.WINDOWS:
            raise SystemExit("--pin: Windows only (the Linux nodes start through cluster-node.sh)")
    ps.node("stop-all")
    try:
        for mode, port in NODES:
            print("  " + (ps.node("start", mode, port, 0, 0).strip() or f"{mode} {port}?"))
        # ⭐ CE-3427 — recording OFF unless asked (CE-4090 run setting): the recorder's cost is not the terrain's
        ps.node("post", NODES[0][1], "/run-settings", json.dumps({"recordLiveRuns": bool(a.record)}))
        report["recorded"] = bool(a.record)
        t0 = time.time()
        ps.node("post", NODES[0][1], "/scenario/load/live", json.dumps({"name": name, "waitForReady": False, "startPaused": False}))
        ready = False
        while time.time() - t0 < a.load_timeout:
            sts = [ps.get(p, "/status") for _, p in NODES]
            # every node OperatingLive; a node that loads (cgf, simhost) has its own load Loaded and its local phase ready to play
            def done(st):
                ld = st.get("load")
                return st.get("clusterState") == "OperatingLive" and (ld is None or (ld.get("phase") == "Loaded" and ld.get("localPhase") in ("Ready", "PlayReady")))
            if all(st and done(st) for st in sts):
                ready = True
                break
            time.sleep(1.0)
        load_s = time.time() - t0
        report["load_s"] = round(load_s, 1)
        print(f"  load: {'ready' if ready else 'NOT READY'} after {load_s:.1f} s")
        if not ready:
            report["verdict"] = "INVALID — the load did not finish"
            return finish(report, a.json, 2)
        ps.node("post", NODES[0][1], "/sim/play")

        def sim():
            return max(((ps.get(p, "/status") or {}).get("simTime") or 0.0) for _, p in NODES)

        def wait(seconds):
            s0, still, last = sim(), time.time(), -1.0
            while sim() - s0 < seconds:
                time.sleep(2)
                now = sim()
                if now > last:
                    last, still = now, time.time()
                if time.time() - still > 120:
                    return False
            return True

        if not wait(a.warmup):
            report["verdict"] = "INVALID — the sim did not advance in the warm-up"
            return finish(report, a.json, 2)
        f1 = {p: ps.node_work(p)[1] for _, p in NODES}
        for _, p in NODES:   # ⭐ CE-3427 — the query timings cover the measured window only
            ps.get(p, "/diagnostics/queries?reset=true")
        if a.trace:
            ps.trace_nodes(a.trace, NODES[1:])
        movers = sample_ids(NODES[2][1], 12)
        p1 = positions(NODES[2][1], movers)
        s1, w1 = sim(), time.time()
        ok = wait(a.seconds)
        s2, w2 = sim(), time.time()
        if not ok:
            report["verdict"] = "INVALID — the sim stopped while measuring"
            return finish(report, a.json, 2)
        report["realtime_factor"] = round((s2 - s1) / max(1e-6, w2 - w1), 2)
        p2 = positions(NODES[2][1], movers)
        moved = [i for i in movers if p1.get(i) and p2.get(i) and math.hypot(p2[i][0] - p1[i][0], p2[i][1] - p1[i][1]) > 1.0]
        report["moved"] = f"{len(moved)}/{len(movers)}"   # the sampled soldiers / vehicles / aircraft that moved > 1 m in the window
        for mode, port in NODES:
            work, frames, top, background = ps.node_work(port)
            gb, gtop, proc = garbage(port)
            st = ps.get(port, "/status")
            report["nodes"][mode] = {
                "work_ms": round(work, 2), "background_ms": round(background, 2), "garbage_b": round(gb, 0), "frames_per_s": round((frames - f1[port]) / max(1e-6, w2 - w1), 1),
                "peak_gb": round((proc.get("peakWorkingSetBytes") or 0) / 2**30, 2), "heap_gb": round((proc.get("managedHeapBytes") or 0) / 2**30, 2),
                "gc": [proc.get("gen0"), proc.get("gen1"), proc.get("gen2")], "entities": st.get("entityCount"),
                "top_ms": [[round(ms, 2), n] for ms, _, n in top[:8]], "top_b": [[round(b), n] for b, n in gtop[:8] if b > 0],
                "queries": queries(port, (frames - f1[port]) / max(1e-6, w2 - w1), a.queries)}
        nodes = {m: n for m, n in report["nodes"].items() if m != "orchestrator"}
        report["worst"] = {"work_ms": max(n["work_ms"] for n in nodes.values()), "garbage_b": max(n["garbage_b"] for n in nodes.values()),
                           "peak_gb": max(n["peak_gb"] for n in report["nodes"].values())}
        report["checks"] = {
            "load": report["load_s"] <= BUDGET["load_s"],
            "frame": report["worst"]["work_ms"] <= BUDGET["frame_ms"],
            "garbage": report["worst"]["garbage_b"] <= BUDGET["garbage_b"],
            "memory": report["worst"]["peak_gb"] <= BUDGET["memory_gb"]}
        report["verdict"] = "MEETS" if all(report["checks"].values()) else "MISSES " + ", ".join(k for k, v in report["checks"].items() if not v)
        print_table(report)
        return finish(report, a.json, 0)
    finally:
        if a.keep:
            print("  --keep: the nodes are still running (" + ", ".join(f"{m} :{p}" for m, p in NODES) + ")")
        else:
            ps.node("stop-all")


def queries(port, fps, n):
    """⭐ CE-3427 — the node's most expensive world queries over the measured window: [ms per frame, calls per frame, avg µs, max µs, name]."""
    d = ps.get(port, "/diagnostics/queries") or {}
    rows = []
    qs = d.get("queries") or []
    # the most time first, then every counter slot ("(count)") and every slot that counts garbage, so neither is cut off
    keep = qs[:n] + [q for q in qs[n:] if "(count)" in (q.get("name") or "") or (q.get("bytesPerSecond") or 0) > 0]
    for q in keep:
        per_s = q.get("msPerSecond") or 0.0
        rows.append([round(per_s / max(fps, 1e-6), 3), round((q.get("perSecond") or 0.0) / max(fps, 1e-6), 2),
                     q.get("avgUs"), q.get("maxUs"), q.get("name"), round((q.get("bytesPerSecond") or 0.0) / max(fps, 1e-6))])
    return rows


def print_table(r):
    b = r["budget"]
    print(f"\n== {r['terrain']}: {r['men']} men, {r['vehicles']} vehicles, {r['aircraft']} aircraft, {r['sides']} side(s) ({r['build']}, {r['machine']}) — "
          f"real-time factor {r.get('realtime_factor')}, sampled movers moved {r.get('moved')}, recorded: {r.get('recorded')}")
    print(f"  load {r['load_s']} s (≤ {b['load_s']:g} warm)")
    print(f"  {'node':<13}{'work ms/frame':>14}{'bg ms/run':>11}{'garbage B/frame':>17}{'frames/s':>10}{'peak GB':>9}{'heap GB':>9}{'entities':>10}  gc 0/1/2")
    for m, n in r["nodes"].items():
        print(f"  {m:<13}{n['work_ms']:>14.2f}{n.get('background_ms', 0):>11.2f}{n['garbage_b']:>17.0f}{n['frames_per_s']:>10.1f}{n['peak_gb']:>9.2f}{n['heap_gb']:>9.2f}{str(n['entities']):>10}  {n['gc']}")
        for ms, s in n["top_ms"][:5]:
            print(f"        {ms:8.2f} ms  {s}")
        for by, s in n["top_b"][:3]:
            print(f"        {by:8d} B   {s}")
        if n.get("queries"):
            print(f"        {'world queries':<44}{'ms/frame':>10}{'calls/frame':>13}{'avg µs':>10}{'max µs':>10}{'KB/frame':>10}")
            for ms, calls, avg, mx, name, by in n["queries"]:
                print(f"        {name:<44}{ms:>10.3f}{calls:>13.2f}{avg:>10.1f}{mx:>10.0f}{by / 1024:>10.1f}")
    print(f"  budgets: " + "  ".join(f"{k} {'✅' if v else '⛔'}" for k, v in r["checks"].items()) + f"  ⇒ {r['verdict']}")


def finish(report, path, code):
    if path:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=1)
    return code


if __name__ == "__main__":
    sys.exit(main())
