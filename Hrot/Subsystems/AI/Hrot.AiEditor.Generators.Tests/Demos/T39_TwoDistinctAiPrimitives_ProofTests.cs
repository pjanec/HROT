using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// BP-41 — <b>three different AiPrimitives on one entity</b>, each keeping its own partition slot.
///
/// <para>
/// This case was previously covered only by analogy. <c>T20</c> places two hardcoded stateful actions
/// of the <em>same</em> type; <c>T35</c> places the <em>same</em> <see cref="DemoAiPrimitiveNodes"/>
/// three times; <c>T31</c>/<c>T33</c>/<c>T34</c> each place exactly one. None of them varies the
/// <c>WorkingState</c> <em>type</em> between placements — so nothing proved that per-slot <b>sizing</b>
/// is driven by each placement's own struct rather than by whichever one happened to be provisioned
/// first.
/// </para>
///
/// <para>The host tree <c>T39_TwoDistinctPrimitives</c> is a <c>Parallel(RequireAll)</c> of three
/// composed AiPrimitive actions with three deliberately different layouts:</para>
///
/// <list type="table">
///   <item><term>A — <see cref="DemoAiPrimitiveNodes"/></term>
///         <description>Params 4 B, WorkingState 4 B (<c>int Ticks</c>); +1 per tick.</description></item>
///   <item><term>B — <see cref="DemoAiPrimitiveNodesB"/></term>
///         <description>Params 8 B, WorkingState 16 B (<c>long Accumulator; int Steps</c>);
///         +<c>Stride</c> (=7) per tick.</description></item>
///   <item><term>C — <c>ParamDemo_CEFE162F_Bp</c></term>
///         <description>a <b>real</b> blueprint-generated primitive (from <c>ParamDemo.bp.json</c>);
///         Params 8 B, <b>empty</b> WorkingState — the zero-size edge.</description></item>
/// </list>
///
/// <para>
/// A/B carry <c>RunsNeeded = 100</c> so neither reaches Success and <c>Parallel(RequireAll)</c>
/// re-executes both every tick. After N ticks A's slot reads <c>Ticks = N</c> while B's reads
/// <c>Accumulator = 7N, Steps = N</c> — arithmetically distinct, so any cross-talk surfaces as a wrong
/// number rather than as a coincidence.
/// </para>
/// </summary>
public sealed class T39_TwoDistinctAiPrimitives_ProofTests : IDisposable
{
    private const string BehaviorName = "T39_TwoDistinctPrimitives";

    private static readonly Guid AssetId = Guid.Parse("bb000039-0000-0000-0000-000000000000");
    private static readonly Guid NodeAVisualId = Guid.Parse("bb390000-0000-0000-0000-000000000003");
    private static readonly Guid NodeBVisualId = Guid.Parse("bb390000-0000-0000-0000-000000000004");
    private static readonly Guid NodeCVisualId = Guid.Parse("bb390000-0000-0000-0000-000000000005");

    // Node-scoped slot keys: FNV-1a-32(assetId ++ nodeVisualId). No WorkingStateTargetField is
    // authored on any of the three, so each node keys off its own VisualId.
    private static readonly int SlotKeyA = BTreeBridgeEmitCore.ComputeStatefulSlotKey(AssetId, NodeAVisualId);
    private static readonly int SlotKeyB = BTreeBridgeEmitCore.ComputeStatefulSlotKey(AssetId, NodeBVisualId);
    private static readonly int SlotKeyC = BTreeBridgeEmitCore.ComputeStatefulSlotKey(AssetId, NodeCVisualId);

    /// <summary>Stride authored on B in the .btree.json — B accumulates this much per tick.</summary>
    private const int StrideB = 7;

    private readonly BehaviorRegistry  _registry  = new();
    private readonly BlueprintRegistry _blueprint = new();

