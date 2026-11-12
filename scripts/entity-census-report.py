#!/usr/bin/env python3
"""entity-census-report.py — N-2 / N-4 of the cross-entity access census (docs/DESIGN_Parallel_Simulation.md §18).

    HROT_ENTITY_CENSUS=1 HROT_ENTITY_CENSUS_OUT=<dir>/census HROT_ACCESS_TRACE_OUT=<dir>/trace  python3 scripts/scale-rig.py ... --json <dir>/rig.json
    python3 scripts/entity-census-report.py --census <dir>/census.<pid>.json --trace <dir>/trace.<pid>.json? --arch <dir>/arch-8311.json \
        --rig <dir>/rig.json --node cgf [--phases Simulation,PostSimulation] > census-cgf.md

Per system of the phases (in the scheduler's order, its VM ms from the rig): own writes · CROSS-entity writes (type, per frame, call sites) ·
cross-entity READS of a type some system of the same phase writes (= the last-tick set) · structural changes on other entities · accesses with no
entity walk open ("none" — a global pass, an event-driven pass, or a loop the hooks do not see) · same-frame read-after-create. Then the class:
READY (own writes only; cross reads only of types nobody writes in the phase) · CONVERTIBLE (the list to convert) · GLOBAL (no per-entity walk).
"""
import argparse, collections, json

WRITE = {"Write", "StructAdd", "StructRemove", "RawWrite", "TryGetRW", "ReadViaRW", "EntityDestroy"}
STRUCT = {"StructAdd", "StructRemove", "EntityDestroy"}
READ = {"Read", "Has", "RawRead"}
GROUPS = ("Group", "TogglableSimulationGroup", "TogglableInputGroup", "TogglablePostSimulationGroup")


def short(t): return t.split(".")[-1].split("+")[-1]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--census", required=True)
    ap.add_argument("--arch", required=True)
    ap.add_argument("--rig")
    ap.add_argument("--node", default="cgf")
    ap.add_argument("--phases", default="Simulation,PostSimulation")
    ap.add_argument("--subsystem", default=None)
    a = ap.parse_args()
    c = json.load(open(a.census))
    frames = max(1, c.get("frames", 1))
    arch = json.load(open(a.arch)); arch = arch.get("data") or arch
    phases = a.phases.split(",")
    order, phase_of = [], {}
    for sub in arch["subsystems"]:
        if a.subsystem and sub["subsystem"] != a.subsystem: continue
        for s in sub["systems"]:
            n = s.get("systemName") or ""
            if s["phase"] in phases and not n.endswith(GROUPS) and n not in phase_of:
                order.append(n); phase_of[n] = s["phase"]
    ms = {}
    if a.rig:
        for v, name in json.load(open(a.rig))["nodes"][a.node]["all_ms"]:
            ms[name.split("/", 1)[-1]] = v

    per = collections.defaultdict(lambda: collections.defaultdict(collections.Counter))   # sys -> rel -> (kind,type) -> n
    sites = collections.defaultdict(set)
    rac = collections.Counter()
    for r in c["rows"]:
        rd = r["reader"]
        if not rd.startswith("system:"): continue
        s = rd[len("system:"):]
        per[s][r["rel"]][(r["kind"], r["type"])] += r["n"]
        rac[s] += r.get("readAfterCreate", 0)
        for site in r.get("sites", []):
            if r["rel"] != "Own": sites[(s, r["kind"], r["type"])].add(site)

    # the types each phase WRITES (any system, any entity)
    written = collections.defaultdict(set)
    for s, rels in per.items():
        ph = phase_of.get(s)
        if not ph: continue
        for rel, kt in rels.items():
            for (k, t), n in kt.items():
                if k in WRITE: written[ph].add(t)

    rows = []
    for s in order:
        rels = per.get(s)
        ph = phase_of[s]
        if not rels:
            rows.append((s, ph, ms.get(s, 0.0), "NOT SEEN", [], [], [], 0, 0, 0)); continue
        own_w = sum(n for (k, t), n in rels.get("Own", {}).items() if k in WRITE)
        cross_w = collections.Counter()
        cross_struct = collections.Counter()
        last_tick = collections.Counter()
        for rel in ("Other", "OtherScan"):
            for (k, t), n in rels.get(rel, {}).items():
                if k in STRUCT: cross_struct[(k, t)] += n
                elif k in WRITE: cross_w[t] += n
                elif k in READ and t in written[ph]: last_tick[t] += n
        none_w = sum(n for (k, t), n in rels.get("None", {}).items() if k in WRITE)
        none_all = sum(rels.get("None", {}).values())
        own_all = sum(rels.get("Own", {}).values())
        if own_all == 0 and none_all > 0: cls = "GLOBAL"
        elif not cross_w and not cross_struct and not last_tick and none_w == 0: cls = "READY"
        else: cls = "CONVERTIBLE"
        rows.append((s, ph, ms.get(s, 0.0), cls, cross_w, cross_struct, last_tick, none_w, none_all, own_w))

    rows.sort(key=lambda r: -r[2])
    print(f"# Census — node `{a.node}`, {frames} frames, phases {a.phases}\n")
    tally = collections.Counter(r[3] for r in rows)
    tms = collections.Counter()
    for r in rows: tms[r[3]] += r[2]
    print("| class | systems | VM ms / frame |\n|---|---|---|")
    for k in ("READY", "CONVERTIBLE", "GLOBAL", "NOT SEEN"): print(f"| {k} | {tally[k]} | {tms[k]:.1f} |")
    print("\n| system | phase | ms | class | cross-entity writes / frame | structural on others / frame | last-tick reads (types) | 'none' writes / frame | read-after-create |")
    print("|---|---|---|---|---|---|---|---|---|")
    for s, ph, m, cls, cw, cst, lt, nw, na, ow in rows:
        cw_s = ", ".join(f"`{short(t)}` {n / frames:.1f}" for t, n in cw.most_common(4)) + (f" (+{len(cw) - 4})" if len(cw) > 4 else "")
        cs_s = ", ".join(f"{k} `{short(t)}` {n / frames:.1f}" for (k, t), n in cst.most_common(3))
        lt_s = ", ".join(f"`{short(t)}`" for t, _ in lt.most_common(5)) + (f" (+{len(lt) - 5})" if len(lt) > 5 else "")
        print(f"| `{s}` | {ph} | {m:.2f} | {cls} | {cw_s} | {cs_s} | {lt_s} | {nw / frames:.1f} | {rac[s]} |")
    print("\n## Cross-entity write sites (sampled)\n")
    for (s, k, t), ss in sorted(sites.items(), key=lambda x: (-ms.get(x[0][0], 0), x[0])):
        if k not in WRITE: continue
        print(f"- `{s}` {k} `{short(t)}`: " + " · ".join(sorted(ss)[:3]))


if __name__ == "__main__":
    main()
