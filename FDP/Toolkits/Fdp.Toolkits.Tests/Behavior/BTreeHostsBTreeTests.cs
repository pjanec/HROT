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
/// ⭐⭐⭐ <b><c>E6</c> / <c>CE-369</c> — A BTREE NODE HOSTS ANOTHER BTREE, THROUGH THE REAL KERNEL.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §33.8.
///
/// <para>🔒 <b>User, <c>2026-09-27</c>:</b> *"for sure we need end-to-end test that actually ticks a
/// host and asserts the child advanced."*</para>
///
/// <para>🔴🔴 <b>WHY THAT SENTENCE IS THE WHOLE POINT, and it is measured rather than rhetorical.</b>
/// The retired <c>BTreeOrchestratorEmitCore</c> <b>passed its shape rails for months</b> while
/// emitting a call to <c>{Child}.GetInterpreter()</c> — <i>a method defined nowhere</i>
/// (<c>CE-333</c>/<c>CE-335</c>) — because <b>nothing ever ran what it emitted</b>. ⇒ ⭐⭐ a rail over
/// emitted TEXT, or over a registered TABLE, cannot tell <i>"hosting works"</i> from <i>"hosting is
/// spelled correctly"</i>. These tick a real host interpreter and watch the child move.</para>
///
/// <para>⚠ <b>SCOPE, STATED HONESTLY:</b> these drive the HOST INTERPRETER directly, which exercises
/// the whole feature path — kernel dispatch → <see cref="OccurrenceSubtreeHost"/> →
/// <see cref="BTreeHostedSites"/> → <see cref="HostedSubtree"/> → the child's own interpreter and its
/// own slot. ⛔ They do NOT go through <c>BrainTickSystem</c>, so the registration/provisioning path a
/// real entity takes is not covered here. That is a named gap, not a silent one.</para>
/// </summary>
public sealed unsafe class BTreeHostsBTreeTests : IDisposable
{
    private const string HostName  = "E6_Host";
    private const string ChildName = "E6_Child";

    private static readonly Guid SiteId = new("06000000-0000-0000-0000-0000000051e0");

    public void Dispose()
    {
        // ⚠ Both tables are process-wide startup state; a leaked binding would make the NEXT test
        //   pass for the wrong reason.
        BTreeHostedSites.ClearForTests();
        HostedChildren.ClearForTests();
    }

    // ── the two trees ─────────────────────────────────────────────────────────

    private static int _childFirstLeafEntries;
    private static int _childTicks;

    /// <summary>The child's FIRST leaf — the observable that says RESUMED or RESTARTED.</summary>
    private static NodeStatus CountAndSucceed(ref byte bb, ref BehaviorTreeState state,
                                              ref BTreeContext ctx, int paramIndex)
    {
        _childFirstLeafEntries++;
        return NodeStatus.Success;
    }

    /// <summary>A leaf that never finishes, so the interpreter must remember where it is.</summary>
    private static NodeStatus StayRunning(ref byte bb, ref BehaviorTreeState state,
                                          ref BTreeContext ctx, int paramIndex)
    {
        _childTicks++;
        return NodeStatus.Running;
    }

    private static NodeStatus HostFails(ref byte bb, ref BehaviorTreeState state,
                                        ref BTreeContext ctx, int paramIndex)
        => NodeStatus.Failure;

    /// <summary>The higher-priority branch's guard — flipped by the test to force an abandonment.</summary>
    private static bool _preempt;

    private static NodeStatus PreemptGuard(ref byte bb, ref BehaviorTreeState state,
                                           ref BTreeContext ctx, int paramIndex)
        => _preempt ? NodeStatus.Success : NodeStatus.Failure;

    private static NodeStatus PreemptBody(ref byte bb, ref BehaviorTreeState state,
                                          ref BTreeContext ctx, int paramIndex)
        => NodeStatus.Running;

