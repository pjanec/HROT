using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules.Actions;
using Xunit;
using static Fdp.Toolkit.ScenarioRules.Tests.Actions.ActionKit;

namespace Fdp.Toolkit.ScenarioRules.Tests.Actions
{
    /// <summary>⭐ rules S2-d (SR-98, CE-4161, CE-4165) — the list runner as a shared action (the PoC's P3 + P6 rails, plus the depth salt and the failure reason).</summary>
    public sealed unsafe class RunActionsTests
    {
        private static readonly string ThreeSteps = Steps(
            Step("TestRecord", "{\"value\":1}"), Step("TestWaitTicks", "{\"ticks\":3}"), Step("TestRecord", "{\"value\":2}"));

        [Fact]
        public void AnEmptyList_IsSuccessAtOnce_AndLeavesNothingBehind()
        {
            using var w = NewWorld();
            var e = Unit(w, "[]");
            Assert.Equal(NodeStatus.Success, ActionHost.Tick(w, e, 0, out var fin));
            Assert.Equal(0, fin.Index);
            Assert.Equal(0, ActionHost.SlotCount(w, e));
        }

        [Fact]
        public void ThreeStepList_WithAMultiTickMiddleStep_RunsInOrder_ThenSucceeds()
        {
            using var w = NewWorld();
            var e = Unit(w, ThreeSteps);

            var seen = new List<NodeStatus>();
            RunActionsState fin = default;
            for (int i = 0; i < 6; i++)
            {
                var st = ActionHost.Tick(w, e, 0, out fin);
                seen.Add(st);
                if (st != NodeStatus.Running) break;
                Assert.Equal(1, w.GetComponentRO<ActionTestLog>(e).Count);     // while the middle step runs the FIRST record has happened, the LAST has not
                Assert.Equal(1, fin.Index);
            }
            Assert.Equal(new[] { NodeStatus.Running, NodeStatus.Running, NodeStatus.Success }, seen);   // ← red-proof: TestWaitTicks returning Success one tick early
            var log = w.GetComponentRO<ActionTestLog>(e);
            Assert.Equal((2, 1, 2), (log.Count, log.V0, log.V1));
            Assert.Equal(3, fin.Index);
            Assert.Equal(0, ActionHost.SlotCount(w, e));
        }

