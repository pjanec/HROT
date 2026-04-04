using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.SimHost.Network.Egress;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DamageAssessedEgressTranslator"/> (BS1-T013).
    /// </summary>
    public class DamageAssessedEgressTranslatorTests : IDisposable
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public DamageAssessedEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<DamageAssessedEvent>();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        private (DamageAssessedEgressTranslator translator, CapturingWriter<EntityHitDamage> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<EntityHitDamage>();
            var translator = new DamageAssessedEgressTranslator(writer, _entityMap);
            return (translator, writer);
        }

        private Entity RegisterEntity(long netId)
        {
            var entity = _world.CreateEntity();
            _entityMap.Register(netId, entity);
            return entity;
        }

        private void PublishEvent(Entity hitEntity, float totalDamage)
        {
            _world.Bus.Publish(new DamageAssessedEvent
            {
                HitEntity   = hitEntity,
                TotalDamage = totalDamage,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Event → DDS message ─────────────────────────────────────────

        /// <summary>
        /// BS1-T013 SC-1: A <see cref="DamageAssessedEvent"/> must be forwarded as an
        /// <see cref="EntityHitDamage"/> DDS message with matching fields.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesEntityHitDamage_ForSingleEvent()
        {
            var (translator, writer) = BuildTranslator();
            var entity = RegisterEntity(netId: 7L);

            PublishEvent(hitEntity: entity, totalDamage: 25.5f);

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            Assert.Equal(7L,    writer.Written[0].HitEntityId);
            Assert.Equal(25.5f, writer.Written[0].TotalDamage);
        }

        // ── SC-2: Zero events → no write ─────────────────────────────────────

        /// <summary>
        /// BS1-T013 SC-2: When no <see cref="DamageAssessedEvent"/> events are on the bus,
        /// the DDS writer must not be called.
        /// </summary>
        [Fact]
        public void ScanAndPublish_DoesNotWrite_WhenNoEvents()
        {
            var (translator, writer) = BuildTranslator();

            _world.Bus.SwapBuffers();
            translator.ScanAndPublish(_world);

            Assert.Equal(0, writer.Written.Count);
        }

        // ── SC-3: Unknown entity → skipped ───────────────────────────────────

        /// <summary>
        /// When the entity is not in <see cref="NetworkEntityMap"/>, no DDS message is written.
        /// </summary>
        [Fact]
        public void ScanAndPublish_SkipsEvent_WhenEntityNotInMap()
        {
            var (translator, writer) = BuildTranslator();
            var unmapped = _world.CreateEntity(); // not registered in _entityMap

            PublishEvent(hitEntity: unmapped, totalDamage: 10f);
            translator.ScanAndPublish(_world);

            Assert.Equal(0, writer.Written.Count);
        }
    }
}
