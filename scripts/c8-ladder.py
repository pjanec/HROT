#!/usr/bin/env python3
"""CE-8009 (C-8, RV-3) — the stochastic units' calibration LADDER, rung 1 (9 v 9 riflemen in the open at 100 / 200 / 275 m).

  python3 scripts/c8-ladder.py physical [--seeds 30] [--rungs c8-open-100,...] [--out DIR]   # run the physical rungs (a fresh headless editor per run)
  python3 scripts/c8-ladder.py compare --physical DIR --stochastic FILE                      # the verdict table

physical: each rung × seed runs `Hrot.ClusterRunner --mode editor --headless --verify <rung>` with HROT_RUN_SEED=<seed> (the run seed, CE-8003)
and keeps the report; the rung's checks (scripts/gen-c8-ladder.py) record the first time 1…9 men of each side are down.
stochastic: the JSON C8LadderStochasticTests writes with HROT_C8_OUT (the same rows, the same seeds).

Metrics: the LOSS-EXCHANGE RATIO (all Red down ÷ all Blue down over the runs — the ratio of totals, R-8011; its physical CI by bootstrap),
the TOTAL down (the informative one for a symmetric rung) and the TIME TO 50 % LOSSES (the first side to lose 5; the run's end when neither does).
PASS (C-8): the stochastic mean inside the physical 95 % CI ± 15 % (when the physical spread is ~0: ± 15 % of the physical mean).
"""
import argparse, glob, json, math, os, statistics, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUNNER = os.path.join(ROOT, "Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll")
RUNGS = ["c8-open-100", "c8-open-200", "c8-open-275"]   # ⭐ R-8009 — 275, inside the rifle's reach for every pair
RUN_FOR = 120


def first_times(report):
    """{"Blue": [t1..t9 or None], "Red": [...]} from a verify report (any shape that carries rows named '<Side> down <k>' with a time)."""
    out = {"Blue": [None] * 9, "Red": [None] * 9}

    def walk(n):
        if isinstance(n, dict):
            name = n.get("name") or n.get("Name")
            if isinstance(name, str) and " down " in name:
                side, k = name.split(" down ")
                t = None
                for key in ("at", "time", "firstAt", "heldAt", "t", "At", "Time"):
                    if isinstance(n.get(key), (int, float)):
                        t = float(n[key]); break
                ok = n.get("ok", n.get("passed", n.get("verdict")))
                if t is not None and ok not in (False, "fail", "FAIL") and side in out and k.isdigit():
                    out[side][int(k) - 1] = t
            for v in n.values(): walk(v)
        elif isinstance(n, list):
            for v in n: walk(v)
    walk(report)
    return out


def metrics(rows):
    """(total down, LER, time to 50 %) of one run's first-time rows."""
    b = sum(1 for t in rows["Blue"] if t is not None)
    r = sum(1 for t in rows["Red"] if t is not None)
    ler = (r + 0.5) / (b + 0.5)   # one run's (smoothed) ratio — kept for the record; the verdict uses the ratio of totals (R-8011)
    t50 = [x for x in (rows["Blue"][4], rows["Red"][4]) if x is not None]
    return b + r, ler, (min(t50) if t50 else float(RUN_FOR)), b, r


def ler(pairs):
    """The loss-exchange ratio over runs: all Red down ÷ all Blue down (R-8011)."""
    b = sum(p[0] for p in pairs); r = sum(p[1] for p in pairs)
    return r / b if b > 0 else float("inf")


def ler_ci(pairs, n=2000):
    """The ratio of totals and its 95 % CI by a seeded bootstrap over the runs."""
    import random
    rnd = random.Random(8011)
    xs = sorted(ler([pairs[rnd.randrange(len(pairs))] for _ in pairs]) for _ in range(n))
    return ler(pairs), xs[int(0.025 * n)], xs[int(0.975 * n) - 1]


def first_loss(rows):
    """The first second either side lost a man (None when nobody did)."""
    ts = [x for x in (rows["Blue"][0], rows["Red"][0]) if x is not None]
    return min(ts) if ts else None


def ci95(xs):
    m = statistics.mean(xs)
    if len(xs) < 2: return m, m, m
    h = 1.96 * statistics.stdev(xs) / math.sqrt(len(xs))
    return m, m - h, m + h


