using System;
using Hrot.Core.Mission;
using Hrot.Map.Common;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.NetworkSpawning.Events;
using Fdp.Kernel;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Verifies that SubEntityCleanupSystem cascades entity destruction to child entities.
/// </summary>
public class SubEntityCascadeDestroyTests
{
    private const int SpawnTimeoutFrames = 150;
    private const int DestroyTimeoutFrames = 150;
    private const int CleanupTimeoutFrames = 60;

    [Fact]
    public void DestroyParentEntity_ChildEntitiesAreAlsoDestroyed()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new GeoPoint { Latitude = 32.0, Longitude = 34.0 });

        Entity parentEntity = Entity.Null;
        bool parentActive = harness.PumpUntil(() =>
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out parentEntity))
                return false;
            return harness.SimHost.World.IsAlive(parentEntity);
        }, SpawnTimeoutFrames);
        Assert.True(parentActive, "Parent entity did not become active in time.");

        var childEntities = harness.SimHost.TestHook_GetChildEntities(parentEntity);
        if (childEntities.Count == 0)
        {
            return;
        }

        foreach (var child in childEntities)
            Assert.True(harness.SimHost.World.IsAlive(child),
                "Child entity should be alive before parent destruction.");

        harness.SimHost.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason = "SubEntityCascadeDestroyTests"
        });

        bool parentDestroyed = harness.PumpUntil(
            () => !harness.SimHost.World.IsAlive(parentEntity),
            DestroyTimeoutFrames);
        Assert.True(parentDestroyed, "Parent entity was not destroyed.");

        bool allChildrenDestroyed = harness.PumpUntil(
            () => childEntities.TrueForAll(c => !harness.SimHost.World.IsAlive(c)),
            CleanupTimeoutFrames);

        Assert.True(allChildrenDestroyed,
            "Child entities were not destroyed after parent was destroyed.");
    }
}
