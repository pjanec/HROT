#!/usr/bin/env python3
"""Generate the pickers' thumbnails by driving an EDITOR over its existing HTTP routes — nothing new renders (CE-4268, docs/DESIGN_Add_Entity_Picker.md §2i).

  scripts/thumbnails.py --scenarios                      every scenario of the repo (skips one with a hand-made preview.png)
  scripts/thumbnails.py --scenarios ua-tank-ammo hill-attack
  scripts/thumbnails.py --types 1001 1101                these TKB type ids (GET /tkb/types lists them); --types with no ids = every type
  scripts/thumbnails.py --scenarios a b --types 1001 --port 5195
  scripts/thumbnails.py --attach --port 5195 ...         use an editor already listening there (do not launch or stop one)

A scenario's still = its START as the editor shows it: POST /scenario/load/edit -> the entities' bounds (GET /entities, GET /entities/{id}) -> POST /film/shot
{eye, target} looking down ~45 degrees over them -> POST /film/capture {path, 512x288} -> DELETE /film/shot.
A type's still = one entity spawned (POST /entities/spawn) in a bare studio scenario (--studio, default test-move: no terrain), framed by distance, captured, removed (DELETE /entities/{id}).

⛔ Output goes into the NAS root ONLY (--nas, default the runtime shared root /tmp/FDP_Temp/shared): scenarios/<name>/preview.png, assets/thumbnails/generated/<typeName>.png.
⛔ Never into the repository, never an edit of map.json. A hand-made repo preview.png wins (the scenario is skipped); ThumbnailMap's third step uses generated/<typeName>.png only for a type no hand-made rule covers.
The file stem of a type is its TKB Name sanitized exactly as ThumbnailMap.GeneratedFileName does: letters, digits . _ - kept, anything else _ (a name holding ".." is refused).
"""
import argparse, importlib.util, json, math, os, re, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
spec = importlib.util.spec_from_file_location("udc", os.path.join(HERE, "utility-demo-check.py"))
udc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(udc)

DEFAULT_NAS = "/tmp/FDP_Temp/shared"
W, H = 512, 288
FOV_DEG = 45.0
SETTLE_CAPTURES = 4


def generated_name(type_name):
    """ThumbnailMap.GeneratedFileName — the SAME rule (the engine reads what this writes)."""
    if not type_name or not type_name.strip() or ".." in type_name:
        return None
    return re.sub(r"[^A-Za-z0-9._-]", "_", type_name.strip())


def ok(resp):
    return isinstance(resp, dict) and resp.get("http") in (None, 200) and resp.get("ok") is not False


def body_of(resp):
    return udc.data(resp)


def say(resp):
    return json.dumps(resp)[:200]


def wait_loaded(timeout=120):
    """The editor reports a settled world: canOperate and a steady entity count."""
    end, prev, steady = time.time() + timeout, None, 0
    while time.time() < end and steady < 3:
        st = body_of(udc.call("GET", "/status")) or {}
        n = st.get("entityCount")
        steady = steady + 1 if st.get("canOperate") and n == prev else 0
        prev = n
        time.sleep(0.5)
    return steady >= 3


def load_edit(name):
    end = time.time() + 60
    while time.time() < end and not (body_of(udc.call("GET", "/status")) or {}).get("canOperate"):
        time.sleep(0.5)
    r = udc.call("POST", "/scenario/load/edit", {"name": name, "waitForReady": True, "discardUnsaved": True})
    if not ok(r):
        return False, "load: " + say(r)
    if not wait_loaded():
        return False, "the world did not settle"
    return True, ""


def positions(limit=160):
    """The entities' ground positions (a sample of at most `limit`): GET /entities then the SimTransform of each."""
    rows = body_of(udc.call("GET", "/entities", None)) or []
    placed = [r for r in rows if "SimTransform" in (r.get("components") or [])]
    # the CAST of the scenario, not its scenery or its camera: units (EntityInfo) when there are any, else what the file authored (EntityGuid), else everything placed
    for need in ("EntityInfo", "EntityGuid"):
        cast = [r for r in placed if need in (r.get("components") or [])]
        if cast:
            placed = cast
            break
    ids = [r["networkId"] for r in placed]
    if len(ids) > limit:
        step = len(ids) / limit
        ids = [ids[int(i * step)] for i in range(limit)]
    pts = []
    for nid in ids:
        comps = (body_of(udc.call("GET", f"/entities/{nid}")) or {}).get("Components") or {}
        p = (comps.get("SimTransform") or {}).get("Position")
        if p and len(p) == 3 and all(math.isfinite(v) for v in p):
            pts.append(tuple(p))
    return pts