def one(rung, seed, out):
    d = os.path.join(out, rung, f"seed-{seed:02d}")
    rep = os.path.join(d, rung + ".json")
    if os.path.exists(rep): return f"{rung} seed {seed}: kept"   # resumable
    os.makedirs(d, exist_ok=True)
    env = dict(os.environ, HROT_RUN_SEED=str(seed))
    t0 = time.time()
    with open(os.path.join(d, "run.log"), "w") as log:
        code = subprocess.call(["dotnet", RUNNER, "--mode", "editor", "--headless", "--verify", rung, "--verify-out", d],
                               cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
    rows = first_times(json.load(open(rep))) if os.path.exists(rep) else None
    return (f"{rung} seed {seed}: exit {code}, {time.time() - t0:.0f} s, "
            + (f"down Blue {sum(t is not None for t in rows['Blue'])} Red {sum(t is not None for t in rows['Red'])}" if rows else "NO REPORT"))


def physical(args):
    from concurrent.futures import ThreadPoolExecutor
    out = args.out or os.path.join(ROOT, "artifacts", "c8-ladder", time.strftime("%Y%m%d-%H%M%S"))
    os.makedirs(out, exist_ok=True)
    jobs = [(r, s) for s in range(1, args.seeds + 1) for r in args.rungs.split(",")]   # seed-major: every rung fills evenly
    with ThreadPoolExecutor(max_workers=args.jobs) as ex:
        for line in ex.map(lambda j: one(j[0], j[1], out), jobs):
            print(line, flush=True)
    print("physical runs in", out)


def compare(args):
    sto = json.load(open(args.stochastic))["rungs"]
    print(f"| rung | metric | physical mean [95 % CI] (n) | stochastic mean (n) | band (CI ± 15 %) | verdict |")
    print("|---|---|---|---|---|---|")
    allpass = True
    for rung in RUNGS:
        phys = []
        for rep in sorted(glob.glob(os.path.join(args.physical, rung, "seed-*", rung + ".json"))):
            phys.append(metrics(first_times(json.load(open(rep)))))
        st = [metrics({"Blue": [t if t >= 0 else None for t in r["Blue"]], "Red": [t if t >= 0 else None for t in r["Red"]]})
              for r in sto.get(rung, [])]
        for i, name in enumerate(("total down", "loss-exchange ratio", "time to 50 % (s)")):
            if not phys or not st:
                print(f"| {rung} | {name} | (n {len(phys)}) | (n {len(st)}) | — | ⏳ no data |"); allpass = False; continue
            if i == 1:
                # ⭐ R-8011 — the LER over seeds is the RATIO OF TOTALS (all Red down ÷ all Blue down), its CI by a seeded bootstrap. ⛔ Not the
                #   mean of per-run ratios: a run where one side loses nobody makes that ratio explode (up to 19) and the mean follows it.
                pm, lo, hi = ler_ci([(p[3], p[4]) for p in phys])
                sm = ler([(s[3], s[4]) for s in st])
            else:
                pm, lo, hi = ci95([p[i] for p in phys])
                sm = statistics.mean(s[i] for s in st)
            if hi - lo < 1e-6: lo, hi = pm, pm
            blo, bhi = lo - 0.15 * abs(lo), hi + 0.15 * abs(hi)
            ok = blo <= sm <= bhi
            allpass &= ok
            print(f"| {rung} | {name} | {pm:.2f} [{lo:.2f}, {hi:.2f}] ({len(phys)}) | {sm:.2f} ({len(st)}) | [{blo:.2f}, {bhi:.2f}] | {'✅' if ok else '⛔'} |")
        # informative (not C-8's verdict): the time to the FIRST loss — the metrics above cannot see whether hits concentrate (CE-8009)
        pf = [p for p in (first_loss(first_times(json.load(open(rep)))) for rep in sorted(glob.glob(os.path.join(args.physical, rung, "seed-*", rung + ".json")))) if p is not None]
        sf = [p for p in (first_loss({"Blue": [t if t >= 0 else None for t in r["Blue"]], "Red": [t if t >= 0 else None for t in r["Red"]]}) for r in sto.get(rung, [])) if p is not None]
        if pf and sf:
            pm, lo, hi = ci95(pf)
            print(f"| {rung} | first loss (s) — informative | {pm:.2f} [{lo:.2f}, {hi:.2f}] ({len(pf)}) | {statistics.mean(sf):.2f} ({len(sf)}) | — | ℹ |")
    print("\nC-8 rung 1:", "PASS" if allpass else "NOT MET")
    return 0 if allpass else 1


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("physical"); p.add_argument("--seeds", type=int, default=30); p.add_argument("--rungs", default=",".join(RUNGS)); p.add_argument("--out"); p.add_argument("--jobs", type=int, default=3)
    c = sub.add_parser("compare"); c.add_argument("--physical", required=True); c.add_argument("--stochastic", required=True)
    a = ap.parse_args()
    if a.cmd == "physical": physical(a); return 0
    return compare(a)


if __name__ == "__main__":
    sys.exit(main())
