"""hrot_fbx_import.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17e): (re)imports FBX files as one static mesh each.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_FBX_JOB = { "imports": [{"file", "dest", "name"}],
                                                                                              "legacy": true, "out" }
    legacy   import with Unreal's classic FBX importer instead of Interchange (the default since 5.5) — the two place a Blender export's
             nodes differently; the 10-pack aircraft came in with parts out of place (U-17d pictures)
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_FBX_JOB"], encoding="utf-8"))
R = {"log": [], "assets": []}


def log(m):
    unreal.log_warning(f"[HROT-FBX] {m}"); R["log"].append(m)


def main():
    if JOB.get("legacy", True):
        unreal.SystemLibrary.execute_console_command(None, "Interchange.FeatureFlags.Import.FBX False")
    tasks = []
    for im in JOB["imports"]:
        ui = unreal.FbxImportUI()
        ui.set_editor_property("import_mesh", True)
        ui.set_editor_property("import_as_skeletal", False)
        ui.set_editor_property("import_materials", True)   # named slots (the paint's rules read them); no textures exist
        ui.set_editor_property("import_textures", False)
        ui.set_editor_property("import_animations", False)
        ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_STATIC_MESH)
        d = ui.get_editor_property("static_mesh_import_data")
        d.set_editor_property("combine_meshes", True)
        d.set_editor_property("generate_lightmap_u_vs", False)
        d.set_editor_property("convert_scene", True)
        t = unreal.AssetImportTask()
        t.set_editor_property("filename", im["file"]); t.set_editor_property("destination_path", im["dest"])
        t.set_editor_property("destination_name", im["name"]); t.set_editor_property("options", ui)
        t.set_editor_property("automated", True); t.set_editor_property("replace_existing", True); t.set_editor_property("save", True)
        tasks.append(t)
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks(tasks)
    for t in tasks:
        paths = list(t.get_editor_property("imported_object_paths"))
        R["assets"] += paths
        for p in paths:
            a = unreal.load_asset(p)
            if isinstance(a, unreal.StaticMesh):
                b = a.get_bounds()
                log(f"{p}: {a.get_num_triangles(0)} triangles, size {2*b.box_extent.x:.0f} x {2*b.box_extent.y:.0f} x {2*b.box_extent.z:.0f} cm")


try:
    main(); R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
