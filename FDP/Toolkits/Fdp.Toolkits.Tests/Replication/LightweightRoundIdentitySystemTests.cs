using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Tests.Helpers;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>⭐ LR-2 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — a visible round gets its network identity and nothing else.</summary>
    public class LightweightRoundIdentitySystemTests
    {
        private static EntityRepository World()
        {
            var world = new EntityRepository();
            world.RegisterComponent<LightweightRound>();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkAuthority>();
            return world;
        }

        /// <summary>A tagged round gets an id from the pool, LOCAL ownership and a map entry — once; an untagged entity gets nothing,
        /// and its lifecycle is left alone (no construction, no acks).</summary>
        [Fact]
        public void LR2_ATaggedRound_GetsAnIdLocalOwnershipAndAMapEntry_Once()
        {
            var world = World();
            var map = new NetworkEntityMap();
            var system = new LightweightRoundIdentitySystem(new StubIdAllocator(500), map, localNodeId: 3);
            var round = world.CreateEntity();
            world.AddComponent(round, default(LightweightRound));
            var other = world.CreateEntity();

            system.Execute(world, 0.016f);

            Assert.Equal(1, system.Assigned);
            Assert.Equal(500, world.GetComponentRO<NetworkIdentity>(round).Value);
            var authority = world.GetComponentRO<NetworkAuthority>(round);
            Assert.True(authority.HasAuthority);
            Assert.Equal(3, authority.PrimaryOwnerId);
            Assert.True(map.TryGetEntity(500, out var mapped) && mapped == round);
            Assert.False(world.HasComponent<NetworkIdentity>(other));
            Assert.Equal(EntityLifecycle.Active, world.GetLifecycleState(round));

            system.Execute(world, 0.016f);
            Assert.Equal(0, system.Assigned);                       // already identified
        }

        /// <summary>A host with no allocator assigns nothing — its rounds stay local.</summary>
        [Fact]
        public void LR2_NoAllocator_AssignsNothing()
        {
            var world = World();
            var round = world.CreateEntity();
            world.AddComponent(round, default(LightweightRound));
            var system = new LightweightRoundIdentitySystem(null, new NetworkEntityMap(), 1);
            system.Execute(world, 0.016f);
            Assert.Equal(0, system.Assigned);
            Assert.False(world.HasComponent<NetworkIdentity>(round));
        }
    }
}
