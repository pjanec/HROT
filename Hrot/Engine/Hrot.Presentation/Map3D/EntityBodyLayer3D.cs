using System.Globalization;
using System.Numerics;
using Fdp.Toolkit.Combat.Components;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Map.Definitions.Tkb;
using Hrot.MuscleCharacter.Animation.Components;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1033 S1 — the entities in the map's 3-D mode (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.5, M7): every entity with a position
/// and a TKB type is drawn as its family's shape kit (<see cref="ShapeKits"/>), a person as the block figure in its LOGICAL stance
/// (<see cref="LogicalStance"/>), sized from the TKB and coloured by its <see cref="VisualData.ColorHex"/>. Units draw nothing —
/// their members draw themselves. Nothing in 2-D.
/// </summary>
public sealed class EntityBodyLayer3D : IMapLayer, IDisposable
{
    private static readonly Color Dark = new(46, 46, 42, 255);
    private static readonly Color Glass = new(70, 92, 112, 255);
    private static readonly Color Metal = new(84, 86, 88, 255);

    private readonly Func<EntityRepository?> _world;
    private readonly Func<ITkbDatabase?> _tkb;
    private readonly Dictionary<long, TypeLook> _looks = new();
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;
    private readonly Func<EntityRepository, Entity, ArticulationPose>? _poseOf;
    private Mesh _cube, _cylinder, _cone;
    private bool _meshes;

    /// <summary>What a TKB type looks like — resolved once per type.</summary>
    /// <para>⭐ CE-1087 — a weapon system's <paramref name="Weapon"/> kind picks its kit.</para>
    /// <para>⭐ CE-1099 (§6q) — <paramref name="TypeColoured"/>: the TYPE names a colour (else <paramref name="Colour"/> is the family
    /// default); <paramref name="Child"/>: a person shorter than <see cref="ChildHeight"/>.</para>
    public readonly record struct TypeLook(VisualFamily Family, Vector3 Size, Color Colour, bool Soldier,
                                           BodyGeometryDto? Geometry = null, WeaponKind Weapon = WeaponKind.Hmg,
                                           bool TypeColoured = false, bool Child = false);

    /// <summary>⭐ CE-1099 (§6q O-3) — a person shorter than this (m) is a child: drawn with a larger head.</summary>
    public const float ChildHeight = 1.5f;

    /// <summary>⭐ CE-1099 — how much larger a child's head is than the figure's scaled one.</summary>
    public const float ChildHead = 1.3f;

    /// <param name="poseOf">⭐ CE-1033 §3.11 — the turret azimuth and gun elevation of an entity with an articulated kit. ⭐ WL-1 — in
    /// production it is <see cref="TurretPoseSource.PoseOf"/> (the turret part's <c>TurretPose</c>, DESIGN_Weapon_Layers W-9), plugged in
    /// at ONE place, <c>MapInteractionPack.AttachMapLayers</c>. Null = every turret faces forward.</param>
    public EntityBodyLayer3D(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb,
                             Func<EntityRepository, Entity, ArticulationPose>? poseOf = null)
    {
        _poseOf = poseOf;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _tkb = tkb ?? throw new ArgumentNullException(nameof(tkb));
    }

    public string Name => "Entities (3-D)";
    public int LayerBitIndex => -1;
    public bool Has3D => true;

    /// <summary>Bodies drawn in the last 3-D frame, and parts — the frame-cost counters (§7).</summary>
    public int BodiesDrawn { get; private set; }
    public int PartsDrawn { get; private set; }
    /// <summary>⭐ CE-1068 — door leaves drawn by the last frame (a destroyed door is not one).</summary>
    public int DoorsDrawn { get; private set; }

    public void Update(float dt) { }
    public void Draw(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    public void Draw3D(RenderContext ctx)
    {
        BodiesDrawn = PartsDrawn = DoorsDrawn = JumpsDrawn = CarriedDrawn = GunnersDrawn = ShoulderedDrawn = FaceDetailsDrawn = StainsDrawn = DetailsDrawn = 0;
        _carried.Clear();
        _shouldering.Clear();
        _figureFrames.Clear();
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<TkbIdentity>() || !world.IsComponentTypeRegistered<SimTransform>()) return;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            var q = world.Query().With<SimTransform>().With<TkbIdentity>().WithLifecycle(EntityLifecycle.All);
            // ⭐ LR-3 (DESIGN_Visual_Effects.md §8.5, R-271) — a replicated ROUND is never a body: ProjectileLayer draws it
            _query = (world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>()
                ? q.Without<Fdp.Toolkit.Replication.Components.LightweightRound>() : q).Build();
        }
        EnsureMeshes();
        var shader = LitShader.Shared;
        var tkb = _tkb();

        // ⭐ CE-1033 S4 — the moving parts run on SIMULATION time (R-143); a host with no sim clock falls back to the frame time.
        _wallFallback += ctx.DeltaTime > 0f && float.IsFinite(ctx.DeltaTime) ? ctx.DeltaTime : 0f;
        double simTime = world.HasSingletonUnmanaged<GlobalTime>() ? world.GetSingletonUnmanaged<GlobalTime>().TotalTime : _wallFallback;
        _drawn.Clear();
        bool hasSelection = world.IsComponentTypeRegistered<Hrot.IG.Components.SelectionState>();
        bool hasNet = world.IsComponentTypeRegistered<NetworkIdentity>();
        SelectionBoxesDrawn = 0;
        _tops.Clear();
        foreach (var e in _query!)
        {
            // ⭐ CE-1068 (§6k) — a DOOR is its leaf, hinged in its doorway, never a shape kit (it used to be an unknown box)
            if (DoorLeaf3D.IsDoor(world, e)) { DrawDoor(world, e, shader, simTime, hasSelection); continue; }
            long type = world.GetComponentRO<TkbIdentity>(e).TkbType;
            var look = LookOf(type, tkb);
            if (look.Family == VisualFamily.Unit) continue;
            // ⭐ CE-1058 — a passenger inside an ENCLOSED vehicle is not seen (the engine's one rule, R-271); an exposed rider is drawn.
            if (Fdp.Toolkit.Perception.PhysicalPresence.IsEnclosedPassenger(world, e)) continue;

            var size = SizeOf(world, e, look);
            var colour = ColourOf(world, e, look.Colour);
            // ⭐ CE-1085 (§6n N-3) — a weapon a man CARRIES is drawn on his back, after every figure is placed (pass 2)
            if (look.Family == VisualFamily.WeaponSystem && CarrierOf(world, e) is { } bearer)
            {
                _carried.Add((e, bearer, look, size, colour));
                _drawn.Add(e);
                continue;
            }
            // ⭐ W2 (docs/DESIGN_Visual_Effects.md §7 VE-N/VE-O) — a destroyed VEHICLE is a near-black wreck, every part; the turret
            //   stays where it last pointed (its pose is no longer driven). A dead person is drawn by the Dead figure instead (VE-S).
            bool wreck = look.Family != VisualFamily.Person && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e);
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
            var body = Matrix4x4.CreateFromQuaternion(tf.Rotation) * Matrix4x4.CreateTranslation(tf.Position);
            var carriage = look.Family == VisualFamily.Person ? Carriage.Of(world, e, tkb) : FigureCarriage.Free;   // ⭐ CE-1081..1084 (§6m)
            // ⭐ CE-1033 S4 — wheels roll, limbs swing, a rotor turns while flying (MotionTracker); a parked helicopter's rotor stands.
            bool rotorTurning = !wreck && (look.Family == VisualFamily.Drone   // ⭐ CE-1086 — a drone's rotors run while it lives
                                           || look.Family == VisualFamily.Helicopter && IsAirborne(world, tf.Position, look.Geometry));
            var motion = _motion.Advance(e, tf.Position, tf.Rotation, look.Family, size, simTime, rotorTurning);

