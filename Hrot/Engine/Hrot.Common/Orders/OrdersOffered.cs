using Fdp.Core;

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
    }
}