    /// <summary>
    /// <c>[count-and-succeed, stay-running]</c> — a surviving cursor resumes at the RUNNING leaf and
    /// enters the first exactly ONCE; a clobbered or reset one re-enters it.
    /// </summary>
    private static Interpreter<byte, BTreeContext> BuildChild()
    {
        var b = new BTreeBuilder<byte, BTreeContext>()
            .Sequence(seq => seq.Action(CountAndSucceed).Action(StayRunning));
        return new Interpreter<byte, BTreeContext>(b.Compile(ChildName), b.GetRegistry());
    }

    /// <summary>A host whose single leaf is a <c>Subtree</c> node — the shape under test.</summary>
    private static BehaviorTreeBlob BuildHostBlob(out ActionRegistry<byte, BTreeContext> registry)
    {
        var b = new BTreeBuilder<byte, BTreeContext>()
            .Sequence(seq => seq.Subtree(ChildName, visualId: SiteId));
        registry = b.GetRegistry();
        return b.Compile(HostName);
    }

    /// <summary>
    /// ⭐⭐ The only shape that actually ABANDONS a still-<c>Running</c> child: an
    /// <c>ObserverSelector</c> whose HIGHER-priority branch is guarded by a flag. While the guard
    /// fails the selector falls through to the subtree; once it passes, the subtree node leaves the
    /// active path and <c>SweepExitedNodes</c> must notice.
    ///
    /// <para>⚠⚠ <b>My first draft used <c>Sequence(Subtree, FailingSibling)</c> and it could not
    /// reproduce anything</b> — a subtree that returns <c>Running</c> parks the sequence AT the
    /// hosting node, so the sibling never runs and the node never exits. The rail failed for that
    /// reason, not because the arm was broken.</para>
    /// </summary>
    private static BehaviorTreeBlob BuildAbandoningHostBlob(out ActionRegistry<byte, BTreeContext> registry)
    {
        var b = new BTreeBuilder<byte, BTreeContext>()
            .ObserverSelector(sel => sel
                .Sequence(hi => hi.Condition(PreemptGuard).Action(PreemptBody))
                .Subtree(ChildName, visualId: SiteId));
        registry = b.GetRegistry();
        return b.Compile(HostName);
    }

