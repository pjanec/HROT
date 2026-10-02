using System;
using System.Runtime.CompilerServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// ⭐⭐⭐ <b>S5 — THE HOSTING MATRIX: any tier hosts any tier.</b> 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> §4 (S5 split), §7
/// (<c>Demo_HostMatrix</c>).
///
/// <para>⭐ S5a: a child of ANY tier runs in its host's site slot <c>[brain][start][block]</c>, sized and started by the
/// CHILD's runner. These drive the real ingress (which provisions the slot from the manifest) and the real
/// <see cref="BrainTickSystem"/>; nothing attaches a slot by hand.</para>
/// </summary>
public sealed unsafe class HostingMatrixTests : IDisposable
{
    private const string HostName  = "S5_Host";
    private const string ChildName = "S5_Child";
    private const int HostId = 0x5A01;
    private static readonly Guid SiteId = new("05a00000-0000-0000-0000-0000000051e0");

    public void Dispose()
    {
        BTreeHostedSites.ClearForTests();
        HostedChildren.ClearForTests();
    }

    // ── the host: a BTree whose only leaf hosts the child (blocking: the child's status is the node's) ──

    private sealed class Run
    {
        public required EntityRepository World;
        public required Entity Entity;
        public required int SlotKey;
        public required BrainTickSystem Brain;

        public int FinishedCount()
        {
            int n = 0;
            foreach (var e in World.Bus.Read<BehaviorFinishedEvent>())
                if (e.Entity.Index == Entity.Index) n++;
            return n;
        }

        public void Frame() { Brain.Execute(World, 0.016f); World.Bus.SwapBuffers(); }

