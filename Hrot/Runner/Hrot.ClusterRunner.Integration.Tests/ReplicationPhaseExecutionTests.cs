using System;
using Hrot.Core.Mission;
using Hrot.Map.Common;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using Fdp.Kernel;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Verifies that DisposalMonitoringSystem and SubEntityCleanupSystem
/// execute after the SimWrapper phase fix and their side-effects are observable.
/// </summary>
public class ReplicationPhaseExecutionTests
{
    private const int TimeoutFrames = 60;

    /// <summary>
    /// Creates a real entity in the SimHost world, registers it in the NetworkEntityMap,
    /// then immediately destroys the entity. After pumping frames, the map should be pruned.
    /// </summary>
    [Fact]
    public void DisposalMonitoringSystem_PrunesMapAfterEntityDestroyed()
    {
        using var harness = new HrotRunnerHarness();

        var entityMap = harness.SimHost.TestHook_EntityMap;

        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new GeoPoint { Latitude = 32.0, Longitude = 34.0 });

        Entity simHostEntity = Entity.Null;
        bool registered = harness.PumpUntil(
            () => entityMap.TryGetEntity(networkId, out simHostEntity),
            TimeoutFrames);
        Assert.True(registered, "Entity was not registered in NetworkEntityMap.");

        harness.SimHost.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason = "REPL-P4-T1 test"
        });

        bool mapPruned = harness.PumpUntil(
            () => !entityMap.TryGetEntity(networkId, out _),
            TimeoutFrames);

        Assert.True(mapPruned,
            "NetworkEntityMap was NOT pruned after entity destruction. " +
            "This indicates DisposalMonitoringSystem is not executing. " +
            "Check that SimWrapper has been removed and the system is registered " +
            "with [UpdateInPhase(SystemPhase.PostSimulation)].");
    }
}
