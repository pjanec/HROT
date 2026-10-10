using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐⭐ <c>CE-3128</c> (§5.2a D6) — the ONE place a <see cref="PathfindingRequestEvent"/> is built from a
    /// <see cref="NavigationIntent"/>: the bridge's first plan and the Muscle's replan both come here, so a field the order carries
    /// reaches the solver on BOTH paths. ⛔ Before, the replan built its request by hand and dropped <c>BackendForce</c> and the
    /// intent's layer mask — a road use added the same way would have been dropped on the first replan.
    /// 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a.
    /// </summary>
    public static class PathRequests
    {
        /// <summary>The request for <paramref name="entity"/>'s <paramref name="intent"/>, from <paramref name="from"/>.</summary>
        public static PathfindingRequestEvent FromIntent(EntityRepository repo, Entity entity, in NavigationIntent intent, Vector3 from, long requestId)
        {
            var profile = repo.IsComponentTypeRegistered<NavAgentProfile>() && repo.HasComponent<NavAgentProfile>(entity)
                ? repo.GetComponent<NavAgentProfile>(entity)
                : default;
            return new PathfindingRequestEvent
            {
                RequestId       = requestId,
                Start           = from,
                End             = intent.FinalDestination,   // real destination Z (Sim Z-up, P3D-302)
                MobilityProfile = profile.MobilityProfile,
                BackendForce    = (NavigationBackend)intent.BackendForce,
                RoadUse         = ResolveRoadUse(repo, entity, intent.RoadUse),
                RouteHandle     = intent.RouteHandle,
                NavLayerMask    = (int)NavLayerSelection.For(repo, entity, intent.LayerMask),
            };
        }

        /// <summary>
        /// ⭐ §5.2a D1 — an order that does not say: a vehicle prefers the roads, anything else (infantry) is neutral, where the
        /// road wins only when walking direct is genuinely longer. ⚠ The locomotion CLASS decides, as in <see cref="NavLayerSelection"/>
        /// — <c>VehicleParams.Class == Pedestrian</c> is infantry even though SimHost infantry carries <c>VehicleState</c> too — and not
        /// <see cref="NavAgentProfile.MobilityProfile"/>, which has no production writer (AQ67).
        /// </summary>
        public static RoadUse ResolveRoadUse(EntityRepository repo, Entity entity, RoadUse requested)
            => requested != RoadUse.Unspecified ? requested : IsVehicle(repo, entity) ? RoadUse.Prefer : RoadUse.Neutral;

        /// <summary>A vehicle by locomotion class: <c>VehicleParams</c> says so (not Pedestrian), else a bare <c>VehicleState</c>.</summary>
        public static bool IsVehicle(EntityRepository repo, Entity entity)
        {
            if (repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>() && repo.HasComponent<global::CarKinem.Core.VehicleParams>(entity))
                return repo.GetComponent<global::CarKinem.Core.VehicleParams>(entity).Class != global::CarKinem.Core.VehicleClass.Pedestrian;
            return repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>() && repo.HasComponent<global::CarKinem.Core.VehicleState>(entity);
        }
    }
}
