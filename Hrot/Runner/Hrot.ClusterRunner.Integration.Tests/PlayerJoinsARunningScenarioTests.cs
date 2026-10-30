using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.UI.Common.RunMode;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <c>CE-3344/3345/3346</c> (AQ110 §3 — the readiness protocol, <c>R-3070</c>/<c>R-3072</c>) — <b>a Player's IG joins a scenario that is ALREADY RUNNING</b>, end to end:
/// the master's catch-up (<c>ClusterMaster.InSync.cs</c> C7), the slave's <c>CatchingUp</c> phase, the node's own <c>ClusterView</c> and the host gate, and the replicated
/// world the joiner ends up holding. 📐 Until now the master side was pinned only in isolation (<c>ClusterMasterCatchUpTests</c>, hand-answered ops) and the harness started
/// every host TOGETHER — nothing proved a real IG, on real DDS, comes up in a live exercise.
/// <para>Home: a class beside <c>DistributedScenarioLoadTests</c> (the harness + staged-scenario precedent) — <c>AllSubsystemsClusterTransitionTests</c> is script-driven and has no scenario or late start.
/// The harness gained <see cref="HrotRunnerHarness.StartLate"/> (the IG goes through the one <c>SubsystemOrchestrator.Initialize</c>, no second bootstrap).</para>
/// 🔴 Red-proof: <c>SendCatchUpOps</c> not sending the operating-state step (the last PrepareState/CommitState pair) ⇒ the IG never reaches PlayReady.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class PlayerJoinsARunningScenarioTests : IDisposable
{
    // Domain ids 96–99 (CycloneDDS accepts 0–232; 92–95 EqsRoundTrip, 100+ the all-in-one harness; grep of domain ids, 2026-10-29).
    private static int _domainSeq = 95;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private const int PlayerNodeId = 301;   // ⛔ not the IG default (300) — that is the Orchestrator's own node id in this one-process harness
    private const long UnitAId = 5000L;
    private const long UnitBId = 5001L;

    private readonly string _scenarioId = "test_player_join_" + Guid.NewGuid().ToString("N");

    public void Dispose() => NasScenarioStaging.Remove(_scenarioId);

    [Fact(Timeout = 120_000)]
    public async Task A_Player_IG_joining_a_running_scenario_is_caught_up_gets_its_gate_open_and_holds_the_entities()
    {
        AuthorScenario();

        // The cluster WITHOUT an IG — the IG object is built by the harness but not initialised until StartLate.
        using var harness = new HrotRunnerHarness("simhost,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var cgfView = harness.Cgf!.NodeForTest!.ClusterSlave.View;   // another node's view of the cluster level (the orchestrator's own is the UI cache) — CGF's, R-3067: ExCon is retired

        Assert.True(harness.PumpUntil(() => master.NodeRoster.ActiveNodes.Count > 0, 1000), "a node must reach the roster before the transition");

        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new
            {
                TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive),
                ScenarioId  = _scenarioId,
            }),
        }).ConfigureAwait(false);

        var cgfWorld = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, 4000),
            $"the cluster must reach OperatingLive (31). Current: {(int)master.CurrentClusterState}.");
        Assert.True(harness.PumpUntil(() => CountUnits(cgfWorld) == 2 && cgfView.ClusterPlayReady, 2000),
            $"the running cluster must hold the 2 scenario units and be PlayReady before the IG joins (units {CountUnits(cgfWorld)}, ClusterPlayReady {cgfView.ClusterPlayReady}).");

        var brainIds = UnitNetworkIds(cgfWorld);
        Assert.Equal(2, brainIds.Count);
        var inSyncBefore = new HashSet<int>(master.InSyncNodeIds);

        // ── the IG starts NOW, into the running scenario ─────────────────────────────────────
        harness.StartLate(harness.Ig, PlayerNodeId);
        var igSlave = harness.Ig.App.ClusterSlave!;
        var igView  = igSlave.View;
        int igNodeId = igView.NodeId ?? throw new InvalidOperationException("the IG's ClusterView carries no node id");
        Assert.DoesNotContain(igNodeId, inSyncBefore);   // the joiner is a NEW node — otherwise nothing below proves a catch-up

        bool sawCatchingUp = false, clusterPlayReadyDropped = false, gateOpened = false;
        bool joined = harness.PumpUntil(() =>
        {
            if (igView.LocalPhase == NodePhase.CatchingUp) sawCatchingUp = true;
            if (!cgfView.ClusterPlayReady) clusterPlayReadyDropped = true;   // ⭐ (d) sampled EVERY frame, not only at the end
            gateOpened = igView.GateOpen(HostGatePolicies.For(RunHostKind.Ig));
            return gateOpened && igView.IsLocalPlayReady && master.InSyncNodeIds.Contains(igNodeId);
        }, 4000);

        string why = $"IG phase {igView.LocalPhase} (step '{igView.LocalStep}'), CanOperate {igView.CanOperate} ({igView.NotOperableReason}), " +
                     $"gate {gateOpened}, in sync {master.InSyncNodeIds.Contains(igNodeId)}, catching up {string.Join(",", master.CatchingUpNodeIds)}";
        Assert.True(joined, "the IG must reach PlayReady with its Player gate open and be in the master's in-sync set — " + why);

        // (a) it went through CatchingUp on its way
        Assert.True(sawCatchingUp, "the IG's phase must pass through CatchingUp (a late joiner is caught up, not loaded) — " + why);
        Assert.Equal(NodePhase.PlayReady, igView.LocalPhase);

        // (b) the Player gate is open (asserted in the wait too; stated alone so a regression names it)
        Assert.True(igView.GateOpen(HostGatePolicies.For(RunHostKind.Ig)), why);

        // (d) the cluster's play-ready never went false while the IG joined — a joiner is not a participant until in sync (R-4014 K6)
        Assert.False(clusterPlayReadyDropped, "ClusterPlayReady (seen by CGF) must never drop while a Player joins");

        // (e) the master counts it in sync
        Assert.Contains(igNodeId, master.InSyncNodeIds);

        // (c) the IG's world holds the scenario's replicated entities — the same ones the brain holds
        var igMap = harness.Ig.App.TestHook_EntityMap;
        var igWorld = harness.Ig.App.World;
        Assert.True(harness.PumpUntil(() => CountKnownOnIg(igMap, igWorld, brainIds) == brainIds.Count, 1000),
            $"the IG must hold all {brainIds.Count} units the brain holds. It holds {CountKnownOnIg(igMap, igWorld, brainIds)}.");
        Assert.True(brainIds.Count > 0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Two tanks, authored offline and staged where the cluster loads scenarios from (the <c>DistributedScenarioLoadTests</c> recipe, without the mission plan).</summary>
    private void AuthorScenario()
    {
        using var editor = new EditorHarness();
        foreach (var id in new[] { UnitAId, UnitBId })
            editor.Bus.PublishManaged(new SpawnEntityCommand
            {
                TkbType = TkbEntityTypes.Tank_M1Abrams, NetworkId = id, OwnerNodeId = 0, InitType = ReliableInitType.None,
            });
        Assert.True(editor.PumpUntil(() => editor.Repo.EntityCount == 2, timeoutMs: 5_000), "EditorHarness must spawn 2 entities within 5 s.");
        editor.PumpFrames(2);
        NasScenarioStaging.Write(editor.FileService, editor.Repo, _scenarioId);
    }

    private static bool IsPart(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<PartMetadata>() && w.HasComponent<PartMetadata>(e);

    private static bool IsWorldEnvironment(EntityRepository w, Entity e)
        => w.HasComponent<TkbIdentity>(e) && w.GetComponentRO<TkbIdentity>(e).TkbType == TkbEntityTypes.WorldEnvironment;

    private static int CountUnits(EntityRepository w) => UnitEntities(w).Count;

    private static List<Entity> UnitEntities(EntityRepository w)
    {
        var units = new List<Entity>();
        for (int i = 0; i <= w.MaxEntityIndex; i++)
        {
            var e = w.GetEntityByIndex(i);
            if (e != Entity.Null && w.IsAlive(e) && !IsPart(w, e) && !IsWorldEnvironment(w, e)) units.Add(e);
        }
        return units;
    }

    /// <summary>The network ids of the brain's units (ghost map of the CGF node).</summary>
    private List<long> UnitNetworkIds(EntityRepository cgfWorld)
    {
        var ids = new List<long>();
        foreach (var e in UnitEntities(cgfWorld))
            if (cgfWorld.HasComponent<NetworkIdentity>(e)) ids.Add(cgfWorld.GetComponentRO<NetworkIdentity>(e).Value);
        return ids;
    }

    private static int CountKnownOnIg(Fdp.Toolkit.Replication.Services.NetworkEntityMap map, EntityRepository world, List<long> ids)
    {
        int n = 0;
        foreach (var id in ids)
            if (map.TryGetEntity(id, out var e) && world.IsAlive(e)) n++;
        return n;
    }
}
