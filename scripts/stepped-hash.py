#!/usr/bin/env python3
"""stepped-hash.py — THE STEPPED DETERMINISM GATE (CE-4501; R-3035 "stepped mode is deterministic", R-4218).

Runs the REAL editor (hidden window on Windows, Xvfb on Linux — the one session driver, scripts/access-trace-session.py) once per entry of
--threads: loads the scenario, enters the preview PAUSED, steps it in fixed chunks with the SAME orders at the SAME steps, and reads
GET /world/hash?byType=1 — the world's content hash over every unmanaged component's FIELD bytes (never padding, M-D9) — then compares
every run with the first, type by type. Two runs that took the same steps agree exactly when their state is identical.

    python scripts/stepped-hash.py --threads 1,2,4,8                       # thread-count independence
    python scripts/stepped-hash.py --threads 1,1                           # repeatability (same config twice)
    python scripts/stepped-hash.py --threads 4,4 --rows WeaponChannel --bytes   # WHICH entities and WHICH bytes differ

Exit code 0 = state equal (EntityGuid excluded: minted at random for an entity created at run time), 1 = differs, 2 = a run failed, or the runs took different numbers of steps (never compared: a different experiment).
Environment: HROT_PARALLEL_SIM=1 to run every per-entity phase (the gate's usual setting); --kernel 3 (default) runs the threaded node,
--kernel 0 the product as shipped (every switch off: the repeatability question, F-2).
"""
import argparse
import importlib.util
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_spec = importlib.util.spec_from_file_location("ats", os.path.join(REPO, "scripts", "access-trace-session.py"))
ats = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ats)
ats.NO_TRACE = True

RANDOM_BY_DESIGN = {"EntityGuid"}


def data(r):
    return (r or {}).get("data") or {}


