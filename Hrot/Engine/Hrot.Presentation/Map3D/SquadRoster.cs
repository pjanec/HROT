using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1214</c> (docs/DESIGN_Entity_Control.md §8j T-1) — TEAM SWITCH: the men a first-person operator may jump to with Tab /
/// Shift+Tab — his SQUAD as the replicated <see cref="UnitSubordinate.Commander"/> draws it: his leader, the men naming that same leader,
/// and his own men — alive, with a network id, not held by another station — in network-id order. ⛔ Not any unit of the side: that is the map's job.
/// </summary>
public static class SquadRoster
{
    /// <summary>The squad of <paramref name="unit"/> (himself included), in network-id order.</summary>
    public static List<(long Id, Entity Unit)> Of(EntityRepository world, Entity unit)
    {
        var roster = new List<(long, Entity)>();
        if (unit.IsNull || !world.HasSingletonManaged<NetworkEntityMap>() || !world.IsComponentTypeRegistered<UnitSubordinate>()) return roster;
        var map = world.GetSingletonManaged<NetworkEntityMap>()!;
        var leader = world.HasComponent<UnitSubordinate>(unit) ? world.GetComponentRO<UnitSubordinate>(unit).Commander : Entity.Null;
        var orders = new Hrot.Common.Orders.EntityOrders(world, map, geo: null);
        void Add(Entity e)
        {
            if (e.IsNull || !Fdp.Toolkit.Combat.CombatLife.IsAlive(world, e) || !map.TryGetNetworkId(e, out long id)) return;
            if (world.IsComponentTypeRegistered<PossessedBy>() && world.HasComponent<PossessedBy>(e)
                && world.GetComponentRO<PossessedBy>(e).StationNodeId != orders.StationFor(e)) return;   // another station's
            if (!roster.Exists(r => r.Item2 == e)) roster.Add((id, e));
        }
        Add(unit);
        Add(leader);
        foreach (var e in world.Query().With<UnitSubordinate>().Build())
        {
            var commander = world.GetComponentRO<UnitSubordinate>(e).Commander;
            if (commander == unit || (!leader.IsNull && commander == leader)) Add(e);
        }
        roster.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return roster;
    }

    /// <summary>The man <paramref name="step"/> places on from <paramref name="unit"/> in his squad (+1 next, −1 previous, wrapping), or
    /// <see cref="Entity.Null"/> when he is alone.</summary>
    public static Entity Next(EntityRepository world, Entity unit, int step)
    {
        var roster = Of(world, unit);
        if (roster.Count < 2) return Entity.Null;
        int at = roster.FindIndex(r => r.Unit == unit);
        if (at < 0) return Entity.Null;
        return roster[((at + step) % roster.Count + roster.Count) % roster.Count].Unit;
    }
}
