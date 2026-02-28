using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using Bagira.IG.Translators;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.IG.Tests
{
    public class MapEntitySymbolTranslatorTests
    {
        private const long KnownId = 10L;

        [Fact]
        public void Decode_GlobalOverride_SetsIgSymbolOverride()
        {
            var entityMap = new NetworkEntityMap();
            var repo = new EntityRepository();
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var translator = new MapEntitySymbolTranslator(null, entityMap, mapGroupId: 5);
            var cmd = new RecordingCommandBuffer();

            translator.ProcessSample(new MapEntitySymbol
            {
                EntityId = (int)KnownId,
                MapGroupId = 0,
                StyleSetId = "hostile"
            }, cmd);

            Assert.True(cmd.SetManagedComponentCalled);
            Assert.NotNull(cmd.LastOverride);
            Assert.Equal("hostile", cmd.LastOverride!.StyleSetId);
        }

        [Fact]
        public void Decode_ScopedOverrideMatchingGroup_SetsIgSymbolOverride()
        {
            var entityMap = new NetworkEntityMap();
            var repo = new EntityRepository();
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var translator = new MapEntitySymbolTranslator(null, entityMap, mapGroupId: 5);
            var cmd = new RecordingCommandBuffer();

            translator.ProcessSample(new MapEntitySymbol
            {
                EntityId = (int)KnownId,
                MapGroupId = 5,
                StyleSetId = "friendly"
            }, cmd);

            Assert.True(cmd.SetManagedComponentCalled);
            Assert.NotNull(cmd.LastOverride);
            Assert.Equal("friendly", cmd.LastOverride!.StyleSetId);
        }

        [Fact]
        public void Decode_ScopedOverrideWrongGroup_IsIgnored()
        {
            var entityMap = new NetworkEntityMap();
            var repo = new EntityRepository();
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var translator = new MapEntitySymbolTranslator(null, entityMap, mapGroupId: 5);
            var cmd = new RecordingCommandBuffer();

            translator.ProcessSample(new MapEntitySymbol
            {
                EntityId = (int)KnownId,
                MapGroupId = 7,
                StyleSetId = "neutral"
            }, cmd);

            Assert.False(cmd.SetManagedComponentCalled);
        }

        [Fact]
        public void Decode_UnknownEntity_IsSkipped()
        {
            var entityMap = new NetworkEntityMap();
            var translator = new MapEntitySymbolTranslator(null, entityMap, mapGroupId: 5);
            var cmd = new RecordingCommandBuffer();

            translator.ProcessSample(new MapEntitySymbol
            {
                EntityId = 99,
                MapGroupId = 0,
                StyleSetId = "hostile"
            }, cmd);

            Assert.False(cmd.SetManagedComponentCalled);
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
