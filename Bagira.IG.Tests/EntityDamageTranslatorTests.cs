using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using Bagira.IG.Translators;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.IG.Tests
{
    public class EntityDamageTranslatorTests
    {
        private const long KnownId = 42L;
        private const long UnknownId = 99L;

        [Fact]
        public void Decode_KnownEntity_SetsIgHealthState()
        {
            using var participant = new DdsParticipant(0);
            var repo = new EntityRepository();
            var entityMap = new NetworkEntityMap();
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var translator = new TestEntityDamageTranslator(participant, entityMap);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new EntityDamage
            {
                EntityId = (int)KnownId,
                Damage = 75f
            }, cmd, repo);

            Assert.True(cmd.SetComponentCalled);
            Assert.NotNull(cmd.LastHealthState);
            Assert.Equal(75f, cmd.LastHealthState!.Value.Damage);
        }

        [Fact]
        public void Decode_UnknownEntity_IsSkipped()
        {
            using var participant = new DdsParticipant(0);
            var repo = new EntityRepository();
            var entityMap = new NetworkEntityMap();

            var translator = new TestEntityDamageTranslator(participant, entityMap);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new EntityDamage
            {
                EntityId = (int)UnknownId,
                Damage = 25f
            }, cmd, repo);

            Assert.False(cmd.SetComponentCalled);
        }

        private sealed class TestEntityDamageTranslator : EntityDamageTranslator
        {
            public TestEntityDamageTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
                : base(participant, entityMap)
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
            public IgHealthState? LastHealthState { get; private set; }

            public Entity CreateEntity() => new Entity();
            public void DestroyEntity(Entity entity) { }
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged { }
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
            {
                SetComponentCalled = true;
                if (component is IgHealthState health)
                    LastHealthState = health;
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
