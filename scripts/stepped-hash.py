#!/usr/bin/env python3
"""stepped-hash.py — THE STEPPED DETERMINISM GATE (CE-4501; R-3035 "stepped mode is deterministic", R-4218).

Runs the REAL editor (hidden window on Windows, Xvfb on Linux — the one session driver, scripts/access-trace-session.py) once per entry of
--threads: loads the scenario, enters the preview PAUSED, steps it in fixed chunks with the SAME orders at the SAME steps, and reads
GET /world/hash?byType=1 — the world's content hash over every unmanaged component's FIELD bytes (never padding, M-D9) — then compares
every run with the first, type by type. Two runs that took the same steps agree exactly when their state is identical.

    python scripts/stepped-hash.py --threads 1,2,4,8                       # thread-count independence
    python scripts/stepped-hash.py --threads 1,1                           # repeatability (same config twice)
    python scripts/stepped-hash.py --threads 4,4 --rows WeaponChannel --bytes   # WHICH entities and WHICH bytes differ

Exit code 0 = state equal (EntityGuid excluded: minted at random for an entity created at run time), 1 = differs, 2 = a run failed.
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
    if a.kernel == "0": os.environ.pop("HROT_THREADED_KERNEL", None)   # the product as shipped: no threaded node
    else: os.environ["HROT_THREADED_KERNEL"] = a.kernel
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
        s.post("/sim/step", {"count": a.chunk}, timeout=600)
        s.post(f"/entities/{men[3]}/order", {"kind": "move", "x": 150, "y": 150})   # the same edit at the same step
        s.post("/sim/step", {"count": a.chunk * 2}, timeout=600)
        s.post(f"/entities/{men[5]}/order", {"kind": "hold"})
        s.post("/sim/step", {"count": a.chunk * 3}, timeout=600)
        q = "/world/hash?byType=1&rows=" + a.rows + ("&bytes=1" if a.bytes else "")
        return data(s.get(q, timeout=600))
    finally:
        s.stop()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--threads", default="1,2,4,8", help="one run per entry, e.g. 1,2,4,8 or 1,1")
    ap.add_argument("--scenario", default="scale-rig-demo-city-1000-2s")
    ap.add_argument("--chunk", type=int, default=20, help="the step chunk (the orders land after 1 and 3 chunks; 6 chunks in all)")
    ap.add_argument("--rows", default="SimTransform", help="a component type: name the entities whose row differs")
    ap.add_argument("--bytes", action="store_true", help="with --rows: print the differing byte offsets of each differing row")
    ap.add_argument("--kernel", default="3", choices=["0", "3"], help="3 = the threaded node (mode 3); 0 = the product as shipped")
    ap.add_argument("--port", type=int, default=8131)
    ap.add_argument("--out", default=os.path.join(REPO, ".stepped-hash"))
    ap.add_argument("--save", help="write every run's result to this JSON file")
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
        print(f"run {k}: {t_} thread(s) " + json.dumps({kk: v for kk, v in r.items() if kk not in ("types", "rows")}), flush=True)
    if a.save:
        with open(a.save, "w") as fh:
            json.dump({str(k): v for k, (_, v) in res.items()}, fh)

    if any("error" in r for _, r in res.values()):
        print("A RUN FAILED"); sys.exit(2)
    _, base = res[0]
    differs = False
    for k, (t, r) in res.items():
        if k == 0:
            continue
        types = sorted(n for n in set(base["types"]) | set(r["types"])
                       if base["types"].get(n) != r["types"].get(n) and n not in RANDOM_BY_DESIGN)
        rows = sorted((n for n in set(base.get("rows") or {}) | set(r.get("rows") or {})
                       if (base.get("rows") or {}).get(n, "").split("@")[0] != (r.get("rows") or {}).get(n, "").split("@")[0]), key=int)
        differs |= bool(types)
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
