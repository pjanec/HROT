using Hrot.NED.Messages;
using Hrot.Network.NED.IG;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
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
            _world.RegisterEvent<IgWeaponFireEvent>();

            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private WeaponFireIngressTranslator BuildTranslator()
            => new WeaponFireIngressTranslator(participant: null, _entityMap);

        /// <summary>
        /// Calls <see cref="WeaponFireIngressTranslator.ProcessSample"/>, plays back the
        /// command buffer, and swaps event buffers so <see cref="IgWeaponFireEvent"/> events
        /// are visible to <see cref="FdpEventBus.Consume{T}"/>.
        /// </summary>
        private ReadOnlySpan<IgWeaponFireEvent> ProcessAndFlush(
            WeaponFireIngressTranslator translator,
            in WeaponFire msg)
        {
            var view = (ISimulationView)_world;
            var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();

            translator.ProcessSample(in msg, cmd);
            cmd.Playback(_world);
            _world.Bus.SwapBuffers();

            return _world.Bus.Consume<IgWeaponFireEvent>();
        }

        // ── SC-1: DDS message → one IgWeaponFireEvent ─────────────────────────

        /// <summary>
        /// BS1-T009 SC-1: A <see cref="WeaponFire"/> DDS message with both entities known
        /// in the map must produce exactly one <see cref="IgWeaponFireEvent"/> with matching data.
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesIgWeaponFireEvent_WhenEntitiesKnown()
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
            Assert.Equal(1L, events[0].ShooterEntityId);
            Assert.Equal(2L, events[0].TargetEntityId);
            Assert.Equal(0,  events[0].WeaponIndex);
        }

        // ── SC-2: Unknown entity → still publish event ────────────────────────

        /// <summary>
        /// BS1-T009 SC-2: When the shooter entity ID is not registered in
        /// <see cref="NetworkEntityMap"/> the translator must still publish
        /// <see cref="IgWeaponFireEvent"/> and must not throw.
        /// </summary>
        [Fact]
        public void ProcessSample_StillPublishesEvent_WhenShooterEntityUnknown()
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
                Assert.Equal(1, events.Length);
                Assert.Equal(5L, events[0].ShooterEntityId);
                Assert.Equal(1,  events[0].WeaponIndex);
            });

            Assert.Null(ex);
        }

        // ── Edge: Both entities unknown → still publish ───────────────────────

        /// <summary>
        /// When neither entity is known, the event is still published with the raw IDs.
        /// </summary>
        [Fact]
        public void ProcessSample_StillPublishesEvent_WhenBothEntitiesUnknown()
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
            Assert.Equal(99L,  events[0].ShooterEntityId);
            Assert.Equal(100L, events[0].TargetEntityId);
        }
    }
}
