using System;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.Map.Common;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// End-to-end test proving that entity destruction results in map cleanup on both nodes.
/// Tests the zombie entity memory leak fix (REPL Issue 2).
/// </summary>
public class ZombieEntityMapTests
{
    private const int SpawnTimeoutFrames = 150;
    private const int DestroyTimeoutFrames = 150;
    private const int MapPruneTimeoutFrames = 60;

    [Fact]
    public void DestroyedEntity_IsRemovedFromNetworkEntityMap_OnSimHost()
    {
        using var harness = new BagiraRunnerHarness();

        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new GeoPosition { Latitude = 32.0, Longitude = 34.0 });

        var simHostMap = harness.SimHost.TestHook_EntityMap;
        bool appeared = harness.PumpUntil(
            () => simHostMap.TryGetEntity(networkId, out _),
            SpawnTimeoutFrames);
        Assert.True(appeared, "Entity did not appear in SimHost NetworkEntityMap.");

        harness.SimHost.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason = "ZombieEntityMapTests"
        });

        bool removedFromMap = harness.PumpUntil(
            () => !simHostMap.TryGetEntity(networkId, out _),
            MapPruneTimeoutFrames);

        Assert.True(removedFromMap,
            "NetworkEntityMap was not pruned after entity destruction on SimHost.");
    }

    [Fact]
    public void DestroyedEntity_IsRemovedFromNetworkEntityMap_OnIg()
    {
        using var harness = new BagiraRunnerHarness();

        long networkId = SpawnEntityThroughPlacement(harness);

        var igMap = harness.Ig.App.TestHook_EntityMap;
        bool igEntityAppeared = harness.PumpUntil(
            () => igMap.TryGetEntity(networkId, out _),
            SpawnTimeoutFrames);
        Assert.True(igEntityAppeared, "Entity did not appear in IG NetworkEntityMap.");

        harness.SimHost.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason = "ZombieEntityMapTests-IG"
        });

        bool removedFromIgMap = harness.PumpUntil(
            () => !igMap.TryGetEntity(networkId, out _),
            MapPruneTimeoutFrames);

        Assert.True(removedFromIgMap,
            "NetworkEntityMap was not pruned after remote destroy on IG.");
    }

    private static long SpawnEntityThroughPlacement(BagiraRunnerHarness harness)
    {
        var iosLogic = harness.Ios.Logic;
        var igApp = harness.Ig.App;
        long tkbType = TkbEntityTypes.Tank_M1Abrams;

        iosLogic.StartPlacementMode(tkbType);

        bool configSynced = harness.PumpUntil(
            () => iosLogic.ActiveContextId != Guid.Empty
               && igApp.TestHook_ActiveContextId == iosLogic.ActiveContextId,
            SpawnTimeoutFrames);
        Assert.True(configSynced, "MapInteractionConfig did not reach IG in time.");

        igApp.TestHook_SimulateMapClick(new Vector2(100f, 200f));

        bool simHostSpawned = harness.PumpUntil(
            () => TryGetSimHostNetworkId(harness.SimHost.World, tkbType, out _),
            SpawnTimeoutFrames);
        Assert.True(simHostSpawned, "SimHost did not spawn an entity in time.");

        Assert.True(TryGetSimHostNetworkId(harness.SimHost.World, tkbType, out long networkId));
        return networkId;
    }

    private static bool TryGetSimHostNetworkId(EntityRepository world, long tkbType, out long networkId)
    {
        var view = (ISimulationView)world;
        var query = world.Query().IncludeAll().With<NetworkIdentity>().With<TkbIdentity>().Build();
        foreach (var entity in query)
        {
            var tkbId = view.GetComponentRO<TkbIdentity>(entity);
            if (tkbId.TkbType != tkbType)
                continue;

            var netId = view.GetComponentRO<NetworkIdentity>(entity);
            networkId = netId.Value;
            return true;
        }

        networkId = 0;
        return false;
    }
}
