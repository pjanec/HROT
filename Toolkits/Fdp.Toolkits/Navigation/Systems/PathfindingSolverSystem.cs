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
    /// supplied <see cref="RoadNetworkBlob"/>.
    ///
    /// <para><b>Execution context:</b> runs inside <see cref="Modules.NavigationSolverModule"/> at
    /// 10 Hz on a background thread (SoD snapshot).  Results are published back as
    /// <see cref="PathfindingResultEvent"/> via <see cref="IEntityCommandBuffer"/> and
    /// materialized on the main thread by <c>PathfindingResultMaterializationSystem</c>.</para>
    ///
    /// <para><b>Empty / default network:</b>
    /// If <see cref="RoadNetworkBlob.Nodes"/> is not created or has no nodes, every
    /// request returns <see cref="PathfindingResultEvent.IsReachable"/> = <c>false</c>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class PathfindingSolverSystem : IEcsModuleSystem
    {
        private readonly RoadNetworkBlob       _roadNetwork;
        private readonly TrajectoryPoolManager _trajectoryPool;

        /// <summary>
        /// Initialises the solver with the road network and trajectory pool.
        /// </summary>
        /// <param name="roadNetwork">
        ///   Static road graph. Pass <c>default</c> (empty blob) for maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        ///   Shared trajectory pool.  Must not be <c>null</c>.
        /// </param>
        public PathfindingSolverSystem(RoadNetworkBlob roadNetwork, TrajectoryPoolManager trajectoryPool)
        {
            _roadNetwork    = roadNetwork;
            _trajectoryPool = trajectoryPool ?? throw new ArgumentNullException(nameof(trajectoryPool));
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // Read all accumulated request events since the last solver tick.
            var requests = view.ReadEvents<PathfindingRequestEvent>();
            if (requests.IsEmpty) return;

            var cmd = view.GetCommandBuffer();
            bool networkEmpty = !_roadNetwork.Nodes.IsCreated || _roadNetwork.Nodes.Length == 0;

            for (int i = 0; i < requests.Length; i++)
            {
                ref readonly var req = ref requests[i];

                var result = networkEmpty ? Unreachable(in req) : SolvePath(in req);

                cmd.PublishEvent(new PathfindingResultEvent
                {
                    RequestId           = result.RequestId,
                    IsReachable         = result.IsReachable,
                    TotalDistanceMeters = result.TotalDistanceMeters,
                    RouteHandle         = result.RouteHandle,
                    SourceNodeId        = result.SourceNodeId,
                });
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>Runs Dijkstra from the road node nearest to <c>req.Start</c> to the node
        /// nearest to <c>req.End</c>, then registers the resulting waypoints.</summary>
        private PathfindingResultEvent SolvePath(in PathfindingRequestEvent req)
        {
            var start2D = new Vector2(req.Start.X, req.Start.Y);
            var end2D   = new Vector2(req.End.X,   req.End.Y);

            int startNode = FindNearestNode(start2D);
            int endNode   = FindNearestNode(end2D);

            if (startNode < 0 || endNode < 0)
                return Unreachable(in req);

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
                return Unreachable(in req);

            // Reconstruct node sequence
            var nodePath = new List<int>();
            for (int n = endNode; n >= 0; n = prev[n])
            {
                nodePath.Add(n);
                if (n == startNode) break;
            }
            nodePath.Reverse();

            // Convert to Vector2 waypoints
            var waypoints = new Vector2[nodePath.Count];
            for (int k = 0; k < nodePath.Count; k++)
                waypoints[k] = _roadNetwork.Nodes[nodePath[k]].Position;

            int handle = _trajectoryPool.RegisterTrajectory(waypoints);

            return new PathfindingResultEvent
            {
                RequestId           = req.RequestId,
                IsReachable         = true,
                TotalDistanceMeters = dist[endNode],
                RouteHandle         = handle,
                SourceNodeId        = req.SourceNodeId,
            };
        }

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

        private static PathfindingResultEvent Unreachable(in PathfindingRequestEvent req) =>
            new PathfindingResultEvent
            {
                RequestId           = req.RequestId,
                IsReachable         = false,
                TotalDistanceMeters = 0f,
                RouteHandle         = -1,
                SourceNodeId        = req.SourceNodeId,
            };
    }
}
