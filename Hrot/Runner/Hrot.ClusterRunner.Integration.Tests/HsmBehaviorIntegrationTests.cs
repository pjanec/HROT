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
            // ⭐⭐ O7c-④b: the brain component is gone; the instance lives in an OCCURRENCE SLOT, so
            //   the tier ladder is what this world must register instead. ⛔ Omitting it does NOT
            //   throw — the tier walk simply enumerates nothing and the brain silently never ticks
            //   (§31's CE-315 shape), which is why it is registered here explicitly rather than
            //   assumed.
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
            world.RegisterComponent<BrainInterrupts>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<PreviousCapabilities>();
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

            // ⭐⭐ O7c-④b: RegionCount 2 is what makes SelectTier answer 128. 🔴 NOT cosmetic — the
            //   instance width decides the EVENT-QUEUE shape, and E2 injects an INTERRUPT-priority
            //   event, which only a 128- or 256-byte instance has a slot for. Before the move every
            //   instance was 128 because the COMPONENT was, so the blob never had to say so.
            var header = new HsmDefinitionHeader { StructureHash = structureHash, StateCount = 3, TransitionCount = 2, RegionCount = 2 };
            return new HsmDefinitionBlob(header, states, transitions,
                new RegionDef[2],
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>());
        }

        /// <summary>⭐ O7c-④b: provision the slot-resident instance, sized by SelectTier(blob).</summary>
        private static void SeedBrain(EntityRepository world, Entity e, int docId, HsmDefinitionBlob blob)
            => Assert.True(Fdp.Toolkit.Behavior.RootHsmAccess.EnsureRootInstance(world, e, docId, blob));

        private static void InjectHsmEvent(EntityRepository world, Entity e, HsmEvent evt)
        {
            Assert.True(Fdp.Toolkit.Behavior.RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out int size));
            Assert.True(HsmEventQueue.TryEnqueue(ptr, size, evt));
        }

        /// <summary>⭐ The first active leaf, read size-driven through the kernel's own accessor.</summary>
        private static ushort ActiveLeaf0(EntityRepository world, Entity e)
        {
            Assert.True(Fdp.Toolkit.Behavior.RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out int size));
            ushort* leaves = HsmKernel.GetActiveLeafIds(ptr, size, out int count);
            Assert.True(leaves != null && count > 0);
            return leaves[0];
        }

        // IT-BHU-E1: CognitiveRuntimeModule registers exactly 5 systems in the required order.
        // CognitiveInterruptSystem, CognitiveCleanupSystem and BehaviorFrameSystem are internal
        // types; their type names are compared as strings. Public types use Assert.IsType<>.
        //
        // ⭐⭐⭐ AX-018 — THIS ASSERT WAS STALE, AND THE CORRECT VALUE WAS ALREADY IN THE REPO.
        //    📐 Measured 2026-08-26: the module registers SEVEN systems — BehaviorFrameSystem was added
        //    at index 6 ("advances the global behaviour-frame pulse", Q46 rule 2b) and this copy of the
        //    claim was never updated, so it asserted 6 and had been red ever since.
        //
        // ⛔ It is a STALE TEST, not a defect in the module — established rather than assumed:
        //    Fdp.Toolkits.Tests/Behavior/Modules/CognitiveRuntimeModuleTests already asserts 7 with
        //    BehaviorFrameSystem at index 6, and is GREEN. ⇒ the module is right and this copy was wrong.
        //
        // ⚠ The claim is therefore asserted TWICE, and the OWNING project's test is the better home —
        //    it can name the internal types directly instead of comparing type-name strings. ⭐ Filed
        //    rather than removed here: deleting a rail is a separate, reviewable act (see AX-018 notes).
        [Fact]
        public void E1_CognitiveRuntimeModule_RegistersExactlyFiveSystemsInOrder()
        {
            var registry = new BehaviorRegistry();
            var module   = new CognitiveRuntimeModule(registry);

            // ⛔ O7c-④b (2026-09-23): was SIX. BTreeTickSystem and HsmTickSystem<BrainHsm128> are
            //   ONE BrainTickSystem — the generic could not survive retiring its component.
            // ⛔ O7c-① (2026-09-22): was SEVEN before that. HsmTickSystem<BrainHsm64> went with its
            //   component — nothing in production ever attached it, so it ticked an empty query.
            Assert.Equal(5, module.SimulationSystems.Count);

            Assert.IsType<ChannelArbitrationSystem>(module.SimulationSystems[0]);

            // CognitiveInterruptSystem is internal to Fdp.Toolkits -- compare by type name.
            Assert.Equal("CognitiveInterruptSystem", module.SimulationSystems[1].GetType().Name);

            Assert.IsType<BrainTickSystem>(module.SimulationSystems[2]);

            // CognitiveCleanupSystem is internal to Fdp.Toolkits -- compare by type name.
            Assert.Equal("CognitiveCleanupSystem", module.SimulationSystems[3].GetType().Name);

            // ⭐ BehaviorFrameSystem is internal to Fdp.Toolkits -- compare by type name.
            Assert.Equal("BehaviorFrameSystem", module.SimulationSystems[4].GetType().Name);

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
            SeedBrain(world, e, docId, blob);
            world.AddComponent(e, new BrainInterrupts());
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            world.AddComponent(e, new PreviousCapabilities { Capabilities = ActorCapabilities.CanMove });

            // Settle machine into Patrol (state 0) by running a few ticks without events.
            for (int i = 0; i < 4; i++)
                module.SimulationSystems[2].Execute(world, 0.016f); // BrainTickSystem — HSM arm

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
            module.SimulationSystems[1].Execute(world, 0.016f); // CognitiveInterrupt: sets the flag
            for (int t = 0; t < 10; t++)
                module.SimulationSystems[2].Execute(world, 0.016f); // BrainTick: completes the transition
            module.SimulationSystems[3].Execute(world, 0.016f); // CognitiveCleanup: clears the flag
            module.SimulationSystems[4].Execute(world, 0.016f); // BehaviorFrame pulse

            // Assert end of Frame 1: HSM transitioned to Stopped (state index 1).
            Assert.Equal(1, ActiveLeaf0(world, e));

            // CognitiveCleanupSystem must have cleared the interrupt field.
            {
                ref readonly var bb = ref world.GetComponentRO<BrainInterrupts>(e);
                Assert.Equal(0, bb.Interrupt_MobilityLost);
            }

            // ---- Frame 2: inject EventDone and drive HSM to final state ----

            // EventDone (id=99) drives Stopped -> Done (IsFinal) -> Terminated -> published.
            InjectHsmEvent(world, e, new HsmEvent { EventId = 99, Priority = EventPriority.Interrupt });

            // Run the complete system sequence for Frame 2.
            module.SimulationSystems[0].Execute(world, 0.016f); // ChannelArbitration
            module.SimulationSystems[1].Execute(world, 0.016f); // CognitiveInterrupt: no edge
            for (int t = 0; t < 10; t++)
                module.SimulationSystems[2].Execute(world, 0.016f); // BrainTick: drives to Done
            module.SimulationSystems[3].Execute(world, 0.016f); // CognitiveCleanup
            module.SimulationSystems[4].Execute(world, 0.016f); // BehaviorFrame pulse

            // Make published events visible for reading.
            world.Bus.SwapBuffers();

            // Assert: BehaviorFinishedEvent published for this entity.
            int count = 0;
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index) count++;
            Assert.Equal(1, count);

            // Assert: Terminated latch cleared after publish; Phase reset to Idle.
            // ⭐ This one is UNCHANGED by O7c-④: the tick's own terminal handler still writes
            //   Phase = Idle after publishing, which is a different code path from ingress's re-bind.
            Assert.True(Fdp.Toolkit.Behavior.RootHsmAccess.TryGetInstance(world, e, out byte* instF2, out _));
            InstanceHeader* hdr = (InstanceHeader*)instF2;
            Assert.Equal(0, (int)(hdr->Flags & InstanceFlags.Terminated));
            Assert.Equal(InstancePhase.Idle, hdr->Phase);

            world.Dispose();
        }
    }
}
