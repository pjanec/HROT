using System;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>
    /// Tests for TASK-EQL-003: Interpreter deactivator array and delta tracking.
    /// Covers success conditions T1-T10 from TASK-DETAIL.md.
    /// </summary>
    public class HybridLifecycleTests
    {
        // ================================================================
        // T1: Natural completion -- deactivator fires when Running action
        // transitions to Success. Single-action Sequence.
        // ================================================================

        [Fact]
        public void T1_NaturalCompletion_FiresDeactivatorOnSuccess()
        {
            int deactivationCount = 0;
            int tick = 0;

            NodeStatus ResourceAction(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Success;
            }

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(ResourceAction));
            var tmpBlob = builder.Compile("T1");
            var registry = builder.GetRegistry();

            // Nodes: Sequence(0), ResourceAction(1).
            string actionKey = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(actionKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    deactivationCount++);

            var blob = builder.Compile("T1");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: action returns Running; deactivator must not fire yet.
            var r0 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r0);
            Assert.Equal(0, deactivationCount);

            // Tick 1: action returns Success; deactivator fires exactly once.
            var r1 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, r1);
            Assert.Equal(1, deactivationCount);
        }

        // ================================================================
        // T2: Sequential handoff -- when Running ActionA completes (Success)
        // and passes control to ActionB, only ActionA's deactivator fires.
        // Sequence with two resource-owning actions.
        // ================================================================

        [Fact]
        public void T2_SequentialHandoff_OnlyCompletedActionDeactivates()
        {
            int countA = 0;
            int countB = 0;
            int tick = 0;

            // ActionA: Running on Tick 0, Success on Tick 1.
            NodeStatus ActionA(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Success;
            }

            // ActionB: always Running (starts after ActionA finishes).
            NodeStatus ActionB(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => NodeStatus.Running;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(ActionA).Action(ActionB));
            var tmpBlob = builder.Compile("T2");
            var registry = builder.GetRegistry();

            // Nodes: Sequence(0), ActionA(1), ActionB(2).
            string keyA = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            string keyB = tmpBlob.MethodNames[tmpBlob.Nodes[2].PayloadIndex];
            registry.RegisterDeactivator(keyA,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    countA++);
            registry.RegisterDeactivator(keyB,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    countB++);

            var blob = builder.Compile("T2");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: ActionA is Running; no deactivation yet.
            var r0 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r0);
            Assert.Equal(0, countA);
            Assert.Equal(0, countB);

            // Tick 1: ActionA succeeds, ActionB starts Running.
            // ActionA exited the active path; its deactivator must fire exactly once.
            // ActionB is now in the active path; its deactivator must NOT fire.
            var r1 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(1, countA);
            Assert.Equal(0, countB);
        }

        // ================================================================
        // T3: Tree failure -- deactivator fires when Running action
        // transitions to Failure. Single-action Sequence.
        // ================================================================

        [Fact]
        public void T3_TreeFailure_FiresDeactivator()
        {
            int deactivationCount = 0;
            int tick = 0;

            NodeStatus ResourceAction(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Failure;
            }

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(ResourceAction));
            var tmpBlob = builder.Compile("T3");
            var registry = builder.GetRegistry();

            string actionKey = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(actionKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    deactivationCount++);

            var blob = builder.Compile("T3");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: Running; no deactivation.
            interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(0, deactivationCount);

            // Tick 1: Failure; deactivator must fire exactly once.
            var r1 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Failure, r1);
            Assert.Equal(1, deactivationCount);
        }

        // ================================================================
        // T4: No deactivator registered -- 1000 ticks with no exception and
        // no heap allocations in the hot path (stackalloc sweep only).
        // ================================================================

        [Fact]
        public void T4_NoDeactivatorRegistered_NoExceptionAndNoAllocation()
        {
            NodeStatus AlwaysRunning(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => NodeStatus.Running;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(AlwaysRunning));
            var blob = builder.Compile("T4");
            var registry = builder.GetRegistry();
            // Intentionally omit RegisterDeactivator.
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);

            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Baseline: collect any pending GC objects before the loop.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gcBefore = GC.CollectionCount(0);

            for (int i = 0; i < 1000; i++)
                interpreter.Tick(ref testBb, ref state, ref ctx);

            int gcAfter = GC.CollectionCount(0);

            // The Tick hot path uses stackalloc; no heap allocations expected per tick.
            Assert.Equal(gcBefore, gcAfter);
        }

        // ================================================================
        // T5: Two resource-owning nodes -- Selector with ActionA and ActionB,
        // both having deactivators. When ActionA exits the path (via Failure),
        // only ActionA's deactivator fires; ActionB's must stay at zero.
        // ================================================================

        [Fact]
        public void T5_TwoResourceOwningNodes_OnlyExitedOneFires()
        {
            int countA = 0;
            int countB = 0;
            int tick = 0;

            // ActionA: Running Tick 0, Failure Tick 1.
            NodeStatus ActionA(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Failure;
            }

            // ActionB: always Running (selector falls through to it when A fails).
            NodeStatus ActionB(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => NodeStatus.Running;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Selector(s => s.Action(ActionA).Action(ActionB));
            var tmpBlob = builder.Compile("T5");
            var registry = builder.GetRegistry();

            // Nodes: Selector(0), ActionA(1), ActionB(2).
            string keyA = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            string keyB = tmpBlob.MethodNames[tmpBlob.Nodes[2].PayloadIndex];
            registry.RegisterDeactivator(keyA,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    countA++);
            registry.RegisterDeactivator(keyB,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    countB++);

            var blob = builder.Compile("T5");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: ActionA is Running.
            interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(0, countA);
            Assert.Equal(0, countB);

            // Tick 1: ActionA fails; selector switches to ActionB (Running).
            // Only ActionA exited the active path; only its deactivator fires.
            interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(1, countA);
            Assert.Equal(0, countB);
        }

        // ================================================================
        // T6: Idle-path sentinel -- when RunningNodeIndex is zero at the
        // start of a tick (no previously-running node), no deactivators fire
        // even if the registered action is in the tree.
        // ================================================================

        [Fact]
        public void T6_IdlePath_NoDeactivationWhenNeverRunning()
        {
            int deactivationCount = 0;

            // Action that completes without ever returning Running.
            NodeStatus AlwaysSuccess(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => NodeStatus.Success;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Action(AlwaysSuccess);
            var tmpBlob = builder.Compile("T6");
            var registry = builder.GetRegistry();

            registry.RegisterDeactivator(tmpBlob.MethodNames[0],
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    deactivationCount++);

            var blob = builder.Compile("T6");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            // Fresh state: RunningNodeIndex == 0 (idle sentinel), NodeIndexStack all zero.
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick with fresh state. Action completes immediately (never enters Running).
            // oldPath = all zeros; sweep skips all zero entries. Deactivator must not fire.
            var result = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(0, deactivationCount);
        }

        // ================================================================
        // T7: Exception propagation -- if a deactivator throws, the exception
        // must propagate out of Tick unchanged.
        // ================================================================

        [Fact]
        public void T7_DeactivatorException_PropagatesOutOfTick()
        {
            int tick = 0;

            NodeStatus ResourceAction(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Success;
            }

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(ResourceAction));
            var tmpBlob = builder.Compile("T7");
            var registry = builder.GetRegistry();

            string actionKey = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(actionKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    throw new InvalidOperationException("deactivator threw"));

            var blob = builder.Compile("T7");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: Running; no deactivator invoked.
            interpreter.Tick(ref testBb, ref state, ref ctx);

            // Tick 1: Success triggers deactivator, which throws.
            // Exception must escape Tick without being swallowed.
            bool threw = false;
            try
            {
                interpreter.Tick(ref testBb, ref state, ref ctx);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Assert.True(threw);
        }

        // ================================================================
        // T8: Deep subtree abort -- leaf action inside a Sequence branch is
        // Running; when the parent Selector moves to a fallback branch (because
        // the leaf fails), the leaf's deactivator is fired via the path sweep.
        // ================================================================

        [Fact]
        public void T8_DeepSubtreeAbort_LeafDeactivatorFiredExactlyOnce()
        {
            int deactivationCount = 0;
            int tick = 0;

            // LeafAction: Running Tick 0, Failure Tick 1 (causes Sequence to fail).
            NodeStatus LeafAction(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Failure;
            }

            // FallbackAction: always Running (reached when Sequence branch fails).
            NodeStatus FallbackAction(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => NodeStatus.Running;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            // Selector(0) -> Sequence(1) -> LeafAction(2), FallbackAction(3)
            builder.Selector(s => s
                .Sequence(seq => seq.Action(LeafAction))
                .Action(FallbackAction));
            var tmpBlob = builder.Compile("T8");
            var registry = builder.GetRegistry();

            // Nodes: Selector(0), Sequence(1), LeafAction(2), FallbackAction(3).
            string leafKey = tmpBlob.MethodNames[tmpBlob.Nodes[2].PayloadIndex];
            registry.RegisterDeactivator(leafKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    deactivationCount++);

            var blob = builder.Compile("T8");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: LeafAction Running (RunningNodeIndex = 2).
            var r0 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r0);
            Assert.Equal(0, deactivationCount);

            // Tick 1: LeafAction fails -> Sequence fails -> Selector moves to FallbackAction.
            // LeafAction (node 2) exits the active path; its deactivator must fire exactly once.
            var r1 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(1, deactivationCount);
        }

        // ================================================================
        // T9: Parallel subtree sweep -- Parallel(RequireAll) with two
        // Sequence-wrapped leaf actions. On exit (both children succeed),
        // SweepParallelChildren fires both leaf deactivators exactly once each.
        // ================================================================

        [Fact]
        public void T9_ParallelExit_SubtreeSweepFiresBothLeafDeactivators()
        {
            int countA = 0;
            int countB = 0;
            bool shouldRun = true;

            // Both actions Running while shouldRun is true, Success afterwards.
            NodeStatus ActionA(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => shouldRun ? NodeStatus.Running : NodeStatus.Success;

            NodeStatus ActionB(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => shouldRun ? NodeStatus.Running : NodeStatus.Success;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            // ForceSuccess(0) -> Parallel(1, policy=0/RequireAll) ->
            //   Sequence(2) -> ActionA(3),
            //   Sequence(4) -> ActionB(5)
            builder.ForceSuccess(fs => fs
                .Parallel(0, p => p
                    .Sequence(s => s.Action(ActionA))
                    .Sequence(s => s.Action(ActionB))));
            var tmpBlob = builder.Compile("T9");
            var registry = builder.GetRegistry();

            // Nodes: ForceSuccess(0), Parallel(1), Sequence(2), ActionA(3), Sequence(4), ActionB(5).
            string keyA = tmpBlob.MethodNames[tmpBlob.Nodes[3].PayloadIndex];
            string keyB = tmpBlob.MethodNames[tmpBlob.Nodes[5].PayloadIndex];
            registry.RegisterDeactivator(keyA,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    countA++);
            registry.RegisterDeactivator(keyB,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    countB++);

            var blob = builder.Compile("T9");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: both actions Running; Parallel keeps Running (RunningNodeIndex = 1).
            var r0 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r0);
            Assert.Equal(0, countA);
            Assert.Equal(0, countB);

            // Allow both actions to succeed.
            shouldRun = false;

            // Tick 1: both actions succeed; Parallel exits the active path.
            // SweepParallelChildren must iterate both child subtrees and fire both deactivators.
            var r1 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, r1);
            Assert.Equal(1, countA);
            Assert.Equal(1, countB);
        }

        // ================================================================
        // T10: Hot-reload bounds-check -- deactivator fires BEFORE
        // RunningNodeIndex is reset; ExecuteNode still runs on the same frame;
        // no double-fire occurs (pathWasReset prevents post-tick sweep).
        // ================================================================

        [Fact]
        public void T10_HotReloadBoundsCheck_DeactivatesOnceBeforeReset()
        {
            int deactivationCount = 0;
            ushort rnAtDeactivation = 0;
            int tick = 0;

            // Build: Sequence(0) -> ResourceAction(1). Two nodes total.
            NodeStatus ResourceAction(
                ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Success;
            }

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(ResourceAction));
            var tmpBlob = builder.Compile("T10");
            var registry = builder.GetRegistry();

            string actionKey = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(actionKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                {
                    deactivationCount++;
                    // Capture RunningNodeIndex at deactivation time (must still be OOB value).
                    rnAtDeactivation = st.RunningNodeIndex;
                });

            var blob = builder.Compile("T10");
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: ResourceAction returns Running; RunningNodeIndex = 1.
            var r0 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r0);
            Assert.Equal((ushort)1, state.RunningNodeIndex);

            // Simulate hot-reload: place the valid action node index (1) into NodeIndexStack[0]
            // and set RunningNodeIndex to blob.Nodes.Length (out-of-bounds for the current blob).
            // This replicates state left by a now-larger blob that has been replaced.
            unsafe
            {
                state.NodeIndexStack[0] = 1;                   // valid index in NodeIndexStack
            }
            state.RunningNodeIndex = (ushort)blob.Nodes.Length; // OOB: triggers hot-reload path

            // Tick 1 with OOB RunningNodeIndex:
            // (a) Hot-reload path fires SweepExitedNodes against emptyPath.
            //     NodeIndexStack[0]=1 is valid -> deactivator fires (count=1).
            //     RunningNodeIndex=2 is OOB -> InvokeDeactivatorIfRegistered bounds-checks it -> skipped.
            // (b) pathWasReset=true; state.RunningNodeIndex reset to 0 before ExecuteNode.
            // (c) ExecuteNode(0) still runs this frame (no early return after bounds-check).
            //     ResourceAction returns Success (tick==2) -> tree returns Success.
            // (d) pathWasReset prevents the post-tick sweep from double-firing.
            var r1 = interpreter.Tick(ref testBb, ref state, ref ctx);

            // (a) Deactivator fired exactly once.
            Assert.Equal(1, deactivationCount);

            // (b) At deactivation time, RunningNodeIndex was still the OOB value (not yet reset).
            Assert.Equal((ushort)blob.Nodes.Length, rnAtDeactivation);

            // (c) Tree executed and succeeded this frame.
            Assert.Equal(NodeStatus.Success, r1);

            // (d) No double-fire from post-tick sweep.
            Assert.Equal(1, deactivationCount);
        }
    }
}
