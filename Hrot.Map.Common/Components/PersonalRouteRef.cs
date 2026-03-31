using Hrot.Map.Definitions;
using Fdp.Kernel;

namespace Hrot.Map.Common.Components;

/// <summary>
/// Blittable ECS component placed on a vehicle entity to provide an O(1)
/// lookup from vehicle → its personal child route entity.
/// </summary>
[ComponentId(HrotComponentIds.PersonalRouteRef)]
public struct PersonalRouteRef
{
    /// <summary>
    /// The route entity that belongs exclusively to this vehicle.
    /// Defaults to <see cref="Entity.Null"/> when no personal route exists.
    /// </summary>
    public Entity RouteEntity;
}
