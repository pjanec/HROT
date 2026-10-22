#!/usr/bin/env python3
"""scenario-height-lint — CE-2351 (behaviors, 2026-10-22): find units authored on the ground that spawn ON TOP of a structure.

Usage: python3 scripts/scenario-height-lint.py OUT.txt   (launches one ClusterRunner --mode all; ~1 h over every scenario)
A unit authored at z 0 whose (x, y) lies inside a solid block or under a roof is placed on top of it at load: it never moves and
fights from the roof (measured: ua-javelin-lock's tank at z 15, the surrender demos' rifleman at z 20, ua-civ-provocation's police).
Known noise: a positionless command unit ("Blue HQ") and scenarios with two units of one name (they refuse to load).
"""
# For every scenario: each named entity authored at ground height (|z| < 0.01, no area outline) that SPAWNED more than 1 m above
# where it was authored — placed on top of a solid block / roof instead of in the street (ua-javelin-lock: authored z 0, spawned z 15).
import importlib.util, json, sys, os, glob
OUT = sys.argv[1]
spec = importlib.util.spec_from_file_location("udc", "/home/user/HROT/scripts/utility-demo-check.py")
u = importlib.util.module_from_spec(spec); sys.argv = ["x"]; spec.loader.exec_module(u)
out = open(OUT, "w")
names = sorted(os.path.basename(os.path.dirname(p)) for p in glob.glob("/home/user/HROT/scenarios/*/scenario.json"))
proc = u.launch(8111)
try:
    for name in names:
        d = json.load(open(f"/home/user/HROT/scenarios/{name}/scenario.json"))
        ents = {}
        for k, e in (d.get("entities") or {}).items():
            pos = (e.get("SimTransform") or {}).get("Position"); n = (e.get("EntityInfo") or {}).get("Name")
            if not n or not pos or "EditablePolyline" in e or len(pos) < 3 or abs(pos[2]) > 0.01: continue
            ents[n] = pos
        if not ents: continue
        try:
            u.load(name)
        except BaseException as ex:
            print(name, "LOAD FAILED", ex, file=out, flush=True); continue
        ids = u.ids_by_name()
        hits = []
        for n, p in ents.items():
            nid = ids.get(n)
            if nid is None: continue
            at = u.position(nid)
            if at and at[2] - p[2] > 1.0: hits.append((n, [round(p[0], 1), round(p[1], 1)], round(at[2], 1)))
        print(name, f"{len(hits)}/{len(ents)}", hits, file=out, flush=True)
finally:
    u.stop(proc)
