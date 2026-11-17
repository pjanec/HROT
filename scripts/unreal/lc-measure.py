#!/usr/bin/env python3
"""lc-measure.py — CE-4651: import one exported terrain into Unreal through Landscape Combinator and measure the HEIGHT AGREEMENT.

    python scripts/unreal/lc-measure.py <export-folder>/<name> --project D:/Work/HrotUnreal/HrotUnreal.uproject
           [--engine C:/UE_5.8/UE_5.8] [--components Auto] [--keep-open]

<export-folder>/<name> is what scripts/terrain-to-unreal.py wrote (the .height.tif + .frame.json). It runs the editor with
scripts/unreal/hrot_lc_import.py, which builds the landscape and line-traces it, then compares each trace with HROT's own ground height
(TerrainHeightGrid.Sample: two planar triangles per cell, split south-west → north-east) at the same point. Points: every post, every cell
centre (capped), a 0.25 m transect across the steepest cell, and points just inside the edges. Result: <export-folder>/<name>/lc-result.json.
"""
import argparse, json, os, subprocess, sys, time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))


def hrot_sample(z, x0, y0, cell, x, y):
    rows, cols = z.shape
    fx = min(max((x - x0) / cell, 0.0), cols - 1); fy = min(max((y - y0) / cell, 0.0), rows - 1)
    c, r = min(int(fx), cols - 2), min(int(fy), rows - 2)
    u, v = fx - c, fy - r
    z00, z10, z01, z11 = z[r, c], z[r, c + 1], z[r + 1, c], z[r + 1, c + 1]
    return float(z00 + u * (z10 - z00) + v * (z11 - z10)) if u >= v else float(z00 + v * (z01 - z00) + u * (z11 - z01))