def one_run(a, threads, k):
    out = os.path.join(a.out, f"run{k}-t{threads}")
    os.makedirs(out, exist_ok=True)
    os.environ["HROT_SIM_THREADS"] = str(threads)
    os.environ["HROT_THREADED_KERNEL"] = a.kernel   # explicit either way: since U-2 (CE-4531) the product's DEFAULT is the threaded node
    s = ats.Session(out, a.port)
    if not s.start(a.dll):
        raise RuntimeError("the editor did not answer")
    try:
        ats.load_scenario(s, a)
        ats.wait_state(s, "OperatingEdit", 900)
        ents = data(s.get("/entities", timeout=120))
        ents = ents if isinstance(ents, list) else ents.get("entities", [])
        men = sorted(e["networkId"] for e in ents if e.get("networkId"))[:20]
        if len(men) < 6:
            raise RuntimeError(f"only {len(men)} entities with a network id")
        s.post("/preview/enter", {"startPaused": True}, timeout=300)
        # ⭐ CE-4570 (X-4) — a paused entry takes ZERO steps: wait a second of real frames, then the world's own clock must still read 0. A slipped
        #   step is reported HERE, as the entry's (where Windows V-4 measured 121 for 120), never mistaken for a step of the run below.
        import time as _t
        _t.sleep(1.0)
        entry = (data(s.get("/status", timeout=60)) or {}).get("simTime")
        if entry not in (0, 0.0, None):
            raise RuntimeError(f"the paused preview entry took steps: simTime {entry} before the first /sim/step (CE-4570)")
        probes = []

        def step(n):   # --probe K: the same steps in pieces of K, the world hashed by type after each (where a run first parts)
            if not a.probe:
                s.post("/sim/step", {"count": n}, timeout=600); return
            for _ in range(n // a.probe):
                s.post("/sim/step", {"count": a.probe}, timeout=600)
                d = data(s.get("/world/hash?byType=1&rows=" + a.rows + ("&bytes=1" if a.bytes else ""), timeout=600))
                probes.append({"types": d.get("types") or {}, "rows": d.get("rows") or {}})

        step(a.chunk)
        s.post(f"/entities/{men[3]}/order", {"kind": "move", "x": 150, "y": 150})   # the same edit at the same step
        step(a.chunk * 2)
        s.post(f"/entities/{men[5]}/order", {"kind": "hold"})
        step(a.chunk * 3)
        q = "/world/hash?byType=1&rows=" + a.rows + ("&bytes=1" if a.bytes else "")
        r = data(s.get(q, timeout=600))
        if a.probe: r["probes"] = probes
        return r
    finally:
        s.stop()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--threads", default="1,2,4,8", help="one run per entry, e.g. 1,2,4,8 or 1,1")
    ap.add_argument("--scenario", default="scale-rig-demo-city-1000-2s")
    ap.add_argument("--chunk", type=int, default=20, help="the step chunk (the orders land after 1 and 3 chunks; 6 chunks in all)")
    ap.add_argument("--rows", default="SimTransform", help="a component type: name the entities whose row differs")
    ap.add_argument("--bytes", action="store_true", help="with --rows: print the differing byte offsets of each differing row")
    ap.add_argument("--kernel", default="3", choices=["0", "3"], help="3 = the threaded node (mode 3, the product default since U-2); 0 = no threaded node")
    ap.add_argument("--port", type=int, default=8131)
    ap.add_argument("--out", default=os.path.join(__import__("tempfile").gettempdir(), "hrot-stepped-hash"), help="the runs' logs (outside the repository)")
    ap.add_argument("--save", help="write every run's result to this JSON file")
    ap.add_argument("--probe", type=int, default=0, help="step in pieces of K steps (K divides --chunk) and hash after each: names the first piece where a run parts from run 0")
    ap.add_argument("--dll", default=os.path.join(REPO, "Hrot/Runner/Hrot.ClusterRunner/bin/Release/net8.0/Hrot.ClusterRunner.dll"))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    res = {}
    for k, t in enumerate(int(x) for x in a.threads.split(",")):
        try:
            res[k] = (t, one_run(a, t, k))
        except Exception as ex:   # a failed run is reported, never read as a difference
            res[k] = (t, {"error": str(ex)})
        t_, r = res[k]
        print(f"run {k}: {t_} thread(s) " + json.dumps({kk: v for kk, v in r.items() if kk not in ("types", "rows", "probes")}), flush=True)
    if a.save:
        with open(a.save, "w") as fh:
            json.dump({str(k): v for k, (_, v) in res.items()}, fh)

    if any("error" in r for _, r in res.values()):
        print("A RUN FAILED"); sys.exit(2)
    _, base = res[0]
    # ⭐ the steer of 2026-11-13 (STEER_Coordinator_To_Backend_Preview_Extra_Step) — runs are compared only at the SAME sim time: a run
    #   that took a different number of steps is a different experiment, never a state difference (📐 Windows V-4: 2 of 7 runs ended at
    #   121 steps, and every difference followed from that one step)
    steps = {k: r.get("simTime") for k, (_, r) in res.items()}
    if len(set(steps.values())) > 1:
        print("A RUN TOOK A DIFFERENT NUMBER OF STEPS — not compared: " + " · ".join(f"run {k} simTime {v}" for k, v in steps.items()))
        sys.exit(2)
    differs = False
    for k, (t, r) in res.items():
        if k == 0:
            continue
        types = sorted(n for n in set(base["types"]) | set(r["types"])
                       if base["types"].get(n) != r["types"].get(n) and n not in RANDOM_BY_DESIGN)
        rows = sorted((n for n in set(base.get("rows") or {}) | set(r.get("rows") or {})
                       if (base.get("rows") or {}).get(n, "").split("@")[0] != (r.get("rows") or {}).get(n, "").split("@")[0]), key=int)
        differs |= bool(types)
        for i, (pb, pr) in enumerate(zip(base.get("probes") or [], r.get("probes") or [])):
            first = sorted(n for n in set(pb["types"]) | set(pr["types"]) if pb["types"].get(n) != pr["types"].get(n) and n not in RANDOM_BY_DESIGN)
            if first:
                moved = sorted((n for n in set(pb["rows"]) | set(pr["rows"])
                                if pb["rows"].get(n, "").split("@")[0] != pr["rows"].get(n, "").split("@")[0]), key=int)
                print(f"run 0 vs {k}: first parts after step {(i + 1) * a.probe}: {first} · {a.rows} rows: "
                      + " ".join(f"#{n}" for n in moved[:30]), flush=True); break
        print(f"run 0 vs {k} ({t} threads): {len(types)} type(s) differ {types[:40]} · {a.rows} rows differ: {len(rows)} "
              + " ".join(f"#{n}" for n in rows[:30]), flush=True)
        if a.bytes:
            for n in rows[:20]:
                x = bytes.fromhex(base["rows"][n].split("@")[2]); y = bytes.fromhex(r["rows"][n].split("@")[2])
                offs = [i for i in range(min(len(x), len(y))) if x[i] != y[i]]
                print(f"    row #{n}: {len(x)} B, differing offsets {offs[:32]}", flush=True)
    print("STATE DIFFERS" if differs else "STATE EQUAL (EntityGuid excluded)")
    sys.exit(1 if differs else 0)


if __name__ == "__main__":
    main()
