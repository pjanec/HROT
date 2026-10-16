using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Hrot.Network.Routing;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// CE-287 (C1) — the production reliable-init wait-set. The provider yields the present peers minus local,
    /// filtered to those that advertise <c>fdp.reliable-init</c> (§3c ① — a non-supporting host is never waited
    /// for, the proactive half of graceful degradation).
    /// </summary>
    public class ClusterCacheExpectedPeersProviderTests
    {
        private static NodeCapability Node(int id, params string[] caps) => Node(id, NodeRole.MuscleGround, caps);

        private static NodeCapability Node(int id, NodeRole role, params string[] caps) => new()
        {
            NodeId = id,
            Role = role,
            Capabilities = new System.Collections.Generic.HashSet<string>(caps),
            LastSeenUtcSeconds = 1.0,
        };

        [Fact]
        public void GetExpectedPeers_ExcludesLocal_AndNonSupportingHosts()
        {
            var cache = new SimpleClusterStateCache();
            cache.UpdateNode(Node(1, CapabilityTokens.ReliableInit));                                  // supporting, authoritative
            cache.UpdateNode(Node(2, NodeRole.Brain, CapabilityTokens.ReliableInit, "fdp.role.brain")); // supporting, authoritative
            cache.UpdateNode(Node(3));                                                // present but NOT supporting
            cache.UpdateNode(Node(400, CapabilityTokens.ReliableInit));               // local (excluded)

            var provider = new ClusterCacheExpectedPeersProvider(cache);
            var peers = provider.GetExpectedPeers(blueprintId: 0, localNodeId: 400).OrderBy(x => x).ToArray();

            // node 3 dropped (no fdp.reliable-init), node 400 dropped (local).
            Assert.Equal(new[] { 1, 2 }, peers);
        }

        [Fact]
        public void GetExpectedPeers_Empty_WhenNoPeerAdvertisesReliableInit()
        {
            var cache = new SimpleClusterStateCache();
            cache.UpdateNode(Node(1, "fdp.role.brain"));   // present, role only, no reliable-init
            cache.UpdateNode(Node(2));                     // present, nothing

            var provider = new ClusterCacheExpectedPeersProvider(cache);
            Assert.Empty(provider.GetExpectedPeers(0, localNodeId: 400));
        }

        /// <summary>
        /// ⭐⭐⭐ C6 (Q91 §4-N, K6) — a creator waits only for peers that are in the master's InSync set AND authoritative: a
        /// joiner still catching up and an OBSERVER (a Player, Map2D only) never gate a spawn. Before any set is received,
        /// in-sync filters nothing (fail-safe toward waiting).
        /// </summary>
        [Fact]
        public void C6_OnlyInSyncAuthoritativePeers_AreWaitedFor()
        {
            var cache = new SimpleClusterStateCache();
            cache.UpdateNode(Node(1, NodeRole.MuscleGround, CapabilityTokens.ReliableInit));
            cache.UpdateNode(Node(2, NodeRole.Brain, CapabilityTokens.ReliableInit));
            cache.UpdateNode(Node(100, NodeRole.Map2D, CapabilityTokens.ReliableInit));   // a Player: an observer
            var provider = new ClusterCacheExpectedPeersProvider(cache);

            Assert.Equal(new[] { 1, 2 }, provider.GetExpectedPeers(0, localNodeId: 400).OrderBy(x => x).ToArray());

            cache.SetInSync(new[] { 1, 100 });                                         // node 2 is catching up
            Assert.Equal(new[] { 1 }, provider.GetExpectedPeers(0, localNodeId: 400).ToArray());
        }

        /// <summary>⭐ J4 (R-4013) — a node ENTERING the InSync set, and a node that departs, lose their "unsupported" mark.</summary>
        [Fact]
        public void J4_EnteringInSyncOrDeparting_ClearsTheUnsupportedMark()
        {
            var cache = new SimpleClusterStateCache();
            cache.UpdateNode(Node(1, CapabilityTokens.ReliableInit));
            cache.SetInSync(new[] { 2 });
            cache.RecordUnsupported(1, CapabilityTokens.ReliableInit);
            Assert.False(cache.Supports(1, CapabilityTokens.ReliableInit));

            cache.SetInSync(new[] { 1, 2 });                                           // node 1 enters
            Assert.True(cache.Supports(1, CapabilityTokens.ReliableInit));

            cache.RecordUnsupported(1, CapabilityTokens.ReliableInit);
            cache.RemoveNode(1);
            cache.UpdateNode(Node(1, CapabilityTokens.ReliableInit));                  // back after a departure
            Assert.True(cache.Supports(1, CapabilityTokens.ReliableInit));
        }
    }
}
