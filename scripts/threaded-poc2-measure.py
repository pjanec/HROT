#!/usr/bin/env python3
"""threaded-poc2-measure.py — POC 2 / POC 3 of the threaded editor (CE-4439, CE-4442): ONE command that runs the same editor session twice on the 1 000-man rig —
the switch OFF (today's single-threaded editor) and HROT_THREADED_KERNEL=3 (POC 3: the node composed over S; --mode 2 for POC 2) — and prints the drawing thread's frame times and the
two-world sync cost side by side. Windows (8 cores): the window is hidden (HROT_HIDDEN_WINDOW=1, set by the driver); Linux: under Xvfb.

    dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release
    python3 scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --write-only
    python scripts/threaded-poc2-measure.py --out <dir> [--play-seconds 120] [--lock-tick] [--mask ui-no-inspector|map|ui-all]

Prints, per mode: the [FrameStats] frame times while playing (HROT_FRAME_STATS=1), the X-4 drawing-frame split (with the world / map only),
the [POC2] sync means, the worker faults and the WorldGuard hit count (HROT_WORLD_GUARD=1 adds a cost: run with --no-guard for clean timings).
"""
import argparse
import os
import re
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
import importlib.util as _ilu   # ⭐ F-5 — the ONE [FrameStats] parser lives in the session driver
_spec = _ilu.spec_from_file_location("ats", os.path.join(REPO, "scripts", "access-trace-session.py"))
ATS = _ilu.module_from_spec(_spec)
_spec.loader.exec_module(ATS)


def run(out, extra_env, play, guard):
    os.makedirs(out, exist_ok=True)
    env = dict(os.environ, HROT_FRAME_STATS="1")
    env.setdefault("HROT_QUERY_TIMINGS", "1")   # ⭐ R-4213 — the per-query timer is off by default; the measurement asks for it
    env.update(extra_env)
    if guard:
        env["HROT_WORLD_GUARD"] = "1"
        env["HROT_WORLD_GUARD_OUT"] = os.path.join(out, "guard.txt")
    cmd = [sys.executable, "-u", os.path.join(REPO, "scripts", "access-trace-session.py"), "--out", out, "--no-trace",
           "--play-seconds", str(play), "--stages", "load,select1,play,stepping,view3d"]
    with open(os.path.join(out, "session.log"), "w") as log:
        subprocess.run(cmd, env=env, stdout=log, stderr=subprocess.STDOUT, cwd=REPO, check=False)


def summarise(name, out):
    text = ""
    for f in sorted(os.listdir(out)):
        if f.startswith("editor-") and f.endswith(".log"):
            with open(os.path.join(out, f), errors="ignore") as fh:
                text += fh.read()
    stats = ATS.frame_stats(text)   # the one parser in the session driver
    frames = [m for m, _ in stats]
    p95 = [p for _, p in stats if p is not None]
    x4f = re.findall(r"\[X-4F\] (.*)", text)
    poc2 = re.findall(r"\[POC2\] (syncs .*)", text)
    faults = len(re.findall(r"worker fault", text))
    late = frames[len(frames) // 2:] or frames   # the second half: the scenario loaded and playing
    late95 = p95[len(p95) // 2:] or p95
    print(f"== {name}")
    if late:
        print(f"   frame time (second half of the run): mean of means {sum(late) / len(late):.1f} ms · worst p95 {max(late95):.1f} ms ({len(late)} periods)")
    if x4f:
        print(f"   drawing frames: {x4f[-1]}")
    if poc2:
        print(f"   sync: {poc2[-1]}")
    print(f"   worker faults: {faults}")
    g = os.path.join(out, "guard.txt")
    if os.path.exists(g):
        with open(g, errors="ignore") as fh:
            print("   guard: " + fh.readline().strip())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--play-seconds", type=int, default=120)
    ap.add_argument("--mask", default="ui-no-inspector")
    ap.add_argument("--lock-tick", action="store_true", help="the worker holds the world lock for its whole tick (HROT_TWO_WORLDS_LOCK_TICK=1)")
    ap.add_argument("--no-guard", action="store_true")
    ap.add_argument("--mode", default="3", choices=["2", "3"], help="2 = POC 2 (clone + retarget, preview only); 3 = POC 3 (the node COMPOSED over S — default)")
    a = ap.parse_args()
    masks = os.path.join(REPO, "docs", "blueprints", "batches", "threaded-poc-2", "presentation-masks.json")
    off, on = os.path.join(a.out, "off"), os.path.join(a.out, "two-worlds")
    run(off, {}, a.play_seconds, guard=False)
    two = {"HROT_THREADED_KERNEL": a.mode, "HROT_TWO_WORLDS_MASKS": masks, "HROT_TWO_WORLDS_MASK": a.mask}
    if a.lock_tick:
        two["HROT_TWO_WORLDS_LOCK_TICK"] = "1"
    run(on, two, a.play_seconds, guard=not a.no_guard)
    summarise("switch OFF (single-threaded)", off)
    summarise(f"HROT_THREADED_KERNEL={a.mode} (two worlds" + (", lock whole tick" if a.lock_tick else "") + ")", on)


if __name__ == "__main__":
    main()