        public byte* SlotPayload(out int size)
        {
            byte* store = OccurrenceStoreAccess.TryGetStore(World, Entity, out _);
            Assert.True(store != null);
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotIndex(store, SlotKey, out int index),
                "the hosted slot must be provisioned from the manifest");
            ref var entry = ref BlueprintBlackboardPartitions.GetSlot(store, index);
            size = entry.PayloadSize;
            return store + entry.PayloadOffset;
        }
    }

    private static Run AssignHostWithChild(BehaviorDefinition child)
    {
        var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterAll(world);
        var beh = new BehaviorRegistry();

        var b = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Subtree(ChildName, visualId: SiteId));
        var hostBlob = b.Compile(HostName);
        var plan = BTreeHostedSites.PlanFor(hostBlob, HostName);
        beh.Register(HostId, HostName, new BehaviorDefinition
        {
            Name                 = HostName,
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter     = new Interpreter<byte, BTreeContext>(hostBlob, b.GetRegistry()) { SubtreeHost = OccurrenceSubtreeHost.Instance },
            StatefulWorkingSlots = plan.Slots,
        });
        BTreeHostedSites.Bind(beh, hostBlob, plan);
        beh.Register(ChildName, child);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = HostName, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(beh).Execute(world, 0.016f);

        return new Run { World = world, Entity = entity, SlotKey = plan.Entries[0].TreeStateSlotKey, Brain = new BrainTickSystem(beh) };
    }

    // ── a blueprint child: a counter in its Exec, Success on the third tick ───────────────────────────

    private static long _execAddress;
    private static uint _seenInstanceId;

    private static NodeStatus BlueprintChildTick(ref byte block, ref byte exec, EntityRepository world,
        IEntityCommandBuffer ecb, Entity self, float time, float deltaTime, uint instanceId)
    {
        _execAddress = (long)Unsafe.AsPointer(ref exec);
        _seenInstanceId = instanceId;
        ref int ticks = ref Unsafe.As<byte, int>(ref exec);
        ticks++;
        return ticks >= 3 ? NodeStatus.Success : NodeStatus.Running;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S5a — a BTree hosts a BLUEPRINT behaviour.</b> The child's brain state (its <c>Exec</c>) is the FIRST region of
    /// the host's site slot, it survives between frames (the counter reaches 3), the child runs under the host's
    /// <c>InstanceId</c> (U-10), and its Success is the subtree node's ⇒ the host finishes on that frame.
    /// <para>✅ Red-proof: make <c>HostedChildren</c> resolve only BTree children again and this throws at the first tick.</para>
    /// </summary>
    [Fact]
    public void S5a_ABTreeHostsABlueprintChild_InItsOwnBrainState_AndTheChildsFinishEndsTheNode()
    {
        _execAddress = 0; _seenInstanceId = 0;
        var run = AssignHostWithChild(new BehaviorDefinition
        {
            Name            = ChildName,
            BrainTier       = BehaviorConstants.BrainTierBlueprint,
            BlueprintTick   = BlueprintChildTick,
            BrainStateBytes = 16,
        });
        using var _ = run.World;

        byte* payload = run.SlotPayload(out int size);
        Assert.Equal(HostedSubtree.BlockOffsetFor(HostedChildren.RequireDefinition(run.SlotKey)), size);   // [16 brain][start] → 24, no block
        Assert.Equal(24, size);

        run.Frame();
        Assert.Equal((long)payload, _execAddress);                 // the Exec IS the slot's first region
        Assert.Equal(1, *(int*)payload);
        Assert.Equal(run.World.GetComponent<BehaviorState>(run.Entity).InstanceId, _seenInstanceId);
        Assert.Equal(0, run.FinishedCount());

        run.Frame();
        Assert.Equal(2, *(int*)payload);                           // resumed, not restarted
        Assert.Equal(0, run.FinishedCount());

        run.Frame();
        Assert.Equal(1, run.FinishedCount());                      // child Success ⇒ subtree Success ⇒ host finished
    }

    // ── HSM children ─────────────────────────────────────────────────────────────────────────────────

    private static HsmDefinitionBlob FinalAtStart(uint hash)
    {
        var states = new[] { new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, Flags = StateFlags.IsInitial | StateFlags.IsFinal } };
        return new HsmDefinitionBlob(new HsmDefinitionHeader { StructureHash = hash, StateCount = 1 }, states,
            Array.Empty<TransitionDef>(), Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(),
            Array.Empty<ushort>(), Array.Empty<ushort>());
    }

    private static HsmDefinitionBlob Quiescent(uint hash)
    {
        var states = new[] { new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, Flags = StateFlags.IsInitial } };
        return new HsmDefinitionBlob(new HsmDefinitionHeader { StructureHash = hash, StateCount = 1 }, states,
            Array.Empty<TransitionDef>(), Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(),
            Array.Empty<ushort>(), Array.Empty<ushort>());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S5a — a BTree hosts an HSM, and the machine runs IN THE SITE SLOT.</b> The child's runner STARTED the instance
    /// there (stamped its <c>MachineId</c>, without which the kernel skips it every tick) and the kernel stepped it.
    /// </summary>
    [Fact]
    public void S5a_ABTreeHostsAnHsmChild_TheMachineIsStartedAndSteppedInTheSiteSlot()
    {
        const uint Hash = 0x5A5A0001u;
        var blob = Quiescent(Hash);
        var run = AssignHostWithChild(new BehaviorDefinition
        {
            Name = ChildName, BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = blob,
        });
        using var _ = run.World;

        int instanceBytes = RootHsmAccess.InstanceBytes(blob);
        byte* payload = run.SlotPayload(out int size);
        Assert.Equal((instanceBytes + sizeof(int) + 7) & ~7, size);

        run.Frame();
        var header = (Fhsm.Kernel.Data.InstanceHeader*)payload;
        Assert.Equal(Hash, header->MachineId);
        Assert.NotEqual(Fhsm.Kernel.Data.InstancePhase.Entry, header->Phase);
        Assert.Equal(0, run.FinishedCount());                      // a quiescent machine keeps the node Running
    }

    /// <summary>
    /// ⭐⭐ <b>S5a / U-4 — an HSM child's <c>Terminated</c> is Success.</b> A machine whose initial state is final terminates on
    /// its first step, so the hosting node succeeds and the host finishes on the first frame.
    /// </summary>
    [Fact]
    public void S5a_AnHsmChildThatTerminates_SucceedsTheHostingNode()
    {
        var run = AssignHostWithChild(new BehaviorDefinition
        {
            Name = ChildName, BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = FinalAtStart(0x5A5A0002u),
        });
        using var _ = run.World;

        run.Frame();
        Assert.Equal(1, run.FinishedCount());
    }

    // ══ S5b — nesting: a hosted child that hosts ═════════════════════════════════════════════════

    private const string MidName   = "S5_Mid";
    private const string LeafName  = "S5_Leaf";
    private static readonly Guid SiteA   = new("05b00000-0000-0000-0000-00000000000a");
    private static readonly Guid SiteB   = new("05b00000-0000-0000-0000-00000000000b");
    private static readonly Guid SiteMid = new("05b00000-0000-0000-0000-0000000000c0");

    private static NodeStatus LeafCounts(ref byte block, ref byte exec, EntityRepository world,
        IEntityCommandBuffer ecb, Entity self, float time, float deltaTime, uint instanceId)
    {
        Unsafe.As<byte, int>(ref exec)++;
        return NodeStatus.Running;
    }

    /// <summary>
    /// Registers HOST → (sites) → MID (BTree, hosts LEAF at one site) → LEAF (blueprint, counts its ticks in its Exec),
    /// assigns the host through the real ingress, and returns the run plus the mid's template key at each host site and the
    /// leaf's template key inside the mid.
    /// </summary>
    private static (Run Run, int[] MidKeys, int LeafKey) AssignChain(Action<BTreeBuilder<byte, BTreeContext>> hostShape)
    {
        var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterAll(world);
        var beh = new BehaviorRegistry();

        var mb = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Subtree(LeafName, visualId: SiteMid));
        var midBlob = mb.Compile(MidName);
        var midPlan = BTreeHostedSites.PlanFor(midBlob, MidName);
        beh.Register(MidName, new BehaviorDefinition
        {
            Name = MidName, BrainTier = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = new Interpreter<byte, BTreeContext>(midBlob, mb.GetRegistry()) { SubtreeHost = OccurrenceSubtreeHost.Instance },
            StatefulWorkingSlots = midPlan.Slots,
        });
        BTreeHostedSites.Bind(beh, midBlob, midPlan);

        beh.Register(LeafName, new BehaviorDefinition
        {
            Name = LeafName, BrainTier = BehaviorConstants.BrainTierBlueprint, BlueprintTick = LeafCounts, BrainStateBytes = 16,
        });

        var hb = new BTreeBuilder<byte, BTreeContext>();
        hostShape(hb);
        var hostBlob = hb.Compile(HostName);
        var hostPlan = BTreeHostedSites.PlanFor(hostBlob, HostName);
        beh.Register(HostId, HostName, new BehaviorDefinition
        {
            Name = HostName, BrainTier = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = new Interpreter<byte, BTreeContext>(hostBlob, hb.GetRegistry()) { SubtreeHost = OccurrenceSubtreeHost.Instance },
            StatefulWorkingSlots = hostPlan.Slots,
        });
        BTreeHostedSites.Bind(beh, hostBlob, hostPlan);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = HostName, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(beh).Execute(world, 0.016f);

        var midKeys = new int[hostPlan.Entries.Count];
        for (int k = 0; k < midKeys.Length; k++) midKeys[k] = hostPlan.Entries[k].TreeStateSlotKey;
        var run = new Run { World = world, Entity = entity, SlotKey = midKeys[0], Brain = new BrainTickSystem(beh) };
        return (run, midKeys, midPlan.Entries[0].TreeStateSlotKey);
    }

    private static int LeafTicks(Run run, int midKey, int leafKey)
    {
        int key = OccurrenceSlots.HostedKeyAt(midKey, leafKey);
        byte* store = OccurrenceStoreAccess.TryGetStore(run.World, run.Entity, out _);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int off),
            $"the grandchild's nested slot {key} must be provisioned by ingress");
        return *(int*)(store + off);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S5b — the SAME child at two sites, each hosting a grandchild: two grandchild occurrences, never one.</b>
    /// Ingress provisions both nested slots (they cannot be attached mid-tick), and each grandchild ticks once per frame in
    /// its own brain state.
    /// <para>✅ Red-proof: make <c>OccurrenceSlots.HostedKeyAt</c> ignore its parent and the two grandchildren share one slot
    /// (or are never provisioned).</para>
    /// </summary>
    [Fact]
    public void S5b_TheSameChildAtTwoSites_GivesItsGrandchildTwoOccurrences()
    {
        var (run, midKeys, leafKey) = AssignChain(h => h.Parallel(0, p => p
            .Subtree(MidName, visualId: SiteA)
            .Subtree(MidName, visualId: SiteB)));
        using var _ = run.World;
        Assert.Equal(2, midKeys.Length);
        Assert.NotEqual(OccurrenceSlots.HostedKeyAt(midKeys[0], leafKey), OccurrenceSlots.HostedKeyAt(midKeys[1], leafKey));

        run.Frame(); run.Frame(); run.Frame();

        Assert.Equal(3, LeafTicks(run, midKeys[0], leafKey));
        Assert.Equal(3, LeafTicks(run, midKeys[1], leafKey));
    }

    /// <summary>
    /// ⭐⭐ <b>S5b — resetting a child resets ITS children too (I7).</b> The reset the kernel routes on a Subtree node's exit
    /// (and the HSM host on an inactive state) now walks down: the grandchild's brain state is cleared with its parent's,
    /// and the next entry starts the whole chain fresh.
    /// <para>⚠ Composed like <c>E6_R3b</c>: the kernel cannot abandon a still-Running child through a selector (it resumes
    /// into it), so the reset is called directly — <c>E6_R3a</c> proves the kernel calls it.</para>
    /// </summary>
    [Fact]
    public void S5b_ResettingAChild_ResetsItsGrandchildToo()
    {
        var (run, midKeys, leafKey) = AssignChain(h => h.Sequence(seq => seq.Subtree(MidName, visualId: SiteA)));
        using var _ = run.World;

        run.Frame(); run.Frame();
        Assert.Equal(2, LeafTicks(run, midKeys[0], leafKey));

        HostedSubtree.Reset(run.World, run.Entity, midKeys[0]);
        Assert.Equal(0, LeafTicks(run, midKeys[0], leafKey));

        run.Frame();
        Assert.Equal(1, LeafTicks(run, midKeys[0], leafKey));   // a fresh start, all the way down
    }


    // ══ S5b step 2 — a hosted child's OWN stateful node keeps one working state per site ══════════

    private const string StatefulName = "S5_Stateful";
    private const int StatefulSlot = 0x5B0057A7;

    /// <summary>Exactly the shape a generated stateful thunk has (BTreeBridgeEmitCore.AppendWorkingStateResolve).</summary>
    private static NodeStatus CountInWorkingState(ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int p)
    {
        if (!OccurrenceStoreAccess.TryResolveOccurrence(ctx.World, ctx.Self,
                OccurrenceSlots.HostedKeyAt(ctx.OccurrenceKey, StatefulSlot), out byte* ws))
            return NodeStatus.Failure;
        (*(int*)ws)++;
        return NodeStatus.Running;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S5b step 2 — the same STATEFUL child at two sites keeps TWO working states.</b> Its node slot is provisioned
    /// under each site's occurrence key, and the node resolves it through <c>ctx.OccurrenceKey</c>, so each site counts its
    /// own ticks. 🔴 Before: a hosted child's own stateful slots were never provisioned (the node returned Failure), and
    /// had they been, both sites would have shared one.
    /// <para>✅ Red-proof: resolve with the bare key (no <c>HostedKeyAt</c>) and the node finds no slot.</para>
    /// </summary>
    [Fact]
    public void S5b_AStatefulChildAtTwoSites_KeepsTwoWorkingStates()
    {
        using var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterAll(world);
        var beh = new BehaviorRegistry();

        var cb = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(CountInWorkingState));
        beh.Register(StatefulName, new BehaviorDefinition
        {
            Name = StatefulName, BrainTier = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = new Interpreter<byte, BTreeContext>(cb.Compile(StatefulName), cb.GetRegistry()),
            StatefulWorkingSlots = new[] { new StatefulSlotInfo(StatefulSlot, sizeof(int), 0x5B5Bu, typeof(int), "counter") },
        });

        var hb = new BTreeBuilder<byte, BTreeContext>().Parallel(0, p => p
            .Subtree(StatefulName, visualId: SiteA)
            .Subtree(StatefulName, visualId: SiteB));
        var hostBlob = hb.Compile(HostName);
        var plan = BTreeHostedSites.PlanFor(hostBlob, HostName);
        beh.Register(HostId, HostName, new BehaviorDefinition
        {
            Name = HostName, BrainTier = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = new Interpreter<byte, BTreeContext>(hostBlob, hb.GetRegistry()) { SubtreeHost = OccurrenceSubtreeHost.Instance },
            StatefulWorkingSlots = plan.Slots,
        });
        BTreeHostedSites.Bind(beh, hostBlob, plan);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = HostName, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(beh).Execute(world, 0.016f);

        var brain = new BrainTickSystem(beh);
        for (int f = 0; f < 3; f++) { brain.Execute(world, 0.016f); world.Bus.SwapBuffers(); }

        byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        foreach (var site in plan.Entries)
        {
            int key = OccurrenceSlots.HostedKeyAt(site.TreeStateSlotKey, StatefulSlot);
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int off), "the child's node slot must be provisioned per site");
            Assert.Equal(3, *(int*)(store + off));
        }
    }
}
