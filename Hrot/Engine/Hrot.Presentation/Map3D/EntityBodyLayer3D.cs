using System.Globalization;
using System.Numerics;
using Fdp.Toolkit.Combat.Components;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Map.Definitions.Tkb;
using Hrot.MuscleCharacter.Animation.Components;
using OutfitHead = Hrot.Core.Tkb.OutfitHead;
using Raylib_cs;
using PostureBlend = Hrot.Look.People.PostureBlend;
using UpperBody = Hrot.Look.People.UpperBody;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1033 S1 — the entities in the map's 3-D mode (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.5, M7): every entity with a position
/// and a TKB type is drawn as its family's shape kit (<see cref="ShapeKits"/>), a person as the block figure in its LOGICAL stance
/// (<see cref="LogicalStance"/>), sized from the TKB and coloured by its <see cref="VisualData.ColorHex"/>. Units draw nothing —
/// their members draw themselves. Nothing in 2-D.
/// </summary>
public sealed partial class EntityBodyLayer3D : ISceneLayer, IDisposable   // ⭐ CE-3289 — the world: filmed
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
    private readonly Func<EntityRepository, IReadOnlyList<AmbientBody>>? _ambient;
    private readonly HashSet<long> _ambientDrawn = new();

    /// <summary>⭐ AQ93 §15.8 (CE-4031) — a body with NO entity, drawn as an entity of <paramref name="TkbType"/> would be: its look by
    /// the type, its clothes, face or paint seeded by <paramref name="Key"/> (its network id — the seed its replica would use), standing
    /// at <paramref name="Position"/> facing <paramref name="YawRad"/>; <paramref name="Colour"/> (0xRRGGBB, 0 = none) is a car's own paint.</summary>
    public readonly record struct AmbientBody(long Key, TkbTypeId TkbType, Vector3 Position, float YawRad, uint Colour = 0);

    /// <summary>⭐ CE-4031 — the ambient bodies drawn by the last 3-D frame (people and vehicles).</summary>
    public int AmbientDrawn { get; private set; }

    /// <summary>⭐ CE-4031 — the ambient source this layer was built with (the forwarding rail reads it).</summary>
    public bool HasAmbientSource => _ambient != null;
    private Mesh _cube, _cylinder, _cone;
    private bool _meshes;

    /// <summary>What a TKB type looks like — resolved once per type.</summary>
    /// <para>⭐ CE-1087 — a weapon system's <paramref name="Weapon"/> kind picks its kit.</para>
    /// <para>⭐ CE-1099 (§6q) — <paramref name="TypeColoured"/>: the TYPE names a colour (else <paramref name="Colour"/> is the family
    /// default); <paramref name="Child"/>: a person shorter than <see cref="ChildHeight"/> — ⭐ CE-4648 (OF-2) or, when the type has a <c>PersonDto</c>,
    /// its age band.</para>
    /// <para>⭐ CE-4648 — <paramref name="Sex"/>: the type's <c>PersonDto.Sex</c>; <paramref name="Wardrobe"/>: its <c>OutfitDto</c> flattened ONCE (OF-7), null
    /// when it has none (the everyday wardrobes).</para>
    public readonly record struct TypeLook(VisualFamily Family, Vector3 Size, Color Colour, bool Soldier,
                                           BodyGeometryDto? Geometry = null, WeaponKind Weapon = WeaponKind.Hmg,
                                           bool TypeColoured = false, bool Child = false, PersonKit Kit = PersonKit.Civilian,
                                           bool Elderly = false, bool Toy = false, CarBodyStyle? Style = null,
                                           PersonSex Sex = PersonSex.Unspecified, Wardrobe? Wardrobe = null);

    /// <summary>⭐ CE-1099 (§6q O-3) — a person shorter than this (m) is a child: drawn with a larger head.</summary>
    public const float ChildHeight = Hrot.Look.People.PersonLook.ChildHeight;   // ⭐ CE-4671 LK-S2

    /// <summary>⭐ CE-1099 — how much larger a child's head is than the figure's scaled one.</summary>
    public const float ChildHead = 1.3f;

    /// <param name="poseOf">⭐ CE-1033 §3.11 — the turret azimuth and gun elevation of an entity with an articulated kit. ⭐ WL-1 — in
    /// production it is <see cref="TurretPoseSource.PoseOf"/> (the turret part's <c>TurretPose</c>, DESIGN_Weapon_Layers W-9), plugged in
    /// at ONE place, <c>MapInteractionPack.AttachMapLayers</c>. Null = every turret faces forward.</param>
    /// <param name="ambient">⭐ AQ93 §15.8 (CE-4031) — bodies with NO entity to draw as ours (SUMO's far pedestrians and cars): in
    /// production <see cref="TrafficAmbientBodies.Of"/>, plugged at the same ONE place. Null = none.</param>
    public EntityBodyLayer3D(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb,
                             Func<EntityRepository, Entity, ArticulationPose>? poseOf = null,
                             Func<EntityRepository, IReadOnlyList<AmbientBody>>? ambient = null)
    {
        _poseOf = poseOf;
        _ambient = ambient;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _tkb = tkb ?? throw new ArgumentNullException(nameof(tkb));
    }

    public string Name => "Entities (3-D)";
    public int LayerBitIndex => -1;

    /// <summary>⭐ <c>CE-4582</c> (docs/DESIGN_Map_3D_Mode.md §6am D-9) — the layer panel's Units ▸ Entities row: the gizmo buffer's bit 0, the mask
    /// <c>LayerControlGizmo</c> hands it (as <c>TerrainLayer2D</c> reads Terrain2D). False ⇒ no body is drawn in the window's pass and none is
    /// pickable (the pick index is emptied). Null = always shown. ⚠ Not the canvas's <c>VisibleLayersMask</c> — that is the older per-kind mask.</summary>
    public Func<bool>? Visible { get; set; }

    /// <summary>⭐ CE-4582 — true when the last window pass drew nothing because Units ▸ Entities is off.</summary>
    public bool Hidden { get; private set; }
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

    /// <summary>⭐ CE-4388 (render under load, R-2) — the half-size (m) of the cube round an entity's position tested against the view: larger than any body's reach
    /// from its reference point (an airframe's wings, a rotor), so nothing partly in view is skipped.</summary>
    public const float CullRadius = 40f;

    /// <summary>⭐ CE-4388 — entities this pass skipped as outside the view (a rail; telemetry).</summary>
    public int BodiesCulled { get; private set; }

    private readonly Vector4[] _planes = new Vector4[6];

    /// <summary>⭐ CE-4557 (docs/DESIGN_Map_3D_Mode.md §6ak D-1) — a body's band, by its height on screen.</summary>
    public enum ScreenBand : byte { Full, Far, Hidden }

    /// <summary>⭐ CE-4557 (§6ak D-1) — below this many pixels a body is not drawn; below <see cref="FullFromPx"/> it is one box; from it, the full figure.</summary>
    public const float HiddenBelowPx = 1.5f, FullFromPx = 12f;

    /// <summary>⭐ CE-4557 (§6ak D-3) — a door leaf smaller than this on screen is not drawn.</summary>
    public const float DoorHiddenBelowPx = 2f;

    /// <summary>⭐ CE-4557 — more than any door leaf's largest extent (m): a door whose entity this many metres would not cover <see cref="DoorHiddenBelowPx"/> is
    /// hidden without looking its leaf up.</summary>
    public const float DoorReachMetres = 3f;

    /// <summary>⭐ CE-4557 — the last frame's bodies by band, and the doors not drawn for their size (rails, <c>GET /diagnostics/render</c>).</summary>
    public int BodiesFull { get; private set; }
    public int BodiesFar { get; private set; }
    public int BodiesHidden { get; private set; }
    public int DoorsHidden { get; private set; }

    private Fdp.Toolkit.Vis3D.ViewFrustum.ScreenScale _scale;

    private readonly BodyPickIndex _pick = new();
    private bool _recordPick;

    /// <summary>⭐ CE-4558 (§6ak D-6) — the boxes the last window frame drew, which the pick walks (<see cref="EntityBoxPicker"/>).</summary>
    public BodyPickIndex PickIndex => _pick;

    /// <summary>⭐ CE-4558 — <paramref name="e"/>'s pick box, as <see cref="TryGetBox"/> builds it, from what the frame already read.</summary>
    private void RecordBox(EntityRepository world, Entity e, in TypeLook look, Vector3 size, bool hasNet)
    {
        ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
        var local = look.Geometry is { } g ? new Vector3(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ) : new Vector3(0f, 0f, size.Z / 2f);
        _pick.Add(e, hasNet ? NetOf(world, e) : 0, tf.Position, tf.Position + Vector3.Transform(local, tf.Rotation), tf.Rotation,
                  Vector3.Max(size / 2f, new Vector3(MinPickHalf)));
    }

    private static int NetOf(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<NetworkIdentity>() && world.HasComponent<NetworkIdentity>(e) ? world.GetComponentRO<NetworkIdentity>(e).Value : 0;

    /// <summary>⭐ CE-4557 — the band of a body <paramref name="size"/> standing at <paramref name="position"/> under <paramref name="scale"/>: its LARGEST
    /// extent on screen (a long low vehicle seen from above keeps its length), against <see cref="HiddenBelowPx"/> / <see cref="FullFromPx"/>. Pure (rails).</summary>
    public static ScreenBand BandFor(in Fdp.Toolkit.Vis3D.ViewFrustum.ScreenScale scale, Vector3 position, Vector3 size)
    {
        if (!scale.IsSet) return ScreenBand.Full;
        float extent = MathF.Max(size.Z, MathF.Max(size.X, size.Y));
        float px = scale.PixelsOf(position + new Vector3(0f, 0f, size.Z * 0.5f), extent);
        return px < HiddenBelowPx ? ScreenBand.Hidden : px < FullFromPx ? ScreenBand.Far : ScreenBand.Full;
    }

    private ScreenBand BandOf(EntityRepository world, Entity e, Vector3 size, bool hasSelection)
    {
        if (e == FirstPerson || IsSelected(world, e, hasSelection)) return ScreenBand.Full;   // D-7
        return BandFor(_scale, world.GetComponentRO<SimTransform>(e).Position, size);
    }

    /// <summary>⭐ <c>CE-4582</c> (§6am D-4) — whether the entity with network id <paramref name="networkId"/> is selected (its card is never hidden):
    /// the same <c>SelectionState</c> that keeps its body Full (§6ak D-7).</summary>
    public static bool IsSelectedNet(EntityRepository? world, long networkId)
    {
        if (world == null || networkId <= 0 || networkId > int.MaxValue || !world.IsComponentTypeRegistered<Hrot.Map.Common.Components.SelectionState>()) return false;
        var e = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, (int)networkId);
        return !e.IsNull && IsSelected(world, e, true);
    }

    private static bool IsSelected(EntityRepository world, Entity e, bool hasSelection)
        => hasSelection && world.HasComponent<Hrot.Map.Common.Components.SelectionState>(e)
           && world.GetComponentRO<Hrot.Map.Common.Components.SelectionState>(e).IsSelected;

    /// <summary>⭐ CE-4557 (§6ak D-2) — how much of the figure's bounding box (across, and front to back) its far box fills: the figure is not a solid block —
    /// its arms, legs and rifle leave gaps — so its whole bounds would read heavier than the man; measured at the Full / Far edge (the band rail).</summary>
    public const float FarFill = 0.5f;

    private static readonly (Vector3 Min, Vector3 Max)?[] FarBounds = new (Vector3, Vector3)?[6];

    /// <summary>⭐ CE-4557 — a man's far box in his body frame: the bounds of his OWN figure's parts in that stance (standing, crouched, prone — a prone figure's
    /// parts lie along the ground), across and front to back × <see cref="FarFill"/>, at his <paramref name="size"/>. The bounds are computed once per
    /// (stance, soldier) from <see cref="BlockFigure.Parts(StanceId, bool, WeaponPostureKind)"/>, so the box follows the figure's own model.</summary>
    public static Matrix4x4 FarBox(StanceId stance, bool soldier, Vector3 size)
    {
        int k = (stance switch { StanceId.Crouched => 1, StanceId.Prone => 2, _ => 0 }) * 2 + (soldier ? 1 : 0);
        if (FarBounds[k] is not { } b)
        {
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            var parts = BlockFigure.Parts((k / 2) switch { 1 => StanceId.Crouched, 2 => StanceId.Prone, _ => StanceId.Standing }, soldier);
            for (int i = 0; i < parts.Length; i++) { min = Vector3.Min(min, parts[i].Centre - parts[i].Extent * 0.5f); max = Vector3.Max(max, parts[i].Centre + parts[i].Extent * 0.5f); }
            b = (min, max);
            FarBounds[k] = b;
        }
        var extent = (b.Max - b.Min) * size;
        extent.X *= FarFill; extent.Y *= FarFill;
        return Matrix4x4.CreateScale(extent) * Matrix4x4.CreateTranslation((b.Min + b.Max) * 0.5f * size);
    }

    /// <summary>⭐ CE-4557 (§6ak D-2) — a man's far colour: his outfit's main colour (the torso's); camouflage is what the eye averages at that size — the
    /// base mixed half and half with the mean of its three tones.</summary>
    public static Color FarColour(in Outfit outfit, Color fallback)
    {
        var c = Outfits.ColourOfPart(outfit, BlockFigure.TorsoIndex).ToColor() ?? fallback;
        if (outfit.Camo == CamoPattern.None) return c;
        var (t1, t2, t3) = Outfits.CamoTones(outfit.Camo);
        static byte Mix(byte b, byte a1, byte a2, byte a3) => (byte)((b + (a1 + a2 + a3) / 3) / 2);
        return new Color(Mix(c.R, t1.R, t2.R, t3.R), Mix(c.G, t1.G, t2.G, t3.G), Mix(c.B, t1.B, t2.B, t3.B), (byte)255);
    }

    /// <summary>
    /// ⭐ CE-4557 (§6ak D-2) — the FAR look: one lit box at the body's size, in its main colour — a person's outfit (his torso's colour), lying when he is down
    /// or prone, lower when crouched; a vehicle its kit box (placed as its pick box) in its paint, near-black when wrecked. No pose, no parts, no face.
    /// </summary>
    private void DrawFar(EntityRepository world, Entity e, in TypeLook look, Vector3 size, long type, Color colour, bool hasNet)
    {
        ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
        var body = Matrix4x4.CreateFromQuaternion(tf.Rotation) * Matrix4x4.CreateTranslation(tf.Position);
        Matrix4x4 local;
        if (look.Family == VisualFamily.Person)
        {
            long seed = SeedOf(world, e, hasNet);
            var outfit = Outfits.For(seed, type, look.Soldier, look.Child, OwnColourOf(world, e, look).ToRgba(), SideOf(world, e), look.Kit, look.Elderly, look.Sex, look.Wardrobe);
            colour = FarColour(outfit, colour);
            var stance = LogicalStance.Of(world, e);
            if (Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e)) stance = StanceId.Prone;   // a dead man lies
            local = FarBox(stance, look.Soldier, size);
        }
        else
        {
            bool wreck = Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e);
            colour = PartColour(ColourRole.Body, colour, wreck);
            var centre = look.Geometry is { } g ? new Vector3(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ) : new Vector3(0f, 0f, size.Z * 0.5f);
            local = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(centre);
        }
        Submit(_cube, HrotToRaylib.ModelFromHrot(local * body), colour, BodySurface.PlainData);
        BodiesFar++;
        BodiesDrawn++;
    }

    public void Draw3D(RenderContext ctx)
    {
        AmbientDrawn = 0; DeadAnimalsDrawn = 0; FlockBirdsDrawn = 0;
        BodiesDrawn = PartsDrawn = DoorsDrawn = JumpsDrawn = CarriedDrawn = GunnersDrawn = GunnersTurned = PillionsAimed = ShoulderedDrawn = FaceDetailsDrawn = StainsDrawn = DetailsDrawn = LittersDrawn = TeamLoadsDrawn = 0;
        DescentsDrawn = CanopiesDrawn = RopesDrawn = CutRopesDrawn = 0;
        _ropeUsers.Clear();
        _carried.Clear();
        _shouldering.Clear();
        _loadInHands.Clear();
        MortarsDrawn = 0;
        ChargeLightsDrawn = 0;
        EjectionSeatsDrawn = 0;
        OpeningsDrawn = CrewOutDrawn = 0; _lightsNow.Clear();   // ⭐ CE-4670
        // ⭐ CE-1985 (DESIGN_Sensor_View §5c) — the thermal pass: every body instance carries its °C as its colour (Submit), the batch reads it so
        _thermal = ctx.Sensor == SceneSensor.Thermal && UnitHeat != null;
        _airHeat = Fdp.Toolkit.Vis3D.LitShader.EncodeC(ctx.AirC + 6f);   // a draw no unit owns (a far SUMO body, a rope, a load): about body-warm
        _submitHeat = _unitBody = _unitHot = _airHeat; _hotSign = 0;
        if (_thermal && LitShader.IsCreated) LitShader.Shared.SetThermalSource(1, ThermalDefaultClass);
        _figureFrames.Clear();
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<TkbIdentity>() || !world.IsComponentTypeRegistered<SimTransform>()) return;
        // ⭐ CE-4582 (§6am D-9) — Units ▸ Entities off: no body drawn, and (the window's pass) an EMPTY pick index so none is pickable. A film draws
        //   its layer set INTERSECTED with the screen's (DebugGizmoLayer.FilmLayers), and Entities is one of its rows ⇒ hidden there too.
        bool hidden = Visible != null && !Visible();
        if (!ctx.Film) Hidden = hidden;
        if (hidden) { if (!ctx.Film) { _pick.Begin(); _pick.End(); BodyDrawStats.Record(0, 0, 0, 0); BodyDrawStats.RecordBands(0, 0, 0, 0); } return; }
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            var q = world.Query().With<SimTransform>().With<TkbIdentity>().WithLifecycle(EntityLifecycle.All);
            // ⭐ LR-3 (DESIGN_Visual_Effects.md §8.5, R-271) — a replicated ROUND is never a body: ProjectileLayer draws it
            _query = (world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>()
                ? q.Without<Fdp.Toolkit.Replication.Components.LightweightRound>() : q).Build();
        }
        EnsureMeshes();
        LitShader.UseSurface(BodySurface.Glsl);   // ⭐ CE-1101 (§6r) — the shader paints camouflage and faces
        var shader = LitShader.Shared;
        _surface.BeginFrame(shader);
        _batch.Begin();   // ⭐ CE-4555
        FacesPainted = CamoPainted = 0;
        var tkb = _tkb();

        // ⭐ CE-1033 S4 — the moving parts run on SIMULATION time (R-143); a host with no sim clock falls back to the frame time.
        _wallFallback += ctx.DeltaTime > 0f && float.IsFinite(ctx.DeltaTime) ? ctx.DeltaTime : 0f;
        _frameClock += ctx.DeltaTime > 0f && float.IsFinite(ctx.DeltaTime) ? ctx.DeltaTime : 0.0;   // ⭐ CE-1353 — the rotors' smooth clock
        double simTime = world.HasSingletonUnmanaged<GlobalTime>() ? world.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : _wallFallback;
        _drawn.Clear();
        bool hasSelection = world.IsComponentTypeRegistered<Hrot.Map.Common.Components.SelectionState>();
        bool hasNet = world.IsComponentTypeRegistered<NetworkIdentity>();
        SelectionBoxesDrawn = 0;
        _tops.Clear();
        // ⭐ CE-4388 (R-2) — the frustum of THIS pass (the window's camera or a film's), the terrain's own test (ViewFrustum): measured on Windows, 2026-11-10,
        //   every body was posed and drawn part by part each frame, in view or not — 31 ms at 1 000 men on scale-1, and all 6 912 doors. A body that leaves the
        //   view and comes back starts its gait afresh, as a newly spawned one does (its trackers end with this frame's _drawn).
        bool cull = Fdp.Toolkit.Vis3D.ViewFrustum.TryCurrent(_planes);
        BodiesCulled = 0;
        // ⭐ CE-4557 (§6ak D-1) — the screen-size bands of THIS pass; no frustum (a rail with no pass) ⇒ every body Full, as before
        _scale = cull ? Fdp.Toolkit.Vis3D.ViewFrustum.CurrentScale() : default;
        BodiesFull = BodiesFar = BodiesHidden = DoorsHidden = 0;
        // ⭐ CE-4558 (§6ak D-6) — the window's pass records the boxes it drew, for the pick (a film's pass draws another camera: it records nothing)
        _recordPick = !ctx.Film;
        if (_recordPick) _pick.Begin();
        foreach (var e in _query!)
        {
            if (cull && !Fdp.Toolkit.Vis3D.ViewFrustum.AroundInside(_planes, world.GetComponentRO<SimTransform>(e).Position, CullRadius)) { BodiesCulled++; continue; }
            // ⭐ CE-1068 (§6k) — a DOOR is its leaf, hinged in its doorway, never a shape kit (it used to be an unknown box)
            if (DoorLeaf3D.IsDoor(world, e))
            {
                // ⭐ CE-4557 (§6ak D-3) — a door that cannot reach DoorHiddenBelowPx even at its largest (DoorReachMetres, round its entity) is skipped before its leaf is
                //   looked up (a managed key and a map per door: 6 912 of them at the whole-terrain view were 11 % of the frame though none was drawn)
                if (_scale.PixelsOf(world.GetComponentRO<SimTransform>(e).Position, DoorReachMetres) < DoorHiddenBelowPx && !IsSelected(world, e, hasSelection))
                {
                    DoorsHidden++;
                    _drawn.Add(e);   // its swing state is kept
                    continue;
                }
                DrawDoor(world, e, shader, simTime, hasSelection);
                continue;
            }
            long type = world.GetComponentRO<TkbIdentity>(e).TkbType;
            var look = LookOf(type, tkb);
            if (look.Family == VisualFamily.Unit) continue;
            // ⭐ CE-4623 (AU-4) — a FLOCK is one entity drawn as N birds, closed-form (never one body)
            if (look.Family == VisualFamily.Bird && AnimalReader.FlockOf(world, e, out var flock)) { DrawFlock(world, e, flock, simTime); _drawn.Add(e); continue; }
            // ⭐ CE-1058 — a passenger inside an ENCLOSED vehicle is not seen (the engine's one rule, R-271); an exposed rider is drawn.
            if (Fdp.Toolkit.Perception.PhysicalPresence.IsEnclosedPassenger(world, e)) continue;

            var size = SizeOf(world, e, look);
            if (_recordPick) RecordBox(world, e, look, size, hasNet);   // ⭐ CE-4558 — every band: a tiny body is still hovered through the pick's pixel margin
            // ⭐ CE-1200 (docs/DESIGN_Entity_Control.md §8o) — the operator's OWN vehicle IS drawn while he looks from it: his eye is a CREW
            //   eye inside its cab / canopy / cockpit (that box, seen from inside, culls itself) or a commander's above its turret, so the
            //   bonnet, the wings, the barrel are in view. ⛔ SUPERSEDED CE-1131 ("not drawn: the airframe filled the view" — the eye was
            //   a fixed height above the reference point, inside the hull). A kind with no crew eye is still not drawn.
            Vector3? crewEye = null;
            // ⭐ CE-1974 — an OPEN carrier (a horse, any animal) has no cab to look out of: its rider sits ON it in plain view, so
            //   it is always drawn. 🔒 User, 2026-11-17: "When a soldier which is possessed gets on a horse, it is rendered as sitting on the
            //   horse, but the horse is not rendered at all". ⛔ It was dropped: no glass, no turret ⇒ no crew eye ⇒ not drawn.
            if (e == FirstPerson && look.Family != VisualFamily.Person && !OpenCarrier(look.Family))
            {
                crewEye = CrewEye(look.Family, size);
                if (crewEye == null) { _drawn.Add(e); continue; }
                crewEye = CommanderEye(world, e, look.Family, crewEye.Value, tkb, type);   // ⭐ CE-4670 (§7d D-5) — head out only when he is
            }
            var colour = ColourOf(world, e, look.Colour);
            bool animal = VisualFamilies.IsAnimal(look.Family);   // ⭐ CE-4623
            if (animal && !HasOwnColour(world, e, look)) colour = AnimalCoats.Of(look.Family, SeedOf(world, e, hasNet));   // a seeded coat, the same on every host
            if (look.Family == VisualFamily.WheeledCar)   // ⭐ CE-1100 (§6q O-6) — every personal car its own paint
                colour = Outfits.CarColour(SeedOf(world, e, hasNet), type, HasOwnColour(world, e, look) ? colour.ToRgba() : null).ToColor();
            // ⭐ CE-1085 (§6n N-3) — a weapon a man CARRIES is drawn on his back, after every figure is placed (pass 2)
            if (look.Family == VisualFamily.WeaponSystem && CarrierOf(world, e) is { } bearer)
            {
                _carried.Add((e, bearer, look, size, colour));
                _drawn.Add(e);
                continue;
            }
            // ⭐ W2 (docs/DESIGN_Visual_Effects.md §7 VE-N/VE-O) — a destroyed VEHICLE is a near-black wreck, every part; the turret
            //   stays where it last pointed (its pose is no longer driven). A dead person is drawn by the Dead figure instead (VE-S).
            // ⭐ CE-4557 (§6ak D-1, D-2, D-7) — by its height on screen: too small to see ⇒ nothing; a few pixels ⇒ one box in its main colour; else the
            //   full figure. ⭐ A selected body and the operator's own are always Full (their boxes, labels and eyes need the pose).
            var band = BandOf(world, e, size, hasSelection);
            if (band == ScreenBand.Hidden) { BodiesHidden++; continue; }
            if (band == ScreenBand.Far) { DrawFar(world, e, look, size, type, colour, hasNet); continue; }
            BodiesFull++;
            bool deadAnimal = animal && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e);   // ⭐ CE-4623 (AR-8) — a dead animal LIES; it is never the burnt-out wreck of a vehicle
            // ⭐ CE-4634 (DESIGN_Dragon §8.2 D) — a dead DRAGON is not a burnt-out wreck either: it lies wings out, legs folded (the flying kit, still)
            bool deadDragon = look.Family == VisualFamily.Dragon && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e);
            bool wreck = look.Family is not (VisualFamily.Person or VisualFamily.Dragon) && !animal && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e);
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
            var body = Matrix4x4.CreateFromQuaternion(tf.Rotation) * Matrix4x4.CreateTranslation(tf.Position);
            var carriage = look.Family == VisualFamily.Person ? Carriage.Of(world, e, tkb) : FigureCarriage.Free;   // ⭐ CE-1081..1084 (§6m)
            // ⭐ CE-1033 S4 — wheels roll, limbs swing, a rotor turns while flying (MotionTracker); a parked helicopter's rotor stands.
            bool rotorTurning = !wreck && (look.Family == VisualFamily.Drone   // ⭐ CE-1086 — a drone's rotors run while it lives
                                           || look.Family is VisualFamily.Helicopter or VisualFamily.AttackHelicopter && IsAirborne(world, tf.Position, look.Geometry)
                                           || look.Family == VisualFamily.MineClearer && FlailActivity.IsOn(world, e)   // ⭐ CE-4624 — the flail drum spins while the flail is on
                                           || look.Family == VisualFamily.AirDefenceVehicle && RadarActivity.IsOn(world, e)   // ⭐ CE-4618 — the antenna spins while the radar radiates
                                           || look.Family == VisualFamily.Dragon && !deadDragon && DragonFlies(world, e, tf.Position, look.Geometry));   // ⭐ CE-4634 — its wings beat / glide while it flies   // ⭐ CE-4618 — the antenna spins while the radar radiates
            var animalMode = animal ? AnimalReader.ModeOf(world, e) : AnimalMode.Calm;
            if (look.Family == VisualFamily.Horse && type is 703 or 704 && animalMode == AnimalMode.Calm) animalMode = AnimalMode.Guard;   // ⭐ CE-4623 — a saddled (riding / war) horse stands head up, it does not graze
            float animalHeight = animal && look.Family is VisualFamily.Horse or VisualFamily.Goat or VisualFamily.Cat or VisualFamily.Poultry
                ? tf.Position.Z - (Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(tf.Position.X, tf.Position.Y) ?? 0f) : 0f;   // ⭐ CE-4623 — a jump's height: the legs tuck
            var motion = _motion.Advance(e, tf.Position, tf.Rotation, look.Family, size, simTime, rotorTurning, _frameClock, animalMode, animalHeight);   // ⭐ CE-1353
            if (deadDragon) motion = MotionPose.Still with { Tuck = 1f };   // ⭐ CE-4634 — dead: wings still, legs folded
            // ⭐ CE-4621 / CE-4634 — the drawn sway off the sim pose (a motorcycle's lean into a turn, a dragon's wingbeat bob): the one owner, its riders and a rider's eye read the same
            var sway = _motion.SwayOf(e);
            if (!sway.IsIdentity) body = sway * body;

            KitPart[] parts;
            KitPivots pivots = default;
            var pose = ArticulationPose.Neutral;
            bool canopy = false;
            int rounds = LauncherLoad.Full;   // ⭐ CE-4618 — the rounds left of mount 0, for the parts tied to a round (KitPart.Round); Full = every part drawn
            bool leans = false;   // ⭐ CE-1193 — only a man in his own pose (not held, not on a rope, not jumping) is drawn leaning
            // ⭐ CE-1236 (People Display U7a) — what his weapon looks like, in his hands and slung; Rifle = the figure as it was
            HeldLook inHands = HeldLook.Rifle, slung = HeldLook.Rifle;
            var heldPosture = WeaponPostureKind.AtEase;
            bool mortarFired = false;
            bool chargeBlink = false;   // ⭐ CE-1179 (U18) — a placed charge's red light, on this frame
            var heldItem = Fdp.Toolkit.Combat.Components.HeldItemKind.None;   // ⭐ CE-1169 (U7) — a phone, binoculars or a throwable shown
            if (look.Family == VisualFamily.Person && DescentFigure(world, e, look.Soldier, size, tf.Position, simTime, out canopy) is { } descending)
            {
                parts = descending;                 // ⭐ CE-1119 (§6w) — on a rope or under a canopy: the phase's figure, no gait
                motion = MotionPose.Still with { HipZ = 0.5f };
            }
            else if (look.Family == VisualFamily.Person && HeldFigure(world, e, look.Soldier, carriage, size, simTime, ref body) is { } held)
            {
                parts = held;                       // ⭐ CE-1081..1083 (§6m) — carried, dragged, seated, down: no gait of his own
                // ⭐ CE-1976 (BW-10, BW-8) — a rider with a bow (the warrior on his pony): the bow in his hands, drawn when he fires, the quiver on his back
                if (look.Soldier && carriage.Kind == CarriageKind.Seated)
                {
                    var (ridden, _) = HeldWeapons.Of(world, e, tkb, out _);
                    if (ridden is HeldLook.Bow or HeldLook.Crossbow)
                    {
                        inHands = ridden;
                        heldPosture = PostureOf(world, e);
                        parts = BlockFigure.BowHeld(parts, ridden, heldPosture);
                    }
                }
                motion = SeatedMotion(world, e, carriage, body, look.Soldier, simTime);   // ⭐ CE-4621 — astride a bike: the lean, or the turn to his aim
            }
            else if (look.Family == VisualFamily.Person)
            {
                // ⭐ WL-6c — the posture first (how AIMED he is), then the gait and the upper body from it
                (parts, float aimed, float crouchRun) = FigurePose(world, e, look.Soldier, simTime, motion);
                motion = FigureMotion(world, e, motion, look.Soldier, tf, size, simTime, aimed, crouchRun);
                motion = IdleMotion(world, e, motion, simTime);   // ⭐ CE-1308 (U19b, U19d) — the knees of a weight shift, a look about
                (parts, motion) = Burdened(world, e, carriage, look.Soldier, parts, motion);
                // ⭐ CE-1236 (People Display U7a, R-1022) — his weapon drawn as what it is, in every posture: the pistol, a machine gun, a
                //   scoped rifle, a launcher's tube (⭐ CE-1090 N-4: on the shoulder when aimed), a mortar on the ground in front of him
                //   while it is fired; a launcher or mortar he is not using rides his back (details, below)
                if (look.Soldier)
                {
                    heldPosture = PostureOf(world, e);
                    (inHands, slung) = HeldWeapons.Of(world, e, tkb, out var heldLoad);
                    if (heldPosture == WeaponPostureKind.Aimed && !HeldWeapons.IsTube(inHands) && inHands != HeldLook.Mortar
                        && Shouldering(world, e, carriage, tkb, simTime))
                        inHands = HeldLook.MissileTube;   // CE-1090 — just fired a launcher, whatever is selected now
                    mortarFired = inHands == HeldLook.Mortar && heldPosture != WeaponPostureKind.AtEase;
                    parts = BlockFigure.Held(parts, inHands, heldPosture, mortarFired ? MortarMouth : null);
                    if (HeldWeapons.IsTube(inHands) && heldPosture == WeaponPostureKind.Aimed) _shouldering.Add(e);
                    else if (!heldLoad.IsNull) _loadInHands.Add(e);   // his carried launcher is in his hands: not drawn on his back
                }
                // ⭐ CE-1169 (People Display U7) — a pocket item or a throwable in his hand: the phone to his ear (in front while he
                //   walks), the binoculars at his eyes, a stone or grenade low and ready; a gesture, a throw or a blow below wins
                (parts, heldItem) = HeldItemShown(world, e, look.Soldier, carriage, parts, motion);
                // ⭐ CE-1166 (civilian U-6, DESIGN_Civilians_And_Crowd_Control §8.7 U6-a) — what his arms show (backend's Gesture, B-6)
                parts = Gestured(world, e, look.Soldier, carriage, parts, simTime);
                parts = Thrown(world, e, look.Soldier, carriage, parts, simTime);   // ⭐ CE-1195 — the arm of a throw just made
                var unstruck = parts;
                parts = Struck(world, e, look.Soldier, carriage, parts, simTime);   // ⭐ CE-1171 — the arm of a blow just struck
                // ⭐ CE-1172 (People Display U11) — the baton man's baton in his hand, the shield man's shield on his arm (from his type until
                //   G4 makes them items); while he strikes, the swing draws the baton
                if (ReferenceEquals(parts, unstruck)) parts = PoliceKit(world, e, type, carriage, parts);
                // ⭐ CE-1179 (People Display U18) — his weapon LAID DOWN (surrender, WeaponAmmo.LaidDown) lies at his feet — after the
                //   gestures, which would sling it (a surrender is hands up)
                if (look.Soldier && carriage.Kind == CarriageKind.Free && LaidDownInHands(world, e)) parts = BlockFigure.LaidAtFeet(parts);
                leans = carriage.Kind == CarriageKind.Free;
                // ⭐ CE-1084 (§6m C-4) — just off a raised seat: drawn falling from it, landing crouched
                var (lift, jumping) = _drops.Free(e, tf.Position.Z + BlockFigure.HipFraction(StanceId.Standing) * size.Z, simTime);
                if (jumping)
                {
                    parts = BlockFigure.Parts(StanceId.Crouched, look.Soldier, PostureOf(world, e), PostureOf(world, e), 1f);
                    motion = MotionPose.Still with { HipZ = BlockFigure.HipFraction(StanceId.Crouched) };
                    body *= Matrix4x4.CreateTranslation(0f, 0f, lift);
                    JumpsDrawn++;
                    leans = false;
                }
            }
            else if (look.Family == VisualFamily.WeaponSystem)
            {
                // ⭐ CE-1087 (§6n N-1) — stowed / deployed from the replicated deploy state, blended while it is set up or packed; only a
                //   deployed weapon traverses and elevates (a laid tube or howitzer at its elevation, N-5)
                float deployed = DeploymentOf(world, e, simTime);
                var kit = WeaponKits.Blend(look.Weapon, deployed);
                (parts, pivots) = (kit.Parts, kit.Pivots);
                // ⭐ CE-1112 (§6v AD-V5) — in a vehicle's station (a technical's bed) it stands on the vehicle: no jacks, no wheels
                if (world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()
                    && world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) parts = WeaponKits.Mounted(look.Weapon, parts);
                if (deployed >= 1f) pose = LaidPose(world, e, WeaponKits.DefaultElevation(look.Weapon));
                // ⭐ CE-1179 (People Display U18) — a PLACED charge (lying, not carried, not on a vehicle) blinks red, a quarter second each second
                chargeBlink = look.Weapon == WeaponKind.Charge && ChargeLightOn(world, e, simTime);
            }
            else if (animal)
            {
                // ⭐ CE-4623 (AU-2..AU-5) — an animal: its own kit (lying when resting, wings folded when a bird is perched), the gait / head / wing from the motion pose; a DEAD one lies on its side, legs stiff
                parts = AnimalKits.For(look.Family, animalMode, look.Family == VisualFamily.Bird && motion.Wing == 0f).Parts;
                if (deadAnimal)
                {
                    var (fallT, _) = _fall.Dead(e, simTime);
                    float a = fallT * MathF.PI / 2f;
                    body = Matrix4x4.CreateRotationX(a) * Matrix4x4.CreateTranslation(0f, size.Z * 0.4f * MathF.Sin(a), size.Y * 0.5f * MathF.Sin(a)) * body;
                    motion = MotionPose.Still;
                    DeadAnimalsDrawn++;
                }
                else _fall.Alive(e, StanceId.Standing, simTime);
            }
            else if (look.Family == VisualFamily.Dragon)
            {
                // ⭐ CE-4634 (DESIGN_Dragon §8.2 K-2, W) — wings spread in flight (and dead), folded when it stands; the neck and the head turn and nod with turret 0 (the breath's
                //   aim) about the TKB's pivots (the placed kit's, cached per type: 0 B) — so the drawn mouth is where the flame leaves
                parts = DragonKits.For(rotorTurning, deadDragon).Parts;
                pivots = PlacedKit(DragonKits.Flying, type, tkb, size).Pivots;
                if (_poseOf != null && !deadDragon) pose = _poseOf(world, e);
            }
            else
            {
                var kit = KitOf(look);
                (parts, pivots) = PlacedKit(kit, type, tkb, size);   // ⭐ E4b — the turret and barrel from the TKB (VE-L)
                if (_poseOf != null && kit.IsArticulated) pose = _poseOf(world, e);
                if (look.Family == VisualFamily.AirDefenceVehicle) rounds = LauncherLoad.RoundsLeft(world, e, 0);
                if (look.Family is VisualFamily.BridgeLayer or VisualFamily.FloatingBridge && BridgeLayerLook.Of(world, e) == BridgeLayerState.NoSpan) rounds = 0;   // ⭐ CE-6502 — the span is laid: off the roof / bed   // ⭐ CE-4618 — the covers of the rounds still in the canisters
                if (look.Family == VisualFamily.SpArtillery) pose = LaidPose(world, e, SpGunElevation);   // ⭐ CE-1089 (N-5)
                if (look.Family == VisualFamily.AttackJet)   // ⭐ CE-1092 (N-8) — the pylons, and a bomb on each for every round left
                    parts = [.. parts, .. ShapeKits.AttackJetPylons, .. ShapeKits.AttackJetBombs.AsSpan(0, BombsLeft(world, e, tkb))];
                else if (JetStores.Carries(look.Family) && !wreck)   // ⭐ CE-4647 — the missiles still on the rails, the afterburner, the wingtip vortex (cached: 0 B)
                {
                    bool carries = JetStores.Left(world, e, tkb, out int irLeft, out int radarLeft);
                    bool burner = false, vortex = false;
                    if (FighterState.Read(world, e) is { } flight) { burner = flight.Throttle >= JetStores.BurnerFrom; vortex = flight.GLoad >= JetStores.VortexFromG; }
                    if (carries || burner || vortex) parts = StoresOf(look.Family, type, size, parts, carries ? irLeft : -1, radarLeft, burner, vortex);
                }
            }
            _drawn.Add(e);
            if (look.Family == VisualFamily.Person) _figureFrames[e] = (body, size.Z);   // ⭐ CE-1085 — where a carried weapon hangs
            if (carriage.Kind == CarriageKind.TeamFront) _teamPartner[e] = carriage.Partner;   // ⭐ CE-1104 — a two-man weapon hangs between them
            if (_shouldering.Contains(e)) ShoulderedDrawn++;
            if (carriage.Kind == CarriageKind.Manning) GunnersDrawn++;
            // ⭐ CE-1041 — a type with Body.Geometry: the kit's box sits where the TKB puts it relative to the reference point
            //   (an aircraft's CG), and the gear is drawn AT the TKB's contact points instead of the kit's generic gear.
            var geometry = look.Geometry;
            var kitToBody = geometry == null
                ? Matrix4x4.Identity
                : Matrix4x4.CreateTranslation(geometry.BodyCentreX, geometry.BodyCentreY, geometry.BodyCentreZ - size.Z / 2f);
            // ⭐ CE-1193 — a man leaning round a corner, sheared as the sim shears him; his eyes and muzzle tip go with it (they read kitToBody)
            if (leans) kitToBody *= LeanShear(world, e);
            if (crewEye is { } eyeKit) _eyes[e] = Vector3.Transform(eyeKit, kitToBody);   // ⭐ CE-1200 — the station puts the camera there
            int stains = look.Family == VisualFamily.Person ? Bloodied(world, e, hasNet, parts, size) : 0;   // ⭐ CE-1098 (§6p)
            // ⭐ CE-1099 (§6q) — what he wears: each part's colour and the outfit's details, from his seed
            Outfit? outfit = null;
            var headFace = FaceOn.Front;
            float camoSeed = 0f;
            if (look.Family == VisualFamily.Person)
            {
                long seed = SeedOf(world, e, hasNet);
                camoSeed = Outfits.CamoSeed(seed);
                outfit = Outfits.For(seed, type, look.Soldier, look.Child, OwnColourOf(world, e, look).ToRgba(), SideOf(world, e), look.Kit,
                                     look.Elderly, look.Sex, look.Wardrobe);
                Outfits.Details(outfit.Value, seed, _details);
                // ⭐ CE-1177 (People Display U16) — his face shows how he feels (pain, anger, fear, alarm), close up
                _expression = BodySurface.Expression(Hrot.Common.Diagnostics.Gizmos.Marks.MoodOf(world, e));
                if (look.Soldier) BlockFigure.WeaponDetails(inHands, slung, heldPosture, _details);   // ⭐ CE-1236
                if (look.Soldier && heldItem == Fdp.Toolkit.Combat.Components.HeldItemKind.None)       // ⭐ CE-1173 / CE-1174 — a toy, a baton launcher
                    BlockFigure.MarkingDetail(HeldWeapons.MarkingOf(world, HeldWeapons.OwnMountInHands(world, e)), _details);
                if (heldItem == Fdp.Toolkit.Combat.Components.HeldItemKind.Throwable
                    && ThrowableDetail(world, e, tkb, parts) is { } round) _details.Add(round);   // ⭐ CE-1169 — the round in his hand
                if (parts.Length > BlockFigure.HeadIndex) headFace = parts[BlockFigure.HeadIndex].Face;
            }
            int details = outfit != null ? _details.Count : 0;
            // ⭐ CE-4670 (VA-S1, backend — cross-lane) — the vehicle's partial damage from the ONE reader (VehicleLook): destroyed parts dark, a thrown turret
            bool vehicleBody = look.Family != VisualFamily.Person && !animal;
            _damageMask = vehicleBody ? VehicleDamageDraw.DestroyedMask(world, e) : (byte)0;
            _turretThrown = vehicleBody && wreck && VehicleDamageDraw.TurretThrown(world, e);
            if (_thermal) SetUnitHeat(world, e, size);   // ⭐ CE-1985
            DrawParts(e, parts, look, size, pivots, pose, motion, kitToBody, body, outfit, headFace, camoSeed, colour, wreck, stains, details,
                      geometry, carriage, shader, rounds);
            _damageMask = 0; _turretThrown = false;
            _unitBody = _unitHot = _airHeat; _hotSign = 0;
            // ⭐ CE-4670 (§7d) — its hatches, doors and ramps as the replicated state has them, a head out of a hatch, its lit lights (a wreck: none)
            if (vehicleBody && !wreck && look.Family != VisualFamily.WeaponSystem)
                DrawFittings(world, e, tkb != null && tkb.TryGetByType(type, out var fittingType) ? fittingType : null, parts, size, pivots, pose, motion,
                             kitToBody, body, colour, shader, simTime);
            _expression = Vector3.Zero;
            if (geometry != null && look.Family != VisualFamily.Dragon) DrawGear(world, geometry, tf.Position, body, shader, wreck);   // ⭐ CE-4634 — a dragon's "wheels" are its feet (the kit's)
            if (mortarFired) DrawMortar(e, body, colour, shader);   // ⭐ CE-1236 — after the figure: its muzzle is the shot's
            if (chargeBlink)   // ⭐ CE-1179 — the detonator's light, over the bundle
            {
                DrawPart(_cube, Matrix4x4.CreateScale(ChargeLightSize) * Matrix4x4.CreateTranslation(ChargeLightAt), ChargeLightColour, body, shader);
                ChargeLightsDrawn++;
            }
            if (canopy) DrawCanopy(world, e, body, shader, simTime);   // ⭐ CE-1119 (§6w AB-V3) — in his frame: it banks with him
            if (look.Family == VisualFamily.Person) DrawEjectionSeat(world, e, body, shader, simTime);   // ⭐ CE-1119 U-2
            // ⭐ CE-1033 S3 — where this body's labels sit: the top of the drawn box above the reference point.
            if (hasNet && world.HasComponent<NetworkIdentity>(e) && world.GetComponentRO<NetworkIdentity>(e).Value is int net && net != 0)
                _tops[net] = look.Family == VisualFamily.Person ? LabelTop(parts, size)   // ⭐ CE-1178 — above a raised sign or hands
                           : geometry != null ? geometry.BodyCentreZ + size.Z / 2f : size.Z;
            if (hasSelection && world.HasComponent<Hrot.Map.Common.Components.SelectionState>(e))
            {
                ref readonly var sel = ref world.GetComponentRO<Hrot.Map.Common.Components.SelectionState>(e);
                if (sel.IsSelected && TryGetBox(world, e, tkb, out var c, out var r, out var h))
                {
                    DrawWireBox(c, r, h * 1.06f, sel.IsPrimarySelection ? PrimarySelection : OtherSelection);
                    SelectionBoxesDrawn++;
                }
            }
            BodiesDrawn++;
        }
        DrawAmbient(world, tkb, shader, simTime);   // ⭐ CE-4031 (AQ93 §15.8) — SUMO's far people and cars, as ours
        _surface.Plain(shader);   // ⭐ CE-1101 — every later draw (and the next frame's terrain) is plain
        DrawCarried(world, shader);   // ⭐ CE-1085 — pass 2 (⭐ CE-4555 — no per-frame closure: a wreck is read inside)
        DrawRopes(world, shader, simTime);   // ⭐ CE-1119 (§6w AB-V5) — pass 3: the fast ropes, and the cut ones lying where they fell
        _batch.Flush(shader, _applySurface ??= d => _surface.Apply(LitShader.Shared, d));   // ⭐ CE-4555 (§6ak.2) — everything above, one instanced call per mesh
        if (_thermal && LitShader.IsCreated) LitShader.Shared.SetThermalSource(0, ThermalDefaultClass);   // ⭐ CE-1985 — the next lit draws by class again
        DrawLightPoints(in ctx);   // ⭐ CE-4670 (§7d D-4) — the lit lights, unlit, after the lit batch · ⭐ CE-1984 — self-luminous under a sensor view
        BodyDrawStats.Record(_batch.DrawCalls, _batch.Instances, BodiesDrawn, DoorsDrawn);
        BodyDrawStats.RecordBands(BodiesFull, BodiesFar, BodiesHidden, DoorsHidden);
        if (_recordPick) _pick.End();
        _teamPartner.Clear();
        _motion.EndFrame(_drawn);
        _motion.EndFrame(_ambientDrawn);
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
        _stances.EndFrame(_drawn);
    }

    /// <summary>⭐ AQ93 §15.8 (CE-4031) — the bodies with NO entity (<see cref="AmbientBody"/>): a person is a civilian walking upright (no
    /// stance, posture, carriage or gesture — he has no components to say otherwise), a vehicle its type's kit with its paint; each through
    /// the same look, gait and part loop as an entity. 🔒 User, 2026-10-18: *"pedestrians from sumo should be mapped to various character
    /// models we are using, definitely not a gray uniform figures please"*.</summary>
    private void DrawAmbient(EntityRepository world, ITkbDatabase? tkb, LitShader shader, double simTime)
    {
        _ambientDrawn.Clear();
        if (_ambient == null) return;
        foreach (var a in _ambient(world))
        {
            var look = LookOf(a.TkbType, tkb);
            if (look.Family == VisualFamily.Unit) continue;
            var size = look.Size;
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, a.YawRad);
            var body = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(a.Position);
            // ⭐ CE-4557 (§6ak D-1) — SUMO's far agents by the same screen-size bands
            var band = BandFor(_scale, a.Position, size);
            if (band == ScreenBand.Hidden) { BodiesHidden++; continue; }
            if (band == ScreenBand.Far)
            {
                Color farColour = look.Colour;
                Color? ownFar = a.Colour != 0 ? new Color((byte)(a.Colour >> 16), (byte)(a.Colour >> 8), (byte)a.Colour, (byte)255) : null;
                Matrix4x4 local;
                if (look.Family == VisualFamily.Person)
                {
                    var dress = Outfits.For(a.Key, a.TkbType, look.Soldier, look.Child, (ownFar ?? (look.TypeColoured ? farColour : null)).ToRgba(), ForceId.Neutral, look.Kit, look.Elderly, look.Sex, look.Wardrobe);
                    farColour = FarColour(dress, farColour);
                    local = FarBox(StanceId.Standing, look.Soldier, size);
                }
                else
                {
                    if (look.Family == VisualFamily.WheeledCar) farColour = Outfits.CarColour(a.Key, a.TkbType, (ownFar ?? (look.TypeColoured ? farColour : null)).ToRgba()).ToColor();
                    else if (ownFar is { } oc) farColour = oc;
                    var centre = look.Geometry is { } g ? new Vector3(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ) : new Vector3(0f, 0f, size.Z * 0.5f);
                    local = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(centre);
                }
                Submit(_cube, HrotToRaylib.ModelFromHrot(local * body), farColour, BodySurface.PlainData);
                BodiesFar++;
                AmbientDrawn++;
                continue;
            }
            BodiesFull++;
            var motion = _motion.Advance(a.Key, a.Position, rotation, look.Family, size, simTime);
            _ambientDrawn.Add(a.Key);
            KitPart[] parts;
            KitPivots pivots = default;
            Outfit? outfit = null;
            var headFace = FaceOn.Front;
            var colour = look.Colour;
            Color? own = a.Colour != 0 ? new Color((byte)(a.Colour >> 16), (byte)(a.Colour >> 8), (byte)a.Colour, (byte)255) : null;
            if (look.Family == VisualFamily.Person)
            {
                parts = BlockFigure.Parts(StanceId.Standing, look.Soldier, WeaponPostureKind.AtEase, WeaponPostureKind.AtEase, 1f);
                motion = Stepping(motion, StanceId.Standing);
                outfit = Outfits.For(a.Key, a.TkbType, look.Soldier, look.Child, (own ?? (look.TypeColoured ? colour : null)).ToRgba(), ForceId.Neutral,
                                     look.Kit, look.Elderly, look.Sex, look.Wardrobe);
                Outfits.Details(outfit.Value, a.Key, _details);
                if (parts.Length > BlockFigure.HeadIndex) headFace = parts[BlockFigure.HeadIndex].Face;
            }
            else
            {
                (parts, pivots) = PlacedKit(KitOf(look), a.TkbType, tkb, size);
                if (look.Family == VisualFamily.WheeledCar) colour = Outfits.CarColour(a.Key, a.TkbType, (own ?? (look.TypeColoured ? colour : null)).ToRgba()).ToColor();
                else if (own is { } c) colour = c;
            }
            var geometry = look.Geometry;
            var kitToBody = geometry == null
                ? Matrix4x4.Identity
                : Matrix4x4.CreateTranslation(geometry.BodyCentreX, geometry.BodyCentreY, geometry.BodyCentreZ - size.Z / 2f);
            DrawParts(null, parts, look, size, pivots, ArticulationPose.Neutral, motion, kitToBody, body, outfit, headFace,
                      Outfits.CamoSeed(a.Key), colour, wreck: false, stains: 0, details: outfit != null ? _details.Count : 0, geometry,
                      FigureCarriage.Free, shader);
            if (geometry != null) DrawGear(world, geometry, a.Position, body, shader);
            AmbientDrawn++;
        }
    }

    /// <summary>⭐ AQ93 §15.8 (CE-4031) — ONE part loop for a body: an entity (<paramref name="e"/> — its weapon tip, its eyes, the
    /// first-person skip) or a body with no entity (null — SUMO's far agents), each part in its outfit or paint, its face, its details and
    /// its stains (<paramref name="stains"/>/<paramref name="details"/> of the shared lists).</summary>
    private byte _damageMask;      // ⭐ CE-4670 (VA-S1) — the drawn vehicle's destroyed parts (VehicleDamageDraw), set around DrawParts
    private bool _turretThrown;    // ⭐ CE-4670 — a Catastrophic wreck: its turret drawn thrown

    private void DrawParts(Entity? e, KitPart[] parts, in TypeLook look, Vector3 size, KitPivots pivots, ArticulationPose pose, MotionPose motion,
                           Matrix4x4 kitToBody, Matrix4x4 body, Outfit? outfit, FaceOn headFace, float camoSeed, Color colour, bool wreck,
                           int stains, int details, BodyGeometryDto? geometry, in FigureCarriage carriage, LitShader shader, int rounds = LauncherLoad.Full)
    {
        for (int pi = 0; pi < parts.Length; pi++)
        {
            var part = parts[pi];
            if (part.Round >= 0 && part.Round >= rounds) continue;   // ⭐ CE-4618 — a canister's cover: gone once its round is fired
            if (look.Child && pi == BlockFigure.HeadIndex)   // ⭐ CE-1099 (O-3) — a child's head is larger, sitting on his shoulders
                part = part with
                {
                    Extent = part.Extent * ChildHead,
                    Centre = part.Face == FaceOn.Front ? part.Centre + new Vector3(0f, 0f, part.Extent.Z * (ChildHead - 1f) / 2f) : part.Centre,
                };
            if (geometry != null && part.Role == PartRole.Gear) continue;
            // ⭐ CE-1070 — a figure's rifle (its weapon part): remember where its muzzle end was drawn, for the muzzle flash.
            //   ⭐ CE-1090 — a shouldered launcher is that part too; its tip is also the CARRIED weapon's muzzle (its shot is the weapon's)
            if (e is { } ent && look.Family == VisualFamily.Person && look.Soldier && pi == BlockFigure.WeaponIndex)
            {
                var tip = WeaponTip(part, size, pivots, pose, motion, kitToBody * body);
                _weaponTips[ent] = tip;
                if (_shouldering.Contains(ent) && !carriage.Load.IsNull) _weaponTips[carriage.Load] = tip;
            }
            // ⭐ CE-1073 (§6l) — a figure's head: remember its eyes (in the body's frame) for the first-person camera, and do not
            //   draw the head of the unit seen FROM it (the camera sits inside it)
            if (e is { } who && look.Family == VisualFamily.Person && pi == BlockFigure.HeadIndex)
            {
                _eyes[who] = EyeLocal(part, size, pivots, pose, motion, kitToBody);
                if (who == FirstPerson && !ShowFirstPersonHead) continue;
            }
            var placed = part.BodyTransform(size, pivots, pose, motion) * kitToBody * body;
            if (_turretThrown && VehicleDamageDraw.Thrown(part))   // ⭐ CE-4670 (VA-S1) — a Catastrophic kill: the turret and gun thrown off the hull
                placed = part.BodyTransform(size, pivots, pose, motion) * VehicleDamageDraw.ThrownTransform(size) * kitToBody * body;
            var model = HrotToRaylib.ModelFromHrot(placed);
            var mesh = part.Shape switch { PartShape.Cylinder => _cylinder, PartShape.Cone => _cone, _ => _cube };
            bool partWrecked = wreck || (_damageMask != 0 && VehicleDamageDraw.Destroyed(_damageMask, part));   // ⭐ CE-4670 — a destroyed part is drawn dark
            var tint = outfit is { } dress && Outfits.ColourOfPart(dress, pi) is { } worn ? worn.ToColor() : PartColour(part.Colour, colour, partWrecked);   // ⭐ CE-1099
            if (_thermal)   // ⭐ CE-1985 — every part at the unit's °C; the hot end is a slab on the hull (below), not whole parts (a hull is one long box)
            {
                _submitHeat = _unitBody;
                if (_hotSign != 0 && part.Shape == PartShape.Box)
                {
                    var ext = part.Extent * size;
                    float volume = ext.X * ext.Y * ext.Z;
                    if (volume > _hullVolume) { _hullVolume = volume; _hullCentre = part.Centre * size; _hullExtent = ext; _hullKitToBody = kitToBody; _hullBody = body; }
                }
            }
            if (look.Toy && !wreck) tint = part.Colour == ColourRole.Dark ? new Color(200, 90, 15, 255) : BlockFigure.ToyOrange;   // ⭐ CE-1173 (U12)
            // ⭐ CE-1101 (§6r) — the shader paints his camouflage or his face; ⭐ CE-4555 (§6ak D-4, D-5) — as this instance's data, into the batch
            Submit(mesh, model, tint, Paint(outfit, pi, part.Face, camoSeed));
            PartsDrawn++;
            if (part.Face != FaceOn.None && !_thermal) DrawFace(part.Face, placed, look.Soldier, colour, shader, outfit);   // ⭐ CE-1097 (§6o) · CE-1985: no painted face in the heat picture
            for (int k = 0; k < details; k++)   // ⭐ CE-1099 (§6q) — the outfit's details on this part
                if (_details[k].Part == pi)
                {
                    var d = _details[k];
                    var local = Matrix4x4.CreateScale(d.Extent) * Matrix4x4.CreateTranslation(d.Centre);
                    if (d.Turned) local *= BlockFigure.FaceFrame(headFace);
                    Submit(_cube, HrotToRaylib.ModelFromHrot(local * placed), d.Colour.ToColor(), BodySurface.PlainData);
                    DetailsDrawn++;
                }
            for (int k = 0; k < stains; k++)   // ⭐ CE-1098 (§6p) — the blood spots on this part
                if (_stains[k].Part == pi)
                {
                    Submit(_cube, HrotToRaylib.ModelFromHrot(BlockFigure.StainDetail(_stains[k], placed)),
                           _stains[k].Char ? Charred : _stains[k].Fresh ? BloodFresh : BloodDark, BodySurface.PlainData);
                    StainsDrawn++;
                }
        }
        // ⭐ CE-1985 (§5b) — the engine's hot end: a slab over the hull's front or rear third, at the hot end's °C
        if (_thermal && _hotSign != 0 && _hullVolume > 0f)
        {
            var slab = Matrix4x4.CreateScale(_hullExtent.X * 0.3f, _hullExtent.Y * 0.92f, _hullExtent.Z * 0.12f)
                     * Matrix4x4.CreateTranslation(_hullCentre.X + _hotSign * _hullExtent.X * 0.34f, _hullCentre.Y, _hullCentre.Z + _hullExtent.Z * 0.47f);
            _submitHeat = _unitHot;
            Submit(_cube, HrotToRaylib.ModelFromHrot(slab * _hullKitToBody * _hullBody), default, BodySurface.PlainData);
        }
        _hullVolume = 0f;
        _submitHeat = _airHeat;
    }

    private float _hullVolume;
    private Vector3 _hullCentre, _hullExtent;
    private Matrix4x4 _hullKitToBody, _hullBody;

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
    public const float MaxAimTwist = UpperBody.MaxAimTwist, MaxAimPitch = UpperBody.MaxAimPitch;   // ⭐ CE-11002 (RR-2) — the shared rule

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
        motion = Stepping(motion, stance);
        var (hipFrom, hipTo, hipT) = DrawnStance(world, e, stance, simTime);   // ⭐ CE-1348 — the hips go down with the blend, not ahead of it
        if (hipT < 1f && hipFrom != hipTo)
            motion = motion with { HipZ = BlockFigure.HipFraction(hipFrom) + (BlockFigure.HipFraction(hipTo) - BlockFigure.HipFraction(hipFrom)) * hipT };
        if (stance == StanceId.Crouched && crouchRun > 0f)
            motion = motion with
            {
                HipZ = motion.HipZ + (BlockFigure.CrouchRunHipFraction - motion.HipZ) * crouchRun,
                Lean = motion.Lean + BlockFigure.CrouchRunLean * crouchRun * (1f - AimedNow(soldier ? PostureOf(world, e) : WeaponPostureKind.AtEase)),
                Swing = motion.Swing * (1f - BlockFigure.CrouchRunStepCut * crouchRun),
            };
        if (!soldier) return motion with { HeadTurn = _postures.Head(e, HeadTurnOf(world, e, 0f), simTime) };   // ⭐ CE-1204 (LK-5)
        if (stance == StanceId.Standing && aimed > 0f)
            motion = motion with
            {
                HipZ = (0.90f - BlockFigure.AthleticDrop * aimed) / 1.8f,
                Lean = motion.Lean + AthleticLean * aimed,
                Swing = motion.Swing * (1f - GlideStepCut * aimed),
            };
        if (!TryAim(world, e, tf, size, stance, out float twist, out float pitch)) twist = pitch = 0f;
        var (t, p) = _postures.Aim(e, twist, pitch, simTime);
        return motion with { Twist = t, Lean = motion.Lean - p, HeadTurn = _postures.Head(e, HeadTurnOf(world, e, t), simTime) };
    }

    /// <summary>
    /// ⭐ <c>CE-1204</c> (docs/DESIGN_Entity_Control.md §8l LK-5, R-1017) — the drawn head's turn off the torso: where his eyes point off his
    /// body (<see cref="Fdp.Toolkit.Perception.Look.LookOf.HeadYaw"/>, replicated) less the torso's drawn <paramref name="drawnTwist"/>, within
    /// his neck. So every node shows where he looks — an aim, an ordered look, a glance (LD-3), an operator's free look. 0 with no look.
    /// </summary>
    public static float HeadTurnOf(EntityRepository world, Entity e, float drawnTwist) => UpperBody.HeadTurnOf(world, e, drawnTwist);   // ⭐ CE-11002 (RR-2)

    /// <summary>⭐ WL-6c — the gait's legs for <paramref name="stance"/>: the hips at the stance's height, each shin bent back as its leg
    /// swings through. ⭐ CE-4031 — shared by an entity's figure and a body with no entity (<see cref="DrawAmbient"/>).</summary>
    public static MotionPose Stepping(MotionPose motion, StanceId stance)
    {
        float swingThrough = MathF.Cos(motion.GaitPhase);   // > 0: the left leg is coming forward, < 0: the right one
        float lift = motion.Stride * KneeLift;
        return motion with
        {
            HipZ = BlockFigure.HipFraction(stance),
            KneeFlexL = lift * MathF.Max(0f, swingThrough),
            KneeFlexR = lift * MathF.Max(0f, -swingThrough),
        };
    }

    /// <summary>The aim of an AIMED unit relative to its body: turn (counter-clockwise) and pitch (up), both clamped. False when it is
    /// not aimed or aims at nothing it can place.</summary>
    public static bool TryAim(EntityRepository world, Entity e, in SimTransform tf, Vector3 size, StanceId stance, out float twist, out float pitch)
        => UpperBody.TryAim(world, e, in tf, size.Z, stance, out twist, out pitch);   // ⭐ CE-11002 (RR-2) — the shared rule

    /// <summary>
    /// ⭐ <c>CE-1193</c> (backend's <c>Lean</c>, <c>CE-3257</c> §13 LN-2; behaviors' poses note CB-21) — a man LEANING round a corner is drawn
    /// exactly as the sim leans him: a SHEAR about his feet across his own left axis (body frame +Y), a point <c>h</c> above his feet moving
    /// <see cref="Fdp.Toolkit.Combat.Leans.ShiftOf(Fdp.ModuleHost.Abstractions.ISimulationView, Entity)"/>·h / eye sideways (⭐ CE-2371: his own
    /// depth, 0.4 m when unset), eye = the sim's eye height for his stance — so the head is drawn where
    /// he sees round the corner, is seen and is hit. Identity when he does not lean or lies prone (the sim ignores it then).
    /// ⛔ <c>MotionPose.Lean</c> is a FORWARD pitch; a sideways lean is this shear, applied after the pose (in body space).
    /// </summary>
    public static Matrix4x4 LeanShear(EntityRepository world, Entity e)
    {
        var stance = LogicalStance.Of(world, e);
        int sign = Fdp.Toolkit.Combat.Leans.SignOf(world, e, stance);
        if (sign == 0) return Matrix4x4.Identity;
        float eye = stance == StanceId.Crouched ? Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightCrouched
                                                : Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding;
        var m = Matrix4x4.Identity;
        // ⭐ CE-2371 (AQ104 G, R-2079; backend CE-3321) — HIS depth (a trained man leans further), the sim's own reader: 0.4 m when unset
        m.M32 = sign * Fdp.Toolkit.Combat.Leans.ShiftOf(world, e) / eye;   // row vectors: y' = y + k·z
        return m;
    }

    /// <summary>
    /// ⭐ <c>CE-1172</c> (docs/DESIGN_People_Display.md §5 U11) — the police kit a man of a security type carries by his TYPE (G4 has not made
    /// them items): the baton man (<c>SecurityBaton</c>) a baton in his right hand, the shield man (<c>SecurityShield</c>) a riot shield on his
    /// left arm. Standing or crouched and free, not cowering; otherwise the parts as they are.
    /// </summary>
    public static KitPart[] PoliceKit(EntityRepository world, Entity e, long type, in FigureCarriage carriage, KitPart[] parts)
    {
        bool baton = type == Hrot.Core.Tkb.CivilianTkbCatalog.TkbSecurityBaton, shield = type == Hrot.Core.Tkb.CivilianTkbCatalog.TkbSecurityShield;
        if ((!baton && !shield) || carriage.Kind != CarriageKind.Free || Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e)) return parts;
        var stance = LogicalStance.Of(world, e);
        if (stance is not (StanceId.Standing or StanceId.Crouched)) return parts;
        return baton ? BlockFigure.BatonInHand(parts) : BlockFigure.ShieldOnArm(parts, stance == StanceId.Crouched);
    }

    /// <summary>
    /// ⭐ <c>CE-1169</c> (docs/DESIGN_People_Display.md §5 U7) — what a man HOLDS that is not his weapon: the replicated
    /// <c>WeaponPosture.HeldItem</c> when it is a phone, binoculars or a throwable (<see cref="BlockFigure.HeldItemArms"/>). Standing or
    /// crouched, free, alive and not cowering; otherwise the parts as they are and <c>None</c>. Walking = his gait is moving.
    /// </summary>
    public static (KitPart[] Parts, Fdp.Toolkit.Combat.Components.HeldItemKind Item) HeldItemShown(EntityRepository world, Entity e, bool soldier,
        in FigureCarriage carriage, KitPart[] parts, in MotionPose motion)
    {
        var none = Fdp.Toolkit.Combat.Components.HeldItemKind.None;
        var item = Fdp.Toolkit.Combat.HeldItems.Of(world, e);
        if (!BlockFigure.IsPocketOrThrowable(item) || carriage.Kind != CarriageKind.Free) return (parts, none);
        if (Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e) || Cowers(world, e, soldier)) return (parts, none);
        var stance = LogicalStance.Of(world, e);
        if (stance is not (StanceId.Standing or StanceId.Crouched)) return (parts, none);
        return (BlockFigure.HeldItemArms(parts, item, stance == StanceId.Crouched, soldier, walking: motion.Moving > 0.1f), item);
    }

    /// <summary>⭐ <c>CE-1169</c> — the round a throwable's hand holds, as its munition looks (its <c>Munition.Visual</c>: a grey stone,
    /// a dark green grenade, a canister); null when his hands hold none.</summary>
    public static PartDetail? ThrowableDetail(EntityRepository world, Entity e, ITkbDatabase? tkb, KitPart[] parts)
    {
        Fdp.Toolkit.Tkb.Domain.MunitionVisualDto? visual = null;
        bool holds = Fdp.Toolkit.Combat.PersonHands.Holding(world, e, ammo => (visual = Hrot.Core.Tkb.MunitionTkbCatalog.VisualOf(tkb, ammo)) != null);
        if (!holds) return null;
        var colour = ParseHex(visual?.ColorHex) ?? new Color(120, 118, 112, 255);
        return BlockFigure.ThrowableDetail(parts, visual?.Length ?? 0.08f, visual?.Diameter ?? 0.07f, colour);
    }

    /// <summary>
    /// ⭐ <c>CE-1195</c> — a man who has just THROWN (a shot from his hands mount, <c>PersonHands</c>, within
    /// <see cref="BlockFigure.ThrowSeconds"/> — the shot the effects spawner keeps in <c>RecentShots</c>) is drawn mid-throw
    /// (<see cref="BlockFigure.ThrowArms"/>). Standing or crouched, free, alive and not cowering; otherwise the parts as they are.
    /// 🔒 User, live, 2026-10-19: <i>"there is no animation for person throwing stone"</i>.
    /// </summary>
    public static KitPart[] Thrown(EntityRepository world, Entity e, bool soldier, in FigureCarriage carriage, KitPart[] parts, double simTime)
    {
        if (carriage.Kind != CarriageKind.Free || Hrot.UI.Common.Effects.RecentShots.Of(world) is not { } shots) return parts;
        if (!shots.TryGet(e, out int mount, out double at) || simTime < at || simTime - at > BlockFigure.ThrowSeconds) return parts;
        if (mount != Fdp.Toolkit.Combat.PersonHands.MountIndex(world, e)) return parts;
        if (Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e) || Cowers(world, e, soldier)) return parts;
        var stance = LogicalStance.Of(world, e);
        if (stance is not (StanceId.Standing or StanceId.Crouched)) return parts;
        return BlockFigure.ThrowArms(parts, stance == StanceId.Crouched, soldier, (float)((simTime - at) / BlockFigure.ThrowSeconds));
    }

    /// <summary>
    /// ⭐ <c>CE-1171</c> (docs/DESIGN_People_Display.md §5 U10) — a man who has just STRUCK a blow (a <c>StrikeNotification</c> within
    /// <see cref="BlockFigure.StrikeSeconds"/>, kept in <c>StrikeTracker</c>) is drawn mid-swing with what he struck with
    /// (<see cref="BlockFigure.StrikeArms"/>). Standing or crouched, free, alive and not cowering; otherwise the parts as they are.
    /// 🔒 User, live, 2026-10-19: <i>"a person hit by melee simply goes down but there is no sign of why (weird)"</i>.
    /// </summary>
    public static KitPart[] Struck(EntityRepository world, Entity e, bool soldier, in FigureCarriage carriage, KitPart[] parts, double simTime)
    {
        if (carriage.Kind != CarriageKind.Free || Hrot.UI.Common.Effects.StrikeTracker.Of(world) is not { } strikes) return parts;
        if (!strikes.StruckWithin(e, simTime, BlockFigure.StrikeSeconds, out var weapon, out double at)) return parts;
        if (Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e) || Cowers(world, e, soldier)) return parts;
        var stance = LogicalStance.Of(world, e);
        if (stance is not (StanceId.Standing or StanceId.Crouched)) return parts;
        return BlockFigure.StrikeArms(parts, stance == StanceId.Crouched, soldier, weapon, (float)((simTime - at) / BlockFigure.StrikeSeconds));
    }

    /// <summary>
    /// ⭐ CE-1166 (civilian U-6, docs/DESIGN_Civilians_And_Crowd_Control.md §8.7 U6-a) — a free man's arms showing his replicated
    /// <c>Gesture</c> (backend B-6, <c>Gestures.Of</c>) over his standing or crouched figure (<see cref="BlockFigure.GestureArms"/>). The
    /// arms already have an owner — and nothing changes — when he is dead, cowering, prone, or carrying / dragging / on a team load.
    /// </summary>
    /// <remarks>⭐ CE-1175 (docs/DESIGN_People_Display.md §5 U14) — animated on <see cref="GestureClock"/>: <paramref name="simTime"/> plus
    /// his seed's offset, so a crowd does not pump its fists in step.</remarks>
    public static KitPart[] Gestured(EntityRepository world, Entity e, bool soldier, in FigureCarriage carriage, KitPart[] parts, double simTime = 0)
    {
        var kind = Fdp.Toolkit.Combat.Gestures.Of(world, e);
        if (kind == Fdp.Toolkit.Combat.GestureKind.None || carriage.Kind != CarriageKind.Free) return parts;
        if (Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e) || Cowers(world, e, soldier)) return parts;
        var stance = LogicalStance.Of(world, e);
        if (stance is not (StanceId.Standing or StanceId.Crouched)) return parts;
        float t = GestureClock(world, e, simTime);
        // ⭐ CE-1308 (People Display §7d U19; behaviors' CE-2368) — an IDLE mood (smoking, shifting weight, a stretch, looking about) is
        //   drawn by its own stateless function on the same clock; ⛔ cast to an arm pose it fell to the wind-up (behaviors' guard, replaced)
        if (Fdp.Toolkit.Combat.Gestures.IsIdle(kind)) return BlockFigure.IdleArms(parts, kind, crouched: stance == StanceId.Crouched, soldier, t);
        var gesture = (BlockFigure.ArmGesture)(byte)kind;   // U6-b: the same six, byte for byte
        return BlockFigure.GestureArms(parts, gesture, crouched: stance == StanceId.Crouched, soldier, t);
    }

    /// <summary>⭐ <c>CE-1308</c> (U19b) — how far the free knee bends while he shifts his weight (rad).</summary>
    public const float ShiftKnee = 0.35f;

    /// <summary>⭐ <c>CE-1308</c> (U19d) — how far and how slowly the head sweeps while he looks about (rad, s).</summary>
    public const float LookAboutSweep = 0.52f, LookAboutCycle = 12f;

    /// <summary>
    /// ⭐ <c>CE-1308</c> (docs/DESIGN_People_Display.md §7d U19b, U19d) — the motion half of an IDLE mood, on his gesture clock: SHIFTING
    /// WEIGHT bends the free knee (the leg the upper body sways away from — <see cref="BlockFigure.ShiftOf"/>), fading while he walks;
    /// LOOKING ABOUT sweeps the head ±30° on a slow cycle — ⚠ only for a man whose eyes are not modelled at all (no <c>LookState</c>): since
    /// behaviors' idle glance (<c>RestlessSystem.Glance</c>, AQ103 I) his real eyes look about, and the drawn head shows where he really
    /// looks (Entity Control LK-5). ⛔ Not "while <c>HeadYaw</c> is 0" (U19d as first built): the glance passes straight ahead, and a drawn
    /// sweep would cut in there and disagree with his eyes.
    /// </summary>
    public static MotionPose IdleMotion(EntityRepository world, Entity e, MotionPose motion, double simTime)
    {
        var kind = Fdp.Toolkit.Combat.Gestures.Of(world, e);
        if (!Fdp.Toolkit.Combat.Gestures.IsIdle(kind) || Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, e)) return motion;
        float t = GestureClock(world, e, simTime);
        if (kind == Fdp.Toolkit.Combat.GestureKind.IdleShiftWeight)
        {
            float sway = BlockFigure.ShiftOf(t) / BlockFigure.ShiftSway, still = 1f - Math.Clamp(motion.Moving, 0f, 1f);
            return motion with
            {
                KneeFlexR = MathF.Max(motion.KneeFlexR, ShiftKnee * still * MathF.Max(0f, sway)),    // the weight on his left: the right knee rests
                KneeFlexL = MathF.Max(motion.KneeFlexL, ShiftKnee * still * MathF.Max(0f, -sway)),
            };
        }
        if (kind == Fdp.Toolkit.Combat.GestureKind.IdleLookAbout && !HasEyes(world, e))
            return motion with { HeadTurn = motion.HeadTurn + LookAboutSweep * MathF.Sin(2f * MathF.PI * t / LookAboutCycle) };
        return motion;
    }

    /// <summary>⭐ U19d — his eyes are modelled: a look was written for him once (<c>LookState</c> is added on the first look and kept, replicated).</summary>
    private static bool HasEyes(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Look.LookState>() && world.HasComponent<Fdp.Toolkit.Perception.Look.LookState>(e);

    /// <summary>⭐ CE-1175 (U14) — a man's gesture clock in seconds: sim time plus up to 10 s from his seed (the same on every node).
    /// Wrapped to an hour so the float keeps its precision.</summary>
    public static float GestureClock(EntityRepository world, Entity e, double simTime)
    {
        long seed = SeedOf(world, e, world.IsComponentTypeRegistered<NetworkIdentity>());
        double offset = (seed * 0.6180339887498949) % 1.0 * 10.0;
        return (float)((simTime + offset) % 3600.0);
    }

    /// <summary>
    /// ⭐ CE-1164 (civilian U-3, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U3-a) — true when <paramref name="e"/> is drawn COWERING
    /// (<see cref="BlockFigure.Cower"/>): an unarmed man whose <c>EffectState</c> carries the CivilianHsm's cower mode (backend F-3,
    /// <c>Effects.IsCowering</c>). A soldier never cowers in the drawing; death and the knock-down (a carriage) win over it.
    /// </summary>
    public static bool Cowers(EntityRepository world, Entity e, bool soldier) => !soldier && Fdp.Toolkit.Combat.Effects.IsCowering(world, e);

    private static float AimedNow(WeaponPostureKind posture) => posture == WeaponPostureKind.Aimed ? 1f : 0f;

    /// <returns>The parts, how AIMED he is standing (0..1, the posture blend) — the athletic stance and the glide follow it — and
    /// ⭐ CE-1064 how much a CROUCHED figure is running (0..1, its stride).</returns>
    /// <summary>⭐ <c>CE-1348</c> — the figure's drawn stance this frame (<see cref="StanceBlend"/>): idempotent within a frame, so the pose
    /// and the hip height read the same blend.</summary>
    private (StanceId From, StanceId To, float T) DrawnStance(EntityRepository world, Entity e, StanceId target, double simTime)
    {
        StanceStatus? muscle = world.IsComponentTypeRegistered<StanceStatus>() && world.HasComponent<StanceStatus>(e)
            ? world.GetComponentRO<StanceStatus>(e) : null;
        return _stances.Of(e, target, simTime, muscle);
    }

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
        if (Cowers(world, e, soldier)) return (BlockFigure.Cower, 0f, 0f);
        // ⭐ CE-1348 — the drawn stance moves smoothly to the logical one (StanceBlend), following the muscle's blend where it runs one
        var (stanceFrom, stanceTo, stanceT) = DrawnStance(world, e, target, simTime);
        if (stanceT < 1f && stanceFrom != stanceTo)
            return (BlockFigure.Blend(stanceFrom, stanceTo, stanceT, soldier, postureFrom, postureTo, postureT), 0f, 0f);
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

    // ── ⭐ CE-1119 (docs/DESIGN_Map_3D_Mode.md §6w) — airborne: a man on a rope or under a canopy, the ropes ─────────────────────────

    /// <summary>⭐ CE-1119 — descending figures, canopies, ropes hanging and cut ropes lying, drawn by the last frame (rails).</summary>
    public int DescentsDrawn { get; private set; }
    public int CanopiesDrawn { get; private set; }
    public int RopesDrawn { get; private set; }
    public int CutRopesDrawn { get; private set; }

    /// <summary>How long a cut rope lies where it fell (s, sim time).</summary>
    public const float CutRopeSeconds = 30f;

    private static readonly Color RoundCanopy = new(92, 100, 64, 255), RamAirCanopy = new(58, 62, 64, 255);
    private static readonly Color Line = new(176, 174, 160, 255), RopeColour = new(66, 60, 44, 255);
    private const float LineThickness = 0.025f;

    private readonly HashSet<(Entity Carrier, int Index)> _ropeUsers = new();
    private readonly Dictionary<(Entity Carrier, int Index), (Vector3 Top, Vector3 Bottom, float Length)> _ropesOut = new();
    private readonly Dictionary<(Entity Carrier, int Index), (Vector3 Top, Vector3 Bottom, float Length)> _ropesNow = new();
    private readonly List<(Vector3 Foot, Vector3 Top, float Length, double At)> _cutRopes = new();
    private readonly List<CanopyPiece> _canopy = new();
    private readonly List<(Vector3 A, Vector3 B)> _lines = new();

    private static bool TryDescent(EntityRepository world, Entity e, out Fdp.Toolkit.Motion.Descent d)
    {
        d = default;
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Descent>() || !world.HasComponent<Fdp.Toolkit.Motion.Descent>(e)) return false;
        d = world.GetComponentRO<Fdp.Toolkit.Motion.Descent>(e);
        return d.Kind != Fdp.Toolkit.Motion.DescentKind.None;
    }

    /// <summary>
    /// ⭐ CE-1119 (§6w AB-V2, AB-V6) — the figure of a man coming down (<see cref="DescentDraw.Figure"/>), null when he has none (no
    /// <c>Descent</c>, or off the rope). While he comes down the jump tracker is told he RIDES at his hips' height, so the end of the descent
    /// is never drawn as a jump from the seat he left (§6m C-4 amended). <paramref name="canopy"/>: a canopy is out over him.
    /// </summary>
    private KitPart[]? DescentFigure(EntityRepository world, Entity e, bool soldier, Vector3 size, Vector3 at, double simTime, out bool canopy)
    {
        canopy = false;
        if (!TryDescent(world, e, out var d)) return null;
        if (d.Kind == Fdp.Toolkit.Motion.DescentKind.FastRope && d.Phase is not (Fdp.Toolkit.Motion.DescentPhase.Landed or Fdp.Toolkit.Motion.DescentPhase.Falling))
            _ropeUsers.Add((d.Anchor, d.AnchorIndex));
        _drops.Riding(e, at.Z + BlockFigure.HipFraction(StanceId.Standing) * size.Z, simTime);   // even off the rope (Landed): no jump from the seat
        float open = Fdp.Toolkit.Motion.ParachuteModel.Of(world, e, d.Canopy == Fdp.Toolkit.Motion.CanopyKind.NoCanopy ? Fdp.Toolkit.Motion.CanopyKind.Round : d.Canopy).OpenSeconds;
        var parts = DescentDraw.Figure(d, simTime, soldier, open);
        if (parts == null) return null;
        _fall.Alive(e, StanceId.Standing, simTime);
        _down.Alive(e, StanceId.Standing, simTime);
        canopy = DescentDraw.Opening(d, simTime, open) >= 0f;
        DescentsDrawn++;
        return parts;
    }

    /// <summary>⭐ CE-1119 (§6w AB-V3, AB-V4) — his canopy and its lines, in his frame <paramref name="body"/> (so it banks with him).</summary>
    private void DrawCanopy(EntityRepository world, Entity e, Matrix4x4 body, LitShader shader, double simTime)
    {
        if (!TryDescent(world, e, out var d)) return;
        float open = Fdp.Toolkit.Motion.ParachuteModel.Of(world, e, d.Canopy).OpenSeconds;
        DescentDraw.Canopy(d.Canopy, DescentDraw.Opening(d, simTime, open), DescentDraw.Collapse(d, simTime), _canopy, _lines);
        if (_canopy.Count == 0) return;
        var colour = d.Canopy == Fdp.Toolkit.Motion.CanopyKind.RamAir ? RamAirCanopy : RoundCanopy;
        foreach (var piece in _canopy)
            DrawPart(piece.Shape switch { PartShape.Cone => _cone, PartShape.Cylinder => _cylinder, _ => _cube }, piece.Local, colour, body, shader);
        foreach (var (a, b) in _lines) DrawPart(_cube, DescentDraw.Stick(a, b, LineThickness), Line, body, shader);
        CanopiesDrawn++;
    }

    /// <summary>⭐ <c>CE-1119</c> U-2 — the ejection seat's grey and its rocket's flame (unlit-bright: an emitter, VE's rule).</summary>
    public static readonly Color EjectionSeatColour = new(58, 60, 62, 255), RocketFlame = new(255, 170, 50, 255);

    /// <summary>Ejection seats drawn by the last frame (rails).</summary>
    public int EjectionSeatsDrawn { get; private set; }

    private readonly List<(PartShape Shape, Matrix4x4 Local, bool Flame)> _seat = new(4);

    /// <summary>⭐ <c>CE-1119</c> U-2 (§6w) — an ejecting man's seat and its flame, in his frame (<see cref="DescentDraw.EjectionSeat"/>).</summary>
    private void DrawEjectionSeat(EntityRepository world, Entity e, Matrix4x4 body, LitShader shader, double simTime)
    {
        if (!TryDescent(world, e, out var d) || d.Kind != Fdp.Toolkit.Motion.DescentKind.Ejection) return;
        DescentDraw.EjectionSeat(d, simTime, _seat);
        if (_seat.Count == 0) return;
        foreach (var (shape, local, flame) in _seat)
            DrawPart(shape == PartShape.Cone ? _cone : _cube, local, flame ? RocketFlame : EjectionSeatColour, body, shader);
        EjectionSeatsDrawn++;
    }

    /// <summary>
    /// ⭐ CE-1119 (§6w AB-V5) — the fast ropes: every rope of a carrier that is ROPING (its replicated <c>NavigationStatus</c>), or that a man
    /// on it names, hangs from <see cref="Fdp.Toolkit.Motion.DescentRules.TryAnchor"/> — the point the slide holds him under — straight down to
    /// the surface below, its spare length coiled at its foot. A rope that stops hanging was CUT: it lies in a heap where it fell for
    /// <see cref="CutRopeSeconds"/>.
    /// </summary>
    private EntityRepository? _ropeQueryWorld;
    private EntityQuery? _ropeQuery;

    private void DrawRopes(EntityRepository world, LitShader shader, double simTime)
    {
        _ropesNow.Clear();
        Fdp.Toolkit.World.IWorldQuery? query = null;   // ⭐ CE-4555 — fetched only when a rope needs it (WorldQuery.Of builds a view each call)
        void Hang(Entity carrier, int index)
        {
            if (_ropesNow.ContainsKey((carrier, index))) return;
            query ??= Fdp.Toolkit.World.WorldQuery.Of(world);
            var probe = new Fdp.Toolkit.Motion.Descent { Anchor = carrier, AnchorIndex = (byte)index };
            if (!Fdp.Toolkit.Motion.DescentRules.TryAnchor(world, probe, out var top, out _, out _)) return;
            float surface = query != null ? Fdp.Toolkit.Motion.DescentRules.HighestSurface(query, top.X, top.Y) : 0f;
            if (surface > top.Z) surface = query?.GroundHeightAt(top.X, top.Y) ?? 0f;
            float length = Fdp.Toolkit.Motion.DescentRules.RopeLength(world, carrier);
            var (bottom, _) = DescentDraw.Rope(top, surface, length);
            _ropesNow[(carrier, index)] = (top, bottom, length);
        }
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationStatus>())
        {
            if (!ReferenceEquals(world, _ropeQueryWorld))   // ⭐ CE-4555 — built once per world (it was built every frame)
            {
                _ropeQueryWorld = world;
                _ropeQuery = world.Query().With<Fdp.Toolkit.Navigation.NavigationStatus>().With<SimTransform>().Build();
            }
            foreach (var c in _ropeQuery!)
            {
                if (world.GetComponentRO<Fdp.Toolkit.Navigation.NavigationStatus>(c).Phase != Fdp.Toolkit.Navigation.NavigationPhase.Roping) continue;
                int ropes = Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, c, out var t) && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>() is { RopeAnchors.Count: > 0 } def
                    ? def.RopeAnchors.Count : 2;
                for (int k = 0; k < ropes; k++) Hang(c, k);
            }
        }
        foreach (var (carrier, index) in _ropeUsers) Hang(carrier, index);

        foreach (var (top, bottom, length) in _ropesNow.Values)
        {
            DrawPart(_cube, DescentDraw.Stick(top, bottom, DescentDraw.RopeThickness), RopeColour, Matrix4x4.Identity, shader);
            float spare = length - (top.Z - bottom.Z);
            if (spare > 0.5f)   // the spare length coiled at its foot
                DrawPart(_cylinder, Matrix4x4.CreateScale(0.6f, 0.6f, MathF.Min(0.25f, 0.02f * spare)) * Matrix4x4.CreateTranslation(bottom), RopeColour, Matrix4x4.Identity, shader);
            RopesDrawn++;
        }
        // a rope that hung last frame and does not now was cut: it falls in a heap under where it hung
        foreach (var (key, rope) in _ropesOut)
            if (!_ropesNow.ContainsKey(key))
            {
                query ??= Fdp.Toolkit.World.WorldQuery.Of(world);
                float surface = query != null ? Fdp.Toolkit.Motion.DescentRules.HighestSurface(query, rope.Top.X, rope.Top.Y) : rope.Bottom.Z;
                if (surface > rope.Top.Z) surface = rope.Bottom.Z;
                _cutRopes.Add((new Vector3(rope.Top.X, rope.Top.Y, surface), rope.Top, rope.Length, simTime));
            }
        _ropesOut.Clear();
        foreach (var (key, rope) in _ropesNow) _ropesOut[key] = rope;
        for (int i = _cutRopes.Count - 1; i >= 0; i--)   // ⭐ CE-4555 — a loop, not RemoveAll with a capturing lambda (a closure every frame)
            if (simTime < _cutRopes[i].At || simTime - _cutRopes[i].At > CutRopeSeconds) _cutRopes.RemoveAt(i);
        foreach (var (foot, _, length, _) in _cutRopes)   // lying out in a loose S from where it hung, the rest in a low heap at its end
        {
            float run = MathF.Min(length * 0.5f, 8f);
            var lift = new Vector3(0f, 0f, 0.03f);
            var p1 = foot + new Vector3(run * 0.5f, 0.7f, 0f);
            var p2 = foot + new Vector3(run, -0.3f, 0f);
            DrawPart(_cube, DescentDraw.Stick(foot + lift, p1 + lift, DescentDraw.RopeThickness), RopeColour, Matrix4x4.Identity, shader);
            DrawPart(_cube, DescentDraw.Stick(p1 + lift, p2 + lift, DescentDraw.RopeThickness), RopeColour, Matrix4x4.Identity, shader);
            DrawPart(_cylinder, Matrix4x4.CreateScale(0.7f, 0.7f, 0.08f) * Matrix4x4.CreateTranslation(p2), RopeColour, Matrix4x4.Identity, shader);
            CutRopesDrawn++;
        }
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
            case CarriageKind.OnStretcher when world.HasComponent<SimTransform>(carriage.Holder):
            {
                // ⭐ CE-1104 (§6t T-b1) — on the litter between the pair, at their hands, facing the front man's way
                ref readonly var front = ref world.GetComponentRO<SimTransform>(carriage.Holder);
                SimTransform? rear = !carriage.Partner.IsNull && world.IsAlive(carriage.Partner) && world.HasComponent<SimTransform>(carriage.Partner)
                    ? world.GetComponentRO<SimTransform>(carriage.Partner) : null;
                body = Carriage.TeamFrame(front, rear);
                _fall.Alive(e, StanceId.Prone, simTime);
                LittersDrawn++;
                return BlockFigure.OnLitter(soldier);
            }
            case CarriageKind.Seated when carriage.Seat is { } seat && world.HasComponent<SimTransform>(carriage.Holder):
            {
                ref readonly var v = ref world.GetComponentRO<SimTransform>(carriage.Holder);
                // ⭐ CE-4621 / CE-4634 — the carrier's drawn sway (a motorcycle's bank, a dragon's bob): its riders move with the body they sit on
                body = Carriage.InSeat(_motion.SwayOf(carriage.Holder) * Matrix4x4.CreateFromQuaternion(v.Rotation) * Matrix4x4.CreateTranslation(v.Position), seat);
                // ⭐ CE-1202 (§6n N-2) — the gunner of a station weapon (the technical's bed pintle, the HMMWV's ring) turns WITH it: his
                //   seat's place swung about the weapon's traverse pivot by the weapon's own azimuth, as its kit is drawn
                body = TurnedWithStation(world, e, body, simTime);
                // ⭐ CE-1088 (§6n N-2) — a gunner seat may say he STANDS or KNEELS at its weapon (the place is then his feet)
                bool stands = seat.Pose is Fdp.Toolkit.Tkb.Domain.SeatPose.Stand or Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel;
                float hips = stands ? BlockFigure.HipFraction(seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel ? StanceId.Crouched : StanceId.Standing) * size.Z : 0f;
                _drops.Riding(e, body.Translation.Z + hips, simTime);
                _fall.Alive(e, StanceId.Crouched, simTime);
                if (stands) return BlockFigure.AtWeapon(seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel, soldier);
                // ⭐ CE-4621 — astride a two-wheeler: the driver on the bars, a pillion holding on — or, on a FIRING seat with his rifle up, the rifle shouldered
                if (seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Ride && IsHorse(world, carriage.Holder))   // ⭐ CE-4623 (AU-6) — in the saddle: legs round the barrel, hands on the reins; firing, the rifle shouldered
                    return FiringAimed(world, e, seat, soldier, driverFires: true) ? BlockFigure.MountedAiming(soldier) : BlockFigure.Mounted(seat.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Driver, soldier);
                if (seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Ride)
                    return FiringAimed(world, e, seat, soldier) ? BlockFigure.RidingAiming(soldier) : BlockFigure.Riding(seat.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Driver, soldier);
                return BlockFigure.Seated(seat.Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut, soldier);
            }
            case CarriageKind.Manning when world.HasComponent<SimTransform>(carriage.Holder)
                                          && GunnerFrame(world, carriage, size, simTime, out var frame, out var pose):
                body = frame;
                // ⭐ CE-1112 (§6v AD-V4) — a gun with seats (the ZU-23): he SITS on it, his hips at the seat
                if (pose is Fdp.Toolkit.Tkb.Domain.SeatPose.Sit or Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut)
                {
                    _drops.Riding(e, body.Translation.Z, simTime);
                    _fall.Alive(e, StanceId.Crouched, simTime);
                    return BlockFigure.Seated(pose == Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut, soldier);
                }
                bool kneel = pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel;
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
        // ⭐ CE-1104 (§6t T-b3) — a team load: both hands on its handles, behind him at the front, ahead of him at the rear
        if (carriage.Kind is CarriageKind.StretcherFront or CarriageKind.StretcherRear or CarriageKind.TeamFront or CarriageKind.TeamRear)
            parts = BlockFigure.HandleArms(parts, front: carriage.Kind is CarriageKind.StretcherFront or CarriageKind.TeamFront, soldier);
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
    private readonly HashSet<Entity> _loadInHands = new();   // ⭐ CE-1236 — a carried launcher held (not aimed): drawn by the figure

    /// <summary>⭐ CE-1236 — soldiers drawn firing their own mortar by the last frame (rails).</summary>
    public int MortarsDrawn { get; private set; }

    /// <summary>⭐ CE-1236 (People Display U7a) — where a man's own mortar stands while he fires it, in his body frame: at his right side,
    /// its baseplate clear of his feet, its tube toward his front (the lay is the kit's default, §6n N-5), its mouth within his right
    /// hand's reach (measured on the first frame: 0.62 m ahead and 0.28 m right put it a metre from either shoulder).</summary>
    public static readonly Matrix4x4 MortarAt = Matrix4x4.CreateTranslation(0.30f, -0.45f, 0f);

    private static readonly ArticulationPose MortarPose = ArticulationPose.Neutral with { GunElevation = WeaponKits.DefaultElevation(WeaponKind.Mortar) };

    /// <summary>⭐ CE-1236 — the mouth of that mortar's tube in his body frame: where his hands go to drop a bomb.</summary>
    public static readonly Vector3 MortarMouth = MortarTip(Matrix4x4.Identity).Tip;

    private static (Vector3 Tip, Vector3 Forward) MortarTip(Matrix4x4 bodyToWorld)
    {
        var kit = WeaponKits.Kit(WeaponKind.Mortar, deployed: true);
        var tube = kit.Parts[1];   // baseplate, TUBE, bipod ×2, sight — WeaponKits.Mortar's order
        return WeaponTip(tube, WeaponKits.DefaultSize(WeaponKind.Mortar), kit.Pivots, MortarPose, MotionPose.Still, MortarAt * bodyToWorld);
    }

    /// <summary>⭐ CE-1236 — a man's own mortar, deployed at <see cref="MortarAt"/>; its tube's mouth is his weapon's tip (the shot's flash).</summary>
    /// <summary>⭐ <c>CE-1179</c> (U18) — is the weapon in his hands one he has LAID DOWN (his active own mount, <c>WeaponLaying</c>,
    /// replicated in <c>WeaponAmmo.LaidDown</c>)?</summary>
    public static bool LaidDownInHands(EntityRepository world, Entity man)
    {
        int active = world.IsComponentTypeRegistered<WeaponPosture>() && world.HasComponent<WeaponPosture>(man)
            ? world.GetComponentRO<WeaponPosture>(man).ActiveMount : 0;
        return !Fdp.Toolkit.Combat.WeaponSystems.IsControlledIndex(active) && Fdp.Toolkit.Combat.WeaponLaying.IsLaidDown(world, man, active);
    }

    /// <summary>⭐ CE-1179 (U18) — the charge's blinking light: where it sits on the bundle (body metres), its size and colour, its rhythm.</summary>
    public static readonly Vector3 ChargeLightAt = new(0f, 0f, 0.105f);
    public const float ChargeLightSize = 0.03f, BlinkPeriod = 1f, BlinkOn = 0.25f;
    public static readonly Color ChargeLightColour = new(255, 30, 20, 255);

    /// <summary>Charge lights drawn since the last frame began (a rail reads it).</summary>
    public int ChargeLightsDrawn { get; private set; }

    /// <summary>⭐ <c>CE-1179</c> — is a charge's light on at <paramref name="simTime"/>: it is PLACED (not carried by a man, not in a vehicle's
    /// station — no <c>IsEmbarkedTag</c>) and the blink is in its lit quarter. Every node sees the same rhythm (sim time).</summary>
    public static bool ChargeLightOn(EntityRepository world, Entity charge, double simTime)
    {
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()
            && world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(charge)) return false;
        double t = simTime % BlinkPeriod;
        return t >= 0 && t < BlinkOn;
    }

    private void DrawMortar(Entity man, Matrix4x4 body, Color colour, LitShader shader)
    {
        var kit = WeaponKits.Kit(WeaponKind.Mortar, deployed: true);
        var size = WeaponKits.DefaultSize(WeaponKind.Mortar);
        foreach (var part in kit.Parts)
        {
            var model = HrotToRaylib.ModelFromHrot(part.BodyTransform(size, kit.Pivots, MortarPose) * MortarAt * body);
            var mesh = part.Shape switch { PartShape.Cylinder => _cylinder, PartShape.Cone => _cone, _ => _cube };
            Submit(mesh, model, PartColour(part.Colour, colour, false), BodySurface.PlainData);
            PartsDrawn++;
        }
        _weaponTips[man] = MortarTip(body);
        MortarsDrawn++;
    }
    private readonly Dictionary<Entity, (Matrix4x4 Frame, float Height)> _figureFrames = new();
    private readonly Dictionary<Entity, Entity> _teamPartner = new();

    /// <summary>⭐ CE-1104 (§6t) — men drawn on a stretcher, and two-man weapons drawn between their pair, by the last frame (rails).</summary>
    public int LittersDrawn { get; private set; }
    public int TeamLoadsDrawn { get; private set; }

    /// <summary>⭐ CE-1085 — weapons drawn on a carrier's back by the last frame (rails).</summary>
    public int CarriedDrawn { get; private set; }
    /// <summary>
    /// ⭐ <c>CE-1202</c> (§6n N-2) — a station weapon's turn as its kit draws it: its traverse pivot in the WORLD (the kit's pivot placed by
    /// the weapon's own transform) and its azimuth (the published turret pose, only once it is deployed — as the kit). False when it has no
    /// kit with a pivot, or does not turn.
    /// </summary>
    internal bool StationTurn(EntityRepository world, Entity weapon, double now, out Vector3 pivot, out float azimuth)
    {
        pivot = default; azimuth = 0f;
        if (!world.IsAlive(weapon) || !world.HasComponent<TkbIdentity>(weapon) || !world.HasComponent<SimTransform>(weapon) || _poseOf == null) return false;
        var look = LookOf(world.GetComponentRO<TkbIdentity>(weapon).TkbType, _tkb());
        if (look.Family != VisualFamily.WeaponSystem || DeploymentOf(world, weapon, now) < 1f) return false;
        azimuth = _poseOf(world, weapon).TurretAzimuth;
        if (azimuth == 0f) return false;
        ref readonly var w = ref world.GetComponentRO<SimTransform>(weapon);
        pivot = Vector3.Transform(WeaponKits.TraversePivot(look.Weapon, SizeOf(world, weapon, look)),
                                  Matrix4x4.CreateFromQuaternion(w.Rotation) * Matrix4x4.CreateTranslation(w.Position));
        return true;
    }

    /// <summary>⭐ <c>CE-1202</c> — <paramref name="seatFrame"/> (a seated man's frame in the world) swung with the station weapon his seat
    /// operates (<see cref="SeatSwitch.StationOf"/>), about its traverse pivot by its azimuth; unchanged for any other seat.</summary>
    internal Matrix4x4 TurnedWithStation(EntityRepository world, Entity man, Matrix4x4 seatFrame, double now)
    {
        if (!SeatSwitch.StationOf(world, man, out _, out _, out var gun) || !StationTurn(world, gun, now, out var pivot, out float azimuth)) return seatFrame;
        GunnersTurned++;
        return seatFrame * Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationZ(azimuth) * Matrix4x4.CreateTranslation(pivot);
    }

    /// <summary>⭐ CE-4623 — dead animals drawn lying by the last frame (rails).</summary>
    public int DeadAnimalsDrawn { get; private set; }
    /// <summary>⭐ CE-4623 — birds drawn from flocks by the last frame (rails).</summary>
    public int FlockBirdsDrawn { get; private set; }

    /// <summary>⭐ CE-4623 — is <paramref name="holder"/> a horse (what a Ride seat sits on)? ⭐ CE-4634 — or a DRAGON (its rider and pillion sit astride the same way, R-4269's
    /// riding pose: legs round the barrel, firing from the saddle).</summary>
    private bool IsHorse(EntityRepository world, Entity holder)
        => world.HasComponent<TkbIdentity>(holder) && LookOf(world.GetComponentRO<TkbIdentity>(holder).TkbType, _tkb()).Family is VisualFamily.Horse or VisualFamily.Dragon;

    /// <summary>⭐ CE-4634 — does the dragon <paramref name="e"/> fly? Its motion model's word (<see cref="Fdp.Toolkit.Motion.FlightState"/>.Airborne, the rotorcraft's take-off / landing);
    /// the height heuristic only when it has no flight state yet.</summary>
    private static bool DragonFlies(EntityRepository world, Entity e, Vector3 position, BodyGeometryDto? geometry)
        => world.IsComponentTypeRegistered<Fdp.Toolkit.Motion.FlightState>() && world.HasComponent<Fdp.Toolkit.Motion.FlightState>(e)
            ? world.GetComponentRO<Fdp.Toolkit.Motion.FlightState>(e).Airborne != 0
            : IsAirborne(world, position, geometry);

    /// <summary>
    /// ⭐ CE-4623 (AU-4, AR-14) — a flock: N birds, each a closed-form offset of (seed, index, sim time, mode) in the flock's frame (<see cref="FlockBirds"/>); a dot each when far, the bird
    /// kit (wings beating; folded landed or perched) when near; the flock vanishes under <see cref="HiddenBelowPx"/>. 0 B per frame.
    /// </summary>
    private void DrawFlock(EntityRepository world, Entity e, in FlockState flock, double simTime)
    {
        int n = Math.Min((int)flock.Count, FlockBirds.MaxDrawn);
        if (n <= 0) return;
        ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
        var frame = Matrix4x4.CreateFromQuaternion(tf.Rotation) * Matrix4x4.CreateTranslation(tf.Position);
        float birdPx = _scale.IsSet ? _scale.PixelsOf(tf.Position, 0.35f) : 100f;
        float flockPx = _scale.IsSet ? _scale.PixelsOf(tf.Position, FlockBirds.Radius(n) * 2f) : 1000f;
        if (flockPx < HiddenBelowPx) { BodiesHidden++; return; }
        bool dots = birdPx < 3f;
        var kit = AnimalKits.For(VisualFamily.Bird, AnimalMode.Calm, perched: flock.Mode != FlockMode.Flying);
        var size = AnimalKits.SizeOf(VisualFamily.Bird);
        var colour = AnimalCoats.Of(VisualFamily.Bird, flock.Seed);
        for (int i = 0; i < n; i++)
        {
            var at = FlockBirds.Offset(flock.Seed, i, simTime, flock.Mode, flock.InitialCount > 0 ? flock.InitialCount : flock.Count);
            var bird = Matrix4x4.CreateRotationZ(FlockBirds.Yaw(flock.Seed, i, simTime, flock.Mode)) * Matrix4x4.CreateTranslation(at) * frame;
            if (dots)
                Submit(_cube, HrotToRaylib.ModelFromHrot(Matrix4x4.CreateScale(0.3f, 0.3f, 0.15f) * Matrix4x4.CreateTranslation(0f, 0f, 0.1f) * bird), colour, BodySurface.PlainData);
            else
            {
                var motion = new MotionPose(0f, 0f, 0f, Wing: FlockBirds.Wing(flock.Seed, i, simTime, flock.Mode));
                var parts = kit.Parts;
                for (int pi = 0; pi < parts.Length; pi++)
                {
                    var part = parts[pi];
                    Submit(_cube, HrotToRaylib.ModelFromHrot(part.BodyTransform(size, default, ArticulationPose.Neutral, motion) * bird), PartColour(part.Colour, colour, false), BodySurface.PlainData);
                    PartsDrawn++;
                }
            }
            FlockBirdsDrawn++;
        }
        BodiesFull++; BodiesDrawn++;
    }

    /// <summary>⭐ CE-4621 — a pillion drawn TURNED to his aim by the last frame (rails).</summary>
    public int PillionsAimed { get; private set; }

    /// <summary>The most a rider on a firing seat turns his upper body off the seat's forward (radians, ~160°) — the seat's own arc is the limit (PF-1), this one only keeps the drawing human.</summary>
    public const float MaxSeatTwist = UpperBody.MaxSeatTwist;   // ⭐ CE-11002 (RR-2)

    /// <summary>⭐ CE-4621 — a soldier on a FIRING seat (a traverse arc on a non-station, non-DRIVER seat, PF-1) whose weapon brain has the rifle up (Aimed): drawn with it shouldered. ⛔ The driver (backend's rider seat has an arc, PF-4: fires only stopped) always stays on the bars — LD-9.</summary>
    private static bool FiringAimed(EntityRepository world, Entity e, Fdp.Toolkit.Tkb.Domain.SeatDto seat, bool soldier, bool driverFires = false)
        => soldier && (driverFires ? seat.Role != Fdp.Toolkit.Behavior.Components.SeatRole.Station : seat.Role is not (Fdp.Toolkit.Behavior.Components.SeatRole.Station or Fdp.Toolkit.Behavior.Components.SeatRole.Driver))
           && seat.TraverseMinDeg != null && seat.TraverseMaxDeg != null
           && PostureOf(world, e) == WeaponPostureKind.Aimed;

    /// <summary>
    /// ⭐ CE-4621 — the motion of a man in a seat: the plain seat's still pose; astride a two-wheeler (<c>SeatPose.Ride</c>) his hips are the figure's origin and his
    /// upper body LEANS (the driver to the bars, a pillion a little); a firing pillion with his rifle up TURNS to his aim — the aim point in the seat's frame, the twist
    /// eased like a standing man's (<see cref="PostureBlend"/>) and limited to <see cref="MaxSeatTwist"/>, the head turning to where his eyes point.
    /// </summary>
    private MotionPose SeatedMotion(EntityRepository world, Entity e, in FigureCarriage carriage, in Matrix4x4 body, bool soldier, double simTime)
    {
        var still = MotionPose.Still with { HipZ = 0.5f };
        if (carriage.Kind != CarriageKind.Seated || carriage.Seat is not { Pose: Fdp.Toolkit.Tkb.Domain.SeatPose.Ride } seat) return still;
        bool driver = seat.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Driver;
        float twist = 0f, pitch = 0f;
        bool mounted = IsHorse(world, carriage.Holder);   // ⭐ CE-4623 — a horse's rider FIRES while riding (R-4269, unlike the motorcycle's driver) and sits upright on the reins
        bool aiming = FiringAimed(world, e, seat, soldier, driverFires: mounted) && Fdp.Toolkit.Combat.WeaponPostureOf.TryAimPoint(world, e, out var at)
                      && Matrix4x4.Invert(body, out var inverse)
                      && AimInSeat(Vector3.Transform(at, inverse), out twist, out pitch);
        if (aiming) PillionsAimed++;
        if (soldier) (twist, pitch) = _postures.Aim(e, twist, pitch, simTime);
        return still with
        {
            HipZ = 0f, Twist = twist,
            Lean = (aiming || MathF.Abs(twist) > 0.02f ? 0f : driver && !mounted ? BlockFigure.DriverLean : BlockFigure.PillionLean) - pitch,
            HeadTurn = soldier ? _postures.Head(e, HeadTurnOf(world, e, twist), simTime) : 0f,
        };
    }

    /// <summary>⭐ CE-4621 — the turn (counter-clockwise) and pitch (up) from the shoulder (0.53 m over the hips) to <paramref name="local"/> (an aim point in the seat's frame), the turn limited to <see cref="MaxSeatTwist"/>. False under 10 cm.</summary>
    public static bool AimInSeat(Vector3 local, out float twist, out float pitch) => UpperBody.AimInSeat(local, out twist, out pitch);   // ⭐ CE-11002 (RR-2)

    /// <summary>⭐ CE-1202 — seated gunners turned with their station's weapon by the last frame (rails).</summary>
    public int GunnersTurned { get; private set; }

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
    /// ⭐ CE-1089 (N-5) / ⭐ CE-1114 (§6n) — the turret and gun pose of a weapon system or a self-propelled gun: its turret part's
    /// REPLICATED pose whenever it has one — azimuth AND elevation, as published (W-9; backend P2 lays mortars and guns by it), a level
    /// gun included; only a gun with no turret part is drawn at <paramref name="fallback"/> (60° mortar, 25° howitzer).
    /// ⛔ HISTORY: the elevation was read only when it was non-zero, else guessed from the gun's newest round on the node running combat
    /// (a stop-gap until the pose crossed the wire) — retired: a published 0 was drawn as the default and a viewer never had the guess.
    /// </summary>
    internal ArticulationPose LaidPose(EntityRepository world, Entity gun, float fallback)
    {
        var pose = _poseOf != null ? _poseOf(world, gun) : ArticulationPose.Neutral;
        return pose.Published ? pose : pose with { GunElevation = fallback };
    }

    /// <summary>
    /// ⭐ CE-1090 (N-4) — does <paramref name="e"/> hold a launcher on his shoulder now: ⭐ CE-1107 — the weapon he has SELECTED is a
    /// launcher (the replicated <c>WeaponPosture.ActiveMount</c>, backend P4 — before his first shot, on every node); or he fired a
    /// launcher mount of his own, or the launcher he carries fired, within <see cref="WeaponKits.ShoulderSeconds"/>. The caller draws it
    /// only while he aims.
    /// </summary>
    internal static bool Shouldering(EntityRepository world, Entity e, in FigureCarriage carriage, ITkbDatabase? tkb, double now)
    {
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponPosture>()
            && world.HasComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>(e))
        {
            int active = world.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponPosture>(e).ActiveMount;
            if (Fdp.Toolkit.Combat.WeaponSystems.IsControlledIndex(active))
            {
                var held = Fdp.Toolkit.Combat.WeaponSystems.ControlledAt(world, e, active);
                if (!held.IsNull && WeaponKits.IsShoulderFired(WeaponKits.TypeOf(world, tkb, held).Kind)) return true;
            }
            else if (WeaponKits.IsLauncher(tkb, Fdp.Toolkit.Combat.CombatTkb.MountOf(world, e, active))) return true;
        }
        if (Hrot.UI.Common.Effects.RecentShots.Of(world) is not { } shots) return false;
        if (shots.FiredWithin(e, now, WeaponKits.ShoulderSeconds, out int mount)
            && WeaponKits.IsLauncher(tkb, Fdp.Toolkit.Combat.CombatTkb.MountOf(world, e, mount))) return true;
        return !carriage.Load.IsNull && shots.FiredWithin(carriage.Load, now, WeaponKits.ShoulderSeconds, out _)
               && WeaponKits.IsShoulderFired(WeaponKits.TypeOf(world, tkb, carriage.Load).Kind);
    }

    /// <summary>
    /// ⭐ CE-1088 (N-2) — where a man in a weapon's own seat is drawn: at its seat's place when the seat declares one, else at the weapon
    /// kind's gunner spot (scaled to the weapon's size), turned with its traverse about its pivot. False for a weapon nobody mans on foot.
    /// </summary>
    internal bool GunnerFrame(EntityRepository world, in FigureCarriage carriage, Vector3 manSize, double now, out Matrix4x4 frame,
                              out Fdp.Toolkit.Tkb.Domain.SeatPose pose)
    {
        frame = default; pose = Fdp.Toolkit.Tkb.Domain.SeatPose.Stand;
        var weapon = carriage.Holder;
        if (!world.HasComponent<TkbIdentity>(weapon)) return false;
        var look = LookOf(world.GetComponentRO<TkbIdentity>(weapon).TkbType, _tkb());
        ref readonly var w = ref world.GetComponentRO<SimTransform>(weapon);
        var weaponBody = Matrix4x4.CreateFromQuaternion(w.Rotation) * Matrix4x4.CreateTranslation(w.Position);
        if (carriage.Seat is { HasPlace: true } seat)
        {
            frame = Carriage.InSeat(weaponBody, seat);
            // ⭐ CE-1202 — a seat the weapon itself declares turns with its traverse too (it was drawn where the seat was at azimuth 0)
            if (StationTurn(world, weapon, now, out var axis, out float turn))
                frame = frame * Matrix4x4.CreateTranslation(-axis) * Matrix4x4.CreateRotationZ(turn) * Matrix4x4.CreateTranslation(axis);
            pose = seat.Pose;
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
        pose = spot.Pose;
        return true;
    }

    /// <summary>⭐ CE-1085 (N-3) — pass 2: each carried weapon on its bearer's back as his figure was drawn, stowed; not drawn while he
    /// holds it on his shoulder (the figure's tube is it), nor when its bearer was not drawn.</summary>
    private void DrawCarried(EntityRepository world, LitShader shader)
    {
        foreach (var (weapon, bearer, look, size, colour) in _carried)
        {
            if (_shouldering.Contains(bearer) || _loadInHands.Contains(bearer) || !_figureFrames.TryGetValue(bearer, out var figure)) continue;
            // ⭐ CE-1169 (People Display U8) — a HIDDEN station is not drawn (a concealed pistol, a worn charge); a hand weapon in his
            //   HANDS is held level, a pistol in his back station is in its holster, the rest is slung across his back
            if (Fdp.Toolkit.Behavior.CarryStations.IsConcealed(world, weapon)) continue;
            bool stowed = Fdp.Toolkit.Behavior.CarryStations.IsStowed(world, weapon);
            var frame = WeaponKits.IsHandWeapon(look.Weapon) && !stowed ? WeaponKits.InHands(figure.Frame, figure.Height, size)
                      : look.Weapon == WeaponKind.Pistol ? WeaponKits.OnHip(figure.Frame, figure.Height, size)
                      : WeaponKits.OnBack(figure.Frame, figure.Height, size);
            // ⭐ CE-1104 (§6t T-b4) — a two-man weapon: between the pair at their hands, stowed, the front man's way
            if (_teamPartner.TryGetValue(bearer, out var partner) && world.HasComponent<SimTransform>(bearer))
            {
                SimTransform? rear = world.IsAlive(partner) && world.HasComponent<SimTransform>(partner) ? world.GetComponentRO<SimTransform>(partner) : null;
                // the stowed kit's own middle at the hands (measured: a fixed share of its size left a stowed mortar on the ground)
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in WeaponKits.Kit(look.Weapon, deployed: false).Parts)
                {
                    float half = p.Extent.Z / 2f;   // a box's height, an upright cylinder's length, a lying one's diameter
                    lo = MathF.Min(lo, p.Centre.Z - half); hi = MathF.Max(hi, p.Centre.Z + half);
                }
                float middle = lo <= hi ? (lo + hi) / 2f * size.Z : 0f;
                frame = Matrix4x4.CreateTranslation(0f, 0f, -middle) * Carriage.TeamFrame(world.GetComponentRO<SimTransform>(bearer), rear);
                TeamLoadsDrawn++;
            }
            bool wreck = Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, weapon);
            foreach (var part in WeaponKits.Kit(look.Weapon, deployed: false).Parts)
            {
                var model = HrotToRaylib.ModelFromHrot(part.BodyTransform(size) * frame);
                var mesh = part.Shape switch { PartShape.Cylinder => _cylinder, PartShape.Cone => _cone, _ => _cube };
                Submit(mesh, model, PartColour(part.Colour, colour, wreck), BodySurface.PlainData);
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

    /// <summary>⭐ <c>CE-1201</c> (docs/DESIGN_Entity_Control.md §8p OS-5) — the over-the-shoulder view: the first-person unit's head is
    /// drawn (he is seen whole); the pickers still skip his body.</summary>
    public bool ShowFirstPersonHead { get; set; }

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

    /// <summary>⭐ <c>CE-1200</c> — how far above a turret's roof a commander's eye is, his head out of the hatch (m).</summary>
    public const float HatchEyeAboveRoof = 0.55f;

    /// <summary>
    /// ⭐ <c>CE-1200</c> (docs/DESIGN_Entity_Control.md §8o CR-2) — a vehicle kind's CREW eye in its kit's body frame (metres, the kit's own
    /// convention: x forward, y left, z up from the box bottom): in its first GLASS part (the cab, the canopy, the cockpit) a fifth of the
    /// way forward and a seventh up from its middle — on the driver's / pilot's side (left) where two sit side by side (a car, a utility, a
    /// helicopter, a transport); else a commander's eye above its TURRET's roof, a quarter of its width to the left. Null: no crew eye.
    /// Pure — the rail runs it over every kind.
    /// </summary>
    /// <summary>⭐ <c>CE-1974</c> — a carrier its rider sits ON in the open (an animal): drawn even while it is the operator's own body. (The motorcycle has a windscreen, so it
    /// already has a crew eye and is drawn.) ⭐ CE-4634 — and a DRAGON: its rider sits on its back in the open, and sees its neck and wings.</summary>
    /// <summary>⭐ CE-4634 — the drawn sway of <paramref name="carrier"/> (<see cref="MotionTracker.SwayOf"/>): what a rider's camera eye moves with.</summary>
    public Matrix4x4 SwayOf(Entity carrier) => _motion.SwayOf(carrier);

    public static bool OpenCarrier(VisualFamily family) => VisualFamilies.IsAnimal(family) || family == VisualFamily.Dragon;

    public static Vector3? CrewEye(VisualFamily family, Vector3 size)
    {
        var kit = ShapeKits.For(family);
        bool sideBySide = family is VisualFamily.WheeledCar or VisualFamily.WheeledUtility or VisualFamily.Helicopter or VisualFamily.CargoPlane;
        foreach (var p in kit.Parts)
            if (p.Colour == ColourRole.Glass)
                return new Vector3((p.Centre.X + 0.2f * p.Extent.X) * size.X, (p.Centre.Y + (sideBySide ? 0.25f * p.Extent.Y : 0f)) * size.Y,
                                   (p.Centre.Z + 0.15f * p.Extent.Z) * size.Z);
        foreach (var p in kit.Parts)
            if (p.Role == PartRole.Turret)
                return new Vector3(p.Centre.X * size.X, (p.Centre.Y + 0.25f * p.Extent.Y) * size.Y, (p.Centre.Z + 0.5f * p.Extent.Z) * size.Z + HatchEyeAboveRoof);
        return null;
    }

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
        if (_recordPick)   // ⭐ CE-4558 — picked by its leaf as drawn (TryGetBox's rule: a 5 cm leaf stays clickable edge-on), shown or too small to draw
            _pick.Add(e, NetOf(world, e), world.GetComponentRO<SimTransform>(e).Position, centre, rotation, Vector3.Max(half, new Vector3(0.1f)));
        // ⭐ CE-4557 (§6ak D-3) — a door less than DoorHiddenBelowPx on screen is not drawn (unless selected); its swing keeps running
        if (_scale.PixelsOf(centre, 2f * MathF.Max(half.X, MathF.Max(half.Y, half.Z))) < DoorHiddenBelowPx && !IsSelected(world, e, hasSelection))
        {
            DoorsHidden++;
            return;
        }
        var model = Matrix4x4.CreateScale(half * 2f) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(centre);
        Submit(_cube, HrotToRaylib.ModelFromHrot(model), DoorLeaf3D.ColourOf(state), BodySurface.PlainData);   // ⭐ CE-1069 — locked tinted; ⭐ CE-4555 (D-3) batched
        PartsDrawn++;
        DoorsDrawn++;
        if (hasSelection && world.HasComponent<Hrot.Map.Common.Components.SelectionState>(e))
        {
            ref readonly var sel = ref world.GetComponentRO<Hrot.Map.Common.Components.SelectionState>(e);
            if (sel.IsSelected)
            {
                DrawWireBox(centre, rotation, half * 1.06f, sel.IsPrimarySelection ? PrimarySelection : OtherSelection);
                SelectionBoxesDrawn++;
            }
        }
    }
    private readonly PostureBlend _postures = new();
    private readonly StanceBlend _stances = new();   // ⭐ CE-1348 — the drawn stance moves smoothly to the logical one

    /// <summary>⭐ WL-6b — the posture changes' timing (the rail's seam).</summary>
    public PostureBlend Postures => _postures;

    /// <summary>The dead men's falls (W4) — read by rails.</summary>
    public DeathFall Falls => _fall;
    private readonly HashSet<Entity> _drawn = new();
    private float _wallFallback;
    private double _frameClock;

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
    /// <summary>⭐ CE-1101 (docs/DESIGN_Map_3D_Mode.md §6r) — what the shader paints on part <paramref name="index"/> of a man in
    /// <paramref name="outfit"/>: his face on his head, his side's camouflage on a soldier's uniform (legs, torso, arms), else nothing.</summary>
    private LitInstanceData Paint(Outfit? outfit, int index, FaceOn face, float camoSeed)
    {
        if (outfit is not { } o) return BodySurface.PlainData;
        if (face != FaceOn.None)
        {
            FacesPainted++;
            return BodySurface.FaceData(face, o.Face, o.Hair.ToColor(), _expression);
        }
        if (o.Camo != CamoPattern.None && index is >= 0 and <= 9 and not BlockFigure.HeadIndex)
        {
            var (t1, t2, t3) = Outfits.CamoTones(o.Camo);
            CamoPainted++;
            return BodySurface.CamouflageData(o.Camo switch
            {
                CamoPattern.Woodland => SurfaceKind.Woodland,
                CamoPattern.Desert => SurfaceKind.Desert,   // ⭐ CE-1235
                _ => SurfaceKind.Fleck,
            }, t1.ToColor(), t2.ToColor(), t3.ToColor(), camoSeed);
        }
        return BodySurface.PlainData;
    }

    private readonly BodySurface _surface = new();

    /// <summary>⭐ CE-4555 (docs/DESIGN_Map_3D_Mode.md §6ak.1, G-2) — every part, face box, outfit detail, stain and door leaf of the frame, drawn at the
    /// end of <see cref="Draw3D"/> in one instanced call per mesh (it was one <c>DrawMesh</c> each: tens of thousands at the whole-terrain view).</summary>
    private readonly LitInstanceBatch _batch = new();
    private int _cubeBucket, _cylinderBucket, _coneBucket;
    private Action<LitInstanceData>? _applySurface;   // the per-draw path's surface (built once: no per-frame closure)

    /// <summary>⭐ CE-4555 — draw the batch one <c>DrawMesh</c> per instance, as before it (the rail comparing the two pictures).</summary>
    public bool ForcePerDraw { get => _batch.ForcePerDraw; set => _batch.ForcePerDraw = value; }

    /// <summary>⭐ CE-4555 — the batch's instanced calls and instances of the last frame (the body layer's draw-call counter, §6ak.5 ②).</summary>
    public int BatchDrawCalls => _batch.DrawCalls;
    public int BatchInstances => _batch.Instances;

    /// <summary>⭐ CE-4555 (rails) — the instances the last frame put in the batch, before it was drawn: per mesh (0 cube, 1 cylinder, 2 cone).</summary>
    internal LitInstanceBatch Batch => _batch;

    private void Submit(Mesh mesh, in Matrix4x4 model, Color colour, in LitInstanceData data)
        => _batch.Add(mesh.VaoId == _cylinder.VaoId ? _cylinderBucket : mesh.VaoId == _cone.VaoId ? _coneBucket : _cubeBucket, model,
                      _thermal ? _submitHeat : colour, data);   // ⭐ CE-1985 — in the thermal pass EVERY body instance carries a °C (never a colour read as one)

    // ── ⭐ CE-1985 (docs/designs/sensor-view/DESIGN_Sensor_View.md §5b, §5c) — the thermal pass ─────────────────────────────────────────────
    /// <summary>The class a lit draw that says nothing is read as in the thermal pass (concrete — plausible background).</summary>
    public const int ThermalDefaultClass = (int)Fdp.Toolkit.Perception.Signatures.ThermalClassId.Concrete;

    /// <summary>A unit's temperatures for the thermal pass (wired to the map's sensor view by <c>AttachMapLayers</c>): its body °C, its hot end's °C
    /// and which end is hot (+1 front, −1 rear, 0 none). Null ⇒ no thermal picture of bodies.</summary>
    public Func<EntityRepository, Entity, (float BodyC, float HotC, int HotSign)>? UnitHeat { get; set; }

    private bool _thermal;
    private Color _airHeat, _submitHeat, _unitBody, _unitHot;
    private int _hotSign;

    private void SetUnitHeat(EntityRepository world, Entity e, Vector3 size)
    {
        var (bodyC, hotC, sign) = UnitHeat!(world, e);
        _unitBody = Fdp.Toolkit.Vis3D.LitShader.EncodeC(bodyC);
        _unitHot = Fdp.Toolkit.Vis3D.LitShader.EncodeC(hotC);
        _hotSign = sign;
        _submitHeat = _unitBody;
    }

    /// <summary>⭐ CE-1101 (§6r) — heads painted with a face, and uniform parts painted with camouflage, by the last frame (rails).</summary>
    public int FacesPainted { get; private set; }
    public int CamoPainted { get; private set; }

    /// <summary>⭐ CE-4648 (OF-11) — the boxes on the head of a man from his own wardrobe: the helmet / police cap / riot helmet it names, else the hair box
    /// (a cap, a skullcap and the scarves are details, <see cref="Outfits.Details"/>; their cloth colours the hair box).</summary>
    private static IReadOnlyList<FacePart> WardrobeHead(OutfitHead kind) => kind switch
    {
        OutfitHead.Helmet => BlockFigure.FaceParts(true),
        OutfitHead.PoliceCap => BlockFigure.PoliceHead(false),
        OutfitHead.RiotHelmet => BlockFigure.PoliceHead(true),
        _ => BlockFigure.FaceParts(false),
    };

    private void DrawFace(FaceOn on, Matrix4x4 head, bool soldier, Color uniform, LitShader shader, Outfit? outfit = null)
    {
        // ⭐ CE-1172 (People Display U11) — a police officer's cap, or the shield man's riot helmet, instead of hair or a soldier's helmet
        var heads = outfit is { Police: true } police ? BlockFigure.PoliceHead(police.Riot)
                  : outfit is { Dressed: true } dressed ? WardrobeHead(dressed.HeadKind)   // ⭐ CE-4648 (OF-11) — his wardrobe's headwear, not his class's helmet
                  : BlockFigure.FaceParts(soldier);
        for (int i = 0; i < heads.Count; i++)   // ⭐ CE-4555 — an index loop: foreach over the IReadOnlyList boxed an enumerator per head, every frame
        {
            var d = heads[i];
            if (d.Shade == FaceShade.Hair && outfit?.HairStyle == HairStyle.None) continue;
            Submit(_cube, HrotToRaylib.ModelFromHrot(BlockFigure.FaceDetail(d, on, head)), FaceColour(d.Shade, uniform, (outfit?.Hair).ToColor()), BodySurface.PlainData);
            FaceDetailsDrawn++;
        }
    }

    /// <summary>⭐ CE-1098 (docs/DESIGN_Map_3D_Mode.md §6p) — this man's blood spots, into <see cref="_stains"/>: as many as his lost health
    /// earns (<see cref="BlockFigure.StainCount"/>), seeded by his network id so every node draws the same spots in the same places
    /// (the entity's index where he has none). Returns the count.</summary>
    private int Bloodied(EntityRepository world, Entity e, bool hasNet, KitPart[] parts, Vector3 size)
    {
        // ⭐ CE-1122 (§6y) — where he was really hit: the replicated marks, on their parts (the seeded spots below are the fallback)
        if (MarkStains(world, e, parts, size, _stains) is int marks and > 0) return marks;
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

    private readonly List<Stain> _stains = new(Fdp.Toolkit.Physics.BodyParts.HitMarks.Capacity);

    /// <summary>⭐ CE-1122 (§6y HM-1, HM-2) — <paramref name="e"/>'s replicated <c>HitMarks</c> as stains on <paramref name="parts"/> (a figure
    /// of <paramref name="size"/>), oldest first, the three newest fresh; 0 when he has none.</summary>
    public static int MarkStains(EntityRepository world, Entity e, KitPart[] parts, Vector3 size, List<Stain> into)
    {
        into.Clear();
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Physics.BodyParts.HitMarks>() || !world.HasComponent<Fdp.Toolkit.Physics.BodyParts.HitMarks>(e)) return 0;
        ref readonly var marks = ref world.GetComponentRO<Fdp.Toolkit.Physics.BodyParts.HitMarks>(e);
        int last = marks.NextSeq;
        for (int seq = Math.Max(0, last - Fdp.Toolkit.Physics.BodyParts.HitMarks.Capacity); seq < last; seq++)
        {
            if (!marks.TryGet(seq, out byte part, out var local, out _, out var kind)) continue;
            if (part >= Fdp.Toolkit.Physics.BodyParts.PersonPart.Count || part >= parts.Length) continue;
            into.Add(BlockFigure.MarkStain(part, local, kind, fresh: seq >= last - 3, parts[part].Extent * size));
        }
        return into.Count;
    }

    /// <summary>⭐ CE-1098 / CE-1099 — a man's seed: his network id (the same on every node), else his entity index.</summary>
    private static long SeedOf(EntityRepository world, Entity e, bool hasNet)
        => hasNet ? Hrot.Look.People.PersonLook.SeedOf(world, e) : e.Index;   // ⭐ CE-4671 LK-S2 — the look library's seed (one owner)

    /// <summary>⭐ CE-1099 (§6q O-4) — his side (replicated <see cref="EntityInfo.ForceId"/>); Neutral where there is none.</summary>
    private static ForceId SideOf(EntityRepository world, Entity e) => Hrot.Look.People.PersonLook.SideOf(world, e);   // ⭐ CE-4671 LK-S2

    /// <summary>⭐ CE-1099 (§6q O-1) — true when his colour is HIS (authored on the entity, or named by his type), not the family default.</summary>
    private static bool HasOwnColour(EntityRepository world, Entity e, in TypeLook look) => look.TypeColoured || HasEntityColour(world, e);

    /// <summary>
    /// ⭐ CE-1099 (§6q O-1) / ⭐ CE-1235 (O-4a, R-1020) — the colour a person's OUTFIT takes as his own, or null for none. A civilian (and a
    /// police officer): his entity's or his type's. A SOLDIER: only one HE names — a type colour is per type, never per side (one type serves
    /// both sides), so it could only make the sides alike, and his side picks the cloth instead. ⚠ The type's colour is stamped onto every
    /// spawned entity (<c>PresentationTkbTranslator</c>), so his own is one that DIFFERS from the type's.
    /// </summary>
    public static Color? OwnColourOf(EntityRepository world, Entity e, in TypeLook look)
        => Hrot.Look.People.PersonLook.OwnColourOf(world, e, FactsFrom(look)).ToColor();   // ⭐ CE-4671 LK-S2 — the look library's rule (one owner)

    /// <summary>⭐ CE-4671 LK-S2 — a type look's person half, as the look library's facts.</summary>
    private static Hrot.Look.People.PersonFacts FactsFrom(in TypeLook look)
        => new(look.Soldier, look.Child, look.Kit, look.Elderly, look.Sex, look.Wardrobe, look.Colour.ToRgba(), look.TypeColoured);

    private static bool SameColour(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

    /// <summary>⭐ CE-1235 — true when the ENTITY itself names a colour (authored appearance or visual data), whatever his type says.</summary>
    private static bool HasEntityColour(EntityRepository world, Entity e) => Hrot.Look.People.PersonLook.HasEntityColour(world, e);   // ⭐ CE-4671 LK-S2

    private readonly List<PartDetail> _details = new(8);

    /// <summary>⭐ CE-1177 — the face expression of the man being drawn (zero = calm; every other body draws calm).</summary>
    private Vector3 _expression;

    /// <summary>⭐ CE-1098 (§6p B-4) — blood: a fresh red and a dried, darker one.</summary>
    public static readonly Color BloodFresh = new(186, 24, 20, 255), BloodDark = new(128, 16, 14, 255);

    /// <summary>⭐ CE-1122 (§6y HM-2) — a burn mark.</summary>
    public static readonly Color Charred = new(34, 28, 24, 255);

    /// <summary>⭐ CE-1104 (§6t T-b2) — a stretcher's canvas.</summary>
    public static readonly Color Canvas = new(122, 116, 84, 255);

    /// <summary>⭐ CE-1097 (§6o F-1) — the skin (the default tone; a man's own comes from his outfit, CE-1099).</summary>
    public static readonly Color Skin = new(206, 168, 132, 255);

    /// <summary>⭐ CE-1097 (§6o F-3) — a head box's colour: the helmet is the uniform a step darker (so the side still reads from above,
    /// where the helmet is most of what is seen of a man); the hair his own (CE-1099) when given. ⭐ CE-1101 — the features are painted.</summary>
    public static Color FaceColour(FaceShade shade, Color uniform, Color? hair = null) => shade switch
    {
        FaceShade.Hair => hair ?? new Color(62, 44, 32, 255),
        FaceShade.Cap => Outfits.PoliceUniform.ToColor(),          // ⭐ CE-1172 (U11)
        FaceShade.CapBand => Outfits.CapBand.ToColor(),
        FaceShade.Peak => new Color(16, 16, 18, 255),
        FaceShade.Visor => new Color(150, 172, 196, 255),
        _ => new Color((byte)(uniform.R * 0.72f), (byte)(uniform.G * 0.72f), (byte)(uniform.B * 0.72f), (byte)255),
    };

    private void DrawPart(Mesh mesh, Matrix4x4 local, Color colour, Matrix4x4 body, LitShader shader)
    {
        Submit(mesh, HrotToRaylib.ModelFromHrot(local * body), colour, BodySurface.PlainData);
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

    /// <summary>⭐ CE-1178 (docs/DESIGN_People_Display.md §7a) — where a figure's card sits: its height, or higher when a drawn part rises
    /// above it (a protest sign's board, hands up), so the card never covers what he holds up.</summary>
    public static float LabelTop(KitPart[] parts, Vector3 size)
    {
        float top = size.Z;
        foreach (var p in parts)
            top = MathF.Max(top, Vector3.Transform(Vector3.Zero, p.BodyTransform(size)).Z + p.Extent.Z * size.Z / 2f);
        return top;
    }

    /// <summary>⭐ CE-1033 S3 (M13) — the height of the top of an entity's DRAWN body above its position, by network id, from
    /// the last 3-D frame; null when the body was not drawn. The gizmo label overlay lifts the entity's labels to it.</summary>
    /// <summary>⭐ CE-4200: the gizmo layer's label-lift callback is keyed by a 64-bit gizmo anchor id; one outside the network-id range names no body.</summary>
    public float? TopAbove(long anchorId) => anchorId is >= int.MinValue and <= int.MaxValue ? TopAbove((int)anchorId) : null;
    public float? TopAbove(int networkId) => _tops.TryGetValue(networkId, out float top) ? top : null;

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

    private readonly Dictionary<(long Type, Vector3 Size, int Load), KitPart[]> _storeLoads = new();

    /// <summary>⭐ CE-4647 — a jet's kit with its rails and the missiles left, its afterburner and its vortex trails (<see cref="JetStores"/>), built ONCE per (type, size, load): a steady frame allocates nothing.</summary>
    private KitPart[] StoresOf(VisualFamily family, long type, Vector3 size, KitPart[] body, int ir, int radar, bool burner, bool vortex)
    {
        var key = (type, size, (ir < 0 ? 0 : 1 + ir * 3 + radar) + (burner ? 16 : 0) + (vortex ? 32 : 0));
        if (!_storeLoads.TryGetValue(key, out var parts)) _storeLoads[key] = parts = JetStores.Load(family, size, body, ir, radar, burner, vortex);
        return parts;
    }

    /// <summary>⭐ CE-4621 — pins the look of <paramref name="tkbType"/> (a rail's seam: a stand-in type whose class the table does not have yet cannot resolve a family).</summary>
    internal void PinLook(long tkbType, TypeLook look) => _looks[tkbType] = look;

    public TypeLook LookOf(long tkbType, ITkbDatabase? tkb)
    {
        if (_looks.TryGetValue(tkbType, out var look)) return look;
        // ⭐ CE-1042 — an EFFECT type (muzzle flash, explosion, decal, tracer) has no body: the effect layer draws it. Checked by
        //   the catalog too, so a host with no TKB (the replay browser) does not draw a recorded effect as an unknown box.
        // ⭐ CE-1033 S5 — nor has the map CAMERA entity (R-260): it is the view, not a thing in it.
        // ⭐ LR-3 — nor has a built-in MUNITION type (a round), on a host whose TKB lacks it (the replay browser).
        // ⭐ CE-1068 — nor has a DOOR a shape kit: it is drawn as its leaf (DoorLeaf3D), and where no terrain places it, not at all.
        if (Hrot.Core.Tkb.EffectTkbCatalog.BuiltIn.ContainsKey(tkbType) || Hrot.Core.Tkb.MapCameraTkb.Is(tkbType)
            || Hrot.Core.Tkb.MunitionTkbCatalog.IsBuiltIn(tkbType) || tkbType == Hrot.Map.Common.TkbEntityTypes.Door
            || Hrot.Core.Tkb.AreaEffectCloudTkb.Is(tkbType)   // ⭐ CE-1165 — nor a gas / smoke CLOUD: EffectLayer draws it
            || Hrot.Core.Tkb.BuildingDamageTkb.Is(tkbType)
            || Hrot.Core.Tkb.GroundDamageTkb.Is(tkbType)
            || Hrot.Core.Tkb.GroundFireTkb.Is(tkbType)   // ⭐ CE-3487 (flame FL-3) — nor a cell's GROUND FIRE record: its FireState spots are drawn by the effect layer (§6 the look contract); backend, cross-lane one-line edit
            || Hrot.Core.Tkb.GroundHeatMarksTkb.Is(tkbType)   // ⭐ CE-4664 (TD-14) — nor a cell's ground HEAT MARKS: never drawn in the normal views; terrain lane, cross-lane one-line edit
            || tkbType == Hrot.Map.Common.TkbEntityTypes.TerrainFeature || tkbType == Hrot.Map.Common.TkbEntityTypes.TerrainBlock   // ⭐ CE-1966 — nor a placed terrain FEATURE / BLOCK: the terrain mesh draws what it makes, the feature gizmo its handle (it drew the unknown pink box at its anchor); terrain lane, cross-lane
            || IsTerrainDrawnObstacle(tkbType)   // ⭐ CE-4656 (terrain's note) — nor a baked CULTURAL-FEATURE obstacle: the terrain mesh draws its material box (it was ALSO drawn as the unknown box); the car (kind 1) stays ours
            || tkbType == Hrot.Map.Common.TkbEntityTypes.AirArena   // ⭐ CE-4647 — nor an AIR ARENA: AirArenaProjector draws it (a map graphic, no body)
            || tkbType == Fdp.Toolkit.Terrain.Minefields.TkbType || Hrot.Core.Tkb.MinefieldRecordTkb.IsRecord(tkbType)   // ⭐ CE-2468 — the under-fire record too   // ⭐ CE-4624 — nor a MINEFIELD or its records: MinefieldGizmo draws the field (it drew an unknown purple box at its anchor)
            || Hrot.Core.Tkb.TreeDamageTkb.Is(tkbType))   // ⭐ CE-4603 (B-6) — nor a cell's felled trunks (the trees vanish from the terrain); terrain lane, cross-lane   // ⭐ CE-4591 (P-4) — nor a ground cell's craters (drawn into the ground); terrain lane, cross-lane one-line edit   // ⭐ CE-4391 S-1 — nor a building's DAMAGE entity (breaches): drawn into the walls in S-4 (ui, R-4195); a cross-lane one-line edit
            return _looks[tkbType] = new TypeLook(VisualFamily.Unit, Vector3.Zero, DefaultColour(VisualFamily.Unit), false);
        look = Resolve(tkb != null && tkb.TryGetByType(tkbType, out var t) ? t : null);
        _looks[tkbType] = look;
        return look;
    }

    /// <summary>⭐ <c>CE-4656</c> (NOTE_Terrain_To_Ui_Obstacle_Drawing) — a static obstacle the terrain bake draws (every obstacle type but the
    /// platform-typed car, which carries <c>TerrainPrism.DrawnByEntity</c>, plus a wreck).</summary>
    internal static bool IsTerrainDrawnObstacle(long tkbType)
        => tkbType != Hrot.Map.Common.TkbEntityTypes.Obstacle_Car
           && (tkbType == Hrot.Map.Common.TkbEntityTypes.Obstacle_Wreck || Array.IndexOf(Hrot.Map.Common.TkbEntityTypes.Obstacles, tkbType) >= 0);

    /// <summary>⭐ CE-1457 (§6ad-3 TC1) — the kit a look is drawn with: its body style's when a wheeled car type names one, else its family's.</summary>
    public static ShapeKit KitOf(in TypeLook look)
        => look.Family == VisualFamily.WheeledCar && look.Style is { } style ? CarKits.For(style) : ShapeKits.For(look.Family);

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
        else if (VisualFamilies.IsAnimal(family) && Positive(t.GetDescriptor<VehicleParametersDto>()?.Length) is { } animalLength)
        {
            // ⭐ CE-4623 — an animal keeps its kit's PROPORTIONS: scaled uniformly to the TKB's length (backend's width / height are first estimates; the kit's seat height then follows the length)
            size *= animalLength / size.X;
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
        // ⭐ CE-4671 LK-S2 — the PERSON half (child, kit, elderly, sex, wardrobe) is the look library's (PersonLook.FactsOf, one owner, cached per type): a gateway
        //   resolves a man as the map does. CE-4648 (OF-2) a stated age band decides a child, else his height; CE-1162 the type says who he is; OF-7 the wardrobe once per type
        var facts = family == VisualFamily.Person ? Hrot.Look.People.PersonLook.FactsOf(t) : null;
        return new TypeLook(family, size, typeColour ?? DefaultColour(family), VisualFamilies.IsSoldier(t), geometry, weapon,
                            TypeColoured: typeColour != null, Child: facts?.Child ?? false,
                            Kit: facts?.Kit ?? PersonKit.Civilian,
                            Elderly: facts?.Elderly ?? false,
                            // ⭐ CE-1173 (People Display U12) — a weapon-system type that looks like a gun and fires nothing: drawn orange
                            Toy: family == VisualFamily.WeaponSystem && t.GetDescriptor<WeaponSuiteDto>()?.Mounts.Any(HeldWeapons.IsToy) == true,
                            Style: family == VisualFamily.WheeledCar ? CarKits.Parse(t.GetDescriptor<VisualDefinitionDto>()?.BodyStyle) : null,   // ⭐ CE-1457
                            Sex: facts?.Sex ?? PersonSex.Unspecified,
                            Wardrobe: facts?.Wardrobe);
    }

    /// <summary>
    /// ⭐ CE-1033 S2 (P1) — the oriented box an entity is drawn in, which is also what a 3-D click hits: its kit's size, placed
    /// by its reference point (ground centre, or the TKB box offset for a CG-referenced body). False for a unit (no body).
    /// </summary>
    public bool TryGetBox(EntityRepository world, Entity e, ITkbDatabase? tkb, out Vector3 centre, out Quaternion rotation, out Vector3 half)
        => TryGetBoxCore(world, e, tkb, out centre, out rotation, out half);

    /// <summary>⭐ CE-1985 — the TKB type name of what this layer draws for <paramref name="e"/> (e.g. a hover readout's fallback when the unit has no name); null when unknown.</summary>
    public string? TypeNameOf(EntityRepository world, Entity e)
        => world.HasComponent<TkbIdentity>(e) && _tkb() is { } tkb && tkb.TryGetByType((long)world.GetComponentRO<TkbIdentity>(e).TkbType, out var t) ? t.Name : null;

    /// <summary>⭐ CE-1985 — <see cref="TryGetBox"/> with the TKB this layer draws from (a screen-space overlay outlining a body, e.g. the thermal view's).</summary>
    public bool TryGetBox(EntityRepository world, Entity e, out Vector3 centre, out Quaternion rotation, out Vector3 half)
        => TryGetBoxCore(world, e, _tkb(), out centre, out rotation, out half);

    private bool TryGetBoxCore(EntityRepository world, Entity e, ITkbDatabase? tkb, out Vector3 centre, out Quaternion rotation, out Vector3 half)
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
        // ⭐ CE-6503 — a laid bridge is as big as ITS deck (the plan's length over the gap, the type's width and height): the same shape the bake lays the walkables from
        if (look.Family is VisualFamily.LaidAssaultBridge or VisualFamily.LaidFloatingBridge && world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.ObstacleShape>()
            && world.HasComponent<Fdp.Toolkit.Terrain.ObstacleShape>(e))
        {
            ref readonly var shape = ref world.GetComponentRO<Fdp.Toolkit.Terrain.ObstacleShape>(e);
            if (shape.Length > 0f && shape.Width > 0f && shape.Height > 0f) return new Vector3(shape.Length, shape.Width, shape.Height);
        }
        if (look.Family == VisualFamily.Person || !world.IsComponentTypeRegistered<CarKinem.Core.VehicleParams>()
            || !world.HasComponent<CarKinem.Core.VehicleParams>(e)) return look.Size;
        ref readonly var p = ref world.GetComponentRO<CarKinem.Core.VehicleParams>(e);
        return new Vector3(p.Length > 0f ? p.Length : look.Size.X, p.Width > 0f ? p.Width : look.Size.Y, look.Size.Z);
    }

    /// <summary>⭐ CE-1033 S5c (§3.7) — the body colour: the entity's own <see cref="EntityAppearance"/> (scenario / per spawn) →
    /// the TKB's <c>VisualData.ColorHex</c> → the family default.</summary>
    public static Color ColourOf(EntityRepository world, Entity e, Color fallback)
        => Hrot.Look.People.PersonLook.ColourOf(world, e, fallback.ToRgba()).ToColor();   // ⭐ CE-4671 LK-S2 — the look library's read (one owner)

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
        ColourRole.Canvas => Canvas,   // ⭐ CE-1104 (§6t)
        ColourRole.Light => new Color((byte)Math.Min(255, body.R * 1.3f + 12), (byte)Math.Min(255, body.G * 1.3f + 12), (byte)Math.Min(255, body.B * 1.3f + 12), (byte)255),   // ⭐ CE-4623
        ColourRole.Bone => new Color(226, 218, 196, 255),
        ColourRole.Red => new Color(190, 40, 36, 255),
        ColourRole.Amber => new Color(225, 165, 40, 255),
        ColourRole.Wood => new Color(139, 96, 54, 255),   // ⭐ CE-1976 — a bow, an arrow, a crossbow stock
        ColourRole.Dark => Dark,
        ColourRole.Glass => Glass,
        ColourRole.Metal => Metal,
        _ => body,
    };

    public static Color DefaultColour(VisualFamily family) => family switch
    {
        VisualFamily.Person => Hrot.Look.People.PersonLook.DefaultColour.ToColor(),   // ⭐ CE-4671 LK-S2
        VisualFamily.Tank => new Color(112, 120, 82, 255),
        VisualFamily.Dragon => new Color(122, 31, 31, 255),   // ⭐ CE-4634 — TKB 411's own #7A1F1F (the TKB's colour wins anyway)
        VisualFamily.Afv => new Color(122, 116, 84, 255),
        VisualFamily.WheeledCar => new Color(72, 104, 168, 255),
        VisualFamily.WheeledUtility => new Color(128, 120, 92, 255),
        VisualFamily.Helicopter => new Color(96, 104, 92, 255),
        VisualFamily.AttackHelicopter => new Color(86, 96, 79, 255),
        VisualFamily.Jet => new Color(128, 136, 146, 255),
        VisualFamily.CargoPlane => new Color(118, 124, 116, 255),
        VisualFamily.Drone => new Color(74, 74, 74, 255),
        VisualFamily.WeaponSystem => new Color(84, 92, 70, 255),
        VisualFamily.SpArtillery => new Color(112, 120, 82, 255),
        VisualFamily.AttackJet => new Color(122, 128, 118, 255),
        VisualFamily.TwinEngineJet => new Color(120, 128, 140, 255),   // ⭐ CE-4647 — a slightly bluer grey than the F-16 (the faction / TKB colour wins)
        VisualFamily.Bulldozer => new Color(227, 176, 28, 255),   // ⭐ CE-6007 — construction yellow (the TKB's own colour wins)
        VisualFamily.MineClearer or VisualFamily.BridgeLayer or VisualFamily.FloatingBridge or VisualFamily.LaidFloatingBridge => new Color(98, 108, 66, 255),   // ⭐ CE-6503 — the floating bridge's pontoons too
        VisualFamily.LaidAssaultBridge => new Color(122, 126, 112, 255),   // ⭐ CE-6503 — bare steel   // ⭐ CE-6502 — the bridge engineers too   // ⭐ CE-4624 — engineer olive (the mockup's; the faction / TKB colour wins)
        VisualFamily.Motorcycle => new Color(96, 104, 84, 255),   // ⭐ CE-4621 — a drab bike (the faction / TKB colour wins)
        VisualFamily.AirDefenceVehicle => new Color(92, 104, 76, 255),   // ⭐ CE-4618 — army olive (the faction / TKB colour wins)
        (>= VisualFamily.Cattle and <= VisualFamily.Goat) or VisualFamily.Rabbit => AnimalKits.DefaultColour(family),   // ⭐ CE-4623 — an animal's own coat (a seeded one wins, below)
        _ => new Color(180, 120, 200, 255),
    };

    /// <summary><c>#RRGGBB</c> or <c>#RRGGBBAA</c> (alpha ignored — bodies are opaque); null for anything else.</summary>
    public static Color? ParseHex(string? hex) => Hrot.Look.Rgba.FromHex(hex).ToColor();   // ⭐ CE-4671 LK-S2 — the look library's parser (one owner)

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
        _cubeBucket = _batch.Register(_cube);           // ⭐ CE-4555 — one bucket per mesh
        _cylinderBucket = _batch.Register(_cylinder);
        _coneBucket = _batch.Register(_cone);
        _meshes = true;
    }

    public void Dispose()
    {
        if (!_meshes) return;
        Raylib.UnloadMesh(_cube);
        Raylib.UnloadMesh(_cylinder);
        Raylib.UnloadMesh(_cone);
        _batch.Dispose();   // ⭐ CE-4555 — its buffers; the meshes are registered again with them
        _meshes = false;
    }
}
