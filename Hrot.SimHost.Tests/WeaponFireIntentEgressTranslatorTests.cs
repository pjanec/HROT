using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.SimHost.Network.Egress;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WeaponFireIntentEgressTranslator"/> (BS1-T005).
    ///
    /// Uses a <see cref="CapturingWriter{T}"/> stub so the tests run without a live
    /// DDS participant.  Authority behaviour is verified via the
    /// <see cref="NetworkAuthority"/> component.
    /// </summary>
    public class WeaponFireIntentEgressTranslatorTests : IDisposable
    {
        // ── Test infrastructure ───────────────────────────────────────────────

        /// <summary>Records every <see cref="Write"/> call for assertion.</summary>
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public WeaponFireIntentEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterEvent<WeaponFireIntent>();

            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // ── Helper ────────────────────────────────────────────────────────────

        private (WeaponFireIntentEgressTranslator translator, CapturingWriter<WeaponFireRequest> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<WeaponFireRequest>();
            var translator = new WeaponFireIntentEgressTranslator(writer, _entityMap);
            return (translator, writer);
        }

        private Entity SpawnShooter(long netId, bool authoritative = true)
        {
            var entity = _world.CreateEntity();
            if (authoritative)
            {
                // Locally owned: PrimaryOwnerId == LocalNodeId.
                _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            }
            else
            {
                // Remotely owned: PrimaryOwnerId != LocalNodeId.
                _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            }
            _entityMap.Register(netId, entity);
            return entity;
        }

        private void PublishIntent(long shooterId, long targetId, int weaponIndex = 0)
        {
            _world.Bus.Publish(new WeaponFireIntent
            {
                ShooterEntityId = shooterId,
                TargetEntityId  = targetId,
                WeaponIndex     = weaponIndex,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Intent → DDS message ────────────────────────────────────────

        /// <summary>
        /// BS1-T005 SC-1: When a <see cref="WeaponFireIntent"/> is on the bus and the
        /// local node has authority over the shooter entity, <see cref="ScanAndPublish"/>
        /// must write exactly one <see cref="WeaponFireRequest"/> with matching payload.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesWeaponFireRequest_WhenAuthoritative()
        {
            var (translator, writer) = BuildTranslator();

            SpawnShooter(netId: 1L, authoritative: true);
            PublishIntent(shooterId: 1L, targetId: 2L, weaponIndex: 0);

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            var msg = writer.Written[0];
            Assert.Equal(1L, msg.ShooterEntityId);
            Assert.Equal(2L, msg.TargetEntityId);
            Assert.Equal(0,  msg.WeaponIndex);
        }

        // ── SC-2: No authority → no publish ──────────────────────────────────

        /// <summary>
        /// BS1-T005 SC-2: When the local node does not have authority for the shooter
        /// entity, <see cref="ScanAndPublish"/> must not call <see cref="IDdsWriter{T}.Write"/>.
        /// </summary>
        [Fact]
        public void ScanAndPublish_DoesNotWrite_WhenNotAuthoritative()
        {
            var (translator, writer) = BuildTranslator();

            SpawnShooter(netId: 1L, authoritative: false);
            PublishIntent(shooterId: 1L, targetId: 2L);

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }

        // ── SC-3: Empty bus → no publish ──────────────────────────────────────

        /// <summary>
        /// BS1-T005 SC-3: When no <see cref="WeaponFireIntent"/> events are on the bus,
        /// <see cref="ScanAndPublish"/> must not call <see cref="IDdsWriter{T}.Write"/>.
        /// </summary>
        [Fact]
        public void ScanAndPublish_DoesNotWrite_WhenBusEmpty()
        {
            var (translator, writer) = BuildTranslator();

            // No events published — just swap buffers to ensure clean state.
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }

        // ── SC-4: Unknown shooter ID → no publish ─────────────────────────────

        /// <summary>
        /// When the shooter ID in <see cref="WeaponFireIntent"/> is not registered in
        /// <see cref="NetworkEntityMap"/>, the translator must skip the event silently.
        /// </summary>
        [Fact]
        public void ScanAndPublish_SkipsEvent_WhenShooterIdUnknown()
        {
            var (translator, writer) = BuildTranslator();

            // ShooterEntityId=99 is not in the map.
            PublishIntent(shooterId: 99L, targetId: 2L);

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }
    }
}
