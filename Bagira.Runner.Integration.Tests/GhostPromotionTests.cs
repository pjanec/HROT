using System;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.Map.Common;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

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
        using var harness = new BagiraRunnerHarness();

        long networkId = 123_456_789L;
        int entityId = (int)networkId;
        harness.Ig.App.TestHook_InjectGeoSpatialDescriptor(new GeoSpatial
        {
            EntityId = entityId,
            Pos = new GeoPosition
            {
                Latitude = 32.0,
                Longitude = 34.0,
                Altitude = 10.0
            },
            Rot = new OrientationHPR
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
            return harness.Ig.App.World.HasComponent<NetworkPosition>(ghostEntity);
        }, TimeoutFrames);

        Assert.True(ghostCreated, "Ghost entity with NetworkPosition was not created after GeoSpatial descriptor.");

        var posAfterGeo = harness.Ig.App.World.GetComponent<NetworkPosition>(ghostEntity).Value;

        harness.Ig.App.TestHook_InjectEntityMasterDescriptor(new EntityMaster
        {
            EntityId = entityId,
            TkbType = TkbEntityTypes.Tank_M1Abrams,
            DisType = 0
        });

        bool promoted = harness.PumpUntil(() =>
        {
            if (!harness.Ig.App.World.IsAlive(ghostEntity)) return false;
            var lifecycle = harness.Ig.App.World.GetLifecycleState(ghostEntity);
            return lifecycle != EntityLifecycle.Ghost;
        }, TimeoutFrames);

        Assert.True(promoted, "Ghost entity was not promoted after EntityMaster descriptor arrived.");

        var posAfterPromotion = harness.Ig.App.World.GetComponent<NetworkPosition>(ghostEntity).Value;
        Assert.Equal(posAfterGeo, posAfterPromotion);
    }
}
