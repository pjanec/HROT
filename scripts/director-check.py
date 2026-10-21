#!/usr/bin/env python3
"""Drive the editor's film director over HTTP — play a reel live, film it, or scout every take — and print what it reported.

⭐ CE-4038 (docs/DESIGN_Film_Director.md; RUNBOOK_Filming.md §0a). Launches ClusterRunner --mode editor under Xvfb (the demo checker's
launch) on --port, or uses one already there (--attach).

  scripts/director-check.py live highlights               # the reel on screen in real time; per take: status, realtime factor, beats
  scripts/director-check.py capture highlights --dir /tmp/films/highlights --size 640x360
  scripts/director-check.py scout-all                     # every seeded take once (nothing baked): found / missing beat / name not found
  scripts/director-check.py scout-all --bake              # …and bake the found times into the takes
"""
import argparse, importlib.util, json, os, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("udc", os.path.join(HERE, "utility-demo-check.py"))
udc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(udc)


def must(resp, what):
    if not isinstance(resp, dict) or resp.get("http") not in (None, 200) or resp.get("ok") is False:
        sys.exit(f"{what} failed: {json.dumps(resp)[:600]}")
    return udc.data(resp)


def wait_done(limit_s):
    """Polls the director until it is Done or Stopped (or the limit passes); the last status."""
    until = time.time() + limit_s
    st = {}
    while time.time() < until:
        st = must(udc.call("GET", "/director/status"), "director/status") or {}
        if st.get("phase") in ("Done", "Stopped"):
            return st
        time.sleep(1)
    udc.call("POST", "/director/stop", {})
    st["timedOut"] = True
    return st


def report(st):
    for r in st.get("reports") or []:
        print(f"   {r['scenario']}/{r['take']:<6} {r['status']:<12} steps {r['steps']:>5}  ×{r['realtimeFactor']:<6} "
              f"max/frame {r['maxStepsInAFrame']}  unretired {r['unretiredSteps']}  late {r['lateFrames']}  frames {r['frames']}  "
              f"found {r['found']}  missing {r['missing']}  {('problems ' + str(r['problems'])) if r['problems'] else ''}", flush=True)
    if st.get("error"):
        print(f"   ERROR {st['error']}")
    if st.get("movie"):
        print(f"   movie {st['movie']}")


def scout_all(args):
    takes = must(udc.call("GET", "/director/takes"), "director/takes")
    rows = []
    for s in takes["scenarios"]:
        for t in s["takes"]:
            must(udc.call("POST", "/director/scout", {"scenario": s["scenario"], "take": t, "bake": args.bake}), "director/scout")
            st = wait_done(args.limit)
            r = (st.get("reports") or [{}])[0]
            names = [p for p in r.get("problems", []) if "no unit" in p]
            verdict = "name not found" if names else ("missing beat" if r.get("missing") else ("found" if r.get("status") == "ok" else r.get("status")))
            rows.append((s["scenario"], t, verdict, r.get("found"), r.get("missing"), names or r.get("problems"), r.get("steps"), r.get("wallSeconds")))
            print(f"   {s['scenario']}/{t}: {verdict}  found {r.get('found')}  missing {r.get('missing')}  {names or ''}  "
                  f"({r.get('steps')} steps, {r.get('wallSeconds')} s)", flush=True)
    with open(args.out, "w") as f:
        json.dump([{"scenario": a, "take": b, "verdict": c, "found": d, "missing": e, "problems": g, "steps": h, "wallSeconds": w}
                   for a, b, c, d, e, g, h, w in rows], f, indent=1)
    counts = {}
    for row in rows:
        counts[row[2]] = counts.get(row[2], 0) + 1
    print(f"   {len(rows)} takes: {counts} — table in {args.out}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("what", choices=["live", "capture", "scout-all"])
    ap.add_argument("reel", nargs="?")
    ap.add_argument("--port", type=int, default=8117)
    ap.add_argument("--attach", action="store_true")
    ap.add_argument("--dir", default="/tmp/director-film")
    ap.add_argument("--size", default="1280x720")
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--bake", action="store_true", help="scout-all: write the found times into the takes")
    ap.add_argument("--limit", type=float, default=900, help="seconds to wait for one run")
    ap.add_argument("--out", default="/tmp/director-scout.json")
    args = ap.parse_args()
    udc.BASE = f"http://localhost:{args.port}"
    proc = None if args.attach else udc.launch(args.port, "editor")
    try:
        started = time.time()
        if args.what == "scout-all":
            scout_all(args)
        else:
            if not args.reel:
                sys.exit("name the reel (GET /director/reels lists them)")
            body = {"reel": args.reel}
            if args.what == "capture":
                body["capture"] = {"dir": args.dir, "fps": args.fps, "size": args.size}
            must(udc.call("POST", "/director/play", body), "director/play")
            report(wait_done(args.limit))
        print(f"   {time.time() - started:.0f} s")
    finally:
        if proc is not None:
            udc.stop(proc)


if __name__ == "__main__":
    main()
