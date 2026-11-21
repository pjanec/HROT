#!/usr/bin/env python3
"""access-trace-masks.py — A-3: turn an A-1 access trace (HROT_ACCESS_TRACE=1) into the masks PublishProbe copies (HROT_PUBLISH_PROBE_MASKS).

    python3 scripts/access-trace-masks.py trace-1.json > masks.json
    HROT_PUBLISH_PROBE=1 HROT_PUBLISH_PROBE_MASKS=masks.json HROT_FRAME_STATS=1 \
        python3 scripts/access-trace-session.py --out <dir> --no-trace --stages load,select1,play,view3d

Masks: every type the PRESENTATION context touched (ui-all), the same without the FDP inspector (which reflects over every table), and the map /
3-D / gizmo readers only (map). Bus, entity-index and query-exclusion records are not types to copy.
"""
import json
import sys

SKIP = {"BusPublish", "BusRead", "BusSwap", "EntityCreate", "EntityDestroy", "EntityIndexRead", "WorldCopy", "QueryExclude"}
PRESENTATION = 2   # TraceContext: Other 0 · Tick 1 · Presentation 2


def main(path):
    d = json.load(open(path))
    kinds, readers = d["kinds"], d["readers"]
    masks = {"ui-all": set(), "ui-no-inspector": set(), "map": set()}
    for ctx, k, rd, _repo, _phase, t, _n in d["rows"]:
        if ctx != PRESENTATION or kinds[k] in SKIP or not t or t.startswith("("):
            continue
        reader = readers[rd]
        masks["ui-all"].add(t)
        if reader != "window:editor_fdp_inspector":
            masks["ui-no-inspector"].add(t)
        if reader.startswith(("layer.", "gizmo-rule", "subsystem.DrawWorld", "map-input")):
            masks["map"].add(t)
    json.dump({k: sorted(v) for k, v in masks.items()}, sys.stdout, indent=1)
    for k, v in masks.items():
        print(f"{k}: {len(v)} types", file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1])
