using System;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EntityHitDamageIngressTranslator"/> (BS1-T014).
    /// </summary>
    public class EntityHitDamageIngressTranslatorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public EntityHitDamageIngressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<DamageAssessedEvent>();

            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        private EntityHitDamageIngressTranslator BuildTranslator()
            => new EntityHitDamageIngressTranslator(participant: null, _entityMap);

        private ReadOnlySpan<DamageAssessedEvent> ProcessAndFlush(
            EntityHitDamageIngressTranslator translator,
            in EntityHitDamage msg)
        {
            var view = (ISimulationView)_world;
            var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();

            translator.ProcessSample(in msg, cmd, view);
            cmd.Playback(_world);
            _world.Bus.SwapBuffers();

            return _world.Bus.Consume<DamageAssessedEvent>();
        }

        // ── SC-1: DDS message → DamageAssessedEvent ───────────────────────────

        /// <summary>
        /// BS1-T014: A valid <see cref="EntityHitDamage"/> for a known entity must produce
        /// one <see cref="DamageAssessedEvent"/> with matching fields.
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesDamageAssessedEvent_WhenTargetKnown()
        {
            var translator = BuildTranslator();

            var entity = _world.CreateEntity();
            _entityMap.Register(7L, entity);

            var msg = new EntityHitDamage
            {
                HitEntityId = 7L,
                TotalDamage = 30f,
            };

            var events = ProcessAndFlush(translator, in msg);

            Assert.Equal(1, events.Length);
            Assert.Equal(entity, events[0].HitEntity);
            Assert.Equal(30f, events[0].TotalDamage);
        }

        // ── SC-2: Unknown entity → skipped ────────────────────────────────────

        [Fact]
        public void ProcessSample_SkipsSample_WhenTargetUnknown()
        {
            var translator = BuildTranslator();

            var msg = new EntityHitDamage { HitEntityId = 9999L, TotalDamage = 30f };

            var ex = Record.Exception(() =>
            {
                var view = (ISimulationView)_world;
                var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();
                translator.ProcessSample(in msg, cmd, view);
            });
            Assert.Null(ex);

            _world.Bus.SwapBuffers();
            var events = _world.Bus.Consume<DamageAssessedEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
