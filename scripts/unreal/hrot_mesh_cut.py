"""hrot_mesh_cut.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17i): a static mesh measured and CUT into parts by boxes.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_MESH_CUT_JOB = { "mesh", "profile": {"axis", "bins"},
                                                                                  "parts": [{"to", "boxes": [[min xyz, max xyz]], "invert"}], "out" }
    profile   the vertices binned along one axis (0 x, 1 y, 2 z): per bin the count and the other two axes' ranges — where a body ends
              and a wing begins (the dragon's wings: a static mesh, no skeleton — U-17i)
    parts     a new static mesh per part: the triangles whose 3 corners lie in any of `boxes` (mesh space), or — `invert` — all the others;
              the part keeps the source's materials and its vertices stay where they were (the kit places parts at the origin)
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_MESH_CUT_JOB"], encoding="utf-8"))
R = {"log": []}
GS = unreal


def log(m):
    unreal.log_warning(f"[HROT-CUT] {m}"); R["log"].append(m)


def load_dm(sm):
    dm = unreal.DynamicMesh()
    res = unreal.GeometryScript_AssetUtils.copy_mesh_from_static_mesh(sm, dm, unreal.GeometryScriptCopyMeshFromAssetOptions(), unreal.GeometryScriptMeshReadLOD())
    return res[0] if isinstance(res, tuple) else dm


def profile(sm, P):
    dm = load_dm(sm)
    res = unreal.GeometryScript_MeshQueries.get_all_vertex_positions(dm, True)
    items = res if isinstance(res, tuple) else (res,)
    log("get_all_vertex_positions -> " + ", ".join(f"{type(x).__name__}{[a for a in dir(x) if not a.startswith('_')][:12]}" for x in items))
    lst = next(x for x in items if type(x).__name__ == "GeometryScriptVectorList")
    r2 = lst.convert_vector_list_to_array()
    pts = r2[1] if isinstance(r2, tuple) else r2
    ax = int(P.get("axis", 0)); bins = int(P.get("bins", 40))
    co = [c for c in (0, 1, 2) if c != ax]
    vals = [(p.x, p.y, p.z) for p in pts]
    if P.get("within"):   # only the vertices inside a box (mesh space) — e.g. a strip along the spine: the back's height, not a wing's
        (x0, y0, z0), (x1, y1, z1) = P["within"]
        vals = [v for v in vals if x0 <= v[0] <= x1 and y0 <= v[1] <= y1 and z0 <= v[2] <= z1]
    lo = min(v[ax] for v in vals); hi = max(v[ax] for v in vals)
    step = (hi - lo) / bins
    table = []
    for b in range(bins):
        a, z = lo + b * step, lo + (b + 1) * step
        sel = [v for v in vals if a <= v[ax] < z or (b == bins - 1 and v[ax] == hi)]
        if not sel: table.append([round(a, 1), 0]); continue
        r1 = (min(v[co[0]] for v in sel), max(v[co[0]] for v in sel))
        r2 = (min(v[co[1]] for v in sel), max(v[co[1]] for v in sel))
        table.append([round(a, 1), len(sel), [round(r1[0], 1), round(r1[1], 1)], [round(r2[0], 1), round(r2[1], 1)]])
        log(f"axis{ax} {a:7.1f}..{z:7.1f}: {len(sel):6} verts  axis{co[0]} {r1[0]:7.1f}..{r1[1]:7.1f}  axis{co[1]} {r2[0]:7.1f}..{r2[1]:7.1f}")
    R["profile"] = table
    R["count"] = len(vals)


def pick(res, type_name):
    """Geometry Script's Python returns (out params…) as a tuple: the item of `type_name`"""
    items = res if isinstance(res, tuple) else (res,)
    for x in items:
        if type(x).__name__ == type_name: return x
    raise RuntimeError(f"no {type_name} in {[type(x).__name__ for x in items]}")


def cut(sm, P):
    dm = load_dm(sm)
    boxes = [unreal.Box(unreal.Vector(*b[0]), unreal.Vector(*b[1])) for b in P["boxes"]]
    sel = None
    for b in boxes:
        r = unreal.GeometryScript_MeshSelection.select_mesh_elements_in_box(dm, b, unreal.GeometryScriptMeshSelectionType.TRIANGLES, False, 3)
        s = pick(r, "GeometryScriptMeshSelection")
        sel = s if sel is None else pick(unreal.GeometryScript_MeshSelection.combine_mesh_selections(sel, s, unreal.GeometryScriptCombineSelectionMode.ADD), "GeometryScriptMeshSelection")
    if not P.get("invert"):   # keep the selection: delete the rest
        sel = pick(unreal.GeometryScript_MeshSelection.invert_mesh_selection(dm, sel), "GeometryScriptMeshSelection")
    r = unreal.GeometryScript_MeshEdits.delete_selected_triangles_from_mesh(dm, sel)
    log(f"  deleted {r[1] if isinstance(r, tuple) else r} triangles")
    folder, name = P["to"].rsplit("/", 1)
    if unreal.EditorAssetLibrary.does_asset_exist(P["to"]): unreal.EditorAssetLibrary.delete_asset(P["to"])
    res = unreal.GeometryScript_NewAssetUtils.create_new_static_mesh_asset_from_mesh(dm, P["to"], unreal.GeometryScriptCreateNewStaticMeshAssetOptions())
    new = unreal.load_asset(P["to"])
    if new is None: raise RuntimeError("could not create " + P["to"])
    src = list(sm.get_editor_property("static_materials")); mats = list(new.get_editor_property("static_materials"))
    for i, s in enumerate(mats):
        if i < len(src): s.set_editor_property("material_interface", src[i].get_editor_property("material_interface")); mats[i] = s
    new.set_editor_property("static_materials", mats)
    unreal.EditorAssetLibrary.save_loaded_asset(new, False)
    b = new.get_bounds()
    log(f"{P['to']}: {new.get_num_triangles(0)} tris, origin {b.origin.x:.1f},{b.origin.y:.1f},{b.origin.z:.1f} extent {b.box_extent.x:.1f},{b.box_extent.y:.1f},{b.box_extent.z:.1f}")


try:
    for J in (JOB.get("jobs") or [JOB]):   # U-18k: several meshes in one run ({"jobs": [{"mesh", "profile", "parts"}], "out"})
        sm = unreal.load_asset(J["mesh"])
        log(f"mesh {J['mesh']}")
        if J.get("profile"): profile(sm, J["profile"])
        for P in J.get("parts", []): cut(sm, P)
    R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
