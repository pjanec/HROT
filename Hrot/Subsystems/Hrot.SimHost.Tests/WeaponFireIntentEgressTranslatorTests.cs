using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.Network.NED.SimHost;
using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WeaponFireIntentEgressTranslator"/> (BS1-T005 / PACK-P003).
    ///
    /// PACK-P003: <see cref="WeaponFireIntent"/> carries local ECS <see cref="Entity"/>
    /// handles; the translator resolves them to network IDs via <see cref="NetworkEntityMap"/>
    /// before writing the DDS wire message.
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

        private Entity SpawnTarget(long netId)
        {
            var entity = _world.CreateEntity();
            _entityMap.Register(netId, entity);
            return entity;
        }

        private void PublishIntent(Entity shooter, Entity target, int weaponIndex = 0)
        {
            _world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = shooter,
                Target      = target,
                WeaponIndex = weaponIndex,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Intent → DDS message ────────────────────────────────────────

        /// <summary>
        /// BS1-T005 SC-1 / PACK-P003: When a <see cref="WeaponFireIntent"/> is on the bus
        /// and the local node has authority over the shooter entity,
        /// <see cref="ScanAndPublish"/> must write exactly one <see cref="WeaponFireRequest"/>
        /// with network IDs resolved from <see cref="NetworkEntityMap"/>.
        /// </summary>
        [Fact]
        public void ScanAndPublish_WritesWeaponFireRequest_WhenAuthoritative()
        {
            var (translator, writer) = BuildTranslator();

            var shooter = SpawnShooter(netId: 1L, authoritative: true);
            var target  = SpawnTarget(netId: 2L);
            PublishIntent(shooter, target, weaponIndex: 0);

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            var msg = writer.Written[0];
            Assert.Equal(1L, msg.ShooterEntityId);
            Assert.Equal(2L, msg.TargetEntityId);
            Assert.Equal(0,  msg.WeaponIndex);
        }

        /// <summary>⭐ M8 (D-I, R-169) — the LOADED round crosses the wire: the intent's <c>AmmoGuid</c> is written on the request, and
        /// the Muscle's ingress turns it back into the same intent field (the per-shot path's half of
        /// <c>M8_TheLoadedRound_ReachesTheMuscle</c>; the turret path rides <c>EntityWeaponTask.AmmoGuid</c>).</summary>
        [Fact]
        public void M8_TheLoadedRound_RoundTripsTheWire()
        {
            var (translator, writer) = BuildTranslator();
            var shooter = SpawnShooter(netId: 1L, authoritative: true);
            var target  = SpawnTarget(netId: 2L);
            _world.Bus.Publish(new WeaponFireIntent { Shooter = shooter, Target = target, AmmoGuid = 9003, RaisedAt = 12.5, BurnSeconds = 37.25f, RequestId = 77u });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            var msg = Assert.Single(writer.Written);
            Assert.Equal(9003L, msg.AmmoGuid);
            Assert.Equal(12.5, msg.RaisedAt);   // ⭐ CE-2180 (R-169 appended) — when the brain raised it crosses too
            Assert.Equal(37.25f, msg.BurnSeconds);   // ⭐ CE-3236 (B-14, VC-7) — a thrown-back canister's burn left
            Assert.Equal(77u, msg.RequestId);          // ⭐ Q94 A-1 (R-3018) — the brain's request id crosses

            var ingress = new WeaponFireRequestIngressTranslator(participant: null, _entityMap);
            var cmd = (EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetCommandBuffer();
            ingress.ProcessSample(in msg, cmd, _world);
            cmd.Playback(_world);
            _world.Bus.SwapBuffers();
            var back = Assert.Single(_world.Bus.Read<WeaponFireIntent>().ToArray());
            Assert.Equal(9003L, back.AmmoGuid);
            Assert.Equal(12.5, back.RaisedAt);
            Assert.Equal(37.25f, back.BurnSeconds);
            Assert.Equal(77u, back.RequestId);
            Assert.True(back.IsRemote);
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

            var shooter = SpawnShooter(netId: 1L, authoritative: false);
            var target  = SpawnTarget(netId: 2L);
            PublishIntent(shooter, target);

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

        // ── SC-4 (PACK-P003): Shooter not in NetworkEntityMap → no publish ────

        /// <summary>
        /// PACK-P003: When the shooter entity is not registered in
        /// <see cref="NetworkEntityMap"/>, the translator must skip the event silently
        /// (no DDS write, no exception).
        /// </summary>
        [Fact]
        public void ScanAndPublish_Skips_WhenShooterNotInMap()
        {
            var (translator, writer) = BuildTranslator();

            // Shooter entity has authority but is NOT in NetworkEntityMap.
            var unmappedShooter = _world.CreateEntity();
            _world.AddComponent(unmappedShooter, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            var target = SpawnTarget(netId: 2L);

            PublishIntent(unmappedShooter, target);

            var ex = Record.Exception(() => translator.ScanAndPublish(_world));
            Assert.Null(ex);
            Assert.Empty(writer.Written);
        }
    }
}
