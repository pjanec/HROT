using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1217</c> (docs/DESIGN_Entity_Control.md §8k C-1) — the first-person COMPASS strip: the heading the view looks along (0 = north,
/// clockwise, as the flyer's <c>HDG</c>) and the ticks a strip ±<see cref="HalfSpanDegrees"/> wide shows, every 15°, the eight points named.
/// </summary>
public static class FpsCompass
{
    /// <summary>How far either side of the heading the strip reaches (degrees).</summary>
    public const float HalfSpanDegrees = 60f;
    /// <summary>A tick every … degrees.</summary>
    public const int StepDegrees = 15;

    private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>The heading of <paramref name="forward"/> (world: X east, Y north), degrees 0 … 360, clockwise from north; 0 when vertical.</summary>
    public static float Heading(Vector3 forward)
    {
        if (forward.X * forward.X + forward.Y * forward.Y < 1e-10f) return 0f;
        float h = MathF.Atan2(forward.X, forward.Y) * 180f / MathF.PI;
        return h < 0f ? h + 360f : h;
    }

    /// <summary>The ticks the strip shows round <paramref name="heading"/>: each one's place (−1 left end … 1 right end) and its label
    /// (a point's name at the 45s, else empty).</summary>
    public static IEnumerable<(float At, int Degrees, string Label)> Ticks(float heading)
    {
        int first = (int)MathF.Ceiling((heading - HalfSpanDegrees) / StepDegrees) * StepDegrees;
        for (int d = first; d <= heading + HalfSpanDegrees; d += StepDegrees)
        {
            int wrapped = ((d % 360) + 360) % 360;
            yield return ((d - heading) / HalfSpanDegrees, wrapped, wrapped % 45 == 0 ? Points[wrapped / 45] : "");
        }
    }
}

/// <summary>
/// ⭐ <c>CE-1217</c> (docs/DESIGN_Entity_Control.md §8k C-2) — the FRIENDS whose names a first-person operator sees over their heads: the
/// units of HIS side (<see cref="EntityInfo.ForceId"/>, never Neutral) within <see cref="ReachMetres"/> of his eye, alive, named, not himself;
/// his own men (<see cref="UnitSubordinate.Commander"/> = him) marked. ⛔ Not the gizmo cards — those show network ids and follow the
/// layer toggles (a debugging view).
/// </summary>
public static class FriendMarks
{
    /// <summary>How far the names reach (m).</summary>
    public const float ReachMetres = 50f;

    /// <summary>One friend: the entity, its name, where it stands, and whether he is one of the operator's own men.</summary>
    public readonly record struct Friend(Entity Unit, string Name, Vector3 At, bool Mine);

    /// <summary>The friends of <paramref name="me"/> round <paramref name="eye"/>, nearest first.</summary>
    public static List<Friend> Of(EntityRepository world, Entity me, Vector3 eye)
    {
        var friends = new List<Friend>();
        if (!world.IsComponentTypeRegistered<EntityInfo>() || !world.HasComponent<EntityInfo>(me)) return friends;
        var side = world.GetComponentRO<EntityInfo>(me).ForceId;
        if (side == ForceId.Neutral) return friends;
        bool chain = world.IsComponentTypeRegistered<UnitSubordinate>();
        foreach (var e in world.Query().With<EntityInfo>().With<SimTransform>().Build())
        {
            if (e == me || !Fdp.Toolkit.Combat.CombatLife.IsAlive(world, e)) continue;
            ref readonly var info = ref world.GetComponentRO<EntityInfo>(e);
            if (info.ForceId != side) continue;
            var at = world.GetComponentRO<SimTransform>(e).Position;
            if (Vector3.Distance(at, eye) > ReachMetres) continue;
            string name = info.Name.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            bool mine = chain && world.HasComponent<UnitSubordinate>(e) && world.GetComponentRO<UnitSubordinate>(e).Commander == me;
            friends.Add(new Friend(e, name, at, mine));
        }
        friends.Sort((a, b) => Vector3.DistanceSquared(a.At, eye).CompareTo(Vector3.DistanceSquared(b.At, eye)));
        return friends;
    }
}
