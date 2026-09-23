using System;
using System.Runtime.CompilerServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
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

    /// <summary>The hosted child's state, read straight out of its slot.</summary>
    private static ref BehaviorTreeState ReadChildState(EntityRepository world, Entity entity, int key)
    {
        byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        Assert.True(store != null);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int off));
        return ref Unsafe.AsRef<BehaviorTreeState>(store + off);
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
    /// ⭐⭐ <b>Rail ②a — <c>D4</c> HALF ONE: a COMPLETED child starts fresh on the next entry.</b>
    ///
    /// <para>⚠ <b>This is the cost of rail ①, not a separate feature.</b> Sharing the master's state
    /// gave the child an ACCIDENTAL reset — the host's own write clobbered its cursor every tick.
    /// Own state removes that, so a completed child would carry stale progress into a fresh entry.</para>
    ///
    /// <para>⭐ The host's tree SUCCEEDS each tick, so the interpreter zeroes its cursor and tick 2
    /// re-enters the hosting node from the top. ⛔ <b>No <c>Repeater(-1)</c>:</b> a repeater over an
    /// always-succeeding body spins forever INSIDE one tick — the first version of this rail hung the
    /// test host for 44 minutes.</para>
    /// </summary>
    [Fact]
    public void O4_R2a_ACompletedChildStartsFreshOnTheNextEntry()
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

        var hb = new BTreeBuilder<HostBb, BTreeContext>().Sequence(seq => seq.Action(Orchestrate));
        var host = new Interpreter<HostBb, BTreeContext>(hb.Compile("O4_HostDone"), hb.GetRegistry());

        var bb    = new HostBb();
        var ctx   = new BTreeContext { Self = entity, World = world };
        var state = new BehaviorTreeState();

        Assert.Equal(NodeStatus.Success, host.Tick(ref bb, ref state, ref ctx));
        Assert.Equal(1, childBb.FirstLeafEntries);

        Assert.Equal(NodeStatus.Success, host.Tick(ref bb, ref state, ref ctx));

        // ⭐⭐ THE RAIL. A completed child re-runs its first leaf on the next entry.
        Assert.Equal(2, childBb.FirstLeafEntries);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ②b — <c>D4</c> HALF TWO, the actual <c>F14</c> case: the host ABANDONS a child
    /// that is still <c>Running</c>.</b>
    ///
    /// <para>🔴🔴 <b>Half one cannot cover this, and finding that out is what this rail is for.</b> A
    /// hosting action runs only when the host ENTERS it. If the host leaves the hosting node while the
    /// child is mid-tree, the action is never called again ⇒ nothing clears the cursor and the next
    /// entry RESUMES MID-TREE. ⭐ <see cref="HostedSubtree.Reset"/> is that clear, and the kernel's
    /// existing deactivator sweep is what invokes it — no ExtDeps change.</para>
    ///
    /// <para>⚠ <b>Driven directly rather than through an abandoning tree.</b> The interpreter's
    /// composites RESUME the running branch by design, so provoking a genuine mid-flight abandon needs
    /// a Parallel or a reactive abort — neither of which this rail needs in order to pin what
    /// <c>Reset</c> guarantees. ⛔ The WIRING (deactivator registered ⇒ node marked resource-owning
    /// ⇒ sweep invokes it) is pinned separately by rail ②c.</para>
    /// </summary>
    [Fact]
    public void O4_R2b_ResetClearsAChildLeftRunning()
    {
        using var world = CreateWorld();
        var entity = CreateHostEntity(world);
        var child  = BuildRunningChild();
        var childBb = new ChildBb();
        var ctx = new BTreeContext { Self = entity, World = world };
        int key = TreeStateKey;

        // Leave the child suspended mid-tree.
        Assert.Equal(NodeStatus.Running, HostedSubtree.Tick(child, ref childBb, ref ctx, key));
        Assert.Equal(1, childBb.FirstLeafEntries);
        Assert.NotEqual(0, ReadChildState(world, entity, key).RunningNodeIndex);

        // ⭐ The host abandons it.
        HostedSubtree.Reset(ref ctx, key);
        Assert.Equal(0, ReadChildState(world, entity, key).RunningNodeIndex);

        // ⭐⭐ THE RAIL. The next entry starts from the top — the first leaf runs again.
        Assert.Equal(NodeStatus.Running, HostedSubtree.Tick(child, ref childBb, ref ctx, key));
        Assert.Equal(2, childBb.FirstLeafEntries);
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ②c — the WIRING: registering a deactivator is what opts the hosting node in.</b>
    ///
    /// <para>⭐ <c>BTreeBuilder.Compile</c> derives <c>IsResourceOwning</c> from
    /// <c>_registry.TryGetDeactivator(methodName)</c>, and <c>Interpreter.SweepExitedNodes</c> invokes
    /// a deactivator only for nodes carrying that bit. ⇒ <b>registration IS the opt-in</b>, and this
    /// pins that chain so a future refactor cannot quietly break <c>F14</c> while every other rail
    /// stays green.</para>
    /// </summary>
    [Fact]
    public void O4_R2c_RegisteringADeactivatorMarksTheHostingNodeResourceOwning()
    {
        const string key = "O4_HostingNode";
        var hb = new BTreeBuilder<HostBb, BTreeContext>();

        hb.GetRegistry().Register(key,
            static (ref HostBb bb, ref BehaviorTreeState st, ref BTreeContext c, int pi) => NodeStatus.Running);

        // ⛔ Without a deactivator the node is NOT resource-owning, so the sweep would skip it.
        var without = new BTreeBuilder<HostBb, BTreeContext>();
        without.GetRegistry().Register(key,
            static (ref HostBb bb, ref BehaviorTreeState st, ref BTreeContext c, int pi) => NodeStatus.Running);
        without.Sequence(seq => seq.Action(key));
        var blobWithout = without.Compile("O4_NoDeactivator");
        Assert.False(blobWithout.Nodes[1].IsResourceOwning);

        // ⭐ Registering one flips the bit — the opt-in, with no kernel change.
        hb.GetRegistry().RegisterDeactivator(key,
            static (ref HostBb bb, ref BehaviorTreeState st, ref BTreeContext c, int pi) => { });
        hb.Sequence(seq => seq.Action(key));
        var blobWith = hb.Compile("O4_WithDeactivator");
        Assert.True(blobWith.Nodes[1].IsResourceOwning);
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

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ⑧ — the TRIPWIRE: why the end-to-end ABANDON rail does not exist.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §22.5.
    ///
    /// <para>🔴🔴 <b>Measured: <c>BehaviorTreeState.PushNode</c> / <c>PopNode</c> have ZERO production
    /// callers</b> (grep and <c>search_graph</c> agree — only <c>Fbt.Tests</c>' own
    /// <c>DataStructuresTests</c> touch them). ⇒ the interpreter never fills
    /// <c>NodeIndexStack</c>, so the "active path" <c>SweepExitedNodes</c> diffs is <b>one entry
    /// wide</b>: <c>RunningNodeIndex</c>.</para>
    ///
    /// <para>⛔⛔ <b>The consequence for <c>F14</c>.</b> A deactivator can only fire for a node that
    /// LEAVES <c>RunningNodeIndex</c> — and the only thing that moves it off a hosting action is the
    /// action itself returning non-<c>Running</c>, which is the COMPLETION case
    /// <see cref="HostedSubtree.Tick"/> already handles. 📐 The composites cannot produce the other
    /// case: <c>Sequence</c>/<c>Selector</c> skip only children BEFORE the running one and never
    /// re-evaluate a higher-priority sibling; <c>ObserverSelector</c> is routed straight to
    /// <c>ExecuteSelector</c> (<i>"uses standard selector semantics"</i>) so it does not abort;
    /// <c>Parallel</c> overwrites <c>RunningNodeIndex</c> with its OWN index, so a hosting action under
    /// one never reaches the path at all; <c>Cooldown</c>'s early-<c>Failure</c> arm is gated on a
    /// token set only on child <c>Success</c>. ⇒ <b>a genuine mid-flight abandon is UNREACHABLE
    /// in-tree today</b>, which is why §20.2's <i>"needs a <c>Parallel</c> or a reactive abort"</i> was
    /// optimistic — neither delivers one.</para>
    ///
    /// <para>⭐⭐ <b>So this rail pins the PREMISE instead of the behaviour.</b> A path stack is the
    /// prerequisite for any real abort, so the day someone fills it this rail reddens and says: the
    /// end-to-end <c>F14</c> case has become reachable and now needs a real rail. ⛔ A silently-absent
    /// test would say nothing.</para>
    /// </summary>
    [Fact]
    public void O4_R8_TheSweepPathIsOneEntryWide_SoAMidFlightAbandonIsUnreachable()
    {
        using var world = CreateWorld();
        var entity = CreateHostEntity(world);
        var child  = BuildRunningChild();
        var childBb = new ChildBb();
        int key = TreeStateKey;

        NodeStatus Orchestrate(ref HostBb master, ref BehaviorTreeState st,
                               ref BTreeContext c, int paramIndex)
            => HostedSubtree.Tick(child, ref childBb, ref c, key);

        // Deliberately NESTED — a real path stack would have something to push.
        var hb = new BTreeBuilder<HostBb, BTreeContext>()
            .Sequence(outer => outer.Selector(inner => inner.Action(Orchestrate)));
        var host = new Interpreter<HostBb, BTreeContext>(hb.Compile("O4_Nested"), hb.GetRegistry());

        var bb    = new HostBb();
        var ctx   = new BTreeContext { Self = entity, World = world };
        var state = new BehaviorTreeState();

        Assert.Equal(NodeStatus.Running, host.Tick(ref bb, ref state, ref ctx));

        // ⭐ The hosting action IS running, three levels down…
        Assert.NotEqual(0, state.RunningNodeIndex);

        // ⭐⭐ THE TRIPWIRE. …and nothing recorded the way down.
        Assert.Equal(0, state.StackPointer);
        for (int i = 0; i < 8; i++)
            Assert.Equal(0, state.NodeIndexStack[i]);
    }

    // ═══ F14b — THE EXTERNAL RESET PATH ═══════════════════════════════════════════════════════
    //  📄 DESIGN_Occurrence_Scoped_Storage.md §21.2. Rails ②a/②b cover the resets that arrive
    //  THROUGH A TICK. BehaviorIngressSystem zeroes BrainBTreeState.State WITHOUT one, so neither
    //  fires and a hosted child resumes mid-tree while the host restarts at its root.

    /// <summary>The manifest a hosted subtree produces — what <c>CollectHostedTreeStateSlots</c> emits.</summary>
    private static StatefulSlotInfo HostedManifestSlot() => new(
        TreeStateKey,
        HostedSubtree.TreeStatePayloadSize,
        StructureHash: 0u,
        WorkingStateType: typeof(BehaviorTreeState),
        NodeLabel: "hosted subtree");

    /// <summary>A BTree behaviour whose only stateful slot is a hosted occurrence's cursor.</summary>
    private static BehaviorDefinition HostingBehavior(string name)
    {
        // ⭐ P4-②: the builder's generic must agree with the Interpreter it feeds below — both `byte`.
        //   ⚠ A builder generic is BUILD-TIME only (Compile() returns an untyped blob), so this is
        //     free HERE precisely because the tree binds a raw delegate, not a selector.
        var b = new BTreeBuilder<byte, BTreeContext>()
            .Sequence(seq => seq.Action(
                static (ref byte bb, ref BehaviorTreeState st, ref BTreeContext c, int pi)
                    => NodeStatus.Running));

        return new BehaviorDefinition
        {
            Name                 = name,
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter     = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            StatefulWorkingSlots = new[] { HostedManifestSlot() },
        };
    }

    /// <summary>
    /// A world and an entity shaped the way production hands them to <see cref="BehaviorIngressSystem"/>
    /// — ⚠ with NO tier component, so the system provisions it from the manifest, as it does live.
    /// </summary>
    private static (EntityRepository world, BehaviorRegistry registry, BehaviorIngressSystem sys, Entity entity)
        CreateIngressFixture()
    {
        var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterAll(world);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

        var registry = new BehaviorRegistry();
        return (world, registry, new BehaviorIngressSystem(registry), entity);
    }

    /// <summary>Leaves BOTH cursors mid-tree, the way a live tick would.</summary>
    private static void PoisonBothCursors(EntityRepository world, Entity entity)
    {
        ref var root = ref RootStateAccess.RequireStateRef(world, entity);   // ⛔ O7c-②: the cursor is an occurrence slot now (§31).
        root.RunningNodeIndex = 7;
        ReadChildState(world, entity, TreeStateKey).RunningNodeIndex = 7;
    }

    private static void AssertBothCursorsReset(EntityRepository world, Entity entity)
    {
        // ⚠ Non-vacuity: the HOST's reset is the behaviour that has always worked. If this reads 7
        //   the fixture never reached the reset at all and the rail below would pass for free.
        Assert.Equal(0, RootStateAccess.GetStateOrDefault(world, entity).RunningNodeIndex);

        // ⭐⭐ THE RAIL. 🔴 Reads 7 without ResetHostedTreeStates.
        Assert.Equal(0, ReadChildState(world, entity, TreeStateKey).RunningNodeIndex);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ⑤ — <c>F14b</c>: re-assigning a behaviour by NAME resets the hosted cursor too.</b>
    ///
    /// <para>🔴 <b>Why the SAME behaviour twice is the sharp case.</b> <c>BehaviorIngressSystem</c>
    /// detaches the outgoing behaviour's slots only when <c>previousBehaviorId != behaviorId</c>
    /// (<c>:146</c>), and <c>AttachSlotsToMemory</c>'s idempotent arm deliberately preserves a slot
    /// whose size and hash match. ⇒ on a re-assign the hosted cursor is NOT reclaimed and NOT
    /// re-zeroed — while the host's own cursor is zeroed unconditionally one line later.</para>
    ///
    /// <para>⛔ <b>Red-proof:</b> delete the <c>ResetHostedTreeStates</c> call at <c>:165</c> and this
    /// reads <c>Actual 7</c> while every other rail stays green.</para>
    /// </summary>
    [Fact]
    public void O4_R5_ReassigningABehaviourByNameResetsTheHostedCursor()
    {
        var (world, registry, sys, entity) = CreateIngressFixture();
        using var _w = world;

        const string name = "O4_HostingBehaviour";
        const int    id   = 8401;
        registry.Register(id, name, HostingBehavior(name));

        void Assign()
        {
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = entity, BehaviorName = name, JsonParams = string.Empty,
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        Assign();                                  // provisions the tier and attaches the hosted slot
        PoisonBothCursors(world, entity);
        Assign();                                  // the EXTERNAL reset — no tick, so no sweep

        AssertBothCursorsReset(world, entity);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ⑥ — <c>F14b</c>: assigning by HASH resets the hosted cursor too.</b>
    ///
    /// <para>🔴🔴 <b>This arm is worse than rail ⑤'s.</b> The <c>AssignBehaviorHashEvent</c> handler
    /// (<c>:211-244</c>) touches no slots <b>at all</b> — it neither detaches the outgoing behaviour's
    /// nor provisions the incoming one's — yet it zeroes <c>BrainBTreeState.State</c> like the others.
    /// ⇒ every hosted cursor survives a phase transition that restarts the host.</para>
    /// </summary>
    [Fact]
    public void O4_R6_AssigningByHashResetsTheHostedCursor()
    {
        var (world, registry, sys, entity) = CreateIngressFixture();
        using var _w = world;

        const string name = "O4_HostingBehaviourByHash";
        const int    id   = 8402;
        registry.Register(id, name, HostingBehavior(name));

        // Provision through the by-NAME path, which is the only one that attaches slots.
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = name, JsonParams = string.Empty,
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);

        PoisonBothCursors(world, entity);

        world.Bus.Publish(new AssignBehaviorHashEvent { Entity = entity, BehaviorHash = id });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);

        AssertBothCursorsReset(world, entity);
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ⑦ — the reset is NARROW: author working state survives, a cursor does not.</b>
    ///
    /// <para>⛔ <c>AttachSlotsToMemory</c>'s idempotent arm preserves a matching slot's payload on
    /// purpose — <i>"no churn on soft reload / no-op re-assign"</i>. ⚠ <c>F14b</c>'s fix must not
    /// quietly turn every re-assign into a working-state wipe, which is what a blanket
    /// <i>"zero every slot of the incoming manifest"</i> would have done. ⭐ The manifest itself draws
    /// the line: <c>WorkingStateType == typeof(BehaviorTreeState)</c> marks exactly the slots the
    /// emitter adds for hosting.</para>
    /// </summary>
    [Fact]
    public void O4_R7_TheResetLeavesAuthorWorkingStateAlone()
    {
        var (world, registry, sys, entity) = CreateIngressFixture();
        using var _w = world;

        int authorKey = (OccurrenceSlots.TreeStateKeyFor(HostAsset, PatrolSite, ChildAsset) ^ 0x5A5A)
                        & 0x7FFFFFFF;

        const string name = "O4_MixedManifest";
        const int    id   = 8403;

        var hosting = HostingBehavior(name);
        registry.Register(id, name, new BehaviorDefinition
        {
            Name                 = name,
            BrainTier            = hosting.BrainTier,
            BTreeInterpreter     = hosting.BTreeInterpreter,
            StatefulWorkingSlots = new[]
            {
                HostedManifestSlot(),
                // An authored WorkingState — 4 bytes, and NOT a BehaviorTreeState.
                new StatefulSlotInfo(authorKey, sizeof(int), 0u, typeof(int), "author state"),
            },
        });

        void Assign()
        {
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = entity, BehaviorName = name, JsonParams = string.Empty,
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        Assign();
        PoisonBothCursors(world, entity);
        {
            byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, authorKey, out int off));
            *(int*)(store + off) = 0x1234;
        }

        Assign();

        AssertBothCursorsReset(world, entity);

        // ⭐⭐ THE RAIL. The author's slot is untouched by the cursor reset.
        {
            byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, authorKey, out int off));
            Assert.Equal(0x1234, *(int*)(store + off));
        }
    }
}
