using System;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
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

            return _world.Bus.Read<DamageAssessedEvent>();
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

        /// <summary>⭐ <c>CE-3210</c> — the wire's id + 1 becomes the part; a sender that never set it (0) means no part, never ThighL.</summary>
        [Fact]
        public void CE3210_ThePartArrives_AndAnUnsetFieldMeansNoPart()
        {
            var translator = BuildTranslator();
            var entity = _world.CreateEntity();
            _entityMap.Register(7L, entity);

            var withPart = new EntityHitDamage { HitEntityId = 7L, TotalDamage = 5f, BodyPart = 6 /* Head + 1 */ };
            var e = ProcessAndFlush(translator, in withPart);
            Assert.Equal(((byte)1, (byte)5), (e[0].HasBodyPart, e[0].BodyPart));

            var none = new EntityHitDamage { HitEntityId = 7L, TotalDamage = 5f };
            e = ProcessAndFlush(translator, in none);
            Assert.Equal((byte)0, e[0].HasBodyPart);
        }

        /// <summary>⭐ <c>CE-3219</c> (civilians B-1, CV-6) — the hit's effect kind and duration cross the wire unchanged; an unset field is
        /// Kinetic (damage only), so every sender from before reads as before.</summary>
        [Fact]
        public void CE3219_TheEffectArrives_AndAnUnsetFieldMeansKinetic()
        {
            var translator = BuildTranslator();
            var entity = _world.CreateEntity();
            _entityMap.Register(8L, entity);

            var blunt = new EntityHitDamage { HitEntityId = 8L, TotalDamage = 2f, EffectKind = (byte)Fdp.Toolkit.Combat.HitEffect.Blunt, EffectSeconds = 4f };
            var e = ProcessAndFlush(translator, in blunt);
            Assert.Equal((Fdp.Toolkit.Combat.HitEffect.Blunt, 4f), (e[0].Effect, e[0].EffectSeconds));

            var plain = new EntityHitDamage { HitEntityId = 8L, TotalDamage = 2f };
            e = ProcessAndFlush(translator, in plain);
            Assert.Equal(Fdp.Toolkit.Combat.HitEffect.Kinetic, e[0].Effect);
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
            var events = _world.Bus.Read<DamageAssessedEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
