using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.Network.NED.SimHost;
using Fdp.Kernel;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MunitionDetonationEgressTranslator"/> (BS1-T011 / PACK-P003).
    ///
    /// PACK-P003: <see cref="DetonationNotification"/> now carries local ECS
    /// <see cref="Entity"/> handles. The translator resolves them to network IDs via
    /// <see cref="NetworkEntityMap"/> before writing the DDS wire message.
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
        private readonly NetworkEntityMap _entityMap;

        public MunitionDetonationEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<DetonationNotification>();

            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private (MunitionDetonationEgressTranslator translator, CapturingWriter<MunitionDetonation> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<MunitionDetonation>();
            var translator = new MunitionDetonationEgressTranslator(writer, _entityMap);
            return (translator, writer);
        }

        private Entity RegisterEntity(long netId)
        {
            var entity = _world.CreateEntity();
            _entityMap.Register(netId, entity);
            return entity;
        }

        private void PublishDetonation(Entity shooter, Entity target,
            float hitX = 1f, float hitY = 2f, float hitZ = 3f)
        {
            _world.Bus.Publish(new DetonationNotification
            {
                Shooter = shooter,
                Target  = target,
                HitX    = hitX,
                HitY    = hitY,
                HitZ    = hitZ,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Event → DDS message with resolved network IDs ───────────────

        /// <summary>
        /// PACK-P003 SC-1 / BS1-T011 SC-1: A single <see cref="DetonationNotification"/>
        /// must be forwarded as a <see cref="MunitionDetonation"/> DDS message with network
        /// IDs resolved via <see cref="NetworkEntityMap"/>.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesMunitionDetonation_ForSingleEvent()
        {
            var (translator, writer) = BuildTranslator();

            var shooterEntity = RegisterEntity(1L);
            var hitEntity     = RegisterEntity(3L);

            PublishDetonation(shooterEntity, hitEntity, hitX: 10f, hitY: 20f, hitZ: 5f);

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
        /// PACK-P003 SC-2: Three <see cref="DetonationNotification"/> events in one frame
        /// (all with mapped entities) must produce exactly three <see cref="MunitionDetonation"/>
        /// DDS writes.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesMultipleTimes_WhenMultipleDetonations()
        {
            var (translator, writer) = BuildTranslator();

            var shooter = RegisterEntity(1L);
            var t1      = RegisterEntity(2L);
            var t2      = RegisterEntity(3L);
            var t3      = RegisterEntity(4L);

            _world.Bus.Publish(new DetonationNotification { Shooter = shooter, Target = t1 });
            _world.Bus.Publish(new DetonationNotification { Shooter = shooter, Target = t2 });
            _world.Bus.Publish(new DetonationNotification { Shooter = shooter, Target = t3 });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(3, writer.Written.Count);
        }

        // ── SC-3: Empty bus → no write ────────────────────────────────────────

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

            Assert.Empty(writer.Written);
        }

        // ── SC-4 (PACK-P003): Unknown shooter → skip, no throw ────────────────

        /// <summary>
        /// PACK-P003 SC-4: When the Shooter entity is not in <see cref="NetworkEntityMap"/>
        /// the event must be silently skipped and no DDS message produced.
        /// </summary>
        [Fact]
        public void ScanAndPublish_Skips_WhenShooterNotInMap()
        {
            var (translator, writer) = BuildTranslator();

            // Only target is registered; shooter is not.
            var hitEntity = RegisterEntity(3L);
            // Shooter is an entity not in the map:
            var unmappedShooter = _world.CreateEntity();

            var ex = Record.Exception(() =>
            {
                PublishDetonation(unmappedShooter, hitEntity);
                translator.ScanAndPublish(_world);
            });

            Assert.Null(ex);
            Assert.Empty(writer.Written);
        }

        // ── SC-5 (PACK-P003): Unknown target → skip, no throw ─────────────────

        /// <summary>
        /// PACK-P003 SC-5: When the Target entity is not in <see cref="NetworkEntityMap"/>
        /// the event must be silently skipped and no DDS message produced.
        /// </summary>
        [Fact]
        public void ScanAndPublish_Skips_WhenTargetNotInMap()
        {
            var (translator, writer) = BuildTranslator();

            var shooterEntity   = RegisterEntity(1L);
            var unmappedTarget  = _world.CreateEntity();  // not in map

            var ex = Record.Exception(() =>
            {
                PublishDetonation(shooterEntity, unmappedTarget);
                translator.ScanAndPublish(_world);
            });

            Assert.Null(ex);
            Assert.Empty(writer.Written);
        }
    }
}
