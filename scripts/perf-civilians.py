#!/usr/bin/env python3
"""CE-3222 (civilians B-4, CV-10) — the civilian perf gate: ms/frame per subsystem for N civilians.

One command: generates a test-town scenario with N civilians (mixed CIV-1 variants, spread over the town so each runs its
routine), runs it in a fresh ClusterRunner --mode all for --seconds of SIM time, and prints the per-system ms/frame on every
subsystem (CGF, SimHost, …) for the systems that scale with the crowd — read from the scheduler's own profiler through
GET /diagnostics/architecture (avgMs / recentAvgMs / maxMs per system). No timing code of its own.

    dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore
    python3 scripts/perf-civilians.py 100 300 500            # one fresh cluster per count
    python3 scripts/perf-civilians.py 300 --budget-ms 16     # exit 1 when a watched system's recent average exceeds the budget
    python3 scripts/perf-civilians.py --gate                 # ⭐ R-3029: 300 civilians, each node's ALL SYSTEMS ≤ 100 ms (the ruled ceiling)

Design: docs/DESIGN_Civilians_And_Crowd_Control.md §7.4 (B-4) and its as-built §7.4a. Demo ua-civ-crowd-300 (§5c) reads this.
"""
import argparse, json, math, os, shutil, sys, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib.util
_spec = importlib.util.spec_from_file_location("udc", os.path.join(os.path.dirname(os.path.abspath(__file__)), "utility-demo-check.py"))
udc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(udc)   # the one launch / load / call helper set (RUNBOOK_Utility_AI_Demos)

CIVILIAN_TYPES = (1101, 1102, 1103, 1104)   # the CIV-1 family (CE-3220): each loads the CivilianHsm (DefaultBehaviorHash)
WATCHED = ("BrainTickSystem", "CivilianFearSystem", "CrowdSystem", "CarKinematicsSystem", "VisualPerception", "Perception",
           "Sensor", "Navigation", "Locomotion",
           # the ones this gate already caught growing with the crowd (CE-3240, CE-3241, CE-3242) — kept in view
           "WeaponAmmoMirror", "EqsResultUpdate", "GhostPromotion", "OwnershipRecompute")
AREA = (60.0, 60.0, 340.0, 340.0)           # test-town, inside its roads' footprint
TOP = 8                                     # the costliest systems shown per subsystem, whatever they are
# ⭐ R-3029 (CE-3222; DESIGN_Civilians_And_Crowd_Control §7.4a) — the approved REGRESSION ceiling on the dev VM: each node's sum of recent
#   averages at 300 civilians. Not a real-time target (that is set separately on a release machine).
GATE_COUNT, GATE_NODE_MS = 300, 100.0


def scenario(count):
    """A test-town scenario with `count` civilians on a jittered grid (deterministic: the same count, the same cast)."""
    side = max(1, math.ceil(math.sqrt(count)))
    x0, y0, x1, y1 = AREA
    dx, dy = (x1 - x0) / side, (y1 - y0) / side
    ents = {}
    for i in range(count):
        gx, gy = i % side, i // side
        jx = ((i * 7919) % 100) / 100.0 - 0.5
        jy = ((i * 104729) % 100) / 100.0 - 0.5
        x, y = x0 + (gx + 0.5 + 0.4 * jx) * dx, y0 + (gy + 0.5 + 0.4 * jy) * dy
        ents[f"c3222000-0000-4000-8000-{i + 1:012d}"] = {
            "SimTransform": {"Position": [round(x, 2), round(y, 2), 0], "Rotation": [0, 0, 0, 1]},
            "Health": {"Current": 100, "Max": 100},
            "NetworkIdentity": {"Value": 32220000 + i + 1},
            "TkbIdentity": {"TkbType": CIVILIAN_TYPES[i % len(CIVILIAN_TYPES)]},
            "EntityInfo": {"Name": f"Civilian {i + 1}", "ForceId": "Neutral"},
            "TargetMemory": {"Entries": []},
        }
    return {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
            "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "test-town"},
            "entities": ents}


def write_scenario(count):
    name = f"perf-civ-{count}"
    folder = os.path.join(udc.REPO, "scenarios", name)
    os.makedirs(folder, exist_ok=True)
    with open(os.path.join(folder, "scenario.json"), "w") as fh:
        json.dump(scenario(count), fh, indent=1)
    return name, folder


def profile_rows():
    """([(subsystem, system, avgMs, recentAvgMs, maxMs, executions)], the same for translators) — every subsystem."""
    d = udc.data(udc.call("GET", "/diagnostics/architecture")) or {}
    rows, trows = [], []
    for s in d.get("subsystems") or []:
        for sy in s.get("systems") or []:
            if "avgMs" not in sy:
                sys.exit("the route carries no timing (avgMs) — the runner predates CE-3222; rebuild it")
            rows.append((s.get("subsystem"), sy.get("systemName") or "?", sy.get("avgMs", 0.0), sy.get("recentAvgMs", 0.0),
                         sy.get("maxMs", 0.0), sy.get("executions", 0)))
        for t in s.get("translators") or []:     # CE-3242: translator rows carry their own ms
            trows.append((s.get("subsystem"), f"{t.get('topic') or t.get('systemName') or '?'} ({t.get('direction') or '?'})",
                          t.get("avgMs", 0.0), t.get("recentAvgMs", 0.0), t.get("maxMs", 0.0), t.get("executions", 0)))
    return rows, trows


