using System.Collections.Generic;
using Fdp.Toolkit.Replication.Abstractions;

namespace Hrot.Network.Routing
{
    /// <summary>
    /// Backs <see cref="IExpectedPeersProvider"/> with the NED cluster cache
    /// (<see cref="IClusterStateCache"/>): the creator waits for every present peer node.
    ///
    /// <para>⚠ Barrier slice A (CE-283) — PROOF-correct, not production-correct: it yields <b>all
    /// present peers except local</b>, ignoring the entity type. In production a creator would then
    /// block on peers that never initialise the type; piece C narrows the set to the roles that
    /// register a participant for the entity type. See
    /// DESIGN_Cross_Node_Construction_Barrier.md §3a.7 (A5 decision).</para>
    /// </summary>
    public sealed class ClusterCacheExpectedPeersProvider : IExpectedPeersProvider
    {
        private readonly IClusterStateCache _cache;

        public ClusterCacheExpectedPeersProvider(IClusterStateCache cache)
        {
            _cache = cache;
        }

        public IReadOnlyList<int> GetExpectedPeers(long blueprintId, int localNodeId)
        {
            var all = _cache.AllNodeIds();
            var peers = new List<int>(all.Count);
            foreach (var id in all)
                if (id != localNodeId)
                    peers.Add(id);
            return peers;
        }
    }
}
