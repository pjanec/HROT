using Fdp.Core;
using Hrot.Map.Common.Components;

namespace Hrot.Map.Common;

/// <summary>
/// Shared registration for route-planning components.
/// </summary>
public static class RouteComponentRegistry
{
    /// <summary>
    /// Registers route component schema.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterManagedComponent<RoutePlan>();
        world.RegisterComponent<PersonalRouteRef>();
        world.RegisterComponent<RouteTrajectoryCache>();
    }
}
