using System;
using System.Threading;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.ClusterRunner.Configuration;
using Hrot.Map.Common;
using Hrot.NED.Common;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK2-R006 — IT-4: Distributed Brain-Muscle integration tests.
/// Pairs one SimHost harness with one CGF harness sharing the same CycloneDDS loopback domain.
/// These tests require CycloneDDS native libraries; they will skip/fail gracefully
/// on machines without DDS support.
/// </summary>
public sealed class DistributedBrainMuscleIntegrationTests
{
    // Domain range starting after HrotRunnerHarness (100–199) and CgfHarness (200–299) ranges.
    private static int _domainCounter = 299;

    private const int SpawnPropagationTimeoutMs  = 5_000;
    private const int MissionAssignmentTimeoutMs = 10_000;

    // ── IT-4a ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SpawnedEntity_ReachesToCgf_ViaDds()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness(RunMode.SimHost, domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new GeoPoint { Latitude = 52.52, Longitude = 13.405, Altitude = 0.0 };

        long networkId = simHost.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        bool reached = PumpBothUntil(
            simHost, cgf,
            () =>
            {
                var map = cgf.CgfSvc.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnPropagationTimeoutMs);

        Assert.True(reached,
            $"Entity {networkId} should appear in CGF ghost map within {SpawnPropagationTimeoutMs} ms");
    }

    // ── IT-4b ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DestroyedEntity_PurgedFromCgfGhostRepo()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness(RunMode.SimHost, domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new GeoPoint { Latitude = 52.52, Longitude = 13.405, Altitude = 0.0 };

        long networkId = simHost.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        // Wait until entity appears in CGF
        bool appeared = PumpBothUntil(simHost, cgf,
            () =>
            {
                var map = cgf.CgfSvc.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnPropagationTimeoutMs);
        Assert.True(appeared, "Entity must appear in CGF before we can test its removal");

        // Destroy via SimHost bus
        simHost.SimHost.App.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason    = "test-destroy",
        });

        bool purged = PumpBothUntil(simHost, cgf,
            () =>
            {
                var map = cgf.CgfSvc.GhostEntityMap;
                // Purged when map is null (after shutdown) or entity no longer present
                return map == null || !map.TryGetEntity(networkId, out _);
            },
            SpawnPropagationTimeoutMs);

        Assert.True(purged,
            $"Entity {networkId} must be purged from CGF ghost map within {SpawnPropagationTimeoutMs} ms");
    }

    // ── IT-4c ─────────────────────────────────────────────────────────────────

    [Fact(Skip = "CGF AI mission assignment round-trip not deterministically testable without ExCon MissionControlRequest chain; NavigationIntent is set only after full doctrine activation.")]
    public void CgfAiIntent_ReachesSimHost_ViaDds()
    {
        // This test requires CGF to receive a doctrine assignment via MissionControlRequest (DDS),
        // activate a navigation executor, and publish NavigationIntent back to SimHost via DDS.
        // The full chain requires ExCon participation which is not part of the SimHost-only harness.
        // Placeholder for future implementation when the ExCon can be driven offline.
        Assert.True(false, "Not implemented — see skip reason above.");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static bool PumpBothUntil(
        HrotRunnerHarness simHost,
        CgfHarness        cgf,
        Func<bool>        condition,
        int               timeoutMs)
    {
        if (condition()) return true;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            simHost.PumpFrames(1);
            cgf.PumpFrames(1);
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return false;
    }
}
