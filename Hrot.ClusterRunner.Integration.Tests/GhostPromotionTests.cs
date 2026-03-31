using System;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
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
        harness.Ig.App.TestHook_InjectGeoSpatialDescriptor(new WorldPos
        {
            EntityId = entityId,
            Pos = new GeoPoint
            {
                Latitude = 32.0,
                Longitude = 34.0,
                Altitude = 10.0
            },
            Ori = new EulerOri
            {
                Heading = 0,
                Pitch = 0,
                Roll = 0
            },
            Time = DateTime.UtcNow
        });

        Entity ghostEntity = Entity.Null;
        bool ghostCreated = harness.PumpUntil(() =>
        {
            var igMap = harness.Ig.App.TestHook_EntityMap;
            if (!igMap.TryGetEntity(networkId, out ghostEntity)) return false;
            return harness.Ig.App.World.HasComponent<NetworkTransform>(ghostEntity);
        }, TimeoutFrames);

        Assert.True(ghostCreated, "Ghost entity with NetworkTransform was not created after GeoSpatial descriptor.");

        var posAfterGeo = harness.Ig.App.World.GetComponent<NetworkTransform>(ghostEntity).LastPosition;

        harness.Ig.App.TestHook_InjectEntityMasterDescriptor(new EntityMaster
        {
            EntityId = entityId,
            TkbType = TkbEntityTypes.Tank_M1Abrams,
            DisType = default
        });

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
