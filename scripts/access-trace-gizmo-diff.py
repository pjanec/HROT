#!/usr/bin/env python3
"""What the gizmo group reads that the presentation side does not — from an A-1 trace (HROT_ACCESS_TRACE=1).

    scripts/access-trace-gizmo-diff.py trace-*.json   (threaded POC 4, design §14.2.0)
"""
import json, sys, collections

READ = {"Read", "Has", "RawRead", "QueryInclude", "TableAccess", "ReadViaRW", "TryGetRW"}
GIZMO_SYS = {"system:StatelessGizmoSystem", "system:GlobalGizmoManager", "system:DataDrivenGizmoSystem", "system:MapSelfCheckSystem"}
PRES = 2


def is_gizmo(r):
    return r.startswith("gizmo") or r in GIZMO_SYS


def main(paths):
    giz = collections.Counter(); giz_readers = collections.defaultdict(set)
    pres = collections.Counter(); pres_noinsp = collections.Counter()
    for p in paths:
        d = json.load(open(p))
        kinds, readers = d["kinds"], d["readers"]
        for ctx, k, rd, _repo, _ph, t, n in d["rows"]:
            if kinds[k] not in READ or not t or t.startswith("("): continue
            r = readers[rd]
            if is_gizmo(r):
                giz[t] += n; giz_readers[t].add(r)
            elif ctx == PRES:
                pres[t] += n
                if r != "window:editor_fdp_inspector": pres_noinsp[t] += n
    only = sorted((t for t in giz if t not in pres_noinsp), key=lambda t: -giz[t])
    only_all = [t for t in only if t not in pres]
    print(f"gizmo readers read {len(giz)} component types; the presentation side (no inspector) reads {len(pres_noinsp)}, with the inspector {len(pres)}")
    print(f"⇒ read ONLY by gizmos (not in the ui-no-inspector mask): {len(only)}  (of which no presentation reader at all, inspector included: {len(only_all)})\n")
    print("| type | gizmo reads | read by | inspector reads it |")
    print("|---|---|---|---|")
    for t in only:
        rs = sorted(giz_readers[t]); short = ", ".join(x.replace("gizmo-rule", "rule") for x in rs[:3]) + (f" +{len(rs)-3}" if len(rs) > 3 else "")
        print(f"| `{t.split('.')[-1]}` | {giz[t]:,} | {short} | {'yes' if t in pres else 'no'} |")


if __name__ == "__main__":
    main(sys.argv[1:])
