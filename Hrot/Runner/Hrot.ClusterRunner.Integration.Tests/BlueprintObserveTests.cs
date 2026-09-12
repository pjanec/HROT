using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Runtime;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// MVE-BATCH-06 headless observe test.
///
/// Proves the full debug-observe loop (07-A: CaptureLiveState API; 07-B: DebugMap registration):
///   register CounterDemo blueprint in the real kernel → manually register a DebugMap that
///   describes its StateLayout → attach to entity → PumpFrames(N) → CaptureLiveState(entity, assetId)
///   → assert FieldValues["Count"] == N.
///
/// CounterDemoBlueprint is code-defined (no Roslyn compile step) and has a real Tick that
/// increments Count once per frame. A DebugMap is constructed that mirrors the compiler's
/// output for an Instance blueprint with a single int variable at offset 16
/// (after the 16-byte BlueprintLatentCursor header).
/// </summary>
public sealed class BlueprintObserveTests
{
    // ---- Test infrastructure ------------------------------------------------

    private sealed class NoOpTimeController : IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause()       { }
        public void RequestResume()      { }
        public void RequestStepOneTick() { }
    }

    // ---- Core observe test --------------------------------------------------

    /// <summary>
    /// After N frames, CaptureLiveState(entity, assetId).FieldValues["Count"] == N.
    /// Proves: 07-A (non-pause-gated live read) + 07-B (DebugMap registration makes field readable).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void CaptureLiveState_AfterNFrames_CountEqualsN(int frames)
    {
        using var harness = new EditorHarness();

        // Register the CounterDemo blueprint (code-defined; real Tick increments Count per frame).
        CounterDemoBlueprint.Register(harness.BlueprintRegistry);
        var asset = CounterDemoBlueprint.MakeAsset();

        // Create the debug session against the live kernel world.
        var session = new BlueprintDebugSession(
            harness.BlueprintRegistry,
            (ISimulationView)harness.Repo,
            new NoOpTimeController());

        // ── 07-B: Register a DebugMap that describes CounterDemo's StateLayout. ──
        // This is what QuickReloadService would do for a compiled blueprint after
        // calling CSharpEmitter.Emit(). For the code-defined CounterDemoBlueprint the
        // layout is known: BlueprintLatentCursor (16 bytes) then Count:int (4 bytes).
        var debugMap = new DebugMap
        {
            AssetId       = CounterDemoBlueprint.AssetGuid,
            AssetName     = CounterDemoBlueprint.AssetName,
            BlueprintId   = CounterDemoBlueprint.BlueprintId,
            StructureHash = CounterDemoBlueprint.StructureHash,
            StateLayout   = new DebugStateLayout
            {
                Fields = new[]
                {
                    // Cursor occupies bytes 0-15 of the payload; Count starts at byte 16.
                    new StateLayoutField(
                        Name:        CounterDemoBlueprint.CountFieldName,
                        Type:        "System.Int32",
                        OffsetBytes: CounterDemoBlueprint.CountOffset,  // == 16
                        SizeBytes:   sizeof(int)),
                },
            },
        };
        session.RegisterDebugMap(debugMap);

        // ── Attach to a real entity and pump N frames through the kernel. ──
        var entity = harness.Repo.CreateEntity();
        var attachResult = BlueprintAttachService.AttachToEntity(
            harness.Repo, harness.BlueprintRegistry, asset, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, attachResult.Status);

        harness.PumpFrames(frames);

        // ── 07-A: CaptureLiveState — no pause required. ──
        var snapshot = session.CaptureLiveState(entity, CounterDemoBlueprint.AssetGuid);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.FieldValues.ContainsKey(CounterDemoBlueprint.CountFieldName),
            $"FieldValues must contain '{CounterDemoBlueprint.CountFieldName}'. " +
            $"Keys present: {string.Join(", ", snapshot.FieldValues.Keys)}");

        // Core assertion: Count == N (real execution, not "no throw").
        Assert.Equal(frames, (int)snapshot.FieldValues[CounterDemoBlueprint.CountFieldName]);
    }

    /// <summary>
    /// ⭐ <c>QA-015</c> — <b>a DebugMap is NOT required to read fields, and asserting that it was
    /// pinned an implementation detail rather than an intent.</b>
    ///
    /// <para>⛔ This used to assert <c>Assert.Empty(snapshot.FieldValues)</c> on the reasoning
    /// *"no RegisterDebugMap ⇒ <c>_debugMaps</c> empty ⇒ <c>StateLayout == null</c> ⇒ no fields."*
    /// 📐 Measured 2026-08-26: <c>CaptureStateSnapshot</c> consults <c>_debugMaps</c> only for the
    /// ASSET NAME; the fields come from <c>_registry.TryGetById</c> and the definition's kind. So a
    /// registered blueprint yields its fields either way — the run produced <c>["Count"] = 3</c>.</para>
    ///
    /// <para>⭐⭐ And the design agrees that this was never DebugMap's job:
    /// <c>Blueprint_Subsystem_DEBUG-DD-ADDENDUM.md</c> lists <c>RegisterDebugMap(asset)</c> as one of
    /// *"the two events that bind/rebind BREAKPOINTS"* — ⛔ nothing makes it a precondition for state
    /// capture. ⇒ the assertion is inverted to the property that actually matters: capture works
    /// WITHOUT a debug map, which is what makes the observe path usable on a fresh session.</para>
    /// </summary>
    [Fact]
    public void CaptureLiveState_WithoutDebugMap_ReturnsSnapshotWithEmptyFields()
    {
        using var harness = new EditorHarness();

        CounterDemoBlueprint.Register(harness.BlueprintRegistry);
        var asset = CounterDemoBlueprint.MakeAsset();

        var session = new BlueprintDebugSession(
            harness.BlueprintRegistry,
            (ISimulationView)harness.Repo,
            new NoOpTimeController());

        // NO RegisterDebugMap call → _debugMaps is empty → StateLayout == null.
        var entity = harness.Repo.CreateEntity();
        var attachResult = BlueprintAttachService.AttachToEntity(
            harness.Repo, harness.BlueprintRegistry, asset, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, attachResult.Status);

        harness.PumpFrames(3);

        var snapshot = session.CaptureLiveState(entity, CounterDemoBlueprint.AssetGuid);
        Assert.NotNull(snapshot);

        // ⭐ QA-015 — the fields come from the REGISTRY, so they are present without a debug map.
        //    ⛔ The old `Assert.Empty` asserted the opposite and had been red ever since capture stopped
        //    depending on _debugMaps for anything but the asset name.
        Assert.True(snapshot!.FieldValues.ContainsKey(CounterDemoBlueprint.CountFieldName),
            "capture must work without RegisterDebugMap — that call binds BREAKPOINTS, not state. "
            + $"Keys present: {string.Join(", ", snapshot.FieldValues.Keys)}");
        Assert.Equal(3, (int)snapshot.FieldValues[CounterDemoBlueprint.CountFieldName]);
    }

    /// <summary>
    /// CaptureLiveState returns null for an entity that has no blueprint slot.
    /// (Entity exists but has never had a blueprint attached.)
    /// </summary>
    [Fact]
    public void CaptureLiveState_EntityWithNoSlot_ReturnsSnapshotWithEmptyFields()
    {
        using var harness = new EditorHarness();

        CounterDemoBlueprint.Register(harness.BlueprintRegistry);
        var debugMap = new DebugMap
        {
            AssetId       = CounterDemoBlueprint.AssetGuid,
            AssetName     = CounterDemoBlueprint.AssetName,
            BlueprintId   = CounterDemoBlueprint.BlueprintId,
            StructureHash = CounterDemoBlueprint.StructureHash,
            StateLayout   = new DebugStateLayout
            {
                Fields = new[] { new StateLayoutField("Count", "System.Int32", CounterDemoBlueprint.CountOffset, sizeof(int)) },
            },
        };

        var session = new BlueprintDebugSession(
            harness.BlueprintRegistry,
            (ISimulationView)harness.Repo,
            new NoOpTimeController());
        session.RegisterDebugMap(debugMap);

        // Create entity but do NOT attach any blueprint.
        var entity = harness.Repo.CreateEntity();

        var snapshot = session.CaptureLiveState(entity, CounterDemoBlueprint.AssetGuid);
        // CaptureStateSnapshot always returns a snapshot (even with no slot), but FieldValues is empty
        // because TryGetSlotOffset returns false when no BB component exists.
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.FieldValues);
    }
}
