using Hrot.Map.Definitions;
using Fdp.Kernel;
using System.Collections.Generic;
using System.Numerics;

namespace Hrot.Map.Common.Components;

/// <summary>
/// Represents a single waypoint on a route.
///
/// Stored in absolute Cartesian world-space coordinates (ENU, metres from map origin).
/// Coordinate conversion to/from geodetic happens only at the DDS boundary.
/// </summary>
public struct RouteWaypoint
{
    /// <summary>
    /// Absolute Cartesian position in local world space (ENU, metres from map origin).
    /// Valid for world domains ≤ 100 km.
    /// </summary>
    public Vector3 Position;

    /// <summary>
    /// Desired vehicle speed on this segment in m/s. 0 = use vehicle default.
    /// </summary>
    public float TargetSpeed;

    /// <summary>
    /// Optional JSON object with "soft advice" hints for the vehicle's behavior tree.
    /// Example: {"dangerLevel": 2, "tacticalStance": "cautious"}
    /// Nullable.
    /// </summary>
    public string? ExtensionJson;
}

/// <summary>
/// Managed ECS component storing a route entity's ordered list of waypoints,
/// loop flag, and a version stamp.
///
/// <para>
/// Defined in <c>Hrot.Map.Common</c> so that both SimHost and IG can reference
/// it without circular dependencies. Registered via
/// <c>repo.RegisterManagedComponent&lt;RoutePlan&gt;()</c> in both hosts.
/// </para>
///
/// <para>
/// <b>Mutation contract:</b> waypoints must only be modified via
/// <see cref="Mutate"/>, which automatically increments <see cref="Version"/>
/// so reactive systems (e.g. <c>RouteTrajectorySyncSystem</c>) always detect
/// the change without manual bookkeeping.
/// </para>
/// </summary>
[ComponentId(HrotComponentIds.RoutePlan)]
public sealed class RoutePlan
{
    private readonly List<RouteWaypoint> _waypoints = new();

    /// <summary>
    /// Read-only view of the ordered waypoint list.
    /// Use <see cref="Mutate"/> to add, remove, or replace waypoints.
    /// </summary>
    public IReadOnlyList<RouteWaypoint> Waypoints => _waypoints;

    /// <summary>
    /// When true, the vehicle loops back to waypoint 0 upon reaching the last waypoint.
    /// </summary>
    public bool IsLoop { get; set; }

    /// <summary>
    /// Monotonically-incremented version stamp. Reactive systems
    /// (e.g. <c>RouteTrajectorySyncSystem</c>) compare against their cached
    /// version to detect mutations without polling every field.
    /// Incremented automatically by <see cref="Mutate"/>.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Mutates the waypoint list and automatically increments <see cref="Version"/>.
    /// All callers that wish to modify waypoints must go through this method so that
    /// reactive systems always see a version bump.
    /// </summary>
    /// <param name="mutation">Delegate that receives the mutable backing list.</param>
    public void Mutate(Action<List<RouteWaypoint>> mutation)
    {
        mutation(_waypoints);
        Version++;
    }
}
