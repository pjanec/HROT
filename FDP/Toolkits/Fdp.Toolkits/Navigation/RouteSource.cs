using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>⭐ Air Ops B1 (docs/designs/air-attack/DESIGN_Air_Operations.md §5) — what a route waypoint IS: passed through, or a
    /// point on the ground a flyer lands on (a ground unit drives through both alike).</summary>
    public enum RouteWaypointKind : byte { Pass = 0, Ground = 1 }

    /// <summary>⭐ Air Ops B1/B7 (§5) — how long a flyer stays on a <see cref="RouteWaypointKind.Ground"/> waypoint: not at all,
    /// <see cref="RouteHold.Seconds"/> (timed by the Muscle), until nobody is aboard (<see cref="RouteHold.Unload"/>) or until a unit
    /// is aboard (<see cref="RouteHold.Pickup"/>) — the last two are judged by the flyer's Brain (it knows rosters), which sets
    /// <c>NavigationIntent.Flags</c>' release bit.
    /// ⭐ Airborne insertion B-3 (docs/designs/airborne/DESIGN_Airborne_Insertion.md D-7) appends two holds on an AIR waypoint:
    /// <see cref="RouteHold.Rope"/> — hover at the waypoint's height above the HIGHEST surface under it (a roof) until nobody is aboard and
    /// nobody is on its ropes; <see cref="RouteHold.Drop"/> — NOT a stop: from the waypoint on, fly the DZ leg reporting <c>Dropping</c>
    /// until nobody is aboard. Both released by the Brain like Unload.</summary>
    public enum RouteHold : byte { None = 0, Seconds = 1, Unload = 2, Pickup = 3, Rope = 4, Drop = 5 }

    /// <summary>One waypoint of a route as the motion side reads it — the engine-neutral view of the route entity's waypoint.</summary>
    public readonly record struct NavRouteWaypoint(Vector3 Position, float TargetSpeed, RouteWaypointKind Kind, float HeightAgl,
                                                   RouteHold Hold, float HoldSeconds, long HoldUnit);

    /// <summary>
    /// ⭐⭐ <c>CE-2171</c> / Air Ops B1 (§5) — the route ENTITY, as the motion side resolves it from the network id the intent carries
    /// (<see cref="NavigationIntent.RouteEntityId"/>). ⚠ The route types (<c>RoutePlan</c>, its compiled trajectory) live in the HROT
    /// layer this toolkit cannot reference, so the host passes an implementation in — the same shape as the navmesh provider.
    /// </summary>
    public interface IRouteSource
    {
        /// <summary>The route's compiled ground trajectory in THIS node's pool; false while it is not compiled yet.</summary>
        bool TryGetTrajectory(EntityRepository repo, long routeNetworkId, out int trajectoryId);

        /// <summary>The route's waypoints into <paramref name="into"/> (cleared first), its loop flag and version; false when unknown.</summary>
        bool TryGetRoute(EntityRepository repo, long routeNetworkId, List<NavRouteWaypoint> into, out bool loop, out int version);
    }
}
