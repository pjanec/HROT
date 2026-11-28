"""hrot_lods.py — runs in the Unreal editor COMMANDLET (CE-4651, U-16): LODs for a body pack that ships without them.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_LODS_JOB = { "folder", "lods", "out" }

Every skeletal mesh directly in `folder` gets `lods` levels (Unreal's built-in skeletal reduction, its default chain), only
where it has fewer — a rerun changes nothing. The result lists vertices and sections per LOD, before and after.
Measured need: Fab "US_Soldier" has ONE LOD on all 15 parts (~26 k vertices a soldier) — 200 of them for U-16.
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_LODS_JOB"], encoding="utf-8"))
R = {"log": [], "meshes": {}}


def log(m):
    unreal.log_warning(f"[HROT-LODS] {m}"); R["log"].append(m)


def stats(sms, m):
    n = sms.get_lod_count(m)
    return [[sms.get_num_verts(m, i), sms.get_num_sections(m, i)] for i in range(n)]


def main():
    sms = unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
    want = int(JOB.get("lods", 4))
    for p in unreal.EditorAssetLibrary.list_assets(JOB["folder"], recursive=False, include_folder=False):
        m = unreal.load_asset(p)
        if not isinstance(m, unreal.SkeletalMesh): continue
        before = stats(sms, m)
        if len(before) < want:
            ok = unreal.SkeletalMeshEditorSubsystem.regenerate_lod(m, want, False, False)
            if not ok: log(f"{m.get_name()}: regenerate_lod failed (mesh reduction unavailable?)")
            unreal.EditorAssetLibrary.save_loaded_asset(m, False)
        R["meshes"][m.get_name()] = {"before": before, "after": stats(sms, m)}
    tot = lambda k, i: sum(v[k][i][0] for v in R["meshes"].values() if len(v[k]) > i)
    log(f"{len(R['meshes'])} meshes; vertices per LOD (all parts): " + ", ".join(str(tot("after", i)) for i in range(want)))


try:
    main(); R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
