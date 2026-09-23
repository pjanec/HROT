using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// BHU-007: terminal-state detection and <see cref="BehaviorFinishedEvent"/> publication.
    /// BHU-009: interrupt-inject path (blackboard byte 126 -> MobilityLost enqueue).
    /// </summary>
    public unsafe class BrainTickSystemHsmArmTests
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
            // ⭐⭐ O7c-④b: the instance lives in an OCCURRENCE SLOT, sized by SelectTier(blob).
            //   EnsureRootInstance provisions the store, attaches the slot and stamps MachineId +
            //   Phase=Entry + 0xFFFF leaves — which is exactly the three lines this replaces.
            Assert.True(RootHsmAccess.EnsureRootInstance(world, e, behaviorId, blob));
            return e;
        }

        /// <summary>The instance pointer and its size, asserting the slot is there.</summary>
        private static byte* InstanceOf(EntityRepository world, Entity e, out int size)
        {
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* p, out size));
            return p;
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
            var sys = new BrainTickSystem(registry);
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
            var sys = new BrainTickSystem(registry);
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
            var sys = new BrainTickSystem(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob, instanceId: 0);

            // Frame 1: initial behavior terminates -- event fires.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountEventsForEntity(world, e));

            // Simulate behavior re-assignment: bump InstanceId and re-initialise HSM.
            ref var behavior = ref world.GetComponentRW<BehaviorState>(e);
            unchecked { behavior.InstanceId++; }
            // ⭐ O7c-④b: re-bind through the slot. This is what BehaviorIngressSystem does on a
            //   real re-assign (RootHsmAccess.ResetInstance), spelled out here so the rail keeps
            //   testing the DEDUP rather than the ingress path.
            Assert.True(RootHsmAccess.ResetInstance(world, e, blob));

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
            var sys = new BrainTickSystem(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob, instanceId: 0);

            // Frame 1: entity terminates -- system starts tracking it.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, CountEventsForEntity(world, e));
            Assert.Equal(1, sys.TrackedEntityCount);

            // ⭐⭐⭐ O7c-④b — "NO LONGER IN THE WALK", RESTATED IN SLOT TERMS. 📄 §31.14.6.
            //   ⛔ This used to remove BrainHsm128. There is no brain component any more, so the
            //   sweep's premise had to be restated: an entity leaves the walk when its STORE goes.
            //   ⚠ That is not a weaker claim — it is the SAME one at the level where it can still be
            //   violated, and the sweep now protects the BTree arm too, which never had it.
            BlueprintTierTable.Of(world, e)!.Remove(world, e);

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
            var sys = new BrainTickSystem(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob);
            world.AddComponent(e, new BrainInterrupts());

            // Signal interrupt: set Interrupt_MobilityLost.
            ref var bb = ref world.GetComponentRW<BrainInterrupts>(e);
            bb.Interrupt_MobilityLost = 1;

            sys.Execute(world, 0.016f);

            // Event is enqueued before HsmKernel.Update (which only advances one phase from Entry).
            // After Init: Phase=Activity, event still in queue.
            byte* inst = InstanceOf(world, e, out int instSize);
            int queueCount = HsmEventQueue.GetCount(inst, instSize);
            Assert.True(queueCount > 0,
                "MobilityLost event must be enqueued into the HSM when blackboard byte 126 is set.");

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7_R58</c> — <c>MobilityLost</c> LANDS EVEN WHEN THE NORMAL RING IS FULL.</b>
        /// 📄 <c>CE-324</c> / §31.23.
        ///
        /// <para>🔴 <b>The defect, and the existing rail could not see it.</b>
        /// <c>HsmInterruptInject_BlackboardByte126Set_EventEnqueued</c> asserts
        /// <c>GetCount(inst) &gt; 0</c> on an EMPTY queue — which passes whichever priority the event
        /// carries. ⛔ <c>EventPriority.Low</c> is <c>0</c>, so
        /// <c>new HsmEvent { EventId = … }</c> built a LOW-priority event and the one interrupt this
        /// system injects went into the SHARED NORMAL/LOW RING rather than the reserved interrupt
        /// slot that exists for it.</para>
        ///
        /// <para>⛔⛔ <b>On the 128 tier that ring holds exactly ONE event</b>
        /// (<c>HsmEventQueue.Tier2_Ring_Capacity = 1</c>). ⇒ a single queued normal event was enough
        /// to make the vehicle-disabled interrupt fail to enqueue — and the call site DISCARDED the
        /// <c>false</c> that said so. ⭐ <b>Filling the ring first is the whole point of this rail:</b>
        /// it is the only arrangement in which the two priorities behave differently.</para>
        ///
        /// <para>⚠ The machine is a 2-region blob so <c>SelectTier</c> answers <b>128</b> — asserted,
        /// because on the 256 tier the ring holds 5 and the rail would be vacuous.</para>
        /// </summary>
        [Fact]
        public void O7_R58_MobilityLostLandsWhenTheNormalRingIsFull_CE324()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int behaviorId = 9324;

            var blob = BuildTwoStateBlobWithRegions(0xCE324000, regionCount: 2);
            Assert.Equal(128, HsmInstanceManager.SelectTier(blob));   // the premise, not the claim

            registry.Register(behaviorId, "Ce324Doc", new BehaviorDefinition
            {
                Name          = "Ce324Doc",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });

            var sys = new BrainTickSystem(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob);
            world.AddComponent(e, new BrainInterrupts());

            byte* inst = InstanceOf(world, e, out int instSize);

            // ⛔ FILL THE NORMAL RING. One event is its entire capacity on this tier.
            Assert.True(HsmEventQueue.TryEnqueue(
                inst, instSize, new HsmEvent { EventId = 7001, Priority = EventPriority.Normal }));
            Assert.False(HsmEventQueue.TryEnqueue(
                inst, instSize, new HsmEvent { EventId = 7002, Priority = EventPriority.Normal }),
                "the premise is that the normal ring is FULL at one event on the 128 tier");

            int before = HsmEventQueue.GetCount(inst, instSize);

            ref var bb = ref world.GetComponentRW<BrainInterrupts>(e);
            bb.Interrupt_MobilityLost = 1;

            sys.Execute(world, 0.016f);

            // ⭐⭐ THE RAIL: the interrupt got in anyway, via the reserved slot.
            //   🔴 RED before CE-324: the Low-priority event found the ring full, TryEnqueue returned
            //     false, the call site threw that away, and the count never moved.
            byte* after = InstanceOf(world, e, out int afterSize);
            Assert.True(HsmEventQueue.GetCount(after, afterSize) > before,
                "MobilityLost must reach the RESERVED interrupt slot, which normal traffic cannot " +
                "crowd out — that is the guarantee the reserved slot exists to give.");
        }

        /// <summary>
        /// ⭐ <b><c>O7_R59</c> — the event this system injects carries <c>Interrupt</c> priority.</b>
        /// ⛔ Stated directly, because <c>O7_R58</c> observes the CONSEQUENCE and a future refactor
        /// could satisfy it by widening the ring instead. ⚠ <c>EventPriority.Low</c> is <c>0</c>, so
        /// the defect was invisible: an un-set <c>Priority</c> field IS a valid priority.
        /// </summary>
        [Fact]
        public void O7_R59_TheInjectedMobilityLostEventIsAnInterrupt_CE324()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            const int behaviorId = 9325;

            var blob = BuildTwoStateBlobWithRegions(0xCE325000, regionCount: 2);
            registry.Register(behaviorId, "Ce325Doc", new BehaviorDefinition
            {
                Name          = "Ce325Doc",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });

            var sys = new BrainTickSystem(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob);
            world.AddComponent(e, new BrainInterrupts());
            world.GetComponentRW<BrainInterrupts>(e).Interrupt_MobilityLost = 1;

            sys.Execute(world, 0.016f);

            // The reserved interrupt slot is the FIRST 24 bytes of the event buffer, and
            // InterruptSlotUsed is the byte that says it is occupied.
            byte* inst = InstanceOf(world, e, out int instSize);
            Assert.Equal(128, instSize);
            var state = *(HsmInstance128*)inst;

            Assert.Equal(1, (int)state.InterruptSlotUsed);
            Assert.Equal(0, (int)state.EventCount);   // ⛔ NOT in the normal ring
            Assert.Equal(BehaviorConstants.EventId_MobilityLost,
                         ((HsmEvent*)state.EventBuffer)->EventId);
        }

        /// <summary>A two-state MobilityLost blob whose region count drives <c>SelectTier</c>.</summary>
        private static HsmDefinitionBlob BuildTwoStateBlobWithRegions(uint hash, int regionCount)
        {
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1 };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF };

            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef
            {
                SourceStateIndex = 0,
                TargetStateIndex = 1,
                EventId          = BehaviorConstants.EventId_MobilityLost,
            };

            var header = new HsmDefinitionHeader
            {
                StructureHash = hash, StateCount = 2, TransitionCount = 1,
                RegionCount   = (ushort)regionCount,
            };

            return new HsmDefinitionBlob(
                header, states, transitions, new RegionDef[regionCount],
                Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
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
            var sys = new BrainTickSystem(registry);
            var e   = CreateHsmEntity(world, behaviorId, blob);
            world.AddComponent(e, new BrainInterrupts());
            // byte 126 is 0 by default.

            sys.Execute(world, 0.016f);

            byte* inst = InstanceOf(world, e, out int instSize);
            int queueCount = HsmEventQueue.GetCount(inst, instSize);
            Assert.Equal(0, queueCount);

            world.Dispose();
        }
    }
}
