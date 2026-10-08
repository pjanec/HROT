using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3123</c> (R-228) — the AUTHORED route a mover follows, highlighted on the mover: its personal route
/// (<see cref="PersonalRouteRef"/> → the route entity's <see cref="RoutePlan"/>), else the shared route whose
/// <see cref="RouteTrajectoryCache"/> carries the trajectory the mover is driving. Lines between the waypoints and a dot on each,
/// on the <c>Paths</c> layer, Family <c>Path</c> — so by default it draws for the selected and the pinned movers (§5b of
/// <c>DESIGN_Terrain_Combat_Tuning.md</c>). ⚠ Replaces <c>SimHostTrajectoryLayer</c>, a SimHost-only raylib layer that never went
/// over the wire and never drew in the Editor or the Replay Browser; same data, same colours. The FOLLOWED trajectory (progress,
/// look-ahead) is <see cref="PlannedPathGizmo"/>'s. 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
/// </summary>
[GizmoProjector(typeof(NavState), typeof(SimTransform), Family = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path)]
public sealed class AuthoredRouteGizmo : IStatelessGizmo
{
    public static readonly Rgba32 PersonalColor = new(255, 161, 0, 255);   // orange, as the layer drew it
    public static readonly Rgba32 SharedColor   = new(255, 215, 0, 192);   // translucent yellow, as the layer drew it

    private EntityRepository? _queryRepo;
    private EntityQuery? _routes;

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        if (view is EntityRepository r0 && !(r0.IsComponentTypeRegistered<PersonalRouteRef>() || r0.IsComponentTypeRegistered<RouteTrajectoryCache>()))
            return;

        if (Registered<PersonalRouteRef>(view) && view.HasComponent<PersonalRouteRef>(entity))
        {
            var route = view.GetComponentRO<PersonalRouteRef>(entity).RouteEntity;
            if (view.IsAlive(route) && view.HasManagedComponent<RoutePlan>(route))
                DrawPlan(draw, view.GetManagedComponentRO<RoutePlan>(route), PersonalColor);
            return;
        }

        ref readonly var nav = ref view.GetComponentRO<NavState>(entity);
        if (nav.Mode != KinematicsMode.CustomTrajectory || nav.TrajectoryId <= 0) return;
        if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<RouteTrajectoryCache>()) return;
        if (!ReferenceEquals(repo, _queryRepo))
        {
            _queryRepo = repo;
            _routes = repo.Query().With<RouteTrajectoryCache>().WithManaged<RoutePlan>().Build();
        }
        foreach (var routeEntity in _routes!)
        {
            if (view.GetComponentRO<RouteTrajectoryCache>(routeEntity).TrajectoryId != nav.TrajectoryId) continue;
            DrawPlan(draw, view.GetManagedComponentRO<RoutePlan>(routeEntity), SharedColor);
            return;
        }
    }

    /// <summary>The route as lines between its waypoints and a dot on each (a waypoint stores X = east, Z = north).</summary>
    public static void DrawPlan(IDebugDrawBuilder draw, RoutePlan plan, Rgba32 color)
    {
        if (plan.Waypoints == null || plan.Waypoints.Count < 2) return;
        int n = plan.Waypoints.Count;
        int segments = plan.IsLoop ? n : n - 1;
        for (int i = 0; i < segments; i++)
            draw.DrawLine(At(plan, i), At(plan, (i + 1) % n), color, 1.5f, layer: DebugTraceLayers.Paths);
        for (int i = 0; i < n; i++)
            draw.DrawSphere(At(plan, i), 0.6f, color, layer: DebugTraceLayers.Paths, fillColor: color);
    }

    private static Vector3 At(RoutePlan plan, int i) => new(plan.Waypoints[i].Position.X, plan.Waypoints[i].Position.Z, 0f);

    private static bool Registered<T>(ISimulationView view) where T : unmanaged =>
        view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>();
}
