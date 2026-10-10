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
    public readonly record struct TypeLook(VisualFamily Family, Vector3 Size, Color Colour, bool Soldier);

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
                parts = BlockFigure.Parts(LogicalStance.Of(world, e), look.Soldier);
            else
            {
                var kit = ShapeKits.For(look.Family);
                parts = kit.Parts;
                pivots = kit.Pivots;
                if (_poseOf != null && kit.IsArticulated) pose = _poseOf(world, e);
            }

            ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
            var body = Matrix4x4.CreateFromQuaternion(tf.Rotation) * Matrix4x4.CreateTranslation(tf.Position);
            foreach (var part in parts)
            {
                var model = HrotToRaylib.ModelFromHrot(part.BodyTransform(size, pivots, pose) * body);
                var mesh = part.Shape switch { PartShape.Cylinder => _cylinder, PartShape.Cone => _cone, _ => _cube };
                Raylib.DrawMesh(mesh, shader.Tinted(Shade(part.Colour, colour)), model);
                PartsDrawn++;
            }
            BodiesDrawn++;
        }
    }

    /// <summary>What <paramref name="tkbType"/> looks like: its family, TKB size (else the family's) and colour.</summary>
    public TypeLook LookOf(long tkbType, ITkbDatabase? tkb)
    {
        if (_looks.TryGetValue(tkbType, out var look)) return look;
        look = Resolve(tkb != null && tkb.TryGetByType(tkbType, out var t) ? t : null);
        _looks[tkbType] = look;
        return look;
    }

    /// <summary>The look of a template (null ⇒ an unknown box). Pure — the rail runs it over the whole built-in catalog.</summary>
    public static TypeLook Resolve(TkbTemplate? t)
    {
        if (t == null) return new TypeLook(VisualFamily.Unknown, ShapeKits.DefaultSize(VisualFamily.Unknown), DefaultColour(VisualFamily.Unknown), false);
        var family = VisualFamilies.Of(t);
        var size = ShapeKits.DefaultSize(family);
        if (family != VisualFamily.Person && family != VisualFamily.Unit)
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
        return new TypeLook(family, size, colour, VisualFamilies.IsSoldier(t));
    }

    private static Vector3 SizeOf(EntityRepository world, Entity e, in TypeLook look)
    {
        if (look.Family == VisualFamily.Person || !world.IsComponentTypeRegistered<CarKinem.Core.VehicleParams>()
            || !world.HasComponent<CarKinem.Core.VehicleParams>(e)) return look.Size;
        ref readonly var p = ref world.GetComponentRO<CarKinem.Core.VehicleParams>(e);
        return new Vector3(p.Length > 0f ? p.Length : look.Size.X, p.Width > 0f ? p.Width : look.Size.Y, look.Size.Z);
    }

    private static Color ColourOf(EntityRepository world, Entity e, Color fallback)
    {
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
