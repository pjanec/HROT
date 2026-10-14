using Hrot.Map.Definitions;
using Fdp.Core;
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

    // ⭐⭐ Air Ops B1 / B7 (docs/designs/air-attack/DESIGN_Air_Operations.md §5) — a ground unit ignores these; a flyer reads them.

    /// <summary>Passed through, or a point on the ground a flyer lands on.</summary>
    public Fdp.Toolkit.Navigation.RouteWaypointKind Kind;

    /// <summary>&gt; 0 ⇒ a flyer holds this height ABOVE THE TERRAIN here (m); 0 ⇒ <see cref="Position"/>'s Z, as before.</summary>
    public float HeightAgl;

    /// <summary>How long a flyer stays on a Ground waypoint (Seconds — timed by the motion side; Unload / Pickup — released by its brain).</summary>
    public Fdp.Toolkit.Navigation.RouteHold Hold;

    /// <summary>The hold for <see cref="Fdp.Toolkit.Navigation.RouteHold.Seconds"/> (s).</summary>
    public float HoldSeconds;

    /// <summary>The unit whose members a <see cref="Fdp.Toolkit.Navigation.RouteHold.Pickup"/> waits for (network id); 0 = none.</summary>
    public long HoldUnit;
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
