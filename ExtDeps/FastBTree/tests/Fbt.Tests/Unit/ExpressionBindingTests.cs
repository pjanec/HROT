using System;
using System.Runtime.InteropServices;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    public class ExpressionBindingTests
    {
        // ---- Test blackboard structs with sequential layout for reliable offsets ----

        [StructLayout(LayoutKind.Sequential)]
        private struct TwoFieldBlackboard
        {
            public int FieldA;   // offset 0, size 4
            public float FieldB; // offset 4, size 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AmmoBlackboard
        {
            public int AmmoCount; // offset 0, size 4
        }

        // ---- Reusable delegates ----

        private static NodeStatus FloatPositiveCheck(
            ref float data, ref BehaviorTreeState state, ref MockContext ctx)
            => data > 0f ? NodeStatus.Success : NodeStatus.Failure;

        private static NodeStatus IntPositiveCheck(
            ref int data, ref BehaviorTreeState state, ref MockContext ctx)
            => data > 0 ? NodeStatus.Success : NodeStatus.Failure;

        private static NodeStatus IntDecrementAction(
            ref int data, ref BehaviorTreeState state, ref MockContext ctx)
        {
            data--;
            return NodeStatus.Success;
        }

        // ---- FBT-003: SC1 / Test 1 ----

        [Fact]
        public void Condition_LambdaFieldSelector_ComputesCorrectByteOffset()
        {
            // FieldA is at offset 0, FieldB is at offset 4.
            // The condition reads FieldB (float > 0). Setting FieldA to -999 and FieldB
            // to 5.0f must produce Success, proving the closure reads from offset 4.
            var builder = new BTreeBuilder<TwoFieldBlackboard, MockContext>();
            var blob = builder
                .Condition(bb => bb.FieldB, FloatPositiveCheck)
                .Compile("OffsetTest");

            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<TwoFieldBlackboard, MockContext>(blob, registry);

            var bb = new TwoFieldBlackboard { FieldA = -999, FieldB = 5.0f };
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
        }

        // ---- FBT-003: SC3 / Test 2 ----

        [Fact]
        public void Action_LambdaFieldSelector_MutatesCorrectField()
        {
            // The action decrements AmmoCount. After one tick AmmoCount must be 4,
            // not any other field (there is only one field in this blackboard).
            var builder = new BTreeBuilder<AmmoBlackboard, MockContext>();
            var blob = builder
                .Action(bb => bb.AmmoCount, IntDecrementAction)
                .Compile("MutateTest");

            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<AmmoBlackboard, MockContext>(blob, registry);

            var bb = new AmmoBlackboard { AmmoCount = 5 };
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(4, bb.AmmoCount);
        }

        // ---- FBT-003: SC2 / Test 3 ----

        [Fact]
        public void Condition_FieldB_WhenFieldBPositive_ReturnsSuccess()
        {
            // FieldA is negative; FieldB is positive. Condition on FieldB must succeed.
            var builder = new BTreeBuilder<TwoFieldBlackboard, MockContext>();
            var blob = builder
                .Condition(bb => bb.FieldB, FloatPositiveCheck)
                .Compile("FieldBPositive");

            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<TwoFieldBlackboard, MockContext>(blob, registry);

            var bb = new TwoFieldBlackboard { FieldA = -1, FieldB = 1.0f };
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Success, interpreter.Tick(ref bb, ref state, ref ctx));
        }

        // ---- FBT-003: SC2 / Test 4 ----

        [Fact]
        public void Condition_FieldB_WhenFieldBNegative_ReturnsFailure()
        {
            // FieldA is positive; FieldB is negative. Condition on FieldB must fail.
            var builder = new BTreeBuilder<TwoFieldBlackboard, MockContext>();
            var blob = builder
                .Condition(bb => bb.FieldB, FloatPositiveCheck)
                .Compile("FieldBNegative");

            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<TwoFieldBlackboard, MockContext>(blob, registry);

            var bb = new TwoFieldBlackboard { FieldA = 1, FieldB = -1.0f };
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Failure, interpreter.Tick(ref bb, ref state, ref ctx));
        }

        // ---- FBT-003: SC3 / Test 5 ----

        [Fact]
        public void Action_DecrementsAmmo_Correctly()
        {
            // Tick twice; AmmoCount must decrease by 2.
            var builder = new BTreeBuilder<AmmoBlackboard, MockContext>();
            var blob = builder
                .Action(bb => bb.AmmoCount, IntDecrementAction)
                .Compile("AmmoDecrement");

            var registry = builder.GetRegistry();
            var interpreter = new Interpreter<AmmoBlackboard, MockContext>(blob, registry);

            var bb = new AmmoBlackboard { AmmoCount = 5 };
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            interpreter.Tick(ref bb, ref state, ref ctx);
            interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(3, bb.AmmoCount);
        }

        // ---- FBT-003: Test 6 ----

        [Fact]
        public void ExpressionBinding_InvalidExpression_ThrowsArgumentException()
        {
            // A constant expression cannot be mapped to a field name.
            var builder = new BTreeBuilder<TwoFieldBlackboard, MockContext>();
            Assert.Throws<ArgumentException>(() =>
                builder.Condition<float>(bb => 42f, FloatPositiveCheck));
        }

        // ---- FBT-003: Test 7 ----

        [Fact]
        public void ExpressionBinding_RegistryKey_IsStableAcrossBuilds()
        {
            // Using the same delegate on the same field twice within one tree must produce
            // a single deduplicated entry in blob.MethodNames.
            var builder = new BTreeBuilder<AmmoBlackboard, MockContext>();
            var blob = builder
                .Sequence(s => s
                    .Condition(bb => bb.AmmoCount, IntPositiveCheck)
                    .Condition(bb => bb.AmmoCount, IntPositiveCheck))
                .Compile("DedupTest");

            Assert.Single(blob.MethodNames);
        }
    }
}
