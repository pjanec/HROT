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
            world.AddComponent(e, new BrainHsm64());

            // Manually set Terminated flag and a non-Idle phase to simulate
            // an HSM that ended its previous behavior in a terminal state.
            ref var brain = ref world.GetComponentRW<BrainHsm64>(e);
            brain.State.Header.Flags |= InstanceFlags.Terminated;
            brain.State.Header.Phase  = InstancePhase.RTC;

            // Assign a new behavior -- ingress system must reset the HSM.
            AssignBehavior(world, sys, e, behaviorName);

            var brainAfter = world.GetComponent<BrainHsm64>(e);
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
            world.AddComponent(e, new BrainHsm64());

            // Simulate a machine that was mid-run: set ActiveLeafIds to non-sentinel values.
            ref var brain = ref world.GetComponentRW<BrainHsm64>(e);
            brain.State.ActiveLeafIds[0] = 2;
            brain.State.ActiveLeafIds[1] = 5;

            // Assign behavior -- ingress system resets leaf IDs to 0xFFFF (uninitialized).
            AssignBehavior(world, sys, e, behaviorName);

            var brainAfter = world.GetComponent<BrainHsm64>(e);
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
    }
}
