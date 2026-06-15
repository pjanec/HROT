using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// S1-3 runtime tests: verify that baked-offset thunks project distinct DTOs at distinct
/// byte offsets within <see cref="BrainBlackboard.BehaviorParameters"/>, and that two
/// thunks operating on adjacent memory regions do NOT interfere with each other.
///
/// These tests manually reproduce what the BTreeBridgeEmitCore.EmitManagedActionThunks /
/// EmitManagedConditionThunks methods would emit into the registrar class at build time.
/// </summary>
public sealed class MultiActionBoundTests
{
    // ── DTOs ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// First DTO at offset 0: { int Counter; int Threshold } — 8 bytes total.
    /// Matches DemoCounterNodes.DemoCounterParams layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterParams
    {
        public int Counter;
        public int Threshold;
    }

    /// <summary>
    /// Second DTO at offset 8: { bool Done } — 1 byte.
    /// Placed immediately after CounterParams.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FlagParams
    {
        public bool Done;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    // Builds a one-node blob using the given method name as the action key.
    private static BehaviorTreeBlob BuildActionBlob(string methodKey) => new BehaviorTreeBlob
    {
        TreeName    = "Test",
        Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
        MethodNames = new[] { methodKey },
        FloatParams = Array.Empty<float>(),
        IntParams   = Array.Empty<int>(),
    };

    // Builds a two-node sequence blob: [Action0, Action1]
    private static BehaviorTreeBlob BuildSequenceBlob(string key0, string key1) => new BehaviorTreeBlob
    {
        TreeName = "TestSeq",
        Nodes = new[]
        {
            new NodeDefinition { Type = NodeType.Sequence,   RawPayloadIndex = 0, SubtreeOffset = 3, ChildCount = 2 },
            new NodeDefinition { Type = NodeType.Action,     RawPayloadIndex = 0, SubtreeOffset = 1 },
            new NodeDefinition { Type = NodeType.Action,     RawPayloadIndex = 1, SubtreeOffset = 1 },
        },
        MethodNames = new[] { key0, key1 },
        FloatParams = Array.Empty<float>(),
        IntParams   = Array.Empty<int>(),
    };

    // ── Test 1 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two actions operate on distinct DTOs at distinct offsets within BehaviorParameters.
    /// Action1 writes Counter@0 only; Action2 writes Done@8 only.
    /// After a 1-tick sequence both fields are set; neither action stomps the other.
    /// </summary>
    [Fact]
    public unsafe void MultiAction_DistinctDtos_ProjectAtDistinctOffsets()
    {
        const int counterOffset = 0;  // CounterParams at offset 0 (int Counter + int Threshold = 8 bytes)
        const int flagOffset    = 8;  // FlagParams at offset 8

        const string actionKey1 = "Test.MultiActionBoundTests.CounterAction@0";
        const string actionKey2 = "Test.MultiActionBoundTests.FlagAction@8";

        var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();

        // Register action1: increments Counter at offset 0.
        actionReg.Register(actionKey1,
            static (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi) =>
            {
                unsafe
                {
                    ref var dto = ref Unsafe.As<byte, CounterParams>(
                        ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)counterOffset));
                    dto.Counter++;
                }
                return NodeStatus.Success;
            });

