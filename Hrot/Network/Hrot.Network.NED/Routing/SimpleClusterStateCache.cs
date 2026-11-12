using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Common;
using Fdp.Core;

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
        // CE-288 (C2): observed "does NOT actually support this capability" overrides — the self-heal from the
        // creator's short phase-1 probe (§3c ②). Kept SEPARATE from _nodes so it survives PollNetwork rebuilding
        // NodeCapability from the durable descriptor each poll (the node still ADVERTISES the token; observation
        // says it never delivers). Supports() forces false for an overridden (node, token).
        private readonly HashSet<(int NodeId, string Token)> _unsupportedOverrides = new();
        private readonly object _lock = new();

        /// <summary>⭐ C6 — the master's InSync set; <see langword="null"/> until one is received (then nothing is filtered).</summary>
        private HashSet<int>? _inSync;

        /// <inheritdoc/>
        public bool IsInSync(int nodeId)
        {
            lock (_lock) return _inSync == null || _inSync.Contains(nodeId);
        }

        /// <summary>
        /// ⭐⭐ C6 (Q91 §4-N) — the master's InSync set arrived. ⭐ J4 (R-4013): a node ENTERING it loses every "unsupported"
        /// mark — a node reset by a catch-up never departs, yet may have missed a probe while it reloaded.
        /// </summary>
        public void SetInSync(IEnumerable<int> nodeIds)
        {
            lock (_lock)
            {
                var next = new HashSet<int>(nodeIds);
                foreach (var id in next)
                    if (_inSync == null || !_inSync.Contains(id))
                        _unsupportedOverrides.RemoveWhere(o => o.NodeId == id);
                _inSync = next;
            }
        }

        /// <summary>The capability record held for a node (the test seam — ⭐ AQ107 I reads the CPU load the heartbeat ingest stored).</summary>
        internal NodeCapability? TryGet(int nodeId)
        {
            lock (_lock) return _nodes.TryGetValue(nodeId, out var cap) ? cap : null;
        }

        /// <inheritdoc/>
        public int? PickNodeForRole(NodeRole requiredRole)
        {
            lock (_lock)
            {
                // ⭐ CE-4075 — no load figure is measured or sent (the heartbeat's CpuUsage was dropped; it was always 0), so every carrier of the
                //   role is equally loaded: the pick is the LOWEST node id, which is deterministic (it used to be dictionary order).
                int? best = null;
                foreach (var cap in _nodes.Values)
                {
                    if (!cap.Role.HasFlag(requiredRole))
                        continue;
                    if (best == null || (int)cap.NodeId < best.Value)
                        best = (int)cap.NodeId;
                }
                return best;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<int> AllNodeIds()
        {
            lock (_lock)
            {
                // ⭐ POC 3 (the parallel-simulation experiment; POC 2 trace): this was 55 % of the CGF's EqsSensorConfig egress — a new
                //   list under the lock for EVERY sensor EVERY frame (SolverFor). One immutable snapshot of the ids, rebuilt only when the
                //   SET of nodes changes (a heartbeat that only refreshes a known node keeps it). Same ids, same order.
                return _idsSnapshot ??= _nodes.Keys.ToArray();
            }
        }

        private int[]? _idsSnapshot;   // null ⇒ rebuilt on the next AllNodeIds

        /// <inheritdoc/>
        public bool HasRole(int nodeId, NodeRole role)
        {
            lock (_lock)
            {
                return _nodes.TryGetValue(nodeId, out var cap) && cap.Role.HasFlag(role);
            }
        }

        /// <summary>⭐ Player Join §9.1 (D-3) — <paramref name="nodeId"/> reported <paramref name="featureId"/> ON on its last heartbeat.</summary>
        public bool FeatureOn(int nodeId, string featureId)
        {
            lock (_lock)
                return _nodes.TryGetValue(nodeId, out var cap) && cap.FeaturesOn.Contains(featureId);
        }

        /// <inheritdoc/>
        public bool Supports(int nodeId, string token)
        {
            lock (_lock)
            {
                if (_unsupportedOverrides.Contains((nodeId, token)))
                    return false;   // CE-288: observed-unsupported override wins over the advertised token.
                return _nodes.TryGetValue(nodeId, out var cap) && cap.Capabilities.Contains(token);
            }
        }

        /// <inheritdoc/>
        public void RecordUnsupported(int nodeId, string token)
        {
            lock (_lock)
            {
                _unsupportedOverrides.Add((nodeId, token));
            }
        }

        /// <inheritdoc/>
        public void UpdateNode(NodeCapability capability)
        {
            if (capability == null) throw new ArgumentNullException(nameof(capability));
            lock (_lock)
            {
                if (!_nodes.ContainsKey(capability.NodeId)) _idsSnapshot = null;   // a NEW node changes the set
                _nodes[capability.NodeId] = capability;
            }
        }

        /// <inheritdoc/>
        /// <summary>
        /// ⭐ S7 — forgets a node the moment its heartbeat instance goes not-alive (crash: lease expiry; exit: dispose),
        /// so the grant strategy stops choosing it and the reliable-init wait-set stops waiting for it. Returns whether
        /// the node was known. 📄 docs/DESIGN_Ownership_Groups_And_Grants.md §5.6 S7.
        /// </summary>
        public bool RemoveNode(int nodeId)
        {
            lock (_lock)
            {
                _unsupportedOverrides.RemoveWhere(o => o.NodeId == nodeId);   // ⭐ J4 — a departure clears the mark too
                bool removed = _nodes.Remove(nodeId);
                if (removed) _idsSnapshot = null;
                return removed;
            }
        }

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
                if (stale.Count > 0) _idsSnapshot = null;
            }
        }
    }
}
