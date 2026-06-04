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
    /// Without a registered DebugMap, CaptureLiveState returns a non-null snapshot but
    /// FieldValues is empty (slot exists but StateLayout is unknown).
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
        Assert.Empty(snapshot!.FieldValues);
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
