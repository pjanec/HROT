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
        private readonly RoadNetworkBlob        _roadNetwork;
        private readonly TrajectoryPoolManager  _trajectoryPool;
        private readonly INavmeshProvider?      _navmesh;
        private readonly IVolumetricPathProvider? _volumetric;

        // MobilityProfile byte value for Flying entities (section 5.1).
        private const byte MobilityProfileFlying = 4;

        // Squared distance threshold: within this radius a point is considered "on road".
        private const float RoadRadiusThresholdSq = 500f * 500f;

        // Maximum waypoints a navmesh / volumetric path may produce per request.
        private const int MaxNavWaypoints = 128;

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
        public PathfindingSolverSystem(
            RoadNetworkBlob          roadNetwork,
            TrajectoryPoolManager    trajectoryPool,
            INavmeshProvider?        navmesh     = null,
            IVolumetricPathProvider? volumetric  = null)
        {
            _roadNetwork    = roadNetwork;
            _trajectoryPool = trajectoryPool ?? throw new ArgumentNullException(nameof(trajectoryPool));
            _navmesh        = navmesh;
            _volumetric     = volumetric;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // Read all accumulated request events since the last solver tick.
            var requests = view.ReadEvents<PathfindingRequestEvent>();
            if (requests.IsEmpty) return;

            var cmd = view.GetCommandBuffer();

            // Budget cap: process at most DefaultCapacity requests per tick (oldest-evict).
            int limit = Math.Min(requests.Length, PathfindingBatchData.DefaultCapacity);

            for (int i = 0; i < limit; i++)
            {
                ref readonly var req = ref requests[i];

                // Allocate or echo the route handle.
                int handle = req.RouteHandle == 0
                    ? NavigationHandleAllocator.Allocate()
                    : req.RouteHandle;

                var selected = SelectBackend(in req);
                PathfindingResultEvent result = ResolveRequest(in req, handle, selected);

                cmd.PublishEvent(result);
            }
        }

        // ── Backend selection ────────────────────────────────────────────────────

        /// <summary>
        /// Selects the appropriate path-planning backend for <paramref name="req"/>
        /// following the section 5.2 pseudocode.
        /// </summary>
        private NavigationBackend SelectBackend(in PathfindingRequestEvent req)
        {
            // Explicit override takes precedence.
            if (req.BackendForce != NavigationBackend.Auto)
                return req.BackendForce;

            // Flying entities use the volumetric provider when available.
            if (req.MobilityProfile == MobilityProfileFlying && _volumetric != null)
                return NavigationBackend.Volumetric;

            // Auto heuristic: prefer road-graph when the start point is near a road node;
            // fall back to navmesh if available, otherwise road-graph.
            bool networkHasNodes = _roadNetwork.Nodes.IsCreated && _roadNetwork.Nodes.Length > 0;
            if (networkHasNodes)
            {
                var start2D = new Vector2(req.Start.X, req.Start.Y);
                int nearest = FindNearestNode(start2D);
                if (nearest >= 0)
                {
                    float distSq = Vector2.DistanceSquared(start2D, _roadNetwork.Nodes[nearest].Position);
                    if (distSq < RoadRadiusThresholdSq)
                        return NavigationBackend.NavRoadGraph;
                }
            }

            if (_navmesh != null)
                return NavigationBackend.Navmesh;

            return NavigationBackend.NavRoadGraph;
        }

        // ── Dispatch ─────────────────────────────────────────────────────────────

        private PathfindingResultEvent ResolveRequest(
            in PathfindingRequestEvent req, int handle, NavigationBackend backend)
        {
            switch (backend)
            {
                case NavigationBackend.Navmesh when _navmesh != null:
                    return SolveNavmesh(in req, handle);

                case NavigationBackend.Volumetric when _volumetric != null:
                    return SolveVolumetric(in req, handle);

                // RoadGraph, Hybrid (not yet implemented), or forced backend whose
                // provider is absent all fall through to the Dijkstra road-graph solver.
                default:
                    bool networkEmpty = !_roadNetwork.Nodes.IsCreated || _roadNetwork.Nodes.Length == 0;
                    return networkEmpty
                        ? Unreachable(in req, handle, NavigationBackend.NavRoadGraph)
                        : SolvePath(in req, handle);
            }
        }

        // ── Road-graph (Dijkstra) backend ─────────────────────────────────────────

        /// <summary>Runs Dijkstra from the road node nearest to <c>req.Start</c> to the node
        /// nearest to <c>req.End</c>, then registers the resulting waypoints under
        /// <paramref name="handle"/>.</summary>
        private PathfindingResultEvent SolvePath(in PathfindingRequestEvent req, int handle)
        {
            var start2D = new Vector2(req.Start.X, req.Start.Y);
            var end2D   = new Vector2(req.End.X,   req.End.Y);

            int startNode = FindNearestNode(start2D);
            int endNode   = FindNearestNode(end2D);

            if (startNode < 0 || endNode < 0)
                return Unreachable(in req, handle, NavigationBackend.NavRoadGraph);

            // Dijkstra
            int  nodeCount = _roadNetwork.Nodes.Length;
            var  dist      = new float[nodeCount];
            var  prev      = new int[nodeCount];
            var  visited   = new bool[nodeCount];

            for (int i = 0; i < nodeCount; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[startNode] = 0f;

            // Simple O(N^2) Dijkstra — road graphs are small (hundreds of nodes).
            for (int iter = 0; iter < nodeCount; iter++)
            {
                // Pick unvisited node with smallest distance
                int u = -1;
                for (int j = 0; j < nodeCount; j++)
                {
                    if (!visited[j] && dist[j] < float.MaxValue)
                    {
                        if (u < 0 || dist[j] < dist[u]) u = j;
                    }
                }
                if (u < 0) break;
                if (u == endNode) break;

                visited[u] = true;

                // Relax outgoing edges (segments whose StartNodeIndex == u)
                for (int s = 0; s < _roadNetwork.Segments.Length; s++)
                {
                    ref readonly var seg = ref _roadNetwork.Segments[s];
                    if (seg.StartNodeIndex != u) continue;

                    int v = seg.EndNodeIndex;
                    if (v < 0 || v >= nodeCount) continue;
                    if (visited[v]) continue;

                    float newDist = dist[u] + seg.Length;
                    if (newDist < dist[v])
                    {
                        dist[v] = newDist;
                        prev[v] = u;
                    }
                }
            }

            // No path found
            if (dist[endNode] == float.MaxValue)
                return Unreachable(in req, handle, NavigationBackend.NavRoadGraph);

            // Reconstruct node sequence
            var nodePath = new List<int>();
            for (int n = endNode; n >= 0; n = prev[n])
            {
                nodePath.Add(n);
                if (n == startNode) break;
            }
            nodePath.Reverse();

            // Convert to 3D waypoints (Sim Z-up). Road nodes are 2D (ground plane), so altitude
            // is 0 here; the navmesh/volumetric backends below carry real altitude (P3D-303).
            var waypoints = new Vector3[nodePath.Count];
            for (int k = 0; k < nodePath.Count; k++)
            {
                var np = _roadNetwork.Nodes[nodePath[k]].Position;
                waypoints[k] = new Vector3(np.X, np.Y, 0f);
            }

            _trajectoryPool.RegisterTrajectoryWithKey(waypoints, handle);

            return new PathfindingResultEvent
            {
                RequestId           = req.RequestId,
                IsReachable         = true,
                TotalDistanceMeters = dist[endNode],
                RouteHandle         = handle,
                SourceNodeId        = req.SourceNodeId,
                PrimaryBackend      = NavigationBackend.NavRoadGraph,
                FailureReason       = NavigationFailureReason.NoFailure,
            };
        }

        // ── Navmesh backend ────────────────────────────────────────────────────────

        private unsafe PathfindingResultEvent SolveNavmesh(in PathfindingRequestEvent req, int handle)
        {
            var buf = stackalloc NavWaypoint[MaxNavWaypoints];
            var span = new Span<NavWaypoint>(buf, MaxNavWaypoints);

            uint layerMask = req.NavLayerMask != 0 ? (uint)req.NavLayerMask : 0xFFFFFFFFu;
            int count = _navmesh!.PlanPath(req.Start, req.End, span, layerMask);

            if (count < 2)
                return Unreachable(in req, handle, NavigationBackend.Navmesh);

            // Convert NavWaypoint positions (Recast Y-up: X=east, Y=altitude, Z=north) to Sim
            // (Z-up) 3D waypoints: X=east, Y=north, Z=altitude (§0.1, P3D-303). Arc length is XY.
            var positions = new Vector3[count];
            float totalDist = 0f;
            for (int k = 0; k < count; k++)
            {
                positions[k] = new Vector3(span[k].Position.X, span[k].Position.Z, span[k].Position.Y);
                if (k > 0)
                    totalDist += Vector2.Distance(
                        new Vector2(positions[k - 1].X, positions[k - 1].Y),
                        new Vector2(positions[k].X, positions[k].Y));
            }

            _trajectoryPool.RegisterTrajectoryWithKey(positions, handle);

            return new PathfindingResultEvent
            {
                RequestId            = req.RequestId,
                IsReachable          = true,
                TotalDistanceMeters  = totalDist,
                RouteHandle          = handle,
                SourceNodeId         = req.SourceNodeId,
                NavmeshVersionAtPlan = (int)(_navmesh.QueryVersion()),
                PrimaryBackend       = NavigationBackend.Navmesh,
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
                positions[k] = new Vector3(span[k].Position.X, span[k].Position.Z, span[k].Position.Y);
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

        private int FindNearestNode(Vector2 pos)
        {
            int   best     = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < _roadNetwork.Nodes.Length; i++)
            {
                float d = Vector2.DistanceSquared(pos, _roadNetwork.Nodes[i].Position);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

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
