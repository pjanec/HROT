#!/usr/bin/env python3
"""look.py — CE-4651 (UL-10): build a LOOK level in Unreal from one exported terrain and render its views to PNG.

    python scripts/terrain-to-unreal.py <terrain> <export>/<name> --look      # the inputs (ground, cover mask, trees, views)
    python scripts/unreal/look.py <export>/<name> --project D:/Work/HrotUnreal/HrotUnreal.uproject --shots <folder>
           [--tree-mesh /Game/... --tree-material /Game/...] [--settle 20] [--keep-open]

Needs the HrotTerrain plugin in the project (tools/unreal/HrotTerrain). The shots land in --shots (never in git); the level is saved as
/Game/Maps/<name>_look.
"""
import argparse, json, os, subprocess, time

HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export"); ap.add_argument("--project", required=True); ap.add_argument("--shots", required=True)
    ap.add_argument("--engine", default=r"C:\UE_5.8\UE_5.8"); ap.add_argument("--settle", type=float, default=20)
    ap.add_argument("--tree-mesh"); ap.add_argument("--tree-material"); ap.add_argument("--keep-open", action="store_true")
    ap.add_argument("--wall-material"); ap.add_argument("--roof-material"); ap.add_argument("--no-buildings", action="store_true")
    ap.add_argument("--width", type=int, default=1920); ap.add_argument("--height", type=int, default=1080)
    a = ap.parse_args()
    folder = os.path.abspath(a.export); name = os.path.basename(folder)
    frame = json.load(open(os.path.join(folder, f"{name}.frame.json"), encoding="utf-8"))
    views = json.load(open(os.path.join(folder, f"{name}.views.json"), encoding="utf-8"))
    os.makedirs(a.shots, exist_ok=True)
    fwd = lambda p: os.path.abspath(p).replace("\\", "/")
    level = "/Game/Maps/" + name.replace("-", "_") + "_look"
    umap = os.path.join(os.path.dirname(os.path.abspath(a.project)), "Content", "Maps", level.rsplit("/", 1)[1] + ".umap")
    if os.path.exists(umap): os.remove(umap)
    job = {"terrain": name, "unreal": dict(frame["unreal"], r16=fwd(os.path.join(folder, frame["unreal"]["r16"]))),
           "cover": fwd(os.path.join(folder, f"{name}.cover.png")), "trees": fwd(os.path.join(folder, f"{name}.trees.bin")),
           "mask": views["mask"], "views": views["views"], "level": level, "shots": fwd(a.shots),
           "out": fwd(os.path.join(folder, "look-result.json")), "settleSeconds": a.settle, "width": a.width, "height": a.height,
           "treeMesh": a.tree_mesh, "treeMaterial": a.tree_material, "wallMaterial": a.wall_material, "roofMaterial": a.roof_material,
           "exportDir": fwd(folder)}
    bm = os.path.join(folder, f"{name}.buildings-mesh.json")
    if os.path.exists(bm) and not a.no_buildings:
        job["buildingMeshes"] = json.load(open(bm, encoding="utf-8"))
    job_path = os.path.join(folder, "look-job.json"); json.dump(job, open(job_path, "w"), indent=1)
    if os.path.exists(job["out"]): os.remove(job["out"])
    env = dict(os.environ, HROT_LOOK_JOB=job_path)
    if a.keep_open: env["HROT_LOOK_KEEP_OPEN"] = "1"
    exe = os.path.join(a.engine, "Engine", "Binaries", "Win64", "UnrealEditor.exe")
    t = time.time()
    rc = subprocess.run([exe, a.project, f"-ExecutePythonScript={os.path.join(HERE, 'hrot_look.py')}", "-unattended", "-nosplash",
                         "-NoSound", f"-abslog={os.path.join(folder, 'look-editor.log')}"], env=env).returncode
    print(f"editor exit {rc} after {time.time() - t:.0f} s")
    if os.path.exists(job["out"]):
        r = json.load(open(job["out"], encoding="utf-8"))
        print(json.dumps({k: r.get(k) for k in ("ok", "why", "seconds", "log", "shots")}, indent=1))
    else:
        print("no result — see look-editor.log")


if __name__ == "__main__":
    main()
