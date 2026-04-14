using System;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Hrot.Map.Common;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

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
        using var harness = new HrotRunnerHarness();

        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new CoreGeoPoint { Latitude = 32.0, Longitude = 34.0 });

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
        using var harness = new HrotRunnerHarness();

        // Spawn directly on SimHost so SimHost is the authority (NetworkOwnership.HasAuthority=true).
        // This ensures CycloneNetworkCleanupSystem fires on destroy and sends EntityMaster DISPOSE
        // to DDS, which allows IG to detect the removal and prune its NetworkEntityMap.
        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new CoreGeoPoint { Latitude = 32.0, Longitude = 34.0 });

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
}
