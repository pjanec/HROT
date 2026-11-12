#!/usr/bin/env python3
"""Parallel-simulation experiment — the one-command measurement (exp/parallel-sim).

Runs scripts/scale-rig.py (1000 men, 50 vehicles, 10 aircraft, demo-city, headless) once serial and once per thread
count with HROT_PARALLEL_SIM=1, and prints the converted systems' ms/frame side by side.

    python scripts/parallel-sim-measure.py                      # serial + 1/2/4/8 threads (Deterministic)
    python scripts/parallel-sim-measure.py --threads 2 6 --mode throughput
    python scripts/parallel-sim-measure.py --content          # + commit by content (D-5), live and recorded
    python scripts/parallel-sim-measure.py --pin --poc2       # POC 2: each item against a recorded serial baseline
    python scripts/parallel-sim-measure.py --pin --poc3       # POC 3: the same for POC 3's items
    python scripts/parallel-sim-measure.py --pin --poc4       # POC 4: the ONE worker pool — serial vs everything on (1 / all threads), threads per node

Build Release first:  dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release
Windows: HROT_HIDDEN_WINDOW=1 is set for every node (no window ever shows). Results land in --out (default ./parallel-sim-results).
"""
import argparse, json, os, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SYSTEMS = ["BrainTickSystem", "StressSystem", "CgfThreatEvaluationSystem", "CalloutSystem",
           "WeaponDispatcherSystem", "SquadCoordinationSystem", "RecorderTickSystem", "CycloneEgressSystem", "CycloneNetworkIngressSystem",
           "EgressPipelineLaunchSystem"]

# ⭐ POC 2 (exp/parallel-sim-2) — one recorded column per item, each against the same recorded serial baseline. Every item is opt-in.
POC2 = [
    ("rec-serial",   {}),
    ("rec-qtoff",    {"HROT_QUERY_TIMINGS": "0"}),                        # Q-0: the per-query timer's own cost
    ("rec-q1",       {"HROT_BP_STAMP_ON_CHANGE": "1"}),                   # Q-1: blueprint chunks stamped only on a real change
    ("rec-q2",       {"FDP_EGRESS_OFFFRAME": "1"}),                       # Q-2: the state egress off the frame
    ("rec-thr",      {"HROT_PARALLEL_SIM": "1", "HROT_SIM_MODE": "throughput"}),                               # POC 1's systems, Throughput
    ("rec-q4",       {"HROT_PARALLEL_SIM": "1", "HROT_SIM_MODE": "throughput", "HROT_BRAIN_PRESCORE": "1"}),   # Q-4: + the brains' pre-score
    ("rec-all",      {"HROT_QUERY_TIMINGS": "0", "HROT_BP_STAMP_ON_CHANGE": "1", "FDP_EGRESS_OFFFRAME": "1",
                      "HROT_PARALLEL_SIM": "1", "HROT_SIM_MODE": "throughput", "HROT_BRAIN_PRESCORE": "1"}),   # everything together
]


def run(name, env_extra, out, rig_args):
    env = dict(os.environ, HROT_HIDDEN_WINDOW="1", **env_extra)
    dll = ROOT / "Hrot/Runner/Hrot.ClusterRunner/bin/Release/net8.0/Hrot.ClusterRunner.dll"
    if dll.exists():
        env.setdefault("HROT_RUNNER_DLL", str(dll))
    js = out / f"{name}.json"
    cmd = [sys.executable, str(ROOT / "scripts/scale-rig.py"), "--terrain", "demo-city", "--men", "1000", "--vehicles", "50",
           "--aircraft", "10", "--warmup", "30", "--seconds", "30", "--json", str(js)] + rig_args
    print(f"--- {name}: {' '.join(f'{k}={v}' for k, v in env_extra.items()) or 'serial'}", flush=True)
    with open(out / f"{name}.txt", "w") as f:
        subprocess.run(cmd, env=env, stdout=f, stderr=subprocess.STDOUT, cwd=ROOT)
    try:
        nodes = json.loads(js.read_text())["nodes"]
        return dict(nodes["cgf"], _nodes=nodes)
    except Exception as e:
        print(f"    no result ({e}); see {out / (name + '.txt')}")
        return None


# ⭐ POC 3 (exp/parallel-sim-3) — each item against a recorded serial baseline (the sampled timer is the default now, R-4209).
POC3 = [
    ("rec-serial",   {}),                                                   # T-1: the sampled timer (default)
    ("rec-timed",    {"HROT_QUERY_SAMPLE": "1"}),                           # T-1: every call timed (POC 2's default)
    ("rec-qoff",     {"HROT_QUERY_TIMINGS": "0"}),                          # T-1: no timer
    ("rec-t2",       {"HROT_EQS_CONFIG_GATE": "1"}),                        # T-2: the EqsSensorConfig change gate
    ("rec-thr",      {"HROT_PARALLEL_SIM": "1", "HROT_SIM_MODE": "throughput"}),
    ("rec-t3",       {"HROT_PARALLEL_SIM": "1", "HROT_SIM_MODE": "throughput", "HROT_BRAIN_PRESCORE": "1"}),   # T-3: posture + ranking pre-score
    ("rec-t5",       {"FDP_EGRESS_OFFFRAME": "1", "HROT_PARALLEL_SYNC": "1"}),   # T-5: off-frame egress, its replica synced in parallel
    ("rec-all",      {"HROT_EQS_CONFIG_GATE": "1", "HROT_PARALLEL_SIM": "1", "HROT_SIM_MODE": "throughput", "HROT_BRAIN_PRESCORE": "1",
                      "HROT_BP_STAMP_ON_CHANGE": "1", "FDP_EGRESS_OFFFRAME": "1", "HROT_PARALLEL_SYNC": "1"}),
]


