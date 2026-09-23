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
        public void BehaviorIngress_HsmReset_ClearsTerminatedFlagAndSetsPhaseIdle()
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
            world.AddComponent(e, new BehaviorState());
            world.AddComponent(e, new BrainHsm128());

            // Manually set Terminated flag and a non-Idle phase to simulate
            // an HSM that ended its previous behavior in a terminal state.
            ref var brain = ref world.GetComponentRW<BrainHsm128>(e);
            brain.State.Header.Flags |= InstanceFlags.Terminated;
            brain.State.Header.Phase  = InstancePhase.RTC;

            // Assign a new behavior -- ingress system must reset the HSM.
            AssignBehavior(world, sys, e, behaviorName);

            var brainAfter = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(0, (int)(brainAfter.State.Header.Flags & InstanceFlags.Terminated));
            Assert.Equal(InstancePhase.Idle, brainAfter.State.Header.Phase);

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
            world.AddComponent(e, new BehaviorState());
            world.AddComponent(e, new BrainHsm128());

            // Simulate a machine that was mid-run: set ActiveLeafIds to non-sentinel values.
            ref var brain = ref world.GetComponentRW<BrainHsm128>(e);
            brain.State.ActiveLeafIds[0] = 2;
            brain.State.ActiveLeafIds[1] = 5;

            // Assign behavior -- ingress system resets leaf IDs to 0xFFFF (uninitialized).
            AssignBehavior(world, sys, e, behaviorName);

            var brainAfter = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(0xFFFF, brainAfter.State.ActiveLeafIds[0]);
            Assert.Equal(0xFFFF, brainAfter.State.ActiveLeafIds[1]);

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
            var tickSystem    = new HsmTickSystem<BrainHsm128>(registry);

            var entity = world.CreateEntity();
            world.AddComponent(entity, new BehaviorState());
            world.AddComponent(entity, new BrainHsm128());

            // 2. Act: assign Behavior A.
            AssignBehaviorHash(world, ingressSystem, entity, DocA);

            // 3. Assert: MachineId must equal blobA.StructureHash.
            ref var brainA = ref world.GetComponentRW<BrainHsm128>(entity);
            InstanceHeader* headerA = (InstanceHeader*)Unsafe.AsPointer(ref brainA);
            Assert.Equal(HashA, headerA->MachineId);

            // 4. Act: reassign to Behavior B.
            AssignBehaviorHash(world, ingressSystem, entity, DocB);

            // 5. Assert: MachineId must now reflect blobB.StructureHash (the bug fix).
            ref var brainB = ref world.GetComponentRW<BrainHsm128>(entity);
            InstanceHeader* headerB = (InstanceHeader*)Unsafe.AsPointer(ref brainB);
            Assert.Equal(HashB, headerB->MachineId);
            Assert.Equal(InstancePhase.Idle, headerB->Phase);
            Assert.Equal(0, (int)(headerB->Flags & InstanceFlags.Terminated));

            // 6. Assert: the kernel evaluates the new definition without soft-locking.
            // Trigger transitions Phase from Idle to Entry; a tick of an empty machine
            // advances Entry -> Idle (ValidateInstance passes when MachineId == StructureHash).
            // If MachineId was stale the kernel would skip the entity and Phase would stay Entry.
            HsmKernel.Trigger(ref brainB);
            Assert.Equal(InstancePhase.Entry, headerB->Phase);

            tickSystem.Execute(world, 0.016f);

            Assert.NotEqual(InstancePhase.Entry, headerB->Phase);

            world.Dispose();
        }

        // ══ O7c-④a — THE ROOT HSM INSTANCE IS AN OCCURRENCE SLOT ════════════════════════════
        //
        // 📄 DESIGN_Occurrence_Scoped_Storage.md §31.14.
        // ⚠ These rails assert the SLOT. The component rails above stay green unchanged for the
        //   duration of ④a, because HsmTickSystem<BrainHsm128> still steps the component — ④b
        //   switches the reader and re-homes them.

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
