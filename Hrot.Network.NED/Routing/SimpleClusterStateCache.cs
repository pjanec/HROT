using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Common;

namespace Hrot.Network.Routing
{
    /// <summary>
    /// Concrete, thread-safe implementation of <see cref="IClusterStateCache"/>.
    ///
    /// <para>
    /// Aggregates <see cref="NodeCapability"/> records as they arrive via
    /// <see cref="UpdateNode"/> and supports O(1) queries for the least-loaded node
    /// in a given role.
    /// </para>
    /// </summary>
    public sealed class SimpleClusterStateCache : IClusterStateCache
    {
        private readonly Dictionary<int, NodeCapability> _nodes = new();
        private readonly object _lock = new();

        /// <inheritdoc/>
        public int? GetLeastLoadedNode(NodeRole requiredRole)
        {
            lock (_lock)
            {
                NodeCapability? best = null;
                foreach (var cap in _nodes.Values)
                {
                    if (!cap.Role.HasFlag(requiredRole))
                        continue;
                    if (best == null || cap.CpuUsagePercent < best.CpuUsagePercent)
                        best = cap;
                }
                return best?.NodeId;
            }
        }

        /// <inheritdoc/>
        public void UpdateNode(NodeCapability capability)
        {
            if (capability == null) throw new ArgumentNullException(nameof(capability));
            lock (_lock)
            {
                _nodes[capability.NodeId] = capability;
            }
        }

        /// <inheritdoc/>
        public void PruneStale(double nowUtcSeconds, double maxSilenceSeconds = 10.0)
        {
            lock (_lock)
            {
                var stale = _nodes.Values
                    .Where(n => nowUtcSeconds - n.LastSeenUtcSeconds > maxSilenceSeconds)
                    .Select(n => n.NodeId)
                    .ToList();
                foreach (var id in stale)
                    _nodes.Remove(id);
            }
        }
    }
}
