using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Vis2D.Shapes;

/// <summary>
/// Built-in shape library that covers the four canonical DIS entity classes
/// (ground vehicle, lifeform, fixed-wing aircraft, rotary-wing aircraft) plus
/// custom named profiles registered at startup.
///
/// <para>
/// All vertices are in <b>normalized local space</b>: X <c>[-0.5, 0.5]</c>
/// maps to entity length and Y <c>[-0.5, 0.5]</c> maps to entity width.
/// Z coordinates above zero produce a perspective-parallax "lift" when combined
/// with the exaggeration coefficient in
/// <see cref="Fdp.Toolkit.Vis2D.Rendering.PerspectiveShapeRenderer"/>.
/// </para>
///
/// <para>
/// DIS domain / kind constants used for shape selection:
/// <list type="bullet">
///   <item><b>Kind 1</b> — Platform.</item>
///   <item><b>Kind 3</b> — Lifeform.</item>
///   <item><b>Domain 1</b> — Land.</item>
///   <item><b>Domain 2</b> — Air.</item>
///   <item>Air Category &lt; 20 — Fixed Wing; >= 20 — Rotary Wing.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DefaultEntityShapeLibrary : IEntityShapeLibrary
{
    // ── Public shape-name constants ───────────────────────────────────────────

    public const string GroundVehicle = "GroundVehicle";
    public const string Humanoid      = "Humanoid";
    public const string FixedWing     = "FixedWing";
    public const string RotaryWing    = "RotaryWing";

    // ── Internal state ────────────────────────────────────────────────────────

    private readonly Dictionary<string, EntityShapeProfile> _profiles
        = new(System.StringComparer.OrdinalIgnoreCase);

    // ── Construction ──────────────────────────────────────────────────────────

    public DefaultEntityShapeLibrary()
    {
        Register(BuildGroundVehicle());
        Register(BuildHumanoid());
        Register(BuildFixedWing());
        Register(BuildRotaryWing());
    }

    // ── IEntityShapeLibrary ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public EntityShapeProfile GetShape(string? shapeName, DISEntityType fallbackDisType)
    {
        // 1. Explicit named lookup.
        if (!string.IsNullOrEmpty(shapeName) &&
            _profiles.TryGetValue(shapeName, out var named))
            return named;

        // 2. DIS-based fallback.
        return SelectByDisType(fallbackDisType);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a custom <see cref="EntityShapeProfile"/> so it can be referenced
    /// by name from <c>VisualData.MapShapeName</c>.  Overwrites any profile with the
    /// same name.
    /// </summary>
    public void Register(EntityShapeProfile profile)
        => _profiles[profile.Name] = profile;

    // ── Private helpers ───────────────────────────────────────────────────────

    private EntityShapeProfile SelectByDisType(DISEntityType dis)
    {
        // Lifeform (Kind 3).
        if (dis.Kind == 3 && _profiles.TryGetValue(Humanoid, out var hum))
            return hum;

        // Air domain (Domain 2).
        if (dis.Domain == 2)
        {
            string key = dis.Category >= 20 ? RotaryWing : FixedWing;
            if (_profiles.TryGetValue(key, out var air))
                return air;
        }

        // Default — ground vehicle.
        return _profiles.TryGetValue(GroundVehicle, out var gv)
            ? gv
            : new EntityShapeProfile { Name = "_fallback", Elements = System.Array.Empty<PolylineDefinition>() };
    }

    // ── Shape builders ────────────────────────────────────────────────────────

    private static EntityShapeProfile BuildGroundVehicle() =>
        new()
        {
            Name = GroundVehicle,
            Elements = new[]
            {
                // Filled body rectangle — four corners, always drawn.
                new PolylineDefinition
                {
                    IsFilled  = true,
                    IsClosed  = true,
                    LocalVertices = new[]
                    {
                        new Vector3( 0.5f,  0.5f, 0f), // front-left
                        new Vector3( 0.5f, -0.5f, 0f), // front-right
                        new Vector3(-0.5f, -0.5f, 0f), // rear-right
                        new Vector3(-0.5f,  0.5f, 0f), // rear-left
                    },
                },
                // Forward indicator: center to front-center nub.
                new PolylineDefinition
                {
                    IsFilled      = false,
                    IsClosed      = false,
                    LineThickness = 1.5f,
                    LocalVertices = new[]
                    {
                        new Vector3(0f,    0f, 0f), // center
                        new Vector3(0.65f, 0f, 0f), // forward nub (slightly beyond nose)
                    },
                },
                // Damage overlay — a short diagonal cross, shown only when damaged.
                new PolylineDefinition
                {
                    IsFilled      = false,
                    IsClosed      = false,
                    ShowWhen      = EntityShapeCondition.Damaged | EntityShapeCondition.Immobile,
                    LineThickness = 1.5f,
                    LocalVertices = new[]
                    {
                        new Vector3(-0.3f, -0.3f, 0f),
                        new Vector3( 0.3f,  0.3f, 0f),
                    },
                },
                new PolylineDefinition
                {
                    IsFilled      = false,
                    IsClosed      = false,
                    ShowWhen      = EntityShapeCondition.Damaged | EntityShapeCondition.Immobile,
                    LineThickness = 1.5f,
                    LocalVertices = new[]
                    {
                        new Vector3( 0.3f, -0.3f, 0f),
                        new Vector3(-0.3f,  0.3f, 0f),
                    },
                },
            },
        };

    private static EntityShapeProfile BuildHumanoid() =>
        new()
        {
            Name = Humanoid,
            Elements = new[]
            {
                // Torso / shoulder line.
                new PolylineDefinition
                {
                    IsFilled      = false,
                    IsClosed      = false,
                    LineThickness = 3f,
                    LocalVertices = new[]
                    {
                        new Vector3(0f, -0.5f, 0f), // right shoulder
                        new Vector3(0f,  0.5f, 0f), // left shoulder
                    },
                },
                // Head — represented as a short forward stub from center.
                new PolylineDefinition
                {
                    IsFilled      = false,
                    IsClosed      = false,
                    LineThickness = 3f,
                    LocalVertices = new[]
                    {
                        new Vector3(0f,    0f, 0f),
                        new Vector3(0.35f, 0f, 0f),
                    },
                },
            },
        };

    private static EntityShapeProfile BuildFixedWing() =>
        new()
        {
            Name = FixedWing,
            Elements = new[]
            {
                // Swept-delta fuselage body (filled).
                new PolylineDefinition
                {
                    IsFilled  = true,
                    IsClosed  = true,
                    LocalVertices = new[]
                    {
                        new Vector3( 0.5f,  0f,   0f), // nose
                        new Vector3(-0.2f,  0.5f, 0f), // left wing-root
                        new Vector3(-0.5f,  0f,   0f), // tail
                        new Vector3(-0.2f, -0.5f, 0f), // right wing-root
                    },
                },
                // Forward fuselage spine.
                new PolylineDefinition
                {
                    IsFilled      = false,
                    IsClosed      = false,
                    LineThickness = 2f,
                    LocalVertices = new[]
                    {
                        new Vector3(-0.5f, 0f, 0f), // tail
                        new Vector3( 0.5f, 0f, 0f), // nose
                    },
                },
            },
        };

    private static EntityShapeProfile BuildRotaryWing() =>
        new()
        {
            Name = RotaryWing,
            Elements = new[]
            {
                // Narrow teardrop body (filled).
                new PolylineDefinition
                {
                    IsFilled  = true,
                    IsClosed  = true,
                    LocalVertices = new[]
                    {
                        new Vector3( 0.5f,  0f,   0f),  // nose
                        new Vector3(-0.2f,  0.3f, 0f),  // left body
                        new Vector3(-0.5f,  0f,   0f),  // tail
                        new Vector3(-0.2f, -0.3f, 0f),  // right body
                    },
                },
                // Rotor disk line — elevated in Z so rolling produces strong parallax.
                // Left tip is at +Z, right tip at +Z; when the helicopter rolls, one tip
                // rises and the other sinks relative to the camera, stretching and
                // compressing the rotor line visually.
                new PolylineDefinition
                {
                    IsFilled      = false,
                    IsClosed      = false,
                    LineThickness = 1.5f,
                    LocalVertices = new[]
                    {
                        new Vector3(0f,  0.6f, 0.5f), // left rotor tip (elevated)
                        new Vector3(0f, -0.6f, 0.5f), // right rotor tip (elevated)
                    },
                },
            },
        };
}
