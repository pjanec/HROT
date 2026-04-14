using Hrot.NED.Descriptors;
using Hrot.IG.Components;
using Hrot.Map.Common.Replication.Ingress;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Core.Abstractions;
using Xunit;

namespace Hrot.IG.Tests
{
    public class MapEntitySymbolTranslatorTests
    {
        private const long KnownId = 10L;

        // Helper to to create a joined fixture with null participant (test mode).
        private static (EntityRepository repo, NetworkEntityMap entityMap, MapEntitySymbolIngressTranslator translator)
            CreateFixture(int mapGroupId = 5)
        {
            var repo = new EntityRepository();
            var entityMap = new NetworkEntityMap();
            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new MapEntitySymbolIngressTranslator(null, entityMap, mapGroupId, ghostCreationSystem, localNodeId: 0);
            return (repo, entityMap, translator);
        }

        [Fact]
        public void Decode_GlobalOverride_SetsIgSymbolOverride()
        {
            var (repo, entityMap, translator) = CreateFixture();
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var cmd = new RecordingCommandBuffer();

            translator.ProcessSample(new MapEntitySymbol
            {
                EntityId   = (int)KnownId,
                MapGroupId = 0,
                StyleSetId = "hostile"
            }, cmd, null);

            Assert.True(cmd.SetManagedComponentCalled);
            Assert.NotNull(cmd.LastOverride);
            Assert.Equal("hostile", cmd.LastOverride!.StyleSetId);
        }

        [Fact]
        public void Decode_ScopedOverrideMatchingGroup_SetsIgSymbolOverride()
        {
            var (repo, entityMap, translator) = CreateFixture(mapGroupId: 5);
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var cmd = new RecordingCommandBuffer();

            translator.ProcessSample(new MapEntitySymbol
            {
                EntityId   = (int)KnownId,
                MapGroupId = 5,
                StyleSetId = "friendly"
            }, cmd, null);

            Assert.True(cmd.SetManagedComponentCalled);
            Assert.NotNull(cmd.LastOverride);
            Assert.Equal("friendly", cmd.LastOverride!.StyleSetId);
        }

        [Fact]
        public void Decode_ScopedOverrideWrongGroup_IsIgnored()
        {
            var (repo, entityMap, translator) = CreateFixture(mapGroupId: 5);
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var cmd = new RecordingCommandBuffer();

            translator.ProcessSample(new MapEntitySymbol
            {
                EntityId   = (int)KnownId,
                MapGroupId = 7,
                StyleSetId = "neutral"
            }, cmd, null);

            Assert.False(cmd.SetManagedComponentCalled);
        }

        [Fact]
        public void Decode_UnknownEntity_WithNullRepo_IsSkipped()
        {
            var (_, _, translator) = CreateFixture();
            var cmd = new RecordingCommandBuffer();

            // Passing null repo means ghost creation is impossible → skipped with a warning.
            translator.ProcessSample(new MapEntitySymbol
            {
                EntityId   = 99,
                MapGroupId = 0,
                StyleSetId = "hostile"
            }, cmd, null);

            Assert.False(cmd.SetManagedComponentCalled,
                "SetManagedComponent must not be called when entity is unknown and repo is null");
        }

        private sealed class RecordingCommandBuffer : IEntityCommandBuffer
        {
            public bool SetManagedComponentCalled { get; private set; }
            public IgSymbolOverride? LastOverride { get; private set; }

            public Entity CreateEntity() => new Entity();
            public void DestroyEntity(Entity entity) { }
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged { }
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged { }
            public void RemoveComponent<T>(Entity entity) where T : unmanaged { }
            public void AddManagedComponent<T>(Entity entity, T? component) where T : class { }
            public void SetManagedComponent<T>(Entity entity, T? component) where T : class
            {
                SetManagedComponentCalled = true;
                if (component is IgSymbolOverride overrideData)
                    LastOverride = overrideData;
            }
            public void RemoveManagedComponent<T>(Entity entity) where T : class { }
            public void PublishEvent<T>(in T evt) where T : unmanaged { }
            public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size) { }
            public void SetManagedComponentRaw(Entity entity, int typeId, object obj) { }
            public void SetLifecycleState(Entity entity, EntityLifecycle state) { }
        }
    }
}
