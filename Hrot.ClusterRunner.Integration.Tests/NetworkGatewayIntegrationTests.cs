using System;
using System.Threading;
using Fdp.Kernel;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.ClusterRunner.Configuration;
using Hrot.Map.Common;
using Hrot.NED.Common;
using ModuleHost.Core.Network.Interfaces;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK3-N004 — <c>NetworkGatewaySystem</c> integration test.
///
/// <para>Proves that a <see cref="SpawnEntityCommand"/> with
/// <see cref="ReliableInitType.AllPeers"/> published on the SimHost bus reaches
/// <see cref="EntityLifecycle.Active"/> on <em>both</em> SimHost and IG via the
/// canonical CycloneDDS loopback transport.</para>
///
/// <para>Architecture proof: the test compiles and passes after PACK3-N002 (deletion
/// of legacy <c>NetworkGatewaySystem</c> clones), confirming that <c>CycloneNetworkModule</c>
/// is correctly wired to the toolkit-canonical implementation.</para>
/// </summary>
[Collection("LogCapture")]
public sealed class NetworkGatewayIntegrationTests
{
    // Domain range: 230.  Must not overlap with other test classes.
    // UrbanCombatFileLifecycleTests = 228, AclBackdoorEliminationTests = 229.
    private const int DomainBase = 230;
    private static int _domainCounter = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainCounter);

    private const int EntityInMapTimeoutFrames    = 60;
    private const int LifecycleActiveTimeoutFrames = 150;

    /// <summary>
    /// PACK3-N004 — main integration test.
    ///
    /// <list type="number">
    /// <item>Publish <c>AllPeers</c> <see cref="SpawnEntityCommand"/> on SimHost bus.</item>
    /// <item>PumpUntil SimHost <c>NetworkEntityMap</c> contains the entity.</item>
    /// <item>PumpUntil SimHost entity reaches <see cref="EntityLifecycle.Active"/>.</item>
    /// <item>PumpUntil IG entity reaches <see cref="EntityLifecycle.Active"/>.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void GenericNetworkGateway_ResolvesReliableInit_AcrossCycloneTransport()
    {
        int domainId = NextDomainId();

        using var harness = new HrotRunnerHarness(
            RunMode.SimHost | RunMode.IG,
            domainId);

        // Spawn via TestHook so we get back the allocated networkId immediately.
        // TestHook_SpawnEntity uses InitType = AllPeers internally.
        var spawnPos  = new GeoPoint { Latitude = 52.52, Longitude = 13.405, Altitude = 0.0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            spawnPos);

        // ── Step 1: SimHost NetworkEntityMap must record the entity ──────────
        bool inSimHostMap = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            EntityInMapTimeoutFrames);

        Assert.True(inSimHostMap,
            $"Entity {networkId} did not appear in SimHost NetworkEntityMap within " +
            $"{EntityInMapTimeoutFrames} frames.");

        // ── Step 2: SimHost entity must reach Active ──────────────────────────
        bool simHostActive = harness.PumpUntil(
            () => SimHostEntityIsActive(harness, networkId),
            LifecycleActiveTimeoutFrames);

        Assert.True(simHostActive,
            $"SimHost entity {networkId} did not reach EntityLifecycle.Active within " +
            $"{LifecycleActiveTimeoutFrames} frames.");

        // ── Step 3: IG entity must reach Active ───────────────────────────────
        bool igActive = harness.PumpUntil(
            () => IgEntityIsActive(harness, networkId),
            LifecycleActiveTimeoutFrames);

        Assert.True(igActive,
            $"IG entity {networkId} did not reach EntityLifecycle.Active within " +
            $"{LifecycleActiveTimeoutFrames} frames.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool SimHostEntityIsActive(HrotRunnerHarness harness, long networkId)
    {
        var world = harness.SimHost.World;
        if (world == null) return false;
        if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var entity))
            return false;
        if (!world.IsAlive(entity)) return false;
        return world.GetLifecycleState(entity) == EntityLifecycle.Active;
    }

    private static bool IgEntityIsActive(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity)) return false;
        var world = harness.Ig.App.World;
        if (!world.IsAlive(entity)) return false;
        return world.GetLifecycleState(entity) == EntityLifecycle.Active;
    }
}
