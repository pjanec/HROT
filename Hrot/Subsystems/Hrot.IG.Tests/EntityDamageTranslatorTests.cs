using Hrot.NED.Descriptors;
using Fdp.Toolkit.Combat.Components;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;
using Xunit;
using Fdp.Interfaces;

namespace Hrot.IG.Tests
{
    public class EntityDamageTranslatorTests
    {
        private const long KnownId   = 42L;
        private const long UnknownId = 99L;

        [Fact]
        public void Decode_KnownEntity_WritesTheAuthoritysHealth()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            var entityMap = new NetworkEntityMap();
            var entity    = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new TestEntityDamageIngressTranslator(participant, entityMap, ghostCreationSystem);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new EntityDamage
            {
                EntityId = (int)KnownId,
                Current  = 12.5f,
                Max      = 50f
            }, cmd, repo);

            // ⭐ CE-196 — the pair arrives verbatim; nothing is converted to a percentage on the way in.
            //   A receiver that kept its own TKB-seeded Max is exactly the divergence this removed.
            Assert.True(cmd.SetComponentCalled);
            Assert.NotNull(cmd.LastHealth);
            Assert.Equal(12.5f, cmd.LastHealth!.Value.Current);
            Assert.Equal(50f,   cmd.LastHealth!.Value.Max);
        }

        [Fact]
        public void Decode_UnknownEntity_CreatesGhostAndWritesHealth()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>(); // required by GhostCreationSystem
            repo.RegisterComponent<GhostStateTracker>(); // required by GhostCreationSystem
            var entityMap = new NetworkEntityMap();

            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new TestEntityDamageIngressTranslator(participant, entityMap, ghostCreationSystem);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new EntityDamage
            {
                EntityId = (int)UnknownId,
                Current  = 25f,
                Max      = 100f
            }, cmd, repo);

            // Ghost must be created and registered
            Assert.True(entityMap.TryGetEntity(UnknownId, out _),
                "Ghost must be registered in entityMap after encountering unknown entity");
            // Health component must be applied to the new ghost
            Assert.True(cmd.SetComponentCalled,
                "SetComponent must be called with Health after ghost creation");
            Assert.Equal(25f,  cmd.LastHealth?.Current);
            Assert.Equal(100f, cmd.LastHealth?.Max);
        }

        /// <summary>
        /// ⭐ The other half of CE-272's guard: the node that RECORDS entity-level ownership is the source of
        /// health and must not take its own loopback sample back. Paired with the two tests above, which
        /// prove a replica (with or without an ownership record yet) does take it.
        /// </summary>
        [Fact]
        public void Decode_OnTheRecordedOwner_DoesNotWriteHealth()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            repo.RegisterComponent<NetworkAuthority>();
            var entityMap = new NetworkEntityMap();
            var entity    = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 3, localNodeId: 3));
            entityMap.Register(KnownId, entity);

            var translator = new TestEntityDamageIngressTranslator(participant, entityMap, new GhostCreationSystem(entityMap));
            var cmd = new RecordingCommandBuffer();
            translator.DecodeForTest(new EntityDamage { EntityId = (int)KnownId, Current = 1f, Max = 50f }, cmd, repo);

            Assert.False(cmd.SetComponentCalled, "the recorded owner must not take its own health back");
        }

        /// <summary>⭐ A replica with a RECORDED ownership elsewhere takes the owner's health.</summary>
        [Fact]
        public void Decode_OnARecordedReplica_WritesHealth()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            repo.RegisterComponent<NetworkAuthority>();
            var entityMap = new NetworkEntityMap();
            var entity    = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 3, localNodeId: 4));
            entityMap.Register(KnownId, entity);

            var translator = new TestEntityDamageIngressTranslator(participant, entityMap, new GhostCreationSystem(entityMap));
            var cmd = new RecordingCommandBuffer();
            translator.DecodeForTest(new EntityDamage { EntityId = (int)KnownId, Current = 1f, Max = 50f }, cmd, repo);

            Assert.Equal(1f, cmd.LastHealth?.Current);
        }

        /// <summary>⭐ S8 / F-5 — the guard keys on the <c>dtEntityDamage</c> RECORD, not the entity's primary owner. SimHost
        /// created the tank (primary owner = me) and granted its Brain group, Health with it, to CGF (node 5): SimHost is a
        /// replica of Health now and must take CGF's value — keyed on the entity it skipped it and kept a stale full health
        /// (CE-272's bug, on every SimHost-created entity). 📄 <c>DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S8.</summary>
        [Fact]
        public void Decode_OnThePrimaryOwner_WhoseDamageDescriptorWasGrantedAway_WritesHealth()
        {
            var (repo, entityMap, entity) = Recorded(primaryOwner: 3, local: 3, damageOwner: 5);
            using var participant = new DdsParticipant(0);
            var translator = new TestEntityDamageIngressTranslator(participant, entityMap, new GhostCreationSystem(entityMap));
            var cmd = new RecordingCommandBuffer();
            translator.DecodeForTest(new EntityDamage { EntityId = (int)KnownId, Current = 1f, Max = 50f }, cmd, repo);

            Assert.Equal(1f, cmd.LastHealth?.Current);
        }

        /// <summary>⭐ S8 — the converse: CGF holds the granted <c>dtEntityDamage</c> of an entity another node created, so
        /// its own sample looping back must not overwrite the Health it writes.</summary>
        [Fact]
        public void Decode_OnTheGrantedOwnerOfTheDamageDescriptor_DoesNotWriteHealth()
        {
            var (repo, entityMap, entity) = Recorded(primaryOwner: 3, local: 5, damageOwner: 5);
            using var participant = new DdsParticipant(0);
            var translator = new TestEntityDamageIngressTranslator(participant, entityMap, new GhostCreationSystem(entityMap));
            var cmd = new RecordingCommandBuffer();
            translator.DecodeForTest(new EntityDamage { EntityId = (int)KnownId, Current = 1f, Max = 50f }, cmd, repo);

            Assert.False(cmd.SetComponentCalled, "the owner of dtEntityDamage must not take its own health back");
        }

        private static (EntityRepository, NetworkEntityMap, Entity) Recorded(int primaryOwner, int local, int damageOwner)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            var entityMap = new NetworkEntityMap();
            var entity    = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: primaryOwner, localNodeId: local));
            var record = new DescriptorOwnership();
            record.Map[Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey((long)EDescriptorType.dtEntityDamage, 0)] = damageOwner;
            repo.SetManagedComponent(entity, record);
            entityMap.Register(KnownId, entity);
            return (repo, entityMap, entity);
        }

        private sealed class TestEntityDamageIngressTranslator : EntityDamageIngressTranslator
        {
            public TestEntityDamageIngressTranslator(
                DdsParticipant participant,
                NetworkEntityMap entityMap,
                GhostCreationSystem ghostCreationSystem)
                : base(participant, entityMap, ghostCreationSystem, localNodeId: 0)
            {
            }

            public void DecodeForTest(in EntityDamage data, IEntityCommandBuffer cmd, ISimulationView view)
            {
                base.Decode(data, cmd, view);
            }
        }

        private sealed class RecordingCommandBuffer : IEntityCommandBuffer
        {
            public bool SetComponentCalled { get; private set; }
            public Health? LastHealth { get; private set; }

            public Entity CreateEntity() => new Entity();
            public void DestroyEntity(Entity entity) { }
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged { }
            public void AddEmptyComponent<T>(Entity entity) where T : unmanaged { }
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
            {
                SetComponentCalled = true;
                if (component is Health health)
                    LastHealth = health;
            }
            public void RemoveComponent<T>(Entity entity) where T : unmanaged { }
            public void AddManagedComponent<T>(Entity entity, T? component) where T : class { }
            public void SetManagedComponent<T>(Entity entity, T? component) where T : class { }
            public void RemoveManagedComponent<T>(Entity entity) where T : class { }
            public void PublishEvent<T>(in T evt) where T : unmanaged { }
            public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size) { }
            public void SetManagedComponentRaw(Entity entity, int typeId, object obj) { }
            public void SetLifecycleState(Entity entity, EntityLifecycle state) { }
        }
    }
}
