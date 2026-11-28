"""hrot_person_kit.py — CE-4651 (U-18d): builds a UHrotPersonKit (a person body as one asset) from what hrot_person MEASURES, run in
the full Unreal EDITOR (-ExecutePythonScript; it spawns throwaway bodies to measure them).

    HROT_PERSON_KIT_JOB = { "body": "US_Soldier", "asset": "/Game/Hrot/People/DA_Person_USSoldier", "out": <json> }
                        or { "kits": [{"body", "asset"}, ...], "out" }   (U-18i: several in one run — the civilians' crowd bodies)

The kit holds: the body (Blueprint or mesh), its leader mesh's yaw and height on the actor (PersonKit.body_offsets), the components to
hide (its own gun), the rifle and where the hand holds it (PersonKit.weapon_at_hand — the grip through the shared aim pose), the two
sides' camouflage, and the CLIP TABLE by role (the role names are HrotDpsPeople's: "<Rifle|Unarmed>.<Stand|Crouch|Prone>.<Idle|Walk|Run>",
"Upper.Ready", "Upper.Aimed", "Death.<Stand|Crouch|Prone>", "Dying", "Transition.CrouchToProne", "Phone", "Gesture.Wave",
"Gesture.Throw", "Action.Fire" (additive), "Hit.Front"). A role whose clip the body lacks is left out — the run time falls back (HrotDpsPeople::Fallback).
"""
import json, os, sys, time, traceback
import unreal

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hrot_person

JOB = json.load(open(os.environ["HROT_PERSON_KIT_JOB"], encoding="utf-8-sig"))
R = {"log": []}

# the shoulder tube (the vehicle lab's, scratchpad unreal/rpg/job.json "launcher": the Fab RPG-7, measured there by eye on a soldier)
LAUNCHER = {"mesh": "/Game/Fab/RPG7/rpg7/StaticMeshes/rpg7.rpg7", "scale": 1.5, "back": 70, "up": 8}
# role → clip name (regex, full match, in the body's clip folder) — the body's clips are retargeted copies (retarget.py)
ROLES = {
    "Rifle.Stand.Idle": "MM_Rifle_Idle_Hipfire", "Rifle.Stand.Walk": "MF_Rifle_Walk_Fwd", "Rifle.Stand.Run": "MF_Rifle_Jog_Fwd",
    "Rifle.Crouch.Idle": "MM_Rifle_Crouch_Idle", "Rifle.Crouch.Walk": "MM_Rifle_Crouch_Walk_Fwd",
    "Rifle.Prone.Idle": "RTG_Prone_Idle.*", "Rifle.Prone.Walk": "RTG_Prone_Forward.*",
    "Unarmed.Stand.Idle": "RTG_Neutral_Idle.*", "Unarmed.Stand.Walk": "MM_Unarmed_Walk_Fwd", "Unarmed.Stand.Run": "MM_Unarmed_Jog_Fwd",
    "Unarmed.Crouch.Idle": "MM_Unarmed_Crouch_Idle", "Unarmed.Crouch.Walk": "MM_Unarmed_Crouch_Walk_Fwd",
    "Unarmed.Prone.Idle": "RTG_Prone_Idle.*", "Unarmed.Prone.Walk": "RTG_Prone_Forward.*",
    "Upper.Ready": "MM_Rifle_Idle_Hipfire", "Upper.Aimed": "MM_Rifle_Idle_ADS",
    "Death.Stand": "RTG_Death_From_The_Front.*", "Death.Crouch": "RTG_Crouch_Death.*", "Death.Prone": "RTG_Prone_Death.*",
    "Dying": "RTG_Dying.*", "Transition.CrouchToProne": "RTG_Crouch_To_Prone.*",
    "Phone": "RTG_Talking_On_Phone.*", "Gesture.Wave": "RTG_Waving.*", "Gesture.Throw": "RTG_Throw.*",
    "Action.Fire": "MM_Rifle_Fire", "Hit.Front": "MM_HitReact_Front_Hvy_01",   # U-18e: the kick (an ADDITIVE clip) and the flinch
}


def log(m):
    m = f"{m}"; unreal.log_warning(f"[HROT-PKIT] {m}"); R["log"].append(m)


