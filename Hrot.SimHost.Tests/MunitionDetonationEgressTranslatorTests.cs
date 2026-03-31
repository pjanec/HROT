using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.SimHost.Network.Egress;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MunitionDetonationEgressTranslator"/> (BS1-T011).
    ///
    /// Uses a <see cref="CapturingWriter{T}"/> stub so the tests run without a live
    /// DDS participant.
    /// </summary>
    public class MunitionDetonationEgressTranslatorTests : IDisposable
    {
        // ── Test infrastructure ───────────────────────────────────────────────

        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _world;

        public MunitionDetonationEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<DetonationNotification>();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private (MunitionDetonationEgressTranslator translator, CapturingWriter<MunitionDetonation> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<MunitionDetonation>();
            var translator = new MunitionDetonationEgressTranslator(writer);
            return (translator, writer);
        }

        private void PublishDetonation(long shooterId, long hitEntityId,
            float hitX = 1f, float hitY = 2f, float hitZ = 3f)
        {
            _world.Bus.Publish(new DetonationNotification
            {
                ShooterEntityId = shooterId,
                HitEntityId     = hitEntityId,
                HitX            = hitX,
                HitY            = hitY,
                HitZ            = hitZ,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Event → DDS message ─────────────────────────────────────────

        /// <summary>
        /// BS1-T011 SC-1: A single <see cref="DetonationNotification"/> must be forwarded
        /// as a <see cref="MunitionDetonation"/> DDS message with matching fields.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesMunitionDetonation_ForSingleEvent()
        {
            var (translator, writer) = BuildTranslator();

            PublishDetonation(shooterId: 1L, hitEntityId: 3L, hitX: 10f, hitY: 20f, hitZ: 5f);

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            var msg = writer.Written[0];
            Assert.Equal(1L,  msg.ShooterEntityId);
            Assert.Equal(3L,  msg.HitEntityId);
            Assert.Equal(10f, msg.HitX);
            Assert.Equal(20f, msg.HitY);
            Assert.Equal(5f,  msg.HitZ);
        }

        // ── SC-2: Multiple detonations → multiple DDS writes ──────────────────

        /// <summary>
        /// BS1-T011 SC-2: Three <see cref="DetonationNotification"/> events in one frame
        /// must produce exactly three <see cref="MunitionDetonation"/> DDS writes.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesMultipleTimes_WhenMultipleDetonations()
        {
            var (translator, writer) = BuildTranslator();

            _world.Bus.Publish(new DetonationNotification { ShooterEntityId = 1L, HitEntityId = 2L });
            _world.Bus.Publish(new DetonationNotification { ShooterEntityId = 1L, HitEntityId = 3L });
            _world.Bus.Publish(new DetonationNotification { ShooterEntityId = 1L, HitEntityId = 4L });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(3, writer.Written.Count);
        }

        // ── Empty bus → no write ──────────────────────────────────────────────

        /// <summary>
        /// When no events are on the bus, <see cref="ScanAndPublish"/> must not call
        /// the DDS writer at all.
        /// </summary>
        [Fact]
        public void ScanAndPublish_DoesNotWrite_WhenNoEvents()
        {
            var (translator, writer) = BuildTranslator();

            // No events published; still swap to set up an empty consume window.
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(0, writer.Written.Count);
        }
    }
}
