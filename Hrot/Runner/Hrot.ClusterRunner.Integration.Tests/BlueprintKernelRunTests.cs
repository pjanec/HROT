using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Editor.Runtime;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// MVE-BATCH-02 — proves an Instance Blueprint actually ticks inside the REAL
/// <see cref="Fdp.ModuleHost.ModuleHostKernel"/> that the editor composes (via
/// <see cref="EditorHarness"/>, which now wires the blueprint runtime through the same
/// shared <see cref="BlueprintRuntimeWiring.WireBlueprintRuntime"/> helper as
/// <c>EditorSubsystem</c>).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the MVE-01 tests (which used the minimal <c>BlueprintTestFixture</c> substrate),
/// these run through the genuine kernel schedule: <see cref="EditorHarness.PumpFrames"/>
/// advances time and calls <c>Kernel.Update()</c>, which executes the Simulation-phase
/// <c>BlueprintTickSystem</c> inside the editor simulation module and the BeforeSync
/// <c>BlueprintMaintenanceSystem</c> registered as a global system.
/// </para>
/// <para>
/// The test CREATES ITS OWN entity (nothing is selected headlessly), attaches the demo
/// blueprint via the production <see cref="BlueprintAttachService"/> (the same seam the
/// MVE-03 toolbar button will use), pumps N frames, and asserts the observable
/// <c>Count</c> advanced by EXACTLY N — real execution, not "no throw".
/// </para>
/// </remarks>
public sealed class BlueprintKernelRunTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void InstanceBlueprint_TicksInRealKernel_CounterAdvancesByFrameCount(int frames)
    {
        using var harness = new EditorHarness();

        // Register the demo blueprint into the kernel's shared registry (same instance the
        // in-kernel BlueprintTickSystem ticks against).
        CounterDemoBlueprint.Register(harness.BlueprintRegistry);
        var asset = CounterDemoBlueprint.MakeAsset();

        // Create our own entity in the kernel's live world and attach via the production helper.
        var entity = harness.Repo.CreateEntity();
        var result = BlueprintAttachService.AttachToEntity(
            harness.Repo, harness.BlueprintRegistry, asset, entity);

        Assert.Equal(BlueprintAttachStatus.Attached, result.Status);
        Assert.Equal(BlackboardTier.B1024, result.Tier);

        // Before any tick the observable is at its InitDefault value (0).
        Assert.Equal(0, ReadCount(harness.Repo, entity));

        harness.PumpFrames(frames);

        // Real execution through the kernel: Count advanced by exactly the pumped frame count.
        Assert.Equal(frames, ReadCount(harness.Repo, entity));
    }

    [Fact]
    public void InstanceBlueprint_TwoEntities_AdvanceIndependentlyInRealKernel()
    {
        using var harness = new EditorHarness();
        CounterDemoBlueprint.Register(harness.BlueprintRegistry);
        var asset = CounterDemoBlueprint.MakeAsset();

        var entityA = harness.Repo.CreateEntity();
        Assert.Equal(BlueprintAttachStatus.Attached,
            BlueprintAttachService.AttachToEntity(harness.Repo, harness.BlueprintRegistry, asset, entityA).Status);

        // Run A alone for 3 frames, then spawn B and run both for 2 more.
        harness.PumpFrames(3);

        var entityB = harness.Repo.CreateEntity();
        Assert.Equal(BlueprintAttachStatus.Attached,
            BlueprintAttachService.AttachToEntity(harness.Repo, harness.BlueprintRegistry, asset, entityB).Status);

        harness.PumpFrames(2);

        // A was attached for all 5 frames; B only for the last 2 — proves per-entity slot state.
        Assert.Equal(5, ReadCount(harness.Repo, entityA));
        Assert.Equal(2, ReadCount(harness.Repo, entityB));
    }

    [Fact]
    public void AttachToEntity_IsIdempotent_DoesNotDoubleCountInRealKernel()
    {
        using var harness = new EditorHarness();
        CounterDemoBlueprint.Register(harness.BlueprintRegistry);
        var asset = CounterDemoBlueprint.MakeAsset();

        var entity = harness.Repo.CreateEntity();

        // Attach twice before running: the second call must be a no-op (one slot only).
        Assert.Equal(BlueprintAttachStatus.Attached,
            BlueprintAttachService.AttachToEntity(harness.Repo, harness.BlueprintRegistry, asset, entity).Status);
        Assert.Equal(BlueprintAttachStatus.AlreadyAttached,
            BlueprintAttachService.AttachToEntity(harness.Repo, harness.BlueprintRegistry, asset, entity).Status);

        harness.PumpFrames(4);

        // A single slot ticked once per frame → exactly 4 (not 8 from a duplicate slot).
        Assert.Equal(4, ReadCount(harness.Repo, entity));
    }

    // Reads the demo blueprint's observable Count field from the entity's B1024 slot.
    // Throws (rather than returning a misleading 0) if the slot is missing.
    private static unsafe int ReadCount(EntityRepository repo, Entity entity)
    {
        Assert.True(repo.HasComponent<BlueprintBlackboard1024>(entity),
            $"Entity {entity} has no BlueprintBlackboard1024 component.");

        ref var bb    = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* memory  = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));

        Assert.True(
            BlueprintBlackboardPartitions.TryGetSlotOffset(
                memory, CounterDemoBlueprint.BlueprintId, out int payloadOffset),
            $"No blueprint slot for CounterDemo on entity {entity}.");

        return Unsafe.ReadUnaligned<int>(memory + payloadOffset + CounterDemoBlueprint.CountOffset);
    }
}
