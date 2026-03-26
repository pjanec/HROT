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
    /// Unit tests for <see cref="WeaponFireNotificationEgressTranslator"/> (BS1-T008).
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

        public WeaponFireNotificationEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<WeaponFireNotification>();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private (WeaponFireNotificationEgressTranslator translator, CapturingWriter<WeaponFire> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<WeaponFire>();
            var translator = new WeaponFireNotificationEgressTranslator(writer);
            return (translator, writer);
        }

        private void PublishNotification(long shooterId, long targetId, int weaponIndex = 0)
        {
            _world.Bus.Publish(new WeaponFireNotification
            {
                ShooterEntityId = shooterId,
                TargetEntityId  = targetId,
                WeaponIndex     = weaponIndex,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Notification → DDS message ─────────────────────────────────

        /// <summary>
        /// BS1-T008 SC-1: A <see cref="WeaponFireNotification"/> on the bus must produce
        /// exactly one <see cref="WeaponFire"/> DDS message with the matching payload.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesWeaponFire_ForSingleNotification()
        {
            var (translator, writer) = BuildTranslator();

            PublishNotification(shooterId: 1L, targetId: 2L, weaponIndex: 0);

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            var msg = writer.Written[0];
            Assert.Equal(1L, msg.ShooterEntityId);
            Assert.Equal(2L, msg.TargetEntityId);
            Assert.Equal(0,  msg.WeaponIndex);
        }

        // ── SC-2: Multiple notifications → multiple DDS writes ────────────────

        /// <summary>
        /// BS1-T008 SC-2: Three <see cref="WeaponFireNotification"/> events in one frame
        /// must result in exactly three <see cref="IDdsWriter{T}.Write"/> calls.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesOnce_PerNotification()
        {
            var (translator, writer) = BuildTranslator();

            _world.Bus.Publish(new WeaponFireNotification { ShooterEntityId = 1L, TargetEntityId = 2L, WeaponIndex = 0 });
            _world.Bus.Publish(new WeaponFireNotification { ShooterEntityId = 3L, TargetEntityId = 4L, WeaponIndex = 1 });
            _world.Bus.Publish(new WeaponFireNotification { ShooterEntityId = 5L, TargetEntityId = 6L, WeaponIndex = 0 });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(3, writer.Written.Count);
        }

        // ── Edge: Empty bus → no write ────────────────────────────────────────

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
    }
}
