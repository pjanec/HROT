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

        /// <summary>⭐ CE-11050 — a host with no replication module (the editor) has no <c>DisposalMonitoringSystem</c>, so the system unmaps its own dead rounds: the map does not grow with every round fired, and
        /// an id the allocator gives out again (a preview rewinds it) registers without throwing.
        /// 🔴 Red-proof: drop the <c>Unmap</c> call ⇒ the dead rounds stay in the map and the reissued id throws "already registered".</summary>
        [Fact]
        public void CE11050_ADeadRound_LeavesTheMap_SoTheMapDoesNotGrowAndAReissuedIdRegisters()
        {
            var world = World();
            var map = new NetworkEntityMap();
            var ids = new StubIdAllocator(500);
            var system = new LightweightRoundIdentitySystem(ids, map, localNodeId: 3);
            var rounds = new Entity[3];
            for (int i = 0; i < rounds.Length; i++) { rounds[i] = world.CreateEntity(); world.AddComponent(rounds[i], default(LightweightRound)); }
            system.Execute(world, 0.016f);
            Assert.Equal(3, system.Assigned);
            Assert.True(map.TryGetEntity(500, out _) && map.TryGetEntity(502, out _));

            world.DestroyEntity(rounds[1]);                       // one dies: only its entry goes
            system.Execute(world, 0.016f);
            Assert.True(map.TryGetEntity(500, out _));
            Assert.False(map.TryGetEntity(501, out _));
            Assert.True(map.TryGetEntity(502, out _));

            world.DestroyEntity(rounds[0]); world.DestroyEntity(rounds[2]);
            ids.Reset(500);                                       // a preview rewinds the allocator: 500 is issued again
            var again = world.CreateEntity(); world.AddComponent(again, default(LightweightRound));
            system.Execute(world, 0.016f);                        // unmaps the dead ones BEFORE it registers the new one
            Assert.Equal(1, system.Assigned);
            Assert.Equal(500, world.GetComponentRO<NetworkIdentity>(again).Value);
            Assert.True(map.TryGetEntity(500, out var mapped) && mapped == again);
            Assert.False(map.TryGetEntity(502, out _));
        }
    
        /// <summary>⭐ CE-5061 (unreal-ig H-6) — the allocator reissues an id the map still holds for ANOTHER live entity: the round takes the next id instead of
        /// ending the process ("already registered"), the holder keeps its entry, and the collision is counted.
        /// 🔴 Red-proof: drop the retry loop ⇒ Register throws.</summary>
        [Fact]
        public void CE5061_AReissuedIdStillMapped_TheRoundTakesTheNextId_NoThrow()
        {
            var world = World();
            var map = new NetworkEntityMap();
            var holder = world.CreateEntity();
            map.Register(500, holder);                                   // a restored round (or any entity) still holds 500
            var system = new LightweightRoundIdentitySystem(new StubIdAllocator(500), map, localNodeId: 3);
            var round = world.CreateEntity();
            world.AddComponent(round, default(LightweightRound));

            system.Execute(world, 0.016f);

            Assert.Equal(1, system.Assigned);
            Assert.Equal(1, system.IdCollisions);
            Assert.Equal(501, world.GetComponentRO<NetworkIdentity>(round).Value);
            Assert.True(map.TryGetEntity(500, out var h) && h == holder);
            Assert.True(map.TryGetEntity(501, out var r) && r == round);
        }
    }
}
