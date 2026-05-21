using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintMaintenanceSystem;

/// <summary>
/// SC1/SC2/SC3/SC4: Tier upgrade from BB1024 to BB4096.
/// Per Runtime DD §11.5.
/// </summary>
public sealed class TierUpgrade_1024_to_4096_Tests
{
    // SC1/SC2: Entity with both BB1024 and BB4096 present -> state migrated, BB1024 removed.
    [Fact]
    public unsafe void TierUpgrade_WhenBothComponentsPresent_MigratesState()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);  // attaches via BB1024

        // Manually trigger upgrade signal by adding BB4096
        fixture.World.AddComponent(entity, default(BlueprintBlackboard4096));

        fixture.TickFrame(0.016f);

        // After maintenance: BB4096 present, BB1024 removed
        Assert.True(fixture.World.HasComponent<BlueprintBlackboard4096>(entity));
        Assert.False(fixture.World.HasComponent<BlueprintBlackboard1024>(entity));

        // Slot still accessible in BB4096
        ref var bb4096  = ref fixture.World.GetComponentRW<BlueprintBlackboard4096>(entity);
        ref byte mem    = ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb4096);
        byte* memory    = (byte*)Unsafe.AsPointer(ref mem);
        bool found = BlueprintBlackboardPartitions.TryGetSlotOffset(
            memory, FakeInstanceBp.BlueprintId, out int payloadOffset);
        Assert.True(found);
    }

    // SC3: Entity with only BB1024 (no BB4096) is not touched by MaintenanceSystem.
    [Fact]
    public void TierUpgrade_EntityWithOnlyBB1024_NotTouched()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);  // BB1024 only

        fixture.TickFrame(0.016f);

        // BB1024 still present
        Assert.True(fixture.World.HasComponent<BlueprintBlackboard1024>(entity));
        Assert.False(fixture.World.HasComponent<BlueprintBlackboard4096>(entity));
    }

    // SC4: State written before upgrade is preserved in BB4096 after upgrade.
    [Fact]
    public unsafe void TierUpgrade_StatePreserved_AfterUpgrade()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick once to increment TickCount to 1
        fixture.TickFrame(0.016f);

        // Verify TickCount == 1 in BB1024
        var state1 = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state1);
        Assert.True(state1!.Value.TryGetField<int>("TickCount", out var tc1));
        Assert.Equal(1, tc1);

        // Add BB4096 to trigger upgrade
        fixture.World.AddComponent(entity, default(BlueprintBlackboard4096));

        fixture.TickFrame(0.016f);

        // Now in BB4096 -- TickCount should be 2 (1 from before + 1 from this tick)
        var state2 = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state2);
        Assert.True(state2!.Value.TryGetField<int>("TickCount", out var tc2));
        // After upgrade: state migrated (TickCount = 1) + 1 tick = 2
        Assert.Equal(2, tc2);
    }
}
