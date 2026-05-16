using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests
{
    /// <summary>
    /// IT-BHU-E1 and IT-BHU-E2: proves CognitiveRuntimeModule system order (BHU-010)
    /// and full-frame HSM + interrupt integration.
    /// </summary>
    public unsafe class HsmBehaviorIntegrationTests
    {
        // -- helpers -------------------------------------------------------

        private static EntityRepository CreateBehaviorWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            return world;
        }

        // Three-state blob for E2: Patrol --(MobilityLost=1)--> Stopped --(EventDone=99)--> Done(IsFinal).
        // Using raw StateDef[] so that state indices (0=Patrol, 1=Stopped, 2=Done) are known exactly.
        private static HsmDefinitionBlob BuildE2Blob(uint structureHash)
        {
            const ushort EventDone         = 99;
            const ushort EventMobilityLost = BehaviorConstants.EventId_MobilityLost;

            var states = new StateDef[3];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1, Flags = StateFlags.IsInitial };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 1, TransitionCount = 1 };
            states[2] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0, Flags = StateFlags.IsFinal };

            var transitions = new TransitionDef[2];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = EventMobilityLost };
            transitions[1] = new TransitionDef { SourceStateIndex = 1, TargetStateIndex = 2, EventId = EventDone };

            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 3, TransitionCount = 2 };
            return new HsmDefinitionBlob(header, states, transitions,
                Array.Empty<RegionDef>(),
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>());
        }

        private static BrainHsm128 MakeBrain128(HsmDefinitionBlob blob)
        {
            var brain = new BrainHsm128();
            brain.State.Header.MachineId = blob.Header.StructureHash;
            brain.State.Header.Phase     = InstancePhase.Entry;
            brain.State.ActiveLeafIds[0] = 0xFFFF;
            return brain;
        }

        private static void InjectHsmEvent(EntityRepository world, Entity e, HsmEvent evt)
        {
            ref var comp = ref world.GetComponentRW<BrainHsm128>(e);
            BrainHsm128* ptr = (BrainHsm128*)Unsafe.AsPointer(ref comp);
            HsmEventQueue.TryEnqueue(ptr, evt);
        }

        // IT-BHU-E1: CognitiveRuntimeModule registers exactly 6 systems in the required order.
        // CognitiveInterruptSystem and CognitiveCleanupSystem are internal types; their type
        // names are compared as strings. Public types use Assert.IsType<>.
        [Fact]
        public void E1_CognitiveRuntimeModule_RegistersExactlySixSystemsInOrder()
        {
            var registry = new BehaviorRegistry();
            var module   = new CognitiveRuntimeModule(registry);

            Assert.Equal(6, module.SimulationSystems.Count);

            Assert.IsType<ChannelArbitrationSystem>(module.SimulationSystems[0]);

            // CognitiveInterruptSystem is internal to Fdp.Toolkits -- compare by type name.
            Assert.Equal("CognitiveInterruptSystem", module.SimulationSystems[1].GetType().Name);

            Assert.IsType<BTreeTickSystem>(module.SimulationSystems[2]);
            Assert.IsType<HsmTickSystem<BrainHsm128>>(module.SimulationSystems[3]);
            Assert.IsType<HsmTickSystem<BrainHsm64>>(module.SimulationSystems[4]);

            // CognitiveCleanupSystem is internal to Fdp.Toolkits -- compare by type name.
            Assert.Equal("CognitiveCleanupSystem", module.SimulationSystems[5].GetType().Name);

            // Confirm no HsmDamageBridgeSystem anywhere (BHU-010 requirement).
            foreach (var sys in module.SimulationSystems)
                Assert.NotEqual("HsmDamageBridgeSystem", sys.GetType().Name);
        }

        // IT-BHU-E2: Full-frame integration -- mobility-lost interrupt drives HSM to
        // Stopped (Frame 1), then EventDone drives it to the final Done state and a
        // BehaviorFinishedEvent is published (Frame 2).
        [Fact]
        public void E2_FullFrame_MobilityLostInterrupt_ThenBehaviorFinished()
        {
            var world    = CreateBehaviorWorld();
            var registry = new BehaviorRegistry();
            const int docId = 0xE2001;

            var blob = BuildE2Blob(0xE2000001);
            registry.Register(docId, "E2Patrol",
                new BehaviorDefinition { Name = "E2Patrol", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob });

            var module = new CognitiveRuntimeModule(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = docId, BrainTier = BehaviorConstants.BrainTierHsm, InstanceId = 1 });
            world.AddComponent(e, MakeBrain128(blob));
            world.AddComponent(e, new BrainBlackboard());
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            world.AddComponent(e, new PreviousCapabilities { Capabilities = ActorCapabilities.CanMove });

            // Settle machine into Patrol (state 0) by running a few ticks without events.
            for (int i = 0; i < 4; i++)
                module.SimulationSystems[3].Execute(world, 0.016f); // HsmTickSystem<BrainHsm128>

            // ---- Frame 1: trigger mobility-lost edge ----

            // Clear CanMove capability so CognitiveInterruptSystem detects the edge.
            {
                ref var ac = ref world.GetComponentRW<ActorCapabilityState>(e);
                ac.Capabilities &= ~ActorCapabilities.CanMove;
            }

            // Run non-HSM systems once, then run HsmTick128 enough times to complete the
            // Idle -> Entry -> RTC -> Activity -> Idle phase cycle (needs ~4 kernel ticks).
            // This mirrors the B1 test pattern where hsmSys.Execute is called 10 times.
            module.SimulationSystems[0].Execute(world, 0.016f); // ChannelArbitration
            module.SimulationSystems[1].Execute(world, 0.016f); // CognitiveInterrupt: sets bb[126]=1
            module.SimulationSystems[2].Execute(world, 0.016f); // BTreeTick
            for (int t = 0; t < 10; t++)
                module.SimulationSystems[3].Execute(world, 0.016f); // HsmTick128: completes transition
            module.SimulationSystems[4].Execute(world, 0.016f); // HsmTick64
            module.SimulationSystems[5].Execute(world, 0.016f); // CognitiveCleanup: clears bb[126]

            // Assert end of Frame 1: HSM transitioned to Stopped (state index 1).
            var brainF1 = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(1, brainF1.State.ActiveLeafIds[0]);

            // CognitiveCleanupSystem (index 5) must have cleared the interrupt field.
            {
                ref readonly var bb = ref world.GetComponentRO<BrainBlackboard>(e);
                Assert.Equal(0, bb.Interrupt_MobilityLost);
            }

            // ---- Frame 2: inject EventDone and drive HSM to final state ----

            // EventDone (id=99) drives Stopped -> Done (IsFinal) -> Terminated -> published.
            InjectHsmEvent(world, e, new HsmEvent { EventId = 99, Priority = EventPriority.Interrupt });

            // Run the complete system sequence for Frame 2.
            module.SimulationSystems[0].Execute(world, 0.016f); // ChannelArbitration
            module.SimulationSystems[1].Execute(world, 0.016f); // CognitiveInterrupt: no edge
            module.SimulationSystems[2].Execute(world, 0.016f); // BTreeTick
            for (int t = 0; t < 10; t++)
                module.SimulationSystems[3].Execute(world, 0.016f); // HsmTick128: drives to Done
            module.SimulationSystems[4].Execute(world, 0.016f); // HsmTick64
            module.SimulationSystems[5].Execute(world, 0.016f); // CognitiveCleanup

            // Make published events visible for reading.
            world.Bus.SwapBuffers();

            // Assert: BehaviorFinishedEvent published for this entity.
            int count = 0;
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index) count++;
            Assert.Equal(1, count);

            // Assert: Terminated latch cleared after publish; Phase reset to Idle.
            var brainF2 = world.GetComponent<BrainHsm128>(e);
            ref var hdr = ref Unsafe.As<BrainHsm128, InstanceHeader>(ref brainF2);
            Assert.Equal(0, (int)(hdr.Flags & InstanceFlags.Terminated));
            Assert.Equal(InstancePhase.Idle, hdr.Phase);

            world.Dispose();
        }
    }
}
