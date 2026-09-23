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

        // ⛔⛔ O7c-④b (2026-09-23): THE `Reserved1` SCRATCH POKE IS GONE, AND ITS REMOVAL IS A FIX.
        //   📐 The old constant tied this test to `HsmInstance128.Reserved1` at offset 58, which is
        //     `HsmKernelCore.CurrentEventId_Offset_128`. ⇒ it was only ever correct for a 128-byte
        //     instance, and the two-state machine below actually selects tier **64**
        //     (HsmInstanceManager.SelectTier: 2 states, 0 regions) — so once the instance is sized
        //     from the machine rather than from a component TYPE, that offset points at the wrong
        //     field entirely.
        //   ⭐ The event is now injected through `HsmEventQueue.TryEnqueue(instance, size, evt)`, the
        //     PUBLIC size-driven API the kernel offers for exactly this. It is not a workaround for
        //     the move: it is what the production interrupt path already used.

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

            var sys = new BrainTickSystem(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = TestHsmId,
                BrainTier          = BehaviorConstants.BrainTierHsm,
            });

            // ⭐ O7c-④: the instance lives in an occurrence slot, sized by SelectTier(blob).
            Assert.True(RootHsmAccess.EnsureRootInstance(world, e, TestHsmId, blob));
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* inst, out int size));

            // Initialise instance: in StateA (index 0), Idle, with EventX (id=10) QUEUED.
            // EnsureRootInstance already stamped MachineId from the blob.
            //
            // ⚠ O7c-④b: the old fixture set Phase = RTC and poked the CurrentEventId scratch, because
            //   the RTC arm reads THAT FIELD and never the queue. Going through HsmEventQueue instead
            //   means going through the kernel's real cycle — Idle sees a non-empty queue and advances
            //   to Entry, Entry runs ProcessEventPhase, which dequeues into RTC. ⇒ several ticks, and
            //   that is what production does too.
            ((InstanceHeader*)inst)->Phase = InstancePhase.Idle;

            ushort* leaves = HsmKernel.GetActiveLeafIds(inst, size, out int leafCount);
            Assert.True(leaves != null && leafCount > 0);
            leaves[0] = 0;   // currently in State 0 (StateA)

            Assert.True(HsmEventQueue.TryEnqueue(inst, size, new HsmEvent { EventId = EventXId }));

            // Act — enough ticks for the full Idle -> Entry -> RTC cycle.
            for (int t = 0; t < 5; t++)
                sys.Execute(world, 0.016f);

            // Assert — HSM transitioned from State 0 to State 1.
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* after, out int afterSize));
            ushort* leavesAfter = HsmKernel.GetActiveLeafIds(after, afterSize, out _);
            Assert.Equal(1, leavesAfter[0]);   // StateB.Id == 1

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

            // Act — simulate what BrainTickSystem does each frame:
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
