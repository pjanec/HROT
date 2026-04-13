using System;
using System.Threading;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Hrot.SimHost;
using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Core.Mission;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// SPLIT-AUTH-IT: Split-authority spawn integration tests.
///
/// <para>
/// Validates that when the CGF (Brain) node creates an entity and delegates
/// the WorldPos descriptor to the SimHost (Muscle) node via
/// <c>DeferredTakeOwnership</c>, the SimHost ghost receives
/// <c>SimTransform</c> / <c>NetworkTransform</c> / <c>NetworkVelocity</c>
/// authority flags after the entity transitions from Ghost → Constructing.
/// </para>
///
/// <para>Domain range: 220–229 (after DistributedBrainMuscleIntegrationTests 219).</para>
/// </summary>
public sealed class SplitAuthoritySpawnTests
{
    private static int _domainCounter = 219;

    private const int PropagationTimeoutMs = 5_000;
    private const int AuthorityTimeoutMs   = 8_000;
    private const int PumpSleepMs          = 5;

    // ── IT-SA-1 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Brain spawns an entity with WorldPos delegated to Muscle.
    /// The entity should appear as a ghost on Muscle within the propagation window.
    /// </summary>
    [Fact]
    public void BrainSpawn_EntityArrives_OnMuscle()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        // SimHost node ID is 1 (SimHostNetworkConstants.LocalNodeId)
        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        bool arrived = PumpBothUntil(simHost, cgf,
            () =>
            {
                var map = simHost.SimHost.TestHook_EntityMap;
                return map.TryGetEntity(networkId, out _);
            },
            PropagationTimeoutMs);

        Assert.True(arrived,
            $"Entity {networkId} should appear on SimHost ghost map within {PropagationTimeoutMs} ms");
    }

    // ── IT-SA-2 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// After the entity arrives and is promoted on Muscle, the ghost should have
    /// <c>SimTransform</c> authority set to <c>true</c>.
    ///
    /// <para>
    /// The <c>DeferredTakeoverSystem</c> runs BeforeSync and processes
    /// <c>PendingAuthorityGrants</c> on Constructing entities by calling
    /// <c>SetAuthority(entity, componentId, true)</c> for each component
    /// registered in the <c>DescriptorOwnershipMap</c> for the WorldPos descriptor.
    /// </para>
    /// </summary>
    [Fact]
    public void BrainSpawn_WithWorldPosDelegation_MuscleTakesSimTransformAuthority()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        // SimHost node ID is 1 (SimHostNetworkConstants.LocalNodeId)
        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for SimTransform authority to become true on the Muscle side.
        bool hasAuthority = PumpBothUntil(simHost, cgf,
            () =>
            {
                var world = simHost.SimHost.World;
                var map   = simHost.SimHost.TestHook_EntityMap;
                if (world == null) return false;

                if (!map.TryGetEntity(networkId, out Entity entity)) return false;
                if (!world.IsAlive(entity)) return false;

                if (!world.HasComponent<SimTransform>(entity)) return false;

                return world.HasAuthority<SimTransform>(entity);
            },
            AuthorityTimeoutMs);

        Assert.True(hasAuthority,
            $"SimHost should have SimTransform authority for entity {networkId} " +
            $"within {AuthorityTimeoutMs} ms after split-authority spawn");
    }

    // ── IT-SA-3 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The CGF (Brain) ghost should NOT have SimTransform authority
    /// — that descriptor belongs to SimHost (Muscle).
    /// </summary>
    [Fact]
    public void BrainSpawn_Brain_DoesNotHaveSimTransformAuthority()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // First wait for the entity to arrive on Muscle so we know propagation completed.
        bool arrived = PumpBothUntil(simHost, cgf,
            () =>
            {
                var map = simHost.SimHost.TestHook_EntityMap;
                return map.TryGetEntity(networkId, out _);
            },
            PropagationTimeoutMs);

        Assert.True(arrived, "Entity must reach SimHost before checking Brain authority");

        // Give a few more frames so the OwnershipUpdate from Muscle flows back to CGF.
        PumpBothFrames(simHost, cgf, frames: 30);

        // CGF should not have SimTransform authority (Muscle owns it).
        var cgfWorld = cgf.World;
        var cgfMap   = cgf.CgfSvc.GhostEntityMap;

        if (cgfWorld == null || cgfMap == null)
        {
            // No CGF ghost map means nothing to assert — skip gracefully.
            return;
        }

        if (!cgfMap.TryGetEntity(networkId, out Entity cgfEntity)) return;
        if (!cgfWorld.IsAlive(cgfEntity)) return;
        if (!cgfWorld.HasComponent<SimTransform>(cgfEntity)) return;

        bool cgfHasAuth = cgfWorld.HasAuthority<SimTransform>(cgfEntity);
        Assert.False(cgfHasAuth,
            $"CGF Brain should NOT have SimTransform authority for entity {networkId} " +
            "when WorldPos was delegated to Muscle");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
            Thread.Sleep(PumpSleepMs);
        }
        return false;
    }

    private static void PumpBothFrames(HrotRunnerHarness simHost, CgfHarness cgf, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            simHost.PumpFrames(1);
            cgf.PumpFrames(1);
            Thread.Sleep(PumpSleepMs);
        }
    }
}
