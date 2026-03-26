using System;
using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.Map.Common.Dds;
using Bagira.SimHost.Network.Egress;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using Xunit;

namespace Bagira.SimHost.Tests
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

        public DamageAssessedEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<DamageAssessedEvent>();
        }

        public void Dispose() => _world.Dispose();

        private (DamageAssessedEgressTranslator translator, CapturingWriter<EntityHitDamage> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<EntityHitDamage>();
            var translator = new DamageAssessedEgressTranslator(writer);
            return (translator, writer);
        }

        private void PublishEvent(long hitEntityId, float totalDamage)
        {
            _world.Bus.Publish(new DamageAssessedEvent
            {
                HitEntityId = hitEntityId,
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

            PublishEvent(hitEntityId: 7L, totalDamage: 25.5f);

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
    }
}
