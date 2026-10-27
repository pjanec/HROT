using System;
using System.Linq;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for the CGF1-S0502 fan-out loop in <see cref="ClusterMaster"/>:
/// <list type="bullet">
///   <item>A <see cref="ClusterOpType.TransitionState"/> request fans out
///     <see cref="NodeOpType.PrepareLive"/> and <see cref="NodeOpType.CommitState"/>
///     commands to all active nodes.</item>
///   <item>When no active nodes are registered the fan-out guard skips without error.</item>
///   <item><see cref="DistributedTransaction.SourceDsmState"/> is captured from the
///     state <em>before</em> the optimistic advance.</item>
///   <item><see cref="DistributedTransaction.PayloadJson"/> is copied from the
///     originating <see cref="ClusterOpRequest"/>.</item>
/// </list>
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterFanOutTests
{
    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Registers a node via the bus heartbeat and ticks ClusterMaster so that the
    /// node is present in <see cref="ClusterMaster.NodeRoster"/>.
    /// </summary>
    private static void RegisterNode(
        FdpEventBus bus, ClusterMaster exercise,
        int nodeId = 1, string subsystem = "SimHost")
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            SubsystemName = subsystem,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();
    }

    // ── CGF1-S0502: PrepareLive fan-out ───────────────────────────────────────

    /// <summary>
    /// After a Standby→LoadingLive transition request is processed against a cluster
    /// with one active node, a <see cref="NodeOpType.PrepareLive"/> NodeOpCommand
    /// must be published on the DDS bus.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_Standby_To_LoadingLive_FansOutPrepareLive()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, exercise);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(
            intents.Any(i => i.Operation == FdpNodeOpType.PrepareLive),
            "ClusterMaster must fan out a PrepareLive NodeOpCommand for Standby→LoadingLive.");
    }

    // ── CGF1-S0502: CommitState fan-out ───────────────────────────────────────

    /// <summary>
    /// After <see cref="NodeOpType.PrepareLive"/>, ClusterMaster must also fan out a
    /// <see cref="NodeOpType.CommitState"/> command whose <c>PayloadJson</c> is the
    /// integer representation of <see cref="ClusterState.LoadingLive"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_Standby_To_LoadingLive_FansOutCommitState()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, exercise);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(
            intents.Any(i => i.Operation == FdpNodeOpType.CommitState),
            "ClusterMaster must fan out a CommitState command with target-state payload after PrepareXxx.");
    }

    // ── CGF1-S0502: no-node guard ─────────────────────────────────────────────

    /// <summary>
    /// When no nodes are registered, the fan-out guard skips the <see cref="NodeOpCommand"/>
    /// writes without throwing.  The transaction is still recorded in
    /// <see cref="ClusterMaster.TransactionHistory"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void NoActiveNodes_FanOutSkipped_NoException()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        // No heartbeat written → roster is empty → fan-out guard must skip without error.
        var ex = Record.Exception(() =>
        {
            exercise.HandleClusterOpRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.TransitionState,
                PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
            });
            exercise.Tick();   // drain injected request queue
        });

        Assert.Null(ex);
        Assert.True(exercise.TransactionHistory.Count > 0,
            "TransactionHistory must contain an entry even when no NodeOpCommands were sent.");
    }

    // ── CGF1-S0501: SourceDsmState capture ───────────────────────────────────

    /// <summary>
    /// <see cref="DistributedTransaction.SourceDsmState"/> must record the state
    /// that was active <em>before</em> the optimistic advance, not the target state.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SourceDsmState_CapturedBeforeOptimisticAdvance()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        exercise.Tick();   // drain injected request queue

        var tx = exercise.TransactionHistory[exercise.TransactionHistory.Count - 1];
        Assert.Equal(ClusterState.Idle, tx.SourceDsmState);
    }

    // ── CMC-S010: DistributedTransaction state capture ─────────────────────────

    /// <summary>
    /// After CMC-S010, <see cref="DistributedTransaction.PayloadJson"/> is always empty
    /// (JSON parsing moved to <see cref="ClusterOpRequestAdapter"/>).
    /// The transaction still records <see cref="DistributedTransaction.TargetDsmState"/>
    /// and <see cref="DistributedTransaction.SourceDsmState"/> for the 2PC history table.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PayloadJson_PopulatedFromClusterOpRequest()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{ClusterState.LoadingLive}\"}}",
        });
        exercise.Tick();   // drain injected request queue

        var tx = exercise.TransactionHistory[exercise.TransactionHistory.Count - 1];
        // CMC-S010: raw JSON is no longer stored in the transaction; PayloadJson is empty.
        Assert.Equal(string.Empty,         tx.PayloadJson);
        // TargetDsmState and SourceDsmState are still captured.
        Assert.Equal(ClusterState.LoadingLive, tx.TargetDsmState);
        Assert.Equal(ClusterState.Idle,         tx.SourceDsmState);
    }

    // ── P3 Debt: standalone ReplaySeek fan-out ────────────────────────────────

    /// <summary>
    /// A standalone <see cref="ClusterOpType.ReplaySeek"/> request fans out
    /// <see cref="NodeOpType.NodeReplaySeek"/> to all active nodes.
    /// Requires the cluster to be in <see cref="ClusterState.OperatingReplay"/> first.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeekStep_FansOutNodeReplaySeek()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, exercise);

        // Transition to OperatingReplay (optimistic: sets _currentDsmState without waiting for ACKs).
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{ClusterState.OperatingReplay}\"}}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // Drain any PrepareReplay / CommitState messages from the transition.
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        MasterShows.State(exercise, Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay);   // ⭐ CE-3329 F8 — the request above names no
                                                                                             //   exercise, so it never lands; a seek needs a recording

        // Now send standalone ReplaySeek.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":1000}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(
            intents.Any(i => i.Operation == FdpNodeOpType.NodeReplaySeek),
            "ClusterMaster must fan out a NodeReplaySeek command for a standalone ReplaySeek request.");
    }
    // ── ⭐ AQ107 C (R-4094) — the master publishes the origin of the NED wire's local frame, from the loaded terrain ──

    /// <summary>A NAS holding one scenario that names <paramref name="terrainName"/> and, when <paramref name="terrainJson"/> is given, that terrain.</summary>
    private static string MakeNasWithTerrain(string scenarioId, string terrainName, string? terrainJson)
    {
        var nas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        var scenarioDir = System.IO.Path.Combine(nas, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
        System.IO.Directory.CreateDirectory(scenarioDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(scenarioDir, "Hrot.SimHost.json"),
            "{\"header\":{\"terrainName\":\"" + terrainName + "\"},\"entities\":[]}");
        if (terrainJson != null)
        {
            var terrainDir = System.IO.Path.Combine(nas, Fdp.Toolkit.Terrain.TerrainCatalog.SharedDirectoryName, terrainName);
            System.IO.Directory.CreateDirectory(terrainDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(terrainDir, Fdp.Toolkit.Terrain.TerrainCatalog.DefinitionFileName), terrainJson);
        }
        return nas;
    }

    private static string TerrainWithOrigin(double lat, double lon, double alt)
        => "{\"schemaVersion\":2,\"origin\":{\"lat\":" + lat.ToString(System.Globalization.CultureInfo.InvariantCulture)
         + ",\"lon\":" + lon.ToString(System.Globalization.CultureInfo.InvariantCulture)
         + ",\"alt\":" + alt.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";

    /// <summary>Requests <paramref name="target"/> (for a load: naming <paramref name="scenarioId"/>), answers every prepare of the round with
    /// success, and returns the <see cref="GeoReferencePublishedEvent"/>s the round's completion published.</summary>
    private static System.Collections.Generic.List<GeoReferencePublishedEvent> CompleteTransition(
        FdpEventBus bus, ClusterMaster master, Fdp.Toolkit.Orchestration.ClusterState target, string? scenarioId)
    {
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = target,
            ScenarioId    = scenarioId,
            ExerciseId    = Guid.NewGuid(),
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        UnparkIfNamed(bus, master, scenarioId);
        foreach (var p in bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState).ToList())
            bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId = p.TransactionId, Operation = p.Operation, NodeId = 1,
                StatusCode = OrchestrationStatusCode.Success, IsParticipating = true,
            });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        return bus.ReadManaged<GeoReferencePublishedEvent>().ToList();
    }

    /// <summary>A load that names a scenario PARKS until its files are staged (L8); the staging saga's completion is stated directly.</summary>
    private static void UnparkIfNamed(FdpEventBus bus, ClusterMaster master, string? scenarioId)
    {
        if (master.ParkedRequestId is not { } parked) return;
        bus.PublishManaged(new PrefetchDistributionCompletedEvent { RequestId = parked, ScenarioId = scenarioId ?? string.Empty, IsSuccess = true });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
    }

    private static ClusterConfiguration ConfigOn(string nas) => new()
    {
        Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10, NasBasePath = nas,
    };

    /// <summary>⭐ AQ107 C (a) — a load whose terrain declares an origin publishes EXACTLY that origin: the terrain name, frame 0 (ENU), generation 1.</summary>
    [Fact(Timeout = 10_000)]
    public void AQ107C_ALoad_PublishesTheTerrainsDeclaredOrigin()
    {
        var nas = MakeNasWithTerrain("s1", "alps", TerrainWithOrigin(47.25, 11.4, 612.5));
        try
        {
            var bus = new FdpEventBus();
            using var master = new ClusterMaster(bus, ConfigOn(nas));
            RegisterNode(bus, master);

            var published = CompleteTransition(bus, master, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, "s1");

            var geo = Assert.Single(published);
            Assert.Equal(47.25, geo.OriginLatitudeDeg);
            Assert.Equal(11.4, geo.OriginLongitudeDeg);
            Assert.Equal(612.5, geo.OriginAltitudeM);
            Assert.Equal("alps", geo.TerrainName);
            Assert.Equal(1, geo.Generation);
            Assert.Equal(1, master.GeoReferenceGeneration);
        }
        finally { System.IO.Directory.Delete(nas, recursive: true); }
    }

    /// <summary>⭐ AQ107 C (b) — a second load with a DIFFERENT origin is generation 2; the same origin loaded again writes nothing and keeps the generation.</summary>
    [Fact(Timeout = 10_000)]
    public void AQ107C_ADifferentOrigin_BumpsTheGeneration_TheSameOriginDoesNot()
    {
        var nas = MakeNasWithTerrain("s1", "alps", TerrainWithOrigin(47.25, 11.4, 612.5));
        MakeNasWithTerrainInto(nas, "s2", "coast", TerrainWithOrigin(54.3, 10.1, 3.0));
        try
        {
            var bus = new FdpEventBus();
            using var master = new ClusterMaster(bus, ConfigOn(nas));
            RegisterNode(bus, master);

            Assert.Equal(1, Assert.Single(CompleteTransition(bus, master, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, "s1")).Generation);
            CompleteTransition(bus, master, Fdp.Toolkit.Orchestration.ClusterState.Idle, null);   // no terrain named by an unload: nothing published
            Assert.Equal(1, master.GeoReferenceGeneration);

            var second = Assert.Single(CompleteTransition(bus, master, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, "s2"));
            Assert.Equal(2, second.Generation);
            Assert.Equal(54.3, second.OriginLatitudeDeg);
            Assert.Equal("coast", second.TerrainName);

            CompleteTransition(bus, master, Fdp.Toolkit.Orchestration.ClusterState.Idle, null);
            Assert.Empty(CompleteTransition(bus, master, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, "s2"));   // the same origin again
            Assert.Equal(2, master.GeoReferenceGeneration);
        }
        finally { System.IO.Directory.Delete(nas, recursive: true); }
    }

    private static void MakeNasWithTerrainInto(string nas, string scenarioId, string terrainName, string terrainJson)
    {
        var scenarioDir = System.IO.Path.Combine(nas, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
        System.IO.Directory.CreateDirectory(scenarioDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(scenarioDir, "Hrot.SimHost.json"),
            "{\"header\":{\"terrainName\":\"" + terrainName + "\"},\"entities\":[]}");
        var terrainDir = System.IO.Path.Combine(nas, Fdp.Toolkit.Terrain.TerrainCatalog.SharedDirectoryName, terrainName);
        System.IO.Directory.CreateDirectory(terrainDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(terrainDir, Fdp.Toolkit.Terrain.TerrainCatalog.DefinitionFileName), terrainJson);
    }

    /// <summary>⭐ AQ107 C (c) — a terrain that declares no origin publishes 0,0,0 (R-229: "Missing geo = zeros"), still under its name.</summary>
    [Fact(Timeout = 10_000)]
    public void AQ107C_ATerrainWithoutAnOrigin_PublishesZeros()
    {
        var nas = MakeNasWithTerrain("s1", "bare", "{\"schemaVersion\":2}");
        try
        {
            var bus = new FdpEventBus();
            using var master = new ClusterMaster(bus, ConfigOn(nas));
            RegisterNode(bus, master);

            var geo = Assert.Single(CompleteTransition(bus, master, Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, "s1"));

            Assert.Equal((0.0, 0.0, 0.0), (geo.OriginLatitudeDeg, geo.OriginLongitudeDeg, geo.OriginAltitudeM));
            Assert.Equal("bare", geo.TerrainName);
            Assert.Equal(1, geo.Generation);
        }
        finally { System.IO.Directory.Delete(nas, recursive: true); }
    }

    /// <summary>⭐ AQ107 C — nothing is published for a load whose round FAILS: the nodes never adopted that terrain.</summary>
    [Fact(Timeout = 10_000)]
    public void AQ107C_AFailedLoad_PublishesNothing()
    {
        var nas = MakeNasWithTerrain("s1", "alps", TerrainWithOrigin(47.25, 11.4, 612.5));
        try
        {
            var bus = new FdpEventBus();
            using var master = new ClusterMaster(bus, ConfigOn(nas));
            RegisterNode(bus, master);

            bus.PublishManaged(new TransitionStateIntent
            {
                TransactionId = Guid.NewGuid(), TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive,
                ScenarioId = "s1", ExerciseId = Guid.NewGuid(),
            });
            bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
            UnparkIfNamed(bus, master, "s1");
            foreach (var p in bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState).ToList())
                bus.PublishManaged(new NodeOpCompletedEvent
                {
                    TransactionId = p.TransactionId, Operation = p.Operation, NodeId = 1,
                    StatusCode = OrchestrationStatusCode.Rejected, IsParticipating = true,
                });
            bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();

            Assert.Empty(bus.ReadManaged<GeoReferencePublishedEvent>());
            Assert.Equal(0, master.GeoReferenceGeneration);
        }
        finally { System.IO.Directory.Delete(nas, recursive: true); }
    }
}