        [Fact]
        public void AStepSuccess_StartsTheNextStepInTheSameCall()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestRecord", "{\"value\":1}"), Step("TestRecord", "{\"value\":2}"), Step("TestRecord", "{\"value\":3}")));
            Assert.Equal(NodeStatus.Success, ActionHost.Tick(w, e, 0, out _));   // three instantaneous steps = ONE tick (a design CHOICE, pinned)
            Assert.Equal(3, w.GetComponentRO<ActionTestLog>(e).Count);
        }

        [Fact]
        public void AFailingStep_StopsTheList_DetachesItsSlot_AndRecordsWhichStepAndWhy()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestRecord", "{\"value\":1}"), Step("TestFail", "{\"code\":9}"), Step("TestRecord", "{\"value\":2}")));
            Assert.Equal(NodeStatus.Failure, ActionHost.Tick(w, e, 0, out var fin));
            Assert.Equal((1, 1), (fin.Failed, fin.FailedStep));
            Assert.Equal(1, w.GetComponentRO<ActionTestLog>(e).Count);           // step 2 never ran
            Assert.False(ActionHost.Holds(w, e, StepRunner.SlotKey(0)));
            Assert.Equal(0, ActionHost.SlotCount(w, e));
        }

        [Fact]
        public void CE4165_AFailureRecordsItsStepAndReason_InTheOneActionFailureComponent()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestRecord", "{\"value\":1}"), Step("TestFail", "{\"code\":9}")));
            ActionHost.Tick(w, e, 0, out _);
            var f = w.GetComponentRO<ActionFailure>(e);
            Assert.Equal(1, f.Step);                                              // ← red-proof: drop the Stamp ⇒ Step stays -1
            Assert.Equal("code 9", f.Reason.ToString());
            Assert.True(ActionFailure.TryGet(w, e, out _));
        }

        [Fact]
        public void CE4165_AnActionThatGivesNoReason_GetsAGenericOne_NamingTheAct()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestFailSilently")));
            ActionHost.Tick(w, e, 0, out _);
            var f = w.GetComponentRO<ActionFailure>(e);
            Assert.Equal(0, f.Step);
            Assert.Contains("TestFailSilently", f.Reason.ToString());
        }

        [Fact]
        public void CE4165_AStaleReason_IsNotReadAsTheNextRunsFailure()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestFail", "{\"code\":1}")), Steps(Step("TestFailSilently")));
            ActionHost.Tick(w, e, 0, out _);
            Assert.Equal("code 1", w.GetComponentRO<ActionFailure>(e).Reason.ToString());
            ActionHost.Tick(w, e, 1, out _);                                     // a different list, a failure that says nothing
            Assert.DoesNotContain("code 1", w.GetComponentRO<ActionFailure>(e).Reason.ToString());
        }

        [Fact]
        public void AFailureAfterAMultiTickStep_IsReportedOnTheTickItHappens()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestWaitTicks", "{\"ticks\":2}"), Step("TestFail", "{\"code\":4}")));
            Assert.Equal(NodeStatus.Running, ActionHost.Tick(w, e, 0, out _));
            Assert.Equal(NodeStatus.Failure, ActionHost.Tick(w, e, 0, out var fin));
            Assert.Equal(1, fin.FailedStep);
            Assert.Equal("code 4", w.GetComponentRO<ActionFailure>(e).Reason.ToString());
        }

        [Fact]
        public void CalledDirectly_WithPlainLocals_TheLoopIsIdempotentAfterItEnds()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestRecord", "{\"value\":1}"), Step("TestFail", "{\"code\":2}")));
            var p = new RunActionsParams { ListRef = 0 }; var s = new RunActionsState();   // the outer as ordinary stack memory
            Assert.Equal(NodeStatus.Failure, ScenarioActions.RunActions(ref p, ref s, e, w));
            Assert.Equal(NodeStatus.Failure, ScenarioActions.RunActions(ref p, ref s, e, w));   // does not re-run the log step
            Assert.Equal(1, w.GetComponentRO<ActionTestLog>(e).Count);

            var ok = Unit(w, Steps(Step("TestRecord", "{\"value\":7}")));
            var s2 = new RunActionsState();
            Assert.Equal(NodeStatus.Success, ScenarioActions.RunActions(ref p, ref s2, ok, w));
            Assert.Equal(NodeStatus.Success, ScenarioActions.RunActions(ref p, ref s2, ok, w));
            Assert.Equal(1, w.GetComponentRO<ActionTestLog>(ok).Count);
        }

        [Fact]
        public void TwoEntities_RunDifferentLists_Interleaved_OnOneCachedRunnerSet()
        {
            using var w = NewWorld();
            var a = Unit(w, Steps(Step("TestWaitTicks", "{\"ticks\":2}"), Step("TestRecord", "{\"value\":10}")));
            var b = Unit(w, Steps(Step("TestWaitTicks", "{\"ticks\":3}"), Step("TestRecord", "{\"value\":20}")));
            var sa = new List<NodeStatus>(); var sb = new List<NodeStatus>();
            for (int i = 0; i < 3; i++)
            {
                if (sa.Count == 0 || sa[^1] == NodeStatus.Running) sa.Add(ActionHost.Tick(w, a, 0, out _));
                if (sb.Count == 0 || sb[^1] == NodeStatus.Running) sb.Add(ActionHost.Tick(w, b, 0, out _));
            }
            Assert.Equal(new[] { NodeStatus.Running, NodeStatus.Success }, sa);
            Assert.Equal(new[] { NodeStatus.Running, NodeStatus.Running, NodeStatus.Success }, sb);
            Assert.Equal(10, w.GetComponentRO<ActionTestLog>(a).V0);
            Assert.Equal(20, w.GetComponentRO<ActionTestLog>(b).V0);
        }

        [Fact]
        public void AnUnknownActionInTheList_ThrowsAtRuntime_ValidateIsTheGuardAtAssignment()
        {
            using var w = NewWorld();
            var e = Unit(w, Steps(Step("TestRecord", "{\"value\":1}"), Step("NoSuchAct")));
            Assert.Throws<InvalidOperationException>(() => ActionHost.Tick(w, e, 0, out _));
            Assert.Equal(1, w.GetComponentRO<ActionTestLog>(e).Count);           // ⚠ step 0 already RAN — which is why ActionList.Validate exists (CE-4163)
        }

        [Fact]
        public void AListRefOutsideTheArgs_IsALoudError()
        {
            using var w = NewWorld();
            var e = Unit(w, "[]");
            Assert.Throws<InvalidOperationException>(() => ActionHost.Tick(w, e, 5, out _));
        }

        // ── CE-4161: a list run from INSIDE a step must not collide with the outer list's step slot ───────────────

        [Fact]
        public void CE4161_ANestedList_AtDepth1_UsesItsOwnStepSlot_AndDoesNotCollideWithDepth0()
        {
            using var w = NewWorld();
            // list 0 (depth 0): record 1 · RunActions(list 1, depth 1) · record 9.   list 1: wait 2 · record 5
            string outer = Steps(Step("TestRecord", "{\"value\":1}"), Step("RunActions", "{\"listRef\":1,\"depth\":1}"), Step("TestRecord", "{\"value\":9}"));
            string inner = Steps(Step("TestWaitTicks", "{\"ticks\":2}"), Step("TestRecord", "{\"value\":5}"));
            var e = Unit(w, outer, inner);

            Assert.Equal(NodeStatus.Running, ActionHost.Tick(w, e, 0, out _));
            // the outer's step (the nested RunActions) holds depth-0 key; ITS step (the wait) holds depth-1 key — two distinct slots live at once
            Assert.True(ActionHost.Holds(w, e, StepRunner.SlotKey(0)));
            Assert.True(ActionHost.Holds(w, e, StepRunner.SlotKey(1)));          // ← red-proof: salt dropped ⇒ both use one key ⇒ the nested one re-attaches over the outer's
            Assert.NotEqual(StepRunner.SlotKey(0), StepRunner.SlotKey(1));
            Assert.Equal((2, 1), (PeekWait(w, e, depth: 1).P.Ticks, PeekWait(w, e, depth: 1).S.Elapsed));

            Assert.Equal(NodeStatus.Success, ActionHost.Tick(w, e, 0, out var fin));
            var log = w.GetComponentRO<ActionTestLog>(e);
            Assert.Equal((3, 1, 5, 9), (log.Count, log.V0, log.V1, log.V2));    // 1 · [wait → 5] · 9, each once, in order
            Assert.Equal(3, fin.Index);
            Assert.Equal(0, ActionHost.SlotCount(w, e));
        }

        [Fact]
        public void CE4161_AFailureInANestedList_FailsTheOuterList_AtTheOuterStep()
        {
            using var w = NewWorld();
            string outer = Steps(Step("TestRecord", "{\"value\":1}"), Step("RunActions", "{\"listRef\":1,\"depth\":1}"), Step("TestRecord", "{\"value\":9}"));
            string inner = Steps(Step("TestFail", "{\"code\":3}"));
            var e = Unit(w, outer, inner);
            Assert.Equal(NodeStatus.Failure, ActionHost.Tick(w, e, 0, out var fin));
            Assert.Equal(1, fin.FailedStep);                                     // the nested RunActions was step 1 of the outer list
            Assert.Equal(1, w.GetComponentRO<ActionTestLog>(e).Count);           // 9 never ran
            Assert.Equal(0, ActionHost.SlotCount(w, e));
        }

        // ── restore mid-step (cheap path: EntityRepository.SyncFrom = the engine's snapshot/restore primitive) ──

        [Fact]
        public void ASnapshotTakenMidStep_ResumesAtTheSameStep_WithItsWorkingState()
        {
            using var live = NewWorld();
            var e = Unit(live, ThreeSteps);                                      // record 1 · wait 3 · record 2
            Assert.Equal(NodeStatus.Running, ActionHost.Tick(live, e, 0, out _));   // record 1 done, wait 1/3
            Assert.Equal(NodeStatus.Running, ActionHost.Tick(live, e, 0, out _));   // wait 2/3

            using var restored = NewWorld();
            restored.SyncFrom(live);                                             // ← the snapshot / restore
            live.GetComponentRW<ActionTestLog>(e).Count = 99;                    // the live world moves on: the copy must not care

            Assert.True(ActionHost.Holds(restored, e, ActionHost.OuterSlotKey)); // outer + step slots came across as bytes
            Assert.True(ActionHost.Holds(restored, e, StepRunner.SlotKey(0)));
            var (p, s) = PeekWait(restored, e);
            Assert.Equal((3, 2), (p.Ticks, s.Elapsed));                          // the step's working state survived the restore

            Assert.Equal(NodeStatus.Success, ActionHost.Tick(restored, e, 0, out var fin));   // ONE more tick finishes it: it did NOT restart the wait
            var log = restored.GetComponentRO<ActionTestLog>(e);
            Assert.Equal((2, 1, 2), (log.Count, log.V0, log.V1));                // record 1 not repeated, record 2 ran once
            Assert.Equal(3, fin.Index);
        }
    }
}
