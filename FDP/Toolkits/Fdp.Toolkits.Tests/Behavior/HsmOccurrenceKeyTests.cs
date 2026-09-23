using System;
using System.Linq;
using System.Collections.Generic;
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

    /// <summary>
    /// ⭐⭐ Attaches this entity's ROOT PARAMS SLOT and returns its base — the fixture's stand-in for
    /// what <c>BehaviorIngressSystem</c> does at assign time, since these rails drive the KERNEL
    /// directly and never publish an <c>AssignBehaviorEvent</c>.
    ///
    /// <para>🔴 <b>Before <c>P3-C</c> this was <c>AddComponent(new BrainBlackboard())</c> plus a write
    /// into its fixed buffer.</b> ⛔ The component is retired; the seed the emitted thunks read is the
    /// slot, so the fixture has to build the slot or it is testing nothing.</para>
    /// </summary>
    private static byte* SeedRootParams(EntityRepository world, Entity entity)
    {
        const int BehaviourHash = 0x7E5701;

        if (!world.HasComponent<Components.BehaviorState>(entity))
            world.AddComponent(entity, new Components.BehaviorState());
        ref var st = ref world.GetComponentRW<Components.BehaviorState>(entity);
        st.ActiveBehaviorHash = BehaviourHash;

        byte* p = RootParamsAccess.ResolveOrAttachRoot(
            world, entity, BehaviourHash, BehaviorConstants.MaxBehaviorParamByteSize,
            OccurrenceKind.Hsm, out _);
        Assert.True(p != null, "the fixture's store had no room for a root params slot");
        return p;
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

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ⑭ — <c>E-cap</c>: a behaviour that declares NO stateful slots still gets a STORE,
    /// so a hosted occurrence can attach on first dispatch.</b> 📄 design §27.
    ///
    /// <para>⛔⛔ <b>This pins a gap that was SHIPPED.</b> <c>ProvisionStatefulSlots</c> ran only on a
    /// non-empty manifest, so a behaviour that hosts a blueprint but declares no stateful slots of its
    /// own got no store — and a hosted occurrence cannot create one, because adding a tier component is
    /// a STRUCTURAL change and must not happen inside a tick. ⇒ the first dispatch threw.</para>
    ///
    /// <para>⚠ <b><c>O7b</c>'s HSM path passed its tests only because the fixture adds a store by
    /// hand.</b> 🔒 That is the exact shape §26.1's rule names — <i>"storage without supply"</i> — and
    /// it is why this rail drives the REAL ingress rather than a hand-built entity.</para>
    /// </summary>
    [Fact]
    public void O7_R14_ABehaviourWithNoManifestStillGetsAStore()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

        var registry = new BehaviorRegistry();
        var sys = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);

        // ⚠ A BTree behaviour with an EMPTY stateful manifest — the case that got no store.
        const string name = "EcapHostNoManifest";
        registry.Register(7401, name, new BehaviorDefinition
        {
            Name = name,
            BrainTier = BehaviorConstants.BrainTierBTree,
            StatefulWorkingSlots = Array.Empty<StatefulSlotInfo>(),
        });

        world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = name, JsonParams = string.Empty,
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);

        // ⭐⭐ THE RAIL. A hosted occurrence can now attach on first dispatch. 🔴 Before E-cap this
        //    threw "carries no occurrence store".
        ref var ws = ref OccurrenceWorkingState.ResolveOrAttach<DemoWorkingState>(
            world, entity, OccurrenceSlots.StandaloneStateKeyFor(Child), 0xABCD,
            OccurrenceKind.Blueprint, out bool fresh);

        Assert.True(fresh);
        ws.Counter = 5;
        Assert.Equal(5, OccurrenceWorkingState.ResolveOrAttach<DemoWorkingState>(
            world, entity, OccurrenceSlots.StandaloneStateKeyFor(Child), 0xABCD,
            OccurrenceKind.Blueprint, out _).Counter);
    }

    /// <summary>
    /// ⭐ <b>Rail ⑮ — and it is BRAIN-TIER ONLY: this is not "a store for every entity".</b>
    ///
    /// <para>⚠ A behaviour that is neither BTree nor HSM cannot host an occurrence, so provisioning one
    /// would be pure cost. ⛔ The rail exists because the cheap wrong version of <c>E-cap</c> is to
    /// provision unconditionally, and nothing else would notice.</para>
    /// </summary>
    [Fact]
    public void O7_R15_ANonBrainBehaviourGetsNoStore()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BehaviorState());

        var registry = new BehaviorRegistry();
        var sys = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);

        const string name = "EcapNotABrain";
        registry.Register(7402, name, new BehaviorDefinition
        {
            Name = name,
            BrainTier = 0,                         // neither BTree nor HSM
            StatefulWorkingSlots = Array.Empty<StatefulSlotInfo>(),
        });

        world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = name, JsonParams = string.Empty,
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);

        // ⭐⭐ THE RAIL. No store — it could never host anything.
        byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        Assert.True(store == null);
    }

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ⑯ — <c>O7b-3</c>: the SMALLEST tier holds THREE slots, and a fourth hosted
    /// occurrence has nowhere to go.</b> 📄 design §27.5 / §27.7.
    ///
    /// <para>⛔⛔ <b>This is the half <c>E-cap</c> deliberately did NOT deliver.</b> <c>E-cap</c>
    /// provisions <c>SelectTierForPayload(0, 0)</c> — the 256 tier, <b>3 slots / 176 payload bytes</b>.
    /// A host with four hosted occurrences overflows it on the FOURTH attach, and the failure is a
    /// throw from inside a dispatch.</para>
    ///
    /// <para>⚠ <b>Nothing in the shipped corpus reaches this yet</b> — measured: exactly ONE asset
    /// declares HSM hosting (<c>MoveAndFireCombo</c>) and its <c>WorkingState</c> is EMPTY. 🔒 The rail
    /// exists because the user's standing ruling is that HSM features are covered by rails before they
    /// are covered by usage: <i>"HSMs are under adopted now, but their time will come soon."</i></para>
    /// </summary>
    [Fact]
    public void O7_R16_AFourthHostedOccurrenceNeedsMoreThanTheSmallestTier()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);

        var entity = AssignHostingBehaviour(world, 7403, "O7b3FourOccurrences", slotCount: 4,
                                            payloadEach: sizeof(DemoWorkingState));

        // ⭐⭐ THE RAIL. Four DISTINCT occurrences must all attach. 🔴 Sized at the smallest tier this
        //    throws on the fourth: MaxSlots is 3.
        for (int i = 0; i < 4; i++)
        {
            ref var ws = ref OccurrenceWorkingState.ResolveOrAttach<DemoWorkingState>(
                world, entity, HsmOccurrence.KeyFor(HostMachine, Child, regionSlotIndex: i, stateId: 1),
                0xABCD, OccurrenceKind.Hsm, out bool fresh);
            Assert.True(fresh);
            ws.Counter = i;
        }

        // …and they are FOUR separate states, not one aliased four times.
        for (int i = 0; i < 4; i++)
            Assert.Equal(i, OccurrenceWorkingState.ResolveOrAttach<DemoWorkingState>(
                world, entity, HsmOccurrence.KeyFor(HostMachine, Child, i, 1),
                0xABCD, OccurrenceKind.Hsm, out _).Counter);
    }

    /// <summary>
    /// 🔴🔴 <b>Rail ⑰ — the OTHER axis: one FAT working state overflows the smallest tier's PAYLOAD.</b>
    ///
    /// <para>⭐ <c>BlueprintTierTable.SelectTierForPayload</c> takes <b>both</b> axes and §5a's <c>F3</c>
    /// says why: slots and bytes run out independently. ⚠ Rail ⑯ exhausts slots with tiny payloads;
    /// this one exhausts bytes with a single slot, so a fix that only counts occurrences still reddens
    /// here.</para>
    /// </summary>
    [Fact]
    public void O7_R17_OneFatWorkingStateNeedsMoreThanTheSmallestTiersPayload()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);

        var entity = AssignHostingBehaviour(world, 7404, "O7b3FatOccurrence", slotCount: 1,
                                            payloadEach: sizeof(FatWorkingState));

        // ⭐⭐ THE RAIL. 🔴 The 256 tier's payload is 176 bytes; FatWorkingState is 512.
        ref var ws = ref OccurrenceWorkingState.ResolveOrAttach<FatWorkingState>(
            world, entity, HsmOccurrence.KeyFor(HostMachine, Child, 0, 1),
            0xFA7, OccurrenceKind.Hsm, out bool fresh);

        Assert.True(fresh);
        ws.Head = 0x5A;
        Assert.Equal((byte)0x5A, OccurrenceWorkingState.ResolveOrAttach<FatWorkingState>(
            world, entity, HsmOccurrence.KeyFor(HostMachine, Child, 0, 1),
            0xFA7, OccurrenceKind.Hsm, out _).Head);
    }

    /// <summary>
    /// ⚠ <b>Rail ⑱ — and the demand must be ADDITIVE to a behaviour's own manifest, not an alternative
    /// to it.</b>
    ///
    /// <para>⛔ The cheap wrong fix sizes the tier for hosted occurrences only in the
    /// <c>EnsureOccurrenceStore</c> branch — which never runs when the behaviour declares stateful slots
    /// of its own. ⇒ a behaviour with BOTH is sized for the manifest alone and overflows exactly as
    /// before, in the branch nobody looked at.</para>
    /// </summary>
    [Fact]
    public void O7_R18_HostedDemandIsAddedOnTopOfTheBehavioursOwnManifest()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);

        // Two manifest slots of its own AND three hosted occurrences ⇒ five in total.
        var own = new[]
        {
            new StatefulSlotInfo(unchecked((int)0xB0000001), sizeof(DemoWorkingState), 0x11),
            new StatefulSlotInfo(unchecked((int)0xB0000002), sizeof(DemoWorkingState), 0x22),
        };
        var entity = AssignHostingBehaviour(world, 7405, "O7b3ManifestPlusHosted", slotCount: 3,
                                            payloadEach: sizeof(DemoWorkingState), ownManifest: own);

        // The manifest slots are there (E-cap never touched this branch)…
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(
            OccurrenceStoreAccess.TryGetStore(world, entity, out _), own[0].SlotKey, out _));

        // ⭐⭐ THE RAIL. …and the three hosted ones still fit on top. 🔴 Sized for the manifest alone
        //    the 256 tier has ONE slot left and this throws on the second.
        for (int i = 0; i < 3; i++)
            OccurrenceWorkingState.ResolveOrAttach<DemoWorkingState>(
                world, entity, HsmOccurrence.KeyFor(HostMachine, Child, i, 1),
                0xABCD, OccurrenceKind.Hsm, out _).Counter = i;

        for (int i = 0; i < 3; i++)
            Assert.Equal(i, OccurrenceWorkingState.ResolveOrAttach<DemoWorkingState>(
                world, entity, HsmOccurrence.KeyFor(HostMachine, Child, i, 1),
                0xABCD, OccurrenceKind.Hsm, out _).Counter);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ⑲ — <c>O7b-3</c>'s PRODUCER: the demand is DERIVED from the machine, not declared.</b>
    ///
    /// <para>🔒 <b>This is the rail for the user's ruling</b> (<c>2026-09-21</c>: <i>"would that mean the
    /// hsm code will need to know about blueprint catalogs? Not good."</i>). ⭐ The join runs the other
    /// way: the machine's own <see cref="StateDef"/>s already carry action ids, and an action id IS the
    /// blueprint id truncated to 16 bits — so nothing has to be emitted and the HSM emitter learns
    /// nothing. ⛔ Rails ⑯–⑱ prove the CONSUMER; without this one the demand could only ever be
    /// hand-written.</para>
    /// </summary>
    [Fact]
    public void O7_R19_TheDemandIsDerivedFromTheMachinesOwnActionIds()
    {
        const int BlueprintId = unchecked((int)0xDEAD0712);   // low-16 = 0x0712
        var blueprints = OneAiPrimitive(BlueprintId, stateSize: 24);

        // BuildTwoRegionBlob puts the SAME action on states 1 and 2 ⇒ two distinct occurrences.
        var def = HostingBehaviour(BuildTwoRegionBlob(unchecked((ushort)BlueprintId)));

        var demand = HostedOccurrenceDemandCalculator.For(def, blueprints);

        Assert.NotNull(demand);
        Assert.Equal(2, demand!.SlotCount);
        Assert.Equal(2 * 24, demand.PayloadBytes);            // 24 is already 8-aligned
    }

    /// <summary>
    /// ⚠ <b>Rail ⑳ — an action id that is NOT a blueprint contributes NOTHING.</b>
    ///
    /// <para>⛔ Most HSM actions are hand-written thunks with no occurrence at all. ⭐ Counting them
    /// would inflate every machine's tier, so the calculator's membership test is the blueprint
    /// registry — and this rail is what stops a future "count every action id" simplification.</para>
    /// </summary>
    [Fact]
    public void O7_R20_AnActionIdThatIsNotABlueprintCostsNothing()
    {
        // The registry knows a DIFFERENT blueprint; the machine's action id matches nothing.
        var blueprints = OneAiPrimitive(unchecked((int)0xDEAD0999), stateSize: 24);
        var def = HostingBehaviour(BuildTwoRegionBlob(actionId: 0x0712));

        var demand = HostedOccurrenceDemandCalculator.For(def, blueprints);

        Assert.NotNull(demand);
        Assert.Equal(0, demand!.SlotCount);
        Assert.Equal(0, demand.PayloadBytes);
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ㉑ — ONE state hosting one blueprint TWICE is ONE occurrence, because it is one KEY.</b>
    ///
    /// <para>🔴 The key is <c>(host, region, state, child)</c> — an entry action and an activity action
    /// on the SAME state bound to the SAME blueprint resolve to the SAME slot. ⛔ Counting each action
    /// id separately would double the demand of every ordinary state. ⚠ It is also the thing that makes
    /// the count an occurrence count rather than a dispatch count.</para>
    /// </summary>
    [Fact]
    public void O7_R21_OneStateHostingOneBlueprintTwiceIsOneOccurrence()
    {
        const int BlueprintId = unchecked((int)0xDEAD0712);
        var blueprints = OneAiPrimitive(BlueprintId, stateSize: 24);
        ushort actionId = unchecked((ushort)BlueprintId);

        var states = new StateDef[1];
        states[0] = new StateDef
        {
            ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF,
            OnEntryActionId = actionId, ActivityActionId = actionId, OnExitActionId = actionId,
        };
        var regions = new[] { new RegionDef { ParentStateIndex = 0xFFFF, InitialStateIndex = 0 } };
        var blob = new HsmDefinitionBlob(
            new HsmDefinitionHeader { StructureHash = HostMachine, StateCount = 1, RegionCount = 1 },
            states, Array.Empty<TransitionDef>(), regions,
            Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());

        var demand = HostedOccurrenceDemandCalculator.For(HostingBehaviour(blob), blueprints);

        Assert.Equal(1, demand!.SlotCount);
        Assert.Equal(24, demand.PayloadBytes);
    }

    /// <summary>
    /// ⚠ <b>Rail ㉒ — NO MACHINE ⇒ <c>null</c>, and that is NOT the same as a zero demand.</b>
    ///
    /// <para>⛔ <c>null</c> means <i>"nobody computed one"</i> — a hand-registered behaviour, or one
    /// whose blueprints were staged by a different scan. ⭐ <c>SlotCount: 0</c> means <i>"measured, and
    /// it hosts nothing"</i>. Collapsing the two would make an unmeasured behaviour look proven.</para>
    /// </summary>
    [Fact]
    public void O7_R22_NoMachineMeansUnknownRatherThanZero()
    {
        var blueprints = OneAiPrimitive(unchecked((int)0xDEAD0712), stateSize: 24);

        var noMachine = new BehaviorDefinition
        {
            Name = "O7b3NoMachine", BrainTier = BehaviorConstants.BrainTierBTree,
        };

        Assert.Null(HostedOccurrenceDemandCalculator.For(noMachine, blueprints));
        Assert.Equal(0, HostedOccurrenceDemandCalculator
            .For(HostingBehaviour(BuildTwoRegionBlob(actionId: 0)), blueprints)!.SlotCount);
    }

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ㉓ — <c>CE-299</c> DEFECT PIN: the dispatcher table is PROCESS-GLOBAL and a
    /// re-registration is SILENT LAST-WRITER-WINS.</b>
    ///
    /// <para>⛔⛔ <b>This rail asserts the CURRENT WRONG behaviour on purpose.</b> An HSM action id is
    /// the blueprint id truncated to 16 bits, so two blueprints in DIFFERENT assemblies can collide;
    /// <c>BHU020_DuplicateDispatcherId</c> only sees one compilation, and
    /// <c>HsmActionDispatcher.RegisterAction</c> is a plain indexer assignment into a
    /// <c>private static</c> dictionary. ⇒ the second registration replaces the first, and the wrong
    /// thunk runs forever.</para>
    ///
    /// <para>⭐⭐ <b>It goes RED the day <c>CE-299</c> is fixed</b> — a throw on re-registering an id
    /// with a DIFFERENT pointer — and whoever fixes it must consciously flip this. ⚠ A SAME-pointer
    /// re-registration must stay idempotent: <c>ClearAll()</c> + re-register is the hot-reload path,
    /// and the second half of this rail pins that so the fix cannot break it.</para>
    ///
    /// <para>🔒 Filed on the user's ruling (<c>2026-09-21</c>): <i>"it is [luck], if not yet resolved it
    /// has to be filed as an issue so we do not forget."</i> ⛔ My own claim that the collision
    /// <i>"cannot occur where it would matter"</i> was assumed, not measured, and was false.</para>
    /// </summary>
    [Fact]
    public void O7_R23_ACollidingDispatcherIdSilentlyReplacesTheFirst_CE299()
    {
        const ushort Colliding = 0x0C99;
        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        try
        {
            Seen.Clear();
            Fhsm.Kernel.HsmActionDispatcher.RegisterAction(
                Colliding, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&RecordingStamp);

            // ⛔ THE DEFECT. A DIFFERENT blueprint, same low-16 id, from "another assembly" — and the
            //    registration is accepted in silence. 🔴 When CE-299 lands this must THROW.
            Fhsm.Kernel.HsmActionDispatcher.RegisterAction(
                Colliding, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&CapturingAction);

            // ⭐ And it is the SECOND one that now runs — proved by dispatching, not by reading a field.
            _capturedRegion = -99;
            var blob = BuildTwoRegionBlob(Colliding);
            var inst = new HsmInstance128();
            inst.Header.MachineId = HostMachine;
            inst.Header.Phase = InstancePhase.Entry;
            for (int r = 0; r < 4; r++) inst.ActiveLeafIds[r] = 0xFFFF;
            var ctx = 0;
            var page = default(CommandPage);
            Fhsm.Kernel.HsmKernel.Update(blob, ref inst, in ctx, 0.016f, ref page);

            Assert.Empty(Seen);                    // the FIRST thunk never ran…
            Assert.NotEqual(-99, _capturedRegion); // …and the SECOND one did.

            // ⭐⭐ THE OTHER HALF, and the fix must preserve it: re-registering the SAME pointer is the
            //    hot-reload path and must stay idempotent.
            Fhsm.Kernel.HsmActionDispatcher.RegisterAction(
                Colliding, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&CapturingAction);
        }
        finally
        {
            Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        }
    }

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ㉔ — <c>E3a</c> / <c>CE-298</c>: TWO REGIONS, TWO PARAMS. A write through one does
    /// not move the other.</b> 📄 §28.
    ///
    /// <para>🔒 <b>The user's case, verbatim (<c>2026-09-21</c>):</b> <i>"the simplest case like two
    /// actions running in two hsm regions would overwrite the params."</i> 🔴 Before <c>E3a</c> both
    /// projected <c>Params</c> over <c>BehaviorParameters[0] + 0</c> — a literal <c>0</c> — so this rail
    /// could not have been written: there was one region of bytes.</para>
    ///
    /// <para>⚠ It writes through <c>p</c> deliberately. ⛔ No shipped thunk does that today (measured:
    /// 0 of 27), and that is exactly why the defect was invisible — the hazard is in the SHAPE.</para>
    /// </summary>
    [Fact]
    public void O7_R24_TwoRegionsGetTheirOwnParams()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);
        var entity = MakeEntityWithStore(world);

        ref var wsA = ref OccurrenceWorkingState.ResolveOrAttach<DemoParams, DemoWorkingState>(
            world, entity, Key(0, 4), 0xE3A, OccurrenceKind.Hsm, out bool freshA, out DemoParams* pA);
        ref var wsB = ref OccurrenceWorkingState.ResolveOrAttach<DemoParams, DemoWorkingState>(
            world, entity, Key(1, 4), 0xE3A, OccurrenceKind.Hsm, out bool freshB, out DemoParams* pB);

        Assert.True(freshA);
        Assert.True(freshB);

        pA->Threshold = 11; pA->Flag = true;
        pB->Threshold = 22; pB->Flag = false;

        // ⭐⭐ THE RAIL. 🔴 One shared region before E3a ⇒ both would read 22/false.
        Assert.Equal(11, pA->Threshold);
        Assert.True(pA->Flag);
        Assert.Equal(22, pB->Threshold);
        Assert.False(pB->Flag);

        // ⚠ And params must not overlap the WORKING state either — the payload is [Params][State].
        wsA.Counter = 7; wsB.Counter = 9;
        Assert.Equal(11, pA->Threshold);
        Assert.Equal(22, pB->Threshold);
        Assert.Equal(7, wsA.Counter);
        Assert.Equal(9, wsB.Counter);
    }

    /// <summary>
    /// 🔴🔴 <b>Rail ㉕ — two DIFFERENT blueprints no longer type-pun each other's bytes.</b>
    ///
    /// <para>⛔⛔ <b>This is the half the user's challenge surfaced and I had missed.</b> Even READ-ONLY,
    /// two different blueprints hosted at two states both projected <b>their own <c>Params</c>
    /// type</b> over <c>BehaviorParameters[0] + 0</c> ⇒ each read the other's variable reinterpreted.
    /// ⚠ No validator guards it (searched <c>Stage2_Validate*</c>, none found).</para>
    ///
    /// <para>⭐ Distinct CHILD asset ids ⇒ distinct keys ⇒ distinct slots, sized for their own types.</para>
    /// </summary>
    [Fact]
    public void O7_R25_TwoDifferentBlueprintsDoNotTypePunEachOther()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);
        var entity = MakeEntityWithStore(world);

        var otherChild = new Guid("07000000-0000-0000-0000-0000000000e3");
        int keySmall = HsmOccurrence.KeyFor(HostMachine, Child, 0, 4);
        int keyFat   = HsmOccurrence.KeyFor(HostMachine, otherChild, 1, 4);

        OccurrenceWorkingState.ResolveOrAttach<DemoParams, DemoWorkingState>(
            world, entity, keySmall, 0xE3A, OccurrenceKind.Hsm, out _, out DemoParams* pSmall);
        OccurrenceWorkingState.ResolveOrAttach<WideParams, DemoWorkingState>(
            world, entity, keyFat, 0xE3B, OccurrenceKind.Hsm, out _, out WideParams* pWide);

        pSmall->Threshold = 0x11111111;
        pWide->A = 0x22222222; pWide->B = 0x33333333; pWide->C = 0x44444444;

        // ⭐⭐ THE RAIL. 🔴 At a shared offset 0 the wider write would have clobbered the narrower one.
        Assert.Equal(0x11111111, pSmall->Threshold);
        Assert.Equal(0x22222222, pWide->A);
        Assert.Equal(0x44444444, pWide->C);
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ㉖ — a params region survives across dispatches, and the slot is found again.</b>
    ///
    /// <para>⚠ The point of the seed is that it runs ONCE. If a second resolve reported
    /// <c>freshlyAttached</c> again, the emitter would re-seed every dispatch and a thunk could never
    /// keep anything in its params — which is the capability the whole slice exists for.</para>
    /// </summary>
    [Fact]
    public void O7_R26_TheParamsRegionPersistsAndIsSeededOnlyOnce()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);
        var entity = MakeEntityWithStore(world);

        OccurrenceWorkingState.ResolveOrAttach<DemoParams, DemoWorkingState>(
            world, entity, Key(0, 4), 0xE3A, OccurrenceKind.Hsm, out bool first, out DemoParams* p1);
        Assert.True(first);
        p1->Threshold = 1234;

        OccurrenceWorkingState.ResolveOrAttach<DemoParams, DemoWorkingState>(
            world, entity, Key(0, 4), 0xE3A, OccurrenceKind.Hsm, out bool second, out DemoParams* p2);

        // ⭐⭐ THE RAIL. ⛔ `second` true would mean the emitter re-seeds every dispatch.
        Assert.False(second);
        Assert.Equal(1234, p2->Threshold);
    }

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ㉗ — A RE-ASSIGN DROPS THE HOSTED OCCURRENCE, so its params re-seed from the NEW
    /// json.</b> 📄 §28.4.
    ///
    /// <para>⛔⛔ <b>Without this the slice is a REGRESSION, not an improvement.</b> Before <c>E3a</c>
    /// the thunk read <c>BrainBlackboard</c> LIVE, so a re-assign's new JSON took effect on the next
    /// dispatch. ⭐ Now the slot holds a COPY ⇒ if the slot survived the assign, new JSON would
    /// <b>silently stop taking effect</b> and the occurrence would run forever on the first assign's
    /// values.</para>
    ///
    /// <para>⭐ And it must be PRECISE: a slot the manifest declares is provisioned, not lazily
    /// attached, and must survive. The second half asserts that.</para>
    /// </summary>
    [Fact]
    public void O7_R27_AReassignDropsTheHostedOccurrenceButKeepsTheManifestSlot()
    {
        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);

        var manifest = new[]
        {
            new StatefulSlotInfo(unchecked((int)0xE3A00001), sizeof(DemoWorkingState), 0x55),
        };
        var entity = AssignHostingBehaviour(world, 7406, "E3aReassign", slotCount: 2,
                                            payloadEach: 32, ownManifest: manifest);

        // A hosted occurrence attaches lazily on first dispatch.
        int hostedKey = Key(0, 4);
        OccurrenceWorkingState.ResolveOrAttach<DemoParams, DemoWorkingState>(
            world, entity, hostedKey, 0xE3A, OccurrenceKind.Hsm, out _, out DemoParams* p);
        p->Threshold = 999;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, hostedKey, out _));

        // Re-assign the SAME behaviour — the shape a new JSON payload arrives in.
        world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = "E3aReassign", JsonParams = string.Empty,
        });
        world.Bus.SwapBuffers();
        _reassignSystem!.Execute(world, 0.016f);

        store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);

        // ⭐⭐ THE RAIL. The lazily-attached occurrence is GONE ⇒ the next dispatch re-seeds it.
        Assert.False(BlueprintBlackboardPartitions.TryGetSlotOffset(store, hostedKey, out _));

        // ⭐ …and the MANIFEST slot survived: it is provisioned, not lazily attached.
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, manifest[0].SlotKey, out _));
    }

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ㉘ — <c>E3b-0</c>: two states seed their params from DIFFERENT variables.</b>
    /// 📄 §28.6.
    ///
    /// <para>⛔⛔ <b>This is the case <c>E3a</c> could NOT close.</b> <c>E3a</c> gave each occurrence its
    /// own params BYTES, but every one of them seeded from <c>BehaviorParameters[0] + 0</c> — so two
    /// regions got their own COPY of the SAME authored value. ⭐ The binding is what makes them differ.</para>
    /// </summary>
    [Fact]
    public void O7_R28_TwoStatesSeedFromTheirOwnBoundVariables()
    {
        HsmParamBindings.ClearAll();
        try
        {
            var stateA = new Guid("07000000-0000-0000-0000-00000000e3a1");
            var stateB = new Guid("07000000-0000-0000-0000-00000000e3b1");

            HsmParamBindings.Register(BlobWithMetadata(stateA, stateB), new[] { (stateA, 0), (stateB, 8) });

            // ⭐⭐ THE RAIL. 🔴 Before E3b-0 both answered 0 — one variable for every region.
            Assert.Equal(0, HsmParamBindings.SeedOffsetFor(HostMachine, stateId: 1));
            Assert.Equal(8, HsmParamBindings.SeedOffsetFor(HostMachine, stateId: 2));
        }
        finally { HsmParamBindings.ClearAll(); }
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ㉙ — an UNBOUND state seeds from 0: the pre-<c>E3b-0</c> behaviour, byte-for-byte.</b>
    ///
    /// <para>⛔ Unbound is the COMMON case — every asset authored before this, and every state that
    /// hosts nothing. ⚠ A sentinel or a throw would push a decision into the emitted thunk, which must
    /// stay trivial, and would make <c>E3b-0</c> breaking instead of additive.</para>
    /// </summary>
    [Fact]
    public void O7_R29_AnUnboundStateSeedsFromZero()
    {
        HsmParamBindings.ClearAll();
        try
        {
            var stateA = new Guid("07000000-0000-0000-0000-00000000e3a2");
            HsmParamBindings.Register(BlobWithMetadata(stateA, Guid.NewGuid()), new[] { (stateA, 16) });

            Assert.Equal(16, HsmParamBindings.SeedOffsetFor(HostMachine, stateId: 1));

            // ⭐⭐ THE RAIL — an unbound state AND an unknown machine both answer 0.
            Assert.Equal(0, HsmParamBindings.SeedOffsetFor(HostMachine, stateId: 2));
            Assert.Equal(0, HsmParamBindings.SeedOffsetFor(0xDEADBEEF, stateId: 1));
        }
        finally { HsmParamBindings.ClearAll(); }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ㉚ — the binding is keyed by AUTHORING identity and resolved through the BLOB.</b>
    ///
    /// <para>⭐ The emitter bakes <c>StableId</c>s because it does not know the flattener's ordering;
    /// <c>MachineMetadata.StateStableIds</c> recovers the flat index at runtime. ⛔ A blob with NO
    /// metadata registers nothing — correctly and silently: a hand-built blob has no authoring identity
    /// to resolve, and every state then seeds from 0.</para>
    /// </summary>
    [Fact]
    public void O7_R30_ABlobWithoutMetadataRegistersNothing()
    {
        HsmParamBindings.ClearAll();
        try
        {
            HsmParamBindings.Register(
                BuildTwoRegionBlob(actionId: 0),                       // no Metadata sidecar
                new[] { (new Guid("07000000-0000-0000-0000-00000000e3a3"), 24) });

            // ⭐⭐ THE RAIL. Nothing registered, no throw, and the seed falls back to 0.
            Assert.Equal(0, HsmParamBindings.Count);
            Assert.Equal(0, HsmParamBindings.SeedOffsetFor(HostMachine, stateId: 1));
        }
        finally { HsmParamBindings.ClearAll(); }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ㉛ — END TO END, THROUGH A REAL KERNEL TICK: two parallel regions resolve two
    /// different seed offsets in ONE dispatch.</b>
    ///
    /// <para>🔒 This is the rail the whole of <c>E3b-0</c> exists for, and it is deliberately driven by
    /// <c>HsmKernel.Update</c> rather than by calling the seam twice with different numbers — the same
    /// standard rail ⑫ set. ⛔ It also pins that <c>SeedParamsOffset</c> and <c>KeyFor</c> read the SAME
    /// stamp: if they ever diverged, params would seed for one occurrence while the working state
    /// resolved for another, with no symptom until two regions disagreed.</para>
    /// </summary>
    [Fact]
    public void O7_R31_TwoRegionsResolveTwoSeedOffsetsInOneTick()
    {
        const ushort ActionId = 0x0E3B;
        HsmParamBindings.ClearAll();
        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        SeenSeeds.Clear();
        try
        {
            var stateA = new Guid("07000000-0000-0000-0000-00000000e3a5");
            var stateB = new Guid("07000000-0000-0000-0000-00000000e3b5");

            var blob = BuildTwoRegionBlob(ActionId);
            blob.Metadata = new MachineMetadata();
            blob.Metadata.StateStableIds[1] = stateA;
            blob.Metadata.StateStableIds[2] = stateB;
            HsmParamBindings.Register(blob, new[] { (stateA, 0), (stateB, 8) });

            Fhsm.Kernel.HsmActionDispatcher.RegisterAction(
                ActionId, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&RecordingSeed);

            var inst = new HsmInstance128();
            inst.Header.MachineId = HostMachine;
            inst.Header.Phase = InstancePhase.Entry;
            for (int r = 0; r < 4; r++) inst.ActiveLeafIds[r] = 0xFFFF;

            var ctx = 0;
            var page = default(CommandPage);
            Fhsm.Kernel.HsmKernel.Update(blob, ref inst, in ctx, 0.016f, ref page);

            // Non-vacuity: the parallel root really did fan out into two regions.
            Assert.Equal(2, SeenSeeds.Count);

            // ⭐⭐ THE RAIL. One asset, one tick, two regions — and TWO DIFFERENT seed offsets.
            //    🔴 Before E3b-0 both would be 0.
            Assert.Contains((1, (ushort)1, 0), SeenSeeds);
            Assert.Contains((2, (ushort)2, 8), SeenSeeds);
        }
        finally
        {
            Fhsm.Kernel.HsmActionDispatcher.ClearAll();
            HsmParamBindings.ClearAll();
        }
    }

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ㉜ — <c>E7a</c>: <see cref="IHostVariableAccess"/> HAS AN IMPLEMENTATION, and a
    /// resolver can read its HOST's variables by name.</b> 📄 §28.7.
    ///
    /// <para>⛔⛔ <b>That interface had ZERO implementers from <c>2026-08-16</c> to today</b> — every
    /// resolver received <c>null</c>. ⭐ What it was waiting for was a call site where a host actually
    /// exists, and <c>E3a</c>+<c>E3b-0</c> built one: the hosted occurrence's seed.</para>
    ///
    /// <para>⭐ NAME-keyed, never an offset — §3.4, because a cross-asset read is
    /// <c>StructureHash</c>-versioned.</para>
    /// </summary>
    [Fact]
    public void O7_R32_AResolverCanReadItsHostsVariablesByName()
    {
        HsmParamBindings.ClearAll();
        try
        {
            var blob = BlobWithMetadata(Guid.NewGuid(), Guid.NewGuid());
            HsmParamBindings.RegisterVariables(blob, new[] { ("Speed", 0, 4), ("Range", 4, 4) });

            byte* host = stackalloc byte[BehaviorConstants.MaxBehaviorParamByteSize];
            new Span<byte>(host, BehaviorConstants.MaxBehaviorParamByteSize).Clear();
            *(int*)(host + 0) = 42;
            *(int*)(host + 4) = 99;

            var access = new HsmHostVariableAccess(
                HostMachine, host, BehaviorConstants.MaxBehaviorParamByteSize);

            // ⭐⭐ THE RAIL. 🔴 Before E7a this interface had no implementation at all.
            Assert.True(access.TryRead<int>("Speed", out int speed));
            Assert.Equal(42, speed);
            Assert.True(access.TryRead<int>("Range", out int range));
            Assert.Equal(99, range);
        }
        finally { HsmParamBindings.ClearAll(); }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ㉝ — it FAILS CLOSED, and never returns a silent zero.</b>
    ///
    /// <para>🔒 §3.4: <i>"hash mismatch, absent name or type mismatch ⇒ false, and the resolver decides.
    /// ⛔ Never a silent zero."</i> ⚠ That distinction is the whole reason the interface returns
    /// <c>bool</c> instead of the value — a zero is indistinguishable from an authored zero.</para>
    /// </summary>
    [Fact]
    public void O7_R33_HostVariableAccessFailsClosed()
    {
        HsmParamBindings.ClearAll();
        try
        {
            var blob = BlobWithMetadata(Guid.NewGuid(), Guid.NewGuid());
            HsmParamBindings.RegisterVariables(blob, new[] { ("Speed", 0, 4) });

            byte* host = stackalloc byte[BehaviorConstants.MaxBehaviorParamByteSize];
            new Span<byte>(host, BehaviorConstants.MaxBehaviorParamByteSize).Clear();
            *(int*)host = 7;

            var access = new HsmHostVariableAccess(
                HostMachine, host, BehaviorConstants.MaxBehaviorParamByteSize);

            // ⭐⭐ THE RAIL — three ways to miss, and all of them say FALSE rather than 0.
            Assert.False(access.TryRead<int>("NoSuchVariable", out _));          // absent name
            Assert.False(access.TryRead<long>("Speed", out _));                  // width disagreement
            Assert.False(new HsmHostVariableAccess(0xDEADBEEF, host, BehaviorConstants.MaxBehaviorParamByteSize)
                             .TryRead<int>("Speed", out _));                     // unknown machine

            // ⭐ …and the one that DOES resolve still works, so the rail is not vacuous.
            Assert.True(access.TryRead<int>("Speed", out int ok));
            Assert.Equal(7, ok);
        }
        finally { HsmParamBindings.ClearAll(); }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ㉞ — <c>C1′</c>: the RESOLVE stage runs, and it SEES the host.</b>
    ///
    /// <para>🔒 <b>User, <c>2026-09-21</c>:</b> <i>"isn't there something like function based param
    /// resolution, allowing to take params from wherever the function/graph has access to? this would
    /// mean own resolve pass."</i> ⭐ Exactly — and this is that pass: the resolver computes a value
    /// from the HOST's variable, which is something no amount of per-occurrence STORAGE could do.</para>
    /// </summary>
    [Fact]
    public void O7_R34_TheResolveStageRunsAndSeesTheHost()
    {
        HsmParamBindings.ClearAll();
        HostedParamResolvers.ClearAll();
        try
        {
            var blob = BlobWithMetadata(Guid.NewGuid(), Guid.NewGuid());
            HsmParamBindings.RegisterVariables(blob, new[] { ("Speed", 0, 4) });

            HostedParamResolvers.Register<DemoParams>(Child, static (ref DemoParams p, Fdp.Core.EntityRepository _, Fdp.Core.Entity _, IHostVariableAccess? host) =>
            {
                // ⭐ The resolver reads its HOST — the capability the whole slice exists for.
                if (host is not null && host.TryRead<int>("Speed", out int speed)) p.Threshold = speed * 2;
            });

            byte* host = stackalloc byte[BehaviorConstants.MaxBehaviorParamByteSize];
            new Span<byte>(host, BehaviorConstants.MaxBehaviorParamByteSize).Clear();
            *(int*)host = 21;

            var p = default(DemoParams);
            bool ran = HostedParamResolvers.TryRun(
                Child, ref p, null!, default,
                new HsmHostVariableAccess(HostMachine, host, BehaviorConstants.MaxBehaviorParamByteSize));

            // ⭐⭐ THE RAIL. The resolve stage ran and computed from the host: 21 * 2.
            Assert.True(ran);
            Assert.Equal(42, p.Threshold);
        }
        finally { HostedParamResolvers.ClearAll(); HsmParamBindings.ClearAll(); }
    }

    /// <summary>
    /// ⭐⭐ <b>Rail ㉟ — NO resolver is FREE and SILENT, which §3.1 says is the common case.</b>
    ///
    /// <para>⛔ <i>"For the overwhelmingly common case the authored DTO and the usable params are the
    /// same shape, and the resolve step IS the deserialize."</i> ⚠ If a missing resolver threw or
    /// logged, every asset would pay for a feature it does not use.</para>
    ///
    /// <para>⛔⛔ …but a resolver registered for the WRONG TYPE throws, because reinterpreting one
    /// asset's bytes as another's layout is silent corruption.</para>
    /// </summary>
    [Fact]
    public void O7_R35_NoResolverIsFreeButAWrongTypedOneThrows()
    {
        HostedParamResolvers.ClearAll();
        try
        {
            var p = default(DemoParams);
            Assert.False(HostedParamResolvers.TryRun(Child, ref p, null!, default, null));
            Assert.Equal(0, p.Threshold);

            // A resolver for a DIFFERENT Params type, registered against this asset.
            HostedParamResolvers.Register<WideParams>(Child, static (ref WideParams w, Fdp.Core.EntityRepository _, Fdp.Core.Entity _, IHostVariableAccess? _) => w.A = 1);

            InvalidOperationException? thrown = null;
            try { HostedParamResolvers.TryRun(Child, ref p, null!, default, null); }
            catch (InvalidOperationException ex) { thrown = ex; }

            // ⭐⭐ THE RAIL — loud, and it names both the asset and the type.
            Assert.NotNull(thrown);
            Assert.Contains("ResolveParams", thrown!.Message);
        }
        finally { HostedParamResolvers.ClearAll(); }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ㊱ — A HAND-AUTHORED RESOLVER'S OUTPUT REACHES TWO REGIONS AS TWO DIFFERENT
    /// VALUES.</b> 📄 user question, <c>2026-09-21</c>: <i>"it must work also with hand authored action
    /// having param dto and hand authored resolver. Will it?"</i>
    ///
    /// <para>⭐⭐ <b>What the existing rails left open.</b> ㉘ and ㉛ prove two regions resolve two
    /// different <b>seed OFFSETS</b>. ⛔ Neither proves that the offset a CURATED resolver <b>writes
    /// at</b> is the offset a state <b>seeds from</b> — they are produced by two different emitters
    /// from one <c>offsetMap</c>, i.e. they agree <b>by construction</b>. ⚠ This repo has been bitten
    /// by exactly that shape before: <c>OccurrenceSlotKey</c> exists because one FNV was hand-copied
    /// into three entry points and one copy was wrong.</para>
    ///
    /// <para>⭐ So this drives the WHOLE chain: the curated resolver is registered exactly as
    /// <c>CgfCuratedBehaviorRegistrar</c> registers one and invoked exactly as
    /// <c>BehaviorIngressSystem:100</c> invokes it, then a REAL kernel tick fans out into two regions
    /// and each reads the blackboard at its own bound offset.</para>
    ///
    /// <para>⛔⛔ <b>What this does NOT prove, stated so it is not read as more than it is:</b> the
    /// curated <c>HsmActionGenerator</c> thunk still bakes a compile-time offset and does NOT call
    /// <c>SeedParamsOffset</c> — that conversion is <c>D2</c>/<c>O7</c>. ⭐ This rail pins that the
    /// SEAM carries a hand-authored resolver's values correctly, which is what makes that conversion
    /// worth doing rather than a guess.</para>
    /// </summary>
    [Fact]
    public void O7_R36_AHandAuthoredResolverSeedsTwoRegionsWithDifferentValues()
    {
        const ushort ActionId = 0x0E3D;
        const int RegionZeroValue = 111;
        const int RegionOneValue  = 222;

        HsmParamBindings.ClearAll();
        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        SeenSeededValues.Clear();
        new Span<byte>(SeedBlackboard, BehaviorConstants.MaxBehaviorParamByteSize).Clear();
        try
        {
            // ── ① the HAND-AUTHORED resolver, in the curated shape ────────────────────
            var registry = new BehaviorRegistry();
            registry.Register("HandAuthored", new BehaviorDefinition
            {
                Name      = "HandAuthored",
                BrainTier = BehaviorConstants.BrainTierHsm,
            });
            registry.RegisterResolver("HandAuthored",
                static (string json, byte* mem, int capacity, EntityRepository world, Entity self,
                        IHostVariableAccess? host) =>
                {
                    // Two packed variables — what a real curated resolver writes after doing its
                    // geo/unit conversion. The offsets are the blackboard PACKING's, not invented.
                    *(int*)(mem + 0) = RegionZeroValue;
                    *(int*)(mem + 8) = RegionOneValue;
                },
                typeof(int));

            Assert.True(registry.TryGetDefinition(BehaviorHash.FromName("HandAuthored"), out var def));

            // ── ② run it exactly as the ingress does (BehaviorIngressSystem:100) ──────
            def!.ParseParams!(string.Empty, SeedBlackboard, BehaviorConstants.MaxBehaviorParamByteSize, null!, default, host: null);

            // Non-vacuity: the resolver really wrote, and wrote two DIFFERENT values.
            Assert.Equal(RegionZeroValue, *(int*)(SeedBlackboard + 0));
            Assert.Equal(RegionOneValue,  *(int*)(SeedBlackboard + 8));

            // ── ③ bind the two states to those two variables ─────────────────────────
            var stateA = new Guid("07000000-0000-0000-0000-00000000e3c1");
            var stateB = new Guid("07000000-0000-0000-0000-00000000e3c2");

            var blob = BuildTwoRegionBlob(ActionId);
            blob.Metadata = new MachineMetadata();
            blob.Metadata.StateStableIds[1] = stateA;
            blob.Metadata.StateStableIds[2] = stateB;
            HsmParamBindings.Register(blob, new[] { (stateA, 0), (stateB, 8) });

            Fhsm.Kernel.HsmActionDispatcher.RegisterAction(
                ActionId, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&RecordingSeededValue);

            // ── ④ one REAL kernel tick ───────────────────────────────────────────────
            var inst = new HsmInstance128();
            inst.Header.MachineId = HostMachine;
            inst.Header.Phase     = InstancePhase.Entry;
            for (int r = 0; r < 4; r++) inst.ActiveLeafIds[r] = 0xFFFF;

            var ctx  = 0;
            var page = default(CommandPage);
            Fhsm.Kernel.HsmKernel.Update(blob, ref inst, in ctx, 0.016f, ref page);

            // Non-vacuity: the parallel root really did fan out into two regions.
            Assert.Equal(2, SeenSeededValues.Count);

            // ⭐⭐⭐ THE RAIL. The hand-authored resolver's TWO values land in the TWO regions —
            //    🔴 and before E3b-0 both regions would have read RegionZeroValue.
            Assert.Contains((1, RegionZeroValue), SeenSeededValues);
            Assert.Contains((2, RegionOneValue),  SeenSeededValues);
        }
        finally
        {
            Fhsm.Kernel.HsmActionDispatcher.ClearAll();
            HsmParamBindings.ClearAll();
            SeenSeededValues.Clear();
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ㊲ — <c>P1</c>: TWO PARALLEL REGIONS CALL A **REAL EMITTED ACTION** WITH PARAMS,
    /// AND EACH GETS ITS OWN.</b> 📄 user, `2026-09-21`: *"a rail that exercises the multi region
    /// parallel action call with params"*.
    ///
    /// <para>⭐⭐ <b>What this adds over ㊱.</b> ㊱ drives a RECORDING STUB — it proves the seam carries a
    /// resolver's values. ⛔ It does not prove a **compiled blueprint thunk** does the same. This one
    /// registers <c>HsmTwoRegionParamsDemo</c>'s own emitted <c>HsmActivity</c> — the real
    /// <c>ResolveOrAttach</c> → <c>SeedParamsOffset</c> → <c>TickCore(ref p, …)</c> body — and reads
    /// the two occurrences back out of the store.</para>
    ///
    /// <para>⭐⭐⭐ <b>And it is reached through the NUMERIC <c>entryActionId</c> BYPASS.</b> 📐 An HSM
    /// asset cannot name a blueprint action by FQN: <c>HsmFlattener</c> hashes the NAME while
    /// <c>CSharpEmitter</c> registers under <c>(ushort)BlueprintId</c>, which is derived from the asset
    /// GUID. ⇒ two unrelated id spaces. ⭐ The blob here stands in for an asset whose state carries
    /// <c>entryActionId = (ushort)BlueprintId</c>, which is exactly the recipe the plan records.</para>
    ///
    /// <para>🔒 <b>This is the invariant <c>BP-297</c> could never redden</b> — <c>HsmOrthogonalRegions</c>'
    /// two regions both run <c>StubIdle</c>, whose body is empty, so there were no bytes to collide.</para>
    /// </summary>
    [Fact]
    public void O7_R37_ARealEmittedBlueprintActionGivesTwoRegionsTheirOwnParams()
    {
        const int RegionZeroValue = 4242;
        const int RegionOneValue  = 7777;

        var bp = typeof(global::Hrot.AI.Behaviors.Generated.HsmTwoRegionParamsDemo_1434647B_Bp);
        ushort actionId = unchecked((ushort)
            global::Hrot.AI.Behaviors.Generated.HsmTwoRegionParamsDemo_1434647B_Bp.BlueprintId);
        Guid childAsset = global::Hrot.AI.Behaviors.Generated.HsmTwoRegionParamsDemo_1434647B_Bp.AssetId;

        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);
        var entity = MakeEntityWithStore(world);

        // ⭐ The SEED source: two ints in the packed params region, at the two offsets the two
        //   states bind. This is what ingress writes (role ③).
        // 🔴 P3-C: that region is the ROOT PARAMS OCCURRENCE SLOT now, not BrainBlackboard.
        byte* p0 = SeedRootParams(world, entity);
        *(int*)(p0 + 0) = RegionZeroValue;
        *(int*)(p0 + 4) = RegionOneValue;

        HsmParamBindings.ClearAll();
        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        var worldHandle = System.Runtime.InteropServices.GCHandle.Alloc(world);
        try
        {
            var stateA = new Guid("07000000-0000-0000-0000-00000000e3d1");
            var stateB = new Guid("07000000-0000-0000-0000-00000000e3d2");

            var blob = BuildTwoRegionBlob(actionId);
            blob.Metadata = new MachineMetadata();
            blob.Metadata.StateStableIds[1] = stateA;
            blob.Metadata.StateStableIds[2] = stateB;
            // ⭐⭐ E3b-0: state 1 seeds from offset 0, state 2 from offset 4 — DIFFERENT variables.
            HsmParamBindings.Register(blob, new[] { (stateA, 0), (stateB, 4) });

            // ⭐⭐⭐ THE REAL EMITTED THUNK, under the id the emitter registers it with.
            var activity = (delegate* unmanaged<void*, void*, HsmCommandWriter*, void>)
                bp.GetMethod("HsmActivity")!.MethodHandle.GetFunctionPointer();
            Fhsm.Kernel.HsmActionDispatcher.RegisterAction(actionId, (IntPtr)activity);

            var inst = new HsmInstance128();
            inst.Header.MachineId = HostMachine;
            inst.Header.Phase     = InstancePhase.Entry;
            for (int r = 0; r < 4; r++) inst.ActiveLeafIds[r] = 0xFFFF;

            var bridge = new Fdp.Toolkit.Behavior.Systems.HsmKernelBridge
            {
                Self         = entity,
                WorldHandle  = System.Runtime.InteropServices.GCHandle.ToIntPtr(worldHandle),
                TraceContext = null,
            };
            var page = default(CommandPage);
            Fhsm.Kernel.HsmKernel.Update(blob, ref inst, in bridge, 0.016f, ref page);

            // ── read the two occurrences back out of the store ───────────────────────
            int keyA = HsmOccurrence.KeyFor(HostMachine, childAsset, regionSlotIndex: 1, stateId: 1);
            int keyB = HsmOccurrence.KeyFor(HostMachine, childAsset, regionSlotIndex: 2, stateId: 2);

            ulong sh = global::Hrot.AI.Behaviors.Generated.HsmTwoRegionParamsDemo_1434647B_Bp.StructureHash;

            OccurrenceWorkingState.ResolveOrAttach<
                    global::Hrot.AI.Behaviors.Generated.HsmTwoRegionParamsDemo_1434647B_Bp.Params,
                    global::Hrot.AI.Behaviors.Generated.HsmTwoRegionParamsDemo_1434647B_Bp.WorkingState>(
                world, entity, keyA, sh, OccurrenceKind.Hsm, out bool freshA, out var pA);
            OccurrenceWorkingState.ResolveOrAttach<
                    global::Hrot.AI.Behaviors.Generated.HsmTwoRegionParamsDemo_1434647B_Bp.Params,
                    global::Hrot.AI.Behaviors.Generated.HsmTwoRegionParamsDemo_1434647B_Bp.WorkingState>(
                world, entity, keyB, sh, OccurrenceKind.Hsm, out bool freshB, out var pB);

            // ⛔ NON-VACUITY: the tick must already have attached both — a `true` here would mean
            //    the thunk never ran and we are reading two slots we just created ourselves.
            Assert.False(freshA, "region 1's occurrence was not attached by the tick — the thunk never ran");
            Assert.False(freshB, "region 2's occurrence was not attached by the tick — the thunk never ran");

            // ⭐⭐⭐ THE RAIL. Two parallel regions, ONE asset, ONE tick — and each holds the value its
            //    OWN bound variable seeded. 🔴 Before E3b-0 both would read RegionZeroValue.
            Assert.Equal(RegionZeroValue, pA->Value);
            Assert.Equal(RegionOneValue,  pB->Value);
        }
        finally
        {
            worldHandle.Free();
            Fhsm.Kernel.HsmActionDispatcher.ClearAll();
            HsmParamBindings.ClearAll();
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ㊳ — <c>P2</c> / <c>BP-297</c> CLOSED: a HAND-AUTHORED, DTO-BOUND HSM ACTION
    /// RUNNING IN TWO PARALLEL REGIONS GETS ITS OWN PARAMS IN EACH.</b>
    ///
    /// <para>🔴🔴 <b>This is the rail <c>BP-297</c> said could not be written.</b> Its words:
    /// <i>"the pre-written rail cannot be made to fail before the change — <c>HsmOrthogonalRegions</c>'
    /// two regions both run <c>StubIdle</c>, whose body is empty ⇒ there are no bytes to collide"</i>,
    /// and <i>"ZERO DTO-bound HSM thunks exist in any binary"</i>. ⭐ <c>P2</c> authored the subject —
    /// which is what reddened the tripwire — and converted the emitter in the same slice.</para>
    ///
    /// <para>⭐⭐ <b>What is different from ㊲.</b> ㊲ drives a compiled BLUEPRINT thunk, keyed by its
    /// asset Guid. This drives the CURATED path — <c>HsmActionGenerator</c>'s emitted thunk, keyed by
    /// <c>KeyForCurated</c> over the compound key <c>fqn@fieldOffset</c>, which is the half that still
    /// baked a blackboard offset an hour ago.</para>
    ///
    /// <para>⭐ The stored layout hash is read back out of the partition rather than restated here —
    /// ⛔ hard-coding the emitter's constant would make this rail agree with the emitter by
    /// construction instead of checking it.</para>
    /// </summary>
    [Fact]
    public void O7_R38_ACuratedDtoBoundActionGetsItsOwnParamsInTwoParallelRegions()
    {
        const string CompoundKey =
            "Hrot.AI.Behaviors.Brains.HsmTwoRegionCuratedNodes.Action_ReadRegionParams@0";
        const int RegionZeroValue = 31337;
        const int RegionOneValue  = 64206;

        var world = TestWorldFactory.Create();
        using var _w = world;
        BlueprintTierTable.RegisterAll(world);
        var entity = MakeEntityWithStore(world);

        // ⭐ The SEED source: two ints in the packed params region, at the two offsets the two
        //   states bind. This is what ingress writes (role ③).
        // 🔴 P3-C: that region is the ROOT PARAMS OCCURRENCE SLOT now, not BrainBlackboard.
        byte* p0 = SeedRootParams(world, entity);
        *(int*)(p0 + 0) = RegionZeroValue;
        *(int*)(p0 + 8) = RegionOneValue;

        HsmParamBindings.ClearAll();
        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        var worldHandle = System.Runtime.InteropServices.GCHandle.Alloc(world);
        try
        {
            // ⭐⭐⭐ The REAL generated registrar for the assembly that now has a DTO-bound subject.
            global::Hrot.AI.Behaviors.Generated.HsmActionRegistrar.RegisterAll();

            // ⚠ The emitted registrar's id for this compound key. ⛔ HsmActionKey is `internal` to
            //   the analyzer, so the rail cannot recompute it — and spelling its FNV here would
            //   duplicate the algorithm, which is the very thing OccurrenceSlotKey exists to prevent.
            // ⭐ A changed id is a FINDING, not a flake: the thunk then never runs and the
            //   "was never attached" assertions below fail with that exact message.
            const ushort actionId = 18280;

            var stateA = new Guid("07000000-0000-0000-0000-00000000e3e1");
            var stateB = new Guid("07000000-0000-0000-0000-00000000e3e2");

            var blob = BuildTwoRegionBlob(actionId);
            blob.Metadata = new MachineMetadata();
            blob.Metadata.StateStableIds[1] = stateA;
            blob.Metadata.StateStableIds[2] = stateB;
            HsmParamBindings.Register(blob, new[] { (stateA, 0), (stateB, 8) });

            var inst = new HsmInstance128();
            inst.Header.MachineId = HostMachine;
            inst.Header.Phase     = InstancePhase.Entry;
            for (int r = 0; r < 4; r++) inst.ActiveLeafIds[r] = 0xFFFF;

            var bridge = new Fdp.Toolkit.Behavior.Systems.HsmKernelBridge
            {
                Self         = entity,
                WorldHandle  = System.Runtime.InteropServices.GCHandle.ToIntPtr(worldHandle),
                TraceContext = null,
            };
            var page = default(CommandPage);
            Fhsm.Kernel.HsmKernel.Update(blob, ref inst, in bridge, 0.016f, ref page);

            // ── read both occurrences out of the partition, by the SAME key the thunk computed ──
            int keyA = HsmOccurrence.KeyForCurated(HostMachine, CompoundKey, regionSlotIndex: 1, stateId: 1);
            int keyB = HsmOccurrence.KeyForCurated(HostMachine, CompoundKey, regionSlotIndex: 2, stateId: 2);

            Assert.NotEqual(keyA, keyB);   // non-vacuity: the two regions really are two occupants

            // ⚠ The stored layout hash comes OUT of the partition — the rail must not restate the
            //   emitter's constant, or it would agree with the emitter by construction.
            uint hashA, hashB;
            ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* store = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, keyA, out _, out hashA),
                    "region 1's curated occurrence was never attached — the converted thunk did not run");
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(store, keyB, out _, out hashB),
                    "region 2's curated occurrence was never attached — the converted thunk did not run");
            }

            // ⛔⛔ The payload is [WorkingState][Params], NOT the other way round
            //    (OccurrenceWorkingState.cs:115 — "the order is load-bearing"). ⇒ resolve through the
            //    SAME helper the emitted thunk uses rather than re-deriving the offset here, which is
            //    exactly the duplication OccurrenceSlotKey exists to prevent.
            OccurrenceWorkingState.ResolveOrAttach<
                    global::Hrot.AI.Behaviors.Brains.HsmTwoRegionCuratedNodes.CuratedRegionParams,
                    HsmOccurrence.EmptyWorkingState>(
                world, entity, keyA, hashA, OccurrenceKind.Hsm, out bool freshA, out var pA);
            OccurrenceWorkingState.ResolveOrAttach<
                    global::Hrot.AI.Behaviors.Brains.HsmTwoRegionCuratedNodes.CuratedRegionParams,
                    HsmOccurrence.EmptyWorkingState>(
                world, entity, keyB, hashB, OccurrenceKind.Hsm, out bool freshB, out var pB);

            // ⛔ NON-VACUITY: the TICK attached these, not this read-back.
            Assert.False(freshA);
            Assert.False(freshB);

            // ⭐⭐⭐ THE RAIL. 🔴 Before P2 both regions read bb.BehaviorParameters[0] + 0 and
            //    would have seen RegionZeroValue twice.
            Assert.Equal(RegionZeroValue, pA->Value);
            Assert.Equal(RegionOneValue,  pB->Value);
        }
        finally
        {
            worldHandle.Free();
            Fhsm.Kernel.HsmActionDispatcher.ClearAll();
            HsmParamBindings.ClearAll();
        }
    }

    // ═══ CE-302 — THE ROOT PARAMS SLOT IS RESERVED, SURVIVES, AND DOES NOT LEAK ═══════════════
    //  📄 DESIGN_Occurrence_Scoped_Storage.md §29.8. These three are the precondition for P3-C's
    //     clean cut: once BrainBlackboard is gone the root slot is the ONLY home for root params,
    //     so "it usually works" stops being good enough.

    /// <summary>
    /// 🔴🔴🔴 <b>Rail ㊴ — <c>CE-302</c> ORDERING: an HSM behaviour's ROOT PARAMS SLOT SURVIVES its
    /// own assign.</b>
    ///
    /// <para>⛔⛔ <b>This pins a defect that <c>P3</c> step 2 shipped.</b> The root slot is attached
    /// with <c>KindOf(def)</c> — <c>OccurrenceKind.Hsm</c> on an HSM brain — and
    /// <c>DetachHostedOccurrenceSlots</c> sweeps exactly <i>"kind Hsm|Blueprint and not named by the
    /// manifest"</i>. ⇒ with the attach ABOVE the sweep, the slot was created and destroyed in the
    /// same call, every time, on every HSM behaviour.</para>
    ///
    /// <para>⭐ <b>Why nothing noticed.</b> The blackboard commit still ran, so params still arrived
    /// and no behaviour changed — the classic shape this programme keeps filing. ⚠ It becomes total
    /// params loss at the clean cut, which is why it is a rail and not a comment.</para>
    ///
    /// <para>⭐⭐ <b>RED-PROOF:</b> move the <c>P3</c> attach block back above
    /// <c>DetachHostedOccurrenceSlots</c> in <c>BehaviorIngressSystem</c> and this goes red on the
    /// <c>TryGetRoot</c> assertion, while the BTree sibling below stays green — which is the whole
    /// point: only the HSM kind is swept.</para>
    /// </summary>
    [Fact]
    public void O7_R39_AnHsmBehavioursRootParamsSlotSurvivesTheHostedSweep()
    {
        using var world = CreateWorld();

        Entity entity = AssignParamsBehaviour(
            world, behaviourId: 7439, name: "Ce302HsmRoot",
            brainTier: BehaviorConstants.BrainTierHsm, threshold: 31337);

        // ⭐⭐⭐ THE RAIL. 🔴 Before the ordering fix this was false on every HSM brain.
        Assert.True(RootParamsAccess.TryGetRoot<DemoParams>(world, entity, out DemoParams* p));
        Assert.Equal(31337, p->Threshold);

        // ⛔ NON-VACUITY: the same behaviour on a BTree brain was ALWAYS fine — the sweep does not
        //    look at OccurrenceKind.BTree — so a rail that only covered BTree would have proved
        //    nothing about the defect.
        Entity bt = AssignParamsBehaviour(
            world, behaviourId: 7440, name: "Ce302BTreeRoot",
            brainTier: BehaviorConstants.BrainTierBTree, threshold: 4242);
        Assert.True(RootParamsAccess.TryGetRoot<DemoParams>(world, bt, out DemoParams* q));
        Assert.Equal(4242, q->Threshold);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Rail ㊵ — <c>CE-302</c> SIZING: a behaviour whose HOSTED demand exactly fills the
    /// smallest tier still gets room for its ROOT PARAMS.</b>
    ///
    /// <para>🔴 <b>The failure without it is silent.</b> The root attach runs AFTER provisioning, so
    /// a full store simply returns <c>null</c>; today the blackboard covers for it, and after the
    /// clean cut the entity runs on a zero-filled params region. ⇒ the demand has to reserve
    /// <b>one slot plus the params extent</b> BEFORE the tier is chosen.</para>
    ///
    /// <para>⭐⭐ <b>RED-PROOF:</b> drop <c>rootParamsCost</c> from either provisioning path and this
    /// goes red — the entity lands on the 256 tier with all three slots spent on hosted occurrences
    /// and <c>TryGetRoot</c> returns false.</para>
    ///
    /// <para>⚠ <b>Three hosted occurrences is the exact ceiling</b>: <c>MaxSlots</c> is 3 on the 256
    /// tier (rail ⑯ measures the same edge from the other side), so the fourth slot the root params
    /// need cannot come from anywhere but a bigger tier.</para>
    /// </summary>
    [Fact]
    public void O7_R40_TheTierIsSizedForTheRootParamsSlotToo()
    {
        using var world = CreateWorld();

        Entity entity = AssignParamsBehaviour(
            world, behaviourId: 7441, name: "Ce302TierDemand",
            brainTier: BehaviorConstants.BrainTierHsm, threshold: 909,
            hostedSlots: 3, hostedPayloadEach: 24);

        // ⛔ NON-VACUITY: the hosted demand really did claim the smallest tier's three slots.
        Assert.True(OccurrenceStoreAccess.GetStoreSize(world, entity) > 256);

        // ⭐⭐⭐ THE RAIL — the root slot fits, and carries the parsed value.
        Assert.True(RootParamsAccess.TryGetRoot<DemoParams>(world, entity, out DemoParams* p));
        Assert.Equal(909, p->Threshold);
    }

    /// <summary>
    /// ⚠ <b>Rail ㊶ — <c>CE-302</c> LEAK: re-assigning to a DIFFERENT behaviour does not strand the
    /// old root params slot.</b>
    ///
    /// <para>⛔ On a BTree brain the hosted sweep cannot see a root slot (kind <c>BTree</c>), so
    /// without an explicit detach each behaviour change leaks one slot — and <c>MaxSlots</c> is 3 on
    /// the 256 tier. ⇒ the fourth assign of a long-lived entity would silently lose its params.</para>
    ///
    /// <para>⭐⭐ <b>RED-PROOF:</b> delete the <c>RootParamsAccess.DetachRoot</c> call in the ingress
    /// and the slot count climbs with every assign instead of staying at one.</para>
    /// </summary>
    [Fact]
    public void O7_R41_ReassigningDoesNotLeakThePreviousRootParamsSlot()
    {
        using var world = CreateWorld();

        var registry = new BehaviorRegistry();
        var sys = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BehaviorState());

        for (int i = 0; i < 4; i++)
        {
            string name = "Ce302Churn" + i;
            registry.Register(7450 + i, name, ParamsBehaviour(name, BehaviorConstants.BrainTierBTree));
            world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
            {
                Entity = entity, BehaviorName = name,
                JsonParams = "{\"Threshold\":" + (100 + i) + "}",
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        Assert.True(store != null);

        // ⭐⭐⭐ THE RAIL. 🔴 Without DetachRoot this is 4 and the last assign had nowhere to land.
        // ⭐⭐ O7c-② / CE-319 — 1 → 2: the current behaviour now holds a root PARAMS slot AND a root
        //    STATE slot. 📐 The rail keeps its whole force: four assigns still leave TWO slots, so the
        //    previous behaviour's pair is still being reclaimed. ⛔ Had RootStateAccess.DetachRoot been
        //    missing or mis-ordered this would read 5, not 2 — which is exactly what this rail is for,
        //    and it is why the number is re-baselined rather than the assertion relaxed.
        Assert.Equal(2, BlueprintBlackboardPartitions.GetSlotCount(store));

        // ⭐ …and it is the CURRENT behaviour's slot, not a survivor of an earlier one.
        Assert.True(RootParamsAccess.TryGetRoot<DemoParams>(world, entity, out DemoParams* p));
        Assert.Equal(103, p->Threshold);
    }

    /// <summary>A behaviour that parses <see cref="DemoParams"/> into its root params region.</summary>
    private static BehaviorDefinition ParamsBehaviour(string name, byte brainTier)
        => new()
        {
            Name                 = name,
            BrainTier            = brainTier,
            StatefulWorkingSlots = Array.Empty<StatefulSlotInfo>(),
            BlackboardLayoutType = typeof(DemoParams),
            ParseParams          = BehaviorParams.FromJson<DemoParams>(),
        };

    /// <summary>
    /// Registers a params-carrying behaviour, optionally with a hosted demand, and assigns it through
    /// the REAL ingress — so the ordering, the sweep and the tier selection are production code.
    /// </summary>
    private static Entity AssignParamsBehaviour(
        EntityRepository world, int behaviourId, string name, byte brainTier, int threshold,
        int hostedSlots = 0, int hostedPayloadEach = 0)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BehaviorState());

        var registry = new BehaviorRegistry();
        var sys = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);
        registry.Register(behaviourId, name, ParamsBehaviour(name, brainTier));

        if (hostedSlots > 0)
            registry.RegisterHostedOccurrenceDemand(
                name, HostedOccurrenceDemand.Of(Enumerable.Repeat(hostedPayloadEach, hostedSlots)));

        world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = name,
            JsonParams = "{\"Threshold\":" + threshold + "}",
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);
        return entity;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>A blob whose metadata maps flat states 1 and 2 to two authoring ids.</summary>
    private static HsmDefinitionBlob BlobWithMetadata(Guid stateOne, Guid stateTwo)
    {
        var blob = BuildTwoRegionBlob(actionId: 0);
        blob.Metadata = new MachineMetadata();
        blob.Metadata.StateStableIds[1] = stateOne;
        blob.Metadata.StateStableIds[2] = stateTwo;
        return blob;
    }

    private static readonly List<(int Region, ushort State, int SeedOffset)> SeenSeeds = new();

    // ── rail ㊱'s harness: a stand-in for BrainBlackboard.BehaviorParameters ──────────
    //
    // ⭐ A pinned native buffer, because the recording thunk is a `static` function pointer and
    //   cannot close over a stack local. Its LIFETIME is the test class, and every rail that uses
    //   it clears it first, so no rail inherits another's bytes.
    private static readonly byte* SeedBlackboard =
        (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(
            (nuint)BehaviorConstants.MaxBehaviorParamByteSize);

    private static readonly List<(int Region, int Value)> SeenSeededValues = new();

    /// <summary>
    /// ⭐ What an emitted thunk does on <c>freshlyAttached</c>: ask for THIS occurrence's seed offset,
    /// then read the params from the blackboard there. ⛔ The offset is never baked here — that is the
    /// whole point of the rail.
    /// </summary>
    private static void RecordingSeededValue(void* instance, void* context, HsmCommandWriter* writer)
    {
        int offset = HsmOccurrence.SeedParamsOffset(instance, writer);
        SeenSeededValues.Add((writer->OccurrenceRegionSlotIndex, *(int*)(SeedBlackboard + offset)));
    }

    /// <summary>Captures exactly what an emitted HSM thunk's seed computes, per dispatch.</summary>
    private static void RecordingSeed(void* instance, void* context, HsmCommandWriter* writer)
        => SeenSeeds.Add((writer->OccurrenceRegionSlotIndex, writer->OccurrenceStateId,
                          HsmOccurrence.SeedParamsOffset(instance, writer)));

    private static Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem? _reassignSystem;


    // ═══ O7c-④c — THE SAME TWO-REGION MACHINE, THROUGH THE REAL SYSTEM ════════════════
    //
    // 🔒 User, `2026-09-23`: *"You did a test for multi region hsm calling actions, didnt you?
    //    Can you use that for testing hsms?"* — ⭐ yes, and this is it.
    //
    // ⛔⛔ WHAT EVERY RAIL ABOVE STOPS SHORT OF. O7_R12/R31/R36/R37/R38 all drive
    //    `HsmKernel.Update` on a STACK-LOCAL `HsmInstance128`, with `context` an `int`. ⇒ they
    //    prove the KERNEL fans out into two regions and stamps two occurrences — and they say
    //    nothing about whether a SYSTEM can reach that instance, because in those rails the
    //    instance is a local variable the test already holds a pointer to.
    // ⭐⭐ After O7c-④ the instance lives in an OCCURRENCE SLOT on a real entity, discovered by the
    //    tier walk. That is a whole chain those rails cannot see: entity → tier query →
    //    BrainTier → RootHsmAccess → Update(ptr, size) → two regions → two hosted slots.

    /// <summary>The action the two-region blob dispatches, in the shape a REAL emitted thunk has.</summary>
    /// <remarks>
    /// ⭐⭐ <b>It recovers the world from the BRIDGE, exactly as every generated thunk does</b>
    /// (<c>GCHandle.FromIntPtr(bridge-&gt;WorldHandle)</c>), and attaches its occurrence through
    /// <c>OccurrenceWorkingState.ResolveOrAttach</c>. ⛔ That is what makes this a test of the SYSTEM
    /// path rather than of the kernel: <c>O7_R12</c>'s stub takes an <c>int</c> context and could not
    /// reach an entity at all.
    /// </remarks>
    private static void SlotResidentRegionAction(void* instance, void* context, HsmCommandWriter* writer)
    {
        var bridge = (Fdp.Toolkit.Behavior.Systems.HsmKernelBridge*)context;
        var repo   = (EntityRepository)System.Runtime.InteropServices.GCHandle
                         .FromIntPtr(bridge->WorldHandle).Target!;

        int    region = writer->OccurrenceRegionSlotIndex;
        ushort state  = writer->OccurrenceStateId;
        int    key    = Key(region, state);

        // The production shape: one keyed occurrence per (region, state), params + working state.
        OccurrenceWorkingState.ResolveOrAttach<DemoParams, DemoWorkingState>(
            repo, bridge->Self, key, 0x04C0, OccurrenceKind.Hsm, out _, out DemoParams* p);
        p->Threshold = 1000 + region;

        SystemDriven.Add((region, state, key));
    }

    private static readonly List<(int Region, ushort State, int Key)> SystemDriven = new();

    /// <summary>
    /// ⭐⭐⭐ <b><c>O7_R48</c> — THE TWO-REGION MACHINE RUNS THROUGH <c>BrainTickSystem</c>, WITH ITS
    /// INSTANCE IN AN OCCURRENCE SLOT.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.17.
    ///
    /// <para>🔒 The rail the user asked for when they asked whether the multi-region test could be
    /// reused for HSMs. ⭐ It reuses <see cref="BuildTwoRegionBlob"/> unchanged — the same machine
    /// <c>O7_R12</c> and <c>O7_R37</c> drive — and changes only HOW it is reached.</para>
    ///
    /// <para>⭐⭐⭐ <b>AND IT IS THE FIRST MACHINE IN THIS SUITE THAT GETS ITS TRUE TIER.</b>
    /// The retired <c>BrainHsm128</c> component was <b>128 bytes for every machine whatever
    /// SelectTier said</b> — the width was a property of a TYPE. ⇒ before <c>O7c</c>-④ this machine
    /// could not have been given the instance the kernel's own policy asks for, on any entity, at
    /// all.</para>
    ///
    /// <para>⚠⚠ <b>"TRUE TIER" IS THE CLAIM; THE NUMBER IS NOT.</b> 📄 <c>CE-325</c>, §31.24. When
    /// this rail was written <c>SelectTier</c> answered <b>256</b> here, and that was quoted in this
    /// comment as though it were the point. 🔴 It was a DEFECT: <c>SelectTier</c>'s own gate stopped
    /// at 2 regions while <c>HsmInstance128</c>'s layout holds <b>4</b>, so a 3-region machine that
    /// fits 128 exactly was sent to 256. ⇒ the answer is now <b>128</b>, and what this rail is about
    /// — the machine running through the REAL system, instance slot-resident, three occurrences on
    /// one entity — never depended on which number it is.</para>
    ///
    /// <para>⚠ <b>Non-vacuity is asserted, not assumed</b>: the parallel root must really fan out
    /// (<c>activeLeafIds == [0,1,2]</c>) and BOTH dispatches must land, or the rest of the rail would
    /// pass on a machine that never ran.</para>
    /// </summary>
    [Fact]
    public void O7_R48_TheTwoRegionMachineRunsThroughTheRealSystem_SlotResident_O7c4c()
    {
        const ushort ActionId = 0x04C1;
        const int    DocId    = 0x04C2;

        SystemDriven.Clear();
        Fhsm.Kernel.HsmActionDispatcher.ClearAll();
        Fhsm.Kernel.HsmActionDispatcher.RegisterAction(
            ActionId,
            (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&SlotResidentRegionAction);
        try
        {
            var world = TestWorldFactory.Create();
            using var _w = world;

            var blob = BuildTwoRegionBlob(ActionId);

            // ⭐ GUARD: the premise of the whole rail — this machine gets a tier chosen from its own
            //   shape, not a hard-coded 128.
            // ⚠ CE-325 (2026-09-23): this asserted 256 and now asserts 128, and the CLAIM is
            //   untouched. Three regions fit HsmInstance128's FOUR leaf slots; 256 was only ever
            //   selected because SelectTier's own gate stopped at 2 regions while the layout held 4.
            //   ⭐ What this rail is about — the machine running through the REAL system with its
            //   instance slot-resident, and three occurrences on one entity — does not depend on
            //   which tier that is.
            Assert.Equal(128, Fhsm.Kernel.HsmInstanceManager.SelectTier(blob));

            var registry = new BehaviorRegistry();
            registry.Register(DocId, "TwoRegionSlotResident", new BehaviorDefinition
            {
                Name          = "TwoRegionSlotResident",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });

            var entity = world.CreateEntity();
            world.AddComponent(entity, new Components.BehaviorState
            {
                ActiveBehaviorHash = DocId,
                BrainTier          = BehaviorConstants.BrainTierHsm,
                InstanceId         = 1,
            });

            // ⭐⭐ THE ONLY SETUP. No component, no hand-held pointer — ingress's provisioner puts the
            //    instance in a slot and sizes it from the MACHINE.
            Assert.True(RootHsmAccess.EnsureRootInstance(world, entity, DocId, blob));

            Assert.True(RootHsmAccess.TryGetInstance(world, entity, out byte* inst, out int size));
            // ⭐ The width the MACHINE asks for, provisioned into the slot.
            // ⚠ CE-325 (2026-09-23): 256 → 128, and this is the THIRD of three sites in this rail
            //   that spelled the old answer (the SelectTier guard, this size, the leaf capacity
            //   below). 📐 Three regions fit HsmInstance128's FOUR leaf slots; the 256 came from
            //   SelectTier's gate stopping at 2 regions while the layout held 4. 📄 §31.24.
            // ⛔ What would be WRONG is 128 arriving here because a retired component was 128 bytes
            //   wide — that is the thing O7c-④ removed, and it is why this asserts at all.
            Assert.Equal(128, size);

            // ── ONE tick of the REAL system. Nothing here names the entity. ──────────────
            new Fdp.Toolkit.Behavior.Systems.BrainTickSystem(registry).Execute(world, 0.016f);

            // ⭐ NON-VACUITY: the parallel root fanned out, so the machine genuinely ran.
            ushort* leaves = Fhsm.Kernel.HsmKernel.GetActiveLeafIds(inst, size, out int regionCount);
            Assert.Equal(4, regionCount);              // the 128 tier's leaf capacity (CE-325)
            Assert.Equal((ushort)0, leaves[0]);
            Assert.Equal((ushort)1, leaves[1]);
            Assert.Equal((ushort)2, leaves[2]);

            // ⭐⭐ THE RAIL, HALF ONE: both regions dispatched in ONE system tick, each stamped with
            //    its own (region, state) — and therefore keyed to its own occurrence.
            Assert.Equal(2, SystemDriven.Count);
            Assert.Contains(SystemDriven, x => x.Region == 1 && x.State == 1);
            Assert.Contains(SystemDriven, x => x.Region == 2 && x.State == 2);
            Assert.NotEqual(SystemDriven[0].Key, SystemDriven[1].Key);

            // ⭐⭐⭐ THE RAIL, HALF TWO — AND THIS IS THE CAPABILITY THE PROGRAMME EXISTS FOR.
            //    The store now holds THREE occurrences on ONE entity: the host machine's own
            //    instance plus one per region. 🔒 A component is addressed by its TYPE, so the
            //    BrainHsm128 world could hold exactly ONE of these; a keyed slot is what makes
            //    "several occurrences on one entity" expressible at all.
            byte* store = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
            Assert.True(store != null);
            Assert.Equal(3, BlueprintBlackboardPartitions.GetSlotCount(store));

            // ⚠ And the two regions' params really are separate bytes, read back from the store
            //   rather than from the pointers the action happened to hold.
            foreach (var (region, state, key) in SystemDriven)
            {
                OccurrenceWorkingState.ResolveOrAttach<DemoParams, DemoWorkingState>(
                    world, entity, key, 0x04C0, OccurrenceKind.Hsm, out bool fresh, out DemoParams* p);
                Assert.False(fresh);                       // found, not re-created
                Assert.Equal(1000 + region, p->Threshold); // each kept ITS OWN value
            }
        }
        finally
        {
            Fhsm.Kernel.HsmActionDispatcher.ClearAll();
            SystemDriven.Clear();
        }
    }

    private struct DemoParams { public int Threshold; public bool Flag; }
    private struct WideParams { public int A; public int B; public int C; }

    private static Dictionary<int, Fdp.Toolkit.Blueprints.BlueprintDefinition> OneAiPrimitive(
        int blueprintId, int stateSize)
        => new()
        {
            [blueprintId] = new Fdp.Toolkit.Blueprints.BlueprintDefinition
            {
                Name          = "HostedChild",
                Kind          = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.AiPrimitive,
                StructureHash = 0xABCD,
                StateSize     = stateSize,
                AssetId       = Child,
            },
        };

    private static BehaviorDefinition HostingBehaviour(HsmDefinitionBlob blob)
        => new()
        {
            Name = "O7b3DerivedDemand",
            BrainTier = BehaviorConstants.BrainTierHsm,
            HsmDefinition = blob,
        };

    private struct FatWorkingState { public byte Head; public fixed byte Bulk[511]; }

    /// <summary>
    /// Registers an HSM behaviour whose machine hosts <paramref name="slotCount"/> occurrences of
    /// <see cref="Child"/>, assigns it through the REAL ingress, and returns the entity.
    ///
    /// <para>⭐ The hosted states are built so the demand is discoverable from the blob alone: one
    /// action id per occurrence, each resolving to the same child asset through the blueprint
    /// registry.</para>
    /// </summary>
    private static Entity AssignHostingBehaviour(
        EntityRepository world, int behaviourId, string name, int slotCount, int payloadEach,
        StatefulSlotInfo[]? ownManifest = null)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BehaviorState());

        var registry = new BehaviorRegistry();
        var sys = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);

        registry.Register(behaviourId, name, new BehaviorDefinition
        {
            Name = name,
            BrainTier = BehaviorConstants.BrainTierHsm,
            StatefulWorkingSlots = ownManifest ?? Array.Empty<StatefulSlotInfo>(),
        });

        // ⭐ The demand is an OVERLAY on the registry, not a field on the definition — the generated
        //   registrar that authors the topology must not know about blueprints (§27.7).
        registry.RegisterHostedOccurrenceDemand(
            name, HostedOccurrenceDemand.Of(Enumerable.Repeat(payloadEach, slotCount)));

        // ⭐ Rail ㉗ re-assigns through the SAME system instance, which is what a live re-assign is.
        _reassignSystem = sys;

        world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = name, JsonParams = string.Empty,
        });
        world.Bus.SwapBuffers();
        sys.Execute(world, 0.016f);
        return entity;
    }

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
