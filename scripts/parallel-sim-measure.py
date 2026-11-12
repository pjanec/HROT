#!/usr/bin/env python3
"""Parallel-simulation experiment — the one-command measurement (exp/parallel-sim).

Runs scripts/scale-rig.py (1000 men, 50 vehicles, 10 aircraft, demo-city, headless) once serial and once per thread
count with HROT_PARALLEL_SIM=1, and prints the converted systems' ms/frame side by side.

    python scripts/parallel-sim-measure.py                      # serial + 1/2/4/8 threads (Deterministic)
    python scripts/parallel-sim-measure.py --threads 2 6 --mode throughput
    python scripts/parallel-sim-measure.py --content          # + commit by content (D-5), live and recorded

Build Release first:  dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release
Windows: HROT_HIDDEN_WINDOW=1 is set for every node (no window ever shows). Results land in --out (default ./parallel-sim-results).
"""
import argparse, json, os, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SYSTEMS = ["BrainTickSystem", "StressSystem", "CgfThreatEvaluationSystem", "CalloutSystem",
           "WeaponDispatcherSystem", "SquadCoordinationSystem"]


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
        return json.loads(js.read_text())["nodes"]["cgf"]
    except Exception as e:
        print(f"    no result ({e}); see {out / (name + '.txt')}")
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--threads", type=int, nargs="+", default=[1, 2, 4, 8])
    ap.add_argument("--mode", choices=["deterministic", "throughput"], default="deterministic")
    ap.add_argument("--out", default="parallel-sim-results")
    ap.add_argument("--pin", action="store_true", help="pass --pin to the rig (Windows)")
    ap.add_argument("--content", action="store_true",
                    help="also run the largest thread count with HROT_CONTENT_VERSION=1 (D-5 commit by content), and a RECORDED pair with/without it")
    a = ap.parse_args()
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    rig_args = ["--pin"] if a.pin else []
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
    print(f"\nraw reports: {out.resolve()}")


if __name__ == "__main__":
    main()