def run(count, seconds, port, budget, node_budget=0.0):
    name, folder = write_scenario(count)
    proc = udc.launch(port)
    try:
        udc.load(name)
        udc.call("POST", "/sim/play", {})
        t0 = udc.sim_time() or 0.0
        if udc.wait_for(lambda: (udc.sim_time() or 0.0) - t0 >= seconds, timeout=max(600, seconds * 20), every=2) is None:
            print(f"   ⚠ the sim did not advance {seconds} s (at {(udc.sim_time() or 0.0) - t0:.1f} s) — reporting what ran")
        rows, trows = profile_rows()
    finally:
        udc.stop(proc)
        shutil.rmtree(folder, ignore_errors=True)
    watched = [r for r in rows if any(w in r[1] for w in WATCHED)]
    watched.sort(key=lambda r: -r[3])
    print(f"\n== {count} civilians, {seconds} s sim — per-system ms/frame (recent average · lifetime average · max · executions)")
    for sub, name_, avg, recent, mx, n in watched:
        print(f"   {sub:<10} {name_:<44} {recent:8.3f}  {avg:8.3f}  {mx:9.3f}  {n:7d}")
    per_sub = {}
    for sub, _, _, recent, _, _ in rows:
        per_sub[sub] = per_sub.get(sub, 0.0) + recent
    for sub, total in sorted(per_sub.items()):
        print(f"   {sub:<10} ALL SYSTEMS (sum of recent averages)              {total:8.3f}")
        top = sorted((r for r in rows if r[0] == sub), key=lambda r: -r[3])[:TOP]
        for _, name_, avg, recent, mx, n in top:
            print(f"      top  {name_:<47} {recent:8.3f}  {avg:8.3f}  {mx:9.3f}  {n:7d}")
    for sub in sorted({r[0] for r in trows}):
        top = sorted((r for r in trows if r[0] == sub), key=lambda r: -r[3])[:TOP]
        print(f"   {sub:<10} TRANSLATORS (costliest)")
        for _, name_, avg, recent, mx, n in top:
            print(f"      tr   {name_:<47} {recent:8.3f}  {avg:8.3f}  {mx:9.3f}  {n:7d}")
    frames = max((r[5] for r in rows), default=0)
    print(f"   frames run: {frames} in {seconds} s of sim (a --mode all host runs every subsystem in one process, one after another)")
    over = [r for r in watched if budget and r[3] > budget]
    # ⭐ R-3029 — a node (subsystem) over the per-node ceiling, reported in the same row shape: (sub, "ALL SYSTEMS", avg, recent, max, n)
    over += [(sub, "ALL SYSTEMS", 0.0, total, 0.0, 0) for sub, total in sorted(per_sub.items()) if node_budget and total > node_budget]
    return over


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("counts", nargs="*", type=int)
    ap.add_argument("--seconds", type=float, default=60.0, help="sim seconds to run before reading (default 60)")
    ap.add_argument("--port", type=int, default=8111)
    ap.add_argument("--budget-ms", type=float, default=0.0, help="fail when a watched system's recent average exceeds this")
    ap.add_argument("--node-budget-ms", type=float, default=0.0, help="fail when a node's ALL SYSTEMS sum exceeds this (R-3029)")
    ap.add_argument("--gate", action="store_true", help=f"the ruled gate: {GATE_COUNT} civilians, --node-budget-ms {GATE_NODE_MS:g} (R-3029)")
    ap.set_defaults(counts=[])
    a = ap.parse_args()
    if a.gate:
        a.counts, a.node_budget_ms = a.counts or [GATE_COUNT], a.node_budget_ms or GATE_NODE_MS
    if not a.counts:
        ap.error("give civilian counts, or --gate")
    udc.BASE = f"http://localhost:{a.port}"
    failed = []
    for c in a.counts:
        failed += [(c,) + r for r in run(c, a.seconds, a.port, a.budget_ms, a.node_budget_ms)]
    if failed:
        print("\nOVER BUDGET:")
        for c, sub, n, _, recent, _, _ in failed:
            limit = a.node_budget_ms if n == "ALL SYSTEMS" else a.budget_ms
            print(f"   {c} civilians · {sub} {n}: {recent:.3f} ms > {limit} ms")
        sys.exit(1)
    print("\nPASS" if a.budget_ms or a.node_budget_ms else "\n(no budget given — measured only)")


if __name__ == "__main__":
    main()
