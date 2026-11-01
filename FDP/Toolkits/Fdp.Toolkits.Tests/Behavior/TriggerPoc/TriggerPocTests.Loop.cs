using System;
using System.Collections.Generic;
using System.Reflection;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    public sealed unsafe partial class TriggerPocTests
    {
        // ══ P3 — THE LOOP AS A SHARED ACTION ═════════════════════════════════════════════════════════════════════════

        private static readonly string ThreeSteps = Steps(
            Step("PocRecord", "{\"value\":1}"), Step("PocWaitTicks", "{\"ticks\":3}"), Step("PocRecord", "{\"value\":2}"));

        [Fact]
        public void P3_AnEmptyList_IsSuccessAtOnce_AndLeavesNothingBehind()
        {
            using var w = NewWorld();
            var e = Unit(w, "[]");
            Assert.Equal(NodeStatus.Success, PocOuter.Tick(w, e, 0, out var fin));
            Assert.Equal(0, fin.Index);
            Assert.Equal(0, PocOuter.SlotCount(w, e));
        }

        [Fact]
        public void P3_ThreeStepList_WithAMultiTickMiddleStep_RunsInOrder_ThenSucceeds()
        {
            using var w = NewWorld();
            var e = Unit(w, ThreeSteps);

            var seen = new List<NodeStatus>();
            RunState fin = default;
            for (int i = 0; i < 6; i++)
            {
                var st = PocOuter.Tick(w, e, 0, out fin);
                seen.Add(st);
                if (st != NodeStatus.Running) break;
                // while the middle step runs the FIRST record has happened, the LAST has not
                Assert.Equal(1, w.GetComponentRO<PocLog>(e).Count);
                Assert.Equal(1, fin.Index);
            }
            Assert.Equal(new[] { NodeStatus.Running, NodeStatus.Running, NodeStatus.Success }, seen);   // ← red-proof: PocWaitTicks returning Success one tick early
            var log = w.GetComponentRO<PocLog>(e);
            Assert.Equal((2, 1, 2), (log.Count, log.V0, log.V1));
            Assert.Equal(3, fin.Index);
            Assert.Equal(0, PocOuter.SlotCount(w, e));
        }

        [Fact]
        public void P3_AStepSuccess_StartsTheNextStepInTheSameCall()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("PocRecord", "{\"value\":1}"), Step("PocRecord", "{\"value\":2}"), Step("PocRecord", "{\"value\":3}")));
            Assert.Equal(NodeStatus.Success, PocOuter.Tick(w, e, 0, out _));    // three instantaneous steps = ONE tick (a design CHOICE, now pinned)
            Assert.Equal(3, w.GetComponentRO<PocLog>(e).Count);
        }

        [Fact]
        public void P3_AFailingStep_StopsTheList_DetachesItsSlot_AndRecordsWhichStepAndWhy()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("PocRecord", "{\"value\":1}"), Step("PocFail", "{\"code\":9}"), Step("PocRecord", "{\"value\":2}")));
            Assert.Equal(NodeStatus.Failure, PocOuter.Tick(w, e, 0, out var fin));
            Assert.Equal((1, 1, 9), (fin.Failed, fin.FailedStep, fin.FailReason));   // step 1, reason 9
            Assert.Equal(1, w.GetComponentRO<PocLog>(e).Count);                      // step 2 never ran
            Assert.False(PocOuter.Holds(w, e, PocActions.StepSlotKey));
            Assert.Equal(0, PocOuter.SlotCount(w, e));
        }

        [Fact]
        public void P3_AFailureAfterAMultiTickStep_IsReportedOnTheTickItHappens()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("PocWaitTicks", "{\"ticks\":2}"), Step("PocFail", "{\"code\":4}")));
            Assert.Equal(NodeStatus.Running, PocOuter.Tick(w, e, 0, out _));
            Assert.Equal(NodeStatus.Failure, PocOuter.Tick(w, e, 0, out var fin));
            Assert.Equal((1, 4), (fin.FailedStep, fin.FailReason));
        }

        [Fact]
        public void P3_CalledDirectly_WithPlainLocals_TheLoopIsIdempotentAfterItEnds()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("PocRecord", "{\"value\":1}"), Step("PocFail", "{\"code\":2}")));
            var p = new RunParams { ListRef = 0 }; var s = new RunState();     // the outer as ordinary stack memory (a generated binding's block would be like this)
            Assert.Equal(NodeStatus.Failure, PocActions.PocRunActions(ref p, ref s, e, w));
            Assert.Equal(NodeStatus.Failure, PocActions.PocRunActions(ref p, ref s, e, w));   // does not re-run the log step
            Assert.Equal(1, w.GetComponentRO<PocLog>(e).Count);

            var ok = Unit(w, Steps(Step("PocRecord", "{\"value\":7}")));
            var s2 = new RunState();
            Assert.Equal(NodeStatus.Success, PocActions.PocRunActions(ref p, ref s2, ok, w));
            Assert.Equal(NodeStatus.Success, PocActions.PocRunActions(ref p, ref s2, ok, w));
            Assert.Equal(1, w.GetComponentRO<PocLog>(ok).Count);
        }

        [Fact]
        public void P3_TwoEntities_RunDifferentLists_Interleaved_OnOneCachedRunnerSet()
        {
            using var w = NewWorld();
            var a = Unit(w, Steps(Step("PocWaitTicks", "{\"ticks\":2}"), Step("PocRecord", "{\"value\":10}")));
            var b = Unit(w, Steps(Step("PocWaitTicks", "{\"ticks\":3}"), Step("PocRecord", "{\"value\":20}")));
            var sa = new List<NodeStatus>(); var sb = new List<NodeStatus>();
            for (int i = 0; i < 3; i++)
            {
                if (sa.Count == 0 || sa[^1] == NodeStatus.Running) sa.Add(PocOuter.Tick(w, a, 0, out _));
                if (sb.Count == 0 || sb[^1] == NodeStatus.Running) sb.Add(PocOuter.Tick(w, b, 0, out _));
            }
            Assert.Equal(new[] { NodeStatus.Running, NodeStatus.Success }, sa);
            Assert.Equal(new[] { NodeStatus.Running, NodeStatus.Running, NodeStatus.Success }, sb);
            Assert.Equal(10, w.GetComponentRO<PocLog>(a).V0);
            Assert.Equal(20, w.GetComponentRO<PocLog>(b).V0);
        }

        [Fact]
        public void P3_AnUnknownActionInTheList_ThrowsAtRuntime_SoLoadTimeValidationIsOwed()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("PocRecord", "{\"value\":1}"), Step("NoSuchAct")));
            Assert.Throws<InvalidOperationException>(() => PocOuter.Tick(w, e, 0, out _));
            // ⚠ step 0 already RAN before the bad name was reached ⇒ validation must be at ASSIGNMENT (SR-91), not at first use
            Assert.Equal(1, w.GetComponentRO<PocLog>(e).Count);
        }

        // ══ P4 — CALLED FROM A REAL BEHAVIOUR (BrainTickSystem; the store is provisioned by INGRESS, not by hand) ═════

        private const int BtOkId = 0x7C01, BtFailId = 0x7C02, BtOtherId = 0x7C03;
        [ThreadStatic] private static NodeStatus _leafLast;
        [ThreadStatic] private static int _leafCalls, _otherTicks;
        [ThreadStatic] private static bool _viewWasActive;
        [ThreadStatic] private static RunState _leafFinal;

        private static bool OccurrenceTickViewActive()
        {
            var f = typeof(OccurrenceStoreAccess).GetField("t_view", BindingFlags.NonPublic | BindingFlags.Static)!;
            var v = f.GetValue(null);
            return v != null && (bool)v.GetType().GetField("Active")!.GetValue(v)!;
        }

        private static NodeStatus ListLeaf(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p)
        {
            _viewWasActive |= OccurrenceTickViewActive();
            _leafLast = PocOuter.Tick(ctx.World, ctx.Self, 0, out _leafFinal);
            _leafCalls++;
            return _leafLast;
        }
        private static NodeStatus Idle(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p) { _otherTicks++; return NodeStatus.Running; }

        private static BehaviorDefinition SeqThenIdle(string name)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(ListLeaf).Action(Idle));
            return new BehaviorDefinition
            {
                Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            };
        }
        private static BehaviorDefinition SelThenIdle(string name)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Selector(sel => sel.Action(ListLeaf).Action(Idle));
            return new BehaviorDefinition
            {
                Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            };
        }
        private static BehaviorDefinition JustIdle(string name)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(Idle));
            return new BehaviorDefinition
            {
                Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            };
        }

        private sealed class BtFixture : IDisposable
        {
            public readonly EntityRepository World = NewWorld();
            public readonly BehaviorRegistry Registry = new();
            public readonly BehaviorIngressSystem Ingress;
            public readonly BrainTickSystem Brain;
            public readonly Entity Unit;

            public BtFixture(string list)
            {
                if (!World.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) World.Bus.RegisterManaged<AssignBehaviorEvent>();
                Registry.Register(BtOkId,    "PocBtOk",    SeqThenIdle("PocBtOk"));
                Registry.Register(BtFailId,  "PocBtFail",  SelThenIdle("PocBtFail"));
                Registry.Register(BtOtherId, "PocBtOther", JustIdle("PocBtOther"));
                Ingress = new BehaviorIngressSystem(Registry);
                Brain   = new BrainTickSystem(Registry);
                Unit    = World.CreateEntity();
                World.AddComponent(Unit, new BehaviorState());
                World.AddComponent(Unit, new PocLog());
                World.AddComponent(Unit, new PocLastReason());
                World.SetManagedComponent(Unit, new PocActionArgs { Items = new[] { list } });
                _leafLast = NodeStatus.Running; _leafCalls = _otherTicks = 0; _viewWasActive = false; _leafFinal = default;
                PocTrap.Reset();
            }

            public void Assign(string name)
            {
                World.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = name, JsonParams = "", Origin = BehaviorOrigin.Superior });
                World.Bus.SwapBuffers();
                Ingress.Execute(World, 0.016f);
            }
            public void Frames(int n) { for (int i = 0; i < n; i++) Brain.Execute(World, 0.016f); }
            public void Dispose() => World.Dispose();
        }

        [Fact]
        public void P4_RunActionsAsAnodeOfARealBTree_RunsTheListThroughBrainTick_InsideTheAmbientTickView()
        {
            using var f = new BtFixture(ThreeSteps);
            f.Assign("PocBtOk");
            Assert.True(OccurrenceStoreAccess.HasStore(f.World, f.Unit));          // the store came from INGRESS (nothing added by hand)

            f.Frames(1);                                                            // record 1, wait 1/3  → Running
            Assert.Equal(NodeStatus.Running, _leafLast);
            Assert.Equal(1, f.World.GetComponentRO<PocLog>(f.Unit).Count);
            f.Frames(2);                                                            // wait 3/3, record 2 → Success
            Assert.Equal(NodeStatus.Success, _leafLast);
            var log = f.World.GetComponentRO<PocLog>(f.Unit);
            Assert.Equal((2, 1, 2), (log.Count, log.V0, log.V1));
            Assert.True(_viewWasActive, "the leaf did not run inside OccurrenceStoreAccess.BeginTickView");   // ← the premise: inner attach happens INSIDE the ambient view
            f.Frames(2);
            Assert.True(_otherTicks >= 1);                                          // the sequence moved on to the next node: the list's Success propagated
            Assert.Equal(3, _leafCalls);                                            // and the list did not run again
        }

        [Fact]
        public void P4_AFailingList_FailsTheBTreeNode_AndTheSelectorFallsThrough()
        {
            using var f = new BtFixture(Steps(Step("PocRecord", "{\"value\":1}"), Step("PocFail", "{\"code\":6}")));
            f.Assign("PocBtFail");
            f.Frames(1);
            Assert.Equal(NodeStatus.Failure, _leafLast);
            Assert.Equal((1, 1, 6), (_leafFinal.Failed, _leafFinal.FailedStep, _leafFinal.FailReason));
            Assert.True(_otherTicks >= 1);                                          // the selector took its next branch in the same tick
        }

        [Fact]
        public void P4_TheStepSlotsOfALongList_AppendBlocksInsideTheTick_AndNothingIsLost()
        {
            // wide steps, one per tick: whatever the ingress-sized first block is, the lazily attached slots must fit or append
            // 📐 measured: ingress sizes the unit's first block EXACTLY (BlueprintBlackboard256, 3 slots / 176 B payload = root cursor + outer + ONE step),
            //   so thin steps swap through the one free index and never append. A FAT step (> 176 B) must append inside the tick.
            var steps = new List<string>();
            for (int i = 0; i < 4; i++) { steps.Add(Step("PocFat", "{\"n\":" + (i + 1) + ",\"a\":\"x\"}")); steps.Add(Step("PocWaitTicks", "{\"ticks\":2}")); }
            using var f = new BtFixture(Steps(steps.ToArray()));
            f.Assign("PocBtOk");
            int blocksBefore = PocOuter.BlockCount(f.World, f.Unit);
            Assert.Equal(1, blocksBefore);
            f.Frames(1);
            Assert.Equal(2, PocOuter.BlockCount(f.World, f.Unit));                  // ← the append happened INSIDE BrainTick's ambient view, under the outer's live ref
            OccurrenceStoreAccess.TryFindSlot(f.World, f.Unit, PocOuter.OuterSlotKey, out byte* ob, out int oo, out _);
            nint outerAddr = (nint)(ob + oo);
            f.Frames(1);
            OccurrenceStoreAccess.TryFindSlot(f.World, f.Unit, PocOuter.OuterSlotKey, out byte* ob2, out int oo2, out _);
            Assert.Equal(outerAddr, (nint)(ob2 + oo2));                             // the outer's slot did not move across the append / detach / re-attach
            f.Frames(6);
            Assert.Equal(NodeStatus.Success, _leafLast);
            var log = f.World.GetComponentRO<PocLog>(f.Unit);
            Assert.Equal(4, log.Count);                                             // each fat step logged exactly once
            for (int i = 0; i < 4; i++) Assert.Equal(i + 1, log.At(i));
            Assert.Equal(0, PocTrap.Moved);
            Assert.Equal(2, PocOuter.BlockCount(f.World, f.Unit));                  // one appended block, reused by every later fat step
        }

        [Fact]
        public void P4_ABehaviourChangeMidStep_SweepsTheOrphanedStepSlot_BecauseItsKindIsHsm()
        {
            using var f = new BtFixture(Steps(Step("PocWaitTicks", "{\"ticks\":50}")));
            f.Assign("PocBtOk");
            f.Frames(2);
            Assert.True(PocOuter.Holds(f.World, f.Unit, PocActions.StepSlotKey));
            Assert.True(PocOuter.Holds(f.World, f.Unit, PocOuter.OuterSlotKey));

            f.Assign("PocBtOther");                                                 // the task is REPLACED while the step is mid-flight
            Assert.False(PocOuter.Holds(f.World, f.Unit, PocActions.StepSlotKey), "the orphaned step slot leaked");
            Assert.False(PocOuter.Holds(f.World, f.Unit, PocOuter.OuterSlotKey), "the orphaned outer slot leaked");
        }

        [Fact]
        public void P4_FINDING_WithKindBTree_TheOrphanedStepSlotLeaks()
        {
            var saved = PocStepRunner.Kind;
            try
            {
                PocStepRunner.Kind = OccurrenceKind.BTree;
                using var f = new BtFixture(Steps(Step("PocWaitTicks", "{\"ticks\":50}")));
                f.Assign("PocBtOk");
                f.Frames(2);
                f.Assign("PocBtOther");
                Assert.True(PocOuter.Holds(f.World, f.Unit, PocActions.StepSlotKey));   // ⛔ ingress sweeps only kinds Hsm|Blueprint ⇒ a BTree-kind step slot is NOT cleaned
            }
            finally { PocStepRunner.Kind = saved; }
        }
    }
}
