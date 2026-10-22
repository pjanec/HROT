using Hrot.Map.Definitions;
using Fdp.Core;

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

    /// <summary>
    /// ⭐ <c>R-3039</c> S1 — 1 while the follow command for a just-authored waypoint waits a frame for the route's trajectory to compile
    /// (was <c>PersonalRouteAuthoringSystem._pendingFollowCommands</c>). Here, on the vehicle, so a checkpoint taken in that frame still
    /// sends the vehicle along its new route after a restore into a fresh process. 📄 docs/DESIGN_System_State_Inventory.md §6 #12.
    /// </summary>
    public byte FollowPending;
}