            KitPart[] parts;
            KitPivots pivots = default;
            var pose = ArticulationPose.Neutral;
            if (look.Family == VisualFamily.Person && HeldFigure(world, e, look.Soldier, carriage, size, simTime, ref body) is { } held)
            {
                parts = held;                       // ⭐ CE-1081..1083 (§6m) — carried, dragged, seated, down: no gait of his own
                motion = MotionPose.Still with { HipZ = 0.5f };
            }
            else if (look.Family == VisualFamily.Person)
            {
                // ⭐ WL-6c — the posture first (how AIMED he is), then the gait and the upper body from it
                (parts, float aimed, float crouchRun) = FigurePose(world, e, look.Soldier, simTime, motion);
                motion = FigureMotion(world, e, motion, look.Soldier, tf, size, simTime, aimed, crouchRun);
                (parts, motion) = Burdened(world, e, carriage, look.Soldier, parts, motion);
                // ⭐ CE-1090 (§6n N-4) — just fired a launcher and still aimed: the tube on his shoulder instead of the rifle
                if (look.Soldier && PostureOf(world, e) == WeaponPostureKind.Aimed && Shouldering(world, e, carriage, tkb, simTime))
                {
                    parts = BlockFigure.Shouldered(parts);
                    _shouldering.Add(e);
                }
                // ⭐ CE-1084 (§6m C-4) — just off a raised seat: drawn falling from it, landing crouched
                var (lift, jumping) = _drops.Free(e, tf.Position.Z + BlockFigure.HipFraction(StanceId.Standing) * size.Z, simTime);
                if (jumping)
                {
                    parts = BlockFigure.Parts(StanceId.Crouched, look.Soldier, PostureOf(world, e), PostureOf(world, e), 1f);
                    motion = MotionPose.Still with { HipZ = BlockFigure.HipFraction(StanceId.Crouched) };
                    body *= Matrix4x4.CreateTranslation(0f, 0f, lift);
                    JumpsDrawn++;
                }
            }
            else if (look.Family == VisualFamily.WeaponSystem)
            {
                // ⭐ CE-1087 (§6n N-1) — stowed / deployed from the replicated deploy state, blended while it is set up or packed; only a
                //   deployed weapon traverses and elevates (a laid tube or howitzer at its elevation, N-5)
                float deployed = DeploymentOf(world, e, simTime);
                var kit = WeaponKits.Blend(look.Weapon, deployed);
                (parts, pivots) = (kit.Parts, kit.Pivots);
                if (deployed >= 1f) pose = LaidPose(world, e, WeaponKits.DefaultElevation(look.Weapon), simTime);
            }
            else
            {
                var kit = ShapeKits.For(look.Family);
                (parts, pivots) = PlacedKit(kit, type, tkb, size);   // ⭐ E4b — the turret and barrel from the TKB (VE-L)
                if (_poseOf != null && kit.IsArticulated) pose = _poseOf(world, e);
                if (look.Family == VisualFamily.SpArtillery) pose = LaidPose(world, e, SpGunElevation, simTime);   // ⭐ CE-1089 (N-5)
                if (look.Family == VisualFamily.AttackJet)   // ⭐ CE-1092 (N-8) — the pylons, and a bomb on each for every round left
                    parts = [.. parts, .. ShapeKits.AttackJetPylons, .. ShapeKits.AttackJetBombs.AsSpan(0, BombsLeft(world, e, tkb))];
            }
            _drawn.Add(e);
            if (look.Family == VisualFamily.Person) _figureFrames[e] = (body, size.Z);   // ⭐ CE-1085 — where a carried weapon hangs
            if (_shouldering.Contains(e)) ShoulderedDrawn++;
            if (carriage.Kind == CarriageKind.Manning) GunnersDrawn++;
            // ⭐ CE-1041 — a type with Body.Geometry: the kit's box sits where the TKB puts it relative to the reference point
            //   (an aircraft's CG), and the gear is drawn AT the TKB's contact points instead of the kit's generic gear.
            var geometry = look.Geometry;
            var kitToBody = geometry == null
                ? Matrix4x4.Identity
                : Matrix4x4.CreateTranslation(geometry.BodyCentreX, geometry.BodyCentreY, geometry.BodyCentreZ - size.Z / 2f);
            int stains = look.Family == VisualFamily.Person ? Bloodied(world, e, hasNet) : 0;   // ⭐ CE-1098 (§6p)
            // ⭐ CE-1099 (§6q) — what he wears: each part's colour and the outfit's details, from his seed
            Outfit? outfit = null;
            var headFace = FaceOn.Front;
            if (look.Family == VisualFamily.Person)
            {
                long seed = SeedOf(world, e, hasNet);
                outfit = Outfits.For(seed, type, look.Soldier, look.Child, HasOwnColour(world, e, look) ? colour : null, SideOf(world, e));
                Outfits.Details(outfit.Value, seed, _details);
                if (parts.Length > BlockFigure.HeadIndex) headFace = parts[BlockFigure.HeadIndex].Face;
            }
            int details = outfit != null ? _details.Count : 0;
            for (int pi = 0; pi < parts.Length; pi++)
            {
                var part = parts[pi];
                if (look.Child && pi == BlockFigure.HeadIndex)   // ⭐ CE-1099 (O-3) — a child's head is larger, sitting on his shoulders
                    part = part with
                    {
                        Extent = part.Extent * ChildHead,
                        Centre = part.Face == FaceOn.Front ? part.Centre + new Vector3(0f, 0f, part.Extent.Z * (ChildHead - 1f) / 2f) : part.Centre,
                    };
                if (geometry != null && part.Role == PartRole.Gear) continue;
                // ⭐ CE-1070 — a figure's rifle (its weapon part): remember where its muzzle end was drawn, for the muzzle flash.
                //   ⭐ CE-1090 — a shouldered launcher is that part too; its tip is also the CARRIED weapon's muzzle (its shot is the weapon's)
                if (look.Family == VisualFamily.Person && look.Soldier && pi == BlockFigure.WeaponIndex)
                {
                    var tip = WeaponTip(part, size, pivots, pose, motion, kitToBody * body);
                    _weaponTips[e] = tip;
                    if (_shouldering.Contains(e) && !carriage.Load.IsNull) _weaponTips[carriage.Load] = tip;
                }
                // ⭐ CE-1073 (§6l) — a figure's head: remember its eyes (in the body's frame) for the first-person camera, and do not
                //   draw the head of the unit seen FROM it (the camera sits inside it)
                if (look.Family == VisualFamily.Person && pi == BlockFigure.HeadIndex)
                {
                    _eyes[e] = EyeLocal(part, size, pivots, pose, motion, kitToBody);
                    if (e == FirstPerson) continue;
                }
                var placed = part.BodyTransform(size, pivots, pose, motion) * kitToBody * body;
                var model = HrotToRaylib.ModelFromHrot(placed);
                var mesh = part.Shape switch { PartShape.Cylinder => _cylinder, PartShape.Cone => _cone, _ => _cube };
                var tint = outfit is { } dress && Outfits.ColourOfPart(dress, pi) is { } worn ? worn : PartColour(part.Colour, colour, wreck);   // ⭐ CE-1099
                Raylib.DrawMesh(mesh, shader.Tinted(tint), model);
                PartsDrawn++;
                if (part.Face != FaceOn.None) DrawFace(part.Face, placed, look.Soldier, colour, shader, outfit);   // ⭐ CE-1097 (§6o)
                for (int k = 0; k < details; k++)   // ⭐ CE-1099 (§6q) — the outfit's details on this part
                    if (_details[k].Part == pi)
                    {
                        var d = _details[k];
                        var local = Matrix4x4.CreateScale(d.Extent) * Matrix4x4.CreateTranslation(d.Centre);
                        if (d.Turned) local *= BlockFigure.FaceFrame(headFace);
                        Raylib.DrawMesh(_cube, shader.Tinted(d.Colour), HrotToRaylib.ModelFromHrot(local * placed));
                        DetailsDrawn++;
                    }
                for (int k = 0; k < stains; k++)   // ⭐ CE-1098 (§6p) — the blood spots on this part
                    if (_stains[k].Part == pi)
                    {
                        Raylib.DrawMesh(_cube, shader.Tinted(_stains[k].Fresh ? BloodFresh : BloodDark),
                                        HrotToRaylib.ModelFromHrot(BlockFigure.StainDetail(_stains[k], placed)));
                        StainsDrawn++;
                    }
            }
            if (geometry != null) DrawGear(world, geometry, tf.Position, body, shader, wreck);
            // ⭐ CE-1033 S3 — where this body's labels sit: the top of the drawn box above the reference point.
            if (hasNet && world.HasComponent<NetworkIdentity>(e) && world.GetComponentRO<NetworkIdentity>(e).Value is long net && net != 0)
                _tops[net] = geometry != null ? geometry.BodyCentreZ + size.Z / 2f : size.Z;
            if (hasSelection && world.HasComponent<Hrot.IG.Components.SelectionState>(e))
            {
                ref readonly var sel = ref world.GetComponentRO<Hrot.IG.Components.SelectionState>(e);
                if (sel.IsSelected && TryGetBox(world, e, tkb, out var c, out var r, out var h))
                {
                    DrawWireBox(c, r, h * 1.06f, sel.IsPrimarySelection ? PrimarySelection : OtherSelection);
                    SelectionBoxesDrawn++;
                }
            }
            BodiesDrawn++;
        }
        DrawCarried(world, shader, wreckOf: e => Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e));   // ⭐ CE-1085 — pass 2
        UpdateLaid(world, simTime);
        _motion.EndFrame(_drawn);
        _fall.EndFrame(_drawn);
        _down.EndFrame(_drawn);
        _drops.EndFrame(_drawn);
        _doors.EndFrame(_drawn);
        _tipsGone.Clear();
        foreach (var k in _weaponTips.Keys) if (!_drawn.Contains(k)) _tipsGone.Add(k);
        foreach (var k in _tipsGone) _weaponTips.Remove(k);
        _tipsGone.Clear();
        foreach (var k in _eyes.Keys) if (!_drawn.Contains(k)) _tipsGone.Add(k);
        foreach (var k in _tipsGone) _eyes.Remove(k);
        _postures.EndFrame(_drawn);
    }

    /// <summary>⭐ CE-1033 S4 — the figure's parts: the logical stance (the shared rule), BLENDED from the stance it is leaving while
    /// the muscle reports a transition in progress (<see cref="StanceStatus"/> — replicated, so every host shows the same blend).</summary>
    /// <summary>⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13) — the weapon brain's posture of <paramref name="e"/>'s weapon (replicated,
    /// R-272); at ease where the world or the unit has none.</summary>
    public static WeaponPostureKind PostureOf(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<WeaponPosture>() && world.HasComponent<WeaponPosture>(e)
            ? world.GetComponentRO<WeaponPosture>(e).Kind : WeaponPostureKind.AtEase;

    /// <summary>⭐ WL-6c — the athletic stance's forward lean (about 10°), how much shorter the glide's steps are than a walk's, and
    /// how far a shin bends back as its leg swings through (radians, at a full stride).</summary>
    public const float AthleticLean = 0.17f, GlideStepCut = 0.45f, KneeLift = 0.55f;

    /// <summary>The most an aimed upper body turns toward its target, and pitches (radians).</summary>
    /// <remarks>⭐ <c>CE-3195</c> (W-14a) — the twist is the person body's own limit, <see cref="CarKinem.Controllers.HumanGait.MaxAimTwist"/>:
    /// the fire arc reads the same value, so the drawn rifle points where the round can go.</remarks>
    public const float MaxAimTwist = global::CarKinem.Controllers.HumanGait.MaxAimTwist, MaxAimPitch = 35f * MathF.PI / 180f;

    /// <summary>
    /// ⭐ WL-6 (W-13e) — the figure's upper body: it leans from the hip at a stance's height; an AIMED soldier turns and pitches it
    /// toward his target or aim point (a soldier's body does not turn to face what he shoots at — nothing writes his heading for it),
    /// within <see cref="MaxAimTwist"/> / <see cref="MaxAimPitch"/>. Prone and dead figures do not lean or turn.
    /// </summary>
    /// ⭐ WL-6b — the turn and pitch EASE toward the aim (<see cref="PostureBlend.Aim"/>), and back to 0 when it is no longer aimed.
    /// ⭐ WL-6c — the GAIT: each shin bends back as its leg swings through (both feet lift); an AIMED soldier (<paramref name="aimed"/>
    /// 0..1, the posture blend) stands in the athletic stance — hips <see cref="BlockFigure.AthleticDrop"/> lower, the upper body leaning
    /// <see cref="AthleticLean"/> forward — and on the move GLIDES: shorter steps, knees always bent.
    /// ⭐ CE-1064 — a crouched figure on the move (<paramref name="crouchRun"/> 0..1, its run weight) carries its hips at the crouched
    /// run's height and leans forward by <see cref="BlockFigure.CrouchRunLean"/> — less as he aims.
    private MotionPose FigureMotion(EntityRepository world, Entity e, MotionPose motion, bool soldier, in SimTransform tf, Vector3 size, double simTime,
                                    float aimed, float crouchRun = 0f)
    {
        var stance = LogicalStance.Of(world, e);
        if (stance == StanceId.Prone || Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e))
        {
            if (soldier) _postures.Aim(e, 0f, 0f, simTime);
            return motion with { Lean = 0f, Twist = 0f, KneeFlexL = 0f, KneeFlexR = 0f };
        }
        float swingThrough = MathF.Cos(motion.GaitPhase);   // > 0: the left leg is coming forward, < 0: the right one
        float lift = motion.Stride * KneeLift;
        motion = motion with
        {
            HipZ = BlockFigure.HipFraction(stance),
            KneeFlexL = lift * MathF.Max(0f, swingThrough),
            KneeFlexR = lift * MathF.Max(0f, -swingThrough),
        };
        if (stance == StanceId.Crouched && crouchRun > 0f)
            motion = motion with
            {
                HipZ = motion.HipZ + (BlockFigure.CrouchRunHipFraction - motion.HipZ) * crouchRun,
                Lean = motion.Lean + BlockFigure.CrouchRunLean * crouchRun * (1f - AimedNow(soldier ? PostureOf(world, e) : WeaponPostureKind.AtEase)),
                Swing = motion.Swing * (1f - BlockFigure.CrouchRunStepCut * crouchRun),
            };
        if (!soldier) return motion;
        if (stance == StanceId.Standing && aimed > 0f)
            motion = motion with
            {
                HipZ = (0.90f - BlockFigure.AthleticDrop * aimed) / 1.8f,
                Lean = motion.Lean + AthleticLean * aimed,
                Swing = motion.Swing * (1f - GlideStepCut * aimed),
            };
        if (!TryAim(world, e, tf, size, stance, out float twist, out float pitch)) twist = pitch = 0f;
        var (t, p) = _postures.Aim(e, twist, pitch, simTime);
        return motion with { Twist = t, Lean = motion.Lean - p };
    }

    /// <summary>The aim of an AIMED unit relative to its body: turn (counter-clockwise) and pitch (up), both clamped. False when it is
    /// not aimed or aims at nothing it can place.</summary>
    public static bool TryAim(EntityRepository world, Entity e, in SimTransform tf, Vector3 size, StanceId stance, out float twist, out float pitch)
    {
        twist = pitch = 0f;
        // ⭐ CE-3195 — the ONE reading of what the posture aims at (the gait and the Stride motor read it too)
        if (!Fdp.Toolkit.Combat.WeaponPostureOf.TryAimPoint(world, e, out var at)) return false;
        float shoulder = (stance == StanceId.Crouched ? 0.98f : 1.41f - BlockFigure.AthleticDrop) / 1.8f * size.Z;   // aimed: the athletic stance
        var toward = at - (tf.Position + new Vector3(0f, 0f, shoulder));
        var local = Vector3.Transform(toward, Quaternion.Inverse(tf.Rotation));
        float horizontal = MathF.Sqrt(local.X * local.X + local.Y * local.Y);
        if (horizontal < 0.1f) return false;
        twist = Math.Clamp(MathF.Atan2(local.Y, local.X), -MaxAimTwist, MaxAimTwist);
        pitch = Math.Clamp(MathF.Atan2(local.Z, horizontal), -MaxAimPitch, MaxAimPitch);
        return true;
    }

    private static float AimedNow(WeaponPostureKind posture) => posture == WeaponPostureKind.Aimed ? 1f : 0f;

    /// <returns>The parts, how AIMED he is standing (0..1, the posture blend) — the athletic stance and the glide follow it — and
    /// ⭐ CE-1064 how much a CROUCHED figure is running (0..1, its stride).</returns>
    private (KitPart[] Parts, float Aimed, float CrouchRun) FigurePose(EntityRepository world, Entity e, bool soldier, double simTime, in MotionPose motion)
    {
        float stride = motion.Stride;
        var target = LogicalStance.Of(world, e);
        var posture = soldier ? PostureOf(world, e) : WeaponPostureKind.AtEase;   // ⭐ WL-6 — the rifle in the weapon brain's posture
        // ⭐ WL-6b — moved there over PostureBlend.Seconds of sim time, not snapped
        var (postureFrom, postureTo, postureT) = soldier ? _postures.Posture(e, posture, simTime) : (posture, posture, 1f);
        float eased = postureT * postureT * (3f - 2f * postureT);
        float aimed = (postureTo == WeaponPostureKind.Aimed ? eased : 0f) + (postureFrom == WeaponPostureKind.Aimed ? 1f - eased : 0f);
        if (postureFrom == postureTo) aimed = postureTo == WeaponPostureKind.Aimed ? 1f : 0f;
        // ⭐ W4 (docs/DESIGN_Visual_Effects.md §7 VE-S) — a dead man lies on his back, fallen into it over 0.6 s of sim time from the
        //   stance he died in; a body first seen already dead just lies there (DeathFall).
        if (Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e))
        {
            var (t, from) = _fall.Dead(e, simTime);
            return (BlockFigure.Fall(BlockFigure.Parts(from, soldier, posture), t, soldier), 0f, 0f);
        }
        _fall.Alive(e, target, simTime);
        if (world.IsComponentTypeRegistered<StanceStatus>() && world.HasComponent<StanceStatus>(e))
        {
            ref readonly var status = ref world.GetComponentRO<StanceStatus>(e);
            if (status.Phase == StanceTransitionPhase.Transitioning && status.CurrentStance != target)
                return (BlockFigure.Blend(status.CurrentStance, target, status.TransitionProgress, soldier, postureFrom, postureTo, postureT), 0f, 0f);
        }
        var parts = BlockFigure.Parts(target, soldier, postureFrom, postureTo, postureT);
        // ⭐ WL-6d — prone and moving: the crawl (never aimed), blended in by how much he moves
        if (target == StanceId.Prone && motion.Moving > 0f) return (BlockFigure.Crawl(parts, soldier, motion.CrawlPhase, motion.Moving), 0f, 0f);
        // ⭐ CE-1064 — crouched and on the move: the crouched run (R-265), blended in by his stride; still, he kneels
        if (target == StanceId.Crouched && stride > 0f)
            return (BlockFigure.CrouchedMoving(parts, soldier, postureFrom, postureTo, postureT, stride), 0f, stride);
        // ⭐ WL-6c — aimed and on the move: the combat glide (feet under the lowered hips, knees bent)
        if (soldier && target == StanceId.Standing && aimed > 0f && stride > 0f) parts = BlockFigure.Glide(parts, aimed * stride);
        return (parts, target == StanceId.Standing ? aimed : 0f, 0f);
    }

    private readonly MotionTracker _motion = new();
    private readonly DeathFall _fall = new();
    private readonly DeathFall _down = new();         // ⭐ CE-1082 — the incapacitated collapse (the same blend as a death fall)
    private readonly DismountDrop _drops = new();     // ⭐ CE-1084 — the jump down from a raised seat

    /// <summary>⭐ CE-1084 — figures drawn mid-jump (or just landed) by the last frame (rails).</summary>
    public int JumpsDrawn { get; private set; }

    /// <summary>⭐ CE-1084 — the jumps (the rails' seam).</summary>
    public DismountDrop Drops => _drops;

    /// <summary>
    /// ⭐⭐ CE-1081 … CE-1083 (docs/DESIGN_Map_3D_Mode.md §6m) — the figure of a man who is HELD, and where it goes: CARRIED across his
    /// carrier's shoulders (<see cref="Carriage.OnShoulders"/>), DRAGGED on his back at his own (the sim's, behind the carrier) place,
    /// SEATED at his seat's place facing its way (<see cref="Carriage.InSeat"/> — and the jump tracker told where his hips are), DOWN on
    /// his back (fallen into it over <see cref="DeathFall.Seconds"/>). Null for a man on his own feet (the stance path draws him).
    /// <paramref name="body"/> becomes the figure's frame in the world.
    /// </summary>
    private KitPart[]? HeldFigure(EntityRepository world, Entity e, bool soldier, in FigureCarriage carriage, Vector3 size, double simTime,
                                  ref Matrix4x4 body)
    {
        if (Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e)) return null;   // the dead fall as before (VE-S)
        switch (carriage.Kind)
        {
            case CarriageKind.Carried when world.HasComponent<SimTransform>(carriage.Holder):
            {
                ref readonly var c = ref world.GetComponentRO<SimTransform>(carriage.Holder);
                body = Carriage.OnShoulders(Matrix4x4.CreateFromQuaternion(c.Rotation) * Matrix4x4.CreateTranslation(c.Position), size.Z);
                _fall.Alive(e, StanceId.Prone, simTime);   // dying up there falls from a lying figure, not from a standing one
                return BlockFigure.Draped(soldier);
            }
            case CarriageKind.Dragged:
                _fall.Alive(e, StanceId.Prone, simTime);
                return BlockFigure.Towed(soldier);
            case CarriageKind.Seated when carriage.Seat is { } seat && world.HasComponent<SimTransform>(carriage.Holder):
            {
                ref readonly var v = ref world.GetComponentRO<SimTransform>(carriage.Holder);
                body = Carriage.InSeat(Matrix4x4.CreateFromQuaternion(v.Rotation) * Matrix4x4.CreateTranslation(v.Position), seat);
                // ⭐ CE-1088 (§6n N-2) — a gunner seat may say he STANDS or KNEELS at its weapon (the place is then his feet)
                bool stands = seat.Pose is Fdp.Toolkit.Tkb.Domain.SeatPose.Stand or Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel;
                float hips = stands ? BlockFigure.HipFraction(seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel ? StanceId.Crouched : StanceId.Standing) * size.Z : 0f;
                _drops.Riding(e, body.Translation.Z + hips, simTime);
                _fall.Alive(e, StanceId.Crouched, simTime);
                if (stands) return BlockFigure.AtWeapon(seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel, soldier);
                return BlockFigure.Seated(seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut, soldier);
            }
            case CarriageKind.Manning when world.HasComponent<SimTransform>(carriage.Holder)
                                          && GunnerFrame(world, carriage, size, simTime, out var frame, out bool kneel):
                body = frame;
                _fall.Alive(e, kneel ? StanceId.Crouched : StanceId.Standing, simTime);
                return BlockFigure.AtWeapon(kneel, soldier);
            case CarriageKind.Downed:
            {
                var (t, from) = _down.Dead(e, simTime);
                _fall.Alive(e, StanceId.Prone, simTime);
                return BlockFigure.Collapse(BlockFigure.Parts(from, soldier, PostureOf(world, e)), t, soldier);
            }
            default:
                _down.Alive(e, LogicalStance.Of(world, e), simTime);
                return null;
        }
    }

    /// <summary>
    /// ⭐ CE-1081 / CE-1082 (§6m C-1, C-2) — a man on his own feet who CARRIES or DRAGS someone (his arms on the load, his body leaning under
    /// it) or who is WOUNDED (he limps: his right leg stiff, shorter steps, his upper body bent — standing still too). Standing only: a
    /// crouched or prone man keeps his stance's figure.
    /// </summary>
    public static (KitPart[] Parts, MotionPose Motion) Burdened(EntityRepository world, Entity e, in FigureCarriage carriage, bool soldier,
                                                                KitPart[] parts, MotionPose motion)
    {
        if (LogicalStance.Of(world, e) != StanceId.Standing || Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e)) return (parts, motion);
        if (carriage.Kind is CarriageKind.Carrying or CarriageKind.Dragging)
        {
            bool drag = carriage.Kind == CarriageKind.Dragging;
            parts = BlockFigure.CarrierArms(parts, drag, soldier);
            motion = motion with { Lean = motion.Lean + (drag ? BlockFigure.DragLean : BlockFigure.CarryLean) };
        }
        if (carriage.Wounded)
            motion = motion with
            {
                Lean = motion.Lean + BlockFigure.WoundedLean,
                KneeFlexR = 0f,
                Swing = motion.Swing * (1f - BlockFigure.WoundedStepCut),
            };
        return (parts, motion);
    }
    // ── ⭐ CE-1085 … CE-1090 (docs/DESIGN_Map_3D_Mode.md §6n) — weapon systems, their gunners, carried weapons, launchers in hands ──

    private readonly List<(Entity Weapon, Entity Bearer, TypeLook Look, Vector3 Size, Color Colour)> _carried = new();
    private readonly HashSet<Entity> _shouldering = new();
    private readonly Dictionary<Entity, (Matrix4x4 Frame, float Height)> _figureFrames = new();
    private readonly Dictionary<Entity, (float Elevation, double At)> _laid = new();
    private readonly HashSet<Entity> _roundsSeen = new();
    private readonly HashSet<Entity> _roundsNow = new();
    private EntityRepository? _roundsWorld;
    private EntityQuery? _rounds;

    /// <summary>⭐ CE-1085 — weapons drawn on a carrier's back by the last frame (rails).</summary>
    public int CarriedDrawn { get; private set; }
    /// <summary>⭐ CE-1088 — men drawn at their weapons by the last frame (rails).</summary>
    public int GunnersDrawn { get; private set; }
    /// <summary>⭐ CE-1090 — soldiers drawn with a launcher on the shoulder by the last frame (rails).</summary>
    public int ShoulderedDrawn { get; private set; }

    /// <summary>⭐ CE-1097 (docs/DESIGN_Map_3D_Mode.md §6o) — the face details (eyes, nose, mouth, helmet or hair) drawn this frame.</summary>
    public int FaceDetailsDrawn { get; private set; }

    /// <summary>⭐ CE-1098 (docs/DESIGN_Map_3D_Mode.md §6p) — the blood spots drawn this frame.</summary>
    public int StainsDrawn { get; private set; }

    /// <summary>⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q) — the outfit details (tie, skirt, long hair, camouflage) drawn this frame.</summary>
    public int DetailsDrawn { get; private set; }

    /// <summary>
    /// ⭐ CE-1092 (N-8) — how many bombs an attack plane still carries (at most its pylons): its bomb mount's rounds from the replicated
    /// <c>WeaponAmmo</c> mirror (CE-1079) — the full load where no mirror exists yet (a scenario being edited).
    /// </summary>
    public static int BombsLeft(EntityRepository world, Entity plane, ITkbDatabase? tkb)
    {
        int pylons = ShapeKits.AttackJetBombs.Length;
        if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, plane, out var template)
            || template.GetDescriptor<WeaponSuiteDto>() is not { } suite) return pylons;
        for (int i = 0; i < suite.Mounts.Count; i++)
        {
            if (suite.Mounts[i].Category != WeaponCategory.Bomb) continue;
            int rounds = suite.Mounts[i].InitialAmmunition;
            if (i < WeaponAmmo.MaxMounts && world.IsComponentTypeRegistered<WeaponAmmo>() && world.HasComponent<WeaponAmmo>(plane)
                && world.GetComponentRO<WeaponAmmo>(plane) is { } ammo && i < ammo.Count)
                rounds = ammo.AmmoOf(i);
            return Math.Clamp(rounds, 0, pylons);
        }
        return 0;
    }

    /// <summary>⭐ CE-1089 (N-5) — a self-propelled howitzer's barrel when nothing says better (radians, 25°).</summary>
    public static readonly float SpGunElevation = WeaponKits.DefaultElevation(WeaponKind.TowedGun);

    /// <summary>⭐ CE-1089 (N-5) — how long a gun keeps the elevation its last round left at (s).</summary>
    public const double LaidSeconds = 60.0;

    /// <summary>The man carrying <paramref name="weapon"/> (in his carry station), or null when it is not carried by a person.</summary>
    private static Entity? CarrierOf(EntityRepository world, Entity weapon)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()
            || !world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(weapon)) return null;
        var holder = world.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(weapon).VehicleEntity;
        return world.IsAlive(holder) && world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PersonCarrier>()
               && Fdp.Toolkit.Behavior.Embarkation.IsPersonCarrier(world, holder) ? holder : null;
    }

    /// <summary>⭐ CE-1087 — how far <paramref name="weapon"/> is deployed now (0 stowed … 1 deployed); 1 without a deploy state.</summary>
    public static float DeploymentOf(EntityRepository world, Entity weapon, double now)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemState>() || !world.HasComponent<Fdp.Toolkit.Combat.WeaponSystemState>(weapon))
            return 1f;
        var state = world.GetComponentRO<Fdp.Toolkit.Combat.WeaponSystemState>(weapon);
        float seconds = 0f;
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemParams>() && world.HasComponent<Fdp.Toolkit.Combat.WeaponSystemParams>(weapon))
        {
            var p = world.GetComponentRO<Fdp.Toolkit.Combat.WeaponSystemParams>(weapon);
            seconds = state.Deploy == Fdp.Toolkit.Combat.WeaponDeploy.Packing ? p.PackSeconds : p.DeploySeconds;
        }
        return WeaponKits.Deployment(state.Deploy, state.Until, now, seconds);
    }

    /// <summary>
    /// ⭐ CE-1089 (N-5) — the turret and gun pose of a gun that fires INDIRECT: its own turret pose where it is driven; else level guns
    /// are laid at the elevation of the last round it fired (seen on this node within <see cref="LaidSeconds"/>), else at
    /// <paramref name="fallback"/>. ⏭ Backend's IF-1 replicated elevation will replace the fallback on every host.
    /// </summary>
    private ArticulationPose LaidPose(EntityRepository world, Entity gun, float fallback, double now)
    {
        var pose = _poseOf != null ? _poseOf(world, gun) : ArticulationPose.Neutral;
        if (MathF.Abs(pose.GunElevation) > 1e-3f) return pose;
        float elevation = _laid.TryGetValue(gun, out var laid) && now >= laid.At && now - laid.At <= LaidSeconds ? laid.Elevation : fallback;
        return pose with { GunElevation = elevation };
    }

    /// <summary>⭐ CE-1089 — remembers the elevation each gun's NEWEST round left at (where combat runs, a round knows its shooter).</summary>
    private void UpdateLaid(EntityRepository world, double now)
    {
        if (!world.IsComponentTypeRegistered<BallisticProjectile>() || !world.IsComponentTypeRegistered<SimVelocity>()) return;
        if (!ReferenceEquals(world, _roundsWorld))
        {
            _roundsWorld = world;
            _rounds = world.Query().With<BallisticProjectile>().With<SimVelocity>().Build();
            _roundsSeen.Clear();
        }
        _roundsNow.Clear();
        foreach (var r in _rounds!)
        {
            _roundsNow.Add(r);
            if (!_roundsSeen.Add(r)) continue;
            var shooter = world.GetComponentRO<BallisticProjectile>(r).Shooter;
            var v = world.GetComponentRO<SimVelocity>(r).Linear;
            float flat = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            if (shooter.IsNull || v.LengthSquared() < 1f) continue;
            _laid[shooter] = (MathF.Atan2(v.Z, flat), now);
        }
        _roundsSeen.IntersectWith(_roundsNow);
    }

    /// <summary>⭐ CE-1089 — a gun's laid elevation as its last round left (the rails' seam on a host with no combat).</summary>
    internal void RememberLaid(Entity gun, float elevation, double at) => _laid[gun] = (elevation, at);

    /// <summary>
    /// ⭐ CE-1090 (N-4) — does <paramref name="e"/> hold a launcher on his shoulder now: he fired a launcher mount of his own, or the
    /// launcher he carries fired, within <see cref="WeaponKits.ShoulderSeconds"/>.
    /// </summary>
    private static bool Shouldering(EntityRepository world, Entity e, in FigureCarriage carriage, ITkbDatabase? tkb, double now)
    {
        if (Hrot.UI.Common.Effects.RecentShots.Of(world) is not { } shots) return false;
        if (shots.FiredWithin(e, now, WeaponKits.ShoulderSeconds, out int mount)
            && WeaponKits.IsLauncher(tkb, Fdp.Toolkit.Combat.CombatTkb.MountOf(world, e, mount))) return true;
        return !carriage.Load.IsNull && shots.FiredWithin(carriage.Load, now, WeaponKits.ShoulderSeconds, out _)
               && WeaponKits.TypeOf(world, tkb, carriage.Load).Kind is WeaponKind.Launcher or WeaponKind.Atgm;
    }

    /// <summary>
    /// ⭐ CE-1088 (N-2) — where a man in a weapon's own seat is drawn: at its seat's place when the seat declares one, else at the weapon
    /// kind's gunner spot (scaled to the weapon's size), turned with its traverse about its pivot. False for a weapon nobody mans on foot.
    /// </summary>
    internal bool GunnerFrame(EntityRepository world, in FigureCarriage carriage, Vector3 manSize, double now, out Matrix4x4 frame, out bool kneel)
    {
        frame = default; kneel = false;
        var weapon = carriage.Holder;
        if (!world.HasComponent<TkbIdentity>(weapon)) return false;
        var look = LookOf(world.GetComponentRO<TkbIdentity>(weapon).TkbType, _tkb());
        ref readonly var w = ref world.GetComponentRO<SimTransform>(weapon);
        var weaponBody = Matrix4x4.CreateFromQuaternion(w.Rotation) * Matrix4x4.CreateTranslation(w.Position);
        if (carriage.Seat is { HasPlace: true } seat)
        {
            frame = Carriage.InSeat(weaponBody, seat);
            kneel = seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel;
            return true;
        }
        if (look.Family != VisualFamily.WeaponSystem || WeaponKits.GunnerSpot(look.Weapon) is not { } spot) return false;
        var size = SizeOf(world, weapon, look);
        var scale = size / WeaponKits.DefaultSize(look.Weapon);
        float azimuth = DeploymentOf(world, weapon, now) >= 1f && _poseOf != null ? _poseOf(world, weapon).TurretAzimuth : 0f;
        var pivot = WeaponKits.TraversePivot(look.Weapon, size);
        frame = Matrix4x4.CreateRotationZ(spot.Facing) * Matrix4x4.CreateTranslation(spot.At * scale)
                * Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationZ(azimuth) * Matrix4x4.CreateTranslation(pivot)
                * weaponBody;
        kneel = spot.Kneel;
        return true;
    }

    /// <summary>⭐ CE-1085 (N-3) — pass 2: each carried weapon on its bearer's back as his figure was drawn, stowed; not drawn while he
    /// holds it on his shoulder (the figure's tube is it), nor when its bearer was not drawn.</summary>
    private void DrawCarried(EntityRepository world, LitShader shader, Func<Entity, bool> wreckOf)
    {
        foreach (var (weapon, bearer, look, size, colour) in _carried)
        {
            if (_shouldering.Contains(bearer) || !_figureFrames.TryGetValue(bearer, out var figure)) continue;
            var frame = WeaponKits.OnBack(figure.Frame, figure.Height, size);
            bool wreck = wreckOf(weapon);
            foreach (var part in WeaponKits.Kit(look.Weapon, deployed: false).Parts)
            {
                var model = HrotToRaylib.ModelFromHrot(part.BodyTransform(size) * frame);
                var mesh = part.Shape switch { PartShape.Cylinder => _cylinder, PartShape.Cone => _cone, _ => _cube };
                Raylib.DrawMesh(mesh, shader.Tinted(PartColour(part.Colour, colour, wreck)), model);
                PartsDrawn++;
            }
            CarriedDrawn++;
            BodiesDrawn++;
        }
    }

    private readonly DoorSwing _doors = new();
    private readonly Dictionary<Entity, (Vector3 Tip, Vector3 Forward)> _weaponTips = new();
    private readonly List<Entity> _tipsGone = new();
    private readonly Dictionary<Entity, Vector3> _eyes = new();

    /// <summary>⭐ CE-1073 — the unit the camera is looking OUT of (first person): its head is not drawn. Null: none.</summary>
    public Entity FirstPerson { get; set; } = Entity.Null;

    /// <summary>⭐ CE-1073 — where the eyes are in a head part's own unit box: the front face, a little above its middle.</summary>
    public static readonly Vector3 EyeInHead = new(0.45f, 0f, 0.1f);

    /// <summary>
    /// ⭐ CE-1073 (docs/DESIGN_Map_3D_Mode.md §6l) — the eyes of a figure's head part as drawn (stance, lean, run and crawl included),
    /// in the BODY's frame (before its position and heading), so a first-person camera can place them on the unit's CURRENT
    /// transform. 🔒 User, 2026-10-14: "the camera should rather be bound to character's head/eyes position wise, not just fixed above
    /// the reference point". Pure — the rail runs it without a GL context.
    /// </summary>
    public static Vector3 EyeLocal(in KitPart head, Vector3 size, KitPivots pivots, ArticulationPose pose, MotionPose motion, Matrix4x4 kitToBody)
        => Vector3.Transform(EyeInHead, head.BodyTransform(size, pivots, pose, motion) * kitToBody);

    /// <summary>⭐ CE-1073 — the eyes of <paramref name="e"/> in its body frame, as its figure was last drawn. False when it was not
    /// drawn as a figure.</summary>
    public bool TryGetEyeLocal(Entity e, out Vector3 local) => _eyes.TryGetValue(e, out local);

    /// <summary>The rails' stand-in for a drawn frame (a figure's eyes without a GL context).</summary>
    internal void RememberEyeLocal(Entity e, Vector3 local) => _eyes[e] = local;

    /// <summary>
    /// ⭐ CE-1070 (docs/DESIGN_Visual_Effects.md §6c) — where the hand-held weapon of a figure drawn in the last frame ends, and which
    /// way it points (its posture, stance, lean and aim included) — the muzzle flash of a type with no TKB muzzle data (a rifleman)
    /// starts there. False when the entity was not drawn as a figure with a weapon.
    /// </summary>
    /// <summary>⭐ CE-1070 — the muzzle end of a weapon part as drawn (a rifle is a box from its stock to its muzzle, along the part's
    /// own +X) and the way it points, through <paramref name="bodyToWorld"/>. Pure — the rail runs it without a GL context.</summary>
    public static (Vector3 Tip, Vector3 Forward) WeaponTip(in KitPart part, Vector3 size, KitPivots pivots, ArticulationPose pose,
                                                           MotionPose motion, Matrix4x4 bodyToWorld)
    {
        var m = part.BodyTransform(size, pivots, pose, motion) * bodyToWorld;
        // ⭐ CE-1090 — a box runs along its own x; a cylinder (a shouldered tube) along its unit z, 0 → 1
        bool tube = part.Shape == PartShape.Cylinder;
        var tip = Vector3.Transform(tube ? new Vector3(0f, 0f, 1f) : new Vector3(0.5f, 0f, 0f), m);
        return (tip, Vector3.Normalize(tip - Vector3.Transform(tube ? Vector3.Zero : new Vector3(-0.5f, 0f, 0f), m)));
    }

    public bool TryGetWeaponTip(Entity e, out Vector3 tip, out Vector3 forward)
    {
        if (_weaponTips.TryGetValue(e, out var t)) { tip = t.Tip; forward = t.Forward; return true; }
        tip = forward = default;
        return false;
    }


    /// <summary>⭐ CE-1068 (§6k) — door entity <paramref name="e"/>'s leaf, swung toward its state's angle on sim time; nothing for a
    /// destroyed door or a host with no terrain to place it in.</summary>
    private void DrawDoor(EntityRepository world, Entity e, LitShader shader, double simTime, bool hasSelection)
    {
        if (!DoorLeaf3D.TryLeaf(world, e, out var leaf, out var state) || DoorLeaf3D.TargetAngle(state) is not { } target) return;
        _drawn.Add(e);
        var (centre, rotation, half) = DoorLeaf3D.Box(leaf, _doors.Angle(e, target, simTime));
        var model = Matrix4x4.CreateScale(half * 2f) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(centre);
        Raylib.DrawMesh(_cube, shader.Tinted(DoorLeaf3D.ColourOf(state)), HrotToRaylib.ModelFromHrot(model));   // ⭐ CE-1069 — locked tinted
        PartsDrawn++;
        DoorsDrawn++;
        if (hasSelection && world.HasComponent<Hrot.IG.Components.SelectionState>(e))
        {
            ref readonly var sel = ref world.GetComponentRO<Hrot.IG.Components.SelectionState>(e);
            if (sel.IsSelected)
            {
                DrawWireBox(centre, rotation, half * 1.06f, sel.IsPrimarySelection ? PrimarySelection : OtherSelection);
                SelectionBoxesDrawn++;
            }
        }
    }
    private readonly PostureBlend _postures = new();

    /// <summary>⭐ WL-6b — the posture changes' timing (the rail's seam).</summary>
    public PostureBlend Postures => _postures;

    /// <summary>The dead men's falls (W4) — read by rails.</summary>
    public DeathFall Falls => _fall;
    private readonly HashSet<Entity> _drawn = new();
    private float _wallFallback;

    /// <summary>The moving-parts tracker (S4) — read by rails.</summary>
    public MotionTracker Motion => _motion;

    /// <summary>Clearly off the ground: above its resting height by <see cref="AirborneMargin"/> (the gear heuristic).</summary>
    private static bool IsAirborne(EntityRepository world, Vector3 position, BodyGeometryDto? geometry)
    {
        float rest = geometry != null ? BodyGeometry.RestingHeight(geometry) : 0f;
        float ground = Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(position.X, position.Y) ?? 0f;
        return position.Z - ground > rest + AirborneMargin;
    }

    /// <summary>Gear legs and wheels / skids drawn at the TKB's contact points. ⚠ Retractable gear is hidden when the body is
    /// clearly airborne — a PRESENTATION guess until a gear-state component exists (docs/DESIGN_Body_Geometry_And_Ground_Contact.md §5).</summary>
    private void DrawGear(EntityRepository world, BodyGeometryDto geometry, Vector3 position, Matrix4x4 body, LitShader shader, bool wreck = false)
    {
        var metal = PartColour(ColourRole.Metal, Metal, wreck);
        var dark = PartColour(ColourRole.Dark, Dark, wreck);
        bool airborne = IsAirborne(world, position, geometry);
        float skidLeftMin = float.MaxValue, skidLeftMax = float.MinValue, skidRightMin = float.MaxValue, skidRightMax = float.MinValue;
        float skidZ = 0f, skidLeftY = 0f, skidRightY = 0f;
        foreach (var c in geometry.GroundContacts)
        {
            if (c.Retractable && airborne) continue;
            float strut = c.StrutLength > 0f ? c.StrutLength : 1f;
            float hub = c.Z + c.WheelRadius;
            DrawPart(_cube, Matrix4x4.CreateScale(0.14f, 0.14f, strut) * Matrix4x4.CreateTranslation(c.X, c.Y, hub + strut / 2f), metal, body, shader);
            if (c.Kind == GroundContactKind.Wheel && c.WheelRadius > 0f)
            {
                float d = 2f * c.WheelRadius;
                DrawPart(_cylinder, Matrix4x4.CreateTranslation(0, 0, -0.5f) * Matrix4x4.CreateScale(d, d, MathF.Max(0.12f, 0.6f * c.WheelRadius))
                                    * Matrix4x4.CreateRotationX(-MathF.PI / 2f) * Matrix4x4.CreateTranslation(c.X, c.Y, hub), dark, body, shader);
            }
            else if (c.Kind == GroundContactKind.Skid)
            {
                skidZ = c.Z;
                if (c.Y >= 0f) { skidLeftMin = MathF.Min(skidLeftMin, c.X); skidLeftMax = MathF.Max(skidLeftMax, c.X); skidLeftY = c.Y; }
                else { skidRightMin = MathF.Min(skidRightMin, c.X); skidRightMax = MathF.Max(skidRightMax, c.X); skidRightY = c.Y; }
            }
        }
        if (skidLeftMax > skidLeftMin) DrawSkid(skidLeftMin, skidLeftMax, skidLeftY, skidZ, body, shader, dark);
        if (skidRightMax > skidRightMin) DrawSkid(skidRightMin, skidRightMax, skidRightY, skidZ, body, shader, dark);
    }

    private void DrawSkid(float x0, float x1, float y, float z, Matrix4x4 body, LitShader shader, Color colour)
        => DrawPart(_cube, Matrix4x4.CreateScale(x1 - x0 + 0.6f, 0.12f, 0.1f) * Matrix4x4.CreateTranslation((x0 + x1) / 2f, y, z + 0.05f), colour, body, shader);

    /// <summary>⭐ CE-1097 (docs/DESIGN_Map_3D_Mode.md §6o) — the face on a figure's head: each detail is a box in the head box's own unit
    /// frame, turned onto the side the face is on (<see cref="BlockFigure.FaceFrame"/>) and then placed by the head's own transform
    /// (<paramref name="head"/>), so it follows every lean, twist, blend and fall.</summary>
    /// ⭐ CE-1099 (§6q) — in his <paramref name="outfit"/>'s skin and hair; no hair when he has none.
    private void DrawFace(FaceOn on, Matrix4x4 head, bool soldier, Color uniform, LitShader shader, Outfit? outfit = null)
    {
        foreach (var d in BlockFigure.FaceParts(soldier))
        {
            if (d.Shade == FaceShade.Hair && outfit?.HairStyle == HairStyle.None) continue;
            Raylib.DrawMesh(_cube, shader.Tinted(FaceColour(d.Shade, uniform, outfit?.Skin, outfit?.Hair)),
                            HrotToRaylib.ModelFromHrot(BlockFigure.FaceDetail(d, on, head)));
            FaceDetailsDrawn++;
        }
    }

    /// <summary>⭐ CE-1098 (docs/DESIGN_Map_3D_Mode.md §6p) — this man's blood spots, into <see cref="_stains"/>: as many as his lost health
    /// earns (<see cref="BlockFigure.StainCount"/>), seeded by his network id so every node draws the same spots in the same places
    /// (the entity's index where he has none). Returns the count.</summary>
    private int Bloodied(EntityRepository world, Entity e, bool hasNet)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>()
            || !world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(e)) return 0;
        ref readonly var hp = ref world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e);
        int n = BlockFigure.StainCount(hp.Current, hp.Max);
        if (n == 0) return 0;
        long seed = SeedOf(world, e, hasNet);
        _stains.Clear();
        for (int k = 0; k < n; k++) _stains.Add(BlockFigure.StainOf(seed, k));
        return n;
    }

    private readonly List<Stain> _stains = new(BlockFigure.MaxStains);

    /// <summary>⭐ CE-1098 / CE-1099 — a man's seed: his network id (the same on every node), else his entity index.</summary>
    private static long SeedOf(EntityRepository world, Entity e, bool hasNet)
        => hasNet && world.HasComponent<NetworkIdentity>(e) && world.GetComponentRO<NetworkIdentity>(e).Value is long net && net != 0 ? net : e.Index;

    /// <summary>⭐ CE-1099 (§6q O-4) — his side (replicated <see cref="EntityInfo.ForceId"/>); Neutral where there is none.</summary>
    private static ForceId SideOf(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(e) ? world.GetComponentRO<EntityInfo>(e).ForceId : ForceId.Neutral;

    /// <summary>⭐ CE-1099 (§6q O-1) — true when his colour is HIS (authored on the entity, or named by his type), not the family default.</summary>
    private static bool HasOwnColour(EntityRepository world, Entity e, in TypeLook look)
    {
        if (look.TypeColoured) return true;
        if (world.IsComponentTypeRegistered<EntityAppearance>() && world.HasComponent<EntityAppearance>(e) && world.GetComponentRO<EntityAppearance>(e).HasColour)
            return true;
        return world.IsComponentTypeRegistered<VisualData>() && world.HasComponent<VisualData>(e)
            && ParseHex(world.GetComponentRO<VisualData>(e).ColorHex) != null;
    }

    private readonly List<PartDetail> _details = new(Outfits.FleckPatches + 4);

    /// <summary>⭐ CE-1098 (§6p B-4) — blood: a fresh red and a dried, darker one.</summary>
    public static readonly Color BloodFresh = new(186, 24, 20, 255), BloodDark = new(128, 16, 14, 255);

    /// <summary>⭐ CE-1097 (§6o F-1) — the skin; the features a shade of it or dark.</summary>
    public static readonly Color Skin = new(206, 168, 132, 255);

    /// <summary>⭐ CE-1097 (§6o F-2, F-3) — a face detail's colour: the helmet is the uniform a step darker (so the side still reads from
    /// above, where the helmet is most of what is seen of a man).</summary>
    /// ⭐ CE-1099 (§6q) — with a man's own <paramref name="skin"/> (the nose a shade of it) and <paramref name="hair"/> when given.
    public static Color FaceColour(FaceShade shade, Color uniform, Color? skin = null, Color? hair = null) => shade switch
    {
        FaceShade.Eye => new Color(28, 24, 22, 255),
        FaceShade.Nose => skin is { } s ? new Color((byte)(s.R * 0.88f), (byte)(s.G * 0.86f), (byte)(s.B * 0.84f), (byte)255) : new Color(184, 142, 108, 255),
        FaceShade.Mouth => new Color(132, 52, 48, 255),
        FaceShade.Hair => hair ?? new Color(62, 44, 32, 255),
        _ => new Color((byte)(uniform.R * 0.72f), (byte)(uniform.G * 0.72f), (byte)(uniform.B * 0.72f), (byte)255),
    };

    private void DrawPart(Mesh mesh, Matrix4x4 local, Color colour, Matrix4x4 body, LitShader shader)
    {
        Raylib.DrawMesh(mesh, shader.Tinted(colour), HrotToRaylib.ModelFromHrot(local * body));
        PartsDrawn++;
    }

    /// <summary>⭐ CE-1033 S2 (§3.2: "wire cubes") — a selected entity's box, drawn as wire edges: green for the primary
    /// selection, yellow for the others (the 2-D map's selection colours).</summary>
    private static void DrawWireBox(Vector3 centre, Quaternion rotation, Vector3 half, Color colour)
    {
        Span<Vector3> c = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            var local = new Vector3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z);
            c[i] = HrotToRaylib.Position(centre + Vector3.Transform(local, rotation));
        }
        ReadOnlySpan<int> edges = stackalloc int[] { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
        for (int k = 0; k < edges.Length; k += 2) Raylib.DrawLine3D(c[edges[k]], c[edges[k + 1]], colour);
    }

    private static readonly Color PrimarySelection = new(60, 230, 90, 255);
    private static readonly Color OtherSelection = new(240, 220, 60, 255);

    private readonly Dictionary<long, float> _tops = new();

    /// <summary>⭐ CE-1033 S3 (M13) — the height of the top of an entity's DRAWN body above its position, by network id, from
    /// the last 3-D frame; null when the body was not drawn. The gizmo label overlay lifts the entity's labels to it.</summary>
    public float? TopAbove(long networkId) => _tops.TryGetValue(networkId, out float top) ? top : null;

    /// <summary>Selection boxes drawn in the last 3-D frame.</summary>
    public int SelectionBoxesDrawn { get; private set; }

    /// <summary>How far above its resting height a body must be before its retractable gear is drawn up.</summary>
    public const float AirborneMargin = 5f;

    /// <summary>What <paramref name="tkbType"/> looks like: its family, TKB size (else the family's) and colour.</summary>
    private readonly Dictionary<(long Type, Vector3 Size), (KitPart[] Parts, KitPivots Pivots)> _placedKits = new();
    private ITkbDatabase? _placedKitsTkb;

    /// <summary>⭐ E4b — the kit for <paramref name="type"/> placed from its TKB turret data (<see cref="KitFromTkb"/>), cached per
    /// type and size; the family kit when the type has none.</summary>
    public (KitPart[] Parts, KitPivots Pivots) PlacedKit(ShapeKit kit, long type, ITkbDatabase? tkb, Vector3 size)
    {
        if (!ReferenceEquals(tkb, _placedKitsTkb)) { _placedKits.Clear(); _placedKitsTkb = tkb; }
        if (_placedKits.TryGetValue((type, size), out var placed)) return placed;
        placed = KitFromTkb.For(kit, tkb != null && tkb.TryGetByType(type, out var t) ? t : null, size);
        _placedKits[(type, size)] = placed;
        return placed;
    }

    public TypeLook LookOf(long tkbType, ITkbDatabase? tkb)
    {
        if (_looks.TryGetValue(tkbType, out var look)) return look;
        // ⭐ CE-1042 — an EFFECT type (muzzle flash, explosion, decal, tracer) has no body: the effect layer draws it. Checked by
        //   the catalog too, so a host with no TKB (the replay browser) does not draw a recorded effect as an unknown box.
        // ⭐ CE-1033 S5 — nor has the map CAMERA entity (R-260): it is the view, not a thing in it.
        // ⭐ LR-3 — nor has a built-in MUNITION type (a round), on a host whose TKB lacks it (the replay browser).
        // ⭐ CE-1068 — nor has a DOOR a shape kit: it is drawn as its leaf (DoorLeaf3D), and where no terrain places it, not at all.
        if (Hrot.Core.Tkb.EffectTkbCatalog.BuiltIn.ContainsKey(tkbType) || Hrot.Core.Tkb.MapCameraTkb.Is(tkbType)
            || Hrot.Core.Tkb.MunitionTkbCatalog.IsBuiltIn(tkbType) || tkbType == Hrot.Map.Common.TkbEntityTypes.Door)
            return _looks[tkbType] = new TypeLook(VisualFamily.Unit, Vector3.Zero, DefaultColour(VisualFamily.Unit), false);
        look = Resolve(tkb != null && tkb.TryGetByType(tkbType, out var t) ? t : null);
        _looks[tkbType] = look;
        return look;
    }

    /// <summary>The look of a template (null ⇒ an unknown box). Pure — the rail runs it over the whole built-in catalog.</summary>
    public static TypeLook Resolve(TkbTemplate? t)
    {
        if (t == null) return new TypeLook(VisualFamily.Unknown, ShapeKits.DefaultSize(VisualFamily.Unknown), DefaultColour(VisualFamily.Unknown), false);
        if (t.GetDescriptor<EffectVisualDto>() != null)   // ⭐ CE-1042 — an effect type has no body
            return new TypeLook(VisualFamily.Unit, Vector3.Zero, DefaultColour(VisualFamily.Unit), false);
        // ⭐ LR-3 (DESIGN_Visual_Effects.md §8.5, R-271) — a MUNITION type (DIS kind 2, or one with a Munition.Visual) is a round, not a body:
        //   ProjectileLayer draws it. ⛔ Without this a replicated round (TkbIdentity = its munition) drew as an unknown purple box.
        if (t.DisType.Kind == 2 || t.GetDescriptor<MunitionVisualDto>() != null)
            return new TypeLook(VisualFamily.Unit, Vector3.Zero, DefaultColour(VisualFamily.Unit), false);
        var family = VisualFamilies.Of(t);
        var weapon = family == VisualFamily.WeaponSystem ? WeaponKits.KindOf(t.DisType) : WeaponKind.Hmg;   // ⭐ CE-1087
        var size = family == VisualFamily.WeaponSystem ? WeaponKits.DefaultSize(weapon) : ShapeKits.DefaultSize(family);
        var geometry = t.GetDescriptor<BodyGeometryDto>();
        if (geometry is { Length: > 0f, Width: > 0f, Height: > 0f })
        {
            size = new Vector3(geometry.Length, geometry.Width, geometry.Height);   // ⭐ CE-1041 — engine-neutral size wins
        }
        else if (family == VisualFamily.Drone && t.GetDescriptor<FlightParamsDto>() is { BodyRadiusM: > 0f, BodyHeightM: > 0f } flight)
        {
            // ⭐ CE-1086 (§6n N-7) — a drone is as big as its flight body (the collider the sim stamps from Gen.Flight)
            size = new Vector3(2f * flight.BodyRadiusM, 2f * flight.BodyRadiusM, flight.BodyHeightM);
        }
        else if (family != VisualFamily.Person && family != VisualFamily.Unit)
        {
            var sim = t.GetDescriptor<SimVehicleDef>();
            var kin = t.GetDescriptor<VehicleParametersDto>();
            float length = Positive(sim?.Length, kin?.Length) ?? size.X;
            float width = Positive(sim?.Width, kin?.Width) ?? size.Y;
            float height = Positive(sim?.Height, t.GetDescriptor<StrideRenderModelDefDto>()?.ShapeHeight) ?? size.Z;
            size = new Vector3(length, width, height);
        }
        else if (family == VisualFamily.Person && Positive(t.GetDescriptor<StrideRenderModelDefDto>()?.ShapeHeight) is { } tall)
        {
            size *= tall / size.Z;
        }
        var typeColour = ParseHex(t.GetDescriptor<VisualDefinitionDto>()?.ColorHex);
        return new TypeLook(family, size, typeColour ?? DefaultColour(family), VisualFamilies.IsSoldier(t), geometry, weapon,
                            TypeColoured: typeColour != null, Child: family == VisualFamily.Person && size.Z < ChildHeight);
    }

    /// <summary>
    /// ⭐ CE-1033 S2 (P1) — the oriented box an entity is drawn in, which is also what a 3-D click hits: its kit's size, placed
    /// by its reference point (ground centre, or the TKB box offset for a CG-referenced body). False for a unit (no body).
    /// </summary>
    public bool TryGetBox(EntityRepository world, Entity e, ITkbDatabase? tkb, out Vector3 centre, out Quaternion rotation, out Vector3 half)
    {
        centre = default; rotation = Quaternion.Identity; half = default;
        if (!world.HasComponent<TkbIdentity>(e) || !world.HasComponent<SimTransform>(e)) return false;
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>()
            && world.HasComponent<Fdp.Toolkit.Replication.Components.LightweightRound>(e)) return false;   // LR-3: a round has no box
        if (Fdp.Toolkit.Perception.PhysicalPresence.IsEnclosedPassenger(world, e)) return false;   // ⭐ CE-1058: nothing to pick or select
        // ⭐ CE-1068 — a door is picked and selected by its LEAF, where it is drawn (shut, open, or nothing when destroyed)
        if (DoorLeaf3D.IsDoor(world, e))
        {
            if (!DoorLeaf3D.TryLeaf(world, e, out var leaf, out var state) || DoorLeaf3D.TargetAngle(state) is not { } target) return false;
            (centre, rotation, half) = DoorLeaf3D.Box(leaf, _doors.Current(e, target));
            half = Vector3.Max(half, new Vector3(0.1f));   // a 5 cm leaf stays clickable edge-on
            return true;
        }
        var look = LookOf(world.GetComponentRO<TkbIdentity>(e).TkbType, tkb);
        if (look.Family == VisualFamily.Unit) return false;
        var size = SizeOf(world, e, look);
        ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
        rotation = tf.Rotation;
        var local = look.Geometry is { } g
            ? new Vector3(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ)
            : new Vector3(0f, 0f, size.Z / 2f);
        centre = tf.Position + Vector3.Transform(local, rotation);
        half = Vector3.Max(size / 2f, new Vector3(MinPickHalf));   // a person stays clickable from afar
        return true;
    }

    /// <summary>The smallest half-extent a pick box gets (m).</summary>
    public const float MinPickHalf = 0.4f;

    private static Vector3 SizeOf(EntityRepository world, Entity e, in TypeLook look)
    {
        if (look.Family == VisualFamily.Person || !world.IsComponentTypeRegistered<CarKinem.Core.VehicleParams>()
            || !world.HasComponent<CarKinem.Core.VehicleParams>(e)) return look.Size;
        ref readonly var p = ref world.GetComponentRO<CarKinem.Core.VehicleParams>(e);
        return new Vector3(p.Length > 0f ? p.Length : look.Size.X, p.Width > 0f ? p.Width : look.Size.Y, look.Size.Z);
    }

    /// <summary>⭐ CE-1033 S5c (§3.7) — the body colour: the entity's own <see cref="EntityAppearance"/> (scenario / per spawn) →
    /// the TKB's <c>VisualData.ColorHex</c> → the family default.</summary>
    public static Color ColourOf(EntityRepository world, Entity e, Color fallback)
    {
        if (world.IsComponentTypeRegistered<EntityAppearance>() && world.HasComponent<EntityAppearance>(e))
        {
            var a = world.GetComponentRO<EntityAppearance>(e);
            if (a.HasColour) return new Color(a.R, a.G, a.B, (byte)255);
        }
        if (!world.IsComponentTypeRegistered<VisualData>() || !world.HasComponent<VisualData>(e)) return fallback;
        string hex = world.GetComponentRO<VisualData>(e).ColorHex;
        return ParseHex(hex) ?? fallback;
    }

    /// <summary>⭐ W2 (VE-O) — the colour a part is drawn: its role's shade of the body colour, or, on a WRECK, near-black for every
    /// role — the roles keep a few steps apart so the burnt-out shape still reads.</summary>
    public static Color PartColour(ColourRole role, Color body, bool wreck) => wreck
        ? role switch
        {
            ColourRole.Dark => new Color(12, 12, 11, 255),
            ColourRole.Glass => new Color(20, 22, 24, 255),
            ColourRole.Metal => new Color(26, 25, 24, 255),
            _ => new Color(32, 30, 28, 255),
        }
        : Shade(role, body);

    private static Color Shade(ColourRole role, Color body) => role switch
    {
        ColourRole.Skin => Skin,   // ⭐ CE-1097 (§6o F-1)
        ColourRole.Dark => Dark,
        ColourRole.Glass => Glass,
        ColourRole.Metal => Metal,
        _ => body,
    };

    public static Color DefaultColour(VisualFamily family) => family switch
    {
        VisualFamily.Person => new Color(96, 110, 72, 255),
        VisualFamily.Tank => new Color(112, 120, 82, 255),
        VisualFamily.Afv => new Color(122, 116, 84, 255),
        VisualFamily.WheeledCar => new Color(72, 104, 168, 255),
        VisualFamily.WheeledUtility => new Color(128, 120, 92, 255),
        VisualFamily.Helicopter => new Color(96, 104, 92, 255),
        VisualFamily.Jet => new Color(128, 136, 146, 255),
        VisualFamily.CargoPlane => new Color(118, 124, 116, 255),
        VisualFamily.Drone => new Color(74, 74, 74, 255),
        VisualFamily.WeaponSystem => new Color(84, 92, 70, 255),
        VisualFamily.SpArtillery => new Color(112, 120, 82, 255),
        VisualFamily.AttackJet => new Color(122, 128, 118, 255),
        _ => new Color(180, 120, 200, 255),
    };

    /// <summary><c>#RRGGBB</c> or <c>#RRGGBBAA</c> (alpha ignored — bodies are opaque); null for anything else.</summary>
    public static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if ((s.Length != 6 && s.Length != 8) || !uint.TryParse(s.AsSpan(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return null;
        return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
    }

    private static float? Positive(params float?[] values)
    {
        foreach (var v in values) if (v is > 0f) return v;
        return null;
    }

    private void EnsureMeshes()
    {
        if (_meshes) return;
        _cube = Raylib.GenMeshCube(1f, 1f, 1f);
        _cylinder = Raylib.GenMeshCylinder(0.5f, 1f, 14);
        _cone = Raylib.GenMeshCone(0.5f, 1f, 14);
        _meshes = true;
    }

    public void Dispose()
    {
        if (!_meshes) return;
        Raylib.UnloadMesh(_cube);
        Raylib.UnloadMesh(_cylinder);
        Raylib.UnloadMesh(_cone);
        _meshes = false;
    }
}
