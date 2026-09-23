using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public unsafe class BhuIntegrationTests
    {
        private const ushort EventX = 10;
        private const ushort EventY = 20;

        private static HsmDefinitionBlob Build3StateBlob(uint structureHash)
        {
            var states = new StateDef[3];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1, Flags = StateFlags.IsInitial };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 1, TransitionCount = 1 };
            states[2] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0, Flags = StateFlags.IsFinal };
            var transitions = new TransitionDef[2];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = EventX };
            transitions[1] = new TransitionDef { SourceStateIndex = 1, TargetStateIndex = 2, EventId = EventY };
            // ⭐⭐ O7c-④b: RegionCount 2 is what makes HsmInstanceManager.SelectTier answer 128.
            //   🔴 NOT cosmetic — the instance width decides the EVENT-QUEUE shape: a 64-byte
            //   instance has ring capacity 1 and NO interrupt slot, while these rails inject two
            //   events and assert interrupt-before-ring ordering. Before the move every instance
            //   was 128 because the COMPONENT was, so the blob never had to say so.
            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 3, TransitionCount = 2, RegionCount = 2 };
            return new HsmDefinitionBlob(header, states, transitions, new RegionDef[2], Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        private static HsmDefinitionBlob Build2StateFinalBlob(uint structureHash)
        {
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1, Flags = StateFlags.IsInitial };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0, Flags = StateFlags.IsFinal };
            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = EventX };
            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 2, TransitionCount = 1, RegionCount = 2 };  // O7c-④b: see Build3StateBlob
            return new HsmDefinitionBlob(header, states, transitions, new RegionDef[2], Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        private static HsmDefinitionBlob BuildPatrolStoppedBlob(uint structureHash)
        {
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1, Flags = StateFlags.IsInitial };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0 };
            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = BehaviorConstants.EventId_MobilityLost };
            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 2, TransitionCount = 1, RegionCount = 2 };  // O7c-④b: see Build3StateBlob
            return new HsmDefinitionBlob(header, states, transitions, new RegionDef[2], Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        private static int CountBehaviorFinishedEvents(EntityRepository world, Entity e)
        {
            int count = 0;
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index) count++;
            return count;
        }

        /// <summary>⭐ O7c-④b: enqueue through the slot, sized from the allocation, never from a type.</summary>
        private static void InjectEvents(EntityRepository world, Entity e, params HsmEvent[] events)
        {
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out int size));
            foreach (var evt in events)
                Assert.True(HsmEventQueue.TryEnqueue(ptr, size, evt),
                    "The instance's event queue rejected an event. Its capacity follows the tier that " +
                    "HsmInstanceManager.SelectTier chose for this machine: 64 holds ONE event and has " +
                    "no interrupt slot; 128 holds an interrupt plus one ring entry.");
        }

        /// <summary>
        /// ⭐ The first ACTIVE LEAF id, read size-driven through the kernel's own accessor.
        /// ⛔ Both the array's offset and its length are functions of the instance size, so a caller
        /// outside the kernel cannot compute them without copying the tier table.
        /// </summary>
        private static ushort ActiveLeaf0(EntityRepository world, Entity e)
        {
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out int size));
            ushort* leaves = HsmKernel.GetActiveLeafIds(ptr, size, out int count);
            Assert.True(leaves != null && count > 0);
            return leaves[0];
        }

        /// <summary>⭐ The instance's header, read through the slot rather than a component field.</summary>
        private static InstanceHeader* HeaderOf(EntityRepository world, Entity e)
        {
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out _));
            return (InstanceHeader*)ptr;
        }

        /// <summary>
        /// ⭐⭐ O7c-④b — provision the root HSM instance slot, which replaces
        /// <c>world.AddComponent(e, MakeBrain128(blob))</c>. <c>EnsureRootInstance</c> stamps
        /// <c>MachineId</c>, <c>Phase = Entry</c> and the 0xFFFF leaves, which is what that helper did.
        /// </summary>
        private static void SeedBrain(EntityRepository world, Entity e, int docId, HsmDefinitionBlob blob)
            => Assert.True(RootHsmAccess.EnsureRootInstance(world, e, docId, blob));

        // ⛔ O7c-① (2026-09-22): MakeBrain64 went with BrainHsm64.
        // ⛔ O7c-④b (2026-09-23): MakeBrain128 went with the instance moving into an occurrence slot.

        // IT-BHU-A1: HSM reaches final state, BehaviorFinishedEvent published.
        // Proves BHU-005 (IsFinal flag emitted) + BHU-006 (Terminated set in kernel)
        // + BHU-007 (HsmTickSystem publishes event and clears latch).
        [Fact]
        public void A1_HsmReachesFinalState_BehaviorFinishedEventPublished()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int    docId = 99001;
            var blob = Build3StateBlob(0xA1000001);

            registry.Register(docId, "A1Doc", new BehaviorDefinition { Name = "A1Doc", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var sys = new BrainTickSystem(registry);
            var e   = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            SeedBrain(world, e, docId, blob);
            world.AddComponent(e, new BrainInterrupts());

            // Tier-2 (128-byte) queue: 1 interrupt slot + 1 ring slot — see Build3StateBlob's note.
            // EventX uses the interrupt slot; EventY goes to the ring slot.
            // Dequeue order: interrupt first (EventX), then ring (EventY).
            InjectEvents(world, e,
                new HsmEvent { EventId = EventX, Priority = EventPriority.Interrupt },
                new HsmEvent { EventId = EventY });

            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);

            world.Bus.SwapBuffers();

            Assert.Equal(1, CountBehaviorFinishedEvents(world, e));

            InstanceHeader* hdr = HeaderOf(world, e);
            Assert.Equal(0, (int)(hdr->Flags & InstanceFlags.Terminated));
            Assert.Equal(InstancePhase.Idle, hdr->Phase);

            world.Dispose();
        }

        // IT-BHU-A2: Second tick same instance does NOT re-publish event (dedup by InstanceId).
        // Proves the deduplication in HsmTickSystem._publishedTerminalForInstanceId.
        [Fact]
        public void A2_SecondTick_SameInstanceId_DoesNotRepublish()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int    docId = 99002;
            var blob = Build3StateBlob(0xA2000001);

            registry.Register(docId, "A2Doc", new BehaviorDefinition { Name = "A2Doc", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var sys = new BrainTickSystem(registry);
            var e   = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            SeedBrain(world, e, docId, blob);
            world.AddComponent(e, new BrainInterrupts());

            // Frame 1: drive to terminal.
            InjectEvents(world, e,
                new HsmEvent { EventId = EventX, Priority = EventPriority.Interrupt },
                new HsmEvent { EventId = EventY });
            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountBehaviorFinishedEvents(world, e));

            // Frame 2: same InstanceId -- dedup must suppress re-publication.
            for (int i = 0; i < 5; i++)
                sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(0, CountBehaviorFinishedEvents(world, e));

            world.Dispose();
        }

        // IT-BHU-A3: Behavior reassignment clears terminal latch and allows a new event.
        // Proves BHU-016 (HSM reset on ingress) and dedup key bump on InstanceId change.
        [Fact]
        public void A3_BehaviorReassignment_AllowsNewEvent_ActiveLeafIdsResetToFfffBeforeFirstTick()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int    docIdA = 99003;
            const int    docIdB = 99004;
            // Both behaviors share the same blob hash so that the MachineId written by
            // BehaviorIngressSystem.ResetHsmComponents remains valid for the shared blob.
            // What we test here is that the InstanceId bump causes a fresh BehaviorFinishedEvent.
            var sharedBlob = Build3StateBlob(0xA3000001);

            registry.Register(docIdA, "A3DocA", new BehaviorDefinition { Name = "A3DocA", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = sharedBlob });
            registry.Register(docIdB, "A3DocB", new BehaviorDefinition { Name = "A3DocB", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = sharedBlob });

            var sys        = new BrainTickSystem(registry);
            var ingressSys = new BehaviorIngressSystem(registry);
            var e          = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = docIdA, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            SeedBrain(world, e, docIdA, sharedBlob);
            world.AddComponent(e, new BrainInterrupts());

            // Drive behavior A to terminal.
            InjectEvents(world, e,
                new HsmEvent { EventId = EventX, Priority = EventPriority.Interrupt },
                new HsmEvent { EventId = EventY });
            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountBehaviorFinishedEvents(world, e));

            // Assign behavior B via AssignBehaviorHashEvent.
            world.Bus.Publish(new AssignBehaviorHashEvent { Entity = e, BehaviorHash = docIdB });
            world.Bus.SwapBuffers();
            ingressSys.Execute(world, 0.016f);

            // BHU-016: ActiveLeafIds must be 0xFFFF before the first tick of behavior B.
            Assert.Equal((ushort)0xFFFF, ActiveLeaf0(world, e));

            var behavior = world.GetComponent<BehaviorState>(e);
            Assert.Equal(2u, behavior.InstanceId);

            // Drive behavior B to terminal (same blob, so MachineId still valid).
            InjectEvents(world, e,
                new HsmEvent { EventId = EventX, Priority = EventPriority.Interrupt },
                new HsmEvent { EventId = EventY });
            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();

            // New event published (dedup key == InstanceId 2).
            Assert.Equal(1, CountBehaviorFinishedEvents(world, e));

            world.Dispose();
        }

        // ⛔ IT-BHU-A4 REMOVED by O7c-① (2026-09-22).
        //   📐 Its stated claim was "covers both instance sizes", and it asserted EXACTLY the three
        //   things IT-BHU-A1 already asserts on the 128 tier: one BehaviorFinishedEvent, Terminated
        //   cleared, Phase == Idle. ⇒ with BrainHsm64 gone the second size does not exist, so this
        //   is a DUPLICATE of A1 rather than lost coverage.
        //   ⚠ Said out loud because "covers both sizes" is the kind of claim that quietly becomes
        //   false while the test keeps passing.


        // IT-BHU-B1: Mobility-lost edge writes byte 126 and HSM receives the event.
        // Proves BHU-008 (CognitiveInterruptSystem) + BHU-009 (HsmTickSystem reads byte 126)
        // + BHU-015 (CognitiveCleanupSystem clears byte at end of frame).
        [Fact]
        public void B1_MobilityLostEdge_WritesByte126_HsmTransitionsToStopped()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int    docId = 99010;
            var blob = BuildPatrolStoppedBlob(0xB1000001);

            registry.Register(docId, "PatrolDoc", new BehaviorDefinition { Name = "PatrolDoc", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var interruptSys = new CognitiveInterruptSystem();
            var hsmSys       = new BrainTickSystem(registry);
            var cleanupSys   = new CognitiveCleanupSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            SeedBrain(world, e, docId, blob);
            world.AddComponent(e, new BrainInterrupts());
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            world.AddComponent(e, new PreviousCapabilities { Capabilities = ActorCapabilities.CanMove });

            // Initialize the machine: settle into Patrol state (ActiveLeafIds[0] = 0).
            for (int i = 0; i < 4; i++)
                hsmSys.Execute(world, 0.016f);

            // Clear CanMove capability to trigger the mobility-lost edge.
            {
                ref var ac = ref world.GetComponentRW<ActorCapabilityState>(e);
                ac.Capabilities &= ~ActorCapabilities.CanMove;
            }

            // CognitiveInterruptSystem detects edge (prev=CanMove, curr=no CanMove) -> Interrupt_MobilityLost=1.
            interruptSys.Execute(world, 0.016f);

            // Assert mid-frame: interrupt field was set.
            {
                ref readonly var bb = ref world.GetComponentRO<BrainInterrupts>(e);
                Assert.Equal(1, bb.Interrupt_MobilityLost);
            }

            // HsmTickSystem reads Interrupt_MobilityLost=1, injects MobilityLost, and drives the transition.
            for (int i = 0; i < 10; i++)
                hsmSys.Execute(world, 0.016f);

            // CognitiveCleanupSystem clears Interrupt_MobilityLost.
            cleanupSys.Execute(world, 0.016f);

            // Assert end-of-frame: field cleared.
            {
                ref readonly var bb = ref world.GetComponentRO<BrainInterrupts>(e);
                Assert.Equal(0, bb.Interrupt_MobilityLost);
            }

            // Assert: HSM transitioned from Patrol(0) to Stopped(1).
            Assert.Equal(1, ActiveLeaf0(world, e));

            world.Dispose();
        }

        // IT-BHU-B2: No re-trigger on second frame when CanMove is still false (edge, not level).
        // Proves the edge-triggered semantics in CognitiveInterruptSystem.
        [Fact]
        public void B2_NoRetrigger_SecondFrame_CanMoveStillFalse()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int    docId = 99011;
            var blob = BuildPatrolStoppedBlob(0xB2000001);

            registry.Register(docId, "PatrolDoc2", new BehaviorDefinition { Name = "PatrolDoc2", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var interruptSys = new CognitiveInterruptSystem();
            var hsmSys       = new BrainTickSystem(registry);
            var cleanupSys   = new CognitiveCleanupSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            SeedBrain(world, e, docId, blob);
            world.AddComponent(e, new BrainInterrupts());
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            world.AddComponent(e, new PreviousCapabilities { Capabilities = ActorCapabilities.CanMove });

            // Initialize machine.
            for (int i = 0; i < 4; i++)
                hsmSys.Execute(world, 0.016f);

            // Frame 1: clear CanMove and run all three systems (triggers transition to Stopped).
            {
                ref var ac = ref world.GetComponentRW<ActorCapabilityState>(e);
                ac.Capabilities &= ~ActorCapabilities.CanMove;
            }
            interruptSys.Execute(world, 0.016f); // sets Interrupt_MobilityLost=1, updates PreviousCapabilities
            for (int i = 0; i < 10; i++)
                hsmSys.Execute(world, 0.016f);
            cleanupSys.Execute(world, 0.016f);   // clears Interrupt_MobilityLost

            // Frame 2: CanMove still false; PreviousCapabilities already updated to no CanMove.
            // No edge => Interrupt_MobilityLost must remain 0 throughout.
            interruptSys.Execute(world, 0.016f); // no edge: prev==curr==no CanMove

            {
                ref readonly var bb = ref world.GetComponentRO<BrainInterrupts>(e);
                Assert.Equal(0, bb.Interrupt_MobilityLost);
            }

            for (int i = 0; i < 5; i++)
                hsmSys.Execute(world, 0.016f);
            cleanupSys.Execute(world, 0.016f);

            {
                ref readonly var bb = ref world.GetComponentRO<BrainInterrupts>(e);
                Assert.Equal(0, bb.Interrupt_MobilityLost);
            }

            // Assert: HSM remains in Stopped (index 1) -- no spurious second transition.
            Assert.Equal(1, ActiveLeaf0(world, e));

            world.Dispose();
        }

        // IT-BHU-B3: BTree entity also gets Interrupt_MobilityLost cleared (brain-tier-agnostic cleanup).
        // Proves CognitiveCleanupSystem operates on all BrainBlackboard entities regardless of tier.
        [Fact]
        public void B3_BTreeEntity_Byte126_ClearedByCleanupSystem()
        {
            var world      = TestWorldFactory.Create();
            var cleanupSys = new CognitiveCleanupSystem();

            // Create a BTree-tier entity with only BrainBlackboard.
            var e = world.CreateEntity();
            world.AddComponent(e, new BrainInterrupts());

            // Directly set Interrupt_MobilityLost = 1 (simulating what CognitiveInterruptSystem would do).
            {
                ref var bb = ref world.GetComponentRW<BrainInterrupts>(e);
                bb.Interrupt_MobilityLost = 1;
            }

            {
                ref readonly var bb = ref world.GetComponentRO<BrainInterrupts>(e);
                Assert.Equal(1, bb.Interrupt_MobilityLost);
            }

            // CognitiveCleanupSystem must clear the field for all BrainBlackboard entities.
            cleanupSys.Execute(world, 0.016f);

            {
                ref readonly var bb = ref world.GetComponentRO<BrainInterrupts>(e);
                Assert.Equal(0, bb.Interrupt_MobilityLost);
            }

            world.Dispose();
        }
    }
}