using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// MVE-01 (the RUN slice): proves headlessly that an Instance Blueprint can be created on
/// an entity and RUN in the real blueprint tick substrate, with an observable per-frame
/// state change, plus exercises the reusable <see cref="BlueprintRunHarness"/> that the
/// editor "Run Opened Blueprint" button (MVE-06) will reuse.
///
/// <para>
/// SUBSTRATE: <see cref="BlueprintTestFixture"/> — the proven minimal world + registry +
/// <c>BlueprintTickSystem</c>/<c>BlueprintMaintenanceSystem</c> harness. The ClusterRunner
/// kernel built by <c>Hrot.ClusterRunner.Integration.Tests/EditorHarness</c> does NOT
/// schedule the blueprint systems or register the blackboard tier components / a
/// <c>BlueprintRegistry</c> (verified: EditorHarness.cs ctor, lines 118–221, registers only
/// SimHost/CGF/EQS/editor modules — no Blueprint* anything). See MVE-BATCH-01-REPORT.md
/// for the gap + the exact module wiring needed to give the editor button a real run
/// substrate later.
/// </para>
///
/// <para>
/// Assertions prove REAL execution: the per-frame counter advances by exactly the number of
/// frames pumped, read back from the blackboard slot — not merely "no throw".
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class BlueprintRunMveTests
{
    // ── Task 1: end-to-end RUN on an entity ──────────────────────────────────

    /// <summary>
    /// Register a real Instance blueprint, create an entity, attach via the tiered
    /// partition path, pump N frames through the real tick, and assert the observable
    /// state advanced by exactly N.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void InstanceBlueprint_RunsOnEntity_CounterAdvancesByFrameCount(int frames)
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var harness = new BlueprintRunHarness(fixture);
        var entity = harness.SpawnAndAttach(asset);

        // Sanity: before any tick the observable counter is at its InitDefault value (0).
        Assert.Equal(0, harness.ReadIntField(entity, asset, "TickCount"));

        harness.Pump(frames);

        // Real execution: the counter advanced by exactly the number of frames pumped.
        Assert.Equal(frames, harness.ReadIntField(entity, asset, "TickCount"));
    }

    /// <summary>
    /// Two distinct entities running the same blueprint advance independently — proves the
    /// per-entity slot state is isolated, not a shared static counter.
    /// </summary>
    [Fact]
    public void InstanceBlueprint_TwoEntities_AdvanceIndependently()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var harness = new BlueprintRunHarness(fixture);
        var entityA = harness.SpawnAndAttach(asset);

        // Run A alone for 3 frames, THEN spawn B and run both for 2 more frames.
        harness.Pump(3);
        var entityB = harness.SpawnAndAttach(asset);
        harness.Pump(2);

        // A was attached for all 5 frames; B only for the last 2.
        Assert.Equal(5, harness.ReadIntField(entityA, asset, "TickCount"));
        Assert.Equal(2, harness.ReadIntField(entityB, asset, "TickCount"));
    }

    // ── Task 1: world-singleton variant ──────────────────────────────────────

    /// <summary>
    /// The substrate supports world singletons: a blueprint registered with
    /// <c>AddWorldSingleton</c> is lazily attached to the world's singleton blackboard on
    /// the first tick (no entity) and then ticks every frame. Assert the singleton's
    /// observable counter advances by exactly the number of frames pumped.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public unsafe void WorldSingletonBlueprint_LazyInitsAndTicks_CounterAdvancesByFrameCount(int frames)
    {
        using var fixture = new BlueprintTestFixture();
        FakeWorldSingletonBp.Register(fixture.Registry);

        // No singleton blackboard exists until the first tick lazily attaches it.
        Assert.False(fixture.World.HasSingleton<BlueprintBlackboard1024>());

        for (int i = 0; i < frames; i++)
            fixture.TickFrame(0.016f);

        // Lazy attach happened exactly once.
        Assert.True(fixture.World.HasSingleton<BlueprintBlackboard1024>());

        ref var bb     = ref fixture.World.GetSingleton<BlueprintBlackboard1024>();
        ref byte mem   = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory   = (byte*)Unsafe.AsPointer(ref mem);

        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        Assert.Equal(1, (int)header.SlotCount);

        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(
            memory, FakeWorldSingletonBp.BlueprintId, out int payloadOffset));

        ref var state = ref Unsafe.AsRef<FakeWorldSingletonBp.State>(memory + payloadOffset);

        // Real execution: singleton ticked once per frame.
        Assert.Equal(frames, state.TickCount);
    }
}
