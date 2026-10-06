using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// ⭐⭐ <c>CE-3072</c> B3 (R-213, B1″) — the solve of one danger-area sensor on the node that holds the navmesh: the route
    /// it watches, then <see cref="DangerAlongRouteClassifier"/> over the terrain. Called by <c>EqsSolverSystem</c> for a
    /// sensor whose template is <see cref="DangerAreaChildSensor.TemplateId"/>. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// <list type="bullet">
    ///   <item>⭐ the route's END: the sensor's <c>ContextPoint1</c> when it carries one (<see cref="DangerRouteSource.ToPoint"/>);
    ///     otherwise the unit's own move — its <see cref="NavigationIntent.FinalDestination"/> (<see cref="DangerRouteSource.OwnMove"/>).
    ///     ⚠ v1 re-plans that move here rather than reading the vehicle's planned trajectory (the pool is not an ECS
    ///     singleton) — the same navmesh and endpoints, so the same route.</item>
    ///   <item>the route's START: the unit, each solve (so the areas behind it drop out on their own);</item>
    ///   <item>the route: <see cref="INavmeshProvider.PlanPath"/> on the unit's layer; a straight leg when the node has no
    ///     navmesh or no path is found.</item>
    /// </list>
    /// </summary>
    public static class DangerAlongRouteSolve
    {
        /// <summary>The most waypoints a planned route keeps.</summary>
        public const int MaxWaypoints = 256;

        /// <summary>
        /// The areas along <paramref name="carrier"/>'s route, written into <paramref name="areas"/>; returns how many. 0 =
        /// a clear route — or no route to watch (the unit is not moving and no point was given), which also answers "none".
        /// </summary>
        public static int Solve(EntityRepository repo, Entity carrier, in EqsSensor sensor, Span<DangerAreaDescriptor> areas)
        {
            if (!EqsContext.SelfPosition(repo, carrier, sensor, out var start)) return 0;
            if (!TryRouteEnd(repo, carrier, in sensor, out var end)) return 0;
            if (!repo.HasSingletonManaged<TerrainWorld>()) return 0;
            var terrain = repo.GetSingletonManaged<TerrainWorld>();
            if (terrain == null) return 0;

            Span<Vector3> route = stackalloc Vector3[MaxWaypoints + 1];
            int points = PlanRoute(repo, carrier, in sensor, start, end, route);
            return DangerAlongRouteClassifier.Classify(route.Slice(0, points), terrain, sensor.SearchRadius, areas);
        }

        /// <summary>Where the watched route ends — see the class remarks.</summary>
        public static bool TryRouteEnd(EntityRepository repo, Entity carrier, in EqsSensor sensor, out Vector3 end)
        {
            if ((sensor.ContextPointMask & EqsSensor.Point1Bit) != 0)
            {
                end = sensor.ContextPoint1;
                return true;
            }
            end = default;
            var unit = EqsContext.Self(repo, carrier, sensor);
            if (unit.IsNull || !repo.IsComponentTypeRegistered<NavigationIntent>() || !repo.HasComponent<NavigationIntent>(unit))
                return false;
            ref readonly var intent = ref repo.GetComponentRO<NavigationIntent>(unit);
            if (intent.Mode == NavigationMode.None) return false;
            end = intent.FinalDestination;
            return true;
        }

        private static int PlanRoute(EntityRepository repo, Entity carrier, in EqsSensor sensor, Vector3 start, Vector3 end, Span<Vector3> route)
        {
            route[0] = start;
            if (repo.HasSingletonManaged<INavmeshProvider>())
            {
                var navmesh = repo.GetSingletonManaged<INavmeshProvider>();
                if (navmesh != null)
                {
                    Span<NavWaypoint> wp = stackalloc NavWaypoint[MaxWaypoints];
                    int n = navmesh.PlanPath(start, end, wp, EqsContext.SelfLayer(repo, carrier, sensor));
                    if (n > 0)
                    {
                        for (int i = 0; i < n; i++) route[i + 1] = wp[i].Position;
                        return n + 1;
                    }
                }
            }
            route[1] = end;   // no navmesh here, or no path: the straight leg
            return 2;
        }
    }
}
