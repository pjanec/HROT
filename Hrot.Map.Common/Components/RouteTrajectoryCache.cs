using Hrot.Map.Definitions;
using Fdp.Kernel;

namespace Hrot.Map.Common.Components;

/// <summary>
/// Blittable ECS component caching the compiled <c>TrajectoryPoolManager</c>
/// entry for a route entity. Attached by <c>RouteTrajectorySyncSystem</c>.
///
/// <para>
/// Not replicated over DDS — purely local, transient performance state.
/// When <c>TrajectoryId == 0</c> the route has not been compiled yet.
/// </para>
/// </summary>
[ComponentId(HrotComponentIds.RouteTrajectoryCache)]
public struct RouteTrajectoryCache
{
    /// <summary>
    /// Index into the <c>TrajectoryPoolManager</c>. 0 = not yet compiled.
    /// </summary>
    public int TrajectoryId;

    /// <summary>
    /// The <see cref="RoutePlan.Version"/> value at the time this trajectory
    /// was last compiled. Compared against the live version to detect staleness.
    /// </summary>
    public int CompiledVersion;
}