# ⭐ POC 4 (exp/parallel-sim-4) — the ONE worker pool (J-1) and the parallel loops on it (J-2): everything on with all threads and with one,
#   against a recorded serial baseline; every node's OS thread count printed (the rig's "threads", from /diagnostics/architecture).
_ALL = {"HROT_EQS_CONFIG_GATE": "1", "HROT_PARALLEL_SIM": "1", "HROT_SIM_MODE": "throughput", "HROT_BRAIN_PRESCORE": "1",
        "HROT_BP_STAMP_ON_CHANGE": "1", "FDP_EGRESS_OFFFRAME": "1", "HROT_PARALLEL_SYNC": "1"}
POC4 = [
    ("rec-serial",   {}),
    ("rec-all",      _ALL),                                  # everything on, the pool at its budget (cores - 2)
    ("rec-all-1",    dict(_ALL, HROT_SIM_THREADS="1")),       # everything on, ONE thread: what the pool itself buys
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--threads", type=int, nargs="+", default=[1, 2, 4, 8])
    ap.add_argument("--mode", choices=["deterministic", "throughput"], default="deterministic")
    ap.add_argument("--out", default="parallel-sim-results")
    ap.add_argument("--pin", action="store_true", help="pass --pin to the rig (Windows)")
    ap.add_argument("--cgf-cpus", type=int, default=0, metavar="N",
                    help="pass --cgf-cpus N to the rig (Windows): cgf alone on the top N logical CPUs, the other nodes on the rest (CE-4451 S-3)")
    ap.add_argument("--content", action="store_true",
                    help="also run the largest thread count with HROT_CONTENT_VERSION=1 (D-5 commit by content), and a RECORDED pair with/without it")
    ap.add_argument("--poc2", action="store_true",
                    help="POC 2: RECORDED serial vs each item (timer off, Q-1 stamp on change, Q-2 off-frame egress, Q-4 brain pre-score) and all together")
    ap.add_argument("--poc3", action="store_true",
                    help="POC 3: RECORDED serial vs timer sampled / every call / off, the EqsSensorConfig gate, the pre-score, the parallel replica sync, all together")
    ap.add_argument("--poc4", action="store_true",
                    help="POC 4: RECORDED serial vs everything on (the one worker pool) at its budget and at one thread; threads per node")
    a = ap.parse_args()
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    rig_args = ["--pin"] if a.pin else (["--cgf-cpus", str(a.cgf_cpus)] if a.cgf_cpus else [])
    if a.poc4:
        runs = {name: run(name, env, out, rig_args + ["--record"]) for name, env in POC4}
        report(runs, out, nodes=True)
        return
    if a.poc3:
        runs = {name: run(name, env, out, rig_args + ["--record"]) for name, env in POC3}
        report(runs, out)
        return
    if a.poc2:
        runs = {name: run(name, env, out, rig_args + ["--record"]) for name, env in POC2}
        report(runs, out)
        return
    runs = {"serial": run("serial", {}, out, rig_args)}
    for t in a.threads:
        runs[f"par-{t}"] = run(f"par-{t}", {"HROT_PARALLEL_SIM": "1", "HROT_SIM_THREADS": str(t), "HROT_SIM_MODE": a.mode},
                               out, rig_args)
    if a.content:
        t = max(a.threads)
        base = {"HROT_PARALLEL_SIM": "1", "HROT_SIM_THREADS": str(t), "HROT_SIM_MODE": a.mode}
        runs[f"par-{t}+cv"] = run(f"par-{t}-cv", dict(base, HROT_CONTENT_VERSION="1"), out, rig_args)
        runs[f"rec-{t}"] = run(f"rec-{t}", base, out, rig_args + ["--record"])
        runs[f"rec-{t}+cv"] = run(f"rec-{t}-cv", dict(base, HROT_CONTENT_VERSION="1"), out, rig_args + ["--record"])
    report(runs, out)


def report(runs, out, nodes=False):
    cols = [k for k, v in runs.items() if v]
    print("\nms/frame (CGF node)" + "".join(f"{c:>11}" for c in cols))
    print(f"{'whole frame':<26}" + "".join(f"{runs[c]['work_ms']:>11.1f}" for c in cols))
    for s in SYSTEMS:
        row = []
        for c in cols:
            rows = runs[c].get("all_ms") or runs[c]["top_ms"]   # ⭐ every system (the rig's all_ms) — top_ms cut at 8 read a dropped system as 0.00
            row.append(sum(ms for ms, n in rows if n.endswith("/" + s)))
        print(f"{s:<26}" + "".join(f"{v:>11.2f}" for v in row))
    print(f"{'garbage B/frame':<26}" + "".join(f"{runs[c]['garbage_b']:>11.0f}" for c in cols))
    if nodes:   # ⭐ POC 4 — every node's frame work and OS threads
        for n in ("cgf", "simhost", "orchestrator"):
            print(f"{n + ' work ms':<26}" + "".join(f"{(runs[c]['_nodes'].get(n) or {}).get('work_ms', 0):>11.1f}" for c in cols))
            print(f"{n + ' threads':<26}" + "".join(f"{str((runs[c]['_nodes'].get(n) or {}).get('threads')):>11}" for c in cols))
    print(f"\nraw reports: {out.resolve()}")


if __name__ == "__main__":
    main()