def compare_buildings(res, folder, name, frame, z, x0, y0, cell):
    """Each Unreal building (its bounds, HROT metres) against our footprint whose bounding box overlaps it most: the footprint's box
    (x, y) and the height above the ground (top − HROT's lowest ground under the footprint)."""
    de, dn = frame["localToProjected"]
    ours = []
    for f in json.load(open(os.path.join(folder, f"{name}.vectors.geojson"), encoding="utf-8"))["features"]:   # projected (the per-kind file is lon / lat)
        if f["properties"].get("hrot_kind") != "building": continue
        ring = np.array(f["geometry"]["coordinates"][0], dtype=float) - [de, dn]
        ground = min(hrot_sample(z, x0, y0, cell, x, y) for x, y in ring)
        ours.append({"name": f["properties"].get("name"), "min": ring.min(0), "max": ring.max(0), "height": f["properties"]["height"],
                     "levels": f["properties"].get("building_levels"), "ground": ground})
    rows, unmatched = [], 0
    for b in res.get("buildings") or []:
        if "min" not in b: continue
        bmin, bmax = np.array(b["min"][:2]), np.array(b["max"][:2])
        best, best_iou = None, 0.0
        for o in ours:
            lo, hi = np.maximum(bmin, o["min"]), np.minimum(bmax, o["max"])
            inter = np.prod(np.clip(hi - lo, 0, None)); union = np.prod(bmax - bmin) + np.prod(o["max"] - o["min"]) - inter
            iou = inter / union if union > 0 else 0
            if iou > best_iou: best, best_iou = o, iou
        if best is None: unmatched += 1; continue
        rows.append({"iou": best_iou, "dxy": float(np.abs(np.concatenate([bmin - best["min"], bmax - best["max"]])).max()),
                     "top_above_ground": b["max"][2] - best["ground"], "base_minus_ground": b["min"][2] - best["ground"],
                     "our_height": best["height"], "levels": best["levels"]})
    if not rows: return {"unreal": len(res.get("buildings") or []), "ours": len(ours), "matched": 0}
    iou = np.array([r["iou"] for r in rows]); dxy = np.array([r["dxy"] for r in rows])
    dh = np.array([r["top_above_ground"] - r["our_height"] for r in rows])
    by_levels = {}
    for r in rows: by_levels.setdefault(r["levels"], []).append(r["top_above_ground"] - r["our_height"])
    return {"unreal": len(res.get("buildings") or []), "ours": len(ours), "matched": len(rows), "unmatched": unmatched,
            "footprintIoU": {"mean": round(float(iou.mean()), 3), "min": round(float(iou.min()), 3)},
            "footprintBoxEdge_m": {"mean": round(float(dxy.mean()), 3), "max": round(float(dxy.max()), 3)},
            "topAboveGroundMinusOurHeight_m": {"mean": round(float(dh.mean()), 3), "min": round(float(dh.min()), 3), "max": round(float(dh.max()), 3)},
            "byLevels": {str(k): round(float(np.mean(v)), 3) for k, v in sorted(by_levels.items(), key=lambda kv: kv[0] or 0)},
            "baseMinusGround_m": {"mean": round(float(np.mean([r["base_minus_ground"] for r in rows])), 3)}}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export"); ap.add_argument("--project", required=True)
    ap.add_argument("--engine", default=r"C:\UE_5.8\UE_5.8"); ap.add_argument("--components", default="Auto")
    ap.add_argument("--max-points", type=int, default=4000); ap.add_argument("--keep-open", action="store_true")
    ap.add_argument("--timeout", type=int, default=1800)
    ap.add_argument("--buildings", action="store_true", help="also build the buildings from <name>.buildings.geojson and measure them")
    a = ap.parse_args()
    folder = os.path.abspath(a.export); name = os.path.basename(folder)
    frame = json.load(open(os.path.join(folder, f"{name}.frame.json"), encoding="utf-8"))
    sys.path.insert(0, os.path.join(HERE, ".."))
    import importlib.util
    spec = importlib.util.spec_from_file_location("t2u", os.path.join(HERE, "..", "terrain-to-unreal.py"))
    t2u = importlib.util.module_from_spec(spec); spec.loader.exec_module(t2u)
    import rasterio
    with rasterio.open(os.path.join(folder, f"{name}.height.tif")) as ds:
        z = ds.read(1)[::-1].astype(np.float64)   # back to row 0 = south
    x0, y0 = frame["southWestPost"]; cell = frame["cell"]; rows, cols = z.shape

    rng = np.random.default_rng(4651)
    posts = [(x0 + c * cell, y0 + r * cell) for r in range(rows) for c in range(cols)]
    centres = [(x0 + (c + .5) * cell, y0 + (r + .5) * cell) for r in range(rows - 1) for c in range(cols - 1)]
    if len(posts) > a.max_points // 2: posts = [posts[i] for i in rng.choice(len(posts), a.max_points // 2, replace=False)]
    if len(centres) > a.max_points // 2: centres = [centres[i] for i in rng.choice(len(centres), a.max_points // 2, replace=False)]
    gy, gx = np.gradient(z, cell)
    r, c = np.unravel_index(np.argmax(np.hypot(gx, gy)), z.shape)
    transect = [(x0 + (c - 2) * cell + d, y0 + r * cell + 0.3 * cell) for d in np.arange(0, 4 * cell, 0.25)]
    w, h = (cols - 1) * cell, (rows - 1) * cell
    edges = [(x0 + 0.1, y0 + h / 2), (x0 + w - 0.1, y0 + h / 2), (x0 + w / 2, y0 + 0.1), (x0 + w / 2, y0 + h - 0.1), (x0 + 0.1, y0 + 0.1), (x0 + w - 0.1, y0 + h - 0.1)]
    groups = {"post": posts, "centre": centres, "transect": transect, "edge": edges}
    points, kinds = [], []
    for k, ps in groups.items():
        points += [[float(p[0]), float(p[1])] for p in ps]; kinds += [k] * len(ps)

    crs = frame["crsUserInput"]   # ⛔ never the WKT: the plugin makes a folder name of it (UL-3)
    # ⚠ Landscape Combinator rewrites its input GeoTIFF in place (its FixNoData / merge phases) ⇒ it gets a COPY, our export stays as written
    import shutil
    work = os.path.join(folder, "lc-input"); os.makedirs(work, exist_ok=True)
    for f in os.listdir(work): os.remove(os.path.join(work, f))
    tif = os.path.join(work, f"{name}.height.tif"); shutil.copyfile(os.path.join(folder, f"{name}.height.tif"), tif)
    job = {"terrain": name, "tif": tif.replace("\\", "/"), "crs": crs,
           "origin": frame["localToProjected"], "components": a.components, "points": points,
           "out": os.path.join(folder, "lc-trace.json").replace("\\", "/"), "level": "/Game/Maps/" + name.replace("-", "_"), "timeout": a.timeout,
           "editorLog": os.path.join(folder, "lc-editor.log").replace("\\", "/")}
    if a.buildings:
        job["buildings"] = os.path.join(folder, f"{name}.buildings.geojson").replace("\\", "/")
    job_path = os.path.join(folder, "lc-job.json"); json.dump(job, open(job_path, "w"), indent=1)
    if os.path.exists(job["out"]): os.remove(job["out"])
    # a rerun starts from a fresh level: the previous map is deleted on disk while the editor is not running (its in-editor delete
    # leaves the file, and new_level refuses an existing asset)
    umap = os.path.join(os.path.dirname(os.path.abspath(a.project)), "Content", "Maps", job["level"].rsplit("/", 1)[1] + ".umap")
    if os.path.exists(umap): os.remove(umap)
    for extra in (umap[:-5] + "_BuiltData.uasset",):
        if os.path.exists(extra): os.remove(extra)
    env = dict(os.environ, HROT_LC_JOB=job_path)
    if a.keep_open: env["HROT_LC_KEEP_OPEN"] = "1"
    exe = os.path.join(a.engine, "Engine", "Binaries", "Win64", "UnrealEditor.exe")
    cmd = [exe, a.project, f"-ExecutePythonScript={os.path.join(HERE, 'hrot_lc_import.py')}", "-unattended", "-nosplash", "-NoSound",
           f"-abslog={os.path.join(folder, 'lc-editor.log')}"]
    t = time.time()
    print("running", " ".join(cmd), flush=True)
    rc = subprocess.run(cmd, env=env).returncode
    print(f"editor exit {rc} after {time.time() - t:.0f} s", flush=True)
    if not os.path.exists(job["out"]):
        raise SystemExit("no trace result — see lc-editor.log")
    res = json.load(open(job["out"], encoding="utf-8"))
    out = {"ok": res.get("ok"), "why": res.get("why"), "landscape": res.get("landscape"), "spawn_seconds": res.get("spawn_seconds"),
           "editor_seconds": round(time.time() - t, 1), "log": res.get("log"), "byKind": {}}
    diffs_all = []
    for (x, y, ue, actor), k in zip(res.get("traces", []), kinds):
        if ue is None:
            out["byKind"].setdefault(k, {"missed": 0})["missed"] = out["byKind"].get(k, {}).get("missed", 0) + 1; continue
        d = ue - hrot_sample(z, x0, y0, cell, x, y)
        out["byKind"].setdefault(k, {"missed": 0}).setdefault("d", []).append(d)
        diffs_all.append((abs(d), x, y, d, k))
    for k, v in out["byKind"].items():
        d = np.array(v.pop("d", []))
        if len(d): v.update(n=int(len(d)), mean=round(float(d.mean()), 4), meanAbs=round(float(np.abs(d).mean()), 4), maxAbs=round(float(np.abs(d).max()), 4))
    diffs_all.sort(reverse=True)
    out["worst"] = [{"x": round(x, 2), "y": round(y, 2), "diff": round(d, 4), "kind": k} for _, x, y, d, k in diffs_all[:10]]
    if a.buildings:
        out["buildings"] = compare_buildings(res, folder, name, frame, z, x0, y0, cell)
    out["generatorSeconds"] = res.get("generatorSeconds"); out["buildingConfig"] = res.get("buildingConfig")
    out["pluginErrors"] = (res.get("pluginErrors") or [])[:20]
    json.dump(out, open(os.path.join(folder, "lc-result.json"), "w"), indent=1)
    print(json.dumps(out, indent=1))


if __name__ == "__main__":
    main()
