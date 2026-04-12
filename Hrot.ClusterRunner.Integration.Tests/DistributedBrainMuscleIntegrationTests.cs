using System;
using FDP.Toolkit.NetworkSpawning.Events;
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
    // Domain range starting after HrotRunnerHarness (100–199) and CgfHarness (200–219).
    // Must stay within CycloneDDS valid range (0–232); previous value of 299 was out of range.
    private static int _domainCounter = 219;

    private const int SpawnPropagationTimeoutMs  = 5_000;
    private const int MissionAssignmentTimeoutMs = 10_000;

    // ── IT-4a ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SpawnedEntity_ReachesToCgf_ViaDds()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new GeoPoint { Latitude = 52.52, Longitude = 13.405, Altitude = 0.0 };

        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        bool reached = harness.PumpUntil(
            () =>
            {
                var map = harness.Cgf!.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnPropagationTimeoutMs / 5);

        Assert.True(reached,
            $"Entity {networkId} should appear in CGF ghost map within {SpawnPropagationTimeoutMs} ms");
    }

    // ── IT-4b ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DestroyedEntity_PurgedFromCgfGhostRepo()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long tkbType  = TkbEntityTypes.Tank_M1Abrams;
        var  spawnPos = new GeoPoint { Latitude = 52.52, Longitude = 13.405, Altitude = 0.0 };

        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        // Wait until entity appears in CGF
        bool appeared = harness.PumpUntil(
            () =>
            {
                var map = harness.Cgf!.GhostEntityMap;
                return map != null && map.TryGetEntity(networkId, out _);
            },
            SpawnPropagationTimeoutMs / 5);
        Assert.True(appeared, "Entity must appear in CGF before we can test its removal");

        // Destroy via SimHost bus
        harness.SimHost.App.World.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason    = "test-destroy",
        });

        bool purged = harness.PumpUntil(
            () =>
            {
                var map = harness.Cgf?.GhostEntityMap;
                // Purged when map is null (after shutdown) or entity no longer present
                return map == null || !map.TryGetEntity(networkId, out _);
            },
            SpawnPropagationTimeoutMs / 5);

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
    // (no PumpBothUntil needed — tests now use harness.PumpUntil directly)
}
