using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Orders;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1213</c> (docs/DESIGN_Entity_Control.md §8j W-1) — the first-person COMMAND WHEEL: while T is held, a ring of the orders the
/// possessed leader's MEN fulfil (his men are the units whose replicated <see cref="UnitSubordinate.Commander"/> is him); the mouse picks a
/// sector, letting go of T sends it to every one of them, completed from the crosshair (a point, or the unit under it). No new order path:
/// the order vocabulary is <see cref="OrderMenu"/>, the draft and the send are the RTS tool's (<c>RtsCommands</c>: one draft per man, his
/// standing designations filling what the crosshair did not, sent through <see cref="EntityOrders"/>). ⛔ Never the leader himself: he is
/// possessed, the operator is his brain.
/// </summary>
public static class CommandWheel
{
    /// <summary>The most sectors on the ring.</summary>
    public const int MaxItems = 8;
    /// <summary>Within this distance of the ring's middle (px) nothing is picked — letting go there sends nothing.</summary>
    public const float DeadZonePx = 30f;
    /// <summary>How far the wheel's cursor may travel from the middle (px).</summary>
    public const float CursorReachPx = 160f;

    /// <summary>The orders shown first, in this order (W-1: "Hold and Move first").</summary>
    private static readonly string[] First = { "Hold", "Move" };

    /// <summary>The living units that name <paramref name="leader"/> as their commander.</summary>
    public static List<Entity> MenOf(EntityRepository world, Entity leader)
    {
        var men = new List<Entity>();
        if (leader.IsNull || !world.IsComponentTypeRegistered<UnitSubordinate>()) return men;
        foreach (var e in world.Query().With<UnitSubordinate>().Build())
            if (e != leader && world.GetComponentRO<UnitSubordinate>(e).Commander == leader && Fdp.Toolkit.Combat.CombatLife.IsAlive(world, e))
                men.Add(e);
        return men;
    }

    /// <summary>
    /// The ring: the orders EVERY one of <paramref name="leader"/>'s men fulfils (<see cref="OrderMenu.OfferedFor"/>) that one gesture can
    /// complete — its pick None, a Point or an Entity, and no other <c>[Required]</c> field (an order needing the form is not on the ring) —
    /// Hold and Move first, the rest in name order, at most <see cref="MaxItems"/>. Empty when he leads nobody.
    /// </summary>
    public static IReadOnlyList<OrderInfo> Items(EntityRepository world, Entity leader)
    {
        var men = MenOf(world, leader);
        if (men.Count == 0) return Array.Empty<OrderInfo>();
        IEnumerable<OrderInfo>? common = null;
        foreach (var man in men)
        {
            var offered = OrderMenu.OfferedFor(world.GetDisType(man));
            common = common == null ? offered : common.Where(o => offered.Any(x => x.Name == o.Name)).ToList();
        }
        return (common ?? Array.Empty<OrderInfo>())
            .Where(OneGesture)
            .OrderBy(o => Array.IndexOf(First, o.Name) is int i && i >= 0 ? i : First.Length)
            .ThenBy(o => o.Name, StringComparer.Ordinal)
            .Take(MaxItems)
            .ToList();
    }

    /// <summary>Can one gesture complete <paramref name="order"/>: a single pick (or none) and nothing else required?</summary>
    public static bool OneGesture(OrderInfo order)
        => order.Pick is OrderPick.None or OrderPick.Point or OrderPick.Entity
           && order.Fields.All(f => !f.Required || f.Name == order.PickField);

    /// <summary>The sector under <paramref name="cursor"/> (px from the ring's middle, screen y down; sector 0 straight up, then
    /// clockwise), or −1 inside the dead zone or with nothing on the ring.</summary>
    public static int Pick(Vector2 cursor, int count)
    {
        if (count <= 0 || cursor.Length() < DeadZonePx) return -1;
        float step = MathF.Tau / count;
        float a = MathF.Atan2(cursor.X, -cursor.Y);
        if (a < 0f) a += MathF.Tau;
        return (int)((a + step / 2f) / step) % count;
    }

    /// <summary>The unit direction of sector <paramref name="index"/> of <paramref name="count"/> on the screen (y down).</summary>
    public static Vector2 Direction(int index, int count)
    {
        float a = MathF.Tau * index / Math.Max(1, count);
        return new Vector2(MathF.Sin(a), -MathF.Cos(a));
    }

    /// <summary>
    /// Sends <paramref name="order"/> to every one of <paramref name="leader"/>'s men, the crosshair its context: the unit under it
    /// (<paramref name="aimTarget"/>, a network id; 0 = none) for an Entity pick, the point (<paramref name="aimPoint"/>) for a Point pick.
    /// One draft per man (his designations fill what the crosshair did not); an incomplete draft is not sent. Returns how many accepted.
    /// </summary>
    public static int Send(EntityRepository world, Entity leader, OrderInfo order, Vector3 aimPoint, long aimTarget)
    {
        if (!world.HasSingletonManaged<NetworkEntityMap>()) return 0;
        var map = world.GetSingletonManaged<NetworkEntityMap>()!;
        var geo = world.HasSingletonManaged<IGeographicTransform>() ? world.GetSingletonManaged<IGeographicTransform>() : null;
        var orders = new EntityOrders(world, map, geo);
        var context = order.Pick switch
        {
            OrderPick.Entity => new OrderContext(aimTarget != 0 ? aimTarget : null, null),
            OrderPick.Point => new OrderContext(null, Hrot.ScenarioEditor.Map.RtsCommands.OrderPoint(world, new Vector2(aimPoint.X, aimPoint.Y))),
            _ => OrderContext.None,
        };
        var store = OrderDesignations.Of(world);
        int accepted = 0;
        foreach (var man in MenOf(world, leader))
        {
            if (!map.TryGetNetworkId(man, out long id)) continue;
            var draft = OrderMenu.Draft(order, new[] { id }, context, json => new[] { orders.Order(id, order.Name, json) },
                designation: store?.Lookup(n => OrderDesignations.IsLive(world, map, n)));
            if (draft.IsComplete && draft.Send(draft.Params.ToJsonString())[0].Accepted) accepted++;
        }
        return accepted;
    }
}
