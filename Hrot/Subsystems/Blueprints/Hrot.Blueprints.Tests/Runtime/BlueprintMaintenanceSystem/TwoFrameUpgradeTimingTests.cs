using Fdp.Toolkit.Blueprints.Components;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintMaintenanceSystem;

/// <summary>
/// §11.5 timing: The upgrade happens at the end of the frame the both-components condition
/// is detected (MaintenanceSystem runs in BeforeSync, same TickFrame call).
/// Per Runtime DD §7.2.
/// </summary>
public sealed class TwoFrameUpgradeTimingTests
{
    // Frame N: both BB1024 + BB4096 present. After TickFrame completes: only BB4096 remains.
    [Fact]
    public void TwoFrame_FrameN_BothComponentsPresent_FrameNPlus1_OnlyBB4096()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);  // BB1024

        // Frame N: manually add BB4096 so both are present
        fixture.World.AddComponent(entity, default(BlueprintBlackboard4096));

        // After this frame, MaintenanceSystem should have upgraded
        fixture.TickFrame(0.016f);

        // Frame N+1: only BB4096 remains
        Assert.True(fixture.World.HasComponent<BlueprintBlackboard4096>(entity),
            "BB4096 must be present after upgrade");
        Assert.False(fixture.World.HasComponent<BlueprintBlackboard1024>(entity),
            "BB1024 must be removed after upgrade");

        // Frame N+1 tick should succeed without any error
        fixture.TickFrame(0.016f);

        // Still only BB4096 after second tick
        Assert.True(fixture.World.HasComponent<BlueprintBlackboard4096>(entity));
        Assert.False(fixture.World.HasComponent<BlueprintBlackboard1024>(entity));
    }
}