        // Register action2: sets Done=true at offset 8.
        actionReg.Register(actionKey2,
            static (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi) =>
            {
                unsafe
                {
                    ref var dto = ref Unsafe.As<byte, FlagParams>(
                        ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)flagOffset));
                    dto.Done = true;
                }
                return NodeStatus.Success;
            });

        var blob        = BuildSequenceBlob(actionKey1, actionKey2);
        var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);
        var bb          = new BrainBlackboard();
        var ctx         = new BTreeContext();
        var state       = new BehaviorTreeState();

        // Tick once — Sequence runs both actions.
        interpreter.Tick(ref bb, ref state, ref ctx);

        // Read back via Unsafe projection to verify independent writes.
        ref var counter = ref Unsafe.As<byte, CounterParams>(
            ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)counterOffset));
        ref var flag = ref Unsafe.As<byte, FlagParams>(
            ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)flagOffset));

        Assert.Equal(1, counter.Counter);  // action1 incremented Counter
        Assert.Equal(0, counter.Threshold); // action1 did NOT touch Threshold
        Assert.True(flag.Done);             // action2 set Done=true

        // Verify action1 did NOT touch flag region (offset 8) and vice-versa.
        // Read raw byte at offset 8 before action2 would have set it:
        // We can't un-run action2, but we CAN verify that action1 at offset 0 didn't
        // bleed into offset 8 by re-running only action1 on a fresh blackboard.
        var bb2   = new BrainBlackboard();
        var state2 = new BehaviorTreeState();
        var singleActionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
        singleActionReg.Register(actionKey1,
            static (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi) =>
            {
                unsafe
                {
                    ref var dto = ref Unsafe.As<byte, CounterParams>(
                        ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)0));
                    dto.Counter++;
                }
                return NodeStatus.Success;
            });

        var singleBlob        = BuildActionBlob(actionKey1);
        var singleInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(singleBlob, singleActionReg);
        singleInterpreter.Tick(ref bb2, ref state2, ref ctx);

        // After running ONLY action1, the flag region (offset 8) must remain zero.
        ref var flagAfterOnly1 = ref Unsafe.As<byte, FlagParams>(
            ref Unsafe.AddByteOffset(ref bb2.BehaviorParameters[0], (nint)8));
        Assert.False(flagAfterOnly1.Done,
            "action1 (offset 0) must not touch the flag region at offset 8");
    }

    // ── Test 2 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A condition thunk gates execution: registers a condition at offset 0 that
    /// returns Success only when Counter &lt; Threshold.  Verifies that the condition
    /// correctly reads the DTO projected at offset 0.
    /// </summary>
    [Fact]
    public unsafe void MultiAction_BoundConditionGates()
    {
        const int offset = 0;
        const string conditionKey = "Test.MultiActionBoundTests.CounterCondition@0";
        const string actionKey    = "Test.MultiActionBoundTests.CounterIncrement@0";

        // Blob: Sequence [Condition, Action]  (uses condition as a guard)
        // MethodNames[0] = conditionKey, MethodNames[1] = actionKey
        // Condition node has RawPayloadIndex=0 (→ conditionKey), Action has RawPayloadIndex=1 (→ actionKey)
        var blob = new BehaviorTreeBlob
        {
            TreeName = "CondGateTest",
            Nodes = new[]
            {
                new NodeDefinition { Type = NodeType.Sequence,  RawPayloadIndex = 0, SubtreeOffset = 3, ChildCount = 2 },
                new NodeDefinition { Type = NodeType.Condition, RawPayloadIndex = 0, SubtreeOffset = 1 },
                new NodeDefinition { Type = NodeType.Action,    RawPayloadIndex = 1, SubtreeOffset = 1 },
            },
            MethodNames = new[] { conditionKey, actionKey },
            FloatParams = Array.Empty<float>(),
            IntParams   = Array.Empty<int>(),
        };

        var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();

        // Condition: Success when Counter < Threshold.
        actionReg.RegisterCondition(conditionKey,
            static (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi) =>
            {
                unsafe
                {
                    ref var dto = ref Unsafe.As<byte, CounterParams>(
                        ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)offset));
                    return dto.Counter < dto.Threshold
                        ? NodeStatus.Success
                        : NodeStatus.Failure;
                }
            });

        // Action: increments Counter.
        actionReg.Register(actionKey,
            static (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi) =>
            {
                unsafe
                {
                    ref var dto = ref Unsafe.As<byte, CounterParams>(
                        ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)offset));
                    dto.Counter++;
                }
                return NodeStatus.Success;
            });

        var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);
        var bb    = new BrainBlackboard();
        var ctx   = new BTreeContext();
        var state = new BehaviorTreeState();

        // Set threshold to 3 so the counter increments 3 times before the condition blocks.
        unsafe
        {
            ref var dto = ref Unsafe.As<byte, CounterParams>(
                ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)offset));
            dto.Threshold = 3;
        }

        // Tick 1-3: condition passes (Counter < 3), action increments Counter.
        for (int tick = 0; tick < 3; tick++)
        {
            state = new BehaviorTreeState(); // reset running state each tick
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        unsafe
        {
            ref var dto = ref Unsafe.As<byte, CounterParams>(
                ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)offset));
            Assert.Equal(3, dto.Counter);  // incremented 3 times
        }

        // Tick 4: Counter == Threshold == 3, condition returns Failure → sequence fails → no increment.
        state = new BehaviorTreeState();
        interpreter.Tick(ref bb, ref state, ref ctx);

        unsafe
        {
            ref var dto = ref Unsafe.As<byte, CounterParams>(
                ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)offset));
            Assert.Equal(3, dto.Counter);  // NOT incremented — condition blocked
        }
    }
}
