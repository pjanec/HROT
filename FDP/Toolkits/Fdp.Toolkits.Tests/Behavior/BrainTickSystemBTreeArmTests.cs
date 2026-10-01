using System;
using Fdp.Core;
using Fbt;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public class BrainTickSystemBTreeArmTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Build a one-node BTree whose single Action node invokes <paramref name="actionName"/>.
        /// </summary>
        private static BehaviorTreeBlob BuildSingleActionBlob(string actionName)
        {
            return new BehaviorTreeBlob
            {
                TreeName    = "Test",
                Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
                MethodNames = new[] { actionName },
                FloatParams = Array.Empty<float>(),
                IntParams   = Array.Empty<int>(),
            };
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BTreeTick_DoesNotThrow_WhenBlobNotRegistered()
        {
            // Arrange — entity with a behavior hash that is NOT in the registry.
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            var sys      = new BrainTickSystem(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = 999,                          // not registered
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

            var stateBefore = RootStateAccess.GetStateOrDefault(world, e);

            // Act + Assert — must not throw, state must be unchanged.
            sys.Execute(world, 0.016f);

            var stateAfter = RootStateAccess.GetStateOrDefault(world, e);
            Assert.Equal(stateBefore.RunningNodeIndex, stateAfter.RunningNodeIndex);

            world.Dispose();
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BTreeTick_DoesNotTick_WhenBrainTierIsNotBTree()
        {
            // Arrange — entity with HSM tier; register a tree that counts invocations.
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();

            int tickCount = 0;
            var blob      = BuildSingleActionBlob("CountTick");
            var actionReg = new ActionRegistry<byte, BTreeContext>();
            actionReg.Register("CountTick", (ref byte _, ref BehaviorTreeState _, ref BTreeContext _, int _) =>
            {
                tickCount++;
                return NodeStatus.Success;
            });
            var interpreter = new Interpreter<byte, BTreeContext>(blob, actionReg);

            const string behaviorName = "CountTick";
            const int   behaviorId   = 9001;
            registry.Register(behaviorId, behaviorName, new BehaviorDefinition
            {
                Name             = behaviorName,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = interpreter,
            });

            var sys = new BrainTickSystem(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = behaviorId,
                BrainTier          = BehaviorConstants.BrainTierHsm, // WRONG tier
            });
            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

            // Act.
            sys.Execute(world, 0.016f);

            // Assert — tree was never ticked because BrainTier != BrainTierBTree.
            Assert.Equal(0, tickCount);

            world.Dispose();
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BTreeTick_WritesActionToChannel_ForRegisteredTree()
        {
            // Arrange — minimal one-node tree that writes LocomotionChannel.
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();

            var blob      = BuildSingleActionBlob("SetLocomotion");
            var actionReg = new ActionRegistry<byte, BTreeContext>();
            actionReg.Register("SetLocomotion",
                (ref byte _, ref BehaviorTreeState _, ref BTreeContext ctx, int _) =>
                {
                    ref var ch = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
                    ch.ActiveAction    = 1;
                    ch.ActionInstanceId = 1;
                    return NodeStatus.Success;
                });
            var interpreter = new Interpreter<byte, BTreeContext>(blob, actionReg);

            const string behaviorName = "SetLocomotion";
            const int   behaviorId   = 9002;
            registry.Register(behaviorId, behaviorName, new BehaviorDefinition
            {
                Name             = behaviorName,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = interpreter,
            });

            var sys = new BrainTickSystem(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = behaviorId,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).
            world.AddComponent(e, new LocomotionChannel()); // BTree node writes here

            // Act.
            sys.Execute(world, 0.016f);

            // Assert — BTree node wrote the expected action into the channel.
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(1, channel.ActiveAction);      // BTree wrote the action
            Assert.Equal(1u, channel.ActionInstanceId); // instance was stamped


            world.Dispose();
        }

        private static unsafe int LiveSlots(EntityRepository world, Entity e)
        {
            byte* memory = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.TryGetStore(world, e, out _);
            if (memory == null) return 0;
            var header = *(Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardHeader*)memory;
            byte* table = memory + sizeof(Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardHeader);
            int live = 0;
            for (int i = 0; i < header.SlotCount; i++)
                if (((Fdp.Toolkit.Blueprints.Partitioning.BlueprintSlotEntry*)(table + i
                        * Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.SlotEntrySize))->BlueprintId != 0)
                    live++;
            return live;
        }

        // ── Task-1 Tests: BehaviorFinishedEvent ──────────────────────────────────

        // Helper: build a one-node tree that always returns the given status.
        private static (BehaviorRegistry registry, BrainTickSystem sys) BuildTerminalSystem(
            EntityRepository world, int behaviorId, string behaviorName, NodeStatus status)
        {
            var registry  = new BehaviorRegistry();
            var blob      = BuildSingleActionBlob(behaviorName);
            var actionReg = new ActionRegistry<byte, BTreeContext>();
            actionReg.Register(behaviorName,
                (ref byte _, ref BehaviorTreeState _, ref BTreeContext _, int _) => status);
            var interpreter = new Interpreter<byte, BTreeContext>(blob, actionReg);
            registry.Register(behaviorId, behaviorName, new BehaviorDefinition
            {
                Name             = behaviorName,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = interpreter,
            });
            var sys = new BrainTickSystem(registry);
            return (registry, sys);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-449</c> — a finished BTree is CLEARED, not re-run.</b> 📄 <c>BD1-DESIGN.md</c> §1.0a.
        /// <para>
        /// 🔒 User, <c>2026-09-30</c>: <i>"finishing a behavior should cancel the commands exactly same as Clear Behavior
        /// does … each channel resets."</i> ⭐ After the root returns <c>Success</c>: <c>BrainTier = 0</c>, the hash is
        /// <c>None</c>, <c>InstanceId</c> is bumped (⇒ <c>ChannelArbitrationSystem</c> resets the channels), the root tree
        /// state slot is gone, and further frames do not tick the tree.
        /// </para>
        /// <para>⚠ Inverse-edit red-proof: delete the <c>BehaviorIngressSystem.Clear</c> call in <c>Finish</c> and the tick
        /// count runs past 1 (the interpreter resets on root completion and re-runs).</para>
        /// </summary>
        [Fact]
        public void CE449_AFinishedBTree_IsClearedAndNotRerun()
        {
            var world = TestWorldFactory.Create();
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
            const int behaviorId = 8101;
            int ticks = 0;
            var registry  = new BehaviorRegistry();
            var actionReg = new ActionRegistry<byte, BTreeContext>();
            actionReg.Register("Once", (ref byte _, ref BehaviorTreeState _, ref BTreeContext _, int _) =>
            {
                ticks++;
                return NodeStatus.Success;
            });
            registry.Register(behaviorId, "Once", new BehaviorDefinition
            {
                Name             = "Once",
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(BuildSingleActionBlob("Once"), actionReg),
            });
            var sys = new BrainTickSystem(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = behaviorId, BrainTier = BehaviorConstants.BrainTierBTree, InstanceId = 7,
            });
            RootStateAccess.EnsureRootState(world, e);
            Assert.Equal(1, LiveSlots(world, e));   // the root tree state slot exists before the finish

            for (int i = 0; i < 4; i++) { sys.Execute(world, 0.016f); world.Bus.SwapBuffers(); }

            Assert.Equal(1, ticks);
            var state = world.GetComponent<BehaviorState>(e);
            Assert.Equal(0, state.BrainTier);
            Assert.Equal(BehaviorIds.None, state.ActiveBehaviorHash);
            Assert.Equal(8u, state.InstanceId);
            // ⚠ Scan the slot TABLE — TryGetState keys by the CURRENT hash, which the clear set to None, so it would
            //   report "gone" even for a leaked slot.
            Assert.Equal(0, LiveSlots(world, e));

            world.Dispose();
        }

        [Fact]
        public void BehaviorRoot_Success_PublishesBehaviorFinishedEvent()
        {
            var world = TestWorldFactory.Create();
            const int behaviorId = 8001;
            var (_, sys) = BuildTerminalSystem(world, behaviorId, "SuccessDoc", NodeStatus.Success);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = behaviorId, BrainTier = BehaviorConstants.BrainTierBTree });
            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

            sys.Execute(world, 0.016f);

            // Events published by the system land in the write buffer; swap to read them.
            world.Bus.SwapBuffers();
            var events = world.Bus.Read<BehaviorFinishedEvent>();

            int count = 0;
            BehaviorFinishedEvent? found = null;
            foreach (var evt in events)
            {
                if (evt.Entity.Index == e.Index) { found = evt; count++; }
            }

            Assert.Equal(1, count);
            Assert.NotNull(found);
            Assert.Equal(NodeStatus.Success, found!.Value.Result);

            world.Dispose();
        }

        [Fact]
        public void BehaviorRoot_Failure_PublishesBehaviorFinishedEvent()
        {
            var world = TestWorldFactory.Create();
            const int behaviorId = 8002;
            var (_, sys) = BuildTerminalSystem(world, behaviorId, "FailureDoc", NodeStatus.Failure);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = behaviorId, BrainTier = BehaviorConstants.BrainTierBTree });
            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

            sys.Execute(world, 0.016f);

            world.Bus.SwapBuffers();
            var events = world.Bus.Read<BehaviorFinishedEvent>();

            BehaviorFinishedEvent? found = null;
            foreach (var evt in events)
                if (evt.Entity.Index == e.Index) found = evt;

            Assert.NotNull(found);
            Assert.Equal(NodeStatus.Failure, found!.Value.Result);

            world.Dispose();
        }

        [Fact]
        public void BehaviorRoot_Running_DoesNotPublishEvent()
        {
            var world = TestWorldFactory.Create();
            const int behaviorId = 8003;
            var (_, sys) = BuildTerminalSystem(world, behaviorId, "RunningDoc", NodeStatus.Running);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = behaviorId, BrainTier = BehaviorConstants.BrainTierBTree });
            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

            sys.Execute(world, 0.016f);

            world.Bus.SwapBuffers();
            var events = world.Bus.Read<BehaviorFinishedEvent>();

            bool anyForEntity = false;
            foreach (var evt in events)
                if (evt.Entity.Index == e.Index) anyForEntity = true;

            Assert.False(anyForEntity);

            world.Dispose();
        }

        [Fact]
        public void BehaviorRoot_Success_PublishedOnlyOnce()
        {
            // BTree always returns Success. Event must be published on frame 1 but NOT frame 2
            // (same InstanceId — suppressed by _publishedTerminalForInstanceId guard).
            var world = TestWorldFactory.Create();
            const int behaviorId = 8004;
            var (_, sys) = BuildTerminalSystem(world, behaviorId, "AlwaysSuccessDoc", NodeStatus.Success);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = behaviorId, BrainTier = BehaviorConstants.BrainTierBTree });
            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

            // Frame 1: expect event.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            int frame1Count = 0;
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index) frame1Count++;

            // Frame 2: same InstanceId — must NOT re-publish.
            sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            int frame2Count = 0;
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index) frame2Count++;

            Assert.Equal(1, frame1Count);
            Assert.Equal(0, frame2Count);

            world.Dispose();
        }

        [Fact]
        public void BehaviorFinished_NotPublishedByLocomotionDispatcher()
        {
            // Running LocomotionDispatcherSystem alone (no BrainTickSystem) must NOT
            // produce a BehaviorFinishedEvent even when the executor sets channel status.
            var world = TestWorldFactory.Create();

            var dispatcher = new LocomotionDispatcherSystem();
            var spy        = new WritingSpyExecutor<LocomotionChannel>(); // writes Status = Running
            dispatcher.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = 1, BrainTier = BehaviorConstants.BrainTierBTree, InstanceId = 1 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction         = 1,
                ActionInstanceId     = 1,
                BehaviorInstanceId   = 1,
                DispatchedInstanceId = 0, // triggers OnEnter + Execute
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            dispatcher.Execute(world, 0.016f);

            // No BehaviorFinishedEvent should appear on the bus.
            world.Bus.SwapBuffers();
            bool anyEvent = false;
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index != 0) anyEvent = true;

            Assert.False(anyEvent);

            world.Dispose();
        }

        // ── CORRECTIVE-1 Test: memory leak pruning ───────────────────────────────

        [Fact]
        public void DestroyedEntity_PrunedFromTerminalTrackingDictionary()
        {
            // Arrange: terminal behavior so the deduplication dictionary gets an entry.
            var world = TestWorldFactory.Create();
            world.RegisterEvent<DestructionOrder>();
            const int behaviorId = 8010;
            var (_, sys) = BuildTerminalSystem(world, behaviorId, "PruneDoc", NodeStatus.Success);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = behaviorId,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

            // Frame 1: entity processed, entry added to deduplication dictionary.
            sys.Execute(world, 0.016f);
            Assert.Equal(1, sys.TrackedEntityCount);

            // Simulate ELM publishing DestructionOrder, then destroy the entity.
            world.Bus.Publish(new DestructionOrder { Entity = e });
            world.Bus.SwapBuffers();
            world.DestroyEntity(e);

            // Frame 2: DestructionOrder in read buffer → entry pruned from dictionary.
            sys.Execute(world, 0.016f);
            Assert.Equal(0, sys.TrackedEntityCount);

            world.Dispose();
        }
        // ── Test 4 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BTreeTick_DoesNotTick_WhenPausedFlagIsSet()
        {
            // Arrange: entity with a registered BTree behavior whose action counts ticks.
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();

            int tickCount = 0;
            var blob      = BuildSingleActionBlob("CountTickPaused");
            var actionReg = new ActionRegistry<byte, BTreeContext>();
            actionReg.Register("CountTickPaused",
                (ref byte _, ref BehaviorTreeState _, ref BTreeContext _, int _) =>
                {
                    tickCount++;
                    return NodeStatus.Running;
                });
            var interpreter = new Interpreter<byte, BTreeContext>(blob, actionReg);

            const string behaviorName = "CountTickPaused";
            const int   behaviorId   = 9010;
            registry.Register(behaviorId, behaviorName, new BehaviorDefinition
            {
                Name             = behaviorName,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = interpreter,
            });

            var sys = new BrainTickSystem(registry);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = behaviorId,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            // ⭐ O7c-②: the Paused flag is seeded THROUGH the slot — the entity carries a hash, so
            //   EnsureRootState can provision it before the first tick.
            RootStateAccess.EnsureRootState(world, e);
            RootStateAccess.SetState(world, e,
                new Fbt.BehaviorTreeState { InstanceFlags = BehaviorInstanceFlags.Paused });

            // Act: tick while Paused -- action must not run.
            sys.Execute(world, 0.016f);
            Assert.Equal(0, tickCount);

            // Resume: clear the Paused flag, tick again -- action must now run.
            ref var stateRef = ref RootStateAccess.RequireStateRef(world, e);   // ⛔ O7c-②: via the slot.
            stateRef.InstanceFlags &= ~BehaviorInstanceFlags.Paused;
            sys.Execute(world, 0.016f);
            Assert.Equal(1, tickCount);

            world.Dispose();
        }

        // ── End of BTreeTickSystemTests ──────────────────────────────────────────
    
        // ══ CE-505 — a steady-state BTree brain tick allocates NOTHING ════════════════════════════
        //   🔒 User 2026-10-01: "there should be no allocation on the hot path." The HSM twin is
        //   BrainTickSystemHsmArmTests.CE505_R3. ⭐ REAL generated behaviours through the REAL registrar
        //   scan, ingress and BrainTickSystem — root state + root params lookups, composed blueprint
        //   actions (T35: AiPrimitiveTickCore, one Behavior-shared and one Node-scoped occurrence) and
        //   C# reusable + stateful actions (T20).

        [Theory]
        [InlineData("T35_SharedWorkingState", "")]
        // ⚠ T20 FINISHES after 7 ticks on its authored limits (cursorB.Limit = 5) and is then cleared, so
        //   cursorB's limit is raised: A succeeds from tick 3 and B stays Running — both stateful nodes
        //   tick every frame.
        [InlineData("T20_MultiStateful", "{\"cursorB\":{\"Limit\":1000000}}")]
        public void CE505_R4_ASteadyStateBTreeBrainTick_AllocatesNothing(string behaviourName, string json)
        {
            var behaviours = new BehaviorRegistry();
            Fdp.Toolkit.Blueprints.BlueprintRegistrarScanner.Scan(
                typeof(global::Hrot.AI.Behaviors.Trees.T35_SharedWorkingState_Block).Assembly,
                new Fdp.Toolkit.Blueprints.BlueprintRegistry().BeginStaging(),
                behaviours);
            Assert.True(behaviours.TryGetId(behaviourName, out int hash), $"{behaviourName} must self-register");

            var world = TestWorldFactory.Create();
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviourName, JsonParams = json });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(behaviours).Execute(world, 0.016f);
            Assert.Equal(BehaviorConstants.BrainTierBTree, world.GetComponentRO<BehaviorState>(e).BrainTier);

            var sys = new BrainTickSystem(behaviours);
            for (int i = 0; i < 24; i++) sys.Execute(world, 0.016f);   // JIT, first attach, dictionary growth
            world.Bus.SwapBuffers();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) sys.Execute(world, 0.016f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // ⛔ NON-VACUITY: still the same running brain — a finished tree is cleared (CE-449) and
            //    would tick nothing at all.
            ref readonly var st = ref world.GetComponentRO<BehaviorState>(e);
            Assert.Equal(hash, st.ActiveBehaviorHash);
            Assert.Equal(BehaviorConstants.BrainTierBTree, st.BrainTier);
            Assert.True(allocated == 0, $"50 steady-state {behaviourName} brain ticks allocated {allocated} bytes");

            world.Dispose();
        }

        // ══ CE-417 B-2 (a′), F8 — a [SharedAiAction] bound in a BTree ASSET, called per binding ═══════════════════
        //   📄 docs/blueprints/DESIGN_Behavior_Action_Binding.md §4 B-2, slice 3b. The BTree twin of
        //   BrainTickSystemHsmArmTests.CE417_R1/R3. BTreeCuratedBindingDemo binds ONE curated method
        //   (HsmTwoRegionCuratedNodes.Action_ReadRegionParams, which does dto.Seen++) to varA (offset 8) and varB (16);
        //   varC (offset 0) is bound by nothing. ⛔ Before CE-417 the asset was SKIPPED (BTREE0002: its 3-param call did
        //   not fit the method), and the analyzer's per-METHOD adapter read the attribute DTO's offset — 0, i.e. varC.

        private const int BtVarC = 0, BtVarA = 8, BtVarB = 16;

        private static (EntityRepository world, BrainTickSystem sys, Entity e) ArrangeBTreeCuratedBindingDemo()
        {
            var behaviours = new BehaviorRegistry();
            Fdp.Toolkit.Blueprints.BlueprintRegistrarScanner.Scan(
                typeof(global::Hrot.AI.Behaviors.Trees.T35_SharedWorkingState_Block).Assembly,
                new Fdp.Toolkit.Blueprints.BlueprintRegistry().BeginStaging(),
                behaviours);
            Assert.True(behaviours.TryGetId("BTreeCuratedBindingDemo", out int hash), "the generated registrar must register it");

            var world = TestWorldFactory.Create();
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = "BTreeCuratedBindingDemo", JsonParams = string.Empty });
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(behaviours).Execute(world, 0.016f);
            Assert.Equal(hash, world.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash);
            return (world, new BrainTickSystem(behaviours), e);
        }

        private static unsafe global::Hrot.AI.Behaviors.Brains.HsmTwoRegionCuratedNodes.CuratedRegionParams BtVar(
            EntityRepository w, Entity e, int offset)
        {
            byte* root = RootParamsAccess.RequireRootBytes(w, e, out int len);
            Assert.True(offset + 8 <= len);
            return *(global::Hrot.AI.Behaviors.Brains.HsmTwoRegionCuratedNodes.CuratedRegionParams*)(root + offset);
        }

        /// <summary>⭐⭐⭐ <c>CE-417</c> F8 — one C# <c>[SharedAiAction]</c> bound to two variables in a BTree asset: each binding
        /// moves ONLY its own variable, every tick; the unbound one at the attribute DTO's offset (0) never moves.</summary>
        [Fact]
        public void CE417_R4_ASharedAiActionBoundTwiceInABTree_EachBindingMovesOnlyItsOwnVariable()
        {
            var (world, sys, e) = ArrangeBTreeCuratedBindingDemo();
            for (int i = 0; i < 12; i++) sys.Execute(world, 0.016f);

            var a = BtVar(world, e, BtVarA); var b = BtVar(world, e, BtVarB); var c = BtVar(world, e, BtVarC);
            Assert.Equal(12, a.Seen);   // a forever repeater: one pass per tick
            Assert.Equal(12, b.Seen);
            Assert.Equal(0, c.Seen);    // ⛔ the retired adapter's offset
            Assert.Equal(1, a.Value);   // the call wrote through its ref, nothing else
            world.Dispose();
        }

        /// <summary>⭐ <c>CE-417</c> + <c>CE-505</c> — the per-binding BTree calls allocate nothing.</summary>
        [Fact]
        public void CE417_R5_PerBindingBTreeCSharpCalls_AllocateNothing()
        {
            var (world, sys, e) = ArrangeBTreeCuratedBindingDemo();
            for (int i = 0; i < 24; i++) sys.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            int seenBefore = BtVar(world, e, BtVarA).Seen;

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) sys.Execute(world, 0.016f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(seenBefore + 50, BtVar(world, e, BtVarA).Seen);   // NON-VACUITY: the call really ran every tick
            Assert.True(allocated == 0, $"50 ticks of two per-binding BTree C# actions allocated {allocated} bytes");
            world.Dispose();
        }
    }
}
