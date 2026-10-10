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
    /// it watches, then <see cref="DangerAlongRouteClassifier"/> over the road graph. Called by <c>EqsSolverSystem</c> for a
    /// sensor whose template is <see cref="DangerAreaChildSensor.TemplateId"/>. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// <list type="bullet">
    ///   <item>⭐ the route's END: the sensor's <c>ContextPoint1</c> when it carries one (<see cref="DangerRouteSource.ToPoint"/>);
    ///     otherwise the unit's own move — its <see cref="NavigationIntent.FinalDestination"/> (<see cref="DangerRouteSource.OwnMove"/>).</item>
    ///   <item>the route's START: the unit, each solve (so the areas behind it drop out on their own);</item>
    ///   <item>⭐⭐ <c>CE-3128</c> (Nav v2 §5.2a D2) — the route: the SAME <see cref="RoutePlanner"/> the path solver uses, with the
    ///     unit's <see cref="RoadUse"/> resolved the same way (<see cref="PathRequests.ResolveRoadUse"/>), so the sensor watches the
    ///     route the unit will drive — navmesh, or navmesh → road → navmesh. A straight leg when nothing can plan it.</item>
    /// </list>
    /// </summary>
    public static class DangerAlongRouteSolve
    {
        /// <summary>
        /// The areas along <paramref name="carrier"/>'s route, written into <paramref name="areas"/>; returns how many. 0 =
        /// a clear route, no road graph, or no route to watch (the unit is not moving and no point was given).
        /// <paramref name="planner"/> is the caller thread's planner; <paramref name="roads"/> the node's road graph carrier.
        /// </summary>
        public static int Solve(EntityRepository repo, Entity carrier, in EqsSensor sensor, Span<DangerAreaDescriptor> areas,
            RoutePlanner planner, global::CarKinem.Road.RoadNetworkHolder? roads)
        {
            if (roads == null || planner == null) return 0;
            if (!EqsContext.SelfPosition(repo, carrier, sensor, out var start)) return 0;
            if (!TryRouteEnd(repo, carrier, in sensor, out var end, out var roadUse)) return 0;
            var terrain = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;

            // ⭐ C6 — lease the graph for the whole plan + classify: a terrain commit may publish a new one meanwhile.
            using var lease = roads.Borrow();
            var graph = lease.Value;
            if (!RoadGraphRouter.HasRoads(graph)) return 0;

            var navmesh = repo.HasSingletonManaged<INavmeshProvider>() ? repo.GetSingletonManaged<INavmeshProvider>() : null;
            var query = new RouteQuery(start, end, roadUse, NavigationBackend.Auto, EqsContext.SelfLayer(repo, carrier, sensor));
            Span<Vector3> route = stackalloc Vector3[RoutePlanner.MaxNavWaypoints * 3];
            int points;
            if (planner.Plan(in query, in graph, navmesh, Fdp.Toolkit.Terrain.DoorStates.Of(repo), out _) != null)
            {
                var planned = planner.Points;
                points = Math.Min(planned.Count, route.Length);
                for (int i = 0; i < points; i++) route[i] = planned[i];
            }
            else
            {
                route[0] = start; route[1] = end; points = 2;   // nothing can plan it: the straight leg
            }
            return DangerAlongRouteClassifier.Classify(route.Slice(0, points), in graph, terrain, sensor.SearchRadius, areas);
        }

        /// <summary>Where the watched route ends — see the class remarks — and the unit's road use for it.</summary>
        public static bool TryRouteEnd(EntityRepository repo, Entity carrier, in EqsSensor sensor, out Vector3 end, out RoadUse roadUse)
        {
            var unit = EqsContext.Self(repo, carrier, sensor);
            roadUse = unit.IsNull ? RoadUse.Neutral : PathRequests.ResolveRoadUse(repo, unit, RoadUse.Unspecified);
            if ((sensor.ContextPointMask & EqsSensor.Point1Bit) != 0)
            {
                end = sensor.ContextPoint1;
                return true;
            }
            end = default;
            if (unit.IsNull || !repo.IsComponentTypeRegistered<NavigationIntent>() || !repo.HasComponent<NavigationIntent>(unit))
                return false;
            ref readonly var intent = ref repo.GetComponentRO<NavigationIntent>(unit);
            if (intent.Mode == NavigationMode.None) return false;
            end = Fdp.Toolkit.Navigation.NavigationDestination.Of(repo, unit, intent);   // ⭐ CE-1035 Q0b
            roadUse = PathRequests.ResolveRoadUse(repo, unit, intent.RoadUse);
            return true;
        }
    }
}
