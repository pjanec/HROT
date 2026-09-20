using System;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// ⭐⭐⭐ <c>O4</c> / task <c>C1</c> rails ① and ② — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c>
/// §3.1, §18, §19. <b>A hosted subtree keeps its OWN cursor, and resets on re-entry.</b>
///
/// <para>🔴 <b>The defect these close, measured before the fix (§18).</b>
/// <c>BTreeOrchestratorEmitCore</c> emitted the hosting action for both variants as
/// <c>child.Tick(ref subBb, <b>ref state</b>, ref ctx)</c> — the MASTER's
/// <see cref="BehaviorTreeState"/>. One 64-byte struct, one <c>RunningNodeIndex</c> ⇒ host and child
/// share a cursor. Rail ① read <c>Expected 1, Actual 2</c>: the child had restarted.</para>
///
/// <para>⚠⚠ <b>WHICH SIDE LOSES — the first version of rail ① got this backwards.</b> It asserted the
/// HOST resumes its hosting node and PASSED, reproducing nothing: the host's <c>ExecuteAction</c>
/// writes its cursor AFTER the hosting action returns, so the host always wins the race. 🔒 The CHILD
/// is destroyed. ⇒ the observable belongs on the loser, which is what <c>FirstLeafEntries</c> is.</para>
///
/// <para>⛔ <b>These deliberately do NOT live in <c>Fbt.Tests</c></b>: that is ExtDeps and outside the
/// root solution, and <c>O4</c>'s defining property is proving the model with <b>zero</b> ExtDeps
/// change (§4.1). <see cref="BehaviorTreeState"/> is untouched — the kernel was never at fault; the
/// ORCHESTRATOR's argument was.</para>
/// </summary>
public sealed unsafe class HostedSubtreeCursorTests
{
    private static readonly Guid HostAsset  = new("04000000-0000-0000-0000-00000000da00");
    private static readonly Guid ChildAsset = new("04000000-0000-0000-0000-00000000db00");
    private static readonly Guid PatrolSite = new("04000000-0000-0000-0000-00000000c100");

    /// <summary>The key both the hand-written host here and a generated orchestrator would compute.</summary>
    private static int TreeStateKey => OccurrenceSlots.TreeStateKeyFor(HostAsset, PatrolSite, ChildAsset);

    private struct HostBb  { public int Ticks; }
    private struct ChildBb { public int FirstLeafEntries; }

    /// <summary>A leaf that never finishes, so the interpreter must remember where it is.</summary>
    private static NodeStatus StayRunning(ref ChildBb bb, ref BehaviorTreeState state,
                                          ref BTreeContext ctx, int paramIndex)
        => NodeStatus.Running;

    /// <summary>The child's FIRST leaf — the observable that says RESUMED or RESTARTED.</summary>
    private static NodeStatus CountAndSucceed(ref ChildBb bb, ref BehaviorTreeState state,
                                              ref BTreeContext ctx, int paramIndex)
    {
        bb.FirstLeafEntries++;
        return NodeStatus.Success;
    }

    /// <summary>A leaf that finishes, so the child completes and the host leaves the hosting node.</summary>
    private static NodeStatus Succeed(ref ChildBb bb, ref BehaviorTreeState state,
                                      ref BTreeContext ctx, int paramIndex)
        => NodeStatus.Success;

    /// <summary>
    /// <c>[succeed-once, stay-running]</c> — the shape that makes the collision observable: a surviving
    /// cursor resumes at the RUNNING leaf and enters the first exactly ONCE; a clobbered one restarts.
    /// </summary>
    private static Interpreter<ChildBb, BTreeContext> BuildRunningChild()
    {
        var b = new BTreeBuilder<ChildBb, BTreeContext>()
            .Sequence(seq => seq.Action(CountAndSucceed).Action(StayRunning));
        return new Interpreter<ChildBb, BTreeContext>(b.Compile("O4_Child"), b.GetRegistry());
    }

    /// <summary><c>[count-and-succeed, succeed]</c> — completes within one tick.</summary>
    private static Interpreter<ChildBb, BTreeContext> BuildCompletingChild()
    {
        var b = new BTreeBuilder<ChildBb, BTreeContext>()
            .Sequence(seq => seq.Action(CountAndSucceed).Action(Succeed));
        return new Interpreter<ChildBb, BTreeContext>(b.Compile("O4_ChildDone"), b.GetRegistry());
    }

