using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.Network.Routing;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// CE-283 piece C — the role-filtered <see cref="ClusterCacheExpectedPeersProvider"/>
    /// (DESIGN_Cross_Node_Construction_Barrier.md §3b). Deterministic proof of the acceptance-1 property:
    /// a creator waits only on present peers whose role INITIALISES one of the entity's
    /// <c>[RequiresPeerInit]</c> components — the required-role peer is included, non-required roles and the
    /// local node are excluded. <c>SimTransform</c> is the marked component; <c>MuscleGround</c> is its
    /// declared initialiser (<c>HrotRoleComponentSets.Initialises</c>).
    /// </summary>
    public sealed class ClusterCacheExpectedPeersProviderTests
    {
        private sealed class FakeCache : IClusterStateCache
        {
            private readonly List<NodeCapability> _nodes;
            public FakeCache(params NodeCapability[] nodes) => _nodes = nodes.ToList();
            public int? GetLeastLoadedNode(NodeRole requiredRole) => null;
            public IReadOnlyList<int> AllNodeIds() => _nodes.Select(n => n.NodeId).ToList();
            public IReadOnlyList<NodeCapability> AllNodes() => _nodes;
            public void UpdateNode(NodeCapability capability) { }
            public void PruneStale(double nowUtcSeconds, double maxSilenceSeconds = 10.0) { }
        }

        private static BitMask512 SpatialMask()
        {
            var m = default(BitMask512);
            m.SetBit(ComponentType<SimTransform>.ID); // [RequiresPeerInit] → needs a Muscle to init
            return m;
        }

        [Fact]
        public void RoleFilter_IncludesMuscle_ExcludesMap2DAndNonInit_ForSpatialType()
        {
            var cache = new FakeCache(
                new NodeCapability { NodeId = 1,   Role = NodeRole.MuscleGround },
                new NodeCapability { NodeId = 100, Role = NodeRole.Map2D },
                new NodeCapability { NodeId = 300, Role = NodeRole.None });
            var provider = new ClusterCacheExpectedPeersProvider(cache);

            var mask = SpatialMask();
            var peers = provider.GetExpectedPeers(in mask, localNodeId: 400); // creator = a Brain node

            Assert.Contains(1, peers);          // MuscleGround initialises SimTransform → waited on
            Assert.DoesNotContain(100, peers);  // Map2D does not initialise it → NOT waited on
            Assert.DoesNotContain(300, peers);  // None → NOT waited on
        }

        [Fact]
        public void RoleFilter_Empty_WhenTypeHasNoRequiresPeerInitComponent()
        {
            var cache = new FakeCache(new NodeCapability { NodeId = 1, Role = NodeRole.MuscleGround });
            var provider = new ClusterCacheExpectedPeersProvider(cache);

            var mask = default(BitMask512); // no SimTransform → nothing needs peer init
            var peers = provider.GetExpectedPeers(in mask, localNodeId: 400);

            Assert.Empty(peers);
        }

        [Fact]
        public void RoleFilter_ExcludesLocalNode_EvenWhenItsRoleInitialises()
        {
            var cache = new FakeCache(
                new NodeCapability { NodeId = 1, Role = NodeRole.MuscleGround },
                new NodeCapability { NodeId = 5, Role = NodeRole.MuscleGround });
            var provider = new ClusterCacheExpectedPeersProvider(cache);

            var mask = SpatialMask();
            var peers = provider.GetExpectedPeers(in mask, localNodeId: 5); // local IS a Muscle

            Assert.Contains(1, peers);
            Assert.DoesNotContain(5, peers); // a node never waits on itself
        }

        [Fact]
        public void RoleFilter_MultiRoleNode_CountsIfAnyRoleInitialises()
        {
            // A node declaring MuscleGround|Perception still initialises SimTransform (via MuscleGround).
            var cache = new FakeCache(
                new NodeCapability { NodeId = 2, Role = NodeRole.MuscleGround | NodeRole.Perception });
            var provider = new ClusterCacheExpectedPeersProvider(cache);

            var mask = SpatialMask();
            var peers = provider.GetExpectedPeers(in mask, localNodeId: 400);

            Assert.Contains(2, peers);
        }
    }
}
