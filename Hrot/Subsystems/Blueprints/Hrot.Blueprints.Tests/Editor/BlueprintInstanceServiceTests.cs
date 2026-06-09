using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Runtime;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Headless unit tests for the core <see cref="BlueprintInstanceService"/> — the
/// unified attach/detach seam keyed by runtime <c>int blueprintId</c>.
/// </summary>
public sealed class BlueprintInstanceServiceTests
{
    private static EntityRepository NewWorldWithTierComponents()
    {
        var world = new EntityRepository();
        BlueprintRuntimeWiring.RegisterTierComponents(world);
        return world;
    }

    // ── SC1: Fresh attach allocates slot + runs InitDefault ──────────────────

    [Fact]
    public void AttachToEntity_FreshEntity_AllocatesSlot_And_RunsInitDefault()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry();
        CounterDemoBlueprint.Register(registry);
        int bpId = CounterDemoBlueprint.BlueprintId;
        var entity = world.CreateEntity();

        var result = BlueprintInstanceService.AttachToEntity(world, registry, bpId, entity);

        Assert.Equal(BlueprintAttachStatus.Attached, result.Status);
        Assert.Equal(BlackboardTier.B1024, result.Tier);
        Assert.True(result.Success);
        // Verify InitDefault ran: Count field == 0
        Assert.Equal(0, ReadCount(world, entity));
    }

    // ── SC2: Idempotent re-attach returns AlreadyAttached ────────────────────

    [Fact]
    public void AttachToEntity_SecondCall_ReturnsAlreadyAttached()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry();
        CounterDemoBlueprint.Register(registry);
        int bpId = CounterDemoBlueprint.BlueprintId;
        var entity = world.CreateEntity();

        var first  = BlueprintInstanceService.AttachToEntity(world, registry, bpId, entity);
        var second = BlueprintInstanceService.AttachToEntity(world, registry, bpId, entity);

        Assert.Equal(BlueprintAttachStatus.Attached, first.Status);
        Assert.Equal(BlueprintAttachStatus.AlreadyAttached, second.Status);
        Assert.True(second.Success);
        // Exactly one slot despite two attach calls
        Assert.Equal(1, SlotCount(world, entity));
    }

    // ── SC3: Unregistered id returns NotRegistered ───────────────────────────

    [Fact]
    public void AttachToEntity_UnregisteredId_ReturnsNotRegistered()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry(); // empty
        int unknownId = unchecked((int)0xDEADBEEF);
        var entity = world.CreateEntity();

        var result = BlueprintInstanceService.AttachToEntity(world, registry, unknownId, entity);

        Assert.Equal(BlueprintAttachStatus.NotRegistered, result.Status);
        Assert.False(result.Success);
        // No tier component added on failure
        Assert.False(world.HasComponent<BlueprintBlackboard1024>(entity));
    }

    // ── SC4: Non-Instance kind returns NotInstanceKind ───────────────────────

    [Fact]
    public void AttachToEntity_LibraryKind_ReturnsNotInstanceKind()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry();
        // Register a Library-kind blueprint under a known id
        int bpId = CounterDemoBlueprint.BlueprintId;
        registry.RegisterLibrary(bpId, "TestLib");
        var entity = world.CreateEntity();

        var result = BlueprintInstanceService.AttachToEntity(world, registry, bpId, entity);

        Assert.Equal(BlueprintAttachStatus.NotInstanceKind, result.Status);
        Assert.False(result.Success);
        Assert.False(world.HasComponent<BlueprintBlackboard1024>(entity));
    }

    // ── SC5: Detach frees slot and dense-compacts ────────────────────────────

    [Fact]
    public void DetachFromEntity_FreesSlot_And_DenseCompacts()
    {
        using var world = NewWorldWithTierComponents();
        var registry = new BlueprintRegistry();

        // Register three small Instance blueprints in one staging batch.
        var staging = registry.BeginStaging();
        staging.Add(CounterDemoBlueprint.BlueprintId, CounterDemoBlueprint.MakeDefinition());
        staging.Add(FakeInstanceBp.BlueprintId, FakeInstanceBp.MakeDefinition());
        staging.Add(FakeWorldSingletonBp.BlueprintId, FakeWorldSingletonBp.MakeDefinition());
        registry.CommitStaging(staging);

        int bpIdA = CounterDemoBlueprint.BlueprintId;
        int bpIdB = FakeInstanceBp.BlueprintId;
        int bpIdC = FakeWorldSingletonBp.BlueprintId;

        var entity = world.CreateEntity();

        // Attach A, B, C
        var resultA = BlueprintInstanceService.AttachToEntity(world, registry, bpIdA, entity);
        var resultB = BlueprintInstanceService.AttachToEntity(world, registry, bpIdB, entity);
        var resultC = BlueprintInstanceService.AttachToEntity(world, registry, bpIdC, entity);

        Assert.Equal(BlueprintAttachStatus.Attached, resultA.Status);
        Assert.Equal(BlueprintAttachStatus.Attached, resultB.Status);
        Assert.Equal(BlueprintAttachStatus.Attached, resultC.Status);

        // Assert slot count == 3
        Assert.Equal(3, SlotCount(world, entity));

        // Detach B
        bool detached = BlueprintInstanceService.DetachFromEntity(world, bpIdB, entity);
        Assert.True(detached);

        // Assert slot count == 2 (dense-compacted)
        Assert.Equal(2, SlotCount(world, entity));

        // Assert A and C still present, B absent
        Assert.True(HasSlot(world, entity, bpIdA));
        Assert.False(HasSlot(world, entity, bpIdB));
        Assert.True(HasSlot(world, entity, bpIdC));
    }

    // ── SC6: Detach of absent id returns false (no throw) ────────────────────

    [Fact]
    public void DetachFromEntity_AbsentId_ReturnsFalse()
    {
        using var world = NewWorldWithTierComponents();
        var entity = world.CreateEntity();

        bool detached = BlueprintInstanceService.DetachFromEntity(world, unchecked((int)0xDEADBEEF), entity);

        Assert.False(detached);
    }

    // ── SC7: Attach→tick via core seam (end-to-end with BlueprintTestFixture) ─

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void AttachToEntity_ThenTick_CounterAdvances(int frames)
    {
        using var fixture = new BlueprintTestFixture();
        CounterDemoBlueprint.Register(fixture.Registry);
        var entity = fixture.World.CreateEntity();

        var result = BlueprintInstanceService.AttachToEntity(
            fixture.World, fixture.Registry, CounterDemoBlueprint.BlueprintId, entity);

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
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var bb   = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
            return BlueprintBlackboardPartitions.GetSlotCount(memory);
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var bb   = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb));
            return BlueprintBlackboardPartitions.GetSlotCount(memory);
        }
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var bb   = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb));
            return BlueprintBlackboardPartitions.GetSlotCount(memory);
        }
        return 0;
    }

    /// <summary>
    /// Returns true if a slot for <paramref name="blueprintId"/> exists on any tier
    /// of <paramref name="entity"/>.
    /// </summary>
    private static unsafe bool HasSlot(EntityRepository world, Entity entity, int blueprintId)
    {
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var bb   = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out _))
                return true;
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var bb   = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb));
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out _))
                return true;
        }
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var bb   = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb));
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out _))
                return true;
        }
        return false;
    }
}
