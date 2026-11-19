"""hrot_retarget.py — runs in the Unreal editor COMMANDLET (CE-4651, U-16 step 3): the mannequin's animations onto another body.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_RETARGET_JOB = { "target", "rig", "anims", "dest", "out" }

    target   the body's skeletal mesh (e.g. a City Sample crowd body, MetaHuman skeleton)
    rig      where its IK rig goes (made once, chains auto-generated — the MetaHuman / mannequin template is recognised)
    anims    regexes (full match) over the animation NAMES under /Game/Characters and /Game/Mixamo/Retargeted (on SKM_Manny)
    dest     the folder for the copies; each keeps its name (the host's clip table is per body by FOLDER, not by renamed clips)

The source is Lyra's SKM_Manny with its IK_Mannequin. Additive clips come with the clips they reference (their base pose).
"""
import json, os, re, traceback
import unreal

JOB = json.load(open(os.environ["HROT_RETARGET_JOB"], encoding="utf-8"))
R = {"log": []}
AT = unreal.AssetToolsHelpers.get_asset_tools()
EAL = unreal.EditorAssetLibrary


def log(m):
    unreal.log_warning(f"[HROT-RETARGET] {m}"); R["log"].append(m)


def get_or_create(path, cls, factory):
    if EAL.does_asset_exist(path): return unreal.load_asset(path)   # a rerun reuses (a retargeter references the rig: no delete)
    folder, name = path.rsplit("/", 1)
    return AT.create_asset(name, folder, cls, factory)


def in_place(paths):
    """⭐ Root motion OFF on the copies: the retargeted root carries the pelvis HEIGHT (the retargeter's root-motion op), and sampling a
    root-motion clip locks the root to the reference pose (z = 0) — measured: Lyra's moving clips sank ~80 cm on the crowd bodies.
    The person plays clips in place anyway (HROT moves the actor; UHrotPersonAnimInstance drops the root's horizontal travel)."""
    for p in paths:
        a = unreal.load_asset(p)
        if not isinstance(a, unreal.AnimSequence): continue
        for prop in ("enable_root_motion", "force_root_lock"):   # either one pins the root to the reference pose when sampled
            if a.get_editor_property(prop):
                a.set_editor_property(prop, False); log(f"{prop} off: {p.rsplit('/', 1)[-1]}")


