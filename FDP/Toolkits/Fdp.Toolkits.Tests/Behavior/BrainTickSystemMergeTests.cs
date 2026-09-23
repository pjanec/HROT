using System;
using Fdp.Core;
using Fbt;
using Fbt.Runtime;
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
    /// ⭐⭐⭐ <b><c>O7c</c>-④b — the claims that belong to THE MERGE ITSELF.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.14 / §31.16.
    ///
    /// <para>⛔ The per-arm behaviour is covered by <c>BrainTickSystemBTreeArmTests</c> and
    /// <c>BrainTickSystemHsmArmTests</c> — those are the re-homed suites of the two systems that
    /// merged, and their claims did not change. ⭐ <b>What NEITHER of them can assert is what the
    /// merge is FOR</b>: that one walk drives both paradigms, and that the body they now share
    /// behaves the same for each.</para>
    /// </summary>
    public unsafe class BrainTickSystemMergeTests
    {
        // ── Fixtures ─────────────────────────────────────────────────────────────

        private static Interpreter<byte, BTreeContext> BuildInterpreter(string actionName, NodeStatus status)
        {
            var blob = new BehaviorTreeBlob
            {
                TreeName    = "MergeTest",
                Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
                MethodNames = new[] { actionName },
                FloatParams = Array.Empty<float>(),
                IntParams   = Array.Empty<int>(),
            };
            var actions = new ActionRegistry<byte, BTreeContext>();
            actions.Register(actionName,
                (ref byte _, ref BehaviorTreeState _, ref BTreeContext _, int _) => status);
            return new Interpreter<byte, BTreeContext>(blob, actions);
        }

        /// <summary>A 2-state machine, 2 regions so <c>SelectTier</c> answers 128.</summary>
        private static HsmDefinitionBlob BuildHsmBlob(uint structureHash)
        {
            var states = new StateDef[2];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1, Flags = StateFlags.IsInitial };
            states[1] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0, Flags = StateFlags.IsFinal };
            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef { SourceStateIndex = 0, TargetStateIndex = 1, EventId = 10 };
            var header = new HsmDefinitionHeader
            {
                StructureHash = structureHash, StateCount = 2, TransitionCount = 1, RegionCount = 2,
            };
            return new HsmDefinitionBlob(header, states, transitions, new RegionDef[2],
                Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        // ── O7_R46 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7_R46</c> — ONE SYSTEM, ONE WALK, BOTH PARADIGMS.</b>
        ///
        /// <para>🔒 This is the claim the user asked for when they asked whether the traversal and the
        /// ticking could be merged, and <b>nothing else in the suite can make it</b>: the per-arm
        /// suites each construct the system for their own paradigm, so both would stay green if the
        /// other arm were deleted outright.</para>
        ///
        /// <para>⭐ A BTree brain and an HSM brain share the occurrence-store tier walk. After a single
        /// <c>Execute</c>, the BTree entity has published its terminal event and the HSM entity has
        /// left <c>Entry</c> — each advanced by its OWN kernel, from its OWN keyed slot.</para>
        /// </summary>
        [Fact]
        public void O7_R46_OneWalkDrivesBothParadigms_O7c4b()
        {
            var world = TestWorldFactory.Create();

            const int BTreeDoc = 8600;
            const int HsmDoc   = 8601;

            var registry = new BehaviorRegistry();
            registry.Register(BTreeDoc, "MergeBTreeDoc", new BehaviorDefinition
            {
                Name             = "MergeBTreeDoc",
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = BuildInterpreter("MergeAction", NodeStatus.Success),
            });

            var hsmBlob = BuildHsmBlob(0x8601u);
            registry.Register(HsmDoc, "MergeHsmDoc", new BehaviorDefinition
            {
                Name          = "MergeHsmDoc",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = hsmBlob,
            });

            var sys = new BrainTickSystem(registry);

            var btreeEntity = world.CreateEntity();
            world.AddComponent(btreeEntity, new BehaviorState
            {
                ActiveBehaviorHash = BTreeDoc, BrainTier = BehaviorConstants.BrainTierBTree,
            });
            Assert.True(RootStateAccess.EnsureRootState(world, btreeEntity, BTreeDoc));

            var hsmEntity = world.CreateEntity();
            world.AddComponent(hsmEntity, new BehaviorState
            {
                ActiveBehaviorHash = HsmDoc, BrainTier = BehaviorConstants.BrainTierHsm,
            });
            Assert.True(RootHsmAccess.EnsureRootInstance(world, hsmEntity, HsmDoc, hsmBlob));

            // Guard: the two roots must be DIFFERENT slots, or this rail proves nothing about
            // keeping the paradigms apart. ($occ.rootState vs $occ.rootHsm — §31.15.2.)
            Assert.NotEqual(
                RootStateAccess.KeyForBehaviour(BTreeDoc),
                RootHsmAccess.KeyForBehaviour(HsmDoc));

            // ── ONE Execute drives both ──────────────────────────────────────────
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();

            // The BTree arm reached a terminal root status and published.
            int published = 0;
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == btreeEntity.Index) published++;
            Assert.Equal(1, published);

            // The HSM arm stepped its instance out of Entry — i.e. the kernel ran, which it only
            // does when MachineId matches and the pointer+size came from the slot.
            Assert.True(RootHsmAccess.TryGetInstance(world, hsmEntity, out byte* inst, out int size));
            Assert.Equal(128, size);                       // sized from the MACHINE, not a type
            Assert.NotEqual(InstancePhase.Entry, ((InstanceHeader*)inst)->Phase);

            // And neither arm touched the other's slot.
            Assert.True(RootStateAccess.TryGetState(world, btreeEntity, out _));
            Assert.False(RootHsmAccess.TryGetInstance(world, btreeEntity, out _, out _));
            Assert.False(RootStateAccess.TryGetState(world, hsmEntity, out _));

            world.Dispose();
        }

        // ── O7_R47 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7_R47</c> — THE STALE-DEDUP SWEEP NOW PROTECTS THE BTREE ARM TOO.</b>
        /// 📄 §31.14.6.
        ///
        /// <para>📐 <b>Measured before the merge: the sweep existed in <c>HsmTickSystem</c> and NOT AT
        /// ALL in <c>BTreeTickSystem</c>.</b> Two systems that should behave identically did not, and
        /// a merge that copied whichever twin it started from would have silently picked a winner.</para>
        ///
        /// <para>⛔⛔ <b>Why it is a CORRECTNESS argument, not tidiness.</b> The dedup dictionary is
        /// keyed by <c>entity.Index</c>, which the ECS <b>reuses</b>. ⇒ an entry left behind for an
        /// entity that has gone would suppress a genuine <c>BehaviorFinishedEvent</c> for a DIFFERENT
        /// entity that later lands on the same index — a missed terminal event, which is exactly the
        /// kind of silent non-execution this programme keeps filing.</para>
        ///
        /// <para>⭐ <b>The premise is restated in slot terms</b>, because there is no brain component
        /// left to remove: an entity leaves the walk when its STORE goes. ⇒ the merge FIXES a latent
        /// BTree gap rather than importing an HSM quirk.</para>
        /// </summary>
        [Fact]
        public void O7_R47_ABTreeEntityLeavingTheWalkDropsItsDedupEntry_O7c4b()
        {
            var world = TestWorldFactory.Create();
            const int BTreeDoc = 8602;

            var registry = new BehaviorRegistry();
            registry.Register(BTreeDoc, "SweepDoc", new BehaviorDefinition
            {
                Name             = "SweepDoc",
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = BuildInterpreter("SweepAction", NodeStatus.Success),
            });

            var sys = new BrainTickSystem(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = BTreeDoc, BrainTier = BehaviorConstants.BrainTierBTree,
            });
            Assert.True(RootStateAccess.EnsureRootState(world, e, BTreeDoc));

            // Frame 1 — terminal, so the system starts tracking this entity index.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(1, sys.TrackedEntityCount);

            // The entity leaves the walk: its occurrence store goes.
            // ⚠ No DestructionOrder and no ClearBehaviorEvent — those two are already pruned by the
            //   lifecycle handlers, so using either would make this rail vacuous.
            BlueprintTierTable.Of(world, e)!.Remove(world, e);

            // Frame 2 — not seen, so the stale entry is swept.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Equal(0, sys.TrackedEntityCount);

            world.Dispose();
        }
    }
}
