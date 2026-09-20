using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>O7</c> / <c>E3</c> — AN HSM-HOSTED OCCURRENCE IS KEYED BY ITS (REGION, STATE).</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §24, and it closes <c>BP-297</c>.
///
/// <para>🔴🔴 <b>The shipped defect.</b> All three emitted HSM thunks resolve their working state as
/// <c>GetComponentRW&lt;Blackboard1024&gt;(bridge-&gt;Self)</c> at a hard-coded <c>memory + 8</c> —
/// <b>one working state per ENTITY</b>. Two concurrently-active HSM regions running the same asset
/// therefore write the SAME bytes, silently. ⭐ The BTree hosting path has been occurrence-keyed since
/// <c>S2</c>; this brings the HSM path onto the same storage.</para>
///
/// <para>⚠ <b>What these rails do and do NOT cover.</b> They pin the KEY and the LOOKUP — the seam the
/// thunks will call. ⛔ They do not yet prove that a generated thunk calls it: that is the emitter
/// slice, and §24 says so plainly rather than letting these rails read as more than they are.</para>
/// </summary>
public sealed unsafe class HsmOccurrenceKeyTests
{
    private static readonly Guid HostHsm = new("07000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Child   = new("07000000-0000-0000-0000-0000000000b1");

    private struct DemoWorkingState { public int Counter; public float Elapsed; }

    private static int Key(int region, ushort state)
        => Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.ComputeHsmStateKey(HostHsm, region, state, Child);

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ① — THE <c>BP-297</c> CLAIM: region and state each discriminate, on their own.</b>
    ///
    /// <para>🔴 <b>Both halves are load-bearing and this rail is written to prove each separately.</b>
    /// Two regions sitting in the SAME state are different occurrences (fold the state only ⇒ they
    /// alias); one region moving BETWEEN states is a different occurrence (fold the region only ⇒ it
    /// aliases). ⛔ <b>Red-proof:</b> make <c>ComputeHsmSiteId</c> ignore either argument and exactly
    /// one of the two assertions below reddens — which is what tells you WHICH half broke.</para>
    /// </summary>
    [Fact]
    public void O7_R1_TheKeyDiscriminatesByRegionAndByState()
    {
        int baseline = Key(region: 0, state: 4);

        // ⛔ THE DEFECT, at key level: two regions, same state, same asset — today all three thunks
        //    would hand both the SAME Blackboard1024 bytes.
        Assert.NotEqual(baseline, Key(region: 1, state: 4));

        // ⛔ And the same region in a different state is a different occurrence too.
        Assert.NotEqual(baseline, Key(region: 0, state: 5));

        // ⭐ Pure: the emitter bakes host/child as literals and the runtime folds the pair, so both
        //   callers must land on the same number (the D3 "one function, two callers" rule).
        Assert.Equal(baseline, Key(region: 0, state: 4));
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ② — the key also discriminates by HOST and by CHILD.</b>
    ///
    /// <para>⚠ Two different HSM assets may each host the same blueprint at <c>(region 0, state 0)</c>;
    /// one entity can carry both. ⛔ Without the host in the key they would alias — the same collision
    /// <c>§19.7 ①</c> found on the BTree side, where <c>ComputeNested</c> drops the site when
    /// <c>hostKey == 0</c>.</para>
    /// </summary>
    [Fact]
    public void O7_R2_TheKeyDiscriminatesByHostAndByChild()
    {
        var otherHost = new Guid("07000000-0000-0000-0000-0000000000a9");
        var otherChild = new Guid("07000000-0000-0000-0000-0000000000b9");
        int baseline = Key(region: 0, state: 4);

        Assert.NotEqual(baseline, Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey
            .ComputeHsmStateKey(otherHost, 0, 4, Child));
        Assert.NotEqual(baseline, Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey
            .ComputeHsmStateKey(HostHsm, 0, 4, otherChild));
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ③ — an HSM site can never be confused with a BTree site.</b>
    ///
    /// <para>⚠ Both hosting paradigms fold a "site" through the same arithmetic into the same store.
    /// ⛔ If an HSM <c>(region, state)</c> could land on the same site id as a BTree node <c>Guid</c>,
    /// one paradigm would read the other's bytes. ⭐ They are separated by their reserved variable ids,
    /// not by luck — and this rail fails if someone "simplifies" them onto one name.</para>
    /// </summary>
    [Fact]
    public void O7_R3_AnHsmSiteIsNeverABTreeSite()
    {
        var node = new Guid("07000000-0000-0000-0000-0000000000c1");

        int hsmKey = Key(region: 0, state: 0);
        int btreeKey = OccurrenceSlots.TreeStateKeyFor(HostHsm, node, Child);

        Assert.NotEqual(hsmKey, btreeKey);

        // ⚠ And a site id is never 0 — a 0 host key re-enters ComputeNested's ROOT branch, which
        //   drops the site entirely.
        Assert.NotEqual(0, OccurrenceSlots.IdentityOf(HostHsm));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ④ — <c>KeyFor</c> reads the stamp <c>O6</c> left, and REFUSES an unstamped writer.</b>
    ///
    /// <para>🔴 <b>This is the join between <c>O6</c> and <c>O7</c>.</b> The kernel stamps the pair; the
    /// thunk folds it into a key. ⛔ Reaching here with the sentinels means the thunk ran outside a
    /// dispatch, and silently resolving that to <i>"region 0, state 0"</i> would hand it a REAL
    /// occurrence's bytes.</para>
    /// </summary>
    [Fact]
    public void O7_R4_KeyForReadsTheKernelsStampAndRefusesAnUnstampedWriter()
    {
        // ⚠ Not Assert.Throws: a lambda may not take the address of a ref local (CS1686/CS8175),
        //   and the whole point of KeyFor is that it reads a POINTER the kernel handed the thunk.
        InvalidOperationException? thrown = null;
        {
            var page = default(CommandPage);
            var writer = new HsmCommandWriter(&page);
            try { HsmOccurrence.KeyFor(HostHsm, Child, &writer); }
            catch (InvalidOperationException ex) { thrown = ex; }
        }

        // ⛔ Unstamped ⇒ loud failure, never a plausible-looking key.
        Assert.NotNull(thrown);
        Assert.Contains("no occurrence stamp", thrown!.Message);

        // ⭐ Stamped by a real kernel tick ⇒ the key the manifest would have registered.
        //   (Driven through the kernel rather than a hand-set field, so this rail breaks if the
        //    stamping path changes — it is the JOIN that matters, not the field.)
        int fromKernel = StampViaKernelAndKey(out int region, out ushort state);
        Assert.Equal(Key(region, state), fromKernel);
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ⑤ — <c>Resolve</c> hands back the occurrence's own bytes, and two occurrences on ONE
    /// entity do not share them.</b>
    ///
    /// <para>🔴🔴 <b>This is <c>BP-297</c> measured on real memory, not on key arithmetic.</b> Two slots
    /// on one entity, keyed for two regions in the same state; writing through one must not move the
    /// other. ⛔ Today's thunks would return the same <c>Blackboard1024</c> bytes for both.</para>
    /// </summary>
    [Fact]
    public void O7_R5_TwoRegionsGetTwoSlotsOnOneEntity()
    {
        using var world = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        int keyA = Key(region: 0, state: 4);
        int keyB = Key(region: 1, state: 4);

        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            BlueprintBlackboardPartitions.Initialize(
                mem, BlueprintBlackboard1024.TotalSize, (byte)BlueprintBlackboard1024.MaxSlots);

            Assert.True(BlueprintBlackboardPartitions.TryAttach(
                mem, keyA, sizeof(DemoWorkingState), 0, OccurrenceKind.Hsm, out _));
            Assert.True(BlueprintBlackboardPartitions.TryAttach(
                mem, keyB, sizeof(DemoWorkingState), 0, OccurrenceKind.Hsm, out _));
        }

        HsmOccurrence.Resolve<DemoWorkingState>(world, entity, keyA).Counter = 11;
        HsmOccurrence.Resolve<DemoWorkingState>(world, entity, keyB).Counter = 22;

        // ⭐⭐ THE RAIL. Region 0 and region 1 are the same asset in the same state, and they do not
        //    share a byte.
        Assert.Equal(11, HsmOccurrence.Resolve<DemoWorkingState>(world, entity, keyA).Counter);
        Assert.Equal(22, HsmOccurrence.Resolve<DemoWorkingState>(world, entity, keyB).Counter);
    }

    /// <summary>
    /// ⭐ <b>Rail ⑥ — a missing slot FAILS LOUDLY</b> (§19.6 ⑤).
    ///
    /// <para>⛔ The alternative — a zeroed scratch — reads as <i>"the action just does nothing"</i>,
    /// which is the silent slot miss <c>A1</c> exists to kill.</para>
    /// </summary>
    [Fact]
    public void O7_R6_AMissingSlotThrowsRatherThanFailingSilently()
    {
        using var world = CreateWorld();
        var entity = world.CreateEntity();

        // No occurrence store at all.
        Assert.Throws<InvalidOperationException>(
            () => HsmOccurrence.Resolve<DemoWorkingState>(world, entity, Key(0, 4)));

        // A store, but no slot for this occurrence.
        world.AddComponent(entity, new BlueprintBlackboard1024());
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
            BlueprintBlackboardPartitions.Initialize(
                mem, BlueprintBlackboard1024.TotalSize, (byte)BlueprintBlackboard1024.MaxSlots);

        Assert.Throws<InvalidOperationException>(
            () => HsmOccurrence.Resolve<DemoWorkingState>(world, entity, Key(0, 4)));
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ⑦ — <c>ResolveOrAttach</c> creates the slot on first use, then reuses it.</b>
    ///
    /// <para>⭐ The shipped thunk is already self-initialising, so lazy attach is a like-for-like
    /// replacement rather than a new lifecycle (§24.8). ⚠ <c>freshlyAttached</c> is what tells the
    /// caller to run <c>InitDefaultWorkingState</c> — ⛔ it is not an error signal.</para>
    /// </summary>
    [Fact]
    public void O7_R7_ResolveOrAttachCreatesTheSlotOnceThenReusesIt()
    {
        using var world = CreateWorld();
        var entity = MakeEntityWithStore(world);
        int key = Key(region: 0, state: 4);

        ref var first = ref HsmOccurrence.ResolveOrAttach<DemoWorkingState>(
            world, entity, key, structureHash: 0xABCD, out bool fresh1);
        Assert.True(fresh1);
        first.Counter = 7;

        ref var second = ref HsmOccurrence.ResolveOrAttach<DemoWorkingState>(
            world, entity, key, structureHash: 0xABCD, out bool fresh2);

        // ⭐⭐ THE RAIL. Second entry finds the SAME slot and the state survived.
        Assert.False(fresh2);
        Assert.Equal(7, second.Counter);
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ⑧ — a <c>StructureHash</c> mismatch RESETS the occurrence.</b>
    ///
    /// <para>⛔ The layout changed under the slot, so the old bytes are not this type's. ⚠ It is also
    /// the only defence against an HSM recompile renumbering states, which would move this occurrence's
    /// key — see §24.5. ⭐ Reset, never reinterpret.</para>
    /// </summary>
    [Fact]
    public void O7_R8_AStructureHashMismatchResetsTheOccurrence()
    {
        using var world = CreateWorld();
        var entity = MakeEntityWithStore(world);
        int key = Key(region: 0, state: 4);

        HsmOccurrence.ResolveOrAttach<DemoWorkingState>(
            world, entity, key, structureHash: 0xABCD, out _).Counter = 99;

        ref var after = ref HsmOccurrence.ResolveOrAttach<DemoWorkingState>(
            world, entity, key, structureHash: 0x1234, out bool fresh);

        // ⭐⭐ THE RAIL. A new layout gets a FRESH slot, not the old bytes reinterpreted.
        Assert.True(fresh);
        Assert.Equal(0, after.Counter);
    }

    /// <summary>
    /// ⭐ <b>Rail ⑨ — a missing STORE is a different failure from a missing SLOT, and says so.</b>
    ///
    /// <para>⛔ Adding a tier component is a STRUCTURAL change and must never happen inside a kernel
    /// dispatch. ⚠ Conflating the two messages is how a caller "fixes" the wrong thing.</para>
    /// </summary>
    [Fact]
    public void O7_R9_AMissingStoreIsRefusedDistinctlyFromAMissingSlot()
    {
        using var world = CreateWorld();
        var entity = world.CreateEntity();

        var ex = Assert.Throws<InvalidOperationException>(
            () => HsmOccurrence.ResolveOrAttach<DemoWorkingState>(
                      world, entity, Key(0, 4), 0xABCD, out _));

        Assert.Contains("structural change", ex.Message);
    }

    private static Entity MakeEntityWithStore(EntityRepository world)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
            BlueprintBlackboardPartitions.Initialize(
                mem, BlueprintBlackboard1024.TotalSize, (byte)BlueprintBlackboard1024.MaxSlots);
        return entity;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var world = TestWorldFactory.Create();
        BlueprintTierTable.RegisterUpTo(world, maxTotalSize: 1024);
        return world;
    }

    private static int _capturedKey;
    private static int _capturedRegion;
    private static ushort _capturedState;

    private static void CapturingAction(void* instance, void* context, HsmCommandWriter* writer)
    {
        _capturedRegion = writer->OccurrenceRegionSlotIndex;
        _capturedState = writer->OccurrenceStateId;
        _capturedKey = HsmOccurrence.KeyFor(HostHsm, Child, writer);
    }

    /// <summary>Drives one real kernel tick so the stamp comes from production code, not a test setter.</summary>
    private static int StampViaKernelAndKey(out int region, out ushort state)
    {
        const ushort ActionId = 0x0701;
        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        Fhsm.Kernel.HsmActionDispatcher.RegisterAction(
            ActionId, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&CapturingAction);

        var states = new[]
        {
            new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, OnEntryActionId = ActionId },
        };
        var header = new HsmDefinitionHeader { StructureHash = 0x0700A5E1, StateCount = 1 };
        var blob = new HsmDefinitionBlob(
            header, states, Array.Empty<TransitionDef>(), Array.Empty<RegionDef>(),
            Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());

        var inst = new HsmInstance128();
        inst.Header.MachineId = header.StructureHash;
        inst.Header.Phase = InstancePhase.Entry;
        for (int r = 0; r < 4; r++) inst.ActiveLeafIds[r] = 0xFFFF;   // see HsmOccurrenceStampTests

        var ctx = 0;
        var page = default(CommandPage);
        Fhsm.Kernel.HsmKernel.Update(blob, ref inst, in ctx, 0.016f, ref page);

        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        region = _capturedRegion;
        state = _capturedState;
        return _capturedKey;
    }
}
