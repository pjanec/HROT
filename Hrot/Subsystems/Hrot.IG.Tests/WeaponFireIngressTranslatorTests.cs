using Hrot.NED.Messages;
using Hrot.Network.NED.IG;
using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using System;
using Xunit;

namespace Hrot.IG.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WeaponFireIngressTranslator"/> (BS1-T009).
    ///
    /// Tests inject samples via <c>ProcessSample</c> (internal, exposed through
    /// <c>InternalsVisibleTo</c>) rather than requiring a live DDS participant.
    /// </summary>
    public class WeaponFireIngressTranslatorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public WeaponFireIngressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<WeaponFireNotification>();

            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private WeaponFireIngressTranslator BuildTranslator()
            => new WeaponFireIngressTranslator(participant: null, _entityMap);

        /// <summary>
        /// Calls <see cref="WeaponFireIngressTranslator.ProcessSample"/>, plays back the
        /// command buffer, and swaps event buffers so <see cref="WeaponFireNotification"/> events
        /// are visible to <see cref="FdpEventBus.Read{T}"/>.
        /// </summary>
        private ReadOnlySpan<WeaponFireNotification> ProcessAndFlush(
            WeaponFireIngressTranslator translator,
            in WeaponFire msg)
        {
            var view = (ISimulationView)_world;
            var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();

            translator.ProcessSample(in msg, cmd);
            cmd.Playback(_world);
            _world.Bus.SwapBuffers();

            return _world.Bus.Read<WeaponFireNotification>();
        }

        // ── SC-1: DDS message → one WeaponFireNotification ────────────────────

        /// <summary>
        /// BS1-T009 SC-1: A <see cref="WeaponFire"/> DDS message with both entities known
        /// in the map must produce exactly one <see cref="WeaponFireNotification"/> with
        /// resolved <see cref="Entity"/> handles.
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesWeaponFireNotification_WhenEntitiesKnown()
        {
            var translator = BuildTranslator();

            var entityA = _world.CreateEntity();
            var entityB = _world.CreateEntity();
            _entityMap.Register(1L, entityA);
            _entityMap.Register(2L, entityB);

            var msg = new WeaponFire
            {
                ShooterEntityId = 1L,
                TargetEntityId  = 2L,
                WeaponIndex     = 0,
            };

            var events = ProcessAndFlush(translator, in msg);

            Assert.Equal(1, events.Length);
            Assert.Equal(entityA, events[0].Shooter);
            Assert.Equal(entityB, events[0].Target);
            Assert.Equal(0,       events[0].WeaponIndex);
        }

        // ── SC-2: Unknown entity → publish with Entity.Null ───────────────────

        /// <summary>
        /// BS1-T009 SC-2: When the shooter entity ID is not registered in
        /// <see cref="NetworkEntityMap"/> the translator must still publish
        /// <see cref="WeaponFireNotification"/> with <see cref="Entity.Null"/> for the
        /// unknown handle, and must not throw.
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesNullShooter_WhenShooterEntityUnknown()
        {
            var translator = BuildTranslator();

            // Only target is known; shooter (id=5) is not in EntityMap.
            var entityB = _world.CreateEntity();
            _entityMap.Register(2L, entityB);

            var msg = new WeaponFire
            {
                ShooterEntityId = 5L,
                TargetEntityId  = 2L,
                WeaponIndex     = 1,
            };

            var ex = Record.Exception(() =>
            {
                var events = ProcessAndFlush(translator, in msg);
                Assert.Equal(1,           events.Length);
                Assert.Equal(Entity.Null, events[0].Shooter);
                Assert.Equal(entityB,     events[0].Target);
                Assert.Equal(1,           events[0].WeaponIndex);
            });

            Assert.Null(ex);
        }

        // ── Edge: Both entities unknown → still publish ───────────────────────

        /// <summary>
        /// When neither entity is known, the event is still published with the raw IDs.
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesNullHandles_WhenBothEntitiesUnknown()
        {
            var translator = BuildTranslator();

            var msg = new WeaponFire
            {
                ShooterEntityId = 99L,
                TargetEntityId  = 100L,
                WeaponIndex     = 0,
            };

            var events = ProcessAndFlush(translator, in msg);

            Assert.Equal(1, events.Length);
            Assert.Equal(Entity.Null, events[0].Shooter);
            Assert.Equal(Entity.Null, events[0].Target);
        }
    }
}
