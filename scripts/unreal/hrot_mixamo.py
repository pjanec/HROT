"""hrot_mixamo.py — runs in the Unreal editor COMMANDLET (CE-4651, U-16): Mixamo animations onto the UE5 mannequin.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_MIXAMO_JOB = { "folder", "out" }

1. imports every Mixamo FBX of `folder` (downloaded WITH skin): the first one with its mesh (the Mixamo skeleton, /Game/Mixamo/Mesh),
   every one as an animation on that skeleton (/Game/Mixamo/Anims) — the legacy FBX importer (Interchange off for the run)
2. an IK Rig for the Mixamo mesh, its retarget chains auto-generated (IKRigController.apply_auto_generated_retarget_definition)
3. a retargeter Mixamo → Lyra's IK_Mannequin (chains auto-mapped, both retarget poses auto-aligned)
4. every animation retargeted onto SKM_Manny (IKRetargetBatchOperation) → /Game/Mixamo/Anims/RTG_<name>
"""
import glob, json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_MIXAMO_JOB"], encoding="utf-8"))
R = {"log": []}
AT = unreal.AssetToolsHelpers.get_asset_tools()
EAL = unreal.EditorAssetLibrary


def log(m):
    unreal.log_warning(f"[HROT-MIXAMO] {m}"); R["log"].append(m)


def import_fbx(path, dest, mesh, skeleton=None):
    ui = unreal.FbxImportUI()
    ui.set_editor_property("import_mesh", mesh)
    ui.set_editor_property("import_as_skeletal", True)
    ui.set_editor_property("import_animations", True)
    ui.set_editor_property("import_materials", mesh)
    ui.set_editor_property("import_textures", mesh)
    ui.set_editor_property("create_physics_asset", False)
    ui.set_editor_property("automated_import_should_detect_type", False)
    ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_SKELETAL_MESH if mesh else unreal.FBXImportType.FBXIT_ANIMATION)
    if skeleton: ui.set_editor_property("skeleton", skeleton)
    ad = ui.get_editor_property("anim_sequence_import_data")
    ad.set_editor_property("import_bone_tracks", True)
    t = unreal.AssetImportTask()
    t.set_editor_property("filename", path); t.set_editor_property("destination_path", dest)
    t.set_editor_property("automated", True); t.set_editor_property("replace_existing", True); t.set_editor_property("save", True)
    t.set_editor_property("options", ui)
    AT.import_asset_tasks([t])
    return list(t.get_editor_property("imported_object_paths") or [])


