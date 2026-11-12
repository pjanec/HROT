#!/usr/bin/env python3
"""Parallel-simulation experiment — the one-command measurement (exp/parallel-sim).

Runs scripts/scale-rig.py (1000 men, 50 vehicles, 10 aircraft, demo-city, headless) once serial and once per thread
count with HROT_PARALLEL_SIM=1, and prints the converted systems' ms/frame side by side.

    python scripts/parallel-sim-measure.py                      # serial + 1/2/4/8 threads (Deterministic)
    python scripts/parallel-sim-measure.py --threads 2 6 --mode throughput

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
    a = ap.parse_args()
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    rig_args = ["--pin"] if a.pin else []
    runs = {"serial": run("serial", {}, out, rig_args)}
    for t in a.threads:
        runs[f"par-{t}"] = run(f"par-{t}", {"HROT_PARALLEL_SIM": "1", "HROT_SIM_THREADS": str(t), "HROT_SIM_MODE": a.mode},
                               out, rig_args)
    cols = [k for k, v in runs.items() if v]
    print("\nms/frame (CGF node)" + "".join(f"{c:>10}" for c in cols))
    print(f"{'whole frame':<26}" + "".join(f"{runs[c]['work_ms']:>10.1f}" for c in cols))
    for s in SYSTEMS:
        row = []
        for c in cols:
            row.append(sum(ms for ms, n in runs[c]["top_ms"] if n.endswith("/" + s)))
        print(f"{s:<26}" + "".join(f"{v:>10.2f}" for v in row))
    print(f"{'garbage B/frame':<26}" + "".join(f"{runs[c]['garbage_b']:>10.0f}" for c in cols))
    print(f"\nraw reports: {out.resolve()}")


if __name__ == "__main__":
    main()
