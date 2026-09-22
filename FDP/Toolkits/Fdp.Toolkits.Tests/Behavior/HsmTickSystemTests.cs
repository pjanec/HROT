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

        // ── Test 2 — REMOVED by O7c-① (2026-09-22) ──────────────────────────────
        //
        // `HsmTick64_And_HsmTick128_AreIndependent` asserted that two GENERIC INSTANTIATIONS of
        // HsmTickSystem each query only the component they own and never touch the other's.
        // ⛔ With BrainHsm64 deleted there is ONE instantiation, so the claim cannot be false.
        // ⚠ This is a claim that EXPIRED, not one that was dropped — stated explicitly because a
        //   silently deleted test and a silently weakened one look identical in a diff.
        //
        // ⭐⭐ THE SUCCESSOR CLAIM IS REAL AND IT BELONGS TO THE HSM SLICE: once the instance lives
        //   in an occurrence slot, the tier walk must filter on OccurrenceKind.Hsm and must not
        //   touch a BTree or Blueprint slot sharing the same store. That is the same "each walker
        //   sees only its own" property, at the level where it can still be violated.
        //   📄 DESIGN_Occurrence_Scoped_Storage.md §31.5 step ④ / §31.7.

    }
}
