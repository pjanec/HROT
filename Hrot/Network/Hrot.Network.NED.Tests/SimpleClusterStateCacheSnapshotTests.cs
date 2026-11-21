using System;
using Fdp.Core;
using Hrot.Network.Routing;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐ POC 3 (the parallel-simulation experiment) — <see cref="SimpleClusterStateCache.AllNodeIds"/> hands back ONE snapshot of the ids,
    /// rebuilt only when the SET of nodes changes: a heartbeat refreshing a known node keeps it (and allocates nothing); a new node, a removal
    /// and a prune each show at once. 📐 Before, every call built a new list under the lock — 55 % of the CGF's EqsSensorConfig egress.
    /// 🔴 Red-proof: the snapshot not dropped on a new node ⇒ node 4 is missing.
    /// </summary>
    public sealed class SimpleClusterStateCacheSnapshotTests
    {
        private static NodeCapability Node(int id, double seen = 100) => new() { NodeId = (Fdp.Core.NodeId)id, Role = NodeRole.Perception, LastSeenUtcSeconds = seen };

        [Fact]
        public void TheIds_AreOneSnapshot_UntilTheSetOfNodesChanges()
        {
            var cache = new SimpleClusterStateCache();
            cache.UpdateNode(Node(2)); cache.UpdateNode(Node(3));
            var a = cache.AllNodeIds();
            Assert.Equal(new[] { 2, 3 }, a);

            cache.UpdateNode(Node(2, 101));   // a heartbeat of a known node
            Assert.Same(a, cache.AllNodeIds());
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) cache.AllNodeIds();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

            cache.UpdateNode(Node(4));        // a new node
            Assert.Equal(new[] { 2, 3, 4 }, cache.AllNodeIds());
            Assert.True(cache.RemoveNode(3));
            Assert.Equal(new[] { 2, 4 }, cache.AllNodeIds());
            cache.PruneStale(nowUtcSeconds: 200, maxSilenceSeconds: 10);   // every node silent for ≥ 99 s
            Assert.Empty(cache.AllNodeIds());
        }
    }
}
