using System;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.Core.Mission;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK3-N004 â€” <c>NetworkGatewaySystem</c> integration test.
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
    /// PACK3-N004 â€” main integration test.
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
            "simhost,ig",
            domainId);

        // Spawn via TestHook so we get back the allocated networkId immediately.
        // TestHook_SpawnEntity uses InitType = AllPeers internally.
        var spawnPos  = new GeoPoint { Latitude = 52.52, Longitude = 13.405, Altitude = 0.0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            spawnPos);

        // â”€â”€ Step 1: SimHost NetworkEntityMap must record the entity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        bool inSimHostMap = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            EntityInMapTimeoutFrames);

        Assert.True(inSimHostMap,
            $"Entity {networkId} did not appear in SimHost NetworkEntityMap within " +
            $"{EntityInMapTimeoutFrames} frames.");

        // â”€â”€ Step 2: SimHost entity must reach Active â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        bool simHostActive = harness.PumpUntil(
            () => SimHostEntityIsActive(harness, networkId),
            LifecycleActiveTimeoutFrames);

        Assert.True(simHostActive,
            $"SimHost entity {networkId} did not reach EntityLifecycle.Active within " +
            $"{LifecycleActiveTimeoutFrames} frames.");

        // â”€â”€ Step 3: IG entity must reach Active â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        bool igActive = harness.PumpUntil(
            () => IgEntityIsActive(harness, networkId),
            LifecycleActiveTimeoutFrames);

        Assert.True(igActive,
            $"IG entity {networkId} did not reach EntityLifecycle.Active within " +
            $"{LifecycleActiveTimeoutFrames} frames.");
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // ── CE-283 reliable-init construction barrier — the barrier ENGAGES and RELEASES over the wire ──

    /// <summary>
    /// The definitive barrier proof (CE-283, DESIGN_Cross_Node_Construction_Barrier.md §6): a CGF (Brain)
    /// node creates a reliable (AllPeers) entity while a SimHost (Muscle) peer is present. The creator's
    /// <c>NetworkGatewaySystem</c> stamps the present peers onto the entity and holds it in
    /// <c>Constructing</c>; the entity can only reach <c>Active</c> once SimHost has ghosted it, reached
    /// <c>Active</c>, and published its <c>EntityLifecycleStatusDescriptor</c> back over CycloneDDS
    /// (A4 → the creator ingress → <c>ReceiveLifecycleStatus</c>).
    ///
    /// <para>Two assertions make this a barrier proof rather than a plain liveness check: (1) the creator
    /// entity reaches Active — the peer's Active-ack travelled the wire and released the gateway; (2) the
    /// entity carries a NON-EMPTY <c>NetworkAckPeerSet</c> — so the barrier genuinely engaged (the A5
    /// provider found a peer), not bypassed via an empty peer set. ⚠ Slice A's provider is
    /// proof-correct (all present peers); piece C narrows it by role (§3a.7).</para>
    /// </summary>
    [Fact]
    public void ReliableInitBarrier_EngagesAndReleases_WhenPeerReportsActiveOverWire()
    {
        int domainId = NextDomainId();
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        // Warm up so CGF's cluster cache learns SimHost from its heartbeats — the barrier's peer source.
        PumpBoth(simHost, cgf, 250);

        // CGF (Brain) creates a reliable (AllPeers) entity; the barrier stamps the present peers (SimHost=1).
        const int SimHostNodeId = 1;
        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: SimHostNodeId);

        // (1) The creator's own entity must reach Active — only possible once SimHost's Active status
        //     travelled back over DDS and released CGF's gateway.
        bool cgfActive = PumpBothUntil(simHost, cgf,
            () => CgfEntityIsActive(cgf, networkId),
            timeoutMs: 8000);
        Assert.True(cgfActive,
            $"CGF reliable entity {networkId} never reached Active — the peer Active-ack did not release the barrier.");

        // (2) The barrier genuinely ENGAGED: the entity carries a non-empty stamped peer set that
        //     includes SimHost. (NetworkAckPeerSet persists after ack — only PendingNetworkAck is stripped.)
        var world = cgf.CgfSvc.World!;
        Assert.True(cgf.CgfSvc.GhostEntityMap!.TryGetEntity(networkId, out var entity),
            "creator entity not found in CGF entity map.");
        Assert.True(world.HasManagedComponent<NetworkAckPeerSet>(entity),
            "creator entity has no NetworkAckPeerSet — the barrier was bypassed (empty peer set / provider unwired).");
        var peerSet = world.GetComponent<NetworkAckPeerSet>(entity);
        Assert.NotEmpty(peerSet.ExpectedAckPeers);
        Assert.Contains(SimHostNodeId, peerSet.ExpectedAckPeers);
    }

    private static bool CgfEntityIsActive(CgfHarness cgf, long networkId)
    {
        var world = cgf.CgfSvc.World;
        if (world == null) return false;
        var map = cgf.CgfSvc.GhostEntityMap;
        if (map == null || !map.TryGetEntity(networkId, out var entity)) return false;
        if (!world.IsAlive(entity)) return false;
        return world.GetLifecycleState(entity) == EntityLifecycle.Active;
    }

    private static void PumpBoth(HrotRunnerHarness simHost, CgfHarness cgf, int frames)
    {
        for (int i = 0; i < frames; i++) { simHost.PumpFrames(1); cgf.PumpFrames(1); Thread.Sleep(2); }
    }

    private static bool PumpBothUntil(HrotRunnerHarness simHost, CgfHarness cgf, Func<bool> condition, int timeoutMs)
    {
        if (condition()) return true;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            simHost.PumpFrames(1);
            cgf.PumpFrames(1);
            if (condition()) return true;
            Thread.Sleep(2);
        }
        return false;
    }
}
