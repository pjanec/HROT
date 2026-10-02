using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Hrot.Map.Common;
using Hrot.NED.Descriptors;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using WireOwnershipUpdate = Fdp.Network.Cyclone.Topics.OwnershipUpdate;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ Ownership build S7 (R-167): a node that LEAVES gives back what it owned, on every node, with no message. The
/// departing node is a foreign DDS participant (node 77) the test drives directly — it heartbeats, takes or is given
/// ownership, then disposes its heartbeat instance, which is what a clean exit does (a crash reaches the same ingest
/// path as not-alive-no-writers once the participant lease expires). 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c>
/// §5.3, §5.6 S7, §5.7 E8.
/// </summary>
public class PartialOwnerReclaimTests
{
    private const int FakeNode = 77;
    private static int _domainCounter = 120;

    private static long Key(EDescriptorType d) => OwnershipExtensions.PackKey((long)d, 0);

    private static void Heartbeat(DdsWriter<NodeHeartbeat> hb)
        => hb.Write(new NodeHeartbeat
        {
            NodeId = FakeNode, SubsystemName = "fake-leaver", WallTicksUtc = DateTime.UtcNow.Ticks, SubsystemsJson = "[]",
        });

    /// <summary>Pumps while node 77 keeps heartbeating (and runs <paramref name="alsoEachTick"/>), as a live node does —
    /// a sample written before discovery has matched the readers is simply lost.</summary>
    private static bool PumpAlive(HrotRunnerHarness harness, DdsWriter<NodeHeartbeat> hb, Func<bool> condition,
                                  int timeoutFrames, Action? alsoEachTick = null)
    {
        int frame = 0;
        return harness.PumpUntil(() =>
        {
            if (frame++ % 10 == 0) { Heartbeat(hb); alsoEachTick?.Invoke(); }
            return condition();
        }, timeoutFrames);
    }

