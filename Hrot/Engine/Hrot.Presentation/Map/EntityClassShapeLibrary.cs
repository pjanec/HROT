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
    };

    public EntityShapeProfile GetShape(string? shapeName, ulong fallbackDisType)
    {
        if (!string.IsNullOrEmpty(shapeName) && _outlines.TryGetProfile(shapeName, out var named)) return named;
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
