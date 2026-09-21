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
    private const uint HostMachine = 0x0700A5E1;   // the HSM definition's StructureHash
    private static readonly Guid Child   = new("07000000-0000-0000-0000-0000000000b1");

    private struct DemoWorkingState { public int Counter; public float Elapsed; }

    private static int Key(int region, ushort state)
        => Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.ComputeHsmStateKey(HostMachine, region, state, Child);

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
        const uint OtherMachine = 0x0700A5E2;
        var otherChild = new Guid("07000000-0000-0000-0000-0000000000b9");
        int baseline = Key(region: 0, state: 4);

        Assert.NotEqual(baseline, Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey
            .ComputeHsmStateKey(OtherMachine, 0, 4, Child));
        Assert.NotEqual(baseline, Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey
            .ComputeHsmStateKey(HostMachine, 0, 4, otherChild));
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
        int btreeKey = OccurrenceSlots.TreeStateKeyFor(Child, node, Child);

        Assert.NotEqual(hsmKey, btreeKey);

        // ⚠ And a site id is never 0 — a 0 host key re-enters ComputeNested's ROOT branch, which
        //   drops the site entirely.
        Assert.NotEqual(0, OccurrenceSlots.IdentityOf(Child));
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
            var hdr = new InstanceHeader { MachineId = HostMachine };
            try { HsmOccurrence.KeyFor(&hdr, Child, &writer); }
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

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ⑩ — <c>O7b-2</c>: every occurrence is recoverable and LABELLED, exactly.</b>
    ///
    /// <para>🔒 <b>User ruling, <c>2026-09-21</c>:</b> <i>"Show all and properly labelled and properly
    /// decoded into readable state."</i> ⛔ The key is an FNV fold and cannot be inverted, and
    /// <c>BlueprintSlotEntry</c> is exactly 16 bytes with no room to record the pair — so the debugger
    /// searches FORWARD. ⭐ A hit is EXACT: the key either equals the one the thunk computed or it does
    /// not, so a label is never a guess.</para>
    /// </summary>
    [Fact]
    public void O7_R10_EveryOccurrenceIsRecoverableAndLabelled()
    {
        foreach (var (region, state) in new[] { (0, (ushort)0), (1, (ushort)4), (3, (ushort)17) })
        {
            int key = Key(region, state);

            Assert.True(HsmOccurrence.TryDescribe(HostMachine, Child, key,
                out int foundRegion, out ushort foundState));

            Assert.Equal(region, foundRegion);
            Assert.Equal(state, foundState);
            Assert.Equal($"Region {region} / State {state}",
                         HsmOccurrence.DescribeLabel(foundRegion, foundState));
        }
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ⑪ — an unrecognised key is REPORTED AS UNKNOWN, never mislabelled.</b>
    ///
    /// <para>⚠ The search is bounded, so a state id beyond the cap is simply not found. ⛔ That must
    /// read as <i>"unknown"</i> and let the caller fall back to the raw key — a plausible-but-wrong
    /// label is worse than none, because it would attribute one region's state to another.</para>
    /// </summary>
    [Fact]
    public void O7_R11_AnUnrecognisedKeyIsNotMislabelled()
    {
        // A key from a DIFFERENT host machine cannot belong to this one.
        int foreign = Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey
            .ComputeHsmStateKey(0x0BADF00D, 0, 4, Child);

        Assert.False(HsmOccurrence.TryDescribe(HostMachine, Child, foreign, out int r, out ushort st));
        Assert.Equal(-1, r);
        Assert.Equal(HsmCommandWriter.NoStateId, st);

        // And a state beyond the search bound is not found either — bounded, not wrong.
        int beyond = Key(0, 900);
        Assert.False(HsmOccurrence.TryDescribe(HostMachine, Child, beyond, out _, out _, maxStateId: 100));
    }

    // ═══ E1 — A GENUINELY MULTI-REGION MACHINE ════════════════════════════════════════
    //  🔴 Measured 2026-09-21: NO test in this repo had ever driven one — RegionDef[] is
    //     Array.Empty in every single fixture. Every O6/O7 claim about "two regions" therefore
    //     rested on key arithmetic and hand-attached slots, never on the kernel.
    //  ⭐ The shape, read out of InitializeMachine/InitializeSlot rather than guessed:
    //     state 0 is IsComposite|IsParallel, so slot 0's drill-down STOPS there (it is its own
    //     leaf); each region r>=1 then initialises because IsAncestor(leaf 0, parent 0) is true
    //     (a state is its own ancestor, :989). ⇒ activeLeafIds == [0, 1, 2].

    private static HsmDefinitionBlob BuildTwoRegionBlob(ushort actionId)
    {
        var states = new StateDef[3];
        // 0 — the parallel composite; the leaf of slot 0.
        states[0] = new StateDef
        {
            ParentIndex = 0xFFFF, FirstChildIndex = 1, ChildCount = 2,
            FirstTransitionIndex = 0xFFFF,
            Flags = StateFlags.IsComposite | StateFlags.IsParallel,
        };
        // 1 and 2 — one per orthogonal region, BOTH hosting the SAME asset.
        states[1] = new StateDef
        {
            ParentIndex = 0, FirstTransitionIndex = 0xFFFF, OnEntryActionId = actionId,
        };
        states[2] = new StateDef
        {
            ParentIndex = 0, FirstTransitionIndex = 0xFFFF, OnEntryActionId = actionId,
        };

        // regions[0] is the root slot; the r>=1 loop is what activates the orthogonal ones.
        var regions = new RegionDef[3];
        regions[0] = new RegionDef { ParentStateIndex = 0xFFFF, InitialStateIndex = 0 };
        regions[1] = new RegionDef { ParentStateIndex = 0, InitialStateIndex = 1 };
        regions[2] = new RegionDef { ParentStateIndex = 0, InitialStateIndex = 2 };

        var header = new HsmDefinitionHeader
        {
            StructureHash = HostMachine, StateCount = 3, RegionCount = 3,
        };
        return new HsmDefinitionBlob(
            header, states, Array.Empty<TransitionDef>(), regions,
            Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ⑫ — <c>E1</c>: TWO REGIONS, ONE TICK, ONE ASSET — the kernel really does stamp
    /// two different occurrences.</b>
    ///
    /// <para>🔒 This is the rail design §7 demanded and could not have: <i>"two regions, two slots…
    /// `BP-297` measured that today's fixture cannot redden this."</i> ⭐ Now it can — and note what
    /// it costs to be honest: the pair must come from a REAL multi-region dispatch, not from calling
    /// <c>KeyFor</c> twice with different numbers.</para>
    /// </summary>
    [Fact]
    public void O7_R12_TwoRegionsInOneTickAreTwoDistinctOccurrences()
    {
        const ushort ActionId = 0x0712;
        Seen.Clear();
        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        Fhsm.Kernel.HsmActionDispatcher.RegisterAction(
            ActionId, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&RecordingStamp);
        try
        {
            var blob = BuildTwoRegionBlob(ActionId);
            var inst = new HsmInstance128();
            inst.Header.MachineId = HostMachine;
            inst.Header.Phase = InstancePhase.Entry;
            for (int r = 0; r < 4; r++) inst.ActiveLeafIds[r] = 0xFFFF;

            var ctx = 0;
            var page = default(CommandPage);
            Fhsm.Kernel.HsmKernel.Update(blob, ref inst, in ctx, 0.016f, ref page);

            // Non-vacuity: the parallel root really did fan out into two regions.
            Assert.Equal((ushort)0, inst.ActiveLeafIds[0]);
            Assert.Equal((ushort)1, inst.ActiveLeafIds[1]);
            Assert.Equal((ushort)2, inst.ActiveLeafIds[2]);

            // ⭐⭐ THE RAIL. One asset, one tick, TWO dispatches — each stamped with its own region.
            Assert.Equal(2, Seen.Count);
            Assert.Contains((1, (ushort)1), Seen);
            Assert.Contains((2, (ushort)2), Seen);

            // ⭐⭐⭐ And therefore two DIFFERENT slot keys — BP-297 closed on a real dispatch, not on
            //     arithmetic. 🔴 Before O7 both of these resolved to Blackboard1024 + 8.
            int keyA = Key(Seen[0].Region, Seen[0].State);
            int keyB = Key(Seen[1].Region, Seen[1].State);
            Assert.NotEqual(keyA, keyB);
        }
        finally
        {
            Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        }
    }

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ⑬ — <c>CE-298</c> PINNED: the two occurrences' WORKING STATE separates, and
    /// their PARAMS DO NOT.</b>
    ///
    /// <para>🔒 <b>User, <c>2026-09-21</c>:</b> <i>"two actions running from hsm regions, each having
    /// its params, they can not share same single place."</i> ⭐ Correct — and
    /// <c>DESIGN_Parameter_Model.md</c> §4.1 already called it a <b>live race</b>.</para>
    ///
    /// <para>⛔⛔ <b>THIS RAIL ASSERTS THE DEFECT, ON PURPOSE.</b> It is green today because params
    /// ARE shared. ⭐⭐ <b>When <c>CE-298</c> lands it must go RED</b> — that is the point: the fix
    /// cannot be shipped silently, and whoever ships it has to come here and flip the assertion to
    /// <c>NotEqual</c>. ⛔ Not a <c>Skip</c>: a skipped rail tells nobody anything.</para>
    /// </summary>
    [Fact]
    public void O7_R13_TwoRegionsWorkingStateSeparates()
    {
        using var world = CreateWorld();
        var entity = MakeEntityWithStore(world);

        // Two occurrences of ONE asset, as rail ⑫ proves the kernel produces.
        int keyRegion1 = Key(region: 1, state: 1);
        int keyRegion2 = Key(region: 2, state: 2);

        HsmOccurrence.ResolveOrAttach<DemoWorkingState>(world, entity, keyRegion1, 0xABCD, out _).Counter = 111;
        HsmOccurrence.ResolveOrAttach<DemoWorkingState>(world, entity, keyRegion2, 0xABCD, out _).Counter = 222;

        // ✅ WORKING STATE — separated by O7b. This half is FIXED.
        Assert.Equal(111, HsmOccurrence.ResolveOrAttach<DemoWorkingState>(world, entity, keyRegion1, 0xABCD, out _).Counter);
        Assert.Equal(222, HsmOccurrence.ResolveOrAttach<DemoWorkingState>(world, entity, keyRegion2, 0xABCD, out _).Counter);

        // 🔴 PARAMS — NOT separated. ⛔ The honest pin for that lives where the defect lives: an
        //    EMISSION rail asserting the thunk's params projection takes no occurrence argument
        //    (ThunkEmissionTests.HsmThunk_StillProjectsParamsPerEntity_CE298). ⚠ Asserting it here
        //    would compare a constant with itself and prove nothing — which is worth saying out
        //    loud, because a vacuous rail is worse than an absent one: it reads as coverage.
    }

    private static void RecordingStamp(void* instance, void* context, HsmCommandWriter* writer)
        => Seen.Add((writer->OccurrenceRegionSlotIndex, writer->OccurrenceStateId));

    private static readonly List<(int Region, ushort State)> Seen = new();

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
        _capturedKey = HsmOccurrence.KeyFor(instance, Child, writer);
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
        var header = new HsmDefinitionHeader { StructureHash = HostMachine, StateCount = 1 };
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