    /// <summary>
    /// An entity carrying an occurrence store with the hosted child's tree-state slot attached —
    /// what <c>BehaviorIngressSystem</c> does in production from the behaviour's stateful manifest.
    /// </summary>
    private static Entity CreateHostEntity(EntityRepository world)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            BlueprintBlackboardPartitions.Initialize(
                mem, BlueprintBlackboard1024.TotalSize, (byte)BlueprintBlackboard1024.MaxSlots);

            Assert.True(BlueprintBlackboardPartitions.TryAttach(
                mem, TreeStateKey, HostedSubtree.TreeStatePayloadSize,
                structureHash: 0, OccurrenceKind.BTree, out _),
                "the hosted occurrence's tree-state slot must attach");
        }
        return entity;
    }

    private static EntityRepository CreateWorld()
    {
        var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterUpTo(world, maxTotalSize: 1024);
        return world;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ① — a hosted subtree keeps its own cursor.</b>
    ///
    /// <para>The host is <c>Running</c> at its hosting node and the child is <c>Running</c> at its own
    /// second leaf. ⭐ Both cursors survive because the child ticks against the
    /// <see cref="BehaviorTreeState"/> in ITS OWN SLOT, not the master's.</para>
    ///
    /// <para>⛔ <b>Red-proof:</b> replace <see cref="HostedSubtree.Tick"/> with
    /// <c>child.Tick(ref childBb, ref state, ref ctx)</c> — the pre-<c>O4</c> emission — and this reads
    /// <c>Actual 2</c>.</para>
    /// </summary>
    [Fact]
    public void O4_R1_AHostedSubtreeKeepsItsOwnCursor()
    {
        using var world = CreateWorld();
        var entity = CreateHostEntity(world);
        var child  = BuildRunningChild();
        var childBb = new ChildBb();
        int key = TreeStateKey;

        NodeStatus Orchestrate(ref HostBb master, ref BehaviorTreeState state,
                               ref BTreeContext ctx, int paramIndex)
        {
            master.Ticks++;
            // ⭐ THE O4 SHAPE — the child's OWN state, from its own slot.
            return HostedSubtree.Tick(child, ref childBb, ref ctx, key);
        }

        var hb = new BTreeBuilder<HostBb, BTreeContext>().Sequence(seq => seq.Action(Orchestrate));
        var host = new Interpreter<HostBb, BTreeContext>(hb.Compile("O4_Host"), hb.GetRegistry());

        var bb    = new HostBb();
        var ctx   = new BTreeContext { Self = entity, World = world };
        var state = new BehaviorTreeState();

        Assert.Equal(NodeStatus.Running, host.Tick(ref bb, ref state, ref ctx));
        Assert.Equal(1, bb.Ticks);
        Assert.Equal(1, childBb.FirstLeafEntries);

        Assert.Equal(NodeStatus.Running, host.Tick(ref bb, ref state, ref ctx));
        Assert.Equal(2, bb.Ticks);

        // ⭐⭐ THE RAIL. The child was left Running at its SECOND leaf, so tick 2 resumes there and
        //    does NOT re-enter the first. 🔴 Shared state reads 2 here.
        Assert.Equal(1, childBb.FirstLeafEntries);
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ② — re-entry reset (<c>F14</c>, <c>D4</c>).</b>
    ///
    /// <para>⚠ <b>This is the cost of rail ①, not a separate feature.</b> Sharing the master's state
    /// gave the child an ACCIDENTAL reset: the host's own write clobbered the child's cursor every
    /// tick. ⛔ Own state removes that, so a child that COMPLETED would resume mid-tree the next time
    /// the host entered the hosting node — carrying stale progress into a fresh entry.</para>
    ///
    /// <para>⭐ The child completes each tick, so the host leaves the hosting node each tick and
    /// re-enters on the next. Its first leaf must run EVERY entry.</para>
    /// </summary>
    [Fact]
    public void O4_R2_ACompletedChildResetsBeforeTheHostReEntersIt()
    {
        using var world = CreateWorld();
        var entity = CreateHostEntity(world);
        var child  = BuildCompletingChild();
        var childBb = new ChildBb();
        int key = TreeStateKey;

        NodeStatus Orchestrate(ref HostBb master, ref BehaviorTreeState state,
                               ref BTreeContext ctx, int paramIndex)
        {
            master.Ticks++;
            return HostedSubtree.Tick(child, ref childBb, ref ctx, key);
        }

        // Repeater(-1) so the host re-enters the hosting node on every tick.
        var hb = new BTreeBuilder<HostBb, BTreeContext>()
            .Repeater(-1, rep => rep.Sequence(seq => seq.Action(Orchestrate)));
        var host = new Interpreter<HostBb, BTreeContext>(hb.Compile("O4_HostRepeat"), hb.GetRegistry());

        var bb    = new HostBb();
        var ctx   = new BTreeContext { Self = entity, World = world };
        var state = new BehaviorTreeState();

        host.Tick(ref bb, ref state, ref ctx);
        int afterFirst = childBb.FirstLeafEntries;
        Assert.True(afterFirst >= 1, "the child must have run at least once");

        host.Tick(ref bb, ref state, ref ctx);

        // ⭐⭐ THE RAIL. A COMPLETED child starts from the top on the next entry — its first leaf runs
        //    again. 🔴 Without D4's clear, the stale cursor resumes past it and this stays flat.
        Assert.True(childBb.FirstLeafEntries > afterFirst,
            $"a completed child must reset before re-entry; first leaf ran {afterFirst} then " +
            $"{childBb.FirstLeafEntries} times");
    }

    /// <summary>
    /// ⭐ <b>Rail ③ — a wrong or missing key FAILS LOUDLY</b> (§19.6 ⑤).
    ///
    /// <para>⛔ <c>D3</c> makes the hosting site responsible for its own identity, so a site that
    /// computes the wrong key would otherwise read as <i>"the subtree just fails"</i> — the silent
    /// slot miss <c>OccurrenceSlotKey</c>'s header says <c>A1</c> exists to kill.</para>
    /// </summary>
    [Fact]
    public void O4_R3_AMissingSlotThrowsRatherThanFailingSilently()
    {
        using var world = CreateWorld();
        var entity = CreateHostEntity(world);
        var child  = BuildRunningChild();
        var childBb = new ChildBb();
        var ctx = new BTreeContext { Self = entity, World = world };

        int wrongKey = OccurrenceSlots.TreeStateKeyFor(
            HostAsset, new Guid("04000000-0000-0000-0000-0000000000ff"), ChildAsset);

        Assert.Throws<InvalidOperationException>(
            () => HostedSubtree.Tick(child, ref childBb, ref ctx, wrongKey));
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ④ — the key DISCRIMINATES by host and by site.</b>
    ///
    /// <para>🔴 This is the one that would have caught §19.7 ①: <c>ComputeNested</c> returns the ROOT
    /// form verbatim when <c>hostKey == 0</c>, <b>dropping <c>siteId</c> entirely</b>. ⇒ had
    /// <see cref="OccurrenceSlots.TreeStateKeyFor"/> passed <c>hostKey: 0</c>, two sites hosting the
    /// same child asset would collide and the root case would swallow the difference in silence.</para>
    /// </summary>
    [Fact]
    public void O4_R4_TheKeyDiscriminatesByHostAndBySite()
    {
        var siteB      = new Guid("04000000-0000-0000-0000-00000000c200");
        var otherHost  = new Guid("04000000-0000-0000-0000-00000000da99");
        var otherChild = new Guid("04000000-0000-0000-0000-00000000db99");

        int baseline = OccurrenceSlots.TreeStateKeyFor(HostAsset, PatrolSite, ChildAsset);

        // ⛔ THE COLLISION §19.7 ① FOUND: same host, same child, DIFFERENT SITE.
        Assert.NotEqual(baseline, OccurrenceSlots.TreeStateKeyFor(HostAsset, siteB, ChildAsset));

        Assert.NotEqual(baseline, OccurrenceSlots.TreeStateKeyFor(otherHost, PatrolSite, ChildAsset));
        Assert.NotEqual(baseline, OccurrenceSlots.TreeStateKeyFor(HostAsset, PatrolSite, otherChild));

        // ⭐ And it is a pure function — the emitter bakes this literal, a hand-written host calls it,
        //   and D3 requires them to agree byte-for-byte.
        Assert.Equal(baseline, OccurrenceSlots.TreeStateKeyFor(HostAsset, PatrolSite, ChildAsset));

        // ⚠ A host identity is never 0 — a 0 would silently re-enter ComputeNested's ROOT branch.
        Assert.NotEqual(0, OccurrenceSlots.IdentityOf(HostAsset));
    }
}