    // ── the world ─────────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterUpTo(world, maxTotalSize: 1024);
        return world;
    }

    /// <summary>
    /// Registers the child so <see cref="HostedChildren"/> can resolve it, plans the host's sites and
    /// binds them — i.e. exactly the two calls the generated registrar emits (<c>CE-364</c>).
    /// </summary>
    private static BTreeHostedSites.Plan RegisterAndBind(
        BehaviorRegistry beh, BehaviorTreeBlob hostBlob, Interpreter<byte, BTreeContext> child)
    {
        beh.Register(ChildName, new BehaviorDefinition
        {
            Name             = ChildName,
            BrainTier        = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = child,
        });

        var plan = BTreeHostedSites.PlanFor(hostBlob, HostName);
        BTreeHostedSites.Bind(beh, hostBlob, plan);
        return plan;
    }

    /// <summary>An entity whose occurrence store carries the planned hosted slot.</summary>
    private static Entity CreateHostEntity(EntityRepository world, in BTreeHostedSites.Plan plan)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            BlueprintBlackboardPartitions.Initialize(
                mem, BlueprintBlackboard1024.TotalSize, (byte)BlueprintBlackboard1024.MaxSlots);

            foreach (var slot in plan.Slots)
            {
                Assert.True(BlueprintBlackboardPartitions.TryAttach(
                    mem, slot.SlotKey, HostedSubtree.TreeStatePayloadSize,
                    structureHash: 0, OccurrenceKind.BTree, out _),
                    "the planned hosted slot must attach — HostedSubtree.Tick THROWS without it");
            }
        }
        return entity;
    }

    private static ref BehaviorTreeState ReadChildState(EntityRepository world, Entity entity, int key)
    {
        byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        Assert.True(store != null);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int off));
        return ref Unsafe.AsRef<BehaviorTreeState>(store + off);
    }

    // ── E6_R1 — the acceptance gate ───────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>E6_R1</c> — THE ACCEPTANCE GATE. A hand-written host ticks, and the child ADVANCES.</b>
    ///
    /// <para>⭐ Hand-written on purpose: this is the route with no emitter, and the one the user asked
    /// for. Nothing here is generated — <c>BTreeBuilder</c> → <c>Compile</c> → <c>PlanFor</c> →
    /// <c>Bind</c>, exactly what a <c>[BTreeDefinition]</c> method and the generated registrar do.</para>
    ///
    /// <para>⛔ <b>Red-proof (<c>E6_R6</c>):</b> revert <c>Interpreter</c>'s <c>NodeType.Subtree</c> case
    /// to <c>return NodeStatus.Failure</c> and this reddens at the very first assertion.</para>
    /// </summary>
    [Fact]
    public void E6_R1_AHandWrittenHostTicksItsChild_AndTheChildAdvances()
    {
        _childFirstLeafEntries = 0;
        _childTicks = 0;

        using var world = CreateWorld();
        var beh   = new BehaviorRegistry();
        var child = BuildChild();

        var hostBlob = BuildHostBlob(out var hostRegistry);
        var plan     = RegisterAndBind(beh, hostBlob, child);

        // ⭐ The site was FOUND by walking the blob — the step that makes hand-written trees work.
        Assert.Single(plan.Entries);
        Assert.Equal(ChildName, plan.Entries[0].ChildName);

        var entity = CreateHostEntity(world, plan);

        var host = new Interpreter<byte, BTreeContext>(hostBlob, hostRegistry)
        {
            SubtreeHost = OccurrenceSubtreeHost.Instance,
        };

        byte hostBb = 0;
        var  ctx    = new BTreeContext { Self = entity, World = world };
        var  state  = new BehaviorTreeState();

        // ── tick 1 ────────────────────────────────────────────────────────────
        Assert.Equal(NodeStatus.Running, host.Tick(ref hostBb, ref state, ref ctx));
        Assert.Equal(1, _childFirstLeafEntries);
        Assert.Equal(1, _childTicks);

        // ── tick 2 ────────────────────────────────────────────────────────────
        Assert.Equal(NodeStatus.Running, host.Tick(ref hostBb, ref state, ref ctx));

        // ⭐⭐ THE RAIL. The child RAN AGAIN (2 ticks of its running leaf) and RESUMED rather than
        //    restarting (its first leaf was entered exactly once). Both halves matter: the first
        //    says hosting happened at all, the second says it happened against the child's OWN state.
        Assert.Equal(2, _childTicks);
        Assert.Equal(1, _childFirstLeafEntries);
    }

    // ── E6_R2 — the cursors are separate ──────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b><c>E6_R2</c> — the host's cursor and the child's are DIFFERENT storage.</b>
    /// 🔒 <c>O4_R1</c>'s claim, now reached through the KERNEL rather than a direct call.
    /// </summary>
    [Fact]
    public void E6_R2_TheHostAndTheChildKeepSeparateCursors()
    {
        _childFirstLeafEntries = 0;
        _childTicks = 0;

        using var world = CreateWorld();
        var beh      = new BehaviorRegistry();
        var hostBlob = BuildHostBlob(out var hostRegistry);
        var plan     = RegisterAndBind(beh, hostBlob, BuildChild());
        var entity   = CreateHostEntity(world, plan);

        var host = new Interpreter<byte, BTreeContext>(hostBlob, hostRegistry)
        {
            SubtreeHost = OccurrenceSubtreeHost.Instance,
        };

        byte hostBb = 0;
        var  ctx    = new BTreeContext { Self = entity, World = world };
        var  state  = new BehaviorTreeState();

        host.Tick(ref hostBb, ref state, ref ctx);

        ref var childState = ref ReadChildState(world, entity, plan.Entries[0].TreeStateSlotKey);

        // ⭐ The child is parked on its SECOND leaf; the host on its own hosting node. Sharing one
        //   BehaviorTreeState would make these equal by construction.
        Assert.NotEqual(state.RunningNodeIndex, childState.RunningNodeIndex);
    }

    // ── E6_R3 — F14, in two composable halves ────────────────────────────────

    /// <summary>⚠ A recording stand-in, so the KERNEL's routing can be observed directly.</summary>
    private sealed class SpyHost : ISubtreeHost<byte, BTreeContext>
    {
        public int Ticks;
        public System.Collections.Generic.List<int> ResetNodes { get; } = new();
        public NodeStatus Next = NodeStatus.Running;

        public NodeStatus Tick(ref byte bb, ref BTreeContext ctx, BehaviorTreeBlob blob, int nodeIndex)
        {
            Ticks++;
            return Next;
        }

        public void Reset(ref BTreeContext ctx, BehaviorTreeBlob blob, int nodeIndex)
            => ResetNodes.Add(nodeIndex);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>E6_R3a</c> — the KERNEL routes a Subtree node's exit to
    /// <see cref="ISubtreeHost{TBlackboard,TContext}.Reset"/>.</b>
    ///
    /// <para>🔴 <b>This is the half that is NOT free</b> and it needed TWO kernel changes:
    /// <c>SweepExitedNode</c> must branch on <c>NodeType.Subtree</c> — the ordinary path resolves a
    /// deactivator by <c>MethodNames[PayloadIndex]</c>, and a Subtree node's PayloadIndex indexes
    /// <c>SubtreeAssetIds</c>, the WRONG array — and <c>ExecuteSubtree</c> must record
    /// <c>RunningNodeIndex</c> the way <c>ExecuteAction</c> does, or the node never enters the path
    /// the sweep diffs and is never visited at all.</para>
    ///
    /// <para>⛔ <b>Red-proof:</b> remove either change and <c>ResetNodes</c> comes back empty.</para>
    /// </summary>
    [Fact]
    public void E6_R3a_TheKernelRoutesASubtreeNodesExit_ToTheHostsReset()
    {
        using var world = CreateWorld();
        var hostBlob = BuildHostBlob(out var hostRegistry);
        var spy      = new SpyHost();

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        var host = new Interpreter<byte, BTreeContext>(hostBlob, hostRegistry) { SubtreeHost = spy };

        byte hostBb = 0;
        var  ctx    = new BTreeContext { Self = entity, World = world };
        var  state  = new BehaviorTreeState();

        // Tick 1 — the subtree is Running, so the node is recorded on the active path.
        Assert.Equal(NodeStatus.Running, host.Tick(ref hostBb, ref state, ref ctx));
        Assert.Equal(1, spy.Ticks);
        Assert.Empty(spy.ResetNodes);

        // Tick 2 — the subtree leaves the path. The sweep must notice and route the reset.
        spy.Next = NodeStatus.Success;
        host.Tick(ref hostBb, ref state, ref ctx);

        Assert.NotEmpty(spy.ResetNodes);
        foreach (int idx in spy.ResetNodes)
            Assert.Equal(NodeType.Subtree, hostBlob.Nodes[idx].Type);
    }

    /// <summary>
    /// ⭐⭐ <b><c>E6_R3b</c> — and the reset the kernel routes actually ZEROES the child's cursor.</b>
    /// Together with <c>E6_R3a</c> this is <c>F14</c>: the kernel routes it, and the host clears it.
    ///
    /// <para>⚠⚠ <b>NOT a single end-to-end abandonment, and that is deliberate rather than a
    /// shortcut.</b> 📐 Measured while writing this: the shapes that abandon a STILL-<c>Running</c>
    /// child in this kernel are a <c>Parallel</c> child sweep and the out-of-bounds path reset —
    /// a <c>Selector</c>/<c>ObserverSelector</c> RESUMES into the running child rather than
    /// re-evaluating, so the obvious "a higher-priority branch preempts it" shape cannot occur.
    /// ⛔ Composing the two halves is honest; asserting a shape the kernel cannot produce would not
    /// have been. 📋 A Parallel-hosted variant is named as a gap.</para>
    /// </summary>
    [Fact]
    public void E6_R3b_TheHostsReset_ZeroesTheChildsCursor()
    {
        _childFirstLeafEntries = 0;

        using var world = CreateWorld();
        var beh      = new BehaviorRegistry();
        var hostBlob = BuildHostBlob(out var hostRegistry);
        var plan     = RegisterAndBind(beh, hostBlob, BuildChild());
        var entity   = CreateHostEntity(world, plan);

        var host = new Interpreter<byte, BTreeContext>(hostBlob, hostRegistry)
        {
            SubtreeHost = OccurrenceSubtreeHost.Instance,
        };

        byte hostBb = 0;
        var  ctx    = new BTreeContext { Self = entity, World = world };
        var  state  = new BehaviorTreeState();
        int  key    = plan.Entries[0].TreeStateSlotKey;
        int  node   = plan.Entries[0].NodeIndex;

        host.Tick(ref hostBb, ref state, ref ctx);
        Assert.NotEqual(0, ReadChildState(world, entity, key).RunningNodeIndex);

        // ⭐ Exactly what the kernel calls on the exit sweep (E6_R3a proves it is called).
        OccurrenceSubtreeHost.Instance.Reset(ref ctx, hostBlob, node);

        Assert.Equal(0, ReadChildState(world, entity, key).RunningNodeIndex);

        // ⭐⭐ And observably: the child restarts at its FIRST leaf instead of resuming mid-tree.
        host.Tick(ref hostBb, ref state, ref ctx);
        Assert.Equal(2, _childFirstLeafEntries);
    }

    // ── E6_R5 — the gating claim ──────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b><c>E6_R5</c> — a tree that hosts NOTHING plans no slot and costs no payload.</b>
    /// ⛔ This is the claim that protects every shipped asset: 0 of 26 <c>*.btree.json</c> hosts a
    /// subtree, so none of them may grow a slot or move a golden byte.
    /// </summary>
    [Fact]
    public void E6_R5_ATreeThatHostsNothing_PlansNoSlot()
    {
        var b = new BTreeBuilder<byte, BTreeContext>()
            .Sequence(seq => seq.Action(HostFails));
        var blob = b.Compile("E6_NoHosting");

        var plan = BTreeHostedSites.PlanFor(blob, "E6_NoHosting");

        Assert.Empty(plan.Entries);
        Assert.Empty(plan.Slots);
    }

    // ── the fails-closed rule ─────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔ <b>An unbound hosting site is a HARD failure, never a quiet <c>Failure</c> status.</b>
    /// §19.6 ⑤: a host that silently does nothing reads as <i>"the subtree just fails"</i>, which is
    /// the exact silent miss this programme keeps paying for.
    /// </summary>
    [Fact]
    public void AnUnboundHostingSite_Throws_RatherThanFailingQuietly()
    {
        using var world = CreateWorld();
        var hostBlob = BuildHostBlob(out var hostRegistry);

        // ⚠ Deliberately NO PlanFor/Bind — the registrar forgot.
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        var host = new Interpreter<byte, BTreeContext>(hostBlob, hostRegistry)
        {
            SubtreeHost = OccurrenceSubtreeHost.Instance,
        };

        byte hostBb = 0;
        var  ctx    = new BTreeContext { Self = entity, World = world };
        var  state  = new BehaviorTreeState();

        var ex = Assert.Throws<InvalidOperationException>(
            () => host.Tick(ref hostBb, ref state, ref ctx));
        Assert.Contains("BTreeHostedSites.Bind", ex.Message);
    }

    /// <summary>
    /// ⭐⭐ <b>Hosting is OPT-IN per interpreter.</b> Without a <c>SubtreeHost</c> a Subtree node keeps
    /// the historical stub, so <c>CE-365</c> cannot have changed any existing tree's behaviour.
    /// </summary>
    [Fact]
    public void WithNoSubtreeHost_ASubtreeNodeStillFails_TheHistoricalBehaviour()
    {
        using var world = CreateWorld();
        var hostBlob = BuildHostBlob(out var hostRegistry);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        var host = new Interpreter<byte, BTreeContext>(hostBlob, hostRegistry);   // no SubtreeHost

        byte hostBb = 0;
        var  ctx    = new BTreeContext { Self = entity, World = world };
        var  state  = new BehaviorTreeState();

        Assert.Equal(NodeStatus.Failure, host.Tick(ref hostBb, ref state, ref ctx));
    }

    // ── E6_R7 — CE-377, the registration-ORDER hazard ─────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>E6_R7</c> — the host's registrar may run BEFORE the child's, and hosting must still
    /// work.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §33.11 ⑥.
    ///
    /// <para>🔴 <b>Why this rail exists, and it is not hypothetical.</b> <c>[BlueprintRegistrar]</c>
    /// methods are discovered by reflection and run in an ARBITRARY order. Until <c>CE-377</c>
    /// <c>HostedChildren.Register</c> resolved the child EAGERLY and simply returned when it was not
    /// registered yet ⇒ the bind was silently dropped and the node threw at TICK time. ⚠ The failure
    /// was order-dependent, so it would appear and disappear between builds — the worst shape there
    /// is.</para>
    ///
    /// <para>📐 <b>It stopped being theoretical when the editor-authored route was wired:</b> three
    /// SHIPPED assets host <c>SampleScout</c> — <c>BTreeRenderShowcase</c>, <c>CombatShowcase</c> and
    /// <c>Authoring/T07_Subtree</c> — and none of them controls when <c>SampleScoutRegistrar</c> runs.
    /// ⛔ This rail pins the PROPERTY (order-independence), not today's accidental order, which is the
    /// only form that cannot rot.</para>
    /// </summary>
    [Fact]
    public void E6_R7_TheHostMayBindBeforeTheChildIsRegistered()
    {
        var beh      = new BehaviorRegistry();
        var hostBlob = BuildHostBlob(out _);

        // ⛔ THE HOST FIRST, and the child NOT registered at all yet — the order an eager resolve lost.
        var plan = BTreeHostedSites.PlanFor(hostBlob, HostName);
        BTreeHostedSites.Bind(beh, hostBlob, plan);

        Assert.Single(plan.Entries);
        int key = plan.Entries[0].TreeStateSlotKey;

        // ⚠ Still unresolvable at this instant — and that must NOT be cached as a permanent miss.
        Assert.False(HostedChildren.TryGet(key, out _));

        // ⭐ NOW the child's registrar runs, exactly as a later [BlueprintRegistrar] would.
        var child = BuildChild();
        beh.Register(ChildName, new BehaviorDefinition
        {
            Name             = ChildName,
            BrainTier        = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = child,
        });

        Assert.True(HostedChildren.TryGet(key, out var bound));
        Assert.Same(child, bound);
        Assert.Same(child, HostedChildren.Require(key));
    }

    // ── E6_R8 — E6b, the EDITOR-SHAPED path: ingress + BrainTickSystem ────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>E6_R8</c> — THE EDITOR-PATH GATE. The host is ASSIGNED by the real
    /// <see cref="BehaviorIngressSystem"/> and ticked by the real <see cref="BrainTickSystem"/>, and
    /// the hosted child advances on every frame.</b> 📄 §33.12.4.
    ///
    /// <para>🔴🔴 <b>Why every earlier E6 rail is insufficient, stated exactly.</b> §33.12.2's module
    /// diagram shows THREE lifetimes feeding one tick: a BOOT-time node→key map
    /// (<see cref="BTreeHostedSites"/>, keyed by <c>StructureHash</c>), an ASSIGN-time slot on the
    /// entity (<c>ProvisionStatefulSlots</c> → <c>AttachManifestSlots</c>), and a PER-FRAME lookup.
    /// ⛔ <b>Nothing type-checks that the three agree</b> — the key computed at boot must equal the
    /// slot attached at assign must equal the key looked up at tick. ⚠ Every rail before this one
    /// hand-supplied at least two of the three, so a disagreement between them was unobservable.</para>
    ///
    /// <para>⭐ This is the twin of the HSM arm's money rail
    /// (<c>HsmOccurrenceKeyTests.E5_R3</c>), deliberately down to the three-frame shape: a hosted
    /// child that ticks ONCE and then stops is the failure that rail was written to catch.</para>
    ///
    /// <para>⚠ <b>Non-vacuity matters here</b>, so the rail asserts the slot was provisioned by the
    /// MANIFEST — nothing in this test attaches it by hand, unlike <c>E5_R3</c>, which had to.</para>
    /// </summary>
    [Fact]
    public void E6_R8_TheHostIsAssignedAndTickedByTheRealSystems()
    {
        const int HostId = 0x6B01;

        using var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterAll(world);

        var beh = new BehaviorRegistry();

        // ── BOOT: exactly what the GENERATED registrar emits (§33.12.3 steps 1-6) ──────
        var hostBlob = BuildHostBlob(out var hostRegistry);
        var plan     = BTreeHostedSites.PlanFor(hostBlob, HostName);
        Assert.Single(plan.Entries);
        Assert.Single(plan.Slots);

        var host = new Interpreter<byte, BTreeContext>(hostBlob, hostRegistry)
        {
            SubtreeHost = OccurrenceSubtreeHost.Instance,
        };
        beh.Register(HostId, HostName, new BehaviorDefinition
        {
            Name                 = HostName,
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter     = host,
            StatefulWorkingSlots = plan.Slots,        // ⭐ the manifest carries the hosted cursor
        });
        BTreeHostedSites.Bind(beh, hostBlob, plan);

        var child = BuildChild();
        beh.Register(ChildName, new BehaviorDefinition
        {
            Name             = ChildName,
            BrainTier        = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = child,
        });

        // ── ASSIGN: the real ingress provisions the tier AND attaches the hosted slot ──
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);

        var ingress = new BehaviorIngressSystem(beh);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = HostName, JsonParams = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        int key = plan.Entries[0].TreeStateSlotKey;

        // ⭐⭐ THE AGREEMENT ASSERTION: the slot the MANIFEST declared is the slot the BOOT-time key
        //    names. ⛔ Nothing here attached it by hand — if ingress used a different key, or sized
        //    the tier without counting the hosted slot, this is where it shows.
        byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        Assert.True(store != null, "ingress must have provisioned an occurrence store");
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out _),
            "the hosted cursor slot must be attached FROM THE MANIFEST, not by the test");

        // ── FRAMES: the real BrainTickSystem, three consecutive frames ─────────────────
        _childFirstLeafEntries = 0;
        _childTicks            = 0;

        var brain = new BrainTickSystem(beh);
        brain.Execute(world, 0.016f);
        int afterFirst = _childTicks;
        brain.Execute(world, 0.016f);
        brain.Execute(world, 0.016f);

        // ⭐⭐⭐ THE RAIL. 🔴 A host whose child never runs reads 0; one that runs once and stops
        //    reads 1 — the E5_R3 failure shape, here for the BTree arm.
        Assert.True(afterFirst >= 1, "the child must run on the first frame the host ticks");
        Assert.Equal(3, _childTicks);

        // ⭐ And it RESUMED rather than restarted: the running leaf is re-entered every frame while
        //   the first leaf is entered exactly once.
        Assert.Equal(1, _childFirstLeafEntries);

        // ⭐ The cursor is SLOT-RESIDENT — the child's state lives in the entity's store, not on a
        //   stack local the test owns.
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int off));
        Assert.True(Unsafe.AsRef<BehaviorTreeState>(store + off).RunningNodeIndex >= 0);
    }

    /// <summary>
    /// ⭐ <b>A child that never appears still fails CLOSED, and the message says WHICH child.</b>
    /// ⛔ Lazy resolution must not turn a real miss into a silent one — that would be the §19.6 ⑤
    /// failure the whole hosting design exists to avoid.
    /// </summary>
    [Fact]
    public void E6_R7b_AChildThatNeverRegisters_ThrowsAndNamesIt()
    {
        var beh      = new BehaviorRegistry();
        var hostBlob = BuildHostBlob(out _);

        var plan = BTreeHostedSites.PlanFor(hostBlob, HostName);
        BTreeHostedSites.Bind(beh, hostBlob, plan);

        var ex = Assert.Throws<InvalidOperationException>(
            () => HostedChildren.Require(plan.Entries[0].TreeStateSlotKey));
        Assert.Contains(ChildName, ex.Message);
    }
}
