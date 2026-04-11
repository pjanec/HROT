using System;
using System.Collections.Generic;

namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Resolves the shortest path between two state IDs using Breadth-First Search over
    /// an <see cref="ITransitionGraph"/>.
    ///
    /// <para>This class is fully generic — it operates on integer state IDs and has no
    /// dependency on any Hrot or application-layer type.  Hrot callers cast their
    /// <c>ClusterState</c> enum values to and from <c>int</c> at the call site.</para>
    /// </summary>
    public sealed class TransitionPlanner
    {
        private readonly ITransitionGraph _graph;

        /// <param name="graph">
        /// State-transition graph that defines the valid directed edges.
        /// Build a Hrot-specific graph with <c>HrotStateGraph.Build()</c>.
        /// </param>
        public TransitionPlanner(ITransitionGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        /// <summary>
        /// Computes the shortest directed path from <paramref name="fromStateId"/> to
        /// <paramref name="toStateId"/> using BFS.
        /// </summary>
        /// <returns>
        /// The ordered list of state IDs to traverse, <b>excluding</b>
        /// <paramref name="fromStateId"/>.  Returns an empty list when the two IDs are equal.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no path exists in the graph.  The message includes both state IDs.
        /// </exception>
        public IReadOnlyList<int> CalculateShortestPath(int fromStateId, int toStateId)
        {
            if (fromStateId == toStateId) return Array.Empty<int>();

            var visited = new HashSet<int> { fromStateId };
            var queue   = new Queue<(int StateId, List<int> Path)>();
            queue.Enqueue((fromStateId, new List<int>()));

            while (queue.Count > 0)
            {
                var (stateId, path) = queue.Dequeue();
                var neighbors       = _graph.GetNeighbors(stateId);

                foreach (var next in neighbors)
                {
                    if (!visited.Add(next)) continue;

                    var newPath = new List<int>(path) { next };

                    if (next == toStateId) return newPath;

                    queue.Enqueue((next, newPath));
                }
            }

            throw new InvalidOperationException(
                $"[TransitionPlanner] No valid path from state {fromStateId} to state {toStateId}. " +
                "The transition is not reachable in the planning graph.");
        }
    }
}