def build(body, path):
    kit_tools = hrot_person.PersonKit(log)
    EAL = unreal.EditorAssetLibrary
    folder, name = path.rsplit("/", 1)
    # every property is set below, so the kit is defined HERE, never by a stale value. ⚠ measured: deleting the existing asset failed
    # silently (it is loaded) and create_asset then returned None — so an existing kit is updated in place
    kit = unreal.load_asset(path) if EAL.does_asset_exist(path) else \
        unreal.AssetToolsHelpers.get_asset_tools().create_asset(name, folder, unreal.HrotPersonKit, unreal.DataAssetFactory())
    if kit is None: raise RuntimeError("cannot create or load " + path)

    # the body and its placement on the actor
    if body in hrot_person.BODY_MESHES:
        mesh = unreal.load_asset(hrot_person.BODY_MESHES[body][0])
        kit.set_editor_property("body_mesh", mesh)
        yaw_off, dz = hrot_person.BODY_MESHES[body][2], 0.0
        probe = hrot_person.spawn(unreal.SkeletalMeshActor, (0, 0, -40000)); probe.skeletal_mesh_component.set_skeletal_mesh_asset(mesh)
        leader = probe.skeletal_mesh_component
    else:
        cls = EAL.load_blueprint_class(hrot_person.body_of(body)[0])
        kit.set_editor_property("body_class", cls)
        yaw_off, dz = kit_tools.body_offsets(cls)
        probe = hrot_person.spawn(cls, (0, 0, -40000)); leader = hrot_person.body_component(probe)
    kit.set_editor_property("yaw_offset", float(yaw_off)); kit.set_editor_property("z_offset", float(dz))
    kit.set_editor_property("hide_components", [unreal.Name(n) for n in hrot_person.BODY_HIDE.get(body, [])])

    # the rifle in the hand: the same rule as PersonKit.set_weapon (Lyra's yaw −90, the grip moved through the shared aim pose)
    turn = unreal.Transform(rotation=unreal.Rotator(roll=0, pitch=0, yaw=-90))
    if body is None and leader.does_socket_exist("weapon_r"): sock, off = "weapon_r", turn
    else: sock, off = hrot_person.HAND.get(body, "hand_r"), turn * kit_tools.weapon_at_hand(leader, body)
    probe.destroy_actor()
    kit.set_editor_property("rifle", unreal.load_asset(hrot_person.HELD["rifle"]))
    kit.set_editor_property("weapon_socket", unreal.Name(sock)); kit.set_editor_property("weapon_offset", off)
    # U-18f — the shoulder TUBE: the vehicle lab's shoulder_launcher rule, measured once here instead of on a posed soldier — the tube along
    # the rifle's barrel line (its Y axis onto the barrel), its middle `back` cm behind the rifle's muzzle and `up` cm above the barrel, in
    # the RIFLE's frame (the muzzle socket is the rifle mesh's own: independent of the pose), then composed with the rifle's grip
    L = LAUNCHER
    probe = hrot_person.spawn(unreal.SkeletalMeshActor, (0, 0, -45000)); rc = probe.skeletal_mesh_component
    rc.set_skeletal_mesh_asset(unreal.load_asset(hrot_person.HELD["rifle"]))
    M = rc.get_socket_transform("Muzzle", unreal.RelativeTransformSpace.RTS_COMPONENT); probe.destroy_actor()
    fwd = M.rotation.get_axis_x(); up = M.rotation.get_axis_z(); fwd.normalize(); up.normalize()
    k = float(L["scale"]); sm = unreal.load_asset(L["mesh"]); b = sm.get_bounds()
    rot = unreal.MathLibrary.make_rot_from_yz(fwd, up)
    centre = M.translation - fwd * float(L["back"]) + up * float(L["up"])
    loc = centre - rot.quaternion().rotate_vector(b.origin * k)
    in_rifle = unreal.Transform(location=loc, rotation=rot, scale=unreal.Vector(k, k, k))
    kit.set_editor_property("launcher", sm); kit.set_editor_property("launcher_offset", unreal.MathLibrary.compose_transforms(in_rifle, off))
    log(f"tube {sm.get_name()} on the shoulder: {L['back']} cm behind the rifle's muzzle, {L['up']} cm above, x{k}")
    log(f"{body}: yaw offset {yaw_off:.1f}, z offset {dz:.1f}, rifle at {sock}")

    # the sides' camouflage
    slots = hrot_person.SIDE_SLOTS.get(body, [])
    if slots:
        kit.set_editor_property("side_materials", {s: kit_tools.side_material(s) for s in ("A", "B")})
        kit.set_editor_property("side_slots", [unreal.Name(s) for s in slots])

    # the clip table
    clips, missing = {}, []
    for role, rx in ROLES.items():
        n, a = kit_tools.resolve(rx, body)
        if a: clips[unreal.Name(role)] = a
        else: missing.append(role)
    kit.set_editor_property("clips", clips)
    R.setdefault("kits", {})[path] = {"roles": len(clips), "missing": missing}
    R["roles"] = {str(k): v.get_name() for k, v in clips.items()}; R["missing"] = missing
    log(f"{len(clips)} roles, missing {missing}")
    EAL.save_loaded_asset(kit, False)
    R["asset"] = path


S = {"done": False, "handle": None}


def tick(dt):
    if S["done"]: return
    S["done"] = True
    try:
        unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).new_level("/Game/Maps/PersonKitTmp") if not unreal.EditorAssetLibrary.does_asset_exist("/Game/Maps/PersonKitTmp") else None
        for k in (JOB.get("kits") or [JOB]): build(k["body"], k["asset"])   # U-18i: several kits in one editor run
        R["ok"] = True
    except Exception:
        R["ok"] = False; R["why"] = traceback.format_exc()
    json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
    unreal.unregister_slate_post_tick_callback(S["handle"])
    unreal.SystemLibrary.quit_editor()


S["handle"] = unreal.register_slate_post_tick_callback(tick)
unreal.EditorPythonScripting.set_keep_python_script_alive(True)
