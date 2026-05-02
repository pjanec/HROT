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
    /// <summary>
    /// BHU-007: terminal-state detection and <see cref="BehaviorFinishedEvent"/> publication.
    /// BHU-009: interrupt-inject path (blackboard byte 126 -> MobilityLost enqueue).
    /// </summary>
    public unsafe class HsmTickSystemTerminalTests
    {
        // ---- Blob builders ----

        /// <summary>
        /// Single-state blob: state 0 is IsInitial | IsFinal.
        /// On first HsmKernel.Update (Phase=Entry, ActiveLeafIds=0xFFFF),
        /// InitializeMachine enters state 0 and BHU-006 sets Terminated immediately.
        /// </summary>
        private static HsmDefinitionBlob BuildFinalStateBlob(uint hash = 0xBEEFCAFE)
        {
            var states = new StateDef[1];
            states[0] = new StateDef
            {
                ParentIndex          = 0xFFFF,
                FirstTransitionIndex = 0xFFFF,
                TransitionCount      = 0,
                Flags                = StateFlags.IsInitial | StateFlags.IsFinal,
            };

            var header = new HsmDefinitionHeader();
            header.StructureHash = hash;
            header.StateCount    = 1;

            return new HsmDefinitionBlob(
                header,
                states,
                Array.Empty<TransitionDef>(),
                Array.Empty<RegionDef>(),
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>());
        }

        /// <summary>
        /// Two-state blob: Active (0) --[MobilityLost]--> Immobilized (1). Neither is final.
        /// Used to test interrupt injection without triggering terminal detection.
        /// </summary>
        private static HsmDefinitionBlob BuildTwoStateBlob(uint hash = 0xABCD1234)
        {
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1 };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF };

            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef
            {
                SourceStateIndex = 0,
                TargetStateIndex = 1,
                EventId          = (ushort)BehaviorConstants.EventId_MobilityLost,
            };

            var header = new HsmDefinitionHeader();
            header.StructureHash   = hash;
            header.StateCount      = 2;
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

        // ---- Helpers ----

        private static Entity CreateHsmEntity(
            EntityRepository world,
            int behaviorId,
            HsmDefinitionBlob blob,
            uint instanceId = 0)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = behaviorId,
                BrainTier          = BehaviorConstants.BrainTierHsm,
                InstanceId         = instanceId,
            });
            var brain = new BrainHsm64();
            brain.State.Header.MachineId = blob.Header.StructureHash;
            brain.State.Header.Phase     = InstancePhase.Entry;
            brain.State.ActiveLeafIds[0] = 0xFFFF;
            world.AddComponent(e, brain);
            return e;
        }

        private static int CountEventsForEntity(EntityRepository world, Entity e)
        {
            int count = 0;
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index) count++;
            return count;
        }

        // ---- BHU-007 Tests ----

        [Fact]
        public void HsmTerminal_FirstTick_PublishesBehaviorFinishedEvent()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int behaviorId = 9100;
            var blob = BuildFinalStateBlob(0xBEEF0001);
            registry.Register(behaviorId, "FinalDoc1", new BehaviorDefinition
            {
                Name          = "FinalDoc1",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });
            var sys = new HsmTickSystem<BrainHsm64>(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob, instanceId: 0);

            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();

            Assert.Equal(1, CountEventsForEntity(world, e));

            world.Dispose();
        }

        [Fact]
        public void HsmTerminal_SecondTick_SameInstanceId_DoesNotRepublish()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int behaviorId = 9101;
            var blob = BuildFinalStateBlob(0xBEEF0002);
            registry.Register(behaviorId, "FinalDoc2", new BehaviorDefinition
            {
                Name          = "FinalDoc2",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });
            var sys = new HsmTickSystem<BrainHsm64>(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob, instanceId: 0);

            // Frame 1: event published, Terminated cleared by system.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            int frame1Count = CountEventsForEntity(world, e);

            // Frame 2: same InstanceId -- must NOT re-publish.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            int frame2Count = CountEventsForEntity(world, e);

            Assert.Equal(1, frame1Count);
            Assert.Equal(0, frame2Count);

            world.Dispose();
        }

        [Fact]
        public void HsmTerminal_NewInstanceId_PublishesNewEvent()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int behaviorId = 9102;
            var blob = BuildFinalStateBlob(0xBEEF0003);
            registry.Register(behaviorId, "FinalDoc3", new BehaviorDefinition
            {
                Name          = "FinalDoc3",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });
            var sys = new HsmTickSystem<BrainHsm64>(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob, instanceId: 0);

            // Frame 1: initial behavior terminates -- event fires.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountEventsForEntity(world, e));

            // Simulate behavior re-assignment: bump InstanceId and re-initialise HSM.
            ref var behavior = ref world.GetComponentRW<BehaviorState>(e);
            unchecked { behavior.InstanceId++; }
            ref var brain = ref world.GetComponentRW<BrainHsm64>(e);
            brain.State.Header.MachineId    = blob.Header.StructureHash;
            brain.State.Header.Phase        = InstancePhase.Entry;
            brain.State.ActiveLeafIds[0]    = 0xFFFF;

            // Frame 2: new InstanceId, machine re-enters final state -- new event.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountEventsForEntity(world, e));

            world.Dispose();
        }

        [Fact]
        public void HsmTerminal_DestroyedEntity_PrunedFromTrackingDict()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int behaviorId = 9103;
            var blob = BuildFinalStateBlob(0xBEEF0004);
            registry.Register(behaviorId, "FinalDoc4", new BehaviorDefinition
            {
                Name          = "FinalDoc4",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });
            var sys = new HsmTickSystem<BrainHsm64>(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob, instanceId: 0);

            // Frame 1: entity terminates -- system starts tracking it.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountEventsForEntity(world, e));
            Assert.Equal(1, sys.TrackedEntityCount);

            // Remove the component so the entity is no longer in the query.
            world.RemoveComponent<BrainHsm64>(e);

            // Frame 2: entity not seen -- stale entry pruned.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(0, sys.TrackedEntityCount);

            world.Dispose();
        }

        // ---- BHU-009 Tests ----

        [Fact]
        public void HsmInterruptInject_BlackboardByte126Set_EnqueuesEvent()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int behaviorId = 9200;
            var blob = BuildTwoStateBlob(0xAA001122);
            registry.Register(behaviorId, "TwoStateDoc1", new BehaviorDefinition
            {
                Name          = "TwoStateDoc1",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });
            var sys = new HsmTickSystem<BrainHsm64>(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob);
            world.AddComponent(e, new BrainBlackboard());

            // Signal interrupt: set byte 126.
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(e);
            bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] = 1;

            sys.Execute(world, 0.016f);

            // Event is enqueued before HsmKernel.Update (which only advances one phase from Entry).
            // After Init: Phase=Activity, event still in queue.
            BrainHsm64 brainCopy = world.GetComponent<BrainHsm64>(e);
            int queueCount = HsmEventQueue.GetCount(&brainCopy);
            Assert.True(queueCount > 0,
                "MobilityLost event must be enqueued into the HSM when blackboard byte 126 is set.");

            world.Dispose();
        }

        [Fact]
        public void HsmInterruptInject_BlackboardByte126Clear_NoEventEnqueued()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int behaviorId = 9201;
            var blob = BuildTwoStateBlob(0xAA003344);
            registry.Register(behaviorId, "TwoStateDoc2", new BehaviorDefinition
            {
                Name          = "TwoStateDoc2",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });
            var sys = new HsmTickSystem<BrainHsm64>(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob);
            world.AddComponent(e, new BrainBlackboard());
            // byte 126 is 0 by default.

            sys.Execute(world, 0.016f);

            BrainHsm64 brainCopy = world.GetComponent<BrainHsm64>(e);
            int queueCount = HsmEventQueue.GetCount(&brainCopy);
            Assert.Equal(0, queueCount);

            world.Dispose();
        }
    }
}