    public void Dispose() => _registry.Clear();

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<BrainBTreeState>();
        world.RegisterComponent<LocomotionChannel>();
        world.RegisterComponent<BlueprintBlackboard1024>();
        world.RegisterComponent<BlueprintBlackboard4096>();
        world.RegisterComponent<BlueprintBlackboard16384>();
        return world;
    }

    private BehaviorDefinition RegisterAndGetDefinition()
    {
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _blueprint.CommitStaging(staging);

        _registry.TryGetId(BehaviorName, out int id)
            .Should().BeTrue($"{BehaviorName} must self-register via its generated [BlueprintRegistrar]");
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        return def!;
    }

    // ── 1. Distinct keys ──────────────────────────────────────────────────────────

    [Fact]
    public void ThreeDistinctPrimitives_GetThreeDistinctSlotKeys()
    {
        // Sanity: without this, every "they stay separate" assertion below would be vacuous.
        new[] { SlotKeyA, SlotKeyB, SlotKeyC }.Distinct().Should().HaveCount(3,
            "each Node-scoped placement keys off its own VisualId, so no two may collide");
    }

    // ── 2. Per-placement sizing — the gap T20/T35 could not cover ─────────────────

    /// <summary>
    /// The manifest must size every slot from <b>its own</b> WorkingState type. T20 and T35 place a
    /// single WorkingState type, so a provisioner that sized all slots from the first placement would
    /// pass both; here it would under-allocate B (16 B) to A's 4 B and corrupt the neighbouring slot.
    /// </summary>
    [Fact]
    public void ManifestSizesEverySlot_FromItsOwnWorkingStateType()
    {
        var def = RegisterAndGetDefinition();

        def.StatefulWorkingSlots.Should().NotBeNull(
            $"{BehaviorName} composes three stateful AiPrimitives");
        def.StatefulWorkingSlots!.Should().HaveCount(3,
            "one manifest entry per composed placement — A, B and C");

        var byKey = def.StatefulWorkingSlots!.ToDictionary(s => s.SlotKey);
        byKey.Keys.Should().BeEquivalentTo(new[] { SlotKeyA, SlotKeyB, SlotKeyC });

        byKey[SlotKeyA].WorkingStateType.Should().Be<DemoAiPrimitiveNodes.WorkingState>();
        byKey[SlotKeyB].WorkingStateType.Should().Be<DemoAiPrimitiveNodesB.WorkingState>();
        byKey[SlotKeyC].WorkingStateType.Should().Be<Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp.WorkingState>();

        // The sizes are what actually differ, and the manifest is emitted as Marshal.SizeOf<T>().
        byKey[SlotKeyA].PayloadSize.Should().Be(Marshal.SizeOf<DemoAiPrimitiveNodes.WorkingState>())
            .And.Be(4, "A's WorkingState is a single int");
        byKey[SlotKeyB].PayloadSize.Should().Be(Marshal.SizeOf<DemoAiPrimitiveNodesB.WorkingState>())
            .And.Be(16, "B's WorkingState is long + int, padded to 8-byte alignment");
        byKey[SlotKeyC].PayloadSize.Should().Be(
            Marshal.SizeOf<Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp.WorkingState>(),
            "C's WorkingState is empty — the zero-size edge, which Marshal.SizeOf reports as 1");

        // Structure hashes fold the size in, so two placements of different types cannot alias.
        def.StatefulWorkingSlots!.Select(s => s.StructureHash).Distinct().Should().HaveCount(3,
            "each placement's StructureHash must differ, or a stale slot could be adopted by the wrong type");
    }

    // ── 3. Provisioned slots do not overlap ──────────────────────────────────────

    /// <summary>
    /// Structural proof of isolation: the three provisioned payload ranges must be pairwise disjoint.
    /// Checked against each slot's <em>allocated</em> extent (size rounded up to the allocator's
    /// 8-byte alignment), so an allocator that handed out overlapping ranges fails here even if the
    /// value assertions below happened to survive.
    /// </summary>
    [Fact]
    public unsafe void ProvisionedPayloadRanges_ArePairwiseDisjoint()
    {
        var def    = RegisterAndGetDefinition();
        var world  = CreateWorld();
        var entity = CreateAssignedEntity(world);

        var byKey = def.StatefulWorkingSlots!.ToDictionary(s => s.SlotKey);

        (int Start, int End) Extent(int slotKey)
        {
            int offset = SlotOffset(world, entity, slotKey);
            int size   = AlignUp(byKey[slotKey].PayloadSize, BlueprintBlackboardPartitions.Alignment);
            return (offset, offset + size);
        }

        var a = Extent(SlotKeyA);
        var b = Extent(SlotKeyB);
        var c = Extent(SlotKeyC);

        Disjoint(a, b).Should().BeTrue($"A {a} and B {b} must not overlap");
        Disjoint(a, c).Should().BeTrue($"A {a} and C {c} must not overlap");
        Disjoint(b, c).Should().BeTrue($"B {b} and C {c} must not overlap");

        static bool Disjoint((int Start, int End) x, (int Start, int End) y)
            => x.End <= y.Start || y.End <= x.Start;

        static int AlignUp(int value, int alignment)
            => (value + alignment - 1) / alignment * alignment;
    }

    // ── 4. Runtime: each primitive accumulates only its own state ────────────────

    [Fact]
    public unsafe void ThreeDifferentPrimitives_OnOneEntity_KeepIndependentWorkingState()
    {
        RegisterAndGetDefinition();

        var world  = CreateWorld();
        var entity = CreateAssignedEntity(world);
        var interpreter = InterpreterFor();

        var ctx = new BTreeContext { Self = entity, World = world };
        NodeStatus Tick()
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            var state  = new BehaviorTreeState();
            return interpreter.Tick(ref bb, ref state, ref ctx);
        }

        for (int tick = 1; tick <= 3; tick++)
        {
            Tick().Should().Be(NodeStatus.Running,
                $"tick {tick} of 3 — RunsNeeded=100 keeps A and B Running, so the Parallel stays Running");

            ReadSlot<DemoAiPrimitiveNodes.WorkingState>(world, entity, SlotKeyA).Ticks
                .Should().Be(tick,
                    $"tick {tick}: A increments its own 4-byte slot once per tick");

            var stateB = ReadSlot<DemoAiPrimitiveNodesB.WorkingState>(world, entity, SlotKeyB);
            stateB.Steps.Should().Be(tick,
                $"tick {tick}: B increments its own 16-byte slot once per tick");
            stateB.Accumulator.Should().Be(StrideB * tick,
                $"tick {tick}: B accumulates Stride={StrideB} per tick — a value A's counter can never " +
                "produce, so a shared slot would show up as a wrong number here");
        }
    }

    /// <summary>
    /// The real blueprint-generated primitive dispatches too, alongside the two stand-ins. Its own
    /// TickCore writes <c>LocomotionChannel.ActiveAction = 99</c> and bumps <c>ActionInstanceId</c> —
    /// the only externally visible effect it has, since its WorkingState is empty.
    /// </summary>
    [Fact]
    public unsafe void RealGeneratedBlueprint_DispatchesAlongsideTheStandIns()
    {
        RegisterAndGetDefinition();

        var world  = CreateWorld();
        var entity = CreateAssignedEntity(world);
        world.AddComponent(entity, new LocomotionChannel());
        var interpreter = InterpreterFor();

        var ctx = new BTreeContext { Self = entity, World = world };
        ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
        var state  = new BehaviorTreeState();
        interpreter.Tick(ref bb, ref state, ref ctx);

        ref readonly var channel = ref world.GetComponentRO<LocomotionChannel>(entity);
        channel.ActiveAction.Should().Be(99,
            "ParamDemo's generated TickCore writes DemoEnumAction (99) to the locomotion channel");
        channel.ActionInstanceId.Should().BeGreaterThan(0,
            "the composed real blueprint must actually run, not just be provisioned a slot");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private Interpreter<BrainBlackboard, BTreeContext> InterpreterFor()
    {
        _registry.TryGetId(BehaviorName, out int id).Should().BeTrue();
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        def!.BTreeInterpreter.Should().NotBeNull($"{BehaviorName} is a BTree behavior");
        return def.BTreeInterpreter!;
    }

    /// <summary>
    /// Creates an entity and runs the real <see cref="BehaviorIngressSystem"/> over an
    /// <see cref="AssignBehaviorEvent"/>, which parses the authored Params defaults and provisions all
    /// three partition slots — the same path the game takes.
    /// </summary>
    private Entity CreateAssignedEntity(EntityRepository world)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        var ingress = new BehaviorIngressSystem(_registry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = BehaviorName,
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        (world.HasComponent<BlueprintBlackboard1024>(entity)
         || world.HasComponent<BlueprintBlackboard4096>(entity)
         || world.HasComponent<BlueprintBlackboard16384>(entity))
            .Should().BeTrue("BehaviorIngressSystem must provision a partition tier for the stateful slots");

        return entity;
    }

    private unsafe delegate TResult TierReader<out TResult>(byte* memory);

    /// <summary>Runs <paramref name="read"/> against whichever BlueprintBlackboard tier was provisioned.</summary>
    private static unsafe TResult WithTierMemory<TResult>(
        EntityRepository world, Entity entity, TierReader<TResult> read)
    {
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = tier.Memory) return read(mem);
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory) return read(mem);
        }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory) return read(mem);
        }
        throw new InvalidOperationException("entity has no BlueprintBlackboard* tier — slots cannot be read");
    }

    private static unsafe int SlotOffset(EntityRepository world, Entity entity, int slotKey)
        => WithTierMemory(world, entity, mem =>
        {
            BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int offset)
                .Should().BeTrue($"slot {slotKey} must have been provisioned");
            return offset;
        });

    private static unsafe T ReadSlot<T>(EntityRepository world, Entity entity, int slotKey)
        where T : unmanaged
        => WithTierMemory(world, entity, mem =>
        {
            BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int offset)
                .Should().BeTrue($"slot {slotKey} must have been provisioned");
            return Unsafe.AsRef<T>(mem + offset);
        });
}
