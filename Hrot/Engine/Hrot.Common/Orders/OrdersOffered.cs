using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Common.Orders
{
    /// <summary>
    /// ⭐ <c>CE-3169</c> (<c>docs/DESIGN_Entity_Control.md</c> §5) — a world singleton saying THIS map can issue unit orders: the map
    /// pack sets it on every live host (its <c>MenuOrders</c> runs there). The unit menu offers Move Here · Engage · Stop only where
    /// it is set — ⛔ never in a replay, which has no live brain to order.
    /// </summary>
    [ComponentId(GlobalComponentIds.OrdersOffered)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]   // ⭐ CE-3299 D7 — declared: a service / session object, not world state (D7)
    public sealed class OrdersOffered
    {
        /// <summary>
        /// ⭐ <c>CE-1363</c> — THE test of "this map offers orders right now": the singleton is set AND the world shows no recording
        /// (<c>ReplayState</c>, read each frame so a host that enters a review after its map was built hides them too, R-3064). The context menu's
        /// orders and the View menu's "RTS Command Mode" both ask THIS, so neither lists what activation would refuse.
        /// </summary>
        public static bool IsOfferedIn(ISimulationView view) =>
            view is EntityRepository repo
            && !Fdp.Toolkit.Replay.ReplayState.IsActive(view)
            && repo.HasSingletonManaged<OrdersOffered>();
    }
}
