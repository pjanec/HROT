using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common;
using Hrot.Core.Mission;
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

    // ── IT-4d ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// IT-4d: Verifies the distributed CQRS damage pipeline across the Brain/Muscle boundary.
    ///
    /// <para>Flow under test:
    /// <list type="number">
    ///   <item>CGF (Brain) spawns an entity with split authority (SimHost owns WorldPos).</item>
    ///   <item>Entity appears in both SimHost and CGF worlds with a <see cref="Health"/> component.</item>
    ///   <item>A <see cref="DetonationNotification"/> is injected on the SimHost bus, simulating
    ///     <c>HitResolutionSystem</c> detecting a physical impact.</item>
    ///   <item><c>DamageCalculationSystem</c> (Muscle) emits <c>DamageAssessedEvent</c>;
    ///     <c>DamageAssessedEgressTranslator</c> broadcasts <c>EntityHitDamage</c> over DDS.</item>
    ///   <item><c>EntityHitDamageIngressTranslator</c> (Brain) delivers the event to CGF;
    ///     <c>HealthApplicationSystem</c> applies the damage on the authoritative
    ///     <see cref="Health"/> component and strips <see cref="ActorCapabilities.CanMove"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Fact]
    public void DistributedCombat_HitOnMuscle_AppliesDamageOnBrain()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        var spawnPos = new GeoPoint { Latitude = 52.52, Longitude = 13.405, Altitude = 0.0 };

        // CGF (Brain) spawns entity with split authority: WorldPos delegated to SimHost.
        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for entity to appear with Health on both nodes.
        bool entityReady = harness.PumpUntil(() =>
        {
            if (harness.SimHost.World == null) return false;
            if (!harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out var cgfEntity)) return false;
            if (!harness.Cgf.World!.IsAlive(cgfEntity)) return false;
            if (!harness.Cgf.World.HasComponent<Health>(cgfEntity)) return false;

            // Health is Brain-authoritative and not replicated to Muscle; check only
            // that the SimHost ghost entity is alive (needed to inject DetonationNotification).
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var simEntity)) return false;
            if (!harness.SimHost.World.IsAlive(simEntity)) return false;
            return true;
        }, SpawnPropagationTimeoutMs / 5);

        Assert.True(entityReady, $"Entity {networkId} with Health must appear on CGF and as a ghost on SimHost before damage injection.");

        // Resolve local ECS handles.
        harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var simHostEntity);
        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out var cgfEntityHandle);

        float healthBefore = harness.Cgf.World!.GetComponent<Health>(cgfEntityHandle).Max;

        // Inject DetonationNotification on SimHost bus, simulating HitResolutionSystem.
        harness.SimHost.App.World.Bus.Publish(new DetonationNotification
        {
            Shooter = Entity.Null,
            Target  = simHostEntity,
            HitX    = 0f,
            HitY    = 0f,
            HitZ    = 0f,
        });

        // The authoritative Health is on the CGF (Brain) node. Pump until it drops.
        bool brainTookDamage = harness.PumpUntil(() =>
        {
            if (!harness.Cgf!.World!.IsAlive(cgfEntityHandle)) return false;
            var health = harness.Cgf.World.GetComponent<Health>(cgfEntityHandle);
            return health.Current < healthBefore;
        }, SpawnPropagationTimeoutMs / 5);

        Assert.True(brainTookDamage,
            "CGF Brain Health must decrease after DetonationNotification injected on SimHost.");

        // Verify ActorCapabilityState.CanMove was stripped by HealthApplicationSystem.
        if (harness.Cgf.World!.HasComponent<ActorCapabilityState>(cgfEntityHandle))
        {
            var caps = harness.Cgf.World.GetComponent<ActorCapabilityState>(cgfEntityHandle);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove),
                "CanMove must be stripped by HealthApplicationSystem on a non-lethal hit.");
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    // (no PumpBothUntil needed — tests now use harness.PumpUntil directly)
}
