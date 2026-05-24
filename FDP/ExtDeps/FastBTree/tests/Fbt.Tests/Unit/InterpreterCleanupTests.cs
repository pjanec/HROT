using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>
    /// Tests for TASK-EQL-012: Interpreter cleanup (removal of _deactivatorDelegates array).
    /// Covers success conditions T1-T4 from TASK-DETAIL.md.
    /// </summary>
    public class InterpreterCleanupTests
    {
        // ================================================================
        // T1: Regression -- deactivator fires on branch switch after the
        // _deactivatorDelegates array removal. Uses compile-after-register
        // pattern with isResourceOwning delegate (V2 blob path).
        // ================================================================

        [Fact]
        public void Deactivator_FiresOnBranchSwitch_AfterRegistryCleanup()
        {
            int deactivatorACount = 0;
            int tick = 0;

            // ActionA: Running on Tick 0, Failure on Tick 1.
            NodeStatus ActionA(ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Failure;
            }

            // ActionB: always Running (selector falls through when A fails).
            NodeStatus ActionB(ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => NodeStatus.Running;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Selector(s => s.Action(ActionA).Action(ActionB));
            var tmpBlob = builder.Compile("T1_Cleanup");
            var registry = builder.GetRegistry();

            // Nodes: Selector(0), ActionA(1), ActionB(2).
            string keyA = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(keyA,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    deactivatorACount++);

            // Compile AFTER register with isResourceOwning delegate so V2 blob bakes in the bit.
            Func<string, bool> isResourceOwning = name => registry.TryGetDeactivator(name, out _);
            var blob = builder.Compile("T1_Cleanup", isResourceOwning);
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: ActionA is Running. No deactivation yet.
            var r0 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r0);
            Assert.Equal(0, deactivatorACount);

            // Tick 1: ActionA fails; Selector switches to ActionB (Running).
            // ActionA exited the active path; its deactivator must fire exactly once.
            var r1 = interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(1, deactivatorACount);
        }

        // ================================================================
        // T2: Reflection check -- Interpreter no longer has a
        // _deactivatorDelegates field of NodeDeactivatorDelegate array type.
        // ================================================================

        [Fact]
        public void Interpreter_HasNo_DeactivatorDelegatesField()
        {
            var fields = typeof(Interpreter<TestBlackboard, MockContext>)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            bool hasArray = fields.Any(f => f.FieldType.IsArray
                && f.FieldType.GetElementType() is { } et
                && et.IsGenericType
                && et.GetGenericTypeDefinition() == typeof(NodeDeactivatorDelegate<,>));
            Assert.False(hasArray);
        }

        // ================================================================
        // T3: No GC pressure on construction for a tree with 500 nodes and
        // no resource-owning actions. Previously, BindDeactivators would
        // allocate a NodeDeactivatorDelegate?[] array; this verifies it is gone.
        // ================================================================

        [Fact]
        public void Constructor_ZeroResourceOwningNodes_NoGcPressure()
        {
            static NodeStatus AlwaysRunning(
                ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p)
                => NodeStatus.Running;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Selector(s =>
            {
                foreach (var _ in Enumerable.Range(0, 500))
                    s.Action(AlwaysRunning);
            });
            var blob = builder.Compile("T3_Gc");
            var registry = builder.GetRegistry();
            // No deactivators registered.

            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gcBefore = GC.CollectionCount(0);

            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);

            int gcAfter = GC.CollectionCount(0);
            Assert.Equal(gcBefore, gcAfter);

            // Keep interpreter alive to prevent premature collection.
            GC.KeepAlive(interpreter);
        }

        // ================================================================
        // T4: Correct deactivator invoked -- only actionA's deactivator fires
        // when actionA exits the path; actionB has no deactivator and is
        // unaffected.
        // ================================================================

        [Fact]
        public void Deactivator_CorrectDelegateInvoked_NotOtherAction()
        {
            int deactivatorACount = 0;
            int tick = 0;

            // ActionA: resource-owning, Running on Tick 0, Failure on Tick 1.
            NodeStatus ActionA(ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            {
                tick++;
                return tick == 1 ? NodeStatus.Running : NodeStatus.Failure;
            }

            // ActionB: not resource-owning (no deactivator), always Running.
            NodeStatus ActionB(ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
                => NodeStatus.Running;

            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Selector(s => s.Action(ActionA).Action(ActionB));
            var tmpBlob = builder.Compile("T4_Correct");
            var registry = builder.GetRegistry();

            // Register deactivator only for actionA; actionB has no deactivator.
            // Nodes: Selector(0), ActionA(1), ActionB(2).
            string keyA = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(keyA,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) =>
                    deactivatorACount++);

            Func<string, bool> isResourceOwning = name => registry.TryGetDeactivator(name, out _);
            var blob = builder.Compile("T4_Correct", isResourceOwning);
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var testBb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 0: ActionA Running.
            interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(0, deactivatorACount);

            // Tick 1: ActionA fails, Selector switches to ActionB.
            // Only actionA's deactivator fires; actionB has none.
            interpreter.Tick(ref testBb, ref state, ref ctx);
            Assert.Equal(1, deactivatorACount);
        }
    }
}
