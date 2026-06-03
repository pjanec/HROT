using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Runtime;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Headless unit tests for the production <see cref="BlueprintAttachService"/> — the
/// run-mode-agnostic attach seam shared by the headless real-kernel test and the future
/// MVE-03 toolbar button. These run on a bare <see cref="EntityRepository"/> + a
/// <see cref="BlueprintRegistry"/> with no kernel or sim loop, proving the service only
/// sets up the entity's components (no requirement that the sim is running).
/// </summary>
public sealed class BlueprintAttachServiceTests
{
    private static EntityRepository NewWorldWithTierComponents()
    {
        var world = new EntityRepository();
        // The demo blueprint fits the 1024 tier; register all three the service may choose.
        BlueprintRuntimeWiring.RegisterTierComponents(world);
        return world;
    }

    [Fact]
    public void AttachToEntity_FreshEntity_AllocatesInitializedSlot()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry();
        CounterDemoBlueprint.Register(registry);
        var asset = CounterDemoBlueprint.MakeAsset();

        var entity = world.CreateEntity();
        var result = BlueprintAttachService.AttachToEntity(world, registry, asset, entity);

        Assert.Equal(BlueprintAttachStatus.Attached, result.Status);
        Assert.Equal(BlackboardTier.B1024, result.Tier);
        Assert.True(result.Success);

        // The slot exists and InitDefault zeroed the observable Count.
        Assert.Equal(0, ReadCount(world, entity));
    }

    [Fact]
    public void AttachToEntity_CalledTwice_IsIdempotent_SingleSlot()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry();
        CounterDemoBlueprint.Register(registry);
        var asset = CounterDemoBlueprint.MakeAsset();

        var entity = world.CreateEntity();

        var first  = BlueprintAttachService.AttachToEntity(world, registry, asset, entity);
        var second = BlueprintAttachService.AttachToEntity(world, registry, asset, entity);

        Assert.Equal(BlueprintAttachStatus.Attached, first.Status);
        Assert.Equal(BlueprintAttachStatus.AlreadyAttached, second.Status);
        Assert.True(second.Success);

        // Exactly one slot was allocated despite two attach calls.
        Assert.Equal(1, SlotCount(world, entity));
    }

    [Fact]
    public void AttachToEntity_UnregisteredAsset_ReturnsNotRegistered()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry(); // empty — nothing registered
        var asset = CounterDemoBlueprint.MakeAsset();

        var entity = world.CreateEntity();
        var result = BlueprintAttachService.AttachToEntity(world, registry, asset, entity);

        Assert.Equal(BlueprintAttachStatus.NotRegistered, result.Status);
        Assert.False(result.Success);
        // No tier component should have been added on the failure path.
        Assert.False(world.HasComponent<BlueprintBlackboard1024>(entity));
    }

    [Fact]
    public void AttachToEntity_NonInstanceBlueprint_ReturnsNotInstanceKind()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry();

        // Register a Library-kind blueprint under the demo asset's id.
        var asset = CounterDemoBlueprint.MakeAsset();
        int id = BlueprintIdHash.Compute(asset.AssetId);
        registry.RegisterLibrary(id, asset.Name);

        var entity = world.CreateEntity();
        var result = BlueprintAttachService.AttachToEntity(world, registry, asset, entity);

        Assert.Equal(BlueprintAttachStatus.NotInstanceKind, result.Status);
        Assert.False(result.Success);
        Assert.False(world.HasComponent<BlueprintBlackboard1024>(entity));
    }

    /// <summary>
    /// End-to-end through the SAME attach path the kernel test uses, but on the minimal
    /// fixture substrate: attach via the service, then tick — proving the slot the service
    /// created is the one the real <c>BlueprintTickSystem</c> ticks (Count advances by N).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void AttachToEntity_ThenTick_CounterAdvances(int frames)
    {
        using var fixture = new BlueprintTestFixture();
        CounterDemoBlueprint.Register(fixture.Registry);
        var asset = CounterDemoBlueprint.MakeAsset();

        var entity = fixture.World.CreateEntity();
        var result = BlueprintAttachService.AttachToEntity(
            fixture.World, fixture.Registry, asset, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, result.Status);

        for (int i = 0; i < frames; i++)
            fixture.TickFrame(0.016f);

        Assert.Equal(frames, ReadCount(fixture.World, entity));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static unsafe int ReadCount(EntityRepository world, Entity entity)
    {
        ref var bb   = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));

        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(
            memory, CounterDemoBlueprint.BlueprintId, out int payloadOffset));

        return Unsafe.ReadUnaligned<int>(memory + payloadOffset + CounterDemoBlueprint.CountOffset);
    }

    private static unsafe int SlotCount(EntityRepository world, Entity entity)
    {
        ref var bb   = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
        return BlueprintBlackboardPartitions.GetSlotCount(memory);
    }
}
