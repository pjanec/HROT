#!/usr/bin/env python3
"""threaded-scale-measure.py — N-16 (CE-4478): the threaded editor's NUMBERS at 1 … 12 sim threads, on Windows and Linux alike.

For each thread count it starts the real editor (HROT_THREADED_KERNEL=3, the brains / squads / movement as parallel phases,
HROT_SIM_THREADS=<n>), loads the 1 000-man rig, PLAYS for --play-seconds, then PAUSES, and prints one table:

  * the sim tick period while playing — wall seconds per tick (GET /world/hash's tick, two samples)
  * the UI frame time playing / paused — the drawing thread's [FrameStats] (HROT_FRAME_STATS=1), each half of the run
  * BrainTick and the other heaviest systems — GET /diagnostics/architecture (recent average ms per call)
  * the staging buffers' and every world's bytes — the memory census (HROT_MEMORY_CENSUS=1, worlds by creator)
  * the editor's working set — the same census (and /diagnostics/architecture's process block)

    dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release --no-restore
    python scripts/threaded-scale-measure.py --out <dir> [--threads 1,2,4,6,8,12] [--play-seconds 60] [--dll <ClusterRunner.dll>]

Windows: the window is hidden (HROT_HIDDEN_WINDOW=1, set by the session driver); Linux: under Xvfb. It drives the editor through the ONE
session driver (scripts/access-trace-session.py's Session) — no second copy of start / stop / HTTP.
"""
import argparse
import glob
import importlib.util
import json
import os
import re
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_spec = importlib.util.spec_from_file_location("ats", os.path.join(REPO, "scripts", "access-trace-session.py"))
ats = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ats)
ats.NO_TRACE = True


def data(r):
    return (r or {}).get("data") or {}


def tick(s):
    return data(s.get("/world/hash", timeout=300)).get("tick")


def systems(s, full=None):
    """(system name, recent avg ms) over every subsystem of this node, the heaviest first; <full> collects every entry with its phase / module."""
    rows = []
    for sub in data(s.get("/diagnostics/architecture", timeout=120)).get("subsystems") or []:
        for sy in sub.get("systems") or []:
            ms = sy.get("recentAvgMs") or sy.get("avgMs") or 0
            rows.append((sy.get("systemName", "?"), float(ms)))
            if full is not None:
                full.append({"system": sy.get("systemName"), "phase": sy.get("phase"), "module": sy.get("module"), "ms": float(ms),
                             "subsystem": sub.get("subsystem")})
    rows.sort(key=lambda r: -r[1])
    return rows


def frame_means(log):
    with open(log, errors="ignore") as fh:
        text = fh.read()
    return [float(m.group(1)) for m in re.finditer(r"\[FrameStats\] \d+ frames\s+mean ([0-9.]+) ms", text)]


def census(out):
    files = sorted(glob.glob(os.path.join(out, "census*.json")), key=os.path.getmtime)
    if not files:
        return None
    with open(files[-1], errors="ignore") as fh:
        return json.load(fh)


def measure(a, threads):
    out = os.path.join(a.out, f"t{threads}")
    os.makedirs(out, exist_ok=True)
    masks = os.path.join(REPO, "docs", "blueprints", "batches", "threaded-poc-2", "presentation-masks.json")
    os.environ.update({
        "HROT_THREADED_KERNEL": "3", "HROT_SIM_THREADS": str(threads), "HROT_FRAME_STATS": "1",
        "HROT_TWO_WORLDS_MASKS": masks, "HROT_TWO_WORLDS_MASK": a.mask,
        "HROT_MEMORY_CENSUS": "1", "HROT_MEMORY_CENSUS_OUT": os.path.join(out, "census"),
    })
    s = ats.Session(out, a.port)
    row = {"threads": threads}
    if not s.start(a.dll):
        row["error"] = "the editor did not answer"
        return row
    try:
        ats.load_scenario(s, a)
        ats.wait_state(s, "OperatingEdit", 900)
        s.post("/sim/play", {}, timeout=180)
        time.sleep(a.play_seconds / 4)                        # settle: the brains wake, paths come back
        t0, w0 = tick(s), time.time()
        time.sleep(a.play_seconds / 2)
        t1, w1 = tick(s), time.time()
        if t0 is not None and t1 is not None and t1 > t0:
            row["tickMs"] = round((w1 - w0) * 1000.0 / (t1 - t0), 2)
        full = []
        row["systems"] = systems(s, full)[:8]
        row["allSystems"] = sorted(full, key=lambda x: -x["ms"])   # every system, its phase and module (M-4: what is still serial)
        time.sleep(a.play_seconds / 4)
        s.post("/sim/pause", {}, timeout=60)
        row["uiPlayingMs"] = frame_means(os.path.join(out, f"editor-{s.gen}.log"))
        time.sleep(15)                                        # paused frames
        frames = frame_means(os.path.join(out, f"editor-{s.gen}.log"))
        row["uiPausedMs"] = frames[len(row["uiPlayingMs"]):]
        time.sleep(6)                                         # one more census period
        row["census"] = census(out)
    finally:
        s.stop()
    return row


def mean(xs):
    xs = [x for x in xs or [] if x > 0]
    return sum(xs) / len(xs) if xs else float("nan")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--threads", default="1,2,4,6,8,12")
    ap.add_argument("--play-seconds", type=int, default=60)
    ap.add_argument("--port", type=int, default=8131)
    ap.add_argument("--scenario", default="scale-rig-demo-city-1000-2s")
    ap.add_argument("--mask", default="ui-no-inspector")
    ap.add_argument("--dll", default=os.path.join(REPO, "Hrot/Runner/Hrot.ClusterRunner/bin/Release/net8.0/Hrot.ClusterRunner.dll"))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    rows = [measure(a, int(t)) for t in a.threads.split(",")]
    with open(os.path.join(a.out, "numbers.json"), "w") as fh:
        json.dump(rows, fh, indent=1)
    print(f"{'threads':>7} {'tick ms':>8} {'UI play':>8} {'UI paused':>9} {'BrainTick':>9}  heaviest systems (ms)")
    for r in rows:
        if "error" in r:
            print(f"{r['threads']:>7}  {r['error']}")
            continue
        sysms = dict(r.get("systems") or [])
        brain = next((v for k, v in sysms.items() if "BrainTick" in k), float("nan"))
        top = ", ".join(f"{k} {v:.1f}" for k, v in (r.get("systems") or [])[:4])
        print(f"{r['threads']:>7} {r.get('tickMs', float('nan')):>8.1f} {mean(r.get('uiPlayingMs')):>8.1f} {mean(r.get('uiPausedMs')):>9.1f} {brain:>9.1f}  {top}")
    for r in rows:
        c = r.get("census")
        if not c:
            continue
        proc = c.get("process") or {}
        print(f"-- memory at {r['threads']} threads: working set {proc.get('workingSet', 0) / 2**20:.0f} MB "
              f"(peak {proc.get('peakWorkingSet', 0) / 2**20:.0f}), GC heap {proc.get('gcHeap', 0) / 2**20:.0f} MB")
        worlds = sorted(c.get("worlds") or [], key=lambda w: -(w.get("nativeBytes", 0) + w.get("managedSlotBytes", 0)))
        for w in worlds[:8]:
            print(f"     {(w.get('nativeBytes', 0) + w.get('managedSlotBytes', 0)) / 2**20:7.1f} MB  {w.get('count')} world(s), "
                  f"{w.get('entities')} entities — {w.get('creator')}")


if __name__ == "__main__":
    main()
