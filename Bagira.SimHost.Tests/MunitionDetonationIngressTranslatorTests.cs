using System;
using Bagira.BDC.SSTM;
using Bagira.SimHost.Network.Ingress;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MunitionDetonationIngressTranslator"/> (BS1-T012).
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

        // ── SC-1: DDS message → DetonationNotification ────────────────────────

        /// <summary>
        /// BS1-T012 SC-1: A valid <see cref="MunitionDetonation"/> sample with a known
        /// target entity must produce one <see cref="DetonationNotification"/> on the
        /// local event bus with matching fields.
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesDetonationNotification_WhenTargetKnown()
        {
            var translator = BuildTranslator();

            var targetEntity = _world.CreateEntity();
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
            Assert.Equal(1L,  events[0].ShooterEntityId);
            Assert.Equal(5L,  events[0].HitEntityId);
            Assert.Equal(10f, events[0].HitX);
            Assert.Equal(20f, events[0].HitY);
            Assert.Equal(5f,  events[0].HitZ);
        }

        // ── SC-2: Unknown target → skipped ────────────────────────────────────

        /// <summary>
        /// BS1-T012: When the target entity ID is not in <see cref="NetworkEntityMap"/>,
        /// the sample must be silently skipped and no event published.
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
    }
}
