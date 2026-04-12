using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Dds;
using Hrot.Network.NED.SimHost;
using Fdp.Kernel;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Replication.Services;
using System.Numerics;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AudioTargetDetectedEgressTranslator"/> (PACK-A001).
    /// </summary>
    public class AudioTargetDetectedEgressTranslatorTests : IDisposable
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public AudioTargetDetectedEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<TargetHeardEvent>();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        private (AudioTargetDetectedEgressTranslator translator, CapturingWriter<AudioTargetDetected> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<AudioTargetDetected>();
            var translator = new AudioTargetDetectedEgressTranslator(writer, _entityMap);
            return (translator, writer);
        }

        private Entity RegisterEntity(long netId)
        {
            var entity = _world.CreateEntity();
            _entityMap.Register(netId, entity);
            return entity;
        }

        private void PublishEvent(Entity listener, int sourceIndex, Vector3 origin)
        {
            _world.Bus.Publish(new TargetHeardEvent
            {
                Listener          = listener,
                SourceEntityIndex = sourceIndex,
                Origin            = origin,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Event → DDS message ─────────────────────────────────────────

        /// <summary>
        /// PACK-A001 SC-1: A <see cref="TargetHeardEvent"/> must be forwarded as an
        /// <see cref="AudioTargetDetected"/> DDS message with matching fields.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesAudioTargetDetected_ForSingleEvent()
        {
            var (translator, writer) = BuildTranslator();
            var listener = RegisterEntity(netId: 5L);

            PublishEvent(listener, sourceIndex: 42, origin: new Vector3(10f, 20f, 30f));

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            Assert.Equal(5L,  writer.Written[0].ListenerEntityId);
            Assert.Equal(42,  writer.Written[0].SourceEntityIndex);
            Assert.Equal(10f, writer.Written[0].OriginX);
            Assert.Equal(20f, writer.Written[0].OriginY);
            Assert.Equal(30f, writer.Written[0].OriginZ);
        }

        // ── SC-2: Zero events → no write ─────────────────────────────────────

        /// <summary>
        /// PACK-A001 SC-2: When no <see cref="TargetHeardEvent"/> events are on the bus,
        /// the DDS writer must not be called.
        /// </summary>
        [Fact]
        public void ScanAndPublish_DoesNotWrite_WhenNoEvents()
        {
            var (translator, writer) = BuildTranslator();
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }

        // ── SC-3: Unmapped listener → skip ────────────────────────────────────

        /// <summary>
        /// PACK-A001 SC-3: A <see cref="TargetHeardEvent"/> whose listener entity is not
        /// present in the <see cref="NetworkEntityMap"/> must be silently skipped.
        /// </summary>
        [Fact]
        public void ScanAndPublish_SkipsEvent_WhenListenerNotMapped()
        {
            var (translator, writer) = BuildTranslator();

            // Create an entity but do NOT register it in the entity map.
            var unmappedListener = _world.CreateEntity();
            PublishEvent(unmappedListener, sourceIndex: 1, origin: Vector3.Zero);

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }
    }
}
