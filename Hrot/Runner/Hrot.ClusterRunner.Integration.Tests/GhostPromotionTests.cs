using System;
using System.Numerics;
using Hrot.Core.Mission;
using Hrot.Map.Common;
using Fdp.Toolkit.Replication.Components;
using Fdp.Core;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Verifies that out-of-order descriptor arrival (GeoSpatial before EntityMaster)
/// results in a properly promoted entity with ECS-as-Staging component preservation.
/// </summary>
public class GhostPromotionTests
{
    private const int TimeoutFrames = 120;

    [Fact]
    public void OutOfOrder_GeoSpatialBeforeEntityMaster_PositionPreservedAfterPromotion()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = 123_456_789L;
        int entityId = (int)networkId;
        harness.Ig.App.TestHook_InjectGeoSpatialDescriptor(entityId, 32.0, 34.0, 10.0);

        Entity ghostEntity = Entity.Null;
        bool ghostCreated = harness.PumpUntil(() =>
        {
            var igMap = harness.Ig.App.TestHook_EntityMap;
            if (!igMap.TryGetEntity(networkId, out ghostEntity)) return false;
            return harness.Ig.App.World.HasComponent<NetworkTransform>(ghostEntity);
        }, TimeoutFrames);

        Assert.True(ghostCreated, "Ghost entity with NetworkTransform was not created after GeoSpatial descriptor.");

        var posAfterGeo = harness.Ig.App.World.GetComponent<NetworkTransform>(ghostEntity).LastPosition;

        harness.Ig.App.TestHook_InjectEntityMasterDescriptor(entityId, TkbEntityTypes.Tank_M1Abrams);

        bool promoted = harness.PumpUntil(() =>
        {
            if (!harness.Ig.App.World.IsAlive(ghostEntity)) return false;
            var lifecycle = harness.Ig.App.World.GetLifecycleState(ghostEntity);
            return lifecycle != EntityLifecycle.Ghost;
        }, TimeoutFrames);

        Assert.True(promoted, "Ghost entity was not promoted after EntityMaster descriptor arrived.");

        var posAfterPromotion = harness.Ig.App.World.GetComponent<NetworkTransform>(ghostEntity).LastPosition;
        Assert.Equal(posAfterGeo, posAfterPromotion);
    }
}
