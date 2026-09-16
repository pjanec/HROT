using System.Collections.Generic;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Abstractions;

namespace Hrot.Network.Routing
{
    /// <summary>
    /// Backs <see cref="IExpectedPeersProvider"/> with the NED cluster cache
    /// (<see cref="IClusterStateCache"/>): the creator waits for every present peer node that
    /// <b>advertises the <c>fdp.reliable-init</c> capability</b>.
    ///
    /// <para>⭐ CE-287 (C1) — the PRODUCTION wait-set: present nodes minus local, filtered to those that
    /// advertise <c>fdp.reliable-init</c> (<see cref="IClusterStateCache.Supports"/>). A host that does not
    /// support reliable init is never waited for — the proactive half of graceful degradation
    /// (DESIGN_Cross_Node_Construction_Barrier.md §3c ①). ⛔ This is NOT a type/component filter: the peer's
    /// wait condition is host-local and opaque to the creator (§3b). The creator MAY narrow this further to a
    /// role subset via <c>SpawnEntityCommand.ReliableInitPeers</c>, applied at the spawn stamp site.</para>
    ///
    /// <para>⚠ SUPERSEDES slice A's proof-correct "all present peers" (§3a.7): that blocked a creator on peers
    /// that never init the type; the capability filter + the optional creator list fix it.</para>
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
                if (id != localNodeId && _cache.Supports(id, CapabilityTokens.ReliableInit))
                    peers.Add(id);
            return peers;
        }
    }
}
