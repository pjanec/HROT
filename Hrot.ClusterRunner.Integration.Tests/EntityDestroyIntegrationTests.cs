using System;
using System.Numerics;
using Hrot.NED.Messages;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Hrot.Map.Common;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

public class EntityDestroyIntegrationTests
{
    private const int ConfigSyncTimeoutFrames = 100;
    private const int SimHostSpawnTimeoutFrames = 100;
    private const int IgSpawnTimeoutFrames = 100;
    private const int DestroyTimeoutFrames = 100;

    [Fact]
    public void SimHost_DestroyEntity_IgGhostIsRemoved()
    {
        using var harness = new HrotRunnerHarness();

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

    private static long SpawnEntityThroughPlacement(HrotRunnerHarness harness, long tkbType)
    {
        var iosLogic = harness.ExCon.Logic;
        var igApp = harness.Ig.App;

        iosLogic.StartPlacementMode(tkbType);
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

    /// <summary>
    /// Verifies the "ClearAll" destroy pattern: multiple entities destroyed in quick
    /// succession via <see cref="DestroyEntityCommand"/> all have their IG ghosts removed.
    ///
    /// <para>
    /// This exercises the same code path that <c>SimHostScenarioManager.ClearAll()</c>
    /// now uses after the Task 33 fix.  Before the fix, <c>ClearAll</c> called
    /// <c>_repo.DestroyEntity(e)</c> directly, which bypassed the
    /// <c>NetworkSpawningSystem</c> → DDS EntityMaster DISPOSE chain and left stale
    /// ghost entities on the IG map.
    /// </para>
    /// </summary>
    [Fact]
    public void SimHost_ClearAllPattern_AllIgGhostsRemoved()
    {
        using var harness = new HrotRunnerHarness();

        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        var geo1 = new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };
        var geo2 = new CoreGeoPoint { Latitude = 52.522, Longitude = 13.407, Altitude = 0 };

        // ── Spawn two entities ────────────────────────────────────────────────
        long networkId1 = SpawnEntityThroughPlacement(harness, tkbType);
        long networkId2 = harness.SimHost.TestHook_SpawnEntity(tkbType, geo2);

        bool bothOnIg = harness.PumpUntil(
            () => IgHasEntityWithNetworkId(harness.Ig.App.World, networkId1)
               && IgHasEntityWithNetworkId(harness.Ig.App.World, networkId2),
            IgSpawnTimeoutFrames);
        Assert.True(bothOnIg,
            $"Both entities (networkId1={networkId1}, networkId2={networkId2}) " +
            $"did not appear on IG within {IgSpawnTimeoutFrames} frames.");

        // ── Destroy both in quick succession (ClearAll pattern) ───────────────
        // This mirrors what SimHostScenarioManager.ClearAll() does after the Task 33 fix.
        harness.SimHost.World!.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId1,
            Reason    = "clear-all-test",
        });
        harness.SimHost.World!.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId2,
            Reason    = "clear-all-test",
        });

        // ── Verify both IG ghosts are removed ─────────────────────────────────
        bool ghost1Removed = harness.PumpUntil(
            () => !IgHasEntityWithNetworkId(harness.Ig.App.World, networkId1),
            DestroyTimeoutFrames);
        Assert.True(ghost1Removed,
            $"IG ghost for networkId1={networkId1} was not removed after DestroyEntityCommand. " +
            $"ClearAll fix may not be routing through NetworkSpawningSystem.");

        bool ghost2Removed = harness.PumpUntil(
            () => !IgHasEntityWithNetworkId(harness.Ig.App.World, networkId2),
            DestroyTimeoutFrames);
        Assert.True(ghost2Removed,
            $"IG ghost for networkId2={networkId2} was not removed after DestroyEntityCommand. " +
            $"ClearAll fix may not be routing through NetworkSpawningSystem.");
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
