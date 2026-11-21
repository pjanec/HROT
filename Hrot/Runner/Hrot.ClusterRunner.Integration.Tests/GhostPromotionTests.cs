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

        int networkId = 123_456_789;
        int entityId = (int)networkId;
        harness.Player.App.TestHook_InjectGeoSpatialDescriptor(entityId, 32.0, 34.0, 10.0);

        Entity ghostEntity = Entity.Null;
        bool ghostCreated = harness.PumpUntil(() =>
        {
            var playerMap = harness.Player.App.TestHook_EntityMap;
            if (!playerMap.TryGetEntity(networkId, out ghostEntity)) return false;
            return harness.Player.App.World.HasComponent<NetworkTransform>(ghostEntity);
        }, TimeoutFrames);

        Assert.True(ghostCreated, "Ghost entity with NetworkTransform was not created after GeoSpatial descriptor.");

        var posAfterGeo = harness.Player.App.World.GetComponent<NetworkTransform>(ghostEntity).LastPosition;

        harness.Player.App.TestHook_InjectEntityMasterDescriptor(entityId, TkbEntityTypes.Tank_M1Abrams);

        // ⭐ CE-157 — an owner publishes EntityInfo too (SmartEgressUtil's first-publish baseline). It is
        //   [PerInstanceValue], so CE-265's derived gate HOLDS promotion until it arrives — by design: the
        //   template default must not win over the instance's own name/faction (docs/designs/tkb-1/DESIGN.md
        //   §6.6a). ⛔ This test injected only GeoSpatial + EntityMaster, so the ghost waited forever.
        harness.Player.App.World.SetComponent(ghostEntity, new Fdp.Core.EntityInfo());

        bool promoted = harness.PumpUntil(() =>
        {
            if (!harness.Player.App.World.IsAlive(ghostEntity)) return false;
            var lifecycle = harness.Player.App.World.GetLifecycleState(ghostEntity);
            return lifecycle != EntityLifecycle.Ghost;
        }, TimeoutFrames);

        Assert.True(promoted, "Ghost entity was not promoted after EntityMaster descriptor arrived.");

        var posAfterPromotion = harness.Player.App.World.GetComponent<NetworkTransform>(ghostEntity).LastPosition;
        Assert.Equal(posAfterGeo, posAfterPromotion);
    }
}
