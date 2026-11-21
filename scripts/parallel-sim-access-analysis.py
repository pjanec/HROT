#!/usr/bin/env python3
"""POC 4 J-3 (docs/DESIGN_Parallel_Simulation.md §16) — from an A-1 access trace of the CGF node and its /diagnostics/architecture (phase, order, ms), compute per-phase read/write sets,
the pairwise conflicts, the dependency DAG (in the scheduler's order), the critical path and a list-scheduled ideal on P cores.

    scripts/parallel-sim-access-analysis.py <trace.json> <architecture.json> [--ms rig.json] [--cores 6] [--subsystem CGF]
"""
import argparse, json, itertools, collections

R_COMP = {"Read", "Has", "RawRead", "QueryInclude", "QueryExclude", "TableAccess"}
W_COMP = {"Write", "TryGetRW", "RawWrite"}
STRUCT = {"StructAdd", "StructRemove"}
NOINDEX = False
GROUPS = ("Group", "TogglableSimulationGroup", "TogglableInputGroup", "TogglablePostSimulationGroup")
R_SING = {"SingletonGet", "SingletonHas", "SingletonEnumerate"}


def sets(trace, variant):
    """reader -> (R, W) of resources (repo, name). variant 'traced': a managed read that stamps (ReadViaRW) is a write;
    'nostamp': it is a read (POC 3's opt-in non-stamping managed read)."""
    kinds, readers = trace["kinds"], trace["readers"]
    out = collections.defaultdict(lambda: (set(), set()))
    for ctx, k, rd, repo, _phase, t, _n in trace["rows"]:
        kind, reader = kinds[k], readers[rd]
        if not reader.startswith("system:"):
            continue
        R, W = out[reader[len("system:"):]]
        c = (repo, "c:" + (t or "?"))
        if kind in R_COMP: R.add(c)
        elif kind in W_COMP: W.add(c)
        elif kind == "ReadViaRW": (W if variant == "traced" else R).add(c)
        elif kind in STRUCT: W.add(c); W.add((repo, "(index)"))
        elif kind in R_SING: R.add((repo, "s:" + (t or "?")))
        elif kind == "SingletonSet": W.add((repo, "s:" + (t or "?")))
        elif kind in ("EntityCreate", "EntityDestroy"): W.add((repo, "(index)"))
        elif kind == "EntityIndexRead": R.add((repo, "(index)"))
        elif kind == "BusRead": R.add((repo, "e:" + (t or "?")))
        elif kind == "BusPublish": W.add((repo, "e:" + (t or "?")))
        elif kind in ("BusSwap", "WorldCopy"): W.add((repo, "(all)"))
    return out


def conflict(a, b):
    (Ra, Wa), (Rb, Wb) = a, b
    if any(x[1] == "(all)" for x in Wa | Wb):
        return {"(all)"}
    hit = (Wa & (Rb | Wb)) | (Ra & Wb)
    if NOINDEX: hit = {x for x in hit if x[1] != "(index)"}
    # a query read of the index conflicts with a structural write of it
    return {x[1] for x in hit}


