using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Simulation-phase system that resolves pending <see cref="PathfindingRequestEvent"/>s
    /// accumulated from the event bus using a node-graph Dijkstra search over the
    /// supplied <see cref="RoadNetworkBlob"/>, or an injected <see cref="INavmeshProvider"/>
    /// / <see cref="IVolumetricPathProvider"/> when the request calls for it.
    ///
    /// <para><b>Execution context:</b> runs inside <see cref="Modules.NavigationSolverModule"/> at
    /// 10 Hz on a background thread (SoD snapshot).  Results are published back as
    /// <see cref="PathfindingResultEvent"/> via <see cref="IEntityCommandBuffer"/> and
    /// materialized on the main thread by <c>PathfindingResultMaterializationSystem</c>.</para>
    ///
    /// <para><b>Empty / default network:</b>
    /// If <see cref="RoadNetworkBlob.Nodes"/> is not created or has no nodes, every
    /// road-graph request returns <see cref="PathfindingResultEvent.IsReachable"/> = <c>false</c>.</para>
    ///
    /// <para><b>Budget:</b> at most <see cref="PathfindingBatchData.DefaultCapacity"/> requests
    /// are processed per tick; excess requests are dropped (oldest-evict ring-buffer semantics).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class PathfindingSolverSystem : IEcsModuleSystem
    {
        /// <summary>
        /// Fallback road graph supplied at construction. ⛔ Not the source of truth — see
        /// <see cref="_activeRoadNetwork"/>. Kept so a caller that never publishes the singleton
        /// (unit rails, a host with a statically supplied graph) behaves exactly as before.
        /// </summary>
        private readonly RoadNetworkBlob        _roadNetwork;

        /// <summary>
        /// ⭐⭐ The graph used by THIS tick, refreshed from the <see cref="ZoneEnvironmentData"/>
        /// singleton at the top of <see cref="Execute"/>.
        /// <para>
        /// ⛔ The blob must NOT be captured once in the constructor: the terrain loader publishes a new
        /// <c>ZoneEnvironmentData</c> when terrain or a zone loads, and a constructor-captured copy makes
        /// that swap reach <c>CarKinematicsSystem</c> (which already re-reads per tick) while silently
        /// doing nothing for pathfinding — vehicles would drive the new roads while routes were still
        /// planned over the old ones, with no error anywhere.
        /// </para>
        /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.4 (R2).
        /// </summary>
        private RoadNetworkBlob                 _activeRoadNetwork;

        /// <summary>
        /// Optional thread-safe carrier of the current graph, used on execution paths where the ECS
        /// singleton is unreachable (a background module receives a snapshot view, and
        /// <c>ISimulationView</c> has no singleton API). <c>null</c> on hosts that never swap.
        /// </summary>
        private readonly RoadNetworkHolder?     _roadNetworkHolder;

        private readonly TrajectoryPoolManager  _trajectoryPool;
        private readonly INavmeshProvider?      _navmesh;
        private Fdp.Toolkit.Terrain.DoorStates? _doors;   // ⭐ R-219 — this solver tick's view of the doors
        private readonly IVolumetricPathProvider? _volumetric;

        // MobilityProfile byte value for Flying entities (section 5.1).
        private const byte MobilityProfileFlying = 4;

        // Maximum waypoints a navmesh / volumetric path may produce per request.
        private const int MaxNavWaypoints = 128;

        /// <summary>⭐ CE-3128 — the ground route planner (reused buffers: one per solver, R-220). CE-2059's "the route ends at
        /// the requested point" is its egress leg.</summary>
        private readonly RoutePlanner _planner = new();

        /// <summary>
        /// Initialises the solver with the road network and trajectory pool.
        /// </summary>
        /// <param name="roadNetwork">
        ///   Static road graph. Pass <c>default</c> (empty blob) for maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        ///   Shared trajectory pool.  Must not be <c>null</c>.
        /// </param>
        /// <param name="navmesh">Optional navmesh provider for ground-based path queries.</param>
        /// <param name="volumetric">Optional volumetric provider for flying entities.</param>
        /// <param name="roadNetworkHolder">
        ///   Optional live carrier of the road graph, for hosts whose execution path cannot reach the
        ///   <c>ZoneEnvironmentData</c> singleton (background/SoD modules). When supplied it is read
        ///   every tick and preferred over <paramref name="roadNetwork"/>.
        /// </param>
        public PathfindingSolverSystem(
            RoadNetworkBlob          roadNetwork,
            TrajectoryPoolManager    trajectoryPool,
            INavmeshProvider?        navmesh     = null,
            IVolumetricPathProvider? volumetric  = null,
            RoadNetworkHolder?       roadNetworkHolder = null)
        {
            _roadNetwork        = roadNetwork;
            _activeRoadNetwork  = roadNetwork;
            _roadNetworkHolder  = roadNetworkHolder;
            _trajectoryPool = trajectoryPool ?? throw new ArgumentNullException(nameof(trajectoryPool));
            _navmesh        = navmesh;
            _volumetric     = volumetric;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // ⭐⭐ R2 — resolve the road graph EVERY tick so a terrain/zone load is observed, instead of
            //   using a blob frozen at construction.
            //
            // ⚠ The order below is forced by a measured constraint, not preference. ISimulationView
            //   exposes NO singleton API; CarKinematicsSystem reads singletons by downcasting the view
            //   to EntityRepository and THROWING when it is not one, which is safe only for a
            //   Synchronous module. This system's own module is SlowBackground (SoD snapshot), so that
            //   downcast legitimately fails there and must degrade, never throw.
            //   📄 DESIGN_Terrain_Zones_And_Assets.md §5.4 — the "a holder becomes necessary" branch.
            if (view is EntityRepository repo && repo.HasSingleton<ZoneEnvironmentData>())
            {
                // Live world, main thread: the swap happens here too, so no reader can be interrupted.
                _activeRoadNetwork = repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork;
                Solve(view, deltaTime);
                return;
            }

            if (_roadNetworkHolder != null)
            {
                // ⭐⭐ C6 — BACKGROUND path: hold a LEASE for the whole traversal. A bare read would give
                //   us a struct of NativeArrays whose memory a concurrent Publish is free to release
                //   mid-walk; the lease keeps the retired generation alive until we are out of it.
                //   ⛔ Do NOT "simplify" this to `_roadNetworkHolder.Current` — that is the use-after-free.
                using var lease = _roadNetworkHolder.Borrow();
                _activeRoadNetwork = lease.Value;
                Solve(view, deltaTime);
                return;
            }

            _activeRoadNetwork = _roadNetwork;   // static host that never publishes
            Solve(view, deltaTime);
        }

        /// <summary>
        /// The tick proper, with <see cref="_activeRoadNetwork"/> already resolved and — on the background
        /// path — pinned by a lease held by the caller for this whole call.
        /// </summary>
        private void Solve(ISimulationView view, float deltaTime)
        {
            // Read all accumulated request events since the last solver tick.
            var requests = view.ReadEvents<PathfindingRequestEvent>();
            if (requests.IsEmpty) return;

            var cmd = view.GetCommandBuffer();

            // ⭐ R-219 — the doors as THIS view sees them (on a background module: its snapshot), once per solver tick, so every
            //   path in the batch is planned against the same door states — never a live value written mid-batch.
            _doors = Fdp.Toolkit.Terrain.DoorStates.Of(view);

            // Budget cap: process at most DefaultCapacity requests per tick (oldest-evict).
            int limit = Math.Min(requests.Length, PathfindingBatchData.DefaultCapacity);

            for (int i = 0; i < limit; i++)
            {
                ref readonly var req = ref requests[i];

                // Allocate or echo the route handle.
                int handle = req.RouteHandle == 0
                    ? NavigationHandleAllocator.Allocate()
                    : req.RouteHandle;

                PathfindingResultEvent result = ResolveRequest(in req, handle);

                cmd.PublishEvent(result);
            }
        }

        // ── Backend selection ────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ CE-3128 (§5.2a) — flying goes to the volumetric provider; everything on the ground goes to the ONE route planner,
        /// which takes the road network on COST from the request's <see cref="PathfindingRequestEvent.RoadUse"/> (R-230). ⛔ The
        /// former "both ends within 500 m of a road node ⇒ road graph, one end ⇒ Hybrid" heuristic and the Phase-1 Hybrid (the
        /// road graph end to end, re-tagged) are gone with it.
        /// </summary>
        private PathfindingResultEvent ResolveRequest(in PathfindingRequestEvent req, int handle)
        {
            bool volumetric = req.BackendForce == NavigationBackend.Volumetric
                           || (req.BackendForce == NavigationBackend.Auto && req.MobilityProfile == MobilityProfileFlying);
            if (volumetric && _volumetric != null)
                return SolveVolumetric(in req, handle);
            return SolveGround(in req, handle);
        }

        // ── Ground: navmesh, road graph, or the splice — one planner ─────────────────────

        private PathfindingResultEvent SolveGround(in PathfindingRequestEvent req, int handle)
        {
            var query = new RouteQuery(req.Start, req.End, req.RoadUse, req.BackendForce,
                req.NavLayerMask != 0 ? (uint)req.NavLayerMask : 0xFFFFFFFFu);
            var backend = _planner.Plan(in query, in _activeRoadNetwork, _navmesh, _doors, out float distance);
            if (backend == null)
            {
                var failed = req.BackendForce == NavigationBackend.NavRoadGraph || _navmesh == null
                    ? NavigationBackend.NavRoadGraph : NavigationBackend.Navmesh;
                return Unreachable(in req, handle, failed);
            }

            var points = _planner.Points;
            var positions = new Vector3[points.Count];
            for (int k = 0; k < positions.Length; k++) positions[k] = points[k];
            _trajectoryPool.RegisterTrajectoryWithKey(positions, handle, _planner.TraversalsOrNull());

            return new PathfindingResultEvent
            {
                RequestId            = req.RequestId,
                IsReachable          = true,
                TotalDistanceMeters  = distance,
                RouteHandle          = handle,
                SourceNodeId         = req.SourceNodeId,
                NavmeshVersionAtPlan = _navmesh != null && backend != NavigationBackend.NavRoadGraph ? (int)_navmesh.QueryVersion() : 0,
                PrimaryBackend       = backend.Value,
                FailureReason        = NavigationFailureReason.NoFailure,
            };
        }

        // ── Volumetric backend ─────────────────────────────────────────────────────

        private unsafe PathfindingResultEvent SolveVolumetric(in PathfindingRequestEvent req, int handle)
        {
            var buf = stackalloc NavWaypoint[MaxNavWaypoints];
            var span = new Span<NavWaypoint>(buf, MaxNavWaypoints);

            int count = _volumetric!.PlanPath(req.Start, req.End, span);

            if (count < 2)
                return Unreachable(in req, handle, NavigationBackend.Volumetric);

            var positions = new Vector3[count];
            float totalDist = 0f;
            for (int k = 0; k < count; k++)
            {
                positions[k] = span[k].Position;
                if (k > 0)
                    totalDist += Vector2.Distance(
                        new Vector2(positions[k - 1].X, positions[k - 1].Y),
                        new Vector2(positions[k].X, positions[k].Y));
            }

            _trajectoryPool.RegisterTrajectoryWithKey(positions, handle);

            return new PathfindingResultEvent
            {
                RequestId           = req.RequestId,
                IsReachable         = true,
                TotalDistanceMeters = totalDist,
                RouteHandle         = handle,
                SourceNodeId        = req.SourceNodeId,
                PrimaryBackend      = NavigationBackend.Volumetric,
                FailureReason       = NavigationFailureReason.NoFailure,
            };
        }

        // ── Shared utilities ──────────────────────────────────────────────────────

        private static PathfindingResultEvent Unreachable(
            in PathfindingRequestEvent req,
            int handle,
            NavigationBackend backend) =>
            new PathfindingResultEvent
            {
                RequestId           = req.RequestId,
                IsReachable         = false,
                TotalDistanceMeters = 0f,
                RouteHandle         = handle,
                SourceNodeId        = req.SourceNodeId,
                PrimaryBackend      = backend,
                FailureReason       = NavigationFailureReason.Unreachable,
            };
    }
}
