#!/usr/bin/env python3
"""
CE-202's real end-to-end control: run a scenario twice from a cold process and
compare the world state, entity by entity.

    python3 scripts/determinism-probe.py --port 8131 --scenario hill-attack-close \
        --steps 600 --out /tmp/run-a.json
    # ... restart the host ...
    python3 scripts/determinism-probe.py --port 8131 --scenario hill-attack-close \
        --steps 600 --out /tmp/run-b.json
    python3 scripts/determinism-probe.py --compare /tmp/run-a.json /tmp/run-b.json

⛔⛔⛔ THE MISTAKE THIS SCRIPT EXISTS TO PREVENT, and it was mine.
CE-202 first claimed "--mode editor: 8 of 8 entities byte-identical across two runs".
The steps had never landed: a freshly loaded editor sits in EDIT mode, where the sim
clock does not run, so /sim/step was dropped and simTime stayed 0.0 in BOTH runs. Two
worlds frozen at t=0 are of course identical. The route's own refusal text said what to
do — "call POST /preview/enter ... to advance" — and I read it without acting on it.

  ⇒ ① this script ENTERS PREVIEW before stepping, and
    ② it REFUSES to write a capture whose clock did not move.

A control that can pass without exercising anything is worse than no control, because
it is believed.
"""
import argparse
import json
import sys
import urllib.request

FIXED_DT = 1.0 / 60.0


def call(port, path, method="GET", body=None, timeout=30):
    # localhost, never 127.0.0.1 — the listener binds the HOSTNAME (RUNBOOK §2.1).
    req = urllib.request.Request(
        f"http://localhost:{port}{path}",
        data=json.dumps(body).encode() if body is not None else None,
        method=method)
    if body is not None:
        req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode())


def data(resp):
    return resp.get("data") if isinstance(resp, dict) and "data" in resp else resp


def run(port, scenario, steps):
    call(port, "/scenario/load/live", "POST",
         {"name": scenario, "waitForReady": True}, timeout=120)
    call(port, "/preview/enter", "POST", {"startPaused": True}, timeout=30)

    before = data(call(port, "/status")).get("simTime") or 0.0
    left = steps
    while left > 0:
        batch = min(25, left)
        call(port, "/sim/step", "POST", {"count": batch}, timeout=120)
        left -= batch
    after = data(call(port, "/status")).get("simTime") or 0.0

    if after <= before:
        raise SystemExit(
            f"REFUSING TO CAPTURE: simTime did not advance ({before} -> {after}). "
            "Every comparison would be between two frozen worlds and would report "
            "'identical' for the wrong reason. Is the host in preview?")

    expected = before + steps * FIXED_DT
    drift = abs(after - expected)
    if drift > FIXED_DT:
        # Not fatal — say it, because a wall-clock contribution destroys reproducibility
        # and would otherwise show up as an unexplained entity mismatch.
        print(f"  ! simTime {after} is {drift:.4f}s off {steps} fixed steps ({expected}) "
              "— something other than the step is advancing the clock", file=sys.stderr)

    # GET /entities answers a bare LIST, not an {entities:[...]} envelope.
    ents = data(call(port, "/entities", timeout=60))
    if isinstance(ents, dict):
        ents = ents.get("entities") or []
    ids = [e.get("networkId") for e in ents]

    states = {}
    for nid in ids:
        if nid is None:
            continue
        states[str(nid)] = data(call(port, f"/entities/{nid}/state", timeout=30))

    return {"scenario": scenario, "steps": steps,
            "simTime": {"before": before, "after": after, "expectedAfter": expected},
            "entityIds": sorted(str(i) for i in ids if i is not None),
            "states": states}


def compare(a, b):
    problems = []
    if a["entityIds"] != b["entityIds"]:
        problems.append(f"entity SET differs: only-A={sorted(set(a['entityIds'])-set(b['entityIds']))} "
                        f"only-B={sorted(set(b['entityIds'])-set(a['entityIds']))}")
    if a["simTime"]["after"] != b["simTime"]["after"]:
        problems.append(f"simTime differs: {a['simTime']['after']} vs {b['simTime']['after']}")

    same, differ = [], []
    for nid in a["entityIds"]:
        if nid not in b["states"]:
            continue
        (same if a["states"][nid] == b["states"][nid] else differ).append(nid)

    print(f"identical entities: {len(same)} / {len(same) + len(differ)}")
    if differ:
        print(f"  differing: {differ}")
        for nid in differ[:2]:
            for key in sorted(set(a["states"][nid]) | set(b["states"][nid])):
                if a["states"][nid].get(key) != b["states"][nid].get(key):
                    print(f"    {nid}.{key}: {str(a['states'][nid].get(key))[:110]}")
                    print(f"    {nid}.{key}: {str(b['states'][nid].get(key))[:110]}")
    for p in problems:
        print(" ", p)
    return 0 if not differ and not problems else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8131)
    ap.add_argument("--scenario", default="hill-attack-close")
    ap.add_argument("--steps", type=int, default=600)
    ap.add_argument("--out")
    ap.add_argument("--compare", nargs=2, metavar=("A", "B"))
    args = ap.parse_args()

    if args.compare:
        return compare(json.load(open(args.compare[0])), json.load(open(args.compare[1])))

    snap = run(args.port, args.scenario, args.steps)
    print(f"  simTime {snap['simTime']['before']} -> {snap['simTime']['after']} "
          f"({args.steps} fixed steps), {len(snap['entityIds'])} entities")
    text = json.dumps(snap, indent=2, sort_keys=True)
    if args.out:
        open(args.out, "w").write(text)
        print(f"wrote {args.out}")
    else:
        print(text[:2000])
    return 0


if __name__ == "__main__":
    sys.exit(main())
