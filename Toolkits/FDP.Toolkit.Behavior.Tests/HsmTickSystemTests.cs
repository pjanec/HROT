using System;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
{
    public unsafe class HsmTickSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

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
            var registry = new DoctrineRegistry();

            var blob = BuildTwoStateBlob();
            const string doctrineName = "TestHsm";
            registry.Register(doctrineName, new DoctrineDefinition
            {
                Name          = doctrineName,
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });

            var sys = new HsmTickSystem<BrainHsm128>(registry);
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState
            {
                ActiveDoctrineHash = doctrineName.GetHashCode(),
                BrainTier          = BehaviorConstants.BrainTierHsm,
            });

            // Initialise instance: in StateA (index 0), RTC phase, EventX (id=10) ready.
            var brain = new BrainHsm128();
            brain.State.Header.MachineId = blob.Header.StructureHash;
            brain.State.Header.Phase     = InstancePhase.RTC;
            // ActiveLeafIds[0] = 0 means currently in State 0 (StateA).
            brain.State.ActiveLeafIds[0] = 0;
            // Write EventX into the CurrentEventId scratch space at offset 58 (Reserved1).
            // HsmKernelCore.CurrentEventId_Offset_128 == 58, which is HsmInstance128.Reserved1.
            brain.State.Reserved1 = 10; // EventX id

            world.AddComponent(e, brain);

            // Act.
            sys.Run();

            // Assert — HSM transitioned from State 0 to State 1.
            var result = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(1, result.State.ActiveLeafIds[0]); // StateB.Id == 1

            sys.Dispose();
            world.Dispose();
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void HsmTick64_And_HsmTick128_AreIndependent()
        {
            // Arrange — entity A has BrainHsm64 only; entity B has BrainHsm128 only.
            var world = TestWorldFactory.Create();

            // Empty registries — neither entity has a registered doctrine, so both
            // systems will skip them. What we're testing is that each system only
            // queries the component it owns and never touches the other type.
            var sys128 = new HsmTickSystem<BrainHsm128>(new DoctrineRegistry());
            var sys64  = new HsmTickSystem<BrainHsm64>(new DoctrineRegistry());
            sys128.Create(world);
            sys64.Create(world);

            // Entity A — only BrainHsm64.
            var entityA = world.CreateEntity();
            world.AddComponent(entityA, new DoctrineState { BrainTier = BehaviorConstants.BrainTierHsm });
            var brainA = new BrainHsm64();
            brainA.State.Header.Phase = InstancePhase.Idle;
            world.AddComponent(entityA, brainA);
            // NO BrainHsm128 on entityA.

            // Entity B — only BrainHsm128.
            var entityB = world.CreateEntity();
            world.AddComponent(entityB, new DoctrineState { BrainTier = BehaviorConstants.BrainTierHsm });
            var brainB = new BrainHsm128();
            brainB.State.Header.Phase = InstancePhase.Idle;
            world.AddComponent(entityB, brainB);
            // NO BrainHsm64 on entityB.

            // Act — run the 128 system first, then the 64 system.
            sys128.Run();

            // sys128 processed only entityB (has BrainHsm128).
            // entityA's BrainHsm64 must be unchanged.
            var aAfter128 = world.GetComponent<BrainHsm64>(entityA);
            Assert.Equal(InstancePhase.Idle, aAfter128.State.Header.Phase);

            sys64.Run();

            // sys64 processed only entityA (has BrainHsm64).
            // entityB's BrainHsm128 must be unchanged.
            var bAfter64 = world.GetComponent<BrainHsm128>(entityB);
            Assert.Equal(InstancePhase.Idle, bAfter64.State.Header.Phase);

            sys128.Dispose();
            sys64.Dispose();
            world.Dispose();
        }
    }
}