def main():
    if JOB.get("fixOnly"):   # only the in-place fix on what is already in `dest`
        out = [p.rsplit(".", 1)[0] for p in EAL.list_assets(JOB["dest"], recursive=True, include_folder=False)]
        in_place(out)
        for p in out: EAL.save_asset(p, True)
        R["retargeted"] = out; return
    target = unreal.load_asset(JOB["target"])
    if not isinstance(target, unreal.SkeletalMesh): raise RuntimeError("not a skeletal mesh: " + JOB["target"])
    manny = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
    src_rig = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Rig/IK_Mannequin")
    sc = unreal.IKRigController.get_controller(src_rig)
    log(f"source IK_Mannequin: root {sc.get_retarget_root()}; chains " +
        str([(str(c.get_editor_property('chain_name')), str(c.get_editor_property('start_bone').get_editor_property('bone_name')),
              str(c.get_editor_property('end_bone').get_editor_property('bone_name'))) for c in sc.get_retarget_chains()]))
    # 1. the target's IK rig
    rig = get_or_create(JOB["rig"], unreal.IKRigDefinition, unreal.IKRigDefinitionFactory())
    rc = unreal.IKRigController.get_controller(rig)
    rc.set_skeletal_mesh(target)
    if JOB.get("chains"):   # ⭐ a skeleton the auto-definition does not know (Rigify DEF-*): the chains by hand, named as IK_Mannequin's
        ok = "by hand"
        for c in list(rc.get_retarget_chains()): rc.remove_retarget_chain(c.get_editor_property("chain_name"))
        rc.set_retarget_root(JOB["root"])
        for name, start, end in JOB["chains"]:
            rc.add_retarget_chain(name, start, end, "")
    else:
        ok = rc.apply_auto_generated_retarget_definition()
    chains = [str(c.get_editor_property("chain_name")) for c in rc.get_retarget_chains()]
    log(f"{JOB['rig']}: auto retarget definition {ok}; root {rc.get_retarget_root()}; {len(chains)} chains {chains}")
    # 2. the retargeter Manny -> target
    rtg = get_or_create(JOB["rig"].rsplit("/", 1)[0] + "/RTG_Manny_To_" + JOB["rig"].rsplit("/", 1)[1].replace("IK_", ""),
                        unreal.IKRetargeter, unreal.IKRetargetFactory())
    xc = unreal.IKRetargeterController.get_controller(rtg)
    xc.set_ik_rig(unreal.RetargetSourceOrTarget.SOURCE, src_rig)
    xc.set_ik_rig(unreal.RetargetSourceOrTarget.TARGET, rig)
    for what, f in (("add_default_ops", lambda: xc.add_default_ops()),
                    ("assign target", lambda: xc.assign_ik_rig_to_all_ops(unreal.RetargetSourceOrTarget.TARGET, rig)),
                    ("assign source", lambda: xc.assign_ik_rig_to_all_ops(unreal.RetargetSourceOrTarget.SOURCE, src_rig)),
                    ("auto_map_chains", lambda: xc.auto_map_chains(unreal.AutoMapChainType.FUZZY, True))):
        try: f()
        except Exception as e: log(f"{what}: {e}")
    # ⭐ NO auto-alignment: the mannequin, the crowd body and the soldier share one reference pose (identical foot axes, measured),
    # and auto_align_all_bones re-aimed the feet — Lyra's clips came out toes-up ~80° on every other body. Both poses reset.
    for side in (unreal.RetargetSourceOrTarget.SOURCE, unreal.RetargetSourceOrTarget.TARGET):
        try: xc.reset_retarget_pose(xc.get_current_retarget_pose_name(side), [], side)
        except Exception as e: log(f"reset_retarget_pose {side}: {e}")
    if JOB.get("alignTarget"):   # a T-posed body: its ARM bones aligned onto the A-posed mannequin's (only these: the feet stay)
        T = unreal.RetargetSourceOrTarget.TARGET
        def offsets():
            return {b: str(xc.get_rotation_offset_for_retarget_pose_bone(b, T).rotator()) for b in JOB["alignTarget"][:3]}
        for method in ([getattr(unreal.RetargetAutoAlignMethod, JOB["alignMethod"])] if JOB.get("alignMethod") else
                       [unreal.RetargetAutoAlignMethod.CHAIN_TO_CHAIN, unreal.RetargetAutoAlignMethod.MESH_TO_MESH]):
            try: xc.auto_align_bones(JOB["alignTarget"], method, T)
            except Exception as e: log(f"auto_align_bones {method}: {e}")
            o = offsets()
            log(f"aligned target arms by {method}: {o}")
            if any("0.000000, 0.000000, 0.000000" not in v for v in o.values()): break   # it moved something
    EAL.save_loaded_asset(rig); EAL.save_loaded_asset(rtg)
    # 3. the animations, by name
    paths = list(EAL.list_assets("/Game/Characters", recursive=True, include_folder=False))
    if EAL.does_directory_exist("/Game/Mixamo/Retargeted"):
        paths += list(EAL.list_assets("/Game/Mixamo/Retargeted", recursive=True, include_folder=False))
    names = {p.rsplit(".", 1)[-1]: p.rsplit(".", 1)[0] for p in paths}
    want, missing = [], []
    for rx in JOB["anims"]:
        rxc = re.compile(rx, re.I)
        hits = [n for n in sorted(names) if rxc.fullmatch(n)]
        hits = [n for n in hits if isinstance(unreal.load_asset(names[n]), unreal.AnimSequence)]
        if not hits: missing.append(rx); continue
        for n in hits[:1]:
            if names[n] not in want: want.append(names[n])
    log(f"{len(want)} animations to retarget; not found: {missing}")
    R["missing"] = missing
    if EAL.does_directory_exist(JOB["dest"]):   # regenerate: the batch retarget does not overwrite (it would add suffixed copies)
        EAL.delete_directory(JOB["dest"]); log(f"cleared {JOB['dest']}")
    data = [EAL.find_asset_data(p) for p in want]
    res = unreal.IKRetargetBatchOperation.duplicate_and_retarget(
        assets_to_retarget=data, source_mesh=manny, target_mesh=target, ik_retarget_asset=rtg, search="", replace="",
        prefix="", suffix="", target_path=JOB["dest"], include_referenced_assets=True)
    out = [str(a.get_editor_property("package_name")) for a in res]
    in_place(out)
    for p in out: EAL.save_asset(p, False)
    R["retargeted"] = out
    log(f"retargeted {len(out)} assets into {JOB['dest']}")


try:
    main(); R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
