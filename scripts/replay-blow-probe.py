#!/usr/bin/env python3
"""CE-4062 probe -- is the blow drawn when a recording is STEPPED across it in the Replay Browser?

One command, two windowed processes under Xvfb (the build must be current: dotnet build the ClusterRunner first):
  1. the editor runs ua-civ-melee LIVE, stepped to 4.4 s (the rioter's blow lands at ~4.17 s), and films the policeman: live.png;
     then it steps on to 10 s and stops, which closes the recording;
  2. the Replay Browser opens that recording, seeks to 3.0 s, STEPS one frame at a time to 4.4 s across the blow, and films the
     SAME shot: replay.png.
The two stills must show the same pose: the rioter's arm forward (the swing's follow-through), the policeman mid-fall.

  python3 scripts/replay-blow-probe.py <out-dir> [--recording <node_1.fdp>]   (--recording skips step 1)

Uses the debug routes only (/scenario/load/live, /sim/step, /replay, /replay/seek, /film/shot, /film/capture) through
scripts/utility-demo-check.py's helpers. docs/blueprints/Blueprint_Issues_Tracker.md CE-4062.
"""
import glob, importlib.util, json, os, subprocess, sys, time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
spec = importlib.util.spec_from_file_location("udc", os.path.join(REPO, "scripts", "utility-demo-check.py"))
udc = importlib.util.module_from_spec(spec); spec.loader.exec_module(udc)

OUT = os.path.abspath(sys.argv[1]); os.makedirs(OUT, exist_ok=True)
RECORDING = sys.argv[sys.argv.index("--recording") + 1] if "--recording" in sys.argv else None
SCENARIO, BLOW_AT, FROM = "ua-civ-melee", 4.4, 3.0


def launch(port, mode, extra=()):
    udc.BASE = f"http://localhost:{port}"
    log = open(os.path.join(OUT, f"{mode}.log"), "w")
    p = subprocess.Popen(["xvfb-run", "-a", "-s", "-screen 0 1600x1000x24", "dotnet", udc.RUNNER, "--mode", mode, *extra],
                         cwd=REPO, env=dict(os.environ, HROT_DEBUG_API_PORT=str(port)), stdout=log, stderr=subprocess.STDOUT,
                         start_new_session=True)
    if not udc.wait_for(lambda: udc.data(udc.call("GET", "/status")), 240, every=1):
        udc.stop(p); sys.exit(f"{mode} did not answer")
    return p


def policeman_shot():
    rows = udc.data(udc.call("GET", "/entities")) or []
    nid = next(r.get("networkId") or r.get("id") for r in rows if r.get("name") == "Policeman")
    return {"follow": nid, "distance": 7, "yawDeg": 200, "pitchDeg": -10}


def film(name):
    udc.call("POST", "/film/shot", policeman_shot()); time.sleep(0.5)
    udc.call("POST", "/film/capture", {"path": os.path.join(OUT, name), "width": 1280, "height": 720})
    print(f"wrote {os.path.join(OUT, name)}", flush=True)


def step_to(t, clock):
    """one frame at a time, each step waited for (a step sent faster than frames is dropped)"""
    while (clock() or 0) < t - 0.009:
        before = clock() or 0
        udc.call("POST", "/sim/step", {"count": 1})
        udc.wait_for(lambda: (clock() or 0) > before + 1e-6, 10, every=0.05)


if RECORDING is None:
    ed = launch(8141, "editor")
    try:
        udc.call("POST", "/scenario/load/live", {"name": SCENARIO, "waitForReady": True})
        udc.call("POST", "/sim/pause", {})
        step_to(BLOW_AT, udc.sim_time)
        film("live.png")
        step_to(10.0, udc.sim_time)
        udc.call("POST", "/cluster/transition", {"target": "Idle"}); time.sleep(8)
    finally:
        udc.stop(ed)
    RECORDING = sorted(glob.glob("/tmp/FDP_Temp/nodes/node-1/exercises/*/node_1.fdp"), key=os.path.getmtime)[-1]
print(f"recording {RECORDING}", flush=True)

rb = launch(8142, "replaybrowser", ["--no-wait"])
try:
    udc.call("POST", "/replay", {"path": RECORDING}); time.sleep(3)
    udc.call("POST", "/replay/seek", {"atSimSeconds": FROM}); time.sleep(1.5)
    step_to(BLOW_AT, lambda: (udc.data(udc.call("GET", "/replay/status")) or {}).get("simSeconds"))
    film("replay.png")
finally:
    udc.stop(rb)
