using System;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WeaponFireRequestIngressTranslator"/> (BS1-T006).
    ///
    /// Tests use <see cref="ProcessSample"/> directly (internal visibility through
    /// <c>InternalsVisibleTo</c>) and a real <see cref="EntityCommandBuffer"/> to
    /// verify that decoded samples become <see cref="WeaponFireIntent"/> events on
    /// the ECS event bus.
    /// </summary>
    public class WeaponFireRequestIngressTranslatorTests : IDisposable
    {
        // ── Infrastructure ────────────────────────────────────────────────────

        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public WeaponFireRequestIngressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterEvent<WeaponFireIntent>();

            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private WeaponFireRequestIngressTranslator BuildTranslator()
            => new WeaponFireRequestIngressTranslator(participant: null, _entityMap);

        /// <summary>
        /// Calls <see cref="ProcessSample"/>, plays back the command buffer, and swaps
        /// event buffers so <see cref="WeaponFireIntent"/> events are visible to
        /// <see cref="FdpEventBus.Consume{T}"/>.
        /// </summary>
        private ReadOnlySpan<WeaponFireIntent> ProcessAndFlush(
            WeaponFireRequestIngressTranslator translator,
            in WeaponFireRequest request)
        {
            var view = (ISimulationView)_world;
            var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();

            translator.ProcessSample(in request, cmd, view);
            cmd.Playback(_world);
            _world.Bus.SwapBuffers();

            return _world.Bus.Consume<WeaponFireIntent>();
        }

        // ── SC-1: Valid sample → WeaponFireIntent on bus ───────────────────────

        /// <summary>
        /// BS1-T006 SC-1: A fully-mapped <see cref="WeaponFireRequest"/> must produce
        /// exactly one <see cref="WeaponFireIntent"/> on the local event bus with the
        /// same IDs.
        /// </summary>
        [Fact]
        public void ProcessSample_PublishesWeaponFireIntent_WhenBothEntitiesKnown()
        {
            var translator = BuildTranslator();

            var entityA = _world.CreateEntity();
            var entityB = _world.CreateEntity();
            _entityMap.Register(1L, entityA);
            _entityMap.Register(2L, entityB);

            var request = new WeaponFireRequest
            {
                ShooterEntityId = 1L,
                TargetEntityId  = 2L,
                WeaponIndex     = 0,
            };

            var events = ProcessAndFlush(translator, in request);

            Assert.Equal(1, events.Length);
            // PACK-P003: WeaponFireIntent now carries Entity handles, not long IDs.
            Assert.Equal(entityA, events[0].Shooter);
            Assert.Equal(entityB, events[0].Target);
            Assert.Equal(0,  events[0].WeaponIndex);
        }

        // ── SC-2: Unknown shooter → skip ──────────────────────────────────────

        /// <summary>
        /// BS1-T006 SC-2: When the shooter ID is not in <see cref="NetworkEntityMap"/>
        /// the translator must publish no events and must not throw.
        /// </summary>
        [Fact]
        public void ProcessSample_SkipsEvent_WhenShooterUnknown()
        {
            var translator = BuildTranslator();

            // Only target is registered; shooter (99) is unknown.
            var entityB = _world.CreateEntity();
            _entityMap.Register(2L, entityB);

            var request = new WeaponFireRequest { ShooterEntityId = 99L, TargetEntityId = 2L };

            var events = ProcessAndFlush(translator, in request);

            Assert.Equal(0, events.Length);
        }

        // ── SC-3: Unknown target → skip ───────────────────────────────────────

        /// <summary>
        /// When the target ID is not in <see cref="NetworkEntityMap"/> the translator
        /// must also skip and not throw.
        /// </summary>
        [Fact]
        public void ProcessSample_SkipsEvent_WhenTargetUnknown()
        {
            var translator = BuildTranslator();

            var entityA = _world.CreateEntity();
            _entityMap.Register(1L, entityA);
            // Target (99) is not registered.

            var request = new WeaponFireRequest { ShooterEntityId = 1L, TargetEntityId = 99L };

            var events = ProcessAndFlush(translator, in request);

            Assert.Equal(0, events.Length);
        }

        // ── SC-4: Null participant → PollIngress is no-op ─────────────────────

        /// <summary>
        /// When constructed with a <c>null</c> participant <see cref="PollIngress"/>
        /// must return without throwing (test / headless mode).
        /// </summary>
        [Fact]
        public void PollIngress_WithNullParticipant_IsNoOpAndDoesNotThrow()
        {
            var translator = BuildTranslator();
            var view       = (ISimulationView)_world;
            var cmd        = (EntityCommandBuffer)view.GetCommandBuffer();

            var ex = Record.Exception(() => translator.PollIngress(cmd, view));

            Assert.Null(ex);
        }
    }
}
