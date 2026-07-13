using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// S3-G (stage 2): proves the FDP-side stateful code-builder helper
/// (<see cref="StatefulBTreeActionBinder"/> + the authoring-side <c>StatefulAction</c> extension) binds a
/// four-parameter stateful method through FastBTree's generic <c>Action</c> seam, so the code
/// <c>[BTreeDefinition]</c> builder can author <c>ThreeParamReusableStateful</c> nodes.
///
/// <para>This is the code-built analogue of <c>S3_BehaviorScopedThunkTests.BehaviorScoped_TwoNodes_ShareOneSlot</c>
/// (which drives the JSON generator): two nodes binding one Behavior-scoped variable dispatch through one thunk
/// over one shared partition slot; a Node-scoped pair keeps independent slots.</para>
/// </summary>
public sealed unsafe class CodeBuiltStatefulActionTests
{
    // Asset id shared by both tests; slot keys differ by scope + variable, not by asset.
    private static readonly Guid AssetId = new("c0de0001-0000-0000-0000-000000000000");

    /// <summary>Code-builder blackboard: params inline at offset 0; working state lives in a partition slot.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorBlackboard
    {
        public DemoCounterNodes.DemoCursorParams Cfg;
    }

    private static EntityRepository CreateWorld()
    {
        var world = TestWorldFactory.Create();
        world.RegisterComponent<BlueprintBlackboard1024>();
        return world;
    }

    // Provisions every slot in the manifest into the entity's 1024 tier (mirrors BehaviorIngressSystem).
    private static void ProvisionSlots(EntityRepository world, Entity entity, StatefulSlotManifestBuilder manifest)
    {
        var slots = manifest.ToManifest();
        Assert.NotNull(slots);
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
            foreach (var s in slots!)
            {
                bool ok = BlueprintBlackboardPartitions.TryAttach(mem, s.SlotKey, s.PayloadSize, s.StructureHash, out _);
                Assert.True(ok, $"TryAttach must succeed for slotKey={s.SlotKey}");
            }
        }
    }

    private static int ReadCursor(EntityRepository world, Entity entity, int slotKey)
    {
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off),
                $"slot {slotKey} must exist");
            return Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + off).Cursor;
        }
    }

    // ── Test 1: Behavior scope — two code-built nodes share one slot ───────────────

    /// <summary>
    /// Two <c>StatefulAction</c> nodes bind ONE Behavior-scoped variable "shared" (Limit=1) via the builder.
    /// They register under one shared slot key and dispatch through one thunk over one slot. In a single tick
    /// the Sequence runs node A (0→1, Success at Limit=1) then node B (1→2, Success) ⇒ final Cursor=2, one slot.
    /// Independent slots would instead leave two slots at Cursor=1 each.
    /// </summary>
    [Fact]
    public void CodeBuilt_BehaviorScoped_TwoNodes_ShareOneSlot()
    {
        const string shared = "shared";
        var manifest = new StatefulSlotManifestBuilder(AssetId);

        var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
            .Sequence(seq => seq
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, DemoCounterNodes.Action_AdvanceCursor, manifest, shared,
                    StatefulSlotScope.Behavior, label: "A")
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, DemoCounterNodes.Action_AdvanceCursor, manifest, shared,
                    StatefulSlotScope.Behavior, label: "B"));

        var slots = manifest.ToManifest();
        Assert.NotNull(slots);
        Assert.Single(slots!);

        int behaviorKey = StatefulBTreeActionBinder.ComputeStatefulSlotKey(
            AssetId, StatefulSlotScope.Behavior, Guid.Empty, shared);
        Assert.Equal(behaviorKey, slots![0].SlotKey);
        Assert.Equal(1 /* State */, slots[0].Role);
        Assert.Equal((byte)StatefulSlotScope.Behavior, slots[0].Scope);

        var blob = builder.Compile("CodeBuiltShared");
        var interp = new Interpreter<CursorBlackboard, BTreeContext>(blob, builder.GetRegistry());

        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());
        ProvisionSlots(world, entity, manifest);
        Assert.Equal(1, SlotCount(world, entity));

        var bb = new CursorBlackboard { Cfg = { Limit = 1 } };
        var ctx = new BTreeContext { Self = entity, World = world };
        var state = new BehaviorTreeState();
        interp.Tick(ref bb, ref state, ref ctx);

        Assert.Equal(2, ReadCursor(world, entity, behaviorKey));

        world.Dispose();
    }

    // ── Test 2: Node scope — two code-built nodes keep independent slots ───────────

    /// <summary>
    /// Regression mirror: two <c>StatefulAction</c> nodes with <see cref="StatefulSlotScope.Node"/> and distinct
    /// visual ids resolve to distinct slot keys, so with Limit=1 each advances its OWN cursor to 1 (two slots),
    /// rather than one shared slot reaching 2.
    /// </summary>
    [Fact]
    public void CodeBuilt_NodeScoped_TwoNodes_IndependentSlots()
    {
        var nA = new Guid("c0de00a0-0000-0000-0000-00000000000a");
        var nB = new Guid("c0de00b0-0000-0000-0000-00000000000b");
        var manifest = new StatefulSlotManifestBuilder(AssetId);

        var builder = new BTreeBuilder<CursorBlackboard, BTreeContext>()
            .Sequence(seq => seq
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, DemoCounterNodes.Action_AdvanceCursor, manifest, "localA",
                    StatefulSlotScope.Node, visualId: nA, label: "A")
                .StatefulAction<CursorBlackboard, DemoCounterNodes.DemoCursorParams, DemoCounterNodes.DemoCursorState>(
                    bb => bb.Cfg, DemoCounterNodes.Action_AdvanceCursor, manifest, "localB",
                    StatefulSlotScope.Node, visualId: nB, label: "B"));

        var slots = manifest.ToManifest();
        Assert.NotNull(slots);
        Assert.Equal(2, slots!.Count);

        int keyA = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetId, StatefulSlotScope.Node, nA, "localA");
        int keyB = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetId, StatefulSlotScope.Node, nB, "localB");
        Assert.NotEqual(keyA, keyB);

        var blob = builder.Compile("CodeBuiltIndependent");
        var interp = new Interpreter<CursorBlackboard, BTreeContext>(blob, builder.GetRegistry());

        var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());
        ProvisionSlots(world, entity, manifest);
        Assert.Equal(2, SlotCount(world, entity));

        var bb = new CursorBlackboard { Cfg = { Limit = 1 } };
        var ctx = new BTreeContext { Self = entity, World = world };
        var state = new BehaviorTreeState();
        interp.Tick(ref bb, ref state, ref ctx);

        Assert.Equal(1, ReadCursor(world, entity, keyA));
        Assert.Equal(1, ReadCursor(world, entity, keyB));

        world.Dispose();
    }

    private static int SlotCount(EntityRepository world, Entity entity)
    {
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
            return BlueprintBlackboardPartitions.GetSlotCount(mem);
    }
}
