using System;
using System.Collections.Generic;
using Fbt;
using Fbt.Kernel;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Core.Jobs;
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
[Xunit.Collection("FdpWorkerPool")]
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

    // ── ⭐⭐ CE-9013 (DESIGN_Occurrence_Scoped_Storage §34.7) — the store GROWS inside the parallel brain phase: a pending block ──────────────
    //   📐 Before: the append was deferred into the batch's command buffer and AddBlock read the block back at once ("Entity … missing
    //   BlueprintBlackboard4096"; uc-urban-battle / ua-rpg-window exit 134). Now the batch hands out the block, the unit's view holds it, and
    //   the merge adds it as the component, first in its batch.

    [UnitMemory] private struct Mem600 { public fixed byte B[600]; }
    [UnitMemory] private struct Mem3K { public fixed byte B[3000]; }
    [UnitMemory] private struct Mem10K { public fixed byte B[10000]; }
    [UnitMemory] private struct Mem300A { public fixed byte B[300]; }
    [UnitMemory] private struct Mem300B { public fixed byte B[300]; }

    private static readonly int[] s_seen = new int[8];

    /// <summary>One unit tick in the phase, as BrainTickSystem.TickInPhase runs it: the unit's view open, then the unit memories touched.</summary>
    private struct GrowJob : IEntityBatchJob
    {
        public EntityRepository Repo;
        public int Steps;   // bit 0: Mem600, bit 1: Mem3K, bit 2: Mem10K, bit 3: Mem300A then Mem300B (both into ONE new block)
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            using var view = OccurrenceStoreAccess.BeginTickView(Repo, e);
            byte tag = (byte)(1 + e.Index % 200);
            if ((Steps & 8) != 0) { UnitMemory.Ref<Mem300A>(Repo, e).B[299] = tag; UnitMemory.Ref<Mem300B>(Repo, e).B[299] = tag; }
            if ((Steps & 1) != 0) { ref var m = ref UnitMemory.Ref<Mem600>(Repo, e); m.B[0] = tag; m.B[599] = tag; }
            if ((Steps & 2) != 0) { ref var m = ref UnitMemory.Ref<Mem3K>(Repo, e); m.B[0] = tag; m.B[2999] = tag; }
            if ((Steps & 4) != 0) { ref var m = ref UnitMemory.Ref<Mem10K>(Repo, e); m.B[0] = tag; m.B[9999] = tag; }
            // read back INSIDE the same tick, through a second lookup — the pending block answers it
            if ((Steps & 4) != 0) s_seen[0] = UnitMemory.Ref<Mem10K>(Repo, e).B[9999];
            s_seen[1] = ctx.OwnRawCount;
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    private static Entity BrainUnitWith(EntityRepository world, int smallestTiers)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        for (int i = 0; i < smallestTiers; i++) OccurrenceStoreAccess.AddBlock(world, e, BlueprintTierTable.Ascending[i]);
        return e;
    }

    private static int TierBits(EntityRepository world, Entity e)
    {
        int bits = 0;
        for (int i = 0; i < BlueprintTierTable.Ascending.Count; i++) if (BlueprintTierTable.Ascending[i].Has(world, e)) bits |= 1 << i;
        return bits;
    }

    private static void InPhase(Action body)
    {
        bool was = ParallelEntities.Enabled;
        ParallelEntities.Enabled = true;
        try { body(); } finally { ParallelEntities.Enabled = was; }
    }

    /// <summary>
    /// ⭐⭐ CE-9013 — EVERY growth step inside the phase: 256 → 1024 (a 600 B memory), 256+1024 → 4096 (3 KB), 256+1024+4096 → 16384 (10 KB). The
    /// new tier is NOT a component during the tick (it is the batch's pending block) and IS one after the merge, with the bytes the tick wrote.
    /// 🔴 Red-proof: AddBlock adding the component as before (the deferred add) ⇒ "missing BlueprintBlackboard…" inside the job.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 0b0011)]   // 256 → +1024
    [InlineData(2, 2, 0b0111)]   // 256 + 1024 → +4096
    [InlineData(3, 4, 0b1111)]   // 256 + 1024 + 4096 → +16384
    public void CE9013_R1_EachGrowthStep_AppendsInsideThePhase_AndLandsAtTheMerge(int startTiers, int step, int expectedBits)
    {
        InPhase(() =>
        {
            using var world = CreateWorld();
            var e = BrainUnitWith(world, startTiers);
            ParallelEntities.Run(world, new[] { e }, 1, new GrowJob { Repo = world, Steps = step });
            Assert.Equal(1, s_seen[1]);                                  // one pending block during the tick
            Assert.Equal(expectedBits, TierBits(world, e));             // the merge added it as the component
            byte tag = (byte)(1 + e.Index % 200);
            if (step == 1) { Assert.Equal(tag, UnitMemory.Ref<Mem600>(world, e).B[0]); Assert.Equal(tag, UnitMemory.Ref<Mem600>(world, e).B[599]); }
            if (step == 2) { Assert.Equal(tag, UnitMemory.Ref<Mem3K>(world, e).B[0]); Assert.Equal(tag, UnitMemory.Ref<Mem3K>(world, e).B[2999]); }
            if (step == 4) { Assert.Equal(tag, UnitMemory.Ref<Mem10K>(world, e).B[0]); Assert.Equal(tag, UnitMemory.Ref<Mem10K>(world, e).B[9999]); }
            Assert.Equal(startTiers + 1, OccurrenceStoreAccess.Measure(world, e).Blocks);
        });
    }

    /// <summary>
    /// ⭐⭐ CE-9013 — a CHAIN in one tick: a 256-only unit takes two 300 B memories (the second must land in the 1024 the FIRST appended), then
    /// a 3 KB and a 10 KB one ⇒ three pending blocks (1024, 4096, 16384), no tier appended twice, and a later lookup in the same tick reads the
    /// 16 KB one. 🔴 Red-proof (measured): TryAttachSlot walking the component mask instead of the view ⇒ the pending 1024 is invisible to the
    /// second 300 B attach, which appends the 1024 again (AllocateOwnRaw throws).
    /// </summary>
    [Fact]
    public void CE9013_R2_AChainOfGrowthsInOneTick_EachFindsThePendingBlocksBeforeIt()
    {
        InPhase(() =>
        {
            using var world = CreateWorld();
            var e = BrainUnitWith(world, 1);
            ParallelEntities.Run(world, new[] { e }, 1, new GrowJob { Repo = world, Steps = 15 });
            byte tag = (byte)(1 + e.Index % 200);
            Assert.Equal(3, s_seen[1]);
            Assert.Equal(tag, s_seen[0]);
            Assert.Equal(0b1111, TierBits(world, e));
            Assert.Equal(tag, UnitMemory.Ref<Mem300A>(world, e).B[299]);
            Assert.Equal(tag, UnitMemory.Ref<Mem300B>(world, e).B[299]);
            Assert.Equal(tag, UnitMemory.Ref<Mem600>(world, e).B[599]);
            Assert.Equal(tag, UnitMemory.Ref<Mem3K>(world, e).B[2999]);
            Assert.Equal(tag, UnitMemory.Ref<Mem10K>(world, e).B[9999]);
        });
    }

    /// <summary>
    /// ⭐ CE-9013 — many units across many batches each grow their OWN store; every one lands with its own bytes, none crosses to another.
    /// And growth is LAZY again (R-237 C): a unit that needs no more room keeps its one block — the R-3103 ladder is gone from the pre-pass.
    /// </summary>
    [Fact]
    public void CE9013_R3_ManyUnitsAcrossBatches_EachGrowsItsOwnStore_AndAUnitThatNeedsNothingKeepsOneBlock()
    {
        InPhase(() =>
        {
            using var world = CreateWorld();
            var units = new Entity[100];
            for (int i = 0; i < units.Length; i++) units[i] = BrainUnitWith(world, 1);
            var idle = BrainUnitWith(world, 1);
            Fdp.Toolkit.Behavior.BrainPhase.Prepare(world);
            Assert.Equal(0b0001, TierBits(world, idle));                 // 🔴 red with the eager ladder (256 + 1024 + 4096)
            ParallelEntities.Run(world, units, units.Length, new GrowJob { Repo = world, Steps = 2 });
            foreach (var u in units)
            {
                Assert.Equal(0b0101, TierBits(world, u));               // 256 + the 4096 the 3 KB memory needed (1024 cannot hold it)
                byte tag = (byte)(1 + u.Index % 200);
                Assert.Equal(tag, UnitMemory.Ref<Mem3K>(world, u).B[0]);
                Assert.Equal(tag, UnitMemory.Ref<Mem3K>(world, u).B[2999]);
            }
            Assert.Equal(0b0001, TierBits(world, idle));
        });
    }

    private struct CrossJob : IEntityBatchJob
    {
        public EntityRepository Repo;
        public Entity Other;
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            using var view = OccurrenceStoreAccess.BeginTickView(Repo, e);
            UnitMemory.Ref<Mem600>(Repo, Other);   // ANOTHER unit's store, inside the phase
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    /// <summary>⭐ CE-9013 — growing ANOTHER unit's store inside the phase is a cross-entity write: it throws (the per-entity guard or §34.7's), and the other store is untouched.</summary>
    [Fact]
    public void CE9013_R4_GrowingAnotherUnitsStoreInsideThePhase_Throws()
    {
        InPhase(() =>
        {
            using var world = CreateWorld();
            var self = BrainUnitWith(world, 1);
            var other = BrainUnitWith(world, 1);
            Assert.ThrowsAny<Exception>(() => ParallelEntities.Run(world, new[] { self }, 1, new CrossJob { Repo = world, Other = other }));
            Assert.Equal(0b0001, TierBits(world, other));
        });
    }
}
