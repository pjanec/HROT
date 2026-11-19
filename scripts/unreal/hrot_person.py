"""hrot_person.py — ONE way to put a state-driven person into an Unreal editor world (CE-4651, U-16). Used by the anim lab
(hrot_animlab.py) and the crowd test (hrot_crowd.py); imported inside the editor (unreal module), never run alone.

    kit = PersonKit(log)
    actor, state, leader = kit.make("US_Soldier", (x, y, z), facing=140, label="Soldier_01",
                                    state={"base": "MF_Rifle_Walk_Fwd", "upper": "MM_Rifle_Idle_ADS", "weapon": "rifle"})

A body is None (the UE5 mannequin), "BP_Person_NN" (a City Sample crowd person) or a BODIES name. The person's STATE lives on its
UHrotPersonState (clips, aim, the weapon in the hand); the clips are found by NAME in the body's own clip folder (retarget.py).
Every rule below was measured, not assumed (DRAFT_Unreal_Image_Generator.md U-16b / U-16c).
"""
import re
import unreal

# a dressed body: its Blueprint and the folder holding the clips on its skeleton (scripts/unreal/retarget.py)
BODIES = {"US_Soldier": ("/Game/US_Soldier/Blueprint/US_Soldier", "/Game/Hrot/People/Anims_USSoldier")}
# a body's OWN weapon meshes, hidden: the weapon is the person state's (measured: the US_Soldier Blueprint shows its "Gun" in play)
BODY_HIDE = {"US_Soldier": ["Gun"]}
# the person HOLDS Lyra's skinned weapons: they carry the Muzzle / ShellEject sockets, and Lyra's attach transform is theirs
# (measured: SM_Rifle is SK_Rifle turned 180° — sockets derived for it landed on the stock)
HELD = {"rifle": "/Game/Weapons/Rifle/Mesh/SK_Rifle", "pistol": "/Game/Weapons/Pistol/Mesh/SK_Pistol"}
MANNY = "/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny"
EAS = lambda: unreal.get_editor_subsystem(unreal.EditorActorSubsystem)


def body_of(name):
    """BP_Person_NN (City Sample crowd) or a BODIES entry → (Blueprint path, clip folder)."""
    return BODIES.get(name) or ("/Game/Hrot/People/" + name, "/Game/Hrot/People/Anims")


def spawn(cls, loc, rot=(0, 0, 0), label=None, folder=None):
    """⚠ label and folder are set HERE, at once: changing them later is an editor edit, which REBUILDS a Blueprint person's
    components (measured, crowd test: the weapon was left behind by the rebuilt body)."""
    a = EAS().spawn_actor_from_class(cls, unreal.Vector(*loc), unreal.Rotator(roll=rot[2], pitch=rot[0], yaw=rot[1]))
    if a and label: a.set_actor_label(label)
    if a and folder: a.set_folder_path(folder)
    return a


def body_component(actor):
    """The dressed person's LEADER — the component the body and outfit parts follow (leader pose). Measured on the crowd Blueprints:
    a hidden root `SkeletalMeshComponent0` (the *_base mesh, playing the crowd's own clip) leads Body, Top, Bottom, Shoes; the Face
    runs its own head Anim BP. ⚠ Driving `Body` does nothing: a follower copies its leader's pose."""
    comps = list(actor.get_components_by_class(unreal.SkeletalMeshComponent))
    leaders = {c.get_editor_property("leader_pose_component") for c in comps}
    for c in comps:
        if c in leaders: return c
    return next((c for c in comps if c.get_skinned_asset()), None)   # a single mesh (the mannequin actor)


def hold_still(actor):
    """HROT moves the person; a Character Blueprint's own movement (gravity, an AI controller possessing it in play) must not.
    Measured: the US_Soldier Character vanished in Simulate (it fell) while the crowd actors stayed."""
    for c in actor.get_components_by_class(unreal.CharacterMovementComponent):
        c.set_editor_property("gravity_scale", 0.0); c.set_component_tick_enabled(False); c.set_editor_property("auto_activate", False)
    if isinstance(actor, unreal.Pawn):
        actor.set_editor_property("auto_possess_ai", unreal.AutoPossessAI.DISABLED)


def mesh_of(comp):
    for getter in ("get_skinned_asset", "get_skeletal_mesh_asset"):
        try:
            m = getattr(comp, getter)()
            if m: return m
        except Exception: pass
    return comp.get_editor_property("skeletal_mesh_asset")


