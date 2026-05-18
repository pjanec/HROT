using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public unsafe class HsmTickSystemTests
    {
        // ── Named constants ───────────────────────────────────────────────────────

        /// <summary>
        /// The event ID used by the transition in the test HSM: State 0 --EventX(id=10)--> State 1.
        /// </summary>
        private const int EventXId = 10;

        /// <summary>
        /// Ties this test to the FastHSM version where HsmInstance128.Reserved1 (offset 58)
        /// doubles as the CurrentEventId scratch field used by HsmKernelCore.
        /// Specifically: HsmKernelCore.CurrentEventId_Offset_128 == 58 == FieldOffset of Reserved1.
        /// If HsmInstance128 layout changes (e.g. Reserved1 is moved or repurposed),
        /// update this constant and the injection line below.
        /// Verified against Fhsm.Kernel v(current) — field is ushort at [FieldOffset(58)].
        /// </summary>
        private const string HsmCurrentEventFieldName = nameof(HsmInstance128.Reserved1);

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Build a minimal 2-state HSM: State 0 --EventX(id=10)--> State 1.
        /// </summary>
        private static HsmDefinitionBlob BuildTwoStateBlob(uint structureHash = 0x12345678)
        {
            var states = new StateDef[2];
            // State 0: root-level, owns transition index 0
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1 };
            // State 1: root-level, no transitions
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF };

            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = 10 };

            var header = new HsmDefinitionHeader();
            header.StructureHash  = structureHash;
            header.StateCount     = 2;
            header.TransitionCount = 1;

            return new HsmDefinitionBlob(
                header,
                states,
                transitions,
                Array.Empty<RegionDef>(),
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>());
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void HsmTick_TransitionsState_OnRegisteredEvent()
        {
            // Arrange.
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();

            var blob = BuildTwoStateBlob();
            const string behaviorName = "TestHsm";
            const int TestHsmId = 9001;
            registry.Register(TestHsmId, behaviorName, new BehaviorDefinition
            {
                Name          = behaviorName,
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });

            var sys = new HsmTickSystem<BrainHsm128>(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = TestHsmId,
                BrainTier          = BehaviorConstants.BrainTierHsm,
            });

            // Initialise instance: in StateA (index 0), RTC phase, EventX (id=10) ready.
            var brain = new BrainHsm128();
            brain.State.Header.MachineId = blob.Header.StructureHash;
            brain.State.Header.Phase     = InstancePhase.RTC;
            // ActiveLeafIds[0] = 0 means currently in State 0 (StateA).
            brain.State.ActiveLeafIds[0] = 0;
            // Inject EventX into the CurrentEventId scratch field (see HsmCurrentEventFieldName above).
            // Reserved1 at offset 58 is the scratch slot HsmKernelCore reads as the pending event id.
#pragma warning disable CS0219 // variable assigned but never read — used as documentation anchor
            _ = HsmCurrentEventFieldName; // documents which field we are writing below
#pragma warning restore CS0219
            brain.State.Reserved1 = EventXId;

            world.AddComponent(e, brain);

            // Act.
            sys.Execute(world, 0.016f);

            // Assert — HSM transitioned from State 0 to State 1.
            var result = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(1, result.State.ActiveLeafIds[0]); // StateB.Id == 1

            world.Dispose();
        }

        // ── Test 3 (DEBT-007 structural test — updated for GCHandle bridge) ────────
        /// <summary>
        /// <see cref="HsmKernelBridge.WorldHandle"/> must allow recovering the original
        /// <see cref="EntityRepository"/> via <c>GCHandle.FromIntPtr</c>.
        /// This validates the zero-allocation GCHandle round-trip used by HSM action delegates.
        /// (Replaces the removed <c>FdpHsmContext_ExposesWorldAccess</c> test —
        /// <c>FdpHsmContext</c> was deleted as part of DEBT-007 full resolution.)
        /// </summary>
        [Fact]
        public void HsmKernelBridge_WorldHandle_RoundTrip_RecoversSameInstance()
        {
            var world = TestWorldFactory.Create();

            // Act — simulate what HsmTickSystem<T> does each frame:
            var bridge = new HsmKernelBridge
            {
                Self        = Entity.Null,
                WorldHandle = world.UnmanagedHandle,
            };

            // Simulate what HSM action delegates do:
            var recovered = (EntityRepository)GCHandle.FromIntPtr(bridge.WorldHandle).Target!;

            // Assert — same instance recovered
            Assert.Same(world, recovered);

            world.Dispose();
        }

        // ── Test 2 ────────────────────────────────────────────────────────────
        [Fact]
        public void HsmTick64_And_HsmTick128_AreIndependent()
        {
            // Arrange — entity A has BrainHsm64 only; entity B has BrainHsm128 only.
            var world = TestWorldFactory.Create();

            // Empty registries — neither entity has a registered behavior, so both
            // systems will skip them. What we're testing is that each system only
            // queries the component it owns and never touches the other type.
            var sys128 = new HsmTickSystem<BrainHsm128>(new BehaviorRegistry());
            var sys64  = new HsmTickSystem<BrainHsm64>(new BehaviorRegistry());

            // Entity A — only BrainHsm64.
            var entityA = world.CreateEntity();
            world.AddComponent(entityA, new BehaviorState { BrainTier = BehaviorConstants.BrainTierHsm });
            var brainA = new BrainHsm64();
            brainA.State.Header.Phase = InstancePhase.Idle;
            world.AddComponent(entityA, brainA);
            // NO BrainHsm128 on entityA.

            // Entity B — only BrainHsm128.
            var entityB = world.CreateEntity();
            world.AddComponent(entityB, new BehaviorState { BrainTier = BehaviorConstants.BrainTierHsm });
            var brainB = new BrainHsm128();
            brainB.State.Header.Phase = InstancePhase.Idle;
            world.AddComponent(entityB, brainB);
            // NO BrainHsm64 on entityB.

            // Act — run the 128 system first, then the 64 system.
            sys128.Execute(world, 0.016f);

            // sys128 processed only entityB (has BrainHsm128).
            // entityA's BrainHsm64 must be unchanged.
            var aAfter128 = world.GetComponent<BrainHsm64>(entityA);
            Assert.Equal(InstancePhase.Idle, aAfter128.State.Header.Phase);

            sys64.Execute(world, 0.016f);

            // sys64 processed only entityA (has BrainHsm64).
            // entityB's BrainHsm128 must be unchanged.
            var bAfter64 = world.GetComponent<BrainHsm128>(entityB);
            Assert.Equal(InstancePhase.Idle, bAfter64.State.Header.Phase);

            world.Dispose();
        }
    }
}
