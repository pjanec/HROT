using System;
using System.Collections.Generic;
using Fbt;
using Fbt.Kernel;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// ⭐⭐⭐ <c>CE-3137</c> U-1 — rails for <see cref="UnitMemory"/> (<c>Q87</c> §5, <c>R-237</c>): a unit-scoped shared struct, one
/// type-keyed slot of kind <see cref="OccurrenceKind.UnitMemory"/>, created on first touch, never swept by a switch.
/// </summary>
public sealed unsafe class UnitMemoryTests
{
    [UnitMemory]
    private struct Burned
    {
        public int Uses = 3;
        public float Heat = 1.5f;
        public Burned() { }
    }

    [UnitMemory]
    private struct Big
    {
        public long A, B, C, D, E, F, G, H;   // 64 B
        public Big() { }
    }

    private static EntityRepository CreateWorld()
    {
        var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterAll(world);
        return world;
    }

    private static BehaviorDefinition Definition(string name, IReadOnlyList<StatefulSlotInfo> slots)
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName    = name,
            Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
            MethodNames = new[] { "noop" },
            FloatParams = Array.Empty<float>(),
            IntParams   = Array.Empty<int>(),
        };
        return new BehaviorDefinition
        {
            Name                 = name,
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter     = new Interpreter<byte, BTreeContext>(blob, new ActionRegistry<byte, BTreeContext>()),
            StatefulWorkingSlots = slots,
        };
    }

    private static void Assign(EntityRepository world, BehaviorIngressSystem sys, Entity e, string name)
    {
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = name, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>U1_R1</c> — THE PROPERTY: a value written under behaviour A is read under B after a SWITCH. 🔴 Red-proof: the
    /// retired Entity-scoped slot was a manifest key and <c>DetachStatefulSlots</c> freed it on every switch
    /// (<c>7d15977d8^:BehaviorIngressSystem.cs:965</c>); a kind-5 slot is outside both sweeps.
    /// </summary>
    [Fact]
    public void U1_R1_AValueWrittenUnderA_IsReadUnderB_AfterASwitch()
    {
        using var world = CreateWorld();
        var registry = new BehaviorRegistry();
        registry.Register(9101, "UmA", Definition("UmA", new[] { new StatefulSlotInfo(0x0A0A0A01, 8, 0u) }));
        registry.Register(9102, "UmB", Definition("UmB", new[] { new StatefulSlotInfo(0x0B0B0B01, 8, 0u) }));
        var sys = new BehaviorIngressSystem(registry);

        var unit = world.CreateEntity();
        world.AddComponent(unit, new BehaviorState());
        Assign(world, sys, unit, "UmA");
        Assert.True(OccurrenceStoreAccess.TryFindSlot(world, unit, 0x0A0A0A01, out _, out _, out _), "premise: A's manifest slot");

        UnitMemory.Ref<Burned>(world, unit).Uses = 42;

        Assign(world, sys, unit, "UmB");
        Assert.False(OccurrenceStoreAccess.TryFindSlot(world, unit, 0x0A0A0A01, out _, out _, out _), "premise: the switch swept A's slot");

        Assert.True(UnitMemory.Has<Burned>(world, unit));
        Assert.Equal(42, UnitMemory.Get<Burned>(world, unit).Uses);
    }

    /// <summary>⭐⭐ <c>U1_R2</c> — a fresh unit reads the DECLARED defaults, through <c>Ref</c> and through <c>Get</c> — not zeros.</summary>
    [Fact]
    public void U1_R2_AFreshUnitReadsTheDeclaredDefaults()
    {
        using var world = CreateWorld();
        var unit = world.CreateEntity();
        Assert.Equal(3, UnitMemory.Get<Burned>(world, unit).Uses);
        ref var m = ref UnitMemory.Ref<Burned>(world, unit);
        Assert.Equal(3, m.Uses);
        Assert.Equal(1.5f, m.Heat);
    }

    /// <summary>⭐ <c>U1_R3</c> — a <c>Get</c> of a type nobody wrote creates NOTHING (Q87 E: a read costs no room).</summary>
    [Fact]
    public void U1_R3_GetOnAnAbsentType_CreatesNothing()
    {
        using var world = CreateWorld();
        var unit = world.CreateEntity();
        OccurrenceStoreAccess.AddBlock(world, unit, BlueprintTierTable.Ascending[0]);
        int before = OccurrenceStoreAccess.Measure(world, unit).SlotCount;

        _ = UnitMemory.Get<Burned>(world, unit);

        Assert.Equal(before, OccurrenceStoreAccess.Measure(world, unit).SlotCount);
        Assert.False(UnitMemory.Has<Burned>(world, unit));
    }

    /// <summary>
    /// ⭐⭐ <c>U1_R4</c> — on a FULL store the first write appends a block, mid-tick, and reads back in the same tick; a ref taken
    /// before the append still points at the live value (U-0, nothing moves).
    /// </summary>
    [Fact]
    public void U1_R4_OnAFullStore_TheFirstWriteAppendsABlock_AndReadsBack()
    {
        using var world = CreateWorld();
        var unit = world.CreateEntity();
        var small = BlueprintTierTable.Ascending[0];
        OccurrenceStoreAccess.AddBlock(world, unit, small);
        ref var first = ref UnitMemory.Ref<Burned>(world, unit);
        first.Uses = 7;
        // Fill the ONE block exactly — every free slot entry, or until the payload cannot take another 8-byte slot.
        for (int k = 0; ; k++)
        {
            var m = OccurrenceStoreAccess.Measure(world, unit);
            if (m.MaxSlots - m.SlotCount == 0 || m.PayloadFree < BlueprintBlackboardPartitions.PayloadCost(8)) break;
            Assert.True(OccurrenceStoreAccess.TryAttachSlot(world, unit, 0x4000 + k, 8, 1, OccurrenceKind.BTree, out _, out _));
        }
        Assert.Equal(1, OccurrenceStoreAccess.Measure(world, unit).Blocks);   // premise: still one block, and it is full

        using (OccurrenceStoreAccess.BeginTickView(world, unit))
        {
            UnitMemory.Ref<Big>(world, unit).H = 99;
            Assert.Equal(99, UnitMemory.Get<Big>(world, unit).H);
        }

        Assert.Equal(2, OccurrenceStoreAccess.Measure(world, unit).Blocks);   // ⭐ the first write APPENDED a block
        Assert.Equal(99, UnitMemory.Get<Big>(world, unit).H);
        Assert.Equal(7, first.Uses);
        first.Uses = 8;
        Assert.Equal(8, UnitMemory.Get<Burned>(world, unit).Uses);
    }

    /// <summary>⭐ <c>U1_R5</c> — a changed LAYOUT under the same type key re-initialises to the declared defaults (Q87 F); <c>Get</c> reads defaults and leaves it.</summary>
    [Fact]
    public void U1_R5_AChangedLayout_ReinitialisesToTheDefaults()
    {
        using var world = CreateWorld();
        var unit = world.CreateEntity();
        int key = UnitMemory.Key<Burned>();
        Assert.True(OccurrenceStoreAccess.TryAttachSlot(world, unit, key, 8, 0xDEADu, OccurrenceKind.UnitMemory, out byte* b, out int o));
        *(int*)(b + o) = 1234;   // "old layout" bytes

        Assert.Equal(3, UnitMemory.Get<Burned>(world, unit).Uses);
        Assert.False(UnitMemory.Has<Burned>(world, unit));
        Assert.Equal(3, UnitMemory.Ref<Burned>(world, unit).Uses);
        Assert.True(UnitMemory.Has<Burned>(world, unit));
    }

    /// <summary>⭐ <c>U1_R6</c> — a slot of ANOTHER kind under the type key is a collision and fails LOUD.</summary>
    [Fact]
    public void U1_R6_AnotherKindUnderTheKey_Throws()
    {
        using var world = CreateWorld();
        var unit = world.CreateEntity();
        Assert.True(OccurrenceStoreAccess.TryAttachSlot(world, unit, UnitMemory.Key<Burned>(), 8, UnitMemory.StructureHash<Burned>(),
            OccurrenceKind.BTree, out _, out _));
        Assert.Throws<InvalidOperationException>(() => UnitMemory.Ref<Burned>(world, unit));
        Assert.Throws<InvalidOperationException>(() => UnitMemory.Get<Burned>(world, unit));
    }

    /// <summary>⭐ <c>U1_R7</c> — with every block of the ladder full, a first touch fails LOUD — never a silent drop.</summary>
    [Fact]
    public void U1_R7_TheWholeLadderFull_Throws()
    {
        using var world = CreateWorld();
        var unit = world.CreateEntity();
        for (int k = 0; OccurrenceStoreAccess.TryAttachSlot(world, unit, 0x5000 + k, 8, 1, OccurrenceKind.BTree, out _, out _); k++) { }
        Assert.Throws<InvalidOperationException>(() => UnitMemory.Ref<Burned>(world, unit));
    }
}