def frame(points, pitch_deg=-45.0, yaw_deg=225.0, margin=1.25, min_radius=12.0):
    """eye/target looking down at pitch over the points' bounds (a bounding sphere, fitted to the vertical field of view)."""
    xs, ys, zs = zip(*points)
    cx, cy, cz = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, (min(zs) + max(zs)) / 2
    radius = max(min_radius, math.hypot(max(xs) - min(xs), max(ys) - min(ys)) / 2)
    dist = radius * margin / math.sin(math.radians(FOV_DEG / 2)) / 1.6   # 16:9 — a wide frame is fitted by its height, the bounds' diagonal gets slack
    pitch, yaw = math.radians(pitch_deg), math.radians(yaw_deg)
    eye = (cx + dist * math.cos(pitch) * math.cos(yaw), cy + dist * math.cos(pitch) * math.sin(yaw), cz - dist * math.sin(pitch))
    return list(eye), [cx, cy, cz]


def shoot(path, eye, target):
    """shot -> a throwaway settle capture (a new shot's first frame reads the weather before the camera moves) -> the capture; the shot released after."""
    try:
        # ⭐ the shot's own layers: `layers: {}` is a take's clean start — the scene rows only, so no unit cards over the picture (a transient override through the
        #   director's layer override; never the user's settings; DELETE /film/shot drops it). Measured: {} alone is enough — a spec's override keeps only the scene rows.
        r = udc.call("POST", "/film/shot", {"spec": {"eye": eye, "target": target, "layers": {}}, "t": 0})
        if not ok(r):
            return False, "shot: " + say(r)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        for _ in range(SETTLE_CAPTURES):           # let the camera arrive (it eases to a new shot) and the weather settle
            udc.call("POST", "/film/capture", {"path": path + ".settle.png", "width": 64, "height": 64})
            time.sleep(0.4)
        try:
            os.remove(path + ".settle.png")
        except OSError:
            pass
        r = udc.call("POST", "/film/capture", {"path": path, "width": W, "height": H})
        if not ok(r):
            return False, "capture: " + say(r)
        return (True, "") if os.path.isfile(path) and os.path.getsize(path) > 0 else (False, "no file written")
    finally:
        udc.call("DELETE", "/film/shot")


def scenario_names(requested):
    root = os.path.join(REPO, "scenarios")
    have = sorted(d for d in os.listdir(root) if os.path.isfile(os.path.join(root, d, "scenario.json")))
    if requested:
        missing = [n for n in requested if n not in have]
        if missing:
            sys.exit(f"no such scenario in the repository: {missing}")
        return requested
    return have


def do_scenarios(names, nas, rows):
    for name in names:
        if os.path.isfile(os.path.join(REPO, "scenarios", name, "preview.png")):
            rows.append((name, "skipped hand-made", "preview.png in the repository")); continue
        good, why = load_edit(name)
        if not good:
            rows.append((name, "failed", why)); continue
        pts = positions()
        if not pts:
            rows.append((name, "failed", "no entity with a position")); continue
        eye, target = frame(pts)
        out = os.path.join(nas, "scenarios", name, "preview.png")
        good, why = shoot(out, eye, target)
        rows.append((name, "ok" if good else "failed", out if good else why))


def type_table():
    rows = body_of(udc.call("GET", "/tkb/types")) or []
    return {int(r["tkbType"]): r["name"] for r in rows}


