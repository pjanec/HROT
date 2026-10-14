using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Map.Common.Components;

/// <summary>
/// ⭐⭐ <c>CE-2171</c> / Air Ops B1 (docs/designs/air-attack/DESIGN_Air_Operations.md §5) — the route ENTITY for the motion side: its
/// compiled ground trajectory on THIS node (<see cref="RouteTrajectoryCache"/>, attached by <c>RouteTrajectorySyncSystem</c>) and its
/// waypoints (<see cref="RoutePlan"/>, with the air fields). Passed to <c>NavigationIntentBridgeSystem</c> and the air module by the pack
/// that compiles routes (<c>SimHostCoreLogicPack</c>).
/// </summary>
public sealed class HrotRouteSource : IRouteSource
{
    public bool TryGetTrajectory(EntityRepository repo, long routeNetworkId, out int trajectoryId)
    {
        trajectoryId = 0;
        if (routeNetworkId <= 0 || !repo.IsComponentTypeRegistered<RouteTrajectoryCache>()) return false;
        var route = NetworkIdResolver.FindEntityByNetworkId(repo, routeNetworkId);
        if (route == Entity.Null || !repo.HasComponent<RouteTrajectoryCache>(route)) return false;
        var cache = repo.GetComponentRO<RouteTrajectoryCache>(route);
        // a stale compile (the plan changed since) is not this route any more — wait for the sync system to recompile it
        if (repo.HasManagedComponent<RoutePlan>(route) && ((ISimulationView)repo).GetManagedComponentRO<RoutePlan>(route) is { } plan
            && plan.Version != cache.CompiledVersion) return false;
        trajectoryId = cache.TrajectoryId;
        return trajectoryId != 0;
    }

    public bool TryGetRoute(EntityRepository repo, long routeNetworkId, List<NavRouteWaypoint> into, out bool loop, out int version)
    {
        into.Clear(); loop = false; version = 0;
        if (routeNetworkId <= 0) return false;
        var route = NetworkIdResolver.FindEntityByNetworkId(repo, routeNetworkId);
        if (route == Entity.Null || !repo.HasManagedComponent<RoutePlan>(route)) return false;
        var plan = ((ISimulationView)repo).GetManagedComponentRO<RoutePlan>(route);
        if (plan == null) return false;
        foreach (var w in plan.Waypoints)
            into.Add(new NavRouteWaypoint(w.Position, w.TargetSpeed, w.Kind, w.HeightAgl, w.Hold, w.HoldSeconds, w.HoldUnit));
        loop = plan.IsLoop;
        version = plan.Version;
        return into.Count > 0;
    }
}
