using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.Network.NED.SimHost;
using Fdp.Kernel;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WeaponFireNotificationEgressTranslator"/> (BS1-T008 / PACK-P003).
    ///
    /// PACK-P003: <see cref="WeaponFireNotification"/> now carries ECS <see cref="Entity"/>
    /// handles instead of <c>long</c> network IDs. The translator resolves Entity → network ID
    /// via <see cref="NetworkEntityMap"/> before writing the DDS wire message.
    ///
    /// Uses a <see cref="CapturingWriter{T}"/> stub so the tests run without a live
    /// DDS participant.
    /// </summary>
    public class WeaponFireNotificationEgressTranslatorTests : IDisposable
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

        public WeaponFireNotificationEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<WeaponFireNotification>();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private (WeaponFireNotificationEgressTranslator translator, CapturingWriter<WeaponFire> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<WeaponFire>();
            var translator = new WeaponFireNotificationEgressTranslator(writer, _entityMap);
            return (translator, writer);
        }

        /// <summary>Creates a world entity and registers it in the map with the given network ID.</summary>
        private Entity SpawnEntity(long networkId)
        {
            var entity = _world.CreateEntity();
            _entityMap.Register(networkId, entity);
            return entity;
        }

        private void PublishNotification(Entity shooter, Entity target, int weaponIndex = 0)
        {
            _world.Bus.Publish(new WeaponFireNotification
            {
                Shooter     = shooter,
                Target      = target,
                WeaponIndex = weaponIndex,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Notification → DDS message with resolved network IDs ────────

        /// <summary>
        /// PACK-P003 / BS1-T008 SC-1: A <see cref="WeaponFireNotification"/> on the bus
        /// must produce exactly one <see cref="WeaponFire"/> DDS message with network IDs
        /// resolved from <see cref="NetworkEntityMap"/>.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesWeaponFire_WithResolvedNetworkIds()
        {
            var (translator, writer) = BuildTranslator();

            var shooter = SpawnEntity(networkId: 1L);
            var target  = SpawnEntity(networkId: 2L);

            PublishNotification(shooter, target, weaponIndex: 0);

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            var msg = writer.Written[0];
            Assert.Equal(1L, msg.ShooterEntityId);
            Assert.Equal(2L, msg.TargetEntityId);
            Assert.Equal(0,  msg.WeaponIndex);
        }

        // ── SC-2: Multiple notifications → multiple DDS writes ────────────────

        /// <summary>
        /// PACK-P003 / BS1-T008 SC-2: Three <see cref="WeaponFireNotification"/> events
        /// in one frame must result in exactly three <see cref="IDdsWriter{T}.Write"/> calls.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesOnce_PerNotification()
        {
            var (translator, writer) = BuildTranslator();

            var s1 = SpawnEntity(1L); var t1 = SpawnEntity(2L);
            var s2 = SpawnEntity(3L); var t2 = SpawnEntity(4L);
            var s3 = SpawnEntity(5L); var t3 = SpawnEntity(6L);

            _world.Bus.Publish(new WeaponFireNotification { Shooter = s1, Target = t1, WeaponIndex = 0 });
            _world.Bus.Publish(new WeaponFireNotification { Shooter = s2, Target = t2, WeaponIndex = 1 });
            _world.Bus.Publish(new WeaponFireNotification { Shooter = s3, Target = t3, WeaponIndex = 0 });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(3, writer.Written.Count);
        }

        // ── SC-3: Empty bus → no write ────────────────────────────────────────

        /// <summary>
        /// When no events are on the bus, <see cref="ScanAndPublish"/> must not call
        /// <see cref="IDdsWriter{T}.Write"/>.
        /// </summary>
        [Fact]
        public void ScanAndPublish_DoesNotWrite_WhenBusEmpty()
        {
            var (translator, writer) = BuildTranslator();

            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }

        // ── SC-4: Unmapped shooter → skipped ─────────────────────────────────

        /// <summary>
        /// PACK-P003: When the shooter <see cref="Entity"/> is not in
        /// <see cref="NetworkEntityMap"/>, the event must be silently skipped
        /// (no DDS write).
        /// </summary>
        [Fact]
        public void ScanAndPublish_SkipsEvent_WhenShooterNotInMap()
        {
            var (translator, writer) = BuildTranslator();

            // Only target is mapped.
            var unmappedShooter = _world.CreateEntity();
            var target          = SpawnEntity(networkId: 2L);

            PublishNotification(unmappedShooter, target);

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }

        // ── SC-5: Unmapped target → skipped ──────────────────────────────────

        /// <summary>
        /// PACK-P003: When the target <see cref="Entity"/> is not in
        /// <see cref="NetworkEntityMap"/>, the event must be silently skipped
        /// (no DDS write).
        /// </summary>
        [Fact]
        public void ScanAndPublish_SkipsEvent_WhenTargetNotInMap()
        {
            var (translator, writer) = BuildTranslator();

            var shooter       = SpawnEntity(networkId: 1L);
            var unmappedTarget = _world.CreateEntity();

            PublishNotification(shooter, unmappedTarget);

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }
    }
}