def do_types(ids, nas, studio, rows):
    table = type_table()
    if not ids:
        ids = sorted(table)
    good, why = load_edit(studio)
    if not good:
        for i in ids:
            rows.append((f"type {i}", "failed", f"studio {studio}: {why}"))
        return
    out_dir = os.path.join(nas, "assets", "thumbnails", "generated")
    origin = [4000.0, 4000.0, 0.0]                      # far from the studio scenario's own entity
    # a spawn is a REQUEST the sim serves: enter preview PAUSED (the clock does not run in edit) and step it
    udc.call("POST", "/preview/enter", {"startPaused": True})
    try:
        for tkb in ids:
            name = table.get(tkb)
            if name is None:
                rows.append((f"type {tkb}", "failed", "not in GET /tkb/types")); continue
            stem = generated_name(name)
            if stem is None:
                rows.append((f"type {tkb} {name}", "failed", "unusable name")); continue
            before = {r["networkId"] for r in (body_of(udc.call("GET", "/entities")) or [])}
            r = udc.call("POST", "/entities/spawn", {"tkbType": tkb, "transform": {"position": {"x": origin[0], "y": origin[1], "z": origin[2]},
                                                                                  "rotation": {"x": 0, "y": 0, "z": 0, "w": 1}}})
            if not ok(r):
                rows.append((f"type {tkb} {name}", "failed", "spawn: " + say(r))); continue
            nids = []
            for _ in range(10):                                   # step until the request is served and the entity exists
                udc.call("POST", "/sim/step", {"count": 6})
                nids = [x["networkId"] for x in (body_of(udc.call("GET", "/entities")) or []) if x["networkId"] not in before]
                if nids:
                    break
            if not nids:
                rows.append((f"type {tkb} {name}", "failed", "the spawn request was never served")); continue
            try:
                udc.call("POST", "/sim/step", {"count": 6})       # let it realise (its body, its size)
                pts = positions_near(origin) or [tuple(origin)]
                eye, target = frame(pts, pitch_deg=-25.0, yaw_deg=215.0, min_radius=6.5)
                path = os.path.join(out_dir, stem + ".png")
                good, why = shoot(path, eye, target)
                rows.append((f"type {tkb} {name}", "ok" if good else "failed", path if good else why))
            finally:
                for nid in nids:
                    udc.call("DELETE", f"/entities/{nid}")
                udc.call("POST", "/sim/step", {"count": 3})
    finally:
        udc.call("POST", "/preview/exit", {})


def positions_near(origin, radius=60.0):
    return [p for p in positions() if math.hypot(p[0] - origin[0], p[1] - origin[1]) <= radius]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scenarios", nargs="*", default=None, metavar="NAME", help="scenario names (none given = every repository scenario)")
    ap.add_argument("--types", nargs="*", type=int, default=None, metavar="ID", help="TKB type ids (none given = every type)")
    ap.add_argument("--port", type=int, default=8111)
    ap.add_argument("--attach", action="store_true", help="use an editor already listening on --port (do not launch or stop one)")
    ap.add_argument("--nas", default=DEFAULT_NAS, help=f"the NAS root the pictures go into (default {DEFAULT_NAS})")
    ap.add_argument("--studio", default="test-move", help="the bare scenario types are spawned in (default test-move: no terrain)")
    args = ap.parse_args()
    if args.scenarios is None and args.types is None:
        sys.exit("say what: --scenarios [names] and/or --types [ids]")
    nas = os.path.abspath(args.nas)
    if os.path.commonpath([nas, REPO]) == REPO:
        sys.exit("⛔ --nas is inside the repository: generated pictures are never written there")

    udc.BASE = f"http://localhost:{args.port}"
    proc, rows = None, []
    try:
        if not args.attach:
            proc = udc.launch(args.port, "editor")
        if args.scenarios is not None:
            do_scenarios(scenario_names(args.scenarios), nas, rows)
        if args.types is not None:
            do_types(args.types, nas, args.studio, rows)
    finally:
        if proc is not None:
            udc.stop(proc)
    width = max((len(r[0]) for r in rows), default=10)
    print(f"{'item'.ljust(width)}  result             detail")
    for item, result, detail in rows:
        print(f"{item.ljust(width)}  {result.ljust(18)} {detail}")
    sys.exit(1 if any(r[1] == "failed" for r in rows) else 0)


if __name__ == "__main__":
    main()
