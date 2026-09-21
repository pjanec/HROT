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
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BrainBlackboard());
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BrainBTreeState());

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
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BrainBlackboard());

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

    /// <summary>Captures exactly what an emitted HSM thunk's seed computes, per dispatch.</summary>
    private static void RecordingSeed(void* instance, void* context, HsmCommandWriter* writer)
        => SeenSeeds.Add((writer->OccurrenceRegionSlotIndex, writer->OccurrenceStateId,
                          HsmOccurrence.SeedParamsOffset(instance, writer)));

    private static Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem? _reassignSystem;

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
        world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BrainBlackboard());

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
