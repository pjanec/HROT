#!/usr/bin/env python3
"""crowd.py — CE-4651 (U-16): the 1000-people test in the Stechovice look level.

    python scripts/unreal/crowd.py <export>/<name> --project D:/Work/HrotUnreal/HrotUnreal.uproject --shots <folder> [--count 1000] [--radius 180]

Needs look.py to have built /Game/Maps/<name>_look, and the City Sample Crowd content in the project. The people are saved into the
look level (folder "Crowd"). Memory is watched (as look.py).
"""
import argparse, json, os, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from look import free_gb   # one watchdog


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export"); ap.add_argument("--project", required=True); ap.add_argument("--shots", required=True)
    ap.add_argument("--engine", default=r"C:\UE_5.8\UE_5.8"); ap.add_argument("--count", type=int, default=1000)
    ap.add_argument("--radius", type=float, default=180.0); ap.add_argument("--min-free-gb", type=float, default=6.0)
    ap.add_argument("--steps", default="100,250,500")
    ap.add_argument("--people", choices=("demo", "soldiers"), default="demo",
                    help="demo: the City Sample crowd demo's people; soldiers: state-driven armed US soldiers (hrot_person)")
    ap.add_argument("--merge", action="store_true", help="soldiers: merge each body's parts into one mesh")
    ap.add_argument("--fire", type=float, default=0.0, help="soldiers: each fires bursts at this many rounds/s (UHrotShotRenderer)")
    ap.add_argument("--all-niagara", action="store_true", help="--fire: the old way (every extra spawned per shot), for comparison")
    ap.add_argument("--play", action="store_true", help="after the last step: measure again in Simulate, the people shown then hidden")
    ap.add_argument("--play-variants", default=None, help="--play: comma list of always,rendered,uro,single — measured in one session")
    ap.add_argument("--impact-rate", type=float, default=None, help="--fire: impacts per second (UHrotShotRenderer.ImpactPerSecond)")
    ap.add_argument("--max-lights", type=int, default=None, help="--fire: flash lights at once")
    ap.add_argument("--only", choices=("smoke", "shell", "impact"), default=None, help="--fire: only this near extra (what each costs)")
    a = ap.parse_args()
    folder = os.path.abspath(a.export); name = os.path.basename(folder)
    views = {v["name"]: v for v in json.load(open(os.path.join(folder, f"{name}.views.json"), encoding="utf-8"))["views"]}
    street = views["street"]
    fwd = lambda p: os.path.abspath(p).replace("\\", "/")
    os.makedirs(a.shots, exist_ok=True)
    job = {"lookLevel": "/Game/Maps/" + name.replace("-", "_") + "_look", "crowdLevel": "/Game/CitySampleCrowd/Maps/CitySampleCrowd_LVL",
           "count": a.count, "steps": [int(x) for x in a.steps.split(",")], "centre": street["target"][:2], "radius": a.radius,
           "views": [views[k] for k in ("street", "village-aerial") if k in views], "shots": fwd(a.shots),
           "out": fwd(os.path.join(folder, "crowd-result.json")), "measureSeconds": 10, "settleSeconds": 15, "minFreeGb": a.min_free_gb,
           "people": a.people, "merge": a.merge, "fire": a.fire, "allNiagara": a.all_niagara,
           "impactPerSecond": a.impact_rate, "play": a.play, "playVariants": a.play_variants.split(",") if a.play_variants else None, "maxLights": a.max_lights,
           **({"nearSmoke": a.only == "smoke", "nearShell": a.only == "shell", "impacts": a.only == "impact"} if a.only else {})}
    jp = os.path.join(folder, "crowd-job.json"); json.dump(job, open(jp, "w"), indent=1)
    if os.path.exists(job["out"]): os.remove(job["out"])
    exe = os.path.join(a.engine, "Engine", "Binaries", "Win64", "UnrealEditor.exe")
    t = time.time()
    proc = subprocess.Popen([exe, a.project, f"-ExecutePythonScript={os.path.join(HERE, 'hrot_crowd.py')}", "-unattended", "-nosplash",
                             "-NoSound", f"-abslog={os.path.join(folder, 'crowd-editor.log')}"], env=dict(os.environ, HROT_CROWD_JOB=jp))
    lowest = None
    while proc.poll() is None:
        f = free_gb(); lowest = f if lowest is None else min(lowest, f)
        if f is not None and f < a.min_free_gb:
            print(f"free memory {f:.1f} GB < {a.min_free_gb} GB - stopping the editor")
            subprocess.run(["taskkill", "/F", "/T", "/PID", str(proc.pid)], capture_output=True); break
        time.sleep(5)
    print(f"editor exit {proc.wait()} after {time.time() - t:.0f} s; lowest free memory {None if lowest is None else round(lowest, 1)} GB")
    if os.path.exists(job["out"]):
        r = json.load(open(job["out"], encoding="utf-8"))
        print(json.dumps({k: r.get(k) for k in ("ok", "why", "people", "measure", "log")}, indent=1))
    else:
        print("no result - see crowd-editor.log")
    # partial results are written after every measurement


if __name__ == "__main__":
    main()
