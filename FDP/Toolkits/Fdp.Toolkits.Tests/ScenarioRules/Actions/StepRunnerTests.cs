using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules.Actions;
using Xunit;
using static Fdp.Toolkit.ScenarioRules.Tests.Actions.ActionKit;

namespace Fdp.Toolkit.ScenarioRules.Tests.Actions
{
    /// <summary>⭐ rules S2-d (CE-4160/4161/4162) — one step slot: seeding from JSON, params intact, bytes not leaked, the outer ref valid (the PoC's P2 rails).</summary>
    public sealed unsafe class StepRunnerTests
    {
        private static int Key => StepRunner.SlotKey(0);

        [Fact]
        public void ParamsArriveIntact_FromJson_IncludingFixedStringEnumFloat()
        {
            using var w = NewWorld();
            var e = Unit(w);
            var run = ActionCatalogue.RunnerFor("TestRich");
            run.Tick(w, e, Key, "{\"name\":\"alpha\",\"mode\":\"Fast\",\"gain\":1.5,\"n\":7}");
            var rp = (RichParams)run.Peek(w, e, Key).Params;
            Assert.Equal("alpha", rp.Name.ToString());
            Assert.Equal(TestMode.Fast, rp.Mode);
            Assert.Equal(1.5f, rp.Gain);
            Assert.Equal(7, rp.N);
            Assert.Equal(7, w.GetComponentRO<ActionTestLog>(e).V0);
        }

        [Fact]
        public void AbsentJsonKeys_KeepTheStructsDefaults()
        {
            using var w = NewWorld();
            var e = Unit(w);
            ActionCatalogue.RunnerFor("TestWaitTicks").Tick(w, e, Key, "{}");
            Assert.Equal(0, PeekWait(w, e).P.Ticks);
        }

        [Fact]
        public void BadArgsJson_LeavesNoHalfSeededSlot_AndThrows()
        {
            using var w = NewWorld();
            var e = Unit(w);
            var run = ActionCatalogue.RunnerFor("TestRich");
            Assert.ThrowsAny<Exception>(() => run.Tick(w, e, Key, "{\"mode\":\"Warp9\"}"));   // strict enum
            Assert.False(ActionHost.Holds(w, e, Key));
            Assert.ThrowsAny<Exception>(() => run.Tick(w, e, Key, "{not json"));
            Assert.False(ActionHost.Holds(w, e, Key));
        }

        // ── CE-4162: an authored reference is an EntityLink, resolved when the step starts ────────────────────

