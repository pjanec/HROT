using System.Collections.Generic;
using Fdp.Core;
using GizmoMap.Presentation.Shapes;
using Hrot.Core.Tkb;

namespace Hrot.Presentation.Map;

/// <summary>
/// ⭐ CE-2138 (C2, rule K1, R-280 (behaviors)) — the 2-D map avatar classified by <see cref="EntityClassTable"/>, so the 2-D
/// symbol answers "what is this" from the SAME table as the 3-D model, the picker and the behaviours. The outlines themselves
/// stay <see cref="DefaultEntityShapeLibrary"/>'s; only the choice moves here.
/// <para>⚠ <c>GizmoMap</c> is an external dependency that cannot see <c>Hrot.Core</c>, so its own library keeps a DIS switch for
/// non-HROT hosts; every HROT host passes this one (<c>MapInteractionPack.BuildRenderLayer</c>).</para>
/// </summary>
public sealed class EntityClassShapeLibrary : IEntityShapeLibrary
{
    private readonly DefaultEntityShapeLibrary _outlines = new();

    /// <summary>The classes that pick a profile; a type takes the first one found walking its chain from the most specific
    /// class up. Every name here is a class of <see cref="EntityClassTable.Default"/> (rail).</summary>
    public static IReadOnlyDictionary<string, string> ProfileOfClass { get; } = new Dictionary<string, string>
    {
        ["Helicopter"] = DefaultEntityShapeLibrary.RotaryWing,
        ["AirPlatform"] = DefaultEntityShapeLibrary.FixedWing,
        ["LandPlatform"] = DefaultEntityShapeLibrary.GroundVehicle,
        ["Person"] = DefaultEntityShapeLibrary.Humanoid,
        ["CulturalFeature"] = DefaultEntityShapeLibrary.CulturalFeature,   // ⭐ CE-3148 — a static obstacle's footprint
        ["ScenarioMacro"] = DefaultEntityShapeLibrary.ScenarioMacro,       // ⭐ SR-178 — the instance's OWN marker (a square frame with an M); its colour is Hrot.Common MacroMarker, set in EntityPresentationGizmo
    };

    // ── ⭐ CE-4623 (animals AU-2, AR-19 / AR-20) — an animal is drawn on the 2-D map as a top-down BODY outline, never the man's line (Kind 3 would say Humanoid) ────────────────────────────

    /// <summary>The profile names of the animals' outlines.</summary>
    public const string AnimalQuadruped = "animal_quadruped", AnimalBird = "animal_bird", AnimalFlock = "animal_flock";

    private static PolylineDefinition Closed(params (float X, float Y)[] pts)
    {
        var v = new System.Numerics.Vector3[pts.Length];
        for (int i = 0; i < pts.Length; i++) v[i] = new System.Numerics.Vector3(pts[i].X, pts[i].Y, 0f);
        return new PolylineDefinition { LocalVertices = v, IsClosed = true, IsFilled = false, LineThickness = 1f };
    }

    private static readonly Dictionary<string, EntityShapeProfile> AnimalOutlines = new()
    {
        // a body, a neck and a head (x forward, y left; −0.5…0.5 of the length and width)
        [AnimalQuadruped] = new EntityShapeProfile { Name = AnimalQuadruped, Elements = new[]
        {
            Closed((-0.50f, -0.38f), (-0.44f, -0.50f), (0.08f, -0.50f), (0.20f, -0.30f), (0.34f, -0.22f), (0.50f, -0.14f), (0.50f, 0.14f), (0.34f, 0.22f), (0.20f, 0.30f), (0.08f, 0.50f), (-0.44f, 0.50f), (-0.50f, 0.38f)),
        } },
        // a bird from above: a body and two wings
        [AnimalBird] = new EntityShapeProfile { Name = AnimalBird, Elements = new[]
        {
            Closed((-0.50f, 0f), (-0.20f, -0.08f), (0.10f, -0.50f), (0.18f, -0.50f), (0.18f, -0.08f), (0.50f, 0f), (0.18f, 0.08f), (0.18f, 0.50f), (0.10f, 0.50f), (-0.20f, 0.08f)),
        } },
        // a flock: a loose cluster of small birds
        [AnimalFlock] = new EntityShapeProfile { Name = AnimalFlock, Elements = new[]
        {
            Closed((-0.4f, -0.1f), (-0.3f, -0.05f), (-0.4f, 0.0f)), Closed((-0.1f, 0.3f), (0.0f, 0.35f), (-0.1f, 0.4f)), Closed((0.1f, -0.3f), (0.2f, -0.25f), (0.1f, -0.2f)),
            Closed((0.3f, 0.1f), (0.4f, 0.15f), (0.3f, 0.2f)), Closed((-0.2f, -0.35f), (-0.1f, -0.3f), (-0.2f, -0.25f)), Closed((0.0f, 0.0f), (0.1f, 0.05f), (0.0f, 0.1f)),
        } },
    };

    /// <summary>⭐ CE-4623 — the animal outline of a DIS type, or null when it is no animal.</summary>
    public static string? AnimalProfileNameOf(DISEntityType dis)
    {
        var family = Hrot.UI.Common.Map3D.VisualFamilies.AnimalOfDis(dis);
        if (family == Hrot.UI.Common.Map3D.VisualFamily.Unknown) return null;
        return family == Hrot.UI.Common.Map3D.VisualFamily.Bird ? (dis.Subcategory == 2 ? AnimalFlock : AnimalBird) : AnimalQuadruped;
    }

    public EntityShapeProfile GetShape(string? shapeName, ulong fallbackDisType)
    {
        if (!string.IsNullOrEmpty(shapeName) && _outlines.TryGetProfile(shapeName, out var named)) return named;
        if (AnimalProfileNameOf(new DISEntityType { Value = fallbackDisType }) is { } animal) return AnimalOutlines[animal];   // ⭐ CE-4623 — before the class walk: Kind 3 would be the man's line
        var name = ProfileNameOf(new DISEntityType { Value = fallbackDisType });
        return name != null && _outlines.TryGetProfile(name, out var profile)
            ? profile
            : new EntityShapeProfile { Name = DefaultEntityShapeLibrary.Fallback };
    }

    /// <summary>The profile name for a DIS type, or null (drawn as nothing — a body-less unit, an unknown kind).</summary>
    public static string? ProfileNameOf(DISEntityType dis)
    {
        var chain = EntityClassTable.Default.Chain(dis);
        for (int i = chain.Count - 1; i >= 0; i--)
            if (ProfileOfClass.TryGetValue(chain[i].Name, out var name)) return name;
        return null;
    }
}
