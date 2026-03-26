using Bagira.Map.Definitions;
using Fdp.Kernel;

namespace Bagira.Map.Common.Components;

/// <summary>
/// Blittable ECS component placed on a vehicle entity to provide an O(1)
/// lookup from vehicle → its personal child route entity.
/// </summary>
[ComponentId(BagiraComponentIds.PersonalRouteRef)]
public struct PersonalRouteRef
{
    /// <summary>
    /// The route entity that belongs exclusively to this vehicle.
    /// Defaults to <see cref="Entity.Null"/> when no personal route exists.
    /// </summary>
    public Entity RouteEntity;
}
