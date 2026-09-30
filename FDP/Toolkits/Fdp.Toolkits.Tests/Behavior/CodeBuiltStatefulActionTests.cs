using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// ⭐ <c>CE-430</c> (<c>Q76</c> §12.23): proves the code-builder stateful helper
/// (<see cref="StatefulBTreeActionBinder.RegisterBlockThunk{TBB,TParams,TWorkingState}"/> + the authoring-side
/// <c>StatefulAction</c> extension) binds a four-parameter stateful method through FastBTree's generic
/// <c>Action</c> seam with BOTH its params and its working state as FIELDS of the builder's blackboard — the
/// behaviour's one block.
///
/// <para>⭐ The suite's two claims survive the move from slots to fields, re-expressed: nodes that project ONE
/// state field share it; nodes that project TWO fields keep independent state.
/// ⛔ HISTORY — under S3-G the same claims were "one Behavior-scoped slot" vs "two Node-scoped slots", proved by
/// provisioning a partition tier from a slot manifest. That manifest and the slot thunk are deleted.</para>
/// </summary>
public sealed class CodeBuiltStatefulActionTests
{
    /// <summary>The code builder's block: params and two independent working-state fields.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorBlackboard
    {
        public DemoCounterNodes.DemoCursorParams Cfg;
        public DemoCounterNodes.DemoCursorState  Shared;
        public DemoCounterNodes.DemoCursorState  Other;
    }

    private static CursorBlackboard Tick(BTreeBuilder<CursorBlackboard, BTreeContext> builder, string name)
    {
        var blob   = builder.Compile(name);
        var interp = new Interpreter<CursorBlackboard, BTreeContext>(blob, builder.GetRegistry());
        var bb     = new CursorBlackboard { Cfg = { Limit = 1 } };
        var ctx    = new BTreeContext();
        var state  = new BehaviorTreeState();
        interp.Tick(ref bb, ref state, ref ctx);
        return bb;
    }

    /// <summary>
    /// Two nodes project the SAME state field. With Limit=1 the Sequence runs A (0→1, Success) then B (1→2,
    /// Success) over the one field ⇒ Cursor=2. Independent state would leave 1.
    /// </summary>
    [Fact]
    public void CodeBuilt_TwoNodes_OverOneStateField_ShareIt()
    {
        var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
            .Sequence(seq => seq
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, bb => bb.Shared, DemoCounterNodes.Action_AdvanceCursor)
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, bb => bb.Shared, DemoCounterNodes.Action_AdvanceCursor));

        var bb = Tick(builder, "CodeBuiltShared");

        Assert.Equal(2, bb.Shared.Cursor);
        Assert.Equal(0, bb.Other.Cursor);
    }

    /// <summary>
    /// Two nodes project DIFFERENT state fields ⇒ each advances its own cursor to 1 — and they register under
    /// distinct keys (the state offset is part of the key), so neither thunk overwrites the other.
    /// </summary>
    [Fact]
    public void CodeBuilt_TwoNodes_OverTwoStateFields_KeepIndependentState()
    {
        var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
            .Sequence(seq => seq
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, bb => bb.Shared, DemoCounterNodes.Action_AdvanceCursor)
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, bb => bb.Other, DemoCounterNodes.Action_AdvanceCursor));

        var bb = Tick(builder, "CodeBuiltIndependent");

        Assert.Equal(1, bb.Shared.Cursor);
        Assert.Equal(1, bb.Other.Cursor);
    }

    /// <summary>
    /// ⭐ The shipped author: the hand-written PlatoonHillAttack tree binds its six stateful nodes over
    /// <see cref="PlatoonHillAttackBlackboard.State"/> — one key per method, all at the same state offset, and
    /// no key carries a slot hash any more.
    /// </summary>
    [Fact]
    public void PlatoonHillAttack_CodeTree_BindsItsStateFromTheBlock()
    {
        var builder = HillAttackCommanderNodes.BuildPlatoonHillAttackTree();
        builder.Compile("PlatoonHillAttack");

        int stateOffset = (int)Marshal.OffsetOf<PlatoonHillAttackBlackboard>(nameof(PlatoonHillAttackBlackboard.State));
        string fqn = typeof(HillAttackCommanderNodes).FullName!;
        foreach (var m in new[] { "Action_CalculateSegments", "Action_DispatchAllToBaseline", "Action_RequestAreaQuery",
                                  "Condition_IsAreaQueryResolved", "Action_DispatchWaveWithTargets", "Condition_IsWaveCompleted" })
            Assert.True(builder.GetRegistry().TryGetAction($"{fqn}.{m}@0@{stateOffset}", out _), $"{m} bound over the block");
    }
}
