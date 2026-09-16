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
        private static NodeCapability Node(int id, params string[] caps) => new()
        {
            NodeId = id,
            Capabilities = new System.Collections.Generic.HashSet<string>(caps),
            LastSeenUtcSeconds = 1.0,
        };

        [Fact]
        public void GetExpectedPeers_ExcludesLocal_AndNonSupportingHosts()
        {
            var cache = new SimpleClusterStateCache();
            cache.UpdateNode(Node(1, CapabilityTokens.ReliableInit));                 // supporting peer
            cache.UpdateNode(Node(2, CapabilityTokens.ReliableInit, "fdp.role.map2d")); // supporting peer (also a role)
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
    }
}
