using System;
using System.Collections.Generic;
using System.Reflection;
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

            // Frame 1: event published; CE-449 — the behaviour is cleared at finish.
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

            // Simulate behavior re-assignment: restore the behaviour, bump InstanceId and re-attach the HSM.
            // ⭐ CE-449: the finish CLEARED the behaviour (hash None, tier 0, instance slot detached), so a re-assign
            //   must restore all three — exactly what BehaviorIngressSystem does. Spelled out here so the rail keeps
            //   testing the DEDUP rather than the ingress path.
            ref var behavior = ref world.GetComponentRW<BehaviorState>(e);
            Assert.Equal(0, behavior.BrainTier);           // CE-449: cleared at finish
            behavior.ActiveBehaviorHash = behaviorId;
            behavior.BrainTier          = BehaviorConstants.BrainTierHsm;
            unchecked { behavior.InstanceId++; }
            Assert.True(RootHsmAccess.EnsureRootInstance(world, e, behaviorId, blob));
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

        // ════════════════════════════════════════════════════════════════════════════════
        // ⭐⭐⭐ CE-398 / ACCEPTANCE RAIL ⑨ — THE WHOLE MECHANISM, END TO END
        // 📄 DESIGN_Hsm_Blueprint_Behaviour_Authoring.md §9 ⑨.
        // ════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐⭐ Registers EVERYTHING the way hot reload does: <see cref="BlueprintRegistrarScanner"/>
        /// over the real <c>Hrot.AI.Behaviors</c> assembly.
        ///
        /// <para>⛔ Deliberately NOT "reflect the one generated type and register its thunk by hand".
        /// The scanner is the PRODUCTION discover-and-invoke path, so this fixture exercises the same
        /// registration the game performs — including <c>HsmGuardDemo</c>'s
        /// <c>HsmActionDispatcher.RegisterGuard</c> and <c>HsmPolledGuardDemo</c>'s behaviour
        /// definition and <c>HsmParamBindings</c> table.</para>
        /// </summary>
        private static BehaviorRegistry ScanTheRealBehavioursAssembly()
        {
            var behaviours = new BehaviorRegistry();
            Fdp.Toolkit.Blueprints.BlueprintRegistrarScanner.Scan(
                typeof(global::Hrot.AI.Behaviors.Machines.HsmPolledGuardDemo).Assembly,
                new Fdp.Toolkit.Blueprints.BlueprintRegistry().BeginStaging(),
                behaviours);
            return behaviours;
        }

        /// <summary>
        /// Writes the blackboard byte the guard's <c>Open</c> parameter is seeded from.
        ///
        /// <para>⛔⛔ <b>READS the slot ingress already attached — it must NOT attach one.</b>
        /// 🔴 Measured: an earlier draft called <c>ResolveOrAttachRoot(..., MaxBehaviorParamByteSize,
        /// ...)</c> and attached a slot of its OWN, far wider than the asset needs, on a tier ingress
        /// had sized without it ⇒ the guard's occurrence then had nowhere to go. ⚠ A fixture that
        /// provisions differently from production is testing a world that does not exist.</para>
        /// </summary>
        private static void SeedOpen(EntityRepository world, Entity e, bool open)
        {
            byte* p = RootParamsAccess.RequireRootBytes(world, e, out int len);
            Assert.True(p != null && len > 0,
                "ingress must have attached the root params slot — the behaviour declares a managed "
              + "blackboard, so its generated registrar supplies ParseParams and RootParamsCost "
              + "reserves the slot");
            // ⚠ Offset 0: `Open` is the asset's only packed variable, and the Tier1 golden records
            //   the guard's own `Open : System.Boolean @0 size=1`. The state's HsmParamBindings entry
            //   (CE-387) is what makes the seed come from HERE.
            *p = open ? (byte)1 : (byte)0;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The PRODUCTION assign path, not a hand-provisioned entity.</b>
        ///
        /// <para>🔴 <b>Measured the hard way:</b> the first draft created the entity and called
        /// <c>RootHsmAccess.EnsureRootInstance</c> directly, and the guard's own occurrence had
        /// nowhere to live — <i>"the tier is full"</i>. ⛔ That was the FIXTURE's fault, not the
        /// product's: <c>HostedOccurrenceDemandCalculator.For</c> already counts
        /// <c>trans.GuardId</c> (<c>:103</c>), and <c>BehaviorIngressSystem</c> reads that demand to
        /// size the tier. ⇒ bypassing ingress bypassed the very sizing the feature depends on.</para>
        ///
        /// <para>⭐ So this publishes an <c>AssignBehaviorEvent</c> and lets ingress provision, which
        /// is both correct AND a stronger rail: it now covers the SIZING of a blueprint-guarded
        /// machine's tier, which nothing else did.</para>
        /// </summary>
        private static (EntityRepository world, BrainTickSystem sys, Entity e, int hash)
            ArrangePolledGuardMachine(bool open)
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);

            var behaviours = ScanTheRealBehavioursAssembly();
            Assert.True(behaviours.TryGetId("HsmPolledGuardDemo", out int hash),
                "the generated HsmPolledGuardDemoRegistrar must have registered the behaviour");

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());

            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = "HsmPolledGuardDemo",
                JsonParams   = string.Empty,
            });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(behaviours).Execute(world, 0.016f);

            // ⛔ NON-VACUITY: ingress must actually have activated the brain. Without this a
            //    mis-registered behaviour would leave the entity inert and every assertion below
            //    would pass for the wrong reason.
            ref var st = ref world.GetComponentRW<BehaviorState>(e);
            Assert.Equal(hash, st.ActiveBehaviorHash);
            Assert.Equal(BehaviorConstants.BrainTierHsm, st.BrainTier);

            SeedOpen(world, e, open);

            return (world, new BrainTickSystem(behaviours), e, hash);
        }

        /// <summary>
        /// 🔴🔴🔴 <b>RAIL ⑨ — and it is the END-TO-END proof of the whole programme.</b>
        ///
        /// <para>One entity, the REAL <c>HsmPolledGuardDemo</c> blob, the REAL blueprint-hosted guard
        /// registered by the REAL registrar scanner, ticked by the REAL <see cref="BrainTickSystem"/>.
        /// ⭐ Nothing here is hand-built: the chain is
        /// <b>polled scan → blueprint guard thunk → params seeded from the blackboard → transition →
        /// final state → <see cref="BehaviorFinishedEvent"/></b>, and <b>no event is ever posted</b>.</para>
        ///
        /// <para>⭐⭐ <b>Exactly once</b>, across many ticks — the `BHU-007` latch must not republish
        /// while the instance id is unchanged.</para>
        /// </summary>
        [Fact]
        public void CE398_R1_APolledBlueprintGuard_DrivesTheMachineToItsFinalState_AndFinishesOnce()
        {
            var (world, sys, e, _) = ArrangePolledGuardMachine(open: true);

            // ⚠ Several ticks: the phase machine advances ONE PHASE PER TICK, so Entry/RTC/Activity
            //   must drain before the Idle arm's polled scan can run at all (design §2.3).
            for (int i = 0; i < 12; i++) sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();

            Assert.Equal(1, CountEventsForEntity(world, e));

            world.Dispose();
        }

        /// <summary>
        /// ⛔⛔ <b>THE NON-VACUITY HALF, and it is what makes the rail above mean anything.</b>
        ///
        /// <para>🔴 With <c>Open = false</c> the guard must REFUSE and the machine must sit in
        /// <c>Waiting</c> forever ⇒ <b>zero</b> <see cref="BehaviorFinishedEvent"/>s. ⚠ Without this,
        /// a machine that simply fell through to its final state would pass the rail above and prove
        /// nothing about the guard being consulted at all.</para>
        /// </summary>
        [Fact]
        public void CE398_R2_WithTheGuardClosed_TheMachineNeverFinishes()
        {
            var (world, sys, e, _) = ArrangePolledGuardMachine(open: false);

            for (int i = 0; i < 12; i++) sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();

            Assert.Equal(0, CountEventsForEntity(world, e));

            world.Dispose();
        }

        // ══ CE-417 B-2 (a′) — ONE generated call PER BINDING for a C# [SharedAiAction] ═════════════════════════
        //   📄 docs/blueprints/DESIGN_Behavior_Action_Binding.md §4 B-2, F4/F7. The REAL HsmCuratedBindingDemo through the
        //   REAL registrar scan, ingress and BrainTickSystem: region zero's activity → varA, region one's → varB, and the
        //   Go transition's action → varC, ONE C# method (Action_ReadRegionParams, which counts `Seen` in place).

        // ⚠ Packed order is varC, varA, varB — varC FIRST so the pre-CE-417 key (attribute offset 0) would have resolved
        //   for the transition action and added its source state's base (varA, 8): the F4 defect, reproducible.
        private const int VarC = 0, VarA = 8, VarB = 16;

        private static (EntityRepository world, BrainTickSystem sys, Entity e) ArrangeCuratedBindingDemo()
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);
            var behaviours = ScanTheRealBehavioursAssembly();
            Assert.True(behaviours.TryGetId("HsmCuratedBindingDemo", out int hash), "the generated registrar must register it");

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = "HsmCuratedBindingDemo", JsonParams = string.Empty });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(behaviours).Execute(world, 0.016f);
            Assert.Equal(hash, world.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash);
            return (world, new BrainTickSystem(behaviours), e);
        }

        private static global::Hrot.AI.Behaviors.Brains.HsmTwoRegionCuratedNodes.CuratedRegionParams Var(EntityRepository w, Entity e, int offset)
        {
            byte* root = RootParamsAccess.RequireRootBytes(w, e, out int len);
            Assert.True(offset + 8 <= len);
            return *(global::Hrot.AI.Behaviors.Brains.HsmTwoRegionCuratedNodes.CuratedRegionParams*)(root + offset);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-417</c> — <b>two parallel regions running ONE C# action, bound to two variables, each move ONLY their own.</b>
        /// <para>Re-homes rail ㊳ (<c>HsmOccurrenceKeyTests.O7_R38</c>), whose subject — the per-METHOD curated thunk and its
        /// occurrence-cached offset — CE-417 retired. ⭐ Here nothing is cached: each binding's call has its offset baked.</para>
        /// </summary>
        [Fact]
        public void CE417_R1_TwoRegionsBoundToTwoVariables_EachCountOnlyTheirOwn()
        {
            var (world, sys, e) = ArrangeCuratedBindingDemo();
            for (int i = 0; i < 12; i++) sys.Execute(world, 0.016f);

            var a = Var(world, e, VarA); var b = Var(world, e, VarB); var c = Var(world, e, VarC);
            Assert.True(a.Seen > 0, "region zero's activity never ran on varA");
            Assert.True(b.Seen > 0, "region one's activity never ran on varB");
            Assert.Equal(a.Seen, b.Seen);   // one call each, every tick
            Assert.Equal(0, c.Seen);        // the transition has not fired
            world.Dispose();
        }

        /// <summary>
        /// 🔴🔴 <c>CE-417</c> <b>F4 — a C# TRANSITION action reads ITS OWN variable, not one offset from its source state's.</b>
        /// <para>Before CE-417 the per-method thunk added <c>SeedParamsOffset(source state)</c> to its field offset, and the
        /// kernel stamps a transition action with its SOURCE state ⇒ bound here (varC at 0, the source bound to varA at 8) it
        /// would have read varA, not varC.
        /// ⭐ Now the call's offset is baked absolute: firing <c>Go</c> moves varC by exactly one and varA not at all.</para>
        /// <para>⚠ Inverse-edit red-proof: emit the thunk with <c>+ HsmOccurrence.SeedParamsOffset(...)</c> and varC stays 0.</para>
        /// </summary>
        [Fact]
        public void CE417_R2_ATransitionAction_TouchesOnlyItsOwnVariable_NotOneOffsetFromItsSourceState()
        {
            var (world, sys, e) = ArrangeCuratedBindingDemo();
            for (int i = 0; i < 12; i++) sys.Execute(world, 0.016f);

            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* inst, out int size));
            Assert.True(HsmEventQueue.TryEnqueue(inst, size, new HsmEvent { EventId = 1, Priority = EventPriority.Normal }));
            var before = Var(world, e, VarA);
            for (int i = 0; i < 6; i++) sys.Execute(world, 0.016f);

            var a = Var(world, e, VarA); var b = Var(world, e, VarB); var c = Var(world, e, VarC);
            Assert.Equal(1, c.Seen);                    // the transition action ran exactly once, on varC
            Assert.True(a.Seen <= before.Seen + 1,      // region zero LEFT its worker state (at most the firing tick's activity)
                $"varA kept counting after the transition ({before.Seen} → {a.Seen})");
            Assert.True(b.Seen > before.Seen, "region one must keep running its own activity on varB");
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-417</c> + <c>CE-505</c> — the per-binding C# calls allocate nothing (F9: the old curated thunk
        /// built a string and searched the occurrence store on every call).</summary>
        [Fact]
        public void CE417_R3_PerBindingCSharpCalls_AllocateNothing()
        {
            var (world, sys, e) = ArrangeCuratedBindingDemo();
            for (int i = 0; i < 24; i++) sys.Execute(world, 0.016f);
            int seenBefore = Var(world, e, VarA).Seen;

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) sys.Execute(world, 0.016f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(seenBefore + 50, Var(world, e, VarA).Seen);   // NON-VACUITY: the call really ran every tick
            Assert.True(allocated == 0, $"50 ticks of two per-binding C# activities allocated {allocated} bytes");
            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-505</c> — <b>a steady-state HSM brain tick allocates NOTHING.</b>
        /// 🔒 User <c>2026-10-01</c>: <i>"there should be no allocation on the hot path."</i>
        ///
        /// <para>The real <c>HsmPolledGuardDemo</c> through the real ingress and <see cref="BrainTickSystem"/>,
        /// guard CLOSED so it is polled every tick: root state/params/HSM-instance lookups plus a blueprint
        /// guard's occurrence key, per tick. ⚠ Warm-up ticks first, so JIT, first attach and the one-off
        /// dictionary growth are outside the measured window.</para>
        /// <para>⚠ Red-proof: the old key folds (<c>Guid.ToByteArray()</c> + <c>Encoding.UTF8.GetBytes</c>).</para>
        /// </summary>
        [Fact]
        public void CE505_R3_ASteadyStateHsmBrainTick_AllocatesNothing()
        {
            var (world, sys, e, _) = ArrangePolledGuardMachine(open: false);

            for (int i = 0; i < 24; i++) sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) sys.Execute(world, 0.016f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // ⛔ NON-VACUITY: the guard really was polled closed the whole time — the machine never finished.
            world.Bus.SwapBuffers();
            Assert.Equal(0, CountEventsForEntity(world, e));
            Assert.True(allocated == 0, $"50 steady-state HSM brain ticks allocated {allocated} bytes");

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-444</c> — <b>a guard reads its host LIVE: a host write mid-run is seen on the next poll.</b>
        /// 📄 <c>DESIGN_Parameter_Model.md</c> §P.3/§P.9 (<c>R-155</c>: an action/condition has no param copy).
        ///
        /// <para>The guard is polled CLOSED for several ticks (its occurrence is attached and its host offset
        /// cached), then the HOST variable is opened. ⭐ The machine must now finish.</para>
        /// <para>⚠ Inverse-edit red-proof: restore the activation-time copy (the old <c>EmitParamSeed</c>) and the
        /// guard keeps its seeded <c>false</c> forever ⇒ zero events.</para>
        /// </summary>
        [Fact]
        public void CE444_R1_APolledGuard_SeesAHostWriteMadeAfterItWasFirstEvaluated()
        {
            var (world, sys, e, _) = ArrangePolledGuardMachine(open: false);

            for (int i = 0; i < 12; i++) sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(0, CountEventsForEntity(world, e));   // evaluated closed — occurrence attached

            SeedOpen(world, e, open: true);                    // the HOST changes mid-run
            for (int i = 0; i < 12; i++) sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();

            Assert.Equal(1, CountEventsForEntity(world, e));

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <b>The transition really is POLLED — nothing posts an event.</b> ⚠ The instance's event
        /// queue stays empty for the whole run, so the transition cannot have been taken by an
        /// event round; the only remaining path is the <c>Idle</c> arm's polled scan (<c>CE-382</c>).
        /// </summary>
        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-399</c> — the reserved tier bytes must cover what the runtime ACTUALLY
        /// attaches for a hosted blueprint.</b>
        ///
        /// <para>🔴 <b>The defect.</b> <c>HostedOccurrenceDemandCalculator.BuildActionIdIndex</c>
        /// indexed <c>def.StateSize</c> alone, while
        /// <c>OccurrenceWorkingState.ResolveOrAttach&lt;TParams, TWorkingState&gt;</c> attaches
        /// <c>AlignedBytes(sizeof(TWorkingState)) + sizeof(TParams)</c> (<c>:121</c>, <c>:146</c>).
        /// ⇒ the params bytes were never reserved, and the raw state size was summed UNALIGNED.
        /// 📌 Measured on this guard: reserved <b>8</b>, needed <b>9</b>.</para>
        ///
        /// <para>⚠⚠ <b>SAID PLAINLY: this is NOT what unblocked rail ⑨.</b> A red-proof reverting the
        /// fix left <c>CE398_R1</c> GREEN — that entity's tier had slack once the fixture stopped
        /// attaching an oversized root-params slot of its own. ⇒ <c>CE-399</c> is a CORRECTNESS fix
        /// with its own rail, not a prerequisite of rail ⑨, and claiming otherwise would have been a
        /// false attribution. ⛔ The slack is not a guarantee: a machine hosting several occurrences
        /// exhausts it, and the failure mode is a hard throw on the first tick.</para>
        ///
        /// <para>⭐ Both sides are READ, never recomputed by hand: the left from the production
        /// calculator over the real registrar's staging, the right from the generated blueprint
        /// class's own <c>StateSize</c>/<c>ParamsSize</c>.</para>
        /// </summary>
        [Fact]
        public void CE399_R1_TheHostedDemandCoversWhatTheRuntimeActuallyAttaches()
        {
            var behaviours = new BehaviorRegistry();
            var staging    = new Fdp.Toolkit.Blueprints.BlueprintRegistry().BeginStaging();
            Fdp.Toolkit.Blueprints.BlueprintRegistrarScanner.Scan(
                typeof(global::Hrot.AI.Behaviors.Machines.HsmPolledGuardDemo).Assembly,
                staging, behaviours);

            Assert.True(behaviours.TryGetHostedOccurrenceDemand("HsmPolledGuardDemo", out var demand));
            Assert.NotNull(demand);
            Assert.Equal(1, demand!.SlotCount);

            var bp = typeof(global::Hrot.AI.Behaviors.Generated.HsmGuardDemo_C2A38D0D_Bp);
            int stateSize  = (int)bp.GetProperty("StateSize")!.GetValue(null)!;
            int paramsSize = (int)bp.GetProperty("ParamsSize")!.GetValue(null)!;

            int needed = OccurrenceWorkingState.AlignedBytes(stateSize) + paramsSize;
            Assert.True(paramsSize > 0,
                "the guard declares an Open parameter — a 0 here means the registration lost it");
            Assert.True(demand.PayloadBytes >= needed,
                $"the tier reserves {demand.PayloadBytes} bytes for this hosted occurrence but the "
              + $"runtime attaches {needed} ({stateSize} state, aligned, + {paramsSize} params)");
        }

        // ════════════════════════════════════════════════════════════════════════════════
        // ⭐⭐⭐ CE-400 / ACCEPTANCE RAIL ⑥ — ONE ACTION PER CHANNEL, IN PARALLEL
        // 🔒 The user's words: "multiple actions in parallel, one per channel like one action for
        //    movement, one for weapon control." 📄 design §9 ⑥.
        // ════════════════════════════════════════════════════════════════════════════════

        private static (EntityRepository world, BrainTickSystem sys, Entity e)
            ArrangeTwoChannelRegions()
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);

            var behaviours = ScanTheRealBehavioursAssembly();
            // ⭐ The curated registrar in Hrot.AI.Behaviors registers the HAND-WRITTEN [HsmAction]
            //   thunks; without it both activities are TryGetValue misses and nothing writes.
            var hsmRegistrar = typeof(global::Hrot.AI.Behaviors.Machines.HsmShowcase).Assembly
                .GetType("Hrot.AI.Behaviors.Generated.HsmActionRegistrar")!;
            hsmRegistrar.GetMethod("RegisterAll",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null);

            Assert.True(behaviours.TryGetId("HsmTwoChannelRegionsDemo", out _));

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.AddComponent(e, new LocomotionChannel());
            world.AddComponent(e, new WeaponChannel());

            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = "HsmTwoChannelRegionsDemo",
                JsonParams   = string.Empty,
            });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(behaviours).Execute(world, 0.016f);

            return (world, new BrainTickSystem(behaviours), e);
        }

        /// <summary>
        /// 🔴🔴🔴 <b>RAIL ⑥ — two orthogonal regions, two DIFFERENT command channels, both live.</b>
        ///
        /// <para>⭐ Region 0's activity writes <see cref="LocomotionChannel"/>, region 1's writes
        /// <see cref="WeaponChannel"/>, and both land from ONE machine on ONE entity.</para>
        ///
        /// <para>⛔⛔ <b>"Both ran" is NOT the claim — "both tick EVERY FRAME" is.</b> A channel write
        /// is idempotent, so a single write and a per-frame write are indistinguishable by the
        /// channel's contents. ⇒ each activity bumps its own <c>ActionInstanceId</c>, and this asserts
        /// the counters ADVANCE TOGETHER across successive ticks.</para>
        /// </summary>
        [Fact]
        public void CE400_R1_TwoParallelRegions_DriveTwoDifferentChannels_EveryFrame()
        {
            var (world, sys, e) = ArrangeTwoChannelRegions();

            // Drain Entry/RTC so both regions reach their Activity phase (one phase per tick).
            for (int i = 0; i < 6; i++) sys.Execute(world, 0.016f);

            uint loco0 = world.GetComponent<LocomotionChannel>(e).ActionInstanceId;
            uint weap0 = world.GetComponent<WeaponChannel>(e).ActionInstanceId;

            Assert.True(loco0 > 0, "region 0's activity never wrote the locomotion channel");
            Assert.True(weap0 > 0, "region 1's activity never wrote the weapon channel");

            // ⭐ The channels carry DIFFERENT actions — one machine, two independent outputs.
            Assert.Equal(global::Hrot.AI.Behaviors.Brains.HsmChannelRegionNodes.ActionIdDrive,
                         world.GetComponent<LocomotionChannel>(e).ActiveAction);
            Assert.Equal(global::Hrot.AI.Behaviors.Brains.HsmChannelRegionNodes.ActionIdFire,
                         world.GetComponent<WeaponChannel>(e).ActiveAction);

            // ⭐⭐ …and BOTH keep advancing, in step, on every subsequent tick.
            for (int i = 0; i < 4; i++) sys.Execute(world, 0.016f);

            uint loco1 = world.GetComponent<LocomotionChannel>(e).ActionInstanceId;
            uint weap1 = world.GetComponent<WeaponChannel>(e).ActionInstanceId;

            Assert.True(loco1 > loco0, "the locomotion region stopped ticking");
            Assert.True(weap1 > weap0, "the weapon region stopped ticking");
            Assert.Equal(loco1 - loco0, weap1 - weap0);

            world.Dispose();
        }

        // ── CE-403 — the channel-safety chain finally has a subscriber ──────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE403_R1</c> — the two channel-writing HSM activities DECLARE their channels,
        /// so <c>RequiredExitCleanups</c> is non-empty and the cleanup thunks exist.</b>
        /// 📄 <c>Architect_Question_74_Blueprint_Channel_Lifecycle.md</c> §4 <c>D-A2</c>.
        ///
        /// <para>🔴 <b>What this pins.</b> 📐 Measured `2026-09-28`: <c>[WritesChannel]</c> had
        /// <b>TWO applications repo-wide and both were inside a unit test OF the attribute</b> —
        /// zero in production — so this dictionary was <b>always empty</b> and the whole
        /// <c>[WritesChannel]</c> → <c>RequiredExitCleanups</c> → <c>ValidateChannelSafety</c> chain
        /// was generated, shipped and inert. <c>HsmTwoChannelRegionsDemo</c> leaked both channels on
        /// state exit, unreported.</para>
        ///
        /// <para>⚠ <b>This rail asserts ADOPTION, not cleanup.</b> The cleanup does not yet HAPPEN —
        /// that is <c>CE-388</c> / <c>D-B1</c>, because the chain has two further breaks this rail
        /// deliberately does not paper over: <c>ValidateChannelSafety</c> has no production caller,
        /// and its key shape is wrong (see <see cref="CE403_R2_TheCleanupTableIsKeyedOnTheSHORTName_NotTheFqn"/>).</para>
        ///
        /// <para>✅ <b>Red-proof:</b> remove either <c>[WritesChannel]</c> ⇒ the entry disappears.</para>
        /// </summary>
        [Fact]
        public void CE403_R1_TheChannelWritingHsmActivitiesDeclareTheirChannels()
        {
            var map = RequiredExitCleanups();

            Assert.Contains("Activity_DriveChannel", map.Keys);
            Assert.Contains("Activity_FireChannel",  map.Keys);

            // ⭐ and the cleanup each names is a real registered thunk, not a dangling string.
            var registrar = typeof(global::Hrot.AI.Behaviors.Machines.HsmPolledGuardDemo).Assembly
                .GetType("Hrot.AI.Behaviors.Generated.HsmActionRegistrar")!;
            foreach (var cleanup in map.Values)
                Assert.True(
                    registrar.GetMethod(cleanup, BindingFlags.NonPublic | BindingFlags.Static) != null,
                    $"'{cleanup}' is named by RequiredExitCleanups but no such thunk was emitted");
        }

        /// <summary>
        /// ⛔⛔ <b><c>CE403_R2</c> — CHARACTERISATION: the cleanup table is keyed on the SHORT method
        /// name, while an HSM asset names its activity by FQN.</b>
        ///
        /// <para>🔴 <b>This is break ③ of three</b>, and it is why populating the table changed
        /// nothing: <c>HsmActionGenerator.cs:565</c> emits <c>m.Name</c>, so the key is
        /// <c>"Activity_DriveChannel"</c> — but <c>HsmTwoChannelRegionsDemo.hsm.json</c> holds
        /// <c>"Hrot.AI.Behaviors.Brains.HsmChannelRegionNodes.Activity_DriveChannel"</c> ⇒
        /// <c>ValidateChannelSafety</c>'s <c>ContainsKey(state.ActivityAction)</c> would MISS even if
        /// it were called (it is not — break ② — its only callers are its own unit tests).</para>
        ///
        /// <para>⭐⭐ <b>Why a rail rather than a fix.</b> <c>CE-388</c>/<c>D-B1</c> has the flattener
        /// bind the cleanup by ID, and the generator registers it under
        /// <c>HsmActionKey.ForExitCleanup(m.Name)</c> — the SHORT name. ⛔ A flattener that derives
        /// the id from the FQN will compute a hash nothing is registered under and bind a
        /// <b>non-existent action</b>, silently. 🔒 This rail is the coupling made explicit so that
        /// mistake cannot be made quietly.</para>
        /// </summary>
        [Fact]
        public void CE403_R2_TheCleanupTableIsKeyedOnTheSHORTName_NotTheFqn()
        {
            var keys = RequiredExitCleanups().Keys;

            Assert.Contains("Activity_DriveChannel", keys);
            Assert.DoesNotContain(
                "Hrot.AI.Behaviors.Brains.HsmChannelRegionNodes.Activity_DriveChannel", keys);
        }

        /// <summary>
        /// 🔴🔴🔴 <b><c>CE388_R8</c> — THE PARITY RAIL: the exit-cleanup id BAKED INTO THE BLOB equals
        /// the id the generated registrar REGISTERED.</b> 📄 <c>Q74</c> §9.4 (<c>D-F</c>), §4 <c>D-D1</c>.
        ///
        /// <para>⭐⭐⭐ <b>Both sides are read from ARTEFACTS</b>, never recomputed here — the same
        /// discipline as acceptance rail ④ (<c>HsmBlueprintGuardIdAgreementTests</c>), and for the
        /// same reason: an id this test derives itself would agree with a wrong emitter.</para>
        ///
        /// <para>🔒 <b>This is the rail that makes the shared formula checkable.</b> The user ruled
        /// <i>"no duplicating the HsmActionKey formula, must be shared"</i>; <c>HsmEmitCore</c> hashes
        /// the short method name to bake <c>.OnExitId(n)</c> and <c>HsmActionGenerator</c> hashes it
        /// to register <c>ExitCleanup_*</c>. ⛔ If those ever diverge the state binds an action
        /// nothing registered, and the ONLY symptom is a channel that is never released — no
        /// exception, no diagnostic. 📌 <c>CE-403</c> measured exactly that shape as break ③.</para>
        ///
        /// <para>⚠ <b>And it is the only place this can be caught.</b> The GOLDEN corpus cannot:
        /// <c>AiAssetCorpus</c> has no Roslyn compilation, so it cannot answer "does this action
        /// declare <c>[WritesChannel]</c>" and emits no <c>OnExitId</c> at all. ⇒ a regression in
        /// <c>D-D1</c> would move ZERO baselines.</para>
        /// </summary>
        [Fact]
        public void CE388_R8_TheBakedExitCleanupId_EqualsTheIdTheRegistrarRegistered()
        {
            var asm = typeof(global::Hrot.AI.Behaviors.Machines.HsmShowcase).Assembly;

            // ── LEFT: what the generated registrar REGISTERED, by name ────────────────────
            var map = RequiredExitCleanups();
            Assert.Contains("Activity_DriveChannel", map.Keys);
            Assert.Contains("Activity_FireChannel",  map.Keys);

            var registrar = asm.GetType("Hrot.AI.Behaviors.Generated.HsmActionRegistrar")!;
            registrar.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static)!
                     .Invoke(null, null);

            var actionTable = (System.Collections.IDictionary)typeof(Fhsm.Kernel.HsmActionDispatcher)
                .GetField("ActionTable", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

            // ── RIGHT: what the COMPILED BLOB bound as each state's exit action ───────────
            // ⭐ The GRAPH the generated CreateBuilder() produced — i.e. exactly what HsmEmitCore
            //   wrote as `.OnExitId(n)`. ⛔ Read from the artefact, never recomputed here.
            var graph = global::Hrot.AI.Behaviors.Machines.HsmTwoChannelRegionsDemo.CreateBuilder().Build();

            var bound = new List<ushort>();
            foreach (var state in graph.States.Values)
                if (state.ExitActionId != 0 && state.ExitActionId != 0xFFFF)
                    bound.Add(state.ExitActionId);

            // ⭐⭐ The claim: the blob bound TWO cleanups it never authored — one per channel-writing
            //    activity — and every one of them is a key the registrar actually registered.
            Assert.Equal(2, bound.Count);
            foreach (ushort id in bound)
                Assert.True(actionTable.Contains(id),
                    $"the blob binds OnExitActionId {id}, which NOTHING registered — the two sides "
                    + "of the shared HsmActionKey have diverged (Q74 D-F)");
        }

        /// <summary>The REAL generated registrar's cleanup table, read from the game assembly.</summary>
        private static IReadOnlyDictionary<string, string> RequiredExitCleanups()
        {
            var registrar = typeof(global::Hrot.AI.Behaviors.Machines.HsmPolledGuardDemo).Assembly
                .GetType("Hrot.AI.Behaviors.Generated.HsmActionRegistrar")
                ?? throw new InvalidOperationException("the generated HsmActionRegistrar is missing");

            var field = registrar.GetField("RequiredExitCleanups", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("RequiredExitCleanups is missing from the registrar");

            return (IReadOnlyDictionary<string, string>)field.GetValue(null)!;
        }

        [Fact]
        public void CE398_R3_NoEventIsEverQueued_SoTheTransitionCanOnlyHaveBeenPolled()
        {
            var (world, sys, e, _) = ArrangePolledGuardMachine(open: true);

            int aliveFrames = 0;
            for (int i = 0; i < 12; i++)
            {
                sys.Execute(world, 0.016f);
                // ⭐ CE-449: once the machine reaches its final state it FINISHES and is cleared — its instance is
                //   freed, so the claim holds for every frame it is alive.
                if (!RootHsmAccess.TryGetInstance(world, e, out byte* inst, out int size)) break;
                aliveFrames++;
                Assert.Equal(0, HsmEventQueue.GetCount(inst, size));
            }
            Assert.True(aliveFrames > 0, "the machine must have run at least one frame");

            world.Dispose();
        }
    }
}
