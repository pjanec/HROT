using System.Globalization;
using System.Numerics;
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
    public readonly record struct TypeLook(VisualFamily Family, Vector3 Size, Color Colour, bool Soldier,
                                           BodyGeometryDto? Geometry = null);

    /// <param name="poseOf">⭐ CE-1033 §3.11 — the turret azimuth and gun elevation of an entity with an articulated kit. ⚠ Null today
    /// in production: the component that will carry the pose awaits the user's ruling (§3.11, M23–M26); until then turrets face
    /// forward. The seam is here so that ruling plugs in at ONE place.</param>
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

    public void Update(float dt) { }
    public void Draw(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    public void Draw3D(RenderContext ctx)
    {
        BodiesDrawn = PartsDrawn = 0;
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<TkbIdentity>() || !world.IsComponentTypeRegistered<SimTransform>()) return;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<SimTransform>().With<TkbIdentity>().WithLifecycle(EntityLifecycle.All).Build();
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
            long type = world.GetComponentRO<TkbIdentity>(e).TkbType;
            var look = LookOf(type, tkb);
            if (look.Family == VisualFamily.Unit) continue;

            var size = SizeOf(world, e, look);
            var colour = ColourOf(world, e, look.Colour);
            KitPart[] parts;
            KitPivots pivots = default;
            var pose = ArticulationPose.Neutral;
            if (look.Family == VisualFamily.Person)
                parts = FigurePose(world, e, look.Soldier);
            else
            {
                var kit = ShapeKits.For(look.Family);
                parts = kit.Parts;
                pivots = kit.Pivots;
                if (_poseOf != null && kit.IsArticulated) pose = _poseOf(world, e);
            }

            ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
            var body = Matrix4x4.CreateFromQuaternion(tf.Rotation) * Matrix4x4.CreateTranslation(tf.Position);
            // ⭐ CE-1033 S4 — wheels roll, limbs swing, a rotor turns while flying (MotionTracker); a parked helicopter's rotor stands.
            bool rotorTurning = look.Family == VisualFamily.Helicopter && IsAirborne(world, tf.Position, look.Geometry);
            var motion = _motion.Advance(e, tf.Position, tf.Rotation, look.Family, size, simTime, rotorTurning);
            _drawn.Add(e);
            // ⭐ CE-1041 — a type with Body.Geometry: the kit's box sits where the TKB puts it relative to the reference point
            //   (an aircraft's CG), and the gear is drawn AT the TKB's contact points instead of the kit's generic gear.
            var geometry = look.Geometry;
            var kitToBody = geometry == null
                ? Matrix4x4.Identity
                : Matrix4x4.CreateTranslation(geometry.BodyCentreX, geometry.BodyCentreY, geometry.BodyCentreZ - size.Z / 2f);
            foreach (var part in parts)
            {
                if (geometry != null && part.Role == PartRole.Gear) continue;
                var model = HrotToRaylib.ModelFromHrot(part.BodyTransform(size, pivots, pose, motion) * kitToBody * body);
                var mesh = part.Shape switch { PartShape.Cylinder => _cylinder, PartShape.Cone => _cone, _ => _cube };
                Raylib.DrawMesh(mesh, shader.Tinted(Shade(part.Colour, colour)), model);
                PartsDrawn++;
            }
            if (geometry != null) DrawGear(world, geometry, tf.Position, body, shader);
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
        _motion.EndFrame(_drawn);
    }

    /// <summary>⭐ CE-1033 S4 — the figure's parts: the logical stance (the shared rule), BLENDED from the stance it is leaving while
    /// the muscle reports a transition in progress (<see cref="StanceStatus"/> — replicated, so every host shows the same blend).</summary>
    private static KitPart[] FigurePose(EntityRepository world, Entity e, bool soldier)
    {
        var target = LogicalStance.Of(world, e);
        if (world.IsComponentTypeRegistered<StanceStatus>() && world.HasComponent<StanceStatus>(e))
        {
            ref readonly var status = ref world.GetComponentRO<StanceStatus>(e);
            if (status.Phase == StanceTransitionPhase.Transitioning && status.CurrentStance != target)
                return BlockFigure.Blend(status.CurrentStance, target, status.TransitionProgress, soldier);
        }
        return BlockFigure.Parts(target, soldier);
    }

    private readonly MotionTracker _motion = new();
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
    private void DrawGear(EntityRepository world, BodyGeometryDto geometry, Vector3 position, Matrix4x4 body, LitShader shader)
    {
        bool airborne = IsAirborne(world, position, geometry);
        float skidLeftMin = float.MaxValue, skidLeftMax = float.MinValue, skidRightMin = float.MaxValue, skidRightMax = float.MinValue;
        float skidZ = 0f, skidLeftY = 0f, skidRightY = 0f;
        foreach (var c in geometry.GroundContacts)
        {
            if (c.Retractable && airborne) continue;
            float strut = c.StrutLength > 0f ? c.StrutLength : 1f;
            float hub = c.Z + c.WheelRadius;
            DrawPart(_cube, Matrix4x4.CreateScale(0.14f, 0.14f, strut) * Matrix4x4.CreateTranslation(c.X, c.Y, hub + strut / 2f), Metal, body, shader);
            if (c.Kind == GroundContactKind.Wheel && c.WheelRadius > 0f)
            {
                float d = 2f * c.WheelRadius;
                DrawPart(_cylinder, Matrix4x4.CreateTranslation(0, 0, -0.5f) * Matrix4x4.CreateScale(d, d, MathF.Max(0.12f, 0.6f * c.WheelRadius))
                                    * Matrix4x4.CreateRotationX(-MathF.PI / 2f) * Matrix4x4.CreateTranslation(c.X, c.Y, hub), Dark, body, shader);
            }
            else if (c.Kind == GroundContactKind.Skid)
            {
                skidZ = c.Z;
                if (c.Y >= 0f) { skidLeftMin = MathF.Min(skidLeftMin, c.X); skidLeftMax = MathF.Max(skidLeftMax, c.X); skidLeftY = c.Y; }
                else { skidRightMin = MathF.Min(skidRightMin, c.X); skidRightMax = MathF.Max(skidRightMax, c.X); skidRightY = c.Y; }
            }
        }
        if (skidLeftMax > skidLeftMin) DrawSkid(skidLeftMin, skidLeftMax, skidLeftY, skidZ, body, shader);
        if (skidRightMax > skidRightMin) DrawSkid(skidRightMin, skidRightMax, skidRightY, skidZ, body, shader);
    }

    private void DrawSkid(float x0, float x1, float y, float z, Matrix4x4 body, LitShader shader)
        => DrawPart(_cube, Matrix4x4.CreateScale(x1 - x0 + 0.6f, 0.12f, 0.1f) * Matrix4x4.CreateTranslation((x0 + x1) / 2f, y, z + 0.05f), Dark, body, shader);

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
    public TypeLook LookOf(long tkbType, ITkbDatabase? tkb)
    {
        if (_looks.TryGetValue(tkbType, out var look)) return look;
        // ⭐ CE-1042 — an EFFECT type (muzzle flash, explosion, decal, tracer) has no body: the effect layer draws it. Checked by
        //   the catalog too, so a host with no TKB (the replay browser) does not draw a recorded effect as an unknown box.
        if (Hrot.Core.Tkb.EffectTkbCatalog.BuiltIn.ContainsKey(tkbType))
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
        var family = VisualFamilies.Of(t);
        var size = ShapeKits.DefaultSize(family);
        var geometry = t.GetDescriptor<BodyGeometryDto>();
        if (geometry is { Length: > 0f, Width: > 0f, Height: > 0f })
        {
            size = new Vector3(geometry.Length, geometry.Width, geometry.Height);   // ⭐ CE-1041 — engine-neutral size wins
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
        var colour = ParseHex(t.GetDescriptor<VisualDefinitionDto>()?.ColorHex) ?? DefaultColour(family);
        return new TypeLook(family, size, colour, VisualFamilies.IsSoldier(t), geometry);
    }

    /// <summary>
    /// ⭐ CE-1033 S2 (P1) — the oriented box an entity is drawn in, which is also what a 3-D click hits: its kit's size, placed
    /// by its reference point (ground centre, or the TKB box offset for a CG-referenced body). False for a unit (no body).
    /// </summary>
    public bool TryGetBox(EntityRepository world, Entity e, ITkbDatabase? tkb, out Vector3 centre, out Quaternion rotation, out Vector3 half)
    {
        centre = default; rotation = Quaternion.Identity; half = default;
        if (!world.HasComponent<TkbIdentity>(e) || !world.HasComponent<SimTransform>(e)) return false;
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

    private static Color Shade(ColourRole role, Color body) => role switch
    {
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
