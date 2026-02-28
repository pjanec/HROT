using System;
using System.Numerics;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

public class EntityDestroyIntegrationTests
{
    private const int ConfigSyncTimeoutFrames = 100;
    private const int SimHostSpawnTimeoutFrames = 100;
    private const int IgSpawnTimeoutFrames = 100;
    private const int DestroyTimeoutFrames = 100;

    [Fact]
    public void SimHost_DestroyEntity_IgGhostIsRemoved()
    {
        using var harness = new BagiraRunnerHarness();

        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        long networkId = SpawnEntityThroughPlacement(harness, tkbType);

        Entity simHostEntity = Entity.Null;
        bool simHostEntityReady = harness.PumpUntil(
            () => TryGetSimHostEntity(harness.SimHost.World, networkId, out simHostEntity),
            SimHostSpawnTimeoutFrames);
        Assert.True(simHostEntityReady, "SimHost entity was not found in time.");

        bool igGhostPresent = harness.PumpUntil(
            () => IgHasEntityWithNetworkId(harness.Ig.App.World, networkId),
            IgSpawnTimeoutFrames);
        Assert.True(igGhostPresent, "IG ghost entity did not appear in time.");

        harness.SimHost.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason = "integration test"
        });

        bool simHostMarkedForTeardown = harness.PumpUntil(
            () => harness.SimHost.World.IsAlive(simHostEntity)
               && harness.SimHost.World.GetLifecycleState(simHostEntity) == EntityLifecycle.TearDown,
            DestroyTimeoutFrames);
        Assert.True(simHostMarkedForTeardown, "SimHost entity did not enter TearDown in time.");

        bool simHostDestroyed = harness.PumpUntil(
            () => !harness.SimHost.World.IsAlive(simHostEntity),
            DestroyTimeoutFrames);
        Assert.True(simHostDestroyed, "SimHost entity was not destroyed after ack.");

        bool igGhostRemoved = harness.PumpUntil(
            () => !IgHasEntityWithNetworkId(harness.Ig.App.World, networkId),
            DestroyTimeoutFrames);
        Assert.True(igGhostRemoved, "IG ghost entity was not removed after destroy.");
    }

    private static long SpawnEntityThroughPlacement(BagiraRunnerHarness harness, long tkbType)
    {
        var iosLogic = harness.Ios.Logic;
        var igApp = harness.Ig.App;

        iosLogic.StartPlacementMode(tkbType, eForceIdentifier.FORCE_FRIENDLY);
        Assert.Equal(tkbType, iosLogic.PlacementType);

        bool configSynced = harness.PumpUntil(
            () => iosLogic.ActiveContextId != Guid.Empty
               && igApp.TestHook_ActiveContextId == iosLogic.ActiveContextId,
            ConfigSyncTimeoutFrames);
        Assert.True(configSynced, "MapInteractionConfig did not reach IG in time.");

        igApp.TestHook_SimulateMapClick(new Vector2(100f, 200f));

        bool simHostSpawned = harness.PumpUntil(
            () => TryGetSimHostNetworkId(harness.SimHost.World, tkbType, out _),
            SimHostSpawnTimeoutFrames);
        Assert.True(simHostSpawned, "SimHost did not spawn an entity in time.");

        Assert.True(TryGetSimHostNetworkId(harness.SimHost.World, tkbType, out long networkId));
        return networkId;
    }

    private static bool TryGetSimHostNetworkId(EntityRepository world, long tkbType, out long networkId)
    {
        var view = (ISimulationView)world;
        var query = world.Query().IncludeAll().With<NetworkIdentity>().With<NetworkSpawnRequest>().Build();
        foreach (var entity in query)
        {
            var spawn = view.GetComponentRO<NetworkSpawnRequest>(entity);
            if (spawn.TkbType != tkbType)
                continue;

            var netId = view.GetComponentRO<NetworkIdentity>(entity);
            networkId = netId.Value;
            return true;
        }

        networkId = 0;
        return false;
    }

    private static bool TryGetSimHostEntity(EntityRepository world, long networkId, out Entity entity)
    {
        var view = (ISimulationView)world;
        var query = world.Query().IncludeAll().With<NetworkIdentity>().Build();
        foreach (var candidate in query)
        {
            var id = view.GetComponentRO<NetworkIdentity>(candidate);
            if (id.Value == networkId)
            {
                entity = candidate;
                return true;
            }
        }

        entity = Entity.Null;
        return false;
    }

    private static bool IgHasEntityWithNetworkId(EntityRepository world, long networkId)
    {
        var view = (ISimulationView)world;
        var query = world.Query().With<NetworkIdentity>().Build();
        foreach (var entity in query)
        {
            var id = view.GetComponentRO<NetworkIdentity>(entity);
            if (id.Value == networkId)
                return true;
        }

        return false;
    }
}
