using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public class TacticalIntentEgressTranslatorTests : IDisposable
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public TacticalIntentEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<DoctrineState>();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        private (TacticalIntentEgressTranslator translator, CapturingWriter<TacticalIntentRequest> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<TacticalIntentRequest>();
            var translator = new TacticalIntentEgressTranslator(writer, _entityMap);
            return (translator, writer);
        }

        // SC-1: Entity in map, no DoctrineState authority -> DDS write happens
        [Fact]
        public void ScanAndPublish_NoAuthority_WritesDdsSample()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new DoctrineState());
            // Authority NOT set (remote entity)
            _entityMap.Register(42L, entity);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            Assert.Equal(42L, writer.Written[0].TargetEntityId);
            Assert.Equal("DefendArea", writer.Written[0].IntentId);
            Assert.Equal(1, translator.SentSampleCount);
        }

        // SC-2: Entity NOT in NetworkEntityMap -> no DDS write
        [Fact]
        public void ScanAndPublish_EntityNotInMap_NoDdsWrite()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new DoctrineState());
            // Entity not registered in entityMap

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
            Assert.Equal(0, translator.SentSampleCount);
        }

        // SC-3: Two events, no authority for either -> two DDS writes
        [Fact]
        public void ScanAndPublish_TwoEvents_NoAuthority_TwoWrites()
        {
            var (translator, writer) = BuildTranslator();

            var e1 = _world.CreateEntity();
            _world.AddComponent(e1, new DoctrineState());
            _entityMap.Register(1L, e1);

            var e2 = _world.CreateEntity();
            _world.AddComponent(e2, new DoctrineState());
            _entityMap.Register(2L, e2);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = e1, IntentId = "DefendArea", JsonParams = "{}" });
            _world.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = e2, IntentId = "ConvoyEscort", JsonParams = "{}" });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(2, writer.Written.Count);
            Assert.Equal(2, translator.SentSampleCount);
        }

        // SC-4: Entity HAS DoctrineState authority -> no DDS write
        [Fact]
        public void ScanAndPublish_HasAuthority_NoDdsWrite()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new DoctrineState());
            _world.SetAuthority<DoctrineState>(entity, true);  // locally owned
            _entityMap.Register(99L, entity);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
            Assert.Equal(0, translator.SentSampleCount);
        }
    }
}
