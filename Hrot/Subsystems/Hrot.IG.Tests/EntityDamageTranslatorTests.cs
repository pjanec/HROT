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