class PersonKit:
    def __init__(self, log, mannequin_clip_roots=("/Game/Characters", "/Game/Mixamo/Retargeted")):
        self.log = log
        EAL = unreal.EditorAssetLibrary
        paths = []
        for root in mannequin_clip_roots:
            if EAL.does_directory_exist(root): paths += list(EAL.list_assets(root, recursive=True, include_folder=False))
        self.names = {p.rsplit(".", 1)[-1]: p for p in paths}   # the mannequin's clips (Lyra + Mixamo retargeted onto it)
        self.clip_index, self.hand_rel, self.offsets = {}, {}, {}
        self.manny = unreal.load_asset(MANNY)
        self.held = {k: unreal.load_asset(p) for k, p in HELD.items()}
        t = spawn(unreal.SkeletalMeshActor, (0, 0, -5000))
        t.skeletal_mesh_component.set_skeletal_mesh_asset(self.manny)
        cs = t.skeletal_mesh_component
        self.weapon_rel = cs.get_socket_transform("weapon_r", unreal.RelativeTransformSpace.RTS_PARENT_BONE_SPACE)
        t.destroy_actor()

    # ---- clips -----------------------------------------------------------------------------------------------------------------
    def clip_names(self, folder):
        if folder not in self.clip_index:
            EAL = unreal.EditorAssetLibrary
            self.clip_index[folder] = ({p.rsplit(".", 1)[-1]: p for p in EAL.list_assets(folder, recursive=True, include_folder=False)}
                                       if EAL.does_directory_exist(folder) else {})
        return self.clip_index[folder]

    def resolve(self, rx, body=None):
        """The clip named by `rx` (regex, full match) — on the mannequin, or (body = a body name) on that body's skeleton."""
        rxc = re.compile(rx, re.I)
        names = self.clip_names(body_of(body)[1]) if body else self.names
        for n in sorted(names):
            if rxc.fullmatch(n):
                a = unreal.load_asset(names[n])
                if isinstance(a, unreal.AnimSequenceBase): return n, a
        return None, None

    # ---- placement -------------------------------------------------------------------------------------------------------------
    def body_offsets(self, cls):
        """A body Blueprint's leader mesh relative to its actor: (yaw offset, height of the mesh origin above the actor origin) — so
        the real one is SPAWNED in place like the mannequin (+Y toward `facing`, feet at z). Measured on a throwaway copy, once per class.
        ⚠ moving a crowd person after spawning re-runs its construction script (a new outfit, the head and hair groom left behind)."""
        key = cls.get_path_name()
        if key not in self.offsets:
            t = spawn(cls, (0, 0, -20000))
            sk = body_component(t)
            self.offsets[key] = (sk.get_world_rotation().yaw - t.get_actor_rotation().yaw, sk.get_world_location().z - t.get_actor_location().z)
            t.destroy_actor()
            self.log(f"body offsets {key.rsplit('.', 1)[-1]}: yaw {self.offsets[key][0]:.0f}, z {self.offsets[key][1]:.1f}")
        return self.offsets[key]

    # ---- the weapon in the hand ------------------------------------------------------------------------------------------------
    def posed_sockets(self, mesh, clip, names):
        """Component-space transforms of `names` with `mesh` posed by `clip` at time 0."""
        t = spawn(unreal.SkeletalMeshActor, (0, 0, -30000))
        c = t.skeletal_mesh_component; c.set_skeletal_mesh_asset(mesh)
        c.set_animation_mode(unreal.AnimationMode.ANIMATION_SINGLE_NODE)
        c.set_update_animation_in_editor(True)
        c.play_animation(clip, False); c.set_position(0.0, False)
        unreal.HrotTerrainLibrary.pose_now(c, 0.0, 1)
        out = {n: c.get_socket_transform(n, unreal.RelativeTransformSpace.RTS_COMPONENT) for n in names}
        t.destroy_actor()
        return out

    def weapon_at_hand(self, comp, body):
        """Where the mannequin holds its weapon (weapon_r), relative to THIS body's hand_r — compared in the SAME animated pose (the
        rifle aim clip: the mannequin's original, the body's retargeted copy; retargeting keeps the hand's orientation in the body).
        ⚠ Measured, both shortcuts wrong: the mannequin's hand-relative offset tilted the soldier's rifle ~25° (other bone axes); the
        reference-pose comparison reversed it (the skeletons' reference poses hold the hand differently)."""
        mesh = mesh_of(comp)
        key = mesh.get_path_name()
        if key not in self.hand_rel:
            clip = "MM_Rifle_Idle_ADS"
            manny = self.posed_sockets(self.manny, self.resolve(clip)[1], ("weapon_r",))
            mine = self.posed_sockets(mesh, self.resolve(clip, body)[1], ("hand_r",))
            self.hand_rel[key] = manny["weapon_r"] * mine["hand_r"].inverse()
            self.log(f"weapon at hand_r for {mesh.get_name()}: from the {clip} pose")
        return self.hand_rel[key]

    def set_weapon(self, state, leader, body, kind):
        """`kind` "rifle" / "pistol" / None — the weapon in the hand, as part of the person state."""
        weapon = self.held.get(kind) if kind else None
        setattr(state, "weapon", weapon)
        if weapon:
            # Lyra's AttachTransform for its SK_ weapons: yaw −90 at weapon_r. ⚠ Measured, not judged by eye: with +90 the muzzle pointed
            # INTO the person on every row. Lyra's weapon_r holds only on the MANNEQUIN skeleton: the crowd skeleton has its own
            # weapon_r, oriented differently (the civilians' rifles came out reversed) — every other body: the grip transferred
            # through the shared aim pose
            turn = unreal.Transform(rotation=unreal.Rotator(roll=0, pitch=0, yaw=-90))
            if not body and leader.does_socket_exist("weapon_r"): sock, off = "weapon_r", turn
            else: sock, off = "hand_r", turn * self.weapon_at_hand(leader, body)
            setattr(state, "weapon_socket", sock); setattr(state, "weapon_offset", off)
        state.apply_weapon()

    # ---- one person ------------------------------------------------------------------------------------------------------------
    def make(self, body, loc, facing=0.0, label=None, state=None, folder=None):
        """Spawns a state-driven person at `loc` (feet), its mesh facing `facing` (yaw of +Y), with `state`:
        {"base", "upper", "action", "actionTime", "yaw", "pitch", "weapon", "timeOffset"} (clip NAMES, regex). → (actor, state, leader),
        or None when a clip is missing (the caller reports what).
        ⚠ the STATE is written by plain attribute (setattr), never set_editor_property: that one sends the editor's "property edited"
        notification, which REBUILDS a Blueprint person's components (measured: the body lost its anim class, the weapon doubled)."""
        st = state or {}
        clips = {}
        for k in ("base", "upper", "action"):
            if st.get(k):
                clips[k] = self.resolve(st[k], body)[1]
                if clips[k] is None: return None
        x, y, z = loc
        if body:   # a DRESSED person: a Blueprint (body + outfit parts on a leader pose)
            cls = unreal.EditorAssetLibrary.load_blueprint_class(body_of(body)[0])
            yaw_off, dz = self.body_offsets(cls)
            p = spawn(cls, (x, y, z - dz), (0, facing - yaw_off, 0), label, folder)   # placed once: never moved / renamed after
            leader = body_component(p)
            if leader is None: raise RuntimeError("no leader body component on " + body)
            hold_still(p)
            for c in p.get_components_by_class(unreal.SceneComponent):
                if c.get_name() in BODY_HIDE.get(body, []): c.set_visibility(False, True); c.set_hidden_in_game(True, True)
        else:
            p = spawn(unreal.SkeletalMeshActor, (x, y, z), (0, facing, 0), label, folder)
            leader = p.skeletal_mesh_component
            leader.set_skeletal_mesh_asset(self.manny)
        leader.set_animation_mode(unreal.AnimationMode.ANIMATION_BLUEPRINT)
        leader.set_anim_instance_class(unreal.HrotPersonAnimInstance.static_class())
        leader.set_update_animation_in_editor(True)
        leader.set_editor_property("visibility_based_anim_tick_option", unreal.VisibilityBasedAnimTickOption.ALWAYS_TICK_POSE_AND_REFRESH_BONES)
        ps = unreal.HrotTerrainLibrary.add_person_state(p)   # the STATE on the actor (persists)
        if ps is None: raise RuntimeError("cannot add HrotPersonState to " + str(label))
        setattr(ps, "base_anim", clips.get("base"))
        if clips.get("upper"): setattr(ps, "upper_anim", clips["upper"])
        if clips.get("action"):
            setattr(ps, "action_anim", clips["action"])
            setattr(ps, "action_time", float(st.get("actionTime", 0.35 * clips["action"].get_play_length())))
        setattr(ps, "aim_yaw", float(st.get("yaw", 0))); setattr(ps, "aim_pitch", float(st.get("pitch", 0)))
        setattr(ps, "time_offset", float(st.get("timeOffset", 0.3)))
        if st.get("weapon"): self.set_weapon(ps, leader, body, st["weapon"])
        return p, ps, leader
