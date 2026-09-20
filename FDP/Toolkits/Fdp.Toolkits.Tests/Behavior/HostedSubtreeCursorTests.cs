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
    private struct ChildBb  { public int Ticks; }

    /// <summary>A leaf that never finishes, so the interpreter must remember where it is.</summary>
    private static NodeStatus StayRunning<TBb>(ref TBb bb, ref BehaviorTreeState state,
                                               ref BTreeContext ctx, int paramIndex)
        => NodeStatus.Running;

    private static Interpreter<ChildBb, BTreeContext> BuildChild()
    {
        var b = new BTreeBuilder<ChildBb, BTreeContext>()
            .Sequence(seq => seq
                .Action(StayRunning<ChildBb>, label: "ChildLeaf"));
        return new Interpreter<ChildBb, BTreeContext>(b.Compile("O4_Child"), b.GetRegistry());
    }

    /// <summary>
    /// 🔴 <b>RED before <c>O4</c>.</b> The host is <c>Running</c> at its hosting node and the child is
    /// <c>Running</c> at its own leaf. Both cursors must survive one tick.
    ///
    /// <para>⭐ The hosting action is written to mirror <c>BTreeOrchestratorEmitCore</c>'s emission
    /// <b>verbatim</b> — it hands the child <c>ref state</c>, the master's own state — so this rail
    /// fails for exactly the reason the generated orchestrator does, not for a reason invented here.
    /// ⇒ ⛔ when <c>O4</c> gives the hosted occurrence its own <see cref="BehaviorTreeState"/> in its
    /// own slot, the two cursors stop sharing a field and this goes green.</para>
    /// </summary>
    [Fact]
    public void O4_R1_AHostedSubtreeKeepsItsOwnCursor()
    {
        var child = BuildChild();

        // ⛔ THE EMITTED SHAPE: the child is ticked with the MASTER's state.
        //    BTreeOrchestratorEmitCore:142  → Tick(ref subBb,  ref state, ref ctx)
        //    BTreeOrchestratorEmitCore:171  → Tick(ref subDto, ref state, ref ctx)
        NodeStatus Orchestrate(ref HostBb master, ref BehaviorTreeState state,
                               ref BTreeContext ctx, int paramIndex)
        {
            master.Ticks++;
            var childBb = new ChildBb();
            return child.Tick(ref childBb, ref state, ref ctx);
        }

        var hostBuilder = new BTreeBuilder<HostBb, BTreeContext>()
            .Sequence(seq => seq
                .Action(Orchestrate, label: "HostingNode"));

        var host = new Interpreter<HostBb, BTreeContext>(
            hostBuilder.Compile("O4_Host"), hostBuilder.GetRegistry());

        var bb    = new HostBb();
        var ctx   = new BTreeContext();
        var state = new BehaviorTreeState();

        var status = host.Tick(ref bb, ref state, ref ctx);

        Assert.Equal(NodeStatus.Running, status);
        Assert.Equal(1, bb.Ticks);

        // ⭐ THE RAIL. The host is suspended at ITS hosting node; the child is suspended at ITS own
        //   leaf. One BehaviorTreeState cannot hold both, so with the shipped `ref state` the host's
        //   cursor is whatever the CHILD last wrote.
        // ⛔ Stated as a property rather than a literal index so it survives any renumbering: after a
        //   second tick the host must resume its OWN hosting node — i.e. run the orchestrator again —
        //   rather than resuming wherever the child left off.
        var status2 = host.Tick(ref bb, ref state, ref ctx);

        Assert.Equal(NodeStatus.Running, status2);
        Assert.Equal(2, bb.Ticks);   // 🔴 the host re-entered its hosting node; shared state loses this
    }
}
