using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CognitiveInterruptSystem"/> (BHU-008).
    /// Verifies edge-triggered detection of <see cref="ActorCapabilities.CanMove"/> loss
    /// and the corresponding write to <see cref="BrainBlackboard.Interrupt_MobilityLost"/>.
    /// </summary>
    public unsafe class CognitiveInterruptSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly CognitiveInterruptSystem _sys;

        public CognitiveInterruptSystemTests()
        {
            _world = TestWorldFactory.Create();
            _sys   = new CognitiveInterruptSystem();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ---- Helpers ----

        /// <summary>
        /// Creates an entity with <see cref="ActorCapabilityState"/>,
        /// <see cref="PreviousCapabilities"/>, and a zeroed <see cref="BrainBlackboard"/>.
        /// The previous capabilities are set separately to allow edge configuration.
        /// </summary>
        private Entity CreateEntity(ActorCapabilities current, ActorCapabilities previous)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = current });
            _world.AddComponent(e, new PreviousCapabilities { Capabilities = previous });
            _world.AddComponent(e, new BrainBlackboard());
            return e;
        }

        // ---- Tests ----

        [Fact]
        public void CognitiveInterrupt_CanMoveLost_SetsByte126()
        {
            // Entity had CanMove; it is now absent -- edge must be detected.
            var e = CreateEntity(
                current:  ActorCapabilities.None,
                previous: ActorCapabilities.CanMove);

            _sys.Execute(_world, 0.016f);

            var bb = _world.GetComponent<BrainBlackboard>(e);
            Assert.Equal(1, bb.Interrupt_MobilityLost);
        }

        [Fact]
        public void CognitiveInterrupt_StaysNoCanMove_DoesNotSetByteAgain()
        {
            // Edge triggered on first Execute. After simulated cleanup, second Execute
            // must not set byte 126 again (edge-triggered, not level-triggered).
            var e = CreateEntity(
                current:  ActorCapabilities.None,
                previous: ActorCapabilities.CanMove);

            // Frame 1: edge detected -- Interrupt_MobilityLost = 1.
            _sys.Execute(_world, 0.016f);
            Assert.Equal(1, _world.GetComponent<BrainBlackboard>(e)
                .Interrupt_MobilityLost);

            // Simulate CognitiveCleanupSystem clearing the interrupt byte.
            ref var bb = ref _world.GetComponentRW<BrainBlackboard>(e);
            bb.Interrupt_MobilityLost = 0;

            // Frame 2: CanMove still absent, no new edge -- Interrupt_MobilityLost must stay 0.
            _sys.Execute(_world, 0.016f);
            Assert.Equal(0, _world.GetComponent<BrainBlackboard>(e)
                .Interrupt_MobilityLost);
        }

        [Fact]
        public void CognitiveInterrupt_AlwaysCanMove_ByteStaysZero()
        {
            // Entity always has CanMove -- no transition, no interrupt.
            var e = CreateEntity(
                current:  ActorCapabilities.CanMove,
                previous: ActorCapabilities.CanMove);

            _sys.Execute(_world, 0.016f);
            _sys.Execute(_world, 0.016f);

            var bb = _world.GetComponent<BrainBlackboard>(e);
            Assert.Equal(0, bb.Interrupt_MobilityLost);
        }
    }
}
