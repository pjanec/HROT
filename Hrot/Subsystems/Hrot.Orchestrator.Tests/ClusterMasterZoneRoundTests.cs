using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// C4 — the <see cref="LoadZoneIntent"/> consumer: ONE <c>PrepareZone</c> → <c>CommitZone</c> round
/// per zone.
///
/// <para>⛔ The red these rails were written against: the intent was PUBLISHED by the wire translator
/// and read by NOTHING — measured dead edge 1 of
/// <c>docs/DESIGN_Terrain_Zones_And_Assets.md</c> §4, while its own doc comment claimed a consumer.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.3 (one op per zone), §9.4 (not `_activeTransaction`),
///    §9.6 (always cluster-wide); docs/designs/mgmt-1/DESIGN.md §11.1 (the 2PC + its abort arm).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterZoneRoundTests
{
    private const int NodeA = 11;
    private const int NodeB = 12;

    /// <summary>Registers the given nodes with the roster; the latch is already clear (no mandatory).</summary>
    private static (ClusterMaster master, FdpEventBus bus) BootstrapWithNodes(params int[] nodeIds)
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus);

        foreach (var id in nodeIds)
        {
            bus.PublishManaged(new NodeHeartbeatEvent
            {
                NodeId        = id,
                LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
                WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
                SubsystemName = "SimHost",
            });
        }

        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        return (master, bus);
    }

    private static void PublishLoadZone(FdpEventBus bus, Guid requestId, string? zoneId)
        => bus.PublishManaged(new LoadZoneIntent { RequestId = requestId, ZoneId = zoneId });

    private static List<ExecuteNodeOpIntent> ReadOps(FdpEventBus bus, FdpNodeOpType op)
        => bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation == op).ToList();

    private static void AckAll(FdpEventBus bus, IEnumerable<ExecuteNodeOpIntent> ops,
                               OrchestrationStatusCode status = OrchestrationStatusCode.Success)
    {
        foreach (var op in ops)
            bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = op.TransactionId,
                Operation       = op.Operation,
                NodeId          = op.TargetNodeId,
                StatusCode      = status,
                IsParticipating = true,
            });
    }

    // ── The happy path ────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The whole contract in one rail: prepare goes to EVERY active node (§9.6 — always
    /// cluster-wide), commit follows only AFTER every node has staged, and the requester is told
    /// Success exactly once, at the END — never when phase 1 completes.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void LoadZoneIntent_FansOutPrepareThenCommit_AndReportsSuccessOnlyAtTheEnd()
    {
        var (master, bus) = BootstrapWithNodes(NodeA, NodeB);
        using var _ = master;

        var requestId = Guid.NewGuid();
        PublishLoadZone(bus, requestId, "zone-alpha");
        bus.SwapBuffers();
        master.Tick();

        // ── Phase 1: PrepareZone to both nodes, one transaction ──
        bus.SwapBuffers();
        var prepares = ReadOps(bus, FdpNodeOpType.PrepareZone);
        Assert.Equal(2, prepares.Count);
        Assert.Equal(new[] { NodeA, NodeB }, prepares.Select(p => p.TargetNodeId).OrderBy(x => x).ToArray());
        Assert.Single(prepares.Select(p => p.TransactionId).Distinct());
        Assert.Empty(ReadOps(bus, FdpNodeOpType.CommitZone));   // ⛔ not yet

        // Only ONE node ACKs — the barrier must hold.
        AckAll(bus, prepares.Take(1));
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        Assert.Empty(ReadOps(bus, FdpNodeOpType.CommitZone));

        // The second ACK releases it.
        AckAll(bus, prepares.Skip(1));
        bus.SwapBuffers();
        master.Tick();

        // ── Phase 2: CommitZone, and STILL no final status ──
        bus.SwapBuffers();
        var commits = ReadOps(bus, FdpNodeOpType.CommitZone);
        Assert.Equal(2, commits.Count);
        // ⭐⭐ ONE transaction id for BOTH phases — a round IS a transaction.
        // ⚠⚠ THIS ASSERTION WAS INVERTED AND WRONG until 2026-09-18. It read `NotEqual`, written when
        //    the first version of this round used a fresh id per phase. D3 then measured that the NODE
        //    stages under the prepare's id and consumes that staging in the commit, so two ids leave
        //    every commit unable to find its own prepare — and the master was changed to reuse one.
        //    ⛔ The rail was not updated with it, so it asserted the defect. 📄 design §10.3.
        Assert.Equal(prepares[0].TransactionId, commits[0].TransactionId);
        Assert.DoesNotContain(bus.ReadManaged<ClusterOpCompletedEvent>(),
            e => e.RequestId == requestId && !e.StatusCode.IsError());

        AckAll(bus, commits);
        bus.SwapBuffers();
        master.Tick();

        // ── Only now is the requester told Success ──
        bus.SwapBuffers();
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(),
            e => e.RequestId == requestId && !e.StatusCode.IsError());
    }

    /// <summary>
    /// ⭐⭐ §9.3, one op per zone — two intents in the same tick must open two INDEPENDENT rounds.
    /// ⛔ This is also the rail that pins §9.4: routing zone rounds through the single-slot
    /// <c>_activeTransaction</c> would make the second overwrite the first, and one of the two
    /// requesters would never hear back.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void TwoZonesInOneTick_RunAsTwoIndependentRounds()
    {
        var (master, bus) = BootstrapWithNodes(NodeA);
        using var _ = master;

        var requestAlpha = Guid.NewGuid();
        var requestBeta  = Guid.NewGuid();
        PublishLoadZone(bus, requestAlpha, "zone-alpha");
        PublishLoadZone(bus, requestBeta,  "zone-beta");
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var prepares = ReadOps(bus, FdpNodeOpType.PrepareZone);
        Assert.Equal(2, prepares.Count);
        Assert.Equal(2, prepares.Select(p => p.TransactionId).Distinct().Count());

        var zoneIds = prepares
            .Select(p => Assert.IsType<ZoneOpPayload>(p.DomainPayload).ZoneId)
            .OrderBy(z => z, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "zone-alpha", "zone-beta" }, zoneIds);

        AckAll(bus, prepares);
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var commits = ReadOps(bus, FdpNodeOpType.CommitZone);
        Assert.Equal(2, commits.Count);

        AckAll(bus, commits);
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.Contains(completed, e => e.RequestId == requestAlpha && !e.StatusCode.IsError());
        Assert.Contains(completed, e => e.RequestId == requestBeta  && !e.StatusCode.IsError());
    }

    // ── Failure and abort ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ A node that fails to STAGE must not leave the others holding staged buffers: the round
    /// aborts, no commit is ever sent, and the requester gets the failure.
    /// 📄 docs/designs/mgmt-1/DESIGN.md §11.1 (ABORT).
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void APrepareFailure_AbortsTheRound_AndNeverCommits()
    {
        var (master, bus) = BootstrapWithNodes(NodeA, NodeB);
        using var _ = master;

        var requestId = Guid.NewGuid();
        PublishLoadZone(bus, requestId, "zone-alpha");
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var prepares = ReadOps(bus, FdpNodeOpType.PrepareZone);
        Assert.Equal(2, prepares.Count);

        AckAll(bus, prepares.Take(1));
        AckAll(bus, prepares.Skip(1), OrchestrationStatusCode.Failure);
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        Assert.Empty(ReadOps(bus, FdpNodeOpType.CommitZone));

        var aborts = ReadOps(bus, FdpNodeOpType.AbortTransaction);
        Assert.Equal(2, aborts.Count);
        Assert.All(aborts, a => Assert.Equal(
            prepares[0].TransactionId,
            Assert.IsType<AbortTransactionPayload>(a.DomainPayload).TargetTransactionId));

        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(),
            e => e.RequestId == requestId && e.StatusCode.IsError());
    }

    // ── Degenerate inputs ─────────────────────────────────────────────────────

    /// <summary>An empty roster has nothing to ask, so the postcondition already holds.</summary>
    [Fact(Timeout = 5_000)]
    public void EmptyRoster_ReportsSuccessWithoutFanningOut()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus);

        var requestId = Guid.NewGuid();
        PublishLoadZone(bus, requestId, "zone-alpha");
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        Assert.Empty(ReadOps(bus, FdpNodeOpType.PrepareZone));
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(),
            e => e.RequestId == requestId && !e.StatusCode.IsError());
    }

    /// <summary>
    /// ⚠ An intent with no zone id is REJECTED, loudly. ⛔ It must not fan out a round for "" — the
    /// silent-no-op shape this programme keeps finding (`R-133`).
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void MissingZoneId_IsRejected_AndFansOutNothing()
    {
        var (master, bus) = BootstrapWithNodes(NodeA);
        using var _ = master;

        var requestId = Guid.NewGuid();
        PublishLoadZone(bus, requestId, zoneId: null);
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        Assert.Empty(ReadOps(bus, FdpNodeOpType.PrepareZone));
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(),
            e => e.RequestId == requestId && e.StatusCode.IsError());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // E4 — the TERRAIN-ASSET BUILD round, and the per-node outcome
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <c>E4</c> — the build is its OWN op pair, not a zone round with different arguments.
    /// 📄 §2.1b: zone preparation and the asset build "are different in nature and stay separate ops".
    /// ⭐ Both reuse the same continuation and abort arm, so there is one 2PC mechanism with two
    /// vocabularies — this rail pins that the vocabularies do not leak into each other.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void E4_BuildTerrainAsset_RunsItsOwnPrepareThenCommitPair()
    {
        var (master, bus) = BootstrapWithNodes(NodeA, NodeB);
        using var _ = master;

        var requestId = Guid.NewGuid();
        bus.PublishManaged(new BuildTerrainAssetIntent { RequestId = requestId, Kinds = null });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var prepares = ReadOps(bus, FdpNodeOpType.PrepareTerrainAsset);
        Assert.Equal(2, prepares.Count);
        Assert.Single(prepares.Select(p => p.TransactionId).Distinct());

        // ⛔ Not the zone pair — the two rounds must not be confusable on the wire.
        Assert.Empty(ReadOps(bus, FdpNodeOpType.PrepareZone));
        Assert.Empty(ReadOps(bus, FdpNodeOpType.CommitTerrainAsset));

        AckAll(bus, prepares);
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var commits = ReadOps(bus, FdpNodeOpType.CommitTerrainAsset);
        Assert.Equal(2, commits.Count);
        // ⭐ ONE transaction id across both phases, so a node's commit can find what its prepare staged.
        Assert.Equal(prepares[0].TransactionId, commits[0].TransactionId);

        AckAll(bus, commits);
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(),
            e => e.RequestId == requestId && !e.StatusCode.IsError());
    }

    /// <summary>
    /// ⚠ No kinds means ALL kinds, and the payload carries that as null rather than an empty array —
    /// an op that asked for nothing would never be published, so "absence = nothing" would silently
    /// turn every unparameterised build into a no-op.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void E4_ABuildWithNoKinds_CarriesNullRatherThanAnEmptyList()
    {
        var (master, bus) = BootstrapWithNodes(NodeA);
        using var _ = master;

        bus.PublishManaged(new BuildTerrainAssetIntent { RequestId = Guid.NewGuid(), Kinds = null });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var prepare = ReadOps(bus, FdpNodeOpType.PrepareTerrainAsset).Single();
        Assert.Null(Assert.IsType<TerrainAssetOpPayload>(prepare.DomainPayload).Kinds);
    }

    /// <summary>⭐ A failed prepare aborts the build round too — the same arm the zone round uses.</summary>
    [Fact(Timeout = 5_000)]
    public void E4_AFailedBuildPrepare_AbortsAndNeverCommits()
    {
        var (master, bus) = BootstrapWithNodes(NodeA, NodeB);
        using var _ = master;

        var requestId = Guid.NewGuid();
        bus.PublishManaged(new BuildTerrainAssetIntent { RequestId = requestId, Kinds = null });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var prepares = ReadOps(bus, FdpNodeOpType.PrepareTerrainAsset);
        AckAll(bus, prepares.Take(1));
        AckAll(bus, prepares.Skip(1), OrchestrationStatusCode.Failure);
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        Assert.Empty(ReadOps(bus, FdpNodeOpType.CommitTerrainAsset));
        Assert.Equal(2, ReadOps(bus, FdpNodeOpType.AbortTransaction).Count);
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(),
            e => e.RequestId == requestId && e.StatusCode.IsError());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>§8.3 N5 / §9.2 U3 — NEVER ONE GLOBAL OK.</b> The per-node outcome must survive the round,
    /// because the master aggregates it away: an operator who sees a single green tick cannot tell WHICH
    /// node is now missing terrain, and that node will render and path incorrectly while the cluster
    /// reports healthy.
    /// </summary>
    [Fact]
    public void E4_ThePerNodeOutcome_NamesTheNodeThatFailed()
    {
        var tracker = new Hrot.Orchestrator.Panels.TerrainBuildOutcomeTracker();
        tracker.Watch(Guid.NewGuid());

        tracker.Record(NodeA, FdpNodeOpType.CommitTerrainAsset, OrchestrationStatusCode.Success);
        tracker.Record(NodeB, FdpNodeOpType.PrepareTerrainAsset, OrchestrationStatusCode.Failure);

        Assert.True(tracker.AnyFailed);
        var outcomes = tracker.Outcomes;
        Assert.Equal(2, outcomes.Count);
        Assert.Equal(NodeA, outcomes[0].NodeId);
        Assert.False(outcomes[0].Failed);
        Assert.Equal(NodeB, outcomes[1].NodeId);
        Assert.True(outcomes[1].Failed);   // ⭐ WHICH node, not just "something failed"
    }

    /// <summary>
    /// ⛔ It records only the terrain ops. The panel shares a bus with every other round, and folding an
    /// unrelated op's ACK in would misreport which node failed at what.
    /// </summary>
    [Fact]
    public void E4_TheOutcomeTracker_IgnoresUnrelatedOps()
    {
        var tracker = new Hrot.Orchestrator.Panels.TerrainBuildOutcomeTracker();
        tracker.Watch(Guid.NewGuid());

        tracker.Record(NodeA, FdpNodeOpType.TakeSnapshot,  OrchestrationStatusCode.Failure);
        tracker.Record(NodeA, FdpNodeOpType.SerializeLocal, OrchestrationStatusCode.Failure);

        Assert.Empty(tracker.Outcomes);
        Assert.False(tracker.AnyFailed);
    }

    /// <summary>⚠ A new build clears the previous round — two rounds' answers mixed is worse than none.</summary>
    [Fact]
    public void E4_StartingANewBuild_ClearsThePreviousOutcomes()
    {
        var tracker = new Hrot.Orchestrator.Panels.TerrainBuildOutcomeTracker();
        tracker.Watch(Guid.NewGuid());
        tracker.Record(NodeA, FdpNodeOpType.CommitTerrainAsset, OrchestrationStatusCode.Failure);
        Assert.True(tracker.AnyFailed);

        tracker.Watch(Guid.NewGuid());
        Assert.Empty(tracker.Outcomes);
        Assert.False(tracker.AnyFailed);
    }

    // ── The injected-request path (§9.6 — the editor) ─────────────────────────

    /// <summary>
    /// ⭐⭐ §9.6: the zone load is always cluster-wide, and on the EDITOR — a single-node cluster — it
    /// arrives as an injected <see cref="ClusterOpRequest"/>, not through the DDS translator.
    /// ⛔ Before C4 this request fell through <c>ProcessSingleClusterOpRequest</c>'s switch and did
    /// nothing at all, on the host most likely to issue it.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void AnInjectedLoadZoneRequest_StartsTheSameRound()
    {
        var (master, bus) = BootstrapWithNodes(NodeA);
        using var _ = master;

        var requestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.LoadZone,
            PayloadJson   = System.Text.Json.JsonSerializer.Serialize(new { ZoneId = "zone-alpha" }),
        });

        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var prepares = ReadOps(bus, FdpNodeOpType.PrepareZone);
        Assert.Single(prepares);
        Assert.Equal("zone-alpha", Assert.IsType<ZoneOpPayload>(prepares[0].DomainPayload).ZoneId);
    }
}
