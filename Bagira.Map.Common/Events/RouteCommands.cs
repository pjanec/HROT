using System.Numerics;
using Fdp.Kernel;

namespace Bagira.Map.Common.Events;

/// <summary>
/// Blittable ECS event published to the event bus by the IG input layer when
/// the operator Shift+Right-Clicks on the map while a vehicle entity is
/// selected. Consumed by <c>PersonalRouteAuthoringSystem</c>.
/// </summary>
[EventId(3002)]
public struct CmdAppendPersonalWaypoint
{
    /// <summary>The vehicle entity that should receive the new waypoint.</summary>
    public Entity VehicleEntity;

    /// <summary>
    /// Absolute Cartesian world-space position of the new waypoint (already
    /// converted from screen/map coordinates before publishing this event).
    /// </summary>
    public Vector3 WorldPosition;
}
