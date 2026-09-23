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
    /// Tests for BHU-016: <see cref="BehaviorIngressSystem"/> resets HSM instance state
    /// on behavior assignment so the new behavior always starts clean.
    /// </summary>
    public unsafe class BehaviorIngressSystemHsmResetTests
    {
        private static (EntityRepository world, BehaviorIngressSystem sys, BehaviorRegistry registry)
            CreateFixture()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            var sys      = new BehaviorIngressSystem(registry);
            return (world, sys, registry);
        }

        /// <summary>
        /// Helper: publish an AssignBehaviorEvent, swap buffers, then run the ingress system.
        /// </summary>
        private static void AssignBehavior(
            EntityRepository world,
            BehaviorIngressSystem sys,
            Entity entity,
            string behaviorName)
        {
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = entity,
                BehaviorName = behaviorName,
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        // Helper: publish an AssignBehaviorHashEvent, swap buffers, then run the ingress system.
        private static void AssignBehaviorHash(
            EntityRepository world,
            BehaviorIngressSystem sys,
            Entity entity,
            int behaviorHash)
        {
            world.Bus.Publish(new AssignBehaviorHashEvent
            {
                Entity       = entity,
                BehaviorHash = behaviorHash,
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        // Helper: build a minimal single-state blob with the given StructureHash.
        // StateCount=1 with no transitions -- kernel advances Entry->Idle on empty queue.
        private static HsmDefinitionBlob BuildMinimalBlob(uint structureHash)
        {
            var states = new StateDef[1];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0 };
            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 1, TransitionCount = 0 };
            return new HsmDefinitionBlob(
                header,
                states,
                Array.Empty<TransitionDef>(),
                Array.Empty<RegionDef>(),
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>());
        }

        // ---- Tests ----

        [Fact]
        public void BehaviorIngress_HsmReset_ClearsTerminatedFlagAndLeavesTheMachineReadyToEnter()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "HsmResetDoc";
            registry.Register(9300, behaviorName, new BehaviorDefinition
            {
                Name          = behaviorName,
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = BuildMinimalBlob(0x9300),
            });

            var e = world.CreateEntity();
            // ⚠ ActiveBehaviorHash MUST be stamped: every root key is COMPUTED from it, so a
            //   BehaviorState left at 0 makes TryGetInstance return false however well the slot was
            //   attached. Same ordering trap RootStateAccess.EnsureRootState's doc records.
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = 9300, BrainTier = BehaviorConstants.BrainTierHsm,
            });

            // ⭐ O7c-④b: seed a slot-resident instance that ended its previous behaviour terminal.
            Assert.True(RootHsmAccess.EnsureRootInstance(world, e, 9300, BuildMinimalBlob(0x9300)));
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* before, out _));
            ((InstanceHeader*)before)->Flags |= InstanceFlags.Terminated;
            ((InstanceHeader*)before)->Phase  = InstancePhase.RTC;

            // Assign a new behavior -- ingress must re-bind the instance.
            AssignBehavior(world, sys, e, behaviorName);

            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* after, out _));
            var hdr = (InstanceHeader*)after;
            Assert.Equal(0, (int)(hdr->Flags & InstanceFlags.Terminated));
            // 🔴🔴 O7c-④b CHANGED THIS VALUE FROM Idle TO Entry, AND IT IS A REAL FIX. 📄 §31.16.3.
            //   📐 Measured in HsmKernelCore.ProcessInstancePhase: the `Idle` arm runs the timer phase
            //     and advances to `Entry` ONLY IF THE EVENT QUEUE IS NON-EMPTY. The `Entry` arm, with
            //     ActiveLeafIds[0] == 0xFFFF, calls InitializeMachine — which is what ENTERS the
            //     machine's initial state and runs its entry actions.
            //   ⇒ the deleted ResetHsmComponents forced Idle, so a freshly assigned HSM behaviour sat
            //     INERT until some external event happened to arrive. ⛔ That is why two example
            //     scenarios hand-set Phase = RTC and seeded ActiveLeafIds[0] themselves — they were
            //     working around it.
            //   ⭐ HsmInstanceManager.Initialize — the kernel's own entry point, which O7c-③ made
            //     size-driven — leaves Entry, so the machine enters on the very next tick.
            Assert.Equal(InstancePhase.Entry, hdr->Phase);

            world.Dispose();
        }

        [Fact]
        public void BehaviorIngress_HsmReset_ClearsActiveLeafIds()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "HsmResetDoc2";
            registry.Register(9301, behaviorName, new BehaviorDefinition
            {
                Name          = behaviorName,
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = BuildMinimalBlob(0x9301),
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = 9301, BrainTier = BehaviorConstants.BrainTierHsm,
            });

            // Simulate a machine that was mid-run: set ActiveLeafIds to non-sentinel values.
            Assert.True(RootHsmAccess.EnsureRootInstance(world, e, 9301, BuildMinimalBlob(0x9301)));
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* before, out int beforeSize));
            ushort* seeded = HsmKernel.GetActiveLeafIds(before, beforeSize, out int leafCount);
            Assert.True(leafCount >= 2);
            seeded[0] = 2;
            seeded[1] = 5;

            // Assign behavior -- ingress resets leaf IDs to 0xFFFF (uninitialized).
            AssignBehavior(world, sys, e, behaviorName);

            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* after, out int afterSize));
            ushort* leaves = HsmKernel.GetActiveLeafIds(after, afterSize, out _);
            Assert.Equal(0xFFFF, leaves[0]);
            Assert.Equal(0xFFFF, leaves[1]);

            world.Dispose();
        }

        // BHU-016 / CRITICAL FIX: Proves that transitioning an entity between two different
        // HSM behaviors overwrites InstanceHeader.MachineId to match the new StructureHash,
        // preventing HsmKernelCore.ValidateInstance from soft-locking the entity.
        [Fact]
        public unsafe void BehaviorIngressSystem_UpdatesMachineId_OnBehaviorReassignment()
        {
            // 1. Arrange: two distinct blobs, two distinct behavior registrations.
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();

            const uint HashA = 0xAAAAu;
            const uint HashB = 0xBBBBu;
            const int  DocA  = 100;
            const int  DocB  = 200;

            var blobA = BuildMinimalBlob(HashA);
            var blobB = BuildMinimalBlob(HashB);

            registry.Register(DocA, "BehaviorA", new BehaviorDefinition
            {
                Name          = "BehaviorA",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blobA,
            });
            registry.Register(DocB, "BehaviorB", new BehaviorDefinition
            {
                Name          = "BehaviorB",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blobB,
            });

            var ingressSystem = new BehaviorIngressSystem(registry);
            var tickSystem    = new BrainTickSystem(registry);

            var entity = world.CreateEntity();
            world.AddComponent(entity, new BehaviorState());

            // ⚠ AssignBehaviorHashEvent PROVISIONS NO STORE — a pre-existing gap (§22 F14b), so the
            //   entity needs one before the hash path can attach into it. EnsureRootInstance is what
            //   the spawn-time provisioner would have done.
            Assert.True(RootHsmAccess.EnsureRootInstance(world, entity, DocA, blobA));

            // 2. Act: assign Behavior A.
            AssignBehaviorHash(world, ingressSystem, entity, DocA);

            // 3. Assert: MachineId must equal blobA.StructureHash.
            Assert.True(RootHsmAccess.TryGetInstance(world, entity, out byte* instA, out _));
            Assert.Equal(HashA, ((InstanceHeader*)instA)->MachineId);

            // 4. Act: reassign to Behavior B.
            AssignBehaviorHash(world, ingressSystem, entity, DocB);

            // 5. Assert: MachineId must now reflect blobB.StructureHash (the bug fix).
            Assert.True(RootHsmAccess.TryGetInstance(world, entity, out byte* instB, out int sizeB));
            InstanceHeader* headerB = (InstanceHeader*)instB;
            Assert.Equal(HashB, headerB->MachineId);
            Assert.Equal(0, (int)(headerB->Flags & InstanceFlags.Terminated));

            // 6. Assert: the kernel evaluates the new definition without soft-locking.
            //    Initialize leaves the machine at Entry; a tick of an empty machine advances it
            //    (ValidateInstance passes only when MachineId == StructureHash). If MachineId were
            //    stale the kernel would skip the entity and Phase would stay Entry.
            Assert.Equal(InstancePhase.Entry, headerB->Phase);

            tickSystem.Execute(world, 0.016f);

            Assert.NotEqual(InstancePhase.Entry, headerB->Phase);

            world.Dispose();
        }


        /// <summary>
        /// ⭐⭐⭐ <b><c>O7_R49</c> — AN ASSIGNED HSM BEHAVIOUR ENTERS ITS INITIAL STATE, WITH NO
        /// EXTERNAL EVENT.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.18.
        ///
        /// <para>🔴🔴 <b>THIS IS THE CONSEQUENCE-LEVEL CLAIM, and it is the one that was broken.</b>
        /// The rail above asserts <c>Phase == Entry</c> — a VALUE. ⛔ A value can be asserted and
        /// still mean nothing; what matters is whether the machine actually RUNS. ⇒ this one ticks
        /// the REAL system and asserts the machine ENTERED.</para>
        ///
        /// <para>📐 <b>Why the old glue made this impossible, read out of
        /// <c>HsmKernelCore.ProcessInstancePhase</c> rather than inferred:</b></para>
        /// <list type="number">
        ///   <item>the <c>Idle</c> arm advances to <c>Entry</c> <b>only if
        ///     <c>HsmEventQueue.GetCount(...) &gt; 0</c></b>;</item>
        ///   <item>the only in-kernel enqueue reachable from <c>Idle</c> is <c>FireTimerEvent</c>,
        ///     called from <c>ProcessTimerPhase</c> — which only fires for a timer that is already
        ///     <c>&gt; 0</c>, and timers are armed on STATE ENTRY;</item>
        ///   <item>⇒ a machine that has entered nothing has no armed timers, so nothing enqueues,
        ///     so it never leaves <c>Idle</c>. <b><c>Idle</c> + <c>ActiveLeafIds[0] == 0xFFFF</c> is a
        ///     FIXED POINT.</b></item>
        /// </list>
        ///
        /// <para>⚠ <b>And that combination is one the ENGINE never produces.</b>
        /// <c>HsmInstanceManager.Initialize</c>/<c>Reset</c> both leave <c>Entry</c>; the engine also
        /// ships <c>HsmKernel.Trigger</c>, whose summary is literally <i>"Trigger state machine to
        /// start processing from Idle"</i> — its existence is the engine SAYING that Idle does not
        /// self-start. ⇒ the defect was HROT's hand-rolled <c>ResetHsmComponents</c> writing a state
        /// the kernel's own contract excludes, not the kernel's phase policy.</para>
        ///
        /// <para>⛔ <b>No event is enqueued anywhere in this rail, deliberately</b> — enqueueing one
        /// would drive <c>Idle → Entry</c> through step ① and the rail would pass on the broken code.
        /// The queue count is asserted to stay 0 so that cannot creep in later.</para>
        /// </summary>
        [Fact]
        public void O7_R49_AnAssignedHsmBehaviourEntersItsInitialState_WithNoExternalEvent_O7c4()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "HsmEntersDoc";
            const int    DocId        = 9350;
            var blob = BuildMinimalBlob(0x9350);

            registry.Register(DocId, behaviorName, new BehaviorDefinition
            {
                Name          = behaviorName,
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = DocId, BrainTier = BehaviorConstants.BrainTierHsm,
            });
            Assert.True(RootHsmAccess.EnsureRootInstance(world, e, DocId, blob));

            // Assign through the REAL ingress — this is the path that used to leave it inert.
            AssignBehavior(world, sys, e, behaviorName);

            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* inst, out int size));

            // ⭐ NON-VACUITY: it has NOT entered yet, so the tick below is what does it.
            ushort* leaves = HsmKernel.GetActiveLeafIds(inst, size, out int leafCount);
            Assert.True(leaves != null && leafCount > 0);
            Assert.Equal((ushort)0xFFFF, leaves[0]);
            Assert.Equal(0, HsmEventQueue.GetCount(inst, size));

            // ── Tick the REAL merged system. NOTHING enqueues an event. ──────────────────
            var brain = new BrainTickSystem(registry);
            for (int t = 0; t < 3; t++)
                brain.Execute(world, 0.016f);

            // ⭐⭐⭐ THE RAIL. The machine entered state 0 on its own.
            //    🔴 With the retired ResetHsmComponents' Phase = Idle this stays 0xFFFF forever.
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* after, out int afterSize));
            ushort* leavesAfter = HsmKernel.GetActiveLeafIds(after, afterSize, out _);
            Assert.Equal((ushort)0, leavesAfter[0]);

            // ⚠ And it got there WITHOUT an event — the queue was never fed.
            Assert.Equal(0, HsmEventQueue.GetCount(after, afterSize));

            world.Dispose();
        }

        // ══ O7c-④a — THE ROOT HSM INSTANCE IS AN OCCURRENCE SLOT ════════════════════════════
        //
        // 📄 DESIGN_Occurrence_Scoped_Storage.md §31.14 (design) · §31.15–§31.16 (as-built).
        // ⭐ O7c-④b (2026-09-23): the BHU-016 rails ABOVE are now slot-resident too — the component
        //   they used to seed is no longer what BrainTickSystem steps. ⚠ Their CLAIMS are unchanged;
        //   only the storage they assert against moved, which is what obligation ③ calls a re-home
        //   rather than a rewrite.

        /// <summary>
        /// Builds a blob whose <c>SelectTier</c> answer is controlled by its REGION COUNT, which is
        /// the cheapest axis to move: tier 1 wants <c>regions &lt;= 1</c>, tier 2 <c>&lt;= 2</c>.
        /// </summary>
        private static HsmDefinitionBlob BuildBlobWithRegions(uint structureHash, ushort regionCount)
        {
            var states = new StateDef[1];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0 };
            var header = new HsmDefinitionHeader
            {
                StructureHash   = structureHash,
                StateCount      = 1,
                TransitionCount = 0,
                RegionCount     = regionCount,
            };
            return new HsmDefinitionBlob(
                header,
                states,
                Array.Empty<TransitionDef>(),
                new RegionDef[regionCount],
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>());
        }

        private static Entity RegisterAndAssign(
            EntityRepository world, BehaviorIngressSystem sys, BehaviorRegistry registry,
            Entity entity, int docId, string name, HsmDefinitionBlob blob)
        {
            registry.Register(docId, name, new BehaviorDefinition
            {
                Name          = name,
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });
            AssignBehavior(world, sys, entity, name);
            return entity;
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7_R42</c> — A 64-BYTE MACHINE RESERVES 64 BYTES.</b>
        ///
        /// <para>🔴 <b>This is the headline of the whole slice, and it is the one claim the component
        /// could never satisfy.</b> <c>BrainHsm128</c> was 128 bytes for every machine, whatever
        /// <c>HsmInstanceManager.SelectTier</c> said — the width was a property of a TYPE. ⇒ the
        /// kernel's smallest tier has existed since FastHSM shipped and has never once been
        /// allocated. ⭐ §9.4's <i>"the tier stops being a TYPE and becomes a PAYLOAD SIZE"</i>,
        /// asserted rather than asserted-about.</para>
        ///
        /// <para>⚠ The guard field carries the extent, which is <c>RootParamsAccess</c>'s existing
        /// convention — so this also pins that the tick arm's <c>instanceSize</c> comes from the same
        /// lookup as its pointer and cannot disagree with the allocation.</para>
        /// </summary>
        [Fact]
        public void O7_R42_TheRootHsmSlotIsSizedFromSelectTier_NotFromAType()
        {
            var (world, sys, registry) = CreateFixture();

            var blob = BuildBlobWithRegions(0x6400u, regionCount: 0);
            Assert.Equal(64, HsmInstanceManager.SelectTier(blob));   // guard: the premise, not the claim

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            RegisterAndAssign(world, sys, registry, e, 9400, "Hsm64Doc", blob);

            Assert.True(
                RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out int size),
                "The assign path must have attached a root HSM instance slot.");
            Assert.True(ptr != null);
            Assert.Equal(64, size);

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <b><c>O7_R43</c> — the instance is BOUND to the machine IN THE SLOT.</b>
        ///
        /// <para>⛔ A freshly attached slot is ZEROED, and a zeroed instance has
        /// <c>MachineId == 0</c>, which <c>HsmKernelCore.ValidateInstance</c> rejects by
        /// <c>continue</c> — silently. ⇒ "the slot exists" is not the claim worth pinning; "the slot
        /// is runnable" is. 📌 That silent-skip is exactly the state every spawned-but-never-assigned
        /// <c>BrainHsm128</c> has been in.</para>
        /// </summary>
        [Fact]
        public void O7_R43_TheSlotResidentInstanceIsBoundToTheMachine_O7c4a()
        {
            var (world, sys, registry) = CreateFixture();

            const uint StructureHash = 0xC0FFEEu;
            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            RegisterAndAssign(world, sys, registry, e, 9401, "HsmBoundDoc",
                              BuildBlobWithRegions(StructureHash, regionCount: 0));

            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out _));

            var header = (InstanceHeader*)ptr;
            Assert.Equal(StructureHash, header->MachineId);
            Assert.Equal(InstancePhase.Entry, header->Phase);
            Assert.Equal(0, (int)(header->Flags & InstanceFlags.Terminated));

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7_R44</c> — A MACHINE THAT OUTGROWS ITS TIER RE-ATTACHES AT THE NEW WIDTH.</b>
        ///
        /// <para>🔴 <b>Not tidiness — an out-of-bounds read.</b> Reassigning from a 1-region machine
        /// (64) to a 3-region one (256) and keeping the 64-byte allocation would have the kernel step
        /// 256 bytes of a 64-byte slot, straight into whatever occurrence was attached after it. ⚠ No
        /// compiler check and no runtime check; §9.4 is the whole argument for sizing from the
        /// allocation. ⇒ the guard mismatch MUST detach and re-attach.</para>
        ///
        /// <para>⚠ The instance state is lost across that move, and that is correct: the state ids it
        /// referred to have been renumbered by the same edit that changed the tier.</para>
        /// </summary>
        [Fact]
        public void O7_R44_AWiderMachineReAttachesTheSlotAtItsNewWidth_O7c4a()
        {
            var (world, sys, registry) = CreateFixture();

            var narrow = BuildBlobWithRegions(0x0064u, regionCount: 0);   // tier 64
            var wide   = BuildBlobWithRegions(0x0256u, regionCount: 3);   // tier 256
            Assert.Equal(64,  HsmInstanceManager.SelectTier(narrow));
            Assert.Equal(256, HsmInstanceManager.SelectTier(wide));

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());

            RegisterAndAssign(world, sys, registry, e, 9402, "HsmNarrowDoc", narrow);
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out _, out int sizeBefore));
            Assert.Equal(64, sizeBefore);

            RegisterAndAssign(world, sys, registry, e, 9403, "HsmWideDoc", wide);
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* widePtr, out int sizeAfter));
            Assert.Equal(256, sizeAfter);

            // And it is bound to the NEW machine, not merely resized.
            Assert.Equal(0x0256u, ((InstanceHeader*)widePtr)->MachineId);

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <b><c>O7_R45</c> — reassigning does not LEAK root HSM slots.</b>
        ///
        /// <para>📐 The <c>O7_R41</c> shape, on the HSM root. The 256-byte store has <b>3</b> slots, so
        /// a leak of one per behaviour change exhausts it in three assigns and the entity then
        /// silently stops getting a machine. ⭐ Reading the ACTUAL number is the point: the root HSM
        /// slot is reclaimed by <c>DetachHostedOccurrenceSlots</c> (its kind is <c>Hsm</c>), so five
        /// assigns must leave exactly ONE.</para>
        /// </summary>
        [Fact]
        public void O7_R45_ReassigningDoesNotLeakTheRootHsmSlot_O7c4a()
        {
            var (world, sys, registry) = CreateFixture();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());

            for (int i = 0; i < 5; i++)
            {
                RegisterAndAssign(
                    world, sys, registry, e, 9500 + i, "HsmChurnDoc" + i,
                    BuildBlobWithRegions(0x9500u + (uint)i, regionCount: 0));
            }

            byte* store = Fdp.Toolkit.Blueprints.Partitioning
                              .OccurrenceStoreAccess.TryGetStore(world, e, out _);
            Assert.True(store != null);

            int slots = Fdp.Toolkit.Blueprints.Partitioning
                           .BlueprintBlackboardPartitions.GetSlotCount(store);

            // 1 = the CURRENT behaviour's root HSM instance. These behaviours parse no params and
            // declare no manifest, so nothing else is attached. A leak would read 5.
            Assert.Equal(1, slots);
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out _, out _));

            world.Dispose();
        }
    }
}
