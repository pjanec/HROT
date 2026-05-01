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
            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 3, TransitionCount = 2 };
            return new HsmDefinitionBlob(header, states, transitions, Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        private static HsmDefinitionBlob Build2StateFinalBlob(uint structureHash)
        {
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1, Flags = StateFlags.IsInitial };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0, Flags = StateFlags.IsFinal };
            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = EventX };
            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 2, TransitionCount = 1 };
            return new HsmDefinitionBlob(header, states, transitions, Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        private static HsmDefinitionBlob BuildPatrolStoppedBlob(uint structureHash)
        {
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1, Flags = StateFlags.IsInitial };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0 };
            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = BehaviorConstants.EventId_MobilityLost };
            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 2, TransitionCount = 1 };
            return new HsmDefinitionBlob(header, states, transitions, Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        private static int CountDoctrineFinishedEvents(EntityRepository world, Entity e)
        {
            int count = 0;
            foreach (var evt in world.Bus.Read<DoctrineFinishedEvent>())
                if (evt.Entity.Index == e.Index) count++;
            return count;
        }

        private static void InjectEvents<T>(EntityRepository world, Entity e, params HsmEvent[] events) where T : unmanaged
        {
            ref var comp = ref world.GetComponentRW<T>(e);
            T* ptr = (T*)Unsafe.AsPointer(ref comp);
            foreach (var evt in events)
                HsmEventQueue.TryEnqueue(ptr, evt);
        }

        private static BrainHsm128 MakeBrain128(HsmDefinitionBlob blob)
        {
            var brain = new BrainHsm128();
            brain.State.Header.MachineId = blob.Header.StructureHash;
            brain.State.Header.Phase = InstancePhase.Entry;
            brain.State.ActiveLeafIds[0] = 0xFFFF;
            return brain;
        }

        private static BrainHsm64 MakeBrain64(HsmDefinitionBlob blob)
        {
            var brain = new BrainHsm64();
            brain.State.Header.MachineId = blob.Header.StructureHash;
            brain.State.Header.Phase = InstancePhase.Entry;
            brain.State.ActiveLeafIds[0] = 0xFFFF;
            return brain;
        }

        // IT-BHU-A1: HSM reaches final state, DoctrineFinishedEvent published.
        // Proves BHU-005 (IsFinal flag emitted) + BHU-006 (Terminated set in kernel)
        // + BHU-007 (HsmTickSystem publishes event and clears latch).
        [Fact]
        public void A1_HsmReachesFinalState_DoctrineFinishedEventPublished()
        {
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            const int    docId = 99001;
            var blob = Build3StateBlob(0xA1000001);

            registry.Register(docId, "A1Doc", new DoctrineDefinition { Name = "A1Doc", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var sys = new HsmTickSystem<BrainHsm128>(registry);
            var e   = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            world.AddComponent(e, MakeBrain128(blob));
            world.AddComponent(e, new BrainBlackboard());

            // BrainHsm128 Tier2 queue: 1 interrupt slot + 1 ring slot.
            // EventX uses the interrupt slot; EventY goes to the ring slot.
            // Dequeue order: interrupt first (EventX), then ring (EventY).
            InjectEvents<BrainHsm128>(world, e,
                new HsmEvent { EventId = EventX, Priority = EventPriority.Interrupt },
                new HsmEvent { EventId = EventY });

            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);

            world.Bus.SwapBuffers();

            Assert.Equal(1, CountDoctrineFinishedEvents(world, e));

            var brainAfter = world.GetComponent<BrainHsm128>(e);
            ref var hdr = ref Unsafe.As<BrainHsm128, InstanceHeader>(ref brainAfter);
            Assert.Equal(0, (int)(hdr.Flags & InstanceFlags.Terminated));
            Assert.Equal(InstancePhase.Idle, hdr.Phase);

            world.Dispose();
        }

        // IT-BHU-A2: Second tick same instance does NOT re-publish event (dedup by InstanceId).
        // Proves the deduplication in HsmTickSystem._publishedTerminalForInstanceId.
        [Fact]
        public void A2_SecondTick_SameInstanceId_DoesNotRepublish()
        {
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            const int    docId = 99002;
            var blob = Build3StateBlob(0xA2000001);

            registry.Register(docId, "A2Doc", new DoctrineDefinition { Name = "A2Doc", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var sys = new HsmTickSystem<BrainHsm128>(registry);
            var e   = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            world.AddComponent(e, MakeBrain128(blob));
            world.AddComponent(e, new BrainBlackboard());

            // Frame 1: drive to terminal.
            InjectEvents<BrainHsm128>(world, e,
                new HsmEvent { EventId = EventX, Priority = EventPriority.Interrupt },
                new HsmEvent { EventId = EventY });
            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountDoctrineFinishedEvents(world, e));

            // Frame 2: same InstanceId -- dedup must suppress re-publication.
            for (int i = 0; i < 5; i++)
                sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(0, CountDoctrineFinishedEvents(world, e));

            world.Dispose();
        }

        // IT-BHU-A3: Doctrine reassignment clears terminal latch and allows a new event.
        // Proves BHU-016 (HSM reset on ingress) and dedup key bump on InstanceId change.
        [Fact]
        public void A3_DoctrineReassignment_AllowsNewEvent_ActiveLeafIdsResetToFfffBeforeFirstTick()
        {
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            const int    docIdA = 99003;
            const int    docIdB = 99004;
            // Both doctrines share the same blob hash so that MachineId remains valid
            // after DoctrineIngressSystem.ResetHsmComponents (which does not update MachineId).
            // What we test here is that the InstanceId bump causes a fresh DoctrineFinishedEvent.
            var sharedBlob = Build3StateBlob(0xA3000001);

            registry.Register(docIdA, "A3DocA", new DoctrineDefinition { Name = "A3DocA", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = sharedBlob });
            registry.Register(docIdB, "A3DocB", new DoctrineDefinition { Name = "A3DocB", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = sharedBlob });

            var sys        = new HsmTickSystem<BrainHsm128>(registry);
            var ingressSys = new DoctrineIngressSystem(registry);
            var e          = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = docIdA, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            world.AddComponent(e, MakeBrain128(sharedBlob));
            world.AddComponent(e, new BrainBlackboard());

            // Drive doctrine A to terminal.
            InjectEvents<BrainHsm128>(world, e,
                new HsmEvent { EventId = EventX, Priority = EventPriority.Interrupt },
                new HsmEvent { EventId = EventY });
            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountDoctrineFinishedEvents(world, e));

            // Assign doctrine B via AssignDoctrineHashEvent.
            world.Bus.Publish(new AssignDoctrineHashEvent { Entity = e, DoctrineHash = docIdB });
            world.Bus.SwapBuffers();
            ingressSys.Execute(world, 0.016f);

            // BHU-016: ActiveLeafIds must be 0xFFFF before the first tick of doctrine B.
            var brainBeforeTick = world.GetComponent<BrainHsm128>(e);
            Assert.Equal((ushort)0xFFFF, brainBeforeTick.State.ActiveLeafIds[0]);

            var doctrine = world.GetComponent<DoctrineState>(e);
            Assert.Equal(2u, doctrine.InstanceId);

            // Drive doctrine B to terminal (same blob, so MachineId still valid).
            InjectEvents<BrainHsm128>(world, e,
                new HsmEvent { EventId = EventX, Priority = EventPriority.Interrupt },
                new HsmEvent { EventId = EventY });
            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();

            // New event published (dedup key == InstanceId 2).
            Assert.Equal(1, CountDoctrineFinishedEvents(world, e));

            world.Dispose();
        }

        // IT-BHU-A4: BrainHsm64 also publishes DoctrineFinishedEvent (covers both instance sizes).
        // Uses a 2-state blob because BrainHsm64 Tier1 queue capacity is one event.
        [Fact]
        public void A4_BrainHsm64_PublishesDoctrineFinishedEvent_LatchCleared()
        {
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            const int    docId = 99005;
            var blob = Build2StateFinalBlob(0xA4000001);

            registry.Register(docId, "A4Doc", new DoctrineDefinition { Name = "A4Doc", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var sys = new HsmTickSystem<BrainHsm64>(registry);
            var e   = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            world.AddComponent(e, MakeBrain64(blob));
            world.AddComponent(e, new BrainBlackboard());

            // Inject single EventX (Tier1 holds only one event).
            InjectEvents<BrainHsm64>(world, e, new HsmEvent { EventId = EventX });

            for (int i = 0; i < 20; i++)
                sys.Execute(world, 0.016f);

            world.Bus.SwapBuffers();

            Assert.Equal(1, CountDoctrineFinishedEvents(world, e));

            var brainAfter = world.GetComponent<BrainHsm64>(e);
            ref var hdr = ref Unsafe.As<BrainHsm64, InstanceHeader>(ref brainAfter);
            Assert.Equal(0, (int)(hdr.Flags & InstanceFlags.Terminated));
            Assert.Equal(InstancePhase.Idle, hdr.Phase);

            world.Dispose();
        }

        // IT-BHU-B1: Mobility-lost edge writes byte 126 and HSM receives the event.
        // Proves BHU-008 (CognitiveInterruptSystem) + BHU-009 (HsmTickSystem reads byte 126)
        // + BHU-015 (CognitiveCleanupSystem clears byte at end of frame).
        [Fact]
        public void B1_MobilityLostEdge_WritesByte126_HsmTransitionsToStopped()
        {
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            const int    docId = 99010;
            var blob = BuildPatrolStoppedBlob(0xB1000001);

            registry.Register(docId, "PatrolDoc", new DoctrineDefinition { Name = "PatrolDoc", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var interruptSys = new CognitiveInterruptSystem();
            var hsmSys       = new HsmTickSystem<BrainHsm128>(registry);
            var cleanupSys   = new CognitiveCleanupSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            world.AddComponent(e, MakeBrain128(blob));
            world.AddComponent(e, new BrainBlackboard());
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

            // CognitiveInterruptSystem detects edge (prev=CanMove, curr=no CanMove) -> Memory[126]=1.
            interruptSys.Execute(world, 0.016f);

            // Assert mid-frame: interrupt byte was set.
            {
                ref readonly var bb = ref world.GetComponentRO<BrainBlackboard>(e);
                Assert.Equal(1, bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost]);
            }

            // HsmTickSystem reads Memory[126]=1, injects MobilityLost, and drives the transition.
            for (int i = 0; i < 10; i++)
                hsmSys.Execute(world, 0.016f);

            // CognitiveCleanupSystem clears Memory[126].
            cleanupSys.Execute(world, 0.016f);

            // Assert end-of-frame: byte cleared.
            {
                ref readonly var bb = ref world.GetComponentRO<BrainBlackboard>(e);
                Assert.Equal(0, bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost]);
            }

            // Assert: HSM transitioned from Patrol(0) to Stopped(1).
            var finalBrain = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(1, finalBrain.State.ActiveLeafIds[0]);

            world.Dispose();
        }

        // IT-BHU-B2: No re-trigger on second frame when CanMove is still false (edge, not level).
        // Proves the edge-triggered semantics in CognitiveInterruptSystem.
        [Fact]
        public void B2_NoRetrigger_SecondFrame_CanMoveStillFalse()
        {
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            const int    docId = 99011;
            var blob = BuildPatrolStoppedBlob(0xB2000001);

            registry.Register(docId, "PatrolDoc2", new DoctrineDefinition { Name = "PatrolDoc2", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var interruptSys = new CognitiveInterruptSystem();
            var hsmSys       = new HsmTickSystem<BrainHsm128>(registry);
            var cleanupSys   = new CognitiveCleanupSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            world.AddComponent(e, MakeBrain128(blob));
            world.AddComponent(e, new BrainBlackboard());
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
            interruptSys.Execute(world, 0.016f); // sets Memory[126]=1, updates PreviousCapabilities
            for (int i = 0; i < 10; i++)
                hsmSys.Execute(world, 0.016f);
            cleanupSys.Execute(world, 0.016f);   // clears Memory[126]

            // Frame 2: CanMove still false; PreviousCapabilities already updated to no CanMove.
            // No edge => Memory[126] must remain 0 throughout.
            interruptSys.Execute(world, 0.016f); // no edge: prev==curr==no CanMove

            {
                ref readonly var bb = ref world.GetComponentRO<BrainBlackboard>(e);
                Assert.Equal(0, bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost]);
            }

            for (int i = 0; i < 5; i++)
                hsmSys.Execute(world, 0.016f);
            cleanupSys.Execute(world, 0.016f);

            {
                ref readonly var bb = ref world.GetComponentRO<BrainBlackboard>(e);
                Assert.Equal(0, bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost]);
            }

            // Assert: HSM remains in Stopped (index 1) -- no spurious second transition.
            var finalBrain = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(1, finalBrain.State.ActiveLeafIds[0]);

            world.Dispose();
        }

        // IT-BHU-B3: BTree entity also gets byte 126 cleared (brain-tier-agnostic cleanup).
        // Proves CognitiveCleanupSystem operates on all BrainBlackboard entities regardless of tier.
        [Fact]
        public void B3_BTreeEntity_Byte126_ClearedByCleanupSystem()
        {
            var world      = TestWorldFactory.Create();
            var cleanupSys = new CognitiveCleanupSystem();

            // Create a BTree-tier entity with only BrainBlackboard.
            var e = world.CreateEntity();
            world.AddComponent(e, new BrainBlackboard());

            // Directly force Memory[126] = 1 (simulating an interrupt byte that was set).
            {
                ref var bb = ref world.GetComponentRW<BrainBlackboard>(e);
                bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] = 1;
            }

            {
                ref readonly var bb = ref world.GetComponentRO<BrainBlackboard>(e);
                Assert.Equal(1, bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost]);
            }

            // CognitiveCleanupSystem must clear the byte for all BrainBlackboard entities.
            cleanupSys.Execute(world, 0.016f);

            {
                ref readonly var bb = ref world.GetComponentRO<BrainBlackboard>(e);
                Assert.Equal(0, bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost]);
            }

            world.Dispose();
        }
    }
}