def schedule(order, ms, deps, cores):
    """greedy list schedule in the scheduler's order: each system starts when its deps are done and a core is free."""
    finish, free = {}, [0.0] * cores
    for s in order:
        ready = max((finish[d] for d in deps[s]), default=0.0)
        i = min(range(cores), key=lambda c: max(free[c], ready))
        start = max(free[i], ready)
        finish[s] = start + ms.get(s, 0.0)
        free[i] = finish[s]
    return max(finish.values(), default=0.0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trace"); ap.add_argument("arch")
    ap.add_argument("--ms", help="a scale-rig JSON whose cgf all_ms gives the untraced ms (else the architecture's recentAvgMs)")
    ap.add_argument("--cores", type=int, default=6)
    ap.add_argument("--subsystem", default="CGF")
    ap.add_argument("--top", type=int, default=12)
    a = ap.parse_args()
    trace = json.load(open(a.trace))
    arch = json.load(open(a.arch)); arch = arch.get("data") or arch
    subs = arch.get("subsystems") or arch.get("nodes", [{}])[0].get("subsystems", [])
    rows = []
    for sub in subs:
        if a.subsystem and sub.get("subsystem") != a.subsystem: continue
        for i, s in enumerate(sub.get("systems", [])):
            if (s.get("systemName") or "").endswith(GROUPS): continue   # a group's time is its inner systems'
            rows.append((s["phase"], i, s["systemName"], s.get("recentAvgMs") or s.get("avgMs") or 0.0, s.get("module")))
    ms = {n: m for _, _, n, m, _ in rows}
    if a.ms:
        rig = json.load(open(a.ms))
        for v, name in rig["nodes"]["cgf"]["all_ms"]:
            ms[name.split("/", 1)[-1]] = v
    phases = collections.OrderedDict()
    seen = collections.Counter()
    for ph, i, n, _, mod in rows:
        seen[n] += 1
        if seen[n] > 1: print(f"note: {n} listed {seen[n]} times (phase {ph}) — the repeat is skipped (one reader in the trace)")
        else: phases.setdefault(ph, []).append(n)
    for variant in ("traced", "nostamp", "noindex"):
        global NOINDEX; NOINDEX = variant == "noindex"
        rw = sets(trace, "nostamp" if variant == "noindex" else variant)
        print(f"\n## variant: {variant}  ({ {'traced':'a stamping managed read is a WRITE','nostamp':'managed reads do not stamp (HROT_MANAGED_READ_NO_STAMP=1)','noindex':'nostamp AND structural changes ignored (as if every create/destroy/add/remove were deferred to a command buffer)'}[variant] })\n")
        print(f"| phase | systems (traced/listed) | serial ms | critical path ms | ideal ms on {a.cores} cores | gain ms | disjoint pairs |")
        print("|---|---|---|---|---|---|---|")
        tot = [0.0, 0.0, 0.0]
        detail = []
        for ph, order in phases.items():
            traced = [s for s in order if s in rw]
            deps = {s: [] for s in order}
            why = {}
            pairs = dis = 0
            for i, j in itertools.combinations(range(len(order)), 2):
                si, sj = order[i], order[j]
                if si not in rw or sj not in rw:
                    deps[sj].append(si); why[(si, sj)] = {"(untraced: assumed dependent)"}; pairs += 1; continue
                c = conflict(rw[si], rw[sj]); pairs += 1
                if c: deps[sj].append(si); why[(si, sj)] = c
                else: dis += 1
            serial = sum(ms.get(s, 0.0) for s in order)
            cp = {}
            for s in order:
                cp[s] = ms.get(s, 0.0) + max((cp[d] for d in deps[s]), default=0.0)
            crit = max(cp.values(), default=0.0)
            ideal = schedule(order, ms, deps, a.cores)
            tot[0] += serial; tot[1] += crit; tot[2] += ideal
            print(f"| {ph} | {len(traced)}/{len(order)} | {serial:.1f} | {crit:.1f} | {ideal:.1f} | {serial-ideal:.1f} | {dis}/{pairs} |")
            # the critical chain
            end = max(order, key=lambda s: cp[s]) if order else None
            chain = []
            while end:
                chain.append(end)
                end = max(deps[end], key=lambda d: cp[d]) if deps[end] else None
            detail.append((ph, list(reversed(chain)), why, order))
        print(f"| **all** | | **{tot[0]:.1f}** | **{tot[1]:.1f}** | **{tot[2]:.1f}** | **{tot[0]-tot[2]:.1f}** | |")
        for ph, chain, why, order in detail:
            heavy = [s for s in order if ms.get(s, 0) >= 1.0]
            if not heavy: continue
            print(f"\n**{ph}** — critical chain: " + " → ".join(f"{s} ({ms.get(s,0):.1f})" for s in chain if ms.get(s, 0) >= 0.5))
            for x, y in itertools.combinations(sorted(heavy, key=lambda s: -ms.get(s, 0))[:a.top], 2):
                k = (x, y) if order.index(x) < order.index(y) else (y, x)
                w = why.get(k)
                if w: print(f"  - {k[0]} ⟂ {k[1]}: " + ", ".join(sorted(w)[:6]) + (" …" if len(w) > 6 else ""))
        untraced = [s for o in phases.values() for s in o if s not in rw]
        print(f"\nnot traced (no system: reader — never ran, or ran only under another reader): {len(untraced)}: " + ", ".join(untraced[:20]))


if __name__ == "__main__":
    main()