def main():
    unreal.SystemLibrary.execute_console_command(None, "Interchange.FeatureFlags.Import.FBX false")   # the legacy importer's options
    files = sorted(glob.glob(os.path.join(JOB["folder"], "*.fbx")))
    if not files: raise RuntimeError("no FBX in " + JOB["folder"])
    # 1. the mesh + skeleton from the first file
    first = import_fbx(files[0], "/Game/Mixamo/Mesh", True)
    log(f"mesh import: {first}")
    mesh = next((unreal.load_asset(p) for p in first if isinstance(unreal.load_asset(p), unreal.SkeletalMesh)), None)
    if mesh is None: raise RuntimeError("no skeletal mesh imported from " + files[0])
    skel = mesh.get_editor_property("skeleton")
    anims = []
    for f in files:
        paths = import_fbx(f, "/Game/Mixamo/Anims", False, skel)
        got = [p for p in paths if isinstance(unreal.load_asset(p), unreal.AnimSequence)]
        anims += got
        log(f"{os.path.basename(f)} -> {[p.rsplit('/', 1)[-1] for p in got]}")
    R["anims"] = anims
    # 2. the Mixamo IK rig
    rig_path = "/Game/Mixamo/IK_Mixamo"   # a rerun reuses the assets (the retargeter references the rig: no delete)
    rig = unreal.load_asset(rig_path) if EAL.does_asset_exist(rig_path) else         AT.create_asset("IK_Mixamo", "/Game/Mixamo", unreal.IKRigDefinition, unreal.IKRigDefinitionFactory())
    rc = unreal.IKRigController.get_controller(rig)
    rc.set_skeletal_mesh(mesh)
    ok = rc.apply_auto_generated_retarget_definition()
    chains = [str(c.get_editor_property("chain_name")) for c in rc.get_retarget_chains()]
    log(f"IK_Mixamo: auto retarget definition {ok}; root {rc.get_retarget_root()}; {len(chains)} chains {chains}")
    # 3. the retargeter Mixamo -> Lyra mannequin
    target_rig = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Rig/IK_Mannequin")
    manny = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
    rtg_path = "/Game/Mixamo/RTG_Mixamo_To_Manny"
    rtg = unreal.load_asset(rtg_path) if EAL.does_asset_exist(rtg_path) else         AT.create_asset("RTG_Mixamo_To_Manny", "/Game/Mixamo", unreal.IKRetargeter, unreal.IKRetargetFactory())
    xc = unreal.IKRetargeterController.get_controller(rtg)
    xc.set_ik_rig(unreal.RetargetSourceOrTarget.SOURCE, rig)
    xc.set_ik_rig(unreal.RetargetSourceOrTarget.TARGET, target_rig)
    try: xc.add_default_ops()
    except Exception as e: log(f"add_default_ops: {e}")
    try: xc.assign_ik_rig_to_all_ops(unreal.RetargetSourceOrTarget.TARGET, target_rig)
    except Exception as e: log(f"assign target rig to ops: {e}")
    try: xc.assign_ik_rig_to_all_ops(unreal.RetargetSourceOrTarget.SOURCE, rig)
    except Exception as e: log(f"assign source rig to ops: {e}")
    try: xc.auto_map_chains(unreal.AutoMapChainType.FUZZY, True)
    except Exception as e: log(f"auto_map_chains: {e}")
    # T-pose (Mixamo) vs A-pose (Manny): ONLY the ARMS are aligned (the mannequin's arm bones onto Mixamo's). ⚠ Measured:
    # auto_align_all_bones on both sides also re-aimed the feet — every Mixamo clip came out on the mannequin with pointed toes.
    for side in (unreal.RetargetSourceOrTarget.SOURCE, unreal.RetargetSourceOrTarget.TARGET):
        try: xc.reset_retarget_pose(xc.get_current_retarget_pose_name(side), [], side)
        except Exception as e: log(f"reset_retarget_pose {side}: {e}")
    arms = [f"{b}_{s}" for s in ("l", "r") for b in ("clavicle", "upperarm", "lowerarm", "hand")]
    try: xc.auto_align_bones(arms, unreal.RetargetAutoAlignMethod.CHAIN_TO_CHAIN, unreal.RetargetSourceOrTarget.TARGET)
    except Exception as e: log(f"auto_align_bones arms: {e}")
    EAL.save_loaded_asset(rig); EAL.save_loaded_asset(rtg)
    # 4. retarget every animation onto SKM_Manny
    if EAL.does_directory_exist("/Game/Mixamo/Retargeted"):   # regenerate (the batch retarget does not overwrite)
        EAL.delete_directory("/Game/Mixamo/Retargeted")
    data = [unreal.EditorAssetLibrary.find_asset_data(p) for p in anims]
    try:
        res = unreal.IKRetargetBatchOperation.duplicate_and_retarget(
            assets_to_retarget=data, source_mesh=mesh, target_mesh=manny, ik_retarget_asset=rtg, search="", replace="",
            prefix="RTG_", suffix="", target_path="/Game/Mixamo/Retargeted", include_referenced_assets=False)
    except TypeError:
        log("duplicate_and_retarget signature: " + (unreal.IKRetargetBatchOperation.duplicate_and_retarget.__doc__ or "?"))
        raise
    out = [str(a.get_editor_property("package_name")) for a in res]
    for p in out: EAL.save_asset(p, False)
    R["retargeted"] = out
    log(f"retargeted {len(out)} animations onto SKM_Manny")


try:
    main(); R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
