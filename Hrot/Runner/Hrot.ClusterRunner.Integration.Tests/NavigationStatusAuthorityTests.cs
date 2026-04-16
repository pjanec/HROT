using System;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Navigation;
using Hrot.SimHost;
using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Core.Mission;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// NAV-STATUS-IT: Integration tests verifying the NavigationStatus split-authority pipeline.
///
/// <para>
/// Validates that when the CGF (Brain) node spawns an entity with both dtWorldPos
/// <em>and</em> dtNavigationStatus delegated to the SimHost (Muscle) node, the
/// NavigationStatus descriptor authority lands on the Muscle side. This is required
/// for the Muscle's <c>NavigationStatusEgressTranslator</c> to be permitted to publish
/// the status back to the Brain once the vehicle arrives at its destination.
/// </para>
///
/// <para>Domain range: 214–218 (below DistributedBrainMuscleIntegrationTests 219–221).</para>
/// </summary>
public sealed class NavigationStatusAuthorityTests
{
    private static int _domainCounter = 213;

    private const int PropagationTimeoutMs = 5_000;
    private const int AuthorityTimeoutMs   = 8_000;
    private const int PumpSleepMs          = 5;

    // ── IT-NAV-1 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// After the CGF spawns an entity with both dtWorldPos and dtNavigationStatus
    /// delegated to SimHost, the SimHost entity should gain <c>NavigationStatus</c>
    /// authority within the authority timeout.
    ///
    /// <para>
    /// This test pins the fix for the Authority Gate Failure described in the design
    /// talk: without granting dtNavigationStatus, the Muscle's
    /// <c>NavigationStatusEgressTranslator</c> silently dropped status packets,
    /// leaving the Brain's <c>MoveToExecutor</c> permanently in the Running state.
    /// </para>
    /// </summary>
    [Fact]
    public void BrainSpawn_WithNavigationStatusDelegation_MuscleTakesNavigationStatusAuthority()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        // SimHost node ID is 1 (SimHostNetworkConstants.LocalNodeId).
        // TestHook_SpawnEntityWithSplitAuthority now grants BOTH dtWorldPos and dtNavigationStatus.
        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        bool hasNavStatusAuthority = PumpBothUntil(simHost, cgf,
            () =>
            {
                var world = simHost.SimHost.World;
                var map   = simHost.SimHost.TestHook_EntityMap;
                if (world == null) return false;
                if (!map.TryGetEntity(networkId, out Entity entity)) return false;
                if (!world.IsAlive(entity)) return false;
                if (!world.HasComponent<NavigationStatus>(entity)) return false;

                return world.HasAuthority<NavigationStatus>(entity);
            },
            AuthorityTimeoutMs);

        Assert.True(hasNavStatusAuthority,
            $"SimHost should have NavigationStatus authority for entity {networkId} " +
            $"within {AuthorityTimeoutMs} ms after split-authority spawn " +
            "(required so NavigationStatusEgressTranslator can publish the Arrived status to Brain)");
    }

    // ── IT-NAV-2 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The CGF (Brain) ghost must NOT hold NavigationStatus authority after delegation
    /// to Muscle — that would prevent the Muscle from being the sole writer of nav status.
    /// </summary>
    [Fact]
    public void BrainSpawn_Brain_DoesNotHaveNavigationStatusAuthority()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for the entity to arrive on SimHost before checking Brain authority.
        bool arrived = PumpBothUntil(simHost, cgf,
            () =>
            {
                var map = simHost.SimHost.TestHook_EntityMap;
                return map.TryGetEntity(networkId, out _);
            },
            PropagationTimeoutMs);

        Assert.True(arrived, "Entity must reach SimHost before checking Brain NavigationStatus authority");

        // Give a few more frames so the OwnershipUpdate from Muscle flows back to CGF.
        PumpBothFrames(simHost, cgf, frames: 30);

        var cgfWorld = cgf.World;
        var cgfMap   = cgf.CgfSvc.GhostEntityMap;

        if (cgfWorld == null || cgfMap == null) return;
        if (!cgfMap.TryGetEntity(networkId, out Entity cgfEntity)) return;
        if (!cgfWorld.IsAlive(cgfEntity)) return;
        if (!cgfWorld.HasComponent<NavigationStatus>(cgfEntity)) return;

        bool cgfHasNavAuth = cgfWorld.HasAuthority<NavigationStatus>(cgfEntity);
        Assert.False(cgfHasNavAuth,
            $"CGF Brain should NOT have NavigationStatus authority for entity {networkId} " +
            "when dtNavigationStatus was delegated to Muscle");
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
