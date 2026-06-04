using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// MVE-BATCH-07 (HOT-RELOAD): proves the full hot-reload loop headlessly through the
/// REAL <see cref="BlueprintTickSystem"/> + <see cref="AiHotReloadCoordinator"/> staging path
/// (the same path invoked by <see cref="Hrot.Blueprints.Editor.Reload.QuickReloadService.TriggerAsync"/>).
///
/// <para>
/// All three tests use a single blueprint / single entity with no re-attach across reload.
/// The hot-reload is performed by calling <c>coordinator.ApplyQuickReload</c> directly,
/// which is exactly the code path <c>QuickReloadService.TriggerAsync</c> calls internally
/// (QuickReloadService → coordinator.ApplyQuickReload → BlueprintRegistry.CommitStaging →
/// BlueprintTickSystem re-resolves next tick).
/// </para>
///
/// <para>
/// <b>VERIFY-FIRST — StructureHash independence (confirmed):</b>
/// <c>StructureHashComputation.Compute</c>
/// (<c>Hrot.Blueprints.Compiler/Compiler/Lowering/StructureHashComputation.cs:9-17</c>)
/// hashes only <c>asset.Dispatch</c>, <c>asset.Parameters</c>, <c>asset.WorkingState</c>,
/// and <c>asset.Variables</c> (names, types, offsets, sizes). The Graphs/Tick body is
/// NOT included. Consequence: two definitions with identical variable layout but different
/// Tick delegates share the same StructureHash, so a hot-reload with only a behavior change
/// takes the state-preserved path (no ResetSlot / InitDefault).
/// </para>
///
/// <para>
/// <b>VERIFY-FIRST — BlueprintAssetBuilder increment expressiveness (confirmed):</b>
/// <c>GraphBuilder.SetVariable(string variableName, string valueExpression)</c>
/// (<c>Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilder.cs:231-237</c>) stores only
/// <c>VariableId = variableName</c>; the <c>valueExpression</c> string is not forwarded to
/// the compiler pipeline. There is no built-in <c>AddNode</c> or <c>GetVariableNode</c> in
/// the builder, so the compiler cannot generate a Count++ increment from graph nodes alone.
/// Consequence: the behavior-change observable is achieved by hot-swapping hand-crafted
/// <see cref="BlueprintDefinition"/> delegates (incrementing by different deltas), committed
/// via <c>coordinator.ApplyQuickReload</c> — the identical staging path QuickReloadService uses.
/// The fallback from the batch instructions (empty-Tick v1 → increment-Tick v2) applies when
/// a Roslyn-compiled pipeline is required, but the hand-crafted approach gives richer
/// observables (Count advances at two different rates across the reload boundary).
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class BlueprintHotReloadMveTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared asset identity
    // ─────────────────────────────────────────────────────────────────────────

    // Single blueprint identity used across all three test cases.
    private static readonly Guid AssetGuid = new Guid("07000007-0000-0000-0000-000000000001");
    private static readonly int  BpId      = BlueprintIdHash.Compute(AssetGuid);

    // A stable StructureHash for v1/v2 (same field layout — only Tick behavior differs).
    // v3 uses a different hash (extra field added).
    private const ulong HashAB = 0x0700000700000001UL;
    private const ulong HashC  = 0x0700000700000002UL;  // different field layout

    // ─────────────────────────────────────────────────────────────────────────
    // Per-slot state layout (used by all three definitions)
    // ─────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct StateV1V2
    {
        public BlueprintLatentCursor Cursor;
        public int Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateV3
    {
        public BlueprintLatentCursor Cursor;
        public int Count;
        public int Extra;  // extra field → different StructureHash
    }

    private static int OffsetCount => Unsafe.SizeOf<BlueprintLatentCursor>();

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: produce a hot-reload coordinator sharing fixture.Registry
    // ─────────────────────────────────────────────────────────────────────────

    private static AiHotReloadCoordinator MakeCoordinator(BlueprintRegistry registry)
        => new AiHotReloadCoordinator(new BehaviorRegistry(), registry,
               new AiHotReloadCoordinatorOptions());

    // ─────────────────────────────────────────────────────────────────────────
    // Definition factories
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>v1: increments Count by 1 per tick. HashAB.</summary>
    private static BlueprintDefinition MakeDefV1() => new BlueprintDefinition
    {
        Name          = "HotReloadTest",
        Kind          = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
        StructureHash = HashAB,
        StateSize     = Unsafe.SizeOf<StateV1V2>(),
        InitDefault   = bytes => bytes.Clear(),
        Tick          = (bytes, _, _, _, _, _, _) =>
        {
            ref var s = ref Unsafe.As<byte, StateV1V2>(ref MemoryMarshal.GetReference(bytes));
            s.Count += 1;
        },
        StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
        {
            ["Count"] = new BlueprintFieldDescriptor(
                "Count", typeof(int), OffsetBytes: OffsetCount, SizeBytes: sizeof(int), CategoryOrEmpty: ""),
        },
    };

    /// <summary>v2: increments Count by 2 per tick. Same HashAB (same field layout).</summary>
    private static BlueprintDefinition MakeDefV2() => new BlueprintDefinition
    {
        Name          = "HotReloadTest",
        Kind          = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
        StructureHash = HashAB,  // SAME hash as v1 → state preserved on reload
        StateSize     = Unsafe.SizeOf<StateV1V2>(),
        InitDefault   = bytes => bytes.Clear(),
        Tick          = (bytes, _, _, _, _, _, _) =>
        {
            ref var s = ref Unsafe.As<byte, StateV1V2>(ref MemoryMarshal.GetReference(bytes));
            s.Count += 2;
        },
        StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
        {
            ["Count"] = new BlueprintFieldDescriptor(
                "Count", typeof(int), OffsetBytes: OffsetCount, SizeBytes: sizeof(int), CategoryOrEmpty: ""),
        },
    };

    /// <summary>v3: adds an Extra field → DIFFERENT StructureHash → hard reset on reload.</summary>
    private static BlueprintDefinition MakeDefV3() => new BlueprintDefinition
    {
        Name          = "HotReloadTest",
        Kind          = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
        StructureHash = HashC,  // DIFFERENT hash → hard reset (ResetSlot + InitDefault)
        StateSize     = Unsafe.SizeOf<StateV3>(),
        InitDefault   = bytes => bytes.Clear(),
        Tick          = (bytes, _, _, _, _, _, _) =>
        {
            ref var s = ref Unsafe.As<byte, StateV3>(ref MemoryMarshal.GetReference(bytes));
            s.Count += 1;
        },
        StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
        {
            ["Count"] = new BlueprintFieldDescriptor(
                "Count", typeof(int), OffsetBytes: OffsetCount, SizeBytes: sizeof(int), CategoryOrEmpty: ""),
            ["Extra"] = new BlueprintFieldDescriptor(
                "Extra", typeof(int),
                OffsetBytes: OffsetCount + sizeof(int),
                SizeBytes:   sizeof(int),
                CategoryOrEmpty: ""),
        },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Commit a definition as a hot-reload (mimics QuickReloadService.TriggerAsync
    // → coordinator.ApplyQuickReload → CommitStaging).
    // ─────────────────────────────────────────────────────────────────────────

    private static void HotReload(
        AiHotReloadCoordinator coordinator,
        int blueprintId,
        BlueprintDefinition def)
    {
        var staging = new BlueprintRegistryStaging();
        staging.Add(blueprintId, def);
        // Throwaway collectible ALC — mirrors what QuickReloadService creates after Roslyn compile.
        var alc = new AssemblyLoadContext($"HotReloadTest_{Guid.NewGuid():N}", isCollectible: true);
        coordinator.ApplyQuickReload(alc, new BehaviorRegistry(), staging);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Asset stub (identity only — runtime behavior comes from the registered def)
    // ─────────────────────────────────────────────────────────────────────────

    private static BlueprintAsset MakeAsset() => new BlueprintAsset
    {
        AssetId  = AssetGuid,
        Name     = "HotReloadTest",
        Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1a — behavior change + state preserved (same StructureHash)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pumps 3 frames with v1 (+1/frame) → Count = 3.
    /// Hot-reloads v2 (+2/frame, same StructureHash = state preserved).
    /// Pumps 4 more frames → Count = 3 + 8 = 11.
    ///
    /// Proves: (a) new Tick code is live after reload (delta changed from +1 to +2),
    ///         (b) pre-reload state is preserved (Count continued from 3, not reset to 0).
    ///
    /// If state were reset (hard-reset path), Count after 4 frames = 8 (not 11).
    /// If reload failed (still v1 Tick), Count after 4 frames = 7 (not 11).
    /// </summary>
    [Fact]
    public void HotReload_BehaviorChange_StatePreserved_SameStructureHash()
    {
        using var fixture    = new BlueprintTestFixture();
        var       coordinator = MakeCoordinator(fixture.Registry);
        var       asset       = MakeAsset();
        var       harness     = new BlueprintRunHarness(fixture);

        // ── Register v1 (delta +1) and attach to entity ──────────────────────
        HotReload(coordinator, BpId, MakeDefV1());

        var entity = harness.SpawnAndAttach(asset);
        Assert.Equal(0, harness.ReadIntField(entity, asset, "Count"));

        // ── Run 3 frames with v1 ──────────────────────────────────────────────
        harness.Pump(3);
        Assert.Equal(3, harness.ReadIntField(entity, asset, "Count"));

        // ── Hot-reload to v2 (delta +2, same StructureHash = state preserved) ─
        // Mimics QuickReloadService.TriggerAsync(v2Asset) → coordinator.ApplyQuickReload.
        HotReload(coordinator, BpId, MakeDefV2());

        // State preserved: Count is still 3 (not 0) immediately after reload.
        Assert.Equal(3, harness.ReadIntField(entity, asset, "Count"));

        // ── Run 4 more frames with v2 (no re-attach) ─────────────────────────
        harness.Pump(4);

        // Count = pre-reload(3) + v2-rate(+2) * 4-frames = 3 + 8 = 11.
        // If hard-reset occurred: 0 + 8 = 8 (wrong).
        // If v1 still running:    3 + 4 = 7 (wrong).
        Assert.Equal(11, harness.ReadIntField(entity, asset, "Count"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1b — structural change → hard-reset reconciliation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pumps 5 frames with v1 (+1/frame) → Count = 5.
    /// Hot-reloads v3 (extra field → different StructureHash).
    /// BlueprintTickSystem detects StructureHash mismatch → ResetSlot + InitDefault → hard reset.
    /// After 2 more frames with v3 (+1/frame): Count = 2 (reset to 0 then incremented twice).
    ///
    /// Proves: <see cref="BlueprintTickSystem.TickTier_1024"/> reconciliation (line 87-99)
    ///         fires and zeroes state when StructureHash changes.
    /// </summary>
    [Fact]
    public void HotReload_StructuralChange_HardResets_State()
    {
        using var fixture     = new BlueprintTestFixture();
        var       coordinator = MakeCoordinator(fixture.Registry);
        var       asset       = MakeAsset();
        var       harness     = new BlueprintRunHarness(fixture);

        // ── Register v1 and run 5 frames ──────────────────────────────────────
        HotReload(coordinator, BpId, MakeDefV1());
        var entity = harness.SpawnAndAttach(asset);
        harness.Pump(5);
        Assert.Equal(5, harness.ReadIntField(entity, asset, "Count"));

        // ── Hot-reload v3 (different StructureHash) ───────────────────────────
        HotReload(coordinator, BpId, MakeDefV3());

        // ── Pump 1 frame — this tick triggers the hard-reset reconciliation ───
        harness.Pump(1);

        // After hard reset + 1 tick: Count = 0 (InitDefault) + 1 (v3 tick) = 1.
        Assert.Equal(1, harness.ReadIntField(entity, asset, "Count"));

        // ── Pump 1 more frame ─────────────────────────────────────────────────
        harness.Pump(1);

        // After 2 ticks total post-reload: Count = 2 (hard-reset proved; pre-reload 5 is gone).
        Assert.Equal(2, harness.ReadIntField(entity, asset, "Count"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1c — observe survives reload (CaptureLiveState after hot-reload)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After the 1a-style soft reload, <c>session.CaptureLiveState(entity, assetId)</c>
    /// returns the live post-reload Count value — proving the DebugMap re-registered on
    /// reload keeps the inspector correct.
    ///
    /// Setup: v1 for 3 frames (Count=3), then v2 hot-reload (state preserved), then 4 more
    /// frames (Count=11). CaptureLiveState must return 11 (not a stale value).
    ///
    /// Uses <see cref="BlueprintDebugSession.CaptureLiveState"/> (MVE-06 07-A API):
    /// non-pause-gated, reads live blackboard slot.
    /// </summary>
    [Fact]
    public void HotReload_CaptureLiveState_ReturnsPostReloadCount()
    {
        using var fixture    = new BlueprintTestFixture();
        var       coordinator = MakeCoordinator(fixture.Registry);
        var       asset       = MakeAsset();
        var       harness     = new BlueprintRunHarness(fixture);

        // ── Register v1, attach, run 3 frames ─────────────────────────────────
        HotReload(coordinator, BpId, MakeDefV1());
        var entity = harness.SpawnAndAttach(asset);
        harness.Pump(3);
        Assert.Equal(3, harness.ReadIntField(entity, asset, "Count"));

        // ── Construct BlueprintDebugSession (MVE-06 07-A) ─────────────────────
        // The session is wired to the fixture's registry + view — same wiring as the
        // editor uses (BlueprintDebugSession.cs:74-82).
        var session = new BlueprintDebugSession(
            fixture.Registry,
            fixture.View,
            new NoOpTimeController());

        // Register the DebugMap that describes the v1/v2 state layout.
        // QuickReloadService re-registers this map on every reload
        // (QuickReloadService.cs:159-161 "Step 6: Register debug map BEFORE coordinator handoff").
        // Here we register it once (v1/v2 share the same layout).
        var debugMap = MakeDebugMapForV1V2();
        session.RegisterDebugMap(debugMap);

        // ── Hot-reload to v2 ──────────────────────────────────────────────────
        HotReload(coordinator, BpId, MakeDefV2());

        // Re-register the DebugMap after reload (mirrors QuickReloadService.cs:159-161).
        session.RegisterDebugMap(debugMap);

        // ── Run 4 more frames with v2 ─────────────────────────────────────────
        harness.Pump(4);

        // ── CaptureLiveState — non-pause-gated (MVE-06 07-A) ─────────────────
        var snapshot = session.CaptureLiveState(entity, AssetGuid);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.FieldValues.ContainsKey("Count"),
            $"FieldValues must contain 'Count'. Keys: {string.Join(", ", snapshot.FieldValues.Keys)}");

        // Core assertion: live Count == 11 (3 from v1 + 8 from v2, state preserved).
        // If DebugMap was stale (not re-registered), FieldValues would be empty.
        // If state was reset, Count would be 8.
        Assert.Equal(11, (int)snapshot.FieldValues["Count"]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1d — DEBT-MVE-003 regression proof: multi-blueprint quick-reload safety
    // ─────────────────────────────────────────────────────────────────────────

    // Second blueprint identity (distinct from AssetGuid / BpId above).
    private static readonly Guid AssetGuidB = new Guid("07000007-0000-0000-0000-000000000002");
    private static readonly int  BpIdB      = BlueprintIdHash.Compute(AssetGuidB);

    private const ulong HashDef = 0x0700000700000010UL; // shared by all parameterized defs

    /// <summary>
    /// DEBT-MVE-003 regression proof — multi-blueprint quick-reload safety.
    ///
    /// <para><b>Defect halves being proved:</b></para>
    /// <para>
    /// <b>Half 1 — Registry wipe</b> (assertions 3 and 5):
    /// Under the old <c>CommitStaging</c> (full-replace) path, reloading blueprint B with a
    /// 1-entry staging buffer wiped all other entries from the registry snapshot — including
    /// blueprint A. <c>fixture.Registry.TryGetById(idA)</c> would return <c>false</c> and
    /// A's next tick would find no definition, causing <c>ReadIntField</c> to throw
    /// ("No blueprint state slot…" / "No int field…"). The assertion at step 3
    /// (<c>TryGetById(idA) == true</c>) and the continued-ticking assertion at step 5
    /// (<c>A.Count == 5</c> after 2 more pumps, not 0 or exception) directly prove this half.
    /// </para>
    /// <para>
    /// <b>Half 2 — ALC dangle</b> (assertion 7):
    /// Under the old single-<c>_currentAlc</c> field, reloading A a second time would unload
    /// the ALC that was holding B's delegates (since both A's first reload and B's reload shared
    /// the same <c>_currentAlc</c> slot). The assertion
    /// <c>coordinator.RetainedAlcCountForTest == 2</c> (A's latest + B's) and the identity
    /// check <c>GetRetainedAlcForTest(idB) == alcForB_before</c> prove structurally that B's
    /// ALC is not displaced. Note: the test delegates live in the test assembly (not a
    /// throwaway ALC), so the tick-correctness assertions prove registry survival directly;
    /// the ALC-retention assertions prove the structural fix structurally.
    /// </para>
    /// </summary>
    [Fact]
    public void MultiBlueprintReload_SiblingDefinitionAndAlcSurvive_DEBT_MVE_003()
    {
        using var fixture     = new BlueprintTestFixture();
        var       coordinator = MakeCoordinator(fixture.Registry);
        var       harness     = new BlueprintRunHarness(fixture);

        // ── Build two distinct assets (A and B) ──────────────────────────────

        var assetA = MakeAsset(AssetGuid,  "HotReloadA");
        var assetB = MakeAsset(AssetGuidB, "HotReloadB");

        var defAv1 = MakeCountingDef("HotReloadA", HashDef, delta: 1);
        var defBv1 = MakeCountingDef("HotReloadB", HashDef, delta: 1);
        var defAv2 = MakeCountingDef("HotReloadA", HashDef, delta: 2); // for second A reload

        // ── Step 1: hot-reload A (delta+1), spawn entity, pump 3 frames ──────
        HotReload(coordinator, BpId,  defAv1);
        var entityA = harness.SpawnAndAttach(assetA);
        harness.Pump(3);
        Assert.Equal(3, harness.ReadIntField(entityA, assetA, "Count"));

        // ── Step 2: hot-reload B — this is the operation that WIPES A under the bug ──
        // (Old CommitStaging: 1-entry staging for B fully replaces the snapshot, erasing A.)
        HotReload(coordinator, BpIdB, defBv1);

        // ── Step 3: Assert A still in registry (proves registry-wipe half of DEBT-MVE-003) ──
        // Under the bug: TryGetById(BpId) == false (A erased from snapshot).
        Assert.True(fixture.Registry.TryGetById(BpId, out _),
            "Blueprint A must still be in the registry after reloading blueprint B. " +
            "Under the bug (CommitStaging full-replace), A would be wiped from the snapshot.");

        // ── Step 4: spawn entity for B, pump 2 more frames (A and B both tick) ──
        var entityB = harness.SpawnAndAttach(assetB);
        harness.Pump(2);

        // ── Step 5: A keeps ticking with no reset/crash (continues from 3, not 0) ──
        // Under the bug: ReadIntField throws "No blueprint state slot" or returns 0 (hard-reset).
        int countA_after5 = harness.ReadIntField(entityA, assetA, "Count");
        Assert.True(countA_after5 == 5,
            $"Blueprint A's Count must continue from 3 (not reset to 0 or throw) after B's reload. " +
            $"Expected 5, got {countA_after5}. Under the bug the registry wipe prevents tick system from finding A's def.");

        // ── Step 6: B ticks correctly ────────────────────────────────────────
        Assert.Equal(2, harness.ReadIntField(entityB, assetB, "Count"));

        // ── Step 7: ALC retention (proves ALC-dangle half of DEBT-MVE-003) ──
        // Capture B's ALC BEFORE reloading A again.
        var alcForB_before = coordinator.GetRetainedAlcForTest(BpIdB);
        Assert.NotNull(alcForB_before);

        // Reload A a second time (new ALC for A only; B's ALC must be preserved).
        HotReload(coordinator, BpId, defAv2);

        // Two distinct ALCs must be retained: A's latest and B's.
        // Under the old single-_currentAlc: after the A reload the count would be 1
        // (B's ALC was displaced/unloaded when A was last loaded).
        int retainedCount = coordinator.RetainedAlcCountForTest;
        Assert.True(retainedCount == 2,
            $"Exactly 2 distinct ALCs must be retained: A's latest + B's (unchanged). " +
            $"Got {retainedCount}. Under the old single-_currentAlc the count would be 1 — B's ALC displaced.");

        // B's ALC must be unchanged (not unloaded and replaced).
        var alcForB_after = coordinator.GetRetainedAlcForTest(BpIdB);
        Assert.Same(alcForB_before, alcForB_after); // B's ALC must be same instance before and after reloading A

        // A's retained ALC must have changed (new ALC for defAv2).
        var alcForA_after = coordinator.GetRetainedAlcForTest(BpId);
        Assert.NotNull(alcForA_after);
        Assert.NotSame(alcForB_after, alcForA_after);

        // Verify A's tick now uses defAv2 (delta+2): pump 1 more frame → Count = 5+2 = 7.
        harness.Pump(1);
        int countA_final = harness.ReadIntField(entityA, assetA, "Count");
        Assert.True(countA_final == 7,
            $"After the second A reload (delta+2), A's Count must advance by 2 per tick. " +
            $"Expected 7, got {countA_final}. Proves new def is live and A's state was preserved (not reset).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Parameterized def / asset factories for the multi-blueprint test
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an Instance-dispatch blueprint definition whose Tick increments the
    /// "Count" int field (at offset <see cref="OffsetCount"/>) by <paramref name="delta"/>
    /// per frame. Uses <see cref="StateV1V2"/> layout.
    /// </summary>
    private static BlueprintDefinition MakeCountingDef(string name, ulong hash, int delta)
        => new BlueprintDefinition
    {
        Name          = name,
        Kind          = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
        StructureHash = hash,
        StateSize     = Unsafe.SizeOf<StateV1V2>(),
        InitDefault   = bytes => bytes.Clear(),
        Tick          = (bytes, _, _, _, _, _, _) =>
        {
            ref var s = ref Unsafe.As<byte, StateV1V2>(ref MemoryMarshal.GetReference(bytes));
            s.Count += delta;
        },
        StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
        {
            ["Count"] = new BlueprintFieldDescriptor(
                "Count", typeof(int), OffsetBytes: OffsetCount, SizeBytes: sizeof(int), CategoryOrEmpty: ""),
        },
    };

    /// <summary>Asset stub parameterized by guid and name (identity only).</summary>
    private static BlueprintAsset MakeAsset(Guid assetId, string name) => new BlueprintAsset
    {
        AssetId  = assetId,
        Name     = name,
        Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static DebugMap MakeDebugMapForV1V2() => new DebugMap
    {
        AssetId       = AssetGuid,
        AssetName     = "HotReloadTest",
        BlueprintId   = BpId,
        StructureHash = HashAB,
        StateLayout   = new DebugStateLayout
        {
            Fields = new[]
            {
                // Count:int starts at byte 16 (after the 16-byte BlueprintLatentCursor header).
                new StateLayoutField(
                    Name:        "Count",
                    Type:        "System.Int32",
                    OffsetBytes: OffsetCount,
                    SizeBytes:   sizeof(int)),
            },
        },
    };

    /// <summary>
    /// Minimal time-controller for the debug session — pausing is not needed for CaptureLiveState.
    /// </summary>
    private sealed class NoOpTimeController : IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause()       { }
        public void RequestResume()      { }
        public void RequestStepOneTick() { }
    }
}
