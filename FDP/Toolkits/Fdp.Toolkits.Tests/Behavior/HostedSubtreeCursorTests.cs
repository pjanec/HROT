using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// ⭐⭐⭐ <c>O4</c> / task <c>C1</c> rail ① — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §3.1, §6.
/// <b>A hosted subtree must keep its OWN cursor.</b>
///
/// <para>⛔⛔ <b>This is written to go RED, and it reproduces a defect in SHIPPED code.</b>
/// <c>BTreeOrchestratorEmitCore</c> emits the hosting action for both variants as
/// <c>…GetInterpreter().Tick(ref subBb, ref state, ref ctx)</c> — passing the <b>MASTER's</b>
/// <see cref="BehaviorTreeState"/> to the child interpreter (Approach A at <c>:142</c>, the
/// COPY IN → TICK → COPY OUT variant at <c>:171</c>; ⚠ the design cites <c>:143-144/:176</c>, which
/// has drifted by a line or two — the shape is unchanged).</para>
///
/// <para>🔴 <see cref="BehaviorTreeState"/> is ONE 64-byte struct holding a single
/// <c>RunningNodeIndex</c>, one <c>StackPointer</c> and one <c>NodeIndexStack[8]</c>. Host and child
/// therefore share one cursor. With a single child that completes within the tick it mostly
/// survives; with a child left <c>Running</c> while the host advances, they overwrite each other.</para>
///
/// <para>⚠⚠ <b>Why no existing test catches this.</b> 📐 Measured: every subtree-hosting test in the
/// repo is <b>EMIT-level</b> — <c>TheOrchestratorCopyTickCopyTests</c>,
/// <c>TheMasterDeclaresTheSubtreeSliceTests</c>, <c>TheOrchestratorIsGeneratedTests</c>,
/// <c>BTreeOrchestratorEmitterTests</c> — and they assert the generated <b>text</b>, which is exactly
/// what the defect looks like when it is correct. ⇒ there was no runtime rail for hosted-subtree
/// cursor behaviour at all. This is it.</para>
///
/// <para>⛔ <b>It deliberately does NOT live in <c>Fbt.Tests</c></b>: that is ExtDeps and outside the
/// root solution, and <c>O4</c>'s defining property is that it proves the model with <b>zero</b>
/// ExtDeps change (§4.1). The kernel is not at fault here — the ORCHESTRATOR's argument is.</para>
/// </summary>
public sealed class HostedSubtreeCursorTests
{
    private struct HostBb   { public int Ticks; }
    private struct ChildBb  { public int FirstLeafEntries; }

    /// <summary>A leaf that never finishes, so the interpreter must remember where it is.</summary>
    private static NodeStatus StayRunning<TBb>(ref TBb bb, ref BehaviorTreeState state,
                                               ref BTreeContext ctx, int paramIndex)
        => NodeStatus.Running;

    /// <summary>The child's FIRST leaf. It succeeds immediately and counts its entries — the
    /// observable that says whether the child RESUMED or RESTARTED.</summary>
    private static NodeStatus CountAndSucceed(ref ChildBb bb, ref BehaviorTreeState state,
                                              ref BTreeContext ctx, int paramIndex)
    {
        bb.FirstLeafEntries++;
        return NodeStatus.Success;
    }

    /// <summary>
    /// The child is a Sequence of [succeed-once, stay-running]. ⭐ That shape is what makes the
    /// collision observable: if the child's cursor survives, tick 2 resumes at the RUNNING leaf and
    /// the first leaf is entered exactly ONCE. ⛔ If the cursor was clobbered, the child restarts
    /// from the top and enters the first leaf again.
    /// </summary>
    private static Interpreter<ChildBb, BTreeContext> BuildChild()
    {
        var b = new BTreeBuilder<ChildBb, BTreeContext>()
            .Sequence(seq => seq
                .Action(CountAndSucceed)
                .Action(StayRunning<ChildBb>));
        return new Interpreter<ChildBb, BTreeContext>(b.Compile("O4_Child"), b.GetRegistry());
    }

    /// <summary>
    /// 🔴 <b>RED before <c>O4</c>.</b> The host is <c>Running</c> at its hosting node and the child is
    /// <c>Running</c> at its own second leaf. Both cursors must survive.
    ///
    /// <para>⭐ The hosting action mirrors <c>BTreeOrchestratorEmitCore</c>'s emission <b>verbatim</b>
    /// — it hands the child <c>ref state</c>, the master's own state — so this fails for exactly the
    /// reason the generated orchestrator does, not for a reason invented here.</para>
    ///
    /// <para>⚠⚠ <b>WHICH SIDE LOSES, and my first version of this rail got it backwards.</b> It
    /// asserted the HOST resumes its hosting node, and that PASSED: the host interpreter writes its
    /// own cursor <i>after</i> the hosting action returns, so the host always wins the race. 🔒 <b>The
    /// CHILD is the side that is destroyed</b> — its cursor is overwritten by the host's write before
    /// it can be read back. ⇒ the rail must observe the CHILD's resumption, which is what
    /// <c>FirstLeafEntries</c> is for.</para>
    /// </summary>
    [Fact]
    public void O4_R1_AHostedSubtreeKeepsItsOwnCursor()
    {
        var child = BuildChild();

        // ⛔ THE EMITTED SHAPE: the child is ticked with the MASTER's state.
        //    BTreeOrchestratorEmitCore:142  → Tick(ref subBb,  ref state, ref ctx)
        //    BTreeOrchestratorEmitCore:171  → Tick(ref subDto, ref state, ref ctx)
        // ⚠ The child blackboard is hoisted out of the action so its observable survives the ticks;
        //   the emitted code slices it off the master, which has the same lifetime.
        var childBb = new ChildBb();

        NodeStatus Orchestrate(ref HostBb master, ref BehaviorTreeState state,
                               ref BTreeContext ctx, int paramIndex)
        {
            master.Ticks++;
            return child.Tick(ref childBb, ref state, ref ctx);
        }

        var hostBuilder = new BTreeBuilder<HostBb, BTreeContext>()
            .Sequence(seq => seq
                .Action(Orchestrate));

        var host = new Interpreter<HostBb, BTreeContext>(
            hostBuilder.Compile("O4_Host"), hostBuilder.GetRegistry());

        var bb    = new HostBb();
        var ctx   = new BTreeContext();
        var state = new BehaviorTreeState();

        Assert.Equal(NodeStatus.Running, host.Tick(ref bb, ref state, ref ctx));
        Assert.Equal(1, bb.Ticks);
        Assert.Equal(1, childBb.FirstLeafEntries);   // the child ran its first leaf once

        Assert.Equal(NodeStatus.Running, host.Tick(ref bb, ref state, ref ctx));
        Assert.Equal(2, bb.Ticks);                   // the host resumed its hosting node (it always does)

        // ⭐⭐ THE RAIL. The child was left Running at its SECOND leaf, so tick 2 must resume there
        //   and NOT re-enter the first. 🔴 With one shared BehaviorTreeState the child's cursor is
        //   gone, so it restarts from the top and this reads 2.
        Assert.Equal(1, childBb.FirstLeafEntries);
    }
}
