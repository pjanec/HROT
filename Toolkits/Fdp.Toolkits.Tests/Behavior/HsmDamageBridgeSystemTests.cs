using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HsmDamageBridgeSystem"/> (BCS-P6-T2).
    /// Each test drives the system directly against a real <see cref="EntityRepository"/>
    /// with all required components pre-registered.
    /// </summary>
    public class HsmDamageBridgeSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly HsmDamageBridgeSystem _sys;

        public HsmDamageBridgeSystemTests()
        {
            _world = TestWorldFactory.Create();
            _sys   = new HsmDamageBridgeSystem();
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            _world.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an entity with <see cref="ActorCapabilityState"/>,
        /// <see cref="PreviousCapabilities"/>, and an initialised <see cref="BrainHsm128"/>.
        /// </summary>
        private Entity CreateHsm128Entity(ActorCapabilities initial)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = initial });
            _world.AddComponent(e, new PreviousCapabilities { Capabilities = initial });
            _world.AddComponent(e, new BrainHsm128());
            return e;
        }

        /// <summary>
        /// Returns the number of events currently queued in a <see cref="BrainHsm128"/> component.
        /// </summary>
        private static unsafe int GetQueueCount128(BrainHsm128 brain)
        {
            // brain is a stack-local copy — no pinning needed; use Unsafe.AsPointer.
            HsmInstance128* ptr = (HsmInstance128*)Unsafe.AsPointer(ref brain.State);
            return HsmEventQueue.GetCount(ptr);
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When <see cref="ActorCapabilities.CanMove"/> transitions from set to cleared,
        /// the bridge must enqueue exactly one <c>MobilityLost</c> event into the HSM.
        /// </summary>
        [Fact]
        public void HsmDamageBridge_InjectsMobilityLostEvent_WhenCanMoveCleared()
        {
            var e = CreateHsm128Entity(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);

            // Tick 1: capabilities unchanged — no event should be enqueued.
            _sys.Run();
            var brainAfterTick1 = _world.GetComponent<BrainHsm128>(e);
            Assert.Equal(0, GetQueueCount128(brainAfterTick1));

            // Strip CanMove.
            ref var caps = ref _world.GetComponentRW<ActorCapabilityState>(e);
            caps.Capabilities &= ~ActorCapabilities.CanMove;

            // Tick 2: bridge detects the CanMove transition → must inject MobilityLost.
            _sys.Run();
            var brainAfterTick2 = _world.GetComponent<BrainHsm128>(e);
            Assert.Equal(1, GetQueueCount128(brainAfterTick2));

            // Verify the queued event has the correct ID.
            unsafe
            {
                var brain2 = _world.GetComponent<BrainHsm128>(e);
                HsmInstance128* ptr = (HsmInstance128*)Unsafe.AsPointer(ref brain2.State);
                bool dequeued = HsmEventQueue.TryDequeue(ptr, out var evt);
                Assert.True(dequeued);
                Assert.Equal(BehaviorConstants.EventId_MobilityLost, evt.EventId);
            }
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When <see cref="ActorCapabilities.CanMove"/> was already clear before the system
        /// runs (i.e., no transition occurred), no event must be injected.
        /// Proves the shadow component check fires only on the <em>transition</em>.
        /// </summary>
        [Fact]
        public void HsmDamageBridge_DoesNotInject_WhenCanMoveWasAlreadyClear()
        {
            // Entity starts without CanMove set; PreviousCapabilities also has no CanMove.
            var e = CreateHsm128Entity(ActorCapabilities.CanShoot); // CanMove NOT set

            _sys.Run();

            var brain = _world.GetComponent<BrainHsm128>(e);
            Assert.Equal(0, GetQueueCount128(brain));
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When only <see cref="ActorCapabilities.CanShoot"/> is cleared (not CanMove),
        /// no <c>MobilityLost</c> event must be injected.
        /// </summary>
        [Fact]
        public void HsmDamageBridge_DoesNotInject_WhenCanShootClearedButNotCanMove()
        {
            // Entity has both capabilities; PreviousCapabilities mirrors that.
            var e = CreateHsm128Entity(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);

            // Tick 1: both set — no event.
            _sys.Run();

            // Clear only CanShoot; CanMove stays.
            ref var caps = ref _world.GetComponentRW<ActorCapabilityState>(e);
            caps.Capabilities &= ~ActorCapabilities.CanShoot;

            // Tick 2: only CanShoot changed — no MobilityLost.
            _sys.Run();

            var brain = _world.GetComponent<BrainHsm128>(e);
            Assert.Equal(0, GetQueueCount128(brain));
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After each tick the shadow component must be updated to reflect the current
        /// <see cref="ActorCapabilityState.Capabilities"/>.
        /// </summary>
        [Fact]
        public void HsmDamageBridge_UpdatesShadowCapabilities_EachTick()
        {
            var initial = ActorCapabilities.CanMove | ActorCapabilities.CanShoot;
            var e = CreateHsm128Entity(initial);

            // Tick 1: no changes.
            _sys.Run();
            var prev1 = _world.GetComponent<PreviousCapabilities>(e);
            var curr1 = _world.GetComponent<ActorCapabilityState>(e);
            Assert.Equal(curr1.Capabilities, prev1.Capabilities);

            // Tick 2: still no changes.
            _sys.Run();
            var prev2 = _world.GetComponent<PreviousCapabilities>(e);
            var curr2 = _world.GetComponent<ActorCapabilityState>(e);
            Assert.Equal(curr2.Capabilities, prev2.Capabilities);
        }
    }
}
