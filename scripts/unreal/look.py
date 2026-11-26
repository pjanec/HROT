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


def free_gb():
    """Free physical memory in GB (Windows), or None."""
    try:
        import ctypes
        class MS(ctypes.Structure):
            _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong), ("ullTotalPhys", ctypes.c_ulonglong),
                        ("ullAvailPhys", ctypes.c_ulonglong), ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                        ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong), ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]
        m = MS(); m.dwLength = ctypes.sizeof(MS)
        ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
        return m.ullAvailPhys / 2**30
    except Exception:
        return None


def find_assets(project):
    """The real assets the project holds (Fab / Megascans / the beech pack), found by NAME — no folder is hard-wired. Returns /Game paths."""
    import glob
    content = os.path.join(os.path.dirname(os.path.abspath(project)), "Content")
    def game(path):
        rel = os.path.relpath(path, content).replace("\\", "/")[:-len(".uasset")]
        return "/Game/" + rel + "." + os.path.basename(rel)
    def one(pattern):
        hits = sorted(glob.glob(os.path.join(content, "**", pattern), recursive=True))
        return game(hits[0]) if hits else None
    def surface(name):   # Megascans surface: its folder name starts with the human name; textures T_<id>_<res>_B / _N / _ORM
        hits = sorted(glob.glob(os.path.join(content, "**", "Surfaces", name + "_*", "**", "T_*_B.uasset"), recursive=True))
        if not hits: return None
        b = hits[0]; base = b[:-len("_B.uasset")]
        return {"B": game(b), "N": game(base + "_N.uasset"), "ORM": game(base + "_ORM.uasset"),
                "MI": game(glob.glob(os.path.join(os.path.dirname(os.path.dirname(b)), "Materials", "MI_*.uasset"))[0])}
    found = {"grass": surface("Grass"), "wildGrass": surface("Wild_Grass"), "forestFloor": surface("Forest_Floor"),
             "rock": surface("Rock_Cliff"), "soil": surface("Soil"), "gravel": surface("Gravel"),
             "plaster": surface("Plaster_Wall"), "roofTiles": surface("Red_Roof_Tiles"), "oldPlaster": surface("Old_Plaster_Wall"),
             "brick": surface("Red_Brick_Wall"), "roofDark": surface("Roof_Tiles"), "wood": surface("Wooden_Planks"), "dirt": surface("Dirt_Ground")}
    def plant(name):   # Megascans plant: its folder starts with the human name; meshes SM_<id>_Var*
        hits = sorted(glob.glob(os.path.join(content, "**", "Plants", name + "_*", "**", "SM_*.uasset"), recursive=True))
        return [game(h) for h in hits]
    found["grassMeshes"] = plant("Rough_Meadow_Grass") + plant("Wild_Grass") + plant("Tall_Grass")[:2]
    found["fernMeshes"] = plant("Fern")
    # the beech pack ships each tree twice (PivotPainter / SimpleWind wind setups): one set only, and the realistic forest sizes —
    # Forest_01 / 02 are 45-48 m giants (measured: their bounds), Forest_03 … 08 are 8-33 m
    found["trees"] = [game(p) for p in sorted(glob.glob(os.path.join(content, "**", "SimpleWind", "SM_EuropeanBeech_Forest_0[3-8].uasset"), recursive=True))]
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export"); ap.add_argument("--project", required=True); ap.add_argument("--shots", required=True)
    ap.add_argument("--engine", default=r"C:\UE_5.8\UE_5.8"); ap.add_argument("--settle", type=float, default=20)
    ap.add_argument("--tree-mesh"); ap.add_argument("--tree-material"); ap.add_argument("--keep-open", action="store_true")
    ap.add_argument("--wall-material"); ap.add_argument("--roof-material"); ap.add_argument("--no-buildings", action="store_true")
    ap.add_argument("--placeholders", action="store_true", help="ignore the project's real assets (pass 1/2 look)")
    ap.add_argument("--min-free-gb", type=float, default=3.0, help="stop the editor when free memory falls below this")
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
           "exportDir": fwd(folder), "roads": fwd(os.path.join(folder, f"{name}.roads.png")),
           "grass": fwd(os.path.join(folder, f"{name}.grass.bin")), "ferns": fwd(os.path.join(folder, f"{name}.ferns.bin"))}
    if not a.placeholders:
        job["assets"] = find_assets(a.project)
        print("assets:", json.dumps({k: (v if not isinstance(v, dict) else v["B"]) for k, v in job["assets"].items()}, indent=1))
    # a terrain's look options (<name>.look-options.json beside its export): {"roadMeshes": false} keeps HROT's road-scene meshes OUT of the level
    # (Stechovice, the user 2026-11-23: "the brown asphalt carpet on the top is useless ... wrong height and variously broken"; a rebuild on
    # 2026-11-25 put it back because the removal lived only in a later edit of the level)
    opts_path = os.path.join(folder, f"{name}.look-options.json")
    opts = json.load(open(opts_path, encoding="utf-8")) if os.path.exists(opts_path) else {}
    rm = os.path.join(folder, "roads", f"{name}.roads-mesh.json")   # hrot-roadscene: HROT's own TrafficRoadScene (UL-14)
    if os.path.exists(rm) and opts.get("roadMeshes", True):
        job["roadMeshes"] = json.load(open(rm, encoding="utf-8")); job["roadMeshDir"] = fwd(os.path.join(folder, "roads"))
    bm = os.path.join(folder, f"{name}.buildings-mesh.json")
    if os.path.exists(bm) and not a.no_buildings:
        job["buildingMeshes"] = json.load(open(bm, encoding="utf-8"))
    job_path = os.path.join(folder, "look-job.json"); json.dump(job, open(job_path, "w"), indent=1)
    if os.path.exists(job["out"]): os.remove(job["out"])
    env = dict(os.environ, HROT_LOOK_JOB=job_path)
    if a.keep_open: env["HROT_LOOK_KEEP_OPEN"] = "1"
    exe = os.path.join(a.engine, "Engine", "Binaries", "Win64", "UnrealEditor.exe")
    t = time.time()
    proc = subprocess.Popen([exe, a.project, f"-ExecutePythonScript={os.path.join(HERE, 'hrot_look.py')}", "-unattended", "-nosplash",
                             "-NoSound", f"-abslog={os.path.join(folder, 'look-editor.log')}"], env=env)
    # ⭐ a memory watchdog: pass 3's first attempt took the whole machine down (the session's memory-pressure stop). Below the floor
    # the editor (and its shader compilers) are stopped and the run reports it, instead of the machine running dry.
    lowest = None
    while proc.poll() is None:
        free = free_gb()
        lowest = free if lowest is None else min(lowest, free)
        if free is not None and free < a.min_free_gb:
            print(f"⛔ free memory {free:.1f} GB < {a.min_free_gb} GB — stopping the editor")
            subprocess.run(["taskkill", "/F", "/T", "/PID", str(proc.pid)], capture_output=True)
            subprocess.run(["taskkill", "/F", "/IM", "ShaderCompileWorker.exe"], capture_output=True)
            break
        time.sleep(5)
    rc = proc.wait()
    print(f"editor exit {rc} after {time.time() - t:.0f} s; lowest free memory {lowest if lowest is None else round(lowest, 1)} GB")
    if os.path.exists(job["out"]):
        r = json.load(open(job["out"], encoding="utf-8"))
        print(json.dumps({k: r.get(k) for k in ("ok", "why", "seconds", "log", "shots")}, indent=1))
    else:
        print("no result — see look-editor.log")
    # the water (hrot_water_mesh.py tiles) is laid by its own pass into the finished level - run it whenever the terrain has water tiles, so a
    # rebuild never drops the river (2026-11-25: the Stechovice rebuild lost the Vltava, added by hand on 2026-11-23)
    wm = os.path.join(folder, f"{name}.water-mesh.json")
    if os.path.exists(wm):
        wjob = os.path.join(folder, "water-job.json")
        json.dump({"level": level, "export": fwd(folder), "terrain": name, "out": fwd(os.path.join(folder, "water-result.json"))}, open(wjob, "w"), indent=1)
        w = subprocess.run([exe, a.project, f"-ExecutePythonScript={os.path.join(HERE, 'hrot_add_water.py')}", "-unattended", "-nosplash", "-NoSound",
                            f"-abslog={os.path.join(folder, 'water-editor.log')}"], env=dict(os.environ, HROT_WATER_JOB=wjob))
        print(f"water pass exit {w.returncode}: " + (open(os.path.join(folder, "water-result.json"), encoding="utf-8").read()[:300] if os.path.exists(os.path.join(folder, "water-result.json")) else "no result"))


if __name__ == "__main__":
    main()
