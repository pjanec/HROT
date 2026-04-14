using System;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Core.Abstractions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MunitionDetonationIngressTranslator"/> (BS1-T012 / PACK-P003).
    ///
    /// PACK-P003: <see cref="DetonationNotification"/> now carries local ECS
    /// <see cref="Entity"/> handles. Tests verify that the translator resolves
    /// <c>long</c> network IDs from the DDS wire message to <see cref="Entity"/> handles
    /// via <see cref="NetworkEntityMap"/>.
    ///
    /// Tests use <see cref="MunitionDetonationIngressTranslator.ProcessSample"/> directly
    /// (internal visibility through <c>InternalsVisibleTo</c>) to verify DDS samples
    /// become <see cref="DetonationNotification"/> ECS events on the local bus.
    /// </summary>
    public class MunitionDetonationIngressTranslatorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public MunitionDetonationIngressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<DetonationNotification>();

            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private MunitionDetonationIngressTranslator BuildTranslator()
            => new MunitionDetonationIngressTranslator(participant: null, _entityMap);

        private ReadOnlySpan<DetonationNotification> ProcessAndFlush(
            MunitionDetonationIngressTranslator translator,
            in MunitionDetonation msg)
        {
            var view = (ISimulationView)_world;
            var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();

            translator.ProcessSample(in msg, cmd, view);
            cmd.Playback(_world);
            _world.Bus.SwapBuffers();

            return _world.Bus.Consume<DetonationNotification>();
        }

        // ── SC-1: DDS message → DetonationNotification with Entity handles ────

        /// <summary>
        /// PACK-P003 SC-1 / BS1-T012 SC-1: A valid <see cref="MunitionDetonation"/> sample
        /// with a known target entity must produce one <see cref="DetonationNotification"/>
        /// on the local event bus with ECS <see cref="Entity"/> handles resolved from the map.
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesDetonationNotification_WhenTargetKnown()
        {
            var translator = BuildTranslator();

            var shooterEntity = _world.CreateEntity();
            var targetEntity  = _world.CreateEntity();
            _entityMap.Register(1L, shooterEntity);
            _entityMap.Register(5L, targetEntity);

            var msg = new MunitionDetonation
            {
                ShooterEntityId = 1L,
                HitEntityId     = 5L,
                HitX            = 10f,
                HitY            = 20f,
                HitZ            = 5f,
            };

            var events = ProcessAndFlush(translator, in msg);

            Assert.Equal(1, events.Length);
            // PACK-P003: assert Entity handles, not raw long IDs.
            Assert.Equal(shooterEntity, events[0].Shooter);
            Assert.Equal(targetEntity,  events[0].Target);
            Assert.Equal(10f, events[0].HitX);
            Assert.Equal(20f, events[0].HitY);
            Assert.Equal(5f,  events[0].HitZ);
        }

        // ── SC-2: Unknown target → skipped ────────────────────────────────────

        /// <summary>
        /// PACK-P003 / BS1-T012: When the target entity ID is not in
        /// <see cref="NetworkEntityMap"/>, the sample must be silently skipped and no
        /// event published.
        /// </summary>
        [Fact]
        public void ProcessSample_SkipsSample_WhenTargetUnknown()
        {
            var translator = BuildTranslator();

            var msg = new MunitionDetonation
            {
                ShooterEntityId = 1L,
                HitEntityId     = 9999L,
            };

            var ex = Record.Exception(() =>
            {
                var view = (ISimulationView)_world;
                var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();
                translator.ProcessSample(in msg, cmd, view);
            });
            Assert.Null(ex);

            _world.Bus.SwapBuffers();
            var events = _world.Bus.Consume<DetonationNotification>();
            Assert.Equal(0, events.Length);
        }

        // ── SC-3: Unknown shooter → emitted with Entity.Null shooter ─────────

        /// <summary>
        /// PACK-P003: When the shooter entity ID is not in <see cref="NetworkEntityMap"/>
        /// but the target IS known, the event is still published with
        /// <c>Shooter == Entity.Null</c> (default).
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesWithNullShooter_WhenShooterUnknown()
        {
            var translator    = BuildTranslator();
            var targetEntity  = _world.CreateEntity();
            _entityMap.Register(5L, targetEntity);

            // Shooter (1L) is NOT registered.
            var msg = new MunitionDetonation
            {
                ShooterEntityId = 1L,
                HitEntityId     = 5L,
                HitX            = 1f,
            };

            var events = ProcessAndFlush(translator, in msg);

            Assert.Equal(1, events.Length);
            Assert.Equal(targetEntity,  events[0].Target);
            // Shooter not in map → Entity.Null (default struct).
            Assert.Equal(default(Entity), events[0].Shooter);
        }
    }
}