        private static Entity Keyed(EntityRepository w, string key, int networkId)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity { Value = networkId });
            w.AddComponent(e, new RefKey(key));
            return e;
        }

        private static EntityRepository LinkWorld()
        {
            var w = NewWorld();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<RefKey>();
            return w;
        }

        [Fact]
        public void CE4162_AnEntityLinkArg_AuthoredAsAKey_IsResolvedWhenTheStepStarts()
        {
            using var w = LinkWorld();
            var e = Unit(w);
            Keyed(w, "BlueHQ", 4711);
            var run = ActionCatalogue.RunnerFor("TestLogLink");
            Assert.Equal(NodeStatus.Success, run.Tick(w, e, Key, "{\"target\":\"@BlueHQ\"}"));
            Assert.Equal(4711, w.GetComponentRO<ActionTestLog>(e).V0);           // ← the action saw the resolved entity (mutation: skip ResolveLinks → 0)
            var rp = (RichParams)run.Peek(w, e, Key).Params;
            Assert.Equal(4711, rp.Target.Ref.NetworkId);
            Assert.Equal("@BlueHQ", rp.Target.Spec.ToString());                  // the authored form is kept
        }

        [Fact]
        public void CE4162_AnUnresolvedLink_FailsTheStep_WithAReason_AndLeavesNoSlot()
        {
            using var w = LinkWorld();
            var e = Unit(w);
            var run = ActionCatalogue.RunnerFor("TestLogLink");
            Assert.Equal(NodeStatus.Failure, run.Tick(w, e, Key, "{\"target\":\"@Nobody\"}"));
            Assert.False(ActionHost.Holds(w, e, Key));
            Assert.True(ActionFailure.TryGet(w, e, out var f));
            Assert.Contains("Nobody", f.Reason.ToString());
            Assert.Equal(0, w.GetComponentRO<ActionTestLog>(e).Count);           // the action never ran
        }

        [Fact]
        public void CE4162_ABareNetworkIdLink_NeedsNoResolutionAndNoScan()
        {
            using var w = NewWorld();                                            // NetworkIdentity / RefKey are NOT even registered here
            var e = Unit(w);
            var run = ActionCatalogue.RunnerFor("TestLogLink");
            Assert.Equal(NodeStatus.Success, run.Tick(w, e, Key, "{\"target\":99}"));
            Assert.Equal(99, w.GetComponentRO<ActionTestLog>(e).V0);
        }

        // ── CE-4161: kinds ────────────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(OccurrenceKind.BTree)]
        [InlineData(OccurrenceKind.BlueprintBehavior)]
        public void CE4161_TheLazyAttachSeam_RefusesTheRootKinds(OccurrenceKind kind)
        {
            // ⭐ FLIPPED from the PoC finding P4_FINDING_WithKindBTree_TheOrphanedStepSlotLeaks: a root kind attached lazily would leak on a behaviour change.
            using var w = NewWorld();
            var e = Unit(w);
            var ex = Assert.Throws<InvalidOperationException>(() => OccurrenceWorkingState.ResolveOrAttach<WaitState>(w, e, 0x1234, 1, kind, out _));
            Assert.Contains("CE-4161", ex.Message);
            Assert.Throws<InvalidOperationException>(() => OccurrenceWorkingState.ResolveOrAttach<WaitParams, WaitState>(w, e, 0x1235, 1, kind, out _, out _));
            Assert.False(ActionHost.Holds(w, e, 0x1234));                        // refused BEFORE anything was attached
        }

        [Theory]
        [InlineData(OccurrenceKind.Hsm)]
        [InlineData(OccurrenceKind.Blueprint)]
        [InlineData(OccurrenceKind.UnitMemory)]
        public void CE4161_TheOtherKinds_StillAttach(OccurrenceKind kind)
        {
            using var w = NewWorld();
            var e = Unit(w);
            OccurrenceWorkingState.ResolveOrAttach<WaitState>(w, e, 0x1234, 1, kind, out bool fresh);
            Assert.True(fresh);
        }

        [Fact]
        public void TheStepSlotKind_IsHsm()
        {
            Assert.Equal(OccurrenceKind.Hsm, StepRunner.Kind);
        }

        // ── per-step slot lifetime ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void OnlyOneStepSlotExists_AtATime_AndNoneAfterTheEnd()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestRecord", "{\"value\":1}"), Step("TestWaitTicks", "{\"ticks\":3}"), Step("TestRecord", "{\"value\":2}")));
            var seen = new List<int>();
            NodeStatus st;
            do { st = ActionHost.Tick(w, e, 0, out _); seen.Add(ActionHost.SlotCount(w, e)); } while (st == NodeStatus.Running);

            Assert.Equal(new[] { 2, 2, 0 }, seen);                               // Running ticks: outer + ONE step slot; terminal tick: both detached
            Assert.False(ActionHost.Holds(w, e, Key));
            Assert.False(ActionHost.Holds(w, e, ActionHost.OuterSlotKey));
        }

        [Fact]
        public void WorkingState_PersistsAcrossTicks_ParamsStayPut()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestWaitTicks", "{\"ticks\":4}")));
            for (int i = 1; i <= 3; i++)
            {
                Assert.Equal(NodeStatus.Running, ActionHost.Tick(w, e, 0, out _));
                var (p, s) = PeekWait(w, e);
                Assert.Equal((4, i), (p.Ticks, s.Elapsed));
            }
        }

        [Fact]
        public void DetachThenReattachOfTheNextStep_DoesNotCorruptOrLeakStaleBytes()
        {
            using var w = NewWorld();
            // step 0 is WIDE (RichParams), step 1 is narrow: it reuses the freed bytes; its working state must start ZERO and its params be its own
            var e = Unit(w, Steps(Step("TestRich", "{\"n\":41,\"gain\":9.5,\"name\":\"zzzz\"}"), Step("TestWaitTicks", "{\"ticks\":2}"), Step("TestRecord", "{\"value\":3}")));
            Assert.Equal(NodeStatus.Running, ActionHost.Tick(w, e, 0, out _));
            var (p, s) = PeekWait(w, e);
            Assert.Equal((2, 1), (p.Ticks, s.Elapsed));
            Assert.Equal(NodeStatus.Success, ActionHost.Tick(w, e, 0, out var fin));
            Assert.Equal(3, fin.Index);
            var log = w.GetComponentRO<ActionTestLog>(e);
            Assert.Equal((2, 41, 3), (log.Count, log.V0, log.V1));
        }

        [Fact]
        public void TRAP_TheOuterWorkingStateRef_StaysValid_WhileTheInnerSlotIsAttachedAndDetached_SameBlock()
        {
            Trap.Reset();
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestRecord", "{\"value\":1}"), Step("TestRich", "{\"n\":2}"), Step("TestWaitTicks", "{\"ticks\":2}")));
            Assert.Equal(NodeStatus.Running, ActionHost.Tick(w, e, 0, out _));   // one call: 2 attaches + 2 detaches, the outer ref held throughout
            Assert.True(Trap.Checks >= 1);
            Assert.Equal(0, Trap.Moved);
            Assert.Equal(1, ActionHost.BlockCount(w, e));                        // it really was the same-block case
        }

        [Fact]
        public void TRAP_TheOuterRef_StaysValid_WhenTheStepDoesNotFitTheFirstBlock_AppendPath()
        {
            Trap.Reset();
            using var w = NewWorld();
            var e = w.CreateEntity();
            w.AddComponent(e, new ActionTestLog()); w.AddComponent(e, new ActionFailure { Step = -1 });
            w.SetManagedComponent(e, new ActionArgs(new[] { Steps(Step("TestRecord", "{\"value\":1}"), Step("TestRich", "{\"n\":2}"), Step("TestWaitTicks", "{\"ticks\":2}")) }));
            w.AddComponent(e, new BlueprintBlackboard256());                      // ONLY the smallest tier …
            ref var tier = ref w.GetComponentRW<BlueprintBlackboard256>(e);
            fixed (byte* mem = tier.Memory)
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard256.TotalSize, (byte)BlueprintBlackboard256.MaxSlots);

            // the outer first (it lands in block 0) …
            OccurrenceWorkingState.ResolveOrAttach<RunActionsParams, RunActionsState>(w, e, ActionHost.OuterSlotKey, 0x0C0FFEE, OccurrenceKind.Hsm, out _, out RunActionsParams* pp);
            pp->ListRef = 0;
            OccurrenceStoreAccess.TryFindSlot(w, e, ActionHost.OuterSlotKey, out byte* blk0, out int outerOff, out _);
            nint outerAddr = (nint)(blk0 + outerOff);
            // … then FILL block 0 (raw TryAttach: never appends) so every step slot MUST append a bigger block
            for (int k = 0; BlueprintBlackboardPartitions.TryAttach(blk0, 0x6000 + k, 16, 0x1, OccurrenceKind.UnitMemory, out _); k++) { }
            Assert.Equal(1, ActionHost.BlockCount(w, e));

            Assert.Equal(NodeStatus.Running, ActionHost.Tick(w, e, 0, out _));   // 2 steps done + the wait started, all inside one outer call
            Assert.Equal(2, ActionHost.BlockCount(w, e));                        // the append DID happen (mid-tick, inside the outer's call)
            OccurrenceStoreAccess.TryFindSlot(w, e, ActionHost.OuterSlotKey, out byte* blkNow, out int offNow, out _);
            Assert.Equal(outerAddr, (nint)(blkNow + offNow));                    // the outer slot did not move
            Assert.Equal(NodeStatus.Success, ActionHost.Tick(w, e, 0, out _));
            Assert.Equal(0, Trap.Moved);
            var log = w.GetComponentRO<ActionTestLog>(e);
            Assert.Equal((2, 1, 2), (log.Count, log.V0, log.V1));                // every step ran, once
        }

        [Fact]
        public void TRAP_AppendingABlock_DoesNotMoveTheOuterBlock_AndAWriteThroughTheOldRefLands()
        {
            using var w = NewWorld();
            var e = w.CreateEntity();
            w.AddComponent(e, new ActionTestLog()); w.AddComponent(e, new ActionFailure { Step = -1 });
            w.AddComponent(e, new BlueprintBlackboard256());
            ref var tier = ref w.GetComponentRW<BlueprintBlackboard256>(e);
            fixed (byte* mem = tier.Memory)
                BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard256.TotalSize, (byte)BlueprintBlackboard256.MaxSlots);

            ref RunActionsState outer = ref OccurrenceWorkingState.ResolveOrAttach<RunActionsParams, RunActionsState>(
                w, e, ActionHost.OuterSlotKey, 1, OccurrenceKind.Hsm, out _, out _);
            nint before = (nint)Unsafe.AsPointer(ref outer);
            outer.Index = 5;

            int appended = 0;                                                    // exhaust block 0, forcing an APPEND (a structural change on the entity)
            for (int k = 0; k < 40 && appended == 0; k++)
            {
                OccurrenceStoreAccess.TryAttachSlot(w, e, 0x6000 + k, 48, 0x2, OccurrenceKind.UnitMemory, out _, out _);
                if (ActionHost.BlockCount(w, e) > 1) appended = k + 1;
            }
            Assert.True(appended > 0, "the append path was not reached");

            OccurrenceStoreAccess.TryDetachSlot(w, e, 0x6000);                   // and a detach in the first block
            OccurrenceStoreAccess.TryFindSlot(w, e, ActionHost.OuterSlotKey, out byte* blk, out int off, out _);
            Assert.Equal(before, (nint)(blk + off));                             // the store's OWN lookup still says the same address ⇒ nothing moved
            outer.Index = 77;                                                    // write through the OLD ref
            Assert.Equal(77, Unsafe.AsRef<RunActionsState>(blk + off).Index);    // lands in the live slot
        }
    }
}