    /// <summary>Q79 P2: CGF grants the kinematic group to a node that never takes over; when that node leaves, CGF
    /// (which yielded the claim at creation) takes the grant back and owns its position again.</summary>
    [Fact(Timeout = 90_000)]
    public void AGrantToANodeThatLeavesBeforeTakingOver_IsTakenBackByTheCreator()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);
        using var fake = new DdsParticipant((uint)domainId);
        using var hb   = new DdsWriter<NodeHeartbeat>(fake);
        Heartbeat(hb);

        var matched = DateTime.UtcNow.AddSeconds(2);                                // let discovery match node 77
        PumpAlive(harness, hb, () => DateTime.UtcNow > matched, timeoutFrames: 5000);

        long net = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(TkbEntityTypes.Tank_M1Abrams, muscleNodeId: FakeNode);
        var cgf = harness.Cgf!.World!;
        Entity tank = Entity.Null;
        Assert.True(PumpAlive(harness, hb, () => harness.Cgf!.GhostEntityMap!.TryGetEntity(net, out tank)
                                         && cgf.HasComponent<SimTransform>(tank)
                                         && cgf.HasManagedComponent<OutgoingGrantsPending>(tank), timeoutFrames: 2000),
            "CGF must create the tank and hold its kinematic grant to node 77 as pending.");
        Assert.False(cgf.HasAuthority<SimTransform>(tank));                         // yielded at creation

        // Node 77 lives a while (as any node does before leaving): a reader that never received one of its
        // heartbeats has no instance for the dispose to end.
        var until = DateTime.UtcNow.AddSeconds(3);                                  // wall clock: frames are ~5 ms
        PumpAlive(harness, hb, () => DateTime.UtcNow > until, timeoutFrames: 5000);
        Assert.True(cgf.HasManagedComponent<OutgoingGrantsPending>(tank), "Node 77 never takes over, so the grant stays pending.");

        hb.DisposeInstance(new NodeHeartbeat { NodeId = FakeNode });                 // node 77 leaves

        Assert.True(harness.PumpUntil(() => cgf.HasAuthority<SimTransform>(tank), timeoutFrames: 2000),
            $"CGF must take back the kinematic group once its grantee has left. {Describe(cgf, tank)}");
        Assert.True(((ISimulationView)cgf).HasAuthority(tank, Key(EDescriptorType.dtWorldPos)));
        Assert.False(cgf.HasManagedComponent<OutgoingGrantsPending>(tank));
    }

    /// <summary>An external node is handed the position (spec <c>OwnershipUpdate</c>, Q79 §0.12 E2), then leaves: every
    /// node records the primary owner (CGF) again, and CGF alone claims it.</summary>
    [Fact(Timeout = 90_000)]
    public void WhatALeavingNodeOwned_ReturnsToThePrimaryOwner_OnEveryNode()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);
        using var fake  = new DdsParticipant((uint)domainId);
        using var hb    = new DdsWriter<NodeHeartbeat>(fake);
        using var owner = new DdsWriter<WireOwnershipUpdate>(fake);
        Heartbeat(hb);

        PumpAlive(harness, hb, () => false, timeoutFrames: 60);                     // let discovery match node 77

        long net = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        Entity cgfTank = Entity.Null, simTank = Entity.Null;
        long worldPos = Key(EDescriptorType.dtWorldPos);
        Assert.True(PumpAlive(harness, hb, () => harness.Cgf!.GhostEntityMap!.TryGetEntity(net, out cgfTank)
                                         && harness.SimHost.TestHook_EntityMap.TryGetEntity(net, out simTank)
                                         && ((ISimulationView)sim).HasAuthority(simTank, worldPos), timeoutFrames: 3000),
            "SimHost must take over the tank's position (the kinematic grant).");

        // Node 77 is handed the position, as an external node would be (re-sent until both nodes have it).
        Assert.True(PumpAlive(harness, hb, () => Recorded(cgf, cgfTank, worldPos) == FakeNode
                                              && Recorded(sim, simTank, worldPos) == FakeNode, timeoutFrames: 2000,
            alsoEachTick: () => owner.Write(new WireOwnershipUpdate
            {
                EntityId = net, DescrTypeId = (long)EDescriptorType.dtWorldPos, InstanceId = 0, NewOwner = FakeNode, OriginNodeId = FakeNode,
            })),
            "Both nodes must record node 77 as the position's owner.");
        Assert.False(sim.HasAuthority<SimTransform>(simTank));

        hb.DisposeInstance(new NodeHeartbeat { NodeId = FakeNode });                 // node 77 leaves

        int cgfNode = harness.Cgf!.TestHook_NodeId;
        Assert.True(harness.PumpUntil(() => Recorded(cgf, cgfTank, worldPos) == cgfNode
                                         && Recorded(sim, simTank, worldPos) is int simRecord && simRecord != FakeNode,
                timeoutFrames: 2000),
            $"Every node must stop naming the departed node. cgf={Recorded(cgf, cgfTank, worldPos)} sim={Recorded(sim, simTank, worldPos)}");
        Assert.True(cgf.HasAuthority<SimTransform>(cgfTank));                             // the primary owner claims it…
        Assert.True(((ISimulationView)cgf).HasAuthority(cgfTank, worldPos));              // …and publishes it
        Assert.False(sim.HasAuthority<SimTransform>(simTank));                            // nobody else claims it…
        Assert.False(((ISimulationView)sim).HasAuthority(simTank, worldPos));             // …or publishes it
        // ⚠ CE-517: a replica does not know the entity's primary owner (EntityMaster carries no owner, so its
        //   NetworkAuthority.PrimaryOwnerId reads -1) — SimHost records the reclaimed key as -1 ("not me"), not 400.
    }

    private static string Describe(EntityRepository world, Entity e)
    {
        var pending = world.HasManagedComponent<OutgoingGrantsPending>(e)
            ? string.Join(",", System.Linq.Enumerable.Select(world.GetComponent<OutgoingGrantsPending>(e).Descriptors, kv => $"{kv.Key}->{kv.Value}"))
            : "none";
        var record = world.HasManagedComponent<DescriptorOwnership>(e)
            ? string.Join(",", System.Linq.Enumerable.Select(world.GetComponent<DescriptorOwnership>(e).Map, kv => $"{kv.Key >> 32}:{kv.Value}"))
            : "none";
        return $"pending=[{pending}] record=[{record}] simTransformClaimed={world.HasAuthority<SimTransform>(e)}";
    }

    private static int? Recorded(EntityRepository world, Entity e, long key)
        => world.HasManagedComponent<DescriptorOwnership>(e) &&
           world.GetComponent<DescriptorOwnership>(e).TryGetOwner(key, out int o) ? o : null;
}
