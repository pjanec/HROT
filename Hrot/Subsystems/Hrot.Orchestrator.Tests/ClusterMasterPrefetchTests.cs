using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
/// Tests that verify the prefetch barrier ordering — specifically that
/// <see cref="NodeOpType.PrefetchFiles"/> is only fan-out to nodes <em>after</em>
/// <see cref="StorageGatewayModule.PrefetchScenarioAsync"/> completes (CGF1-S0302 / A.1).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterPrefetchTests : IDisposable
{
    private readonly string _nasDir;
    private readonly string _scenarioId = "prefetch_test_scenario";

    public ClusterMasterPrefetchTests()
    {
        _nasDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_nasDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_nasDir))
            Directory.Delete(_nasDir, recursive: true);
    }

    // ── A.1: PrefetchFiles only fan-out after gateway copy success ────────

    /// <summary>
    /// When the NAS source directory exists and files are present,
    /// <see cref="NodeOpType.PrefetchFiles"/> must only arrive at nodes
    /// <em>after</em> the gateway copy completes (i.e. not in the same tick
    /// as the ClusterOpRequest, but only once the task has resolved).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion()
    {
        // Arrange: create a scenario directory on the NAS with a test file.
        // 🔴 FIXED 2026-09-18 (T-1, artifact-staging batch): same defect as
        //    StorageGatewayTests.PrefetchScenarioAsync_EmptyDirectory_ThrowsInvalidOperation — the NAS
        //    layout was built WITHOUT the `scenarios/` segment production resolves
        //    (StorageGatewayModule.cs:238). ⛔ The gateway threw DirectoryNotFound, no copy ever ran, and
        //    so PrefetchFiles was never fanned out — which is exactly what this test then reported.
        var scenarioDir = Path.Combine(
            _nasDir, Fdp.Toolkit.Orchestration.OrchestrationConstants.ScenariosDirectoryName, _scenarioId);
        Directory.CreateDirectory(scenarioDir);
        File.WriteAllText(Path.Combine(scenarioDir, "Hrot.SimHost.json"), "{}");

        // Arrange: staging dir for AssetPrefetchProcessManager.
        var stagingDir = Path.Combine(Path.GetTempPath(), "fdp_pf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        var bus = new FdpEventBus();
        var config = new ClusterConfiguration
        {
            Mandatory = System.Array.Empty<string>(),
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };
        using var exercise = new ClusterMaster(bus, config);

        var gateway = new StorageGatewayModule();
        var assetPrefetchPM = new AssetPrefetchProcessManager(bus, gateway, _nasDir, stagingDir);

        // Register a fake node so FanOutNodeOp has a target to write PrefetchFiles to.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = 1, SubsystemName = "TestNode",
            LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick();     // ingest heartbeat into roster
        bus.SwapBuffers();

        // Issue a TransitionState(LoadingEdit) with a ScenarioId to trigger prefetch.
        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{ClusterState.LoadingEdit}\",\"ScenarioId\":\"{_scenarioId}\"}}",
        });

        // First tick: exercise publishes ExecutePrefetchIntent to WRITE buffer.
        assetPrefetchPM.Tick();
        exercise.Tick();
        bus.SwapBuffers();

        // Second tick: assetPrefetchPM reads ExecutePrefetchIntent, starts gateway task.
        assetPrefetchPM.Tick();
        exercise.Tick();
        bus.SwapBuffers();

        // Spin until PrefetchFiles fan-out arrives (gateway completes async).
        bool prefetchFilesReceived = false;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!prefetchFilesReceived && DateTime.UtcNow < deadline)
        {
            assetPrefetchPM.Tick();
            exercise.Tick();
            bus.SwapBuffers();
            prefetchFilesReceived = bus.ReadManaged<ExecuteNodeOpIntent>()
                .Any(i => i.Operation == FdpNodeOpType.PrefetchFiles);
            Thread.Sleep(10);
        }

        try { Directory.Delete(stagingDir, recursive: true); } catch { }

        Assert.True(prefetchFilesReceived,
            "PrefetchFiles command was never received — expected it after gateway copy completed.");
    }

    /// <summary>
    /// When the NAS source directory is missing, the gateway throws
    /// <see cref="DirectoryNotFoundException"/> and the ClusterMaster must publish
    /// <see cref="ClusterOpStatus.Failure"/> rather than silently proceeding.
    /// No <see cref="NodeOpType.PrefetchFiles"/> command must be sent.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PrefetchScenario_WhenNasSourceDirMissing_PublishesFailure_AndNoPrefetchFiles()
    {
        // Arrange: NAS dir exists but the scenarioId sub-dir does NOT.
        const string missingScenarioId = "nonexistent_scenario_xyz";

        // Arrange: staging dir for AssetPrefetchProcessManager.
        var stagingDir = Path.Combine(Path.GetTempPath(), "fdp_pf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        var bus = new FdpEventBus();
        var config = new ClusterConfiguration
        {
            Mandatory = System.Array.Empty<string>(),
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };
        using var exercise = new ClusterMaster(bus, config);

        var gateway = new StorageGatewayModule();
        var assetPrefetchPM = new AssetPrefetchProcessManager(bus, gateway, _nasDir, stagingDir);

        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{ClusterState.LoadingEdit}\",\"ScenarioId\":\"{missingScenarioId}\"}}",
        });

        // Spin until the failure status arrives.
        bool observedFailure = false;
        bool prefetchFilesReceived = false;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !observedFailure)
        {
            assetPrefetchPM.Tick();
            exercise.Tick();
            bus.SwapBuffers();
            Thread.Sleep(15);

            foreach (var ev in bus.ReadManaged<ClusterOpCompletedEvent>())
            {
                if (ev.RequestId == reqId && ev.StatusCode.IsError())
                    observedFailure = true;
            }

            foreach (var intent in bus.ReadManaged<ExecuteNodeOpIntent>())
            {
                if (intent.Operation == FdpNodeOpType.PrefetchFiles)
                    prefetchFilesReceived = true;
            }
        }

        try { Directory.Delete(stagingDir, recursive: true); } catch { }

        Assert.True(observedFailure,
            $"Expected a failure ClusterOpCompletedEvent but none arrived.");
        Assert.False(prefetchFilesReceived,
            "PrefetchFiles command must NOT be sent when the NAS source directory is missing.");
    }

    // ══ L8 — THE DETERMINISTIC STAGING WAIT ═══════════════════════════════════════════════════════
    //
    // 🔒 User, `2026-09-18`: "it cannot depend on timeouts where can easily wait deterministically."
    // 📄 docs/DESIGN_Cluster_Load_Phase.md §7 — these are §7.5's "what must be PROVEN, not assumed".
    //
    // ⭐⭐ Why they live HERE and not in a new class (T-1 ④): this IS the prefetch-barrier suite. The
    //    property under test is the same barrier the two cases above assert, moved one step later —
    //    PrefetchFiles used to be ordered after the copy, and now the whole TRANSITION is ordered after
    //    the nodes have acknowledged it.

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>Registers node 1 in the roster so fan-outs have a target.</summary>
    private static void RegisterNode(FdpEventBus bus, ClusterMaster master)
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = 1, SubsystemName = "TestNode",
            LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
    }

    private static void RequestTransition(
        ClusterMaster master, Guid requestId, ClusterState target, string? scenarioId)
    {
        string payload = scenarioId == null
            ? $"{{\"TargetState\":\"{target}\"}}"
            : $"{{\"TargetState\":\"{target}\",\"ScenarioId\":\"{scenarioId}\"}}";

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = payload,
        });
    }

    private static bool SawPrepare(FdpEventBus bus) =>
        bus.ReadManaged<ExecuteNodeOpIntent>().Any(i =>
            i.Operation == FdpNodeOpType.PrepareLive || i.Operation == FdpNodeOpType.PrepareEdit);

    /// <summary>
    /// ⭐⭐ <b>A transition that names NO scenario is not delayed by a single frame.</b> §7.5 row 1.
    ///
    /// <para>⛔ This is the rail that keeps the change from becoming a blanket wait. Replay, preview, idle
    /// and every unload plan no <c>PrefetchScenario</c> step, so nothing is ever parked for them — the
    /// derivation, not a special case. ⚠ Without this case a "park everything, it is only a few frames"
    /// regression would stay green.</para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void A_transition_with_no_scenario_fans_out_in_the_same_tick()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        RegisterNode(bus, master);

        RequestTransition(master, Guid.NewGuid(), ClusterState.OperatingLive, scenarioId: null);
        master.Tick();
        bus.SwapBuffers();

        Assert.Null(master.ParkedRequestId);
        Assert.True(SawPrepare(bus),
            "A transition carrying no scenario must fan out in the tick that admits it.");
    }

    /// <summary>
    /// 🔴🔴 <b>A transition that DOES name a scenario fans out NOTHING until the distribution completes —
    /// and then fans out normally.</b> §7.5 rows 3 and 5, and the whole point of <c>L8</c>.
    ///
    /// <para>⭐⭐ The sequence is deliberately checked in three parts, because only the middle one is new:
    /// <b>(a)</b> nothing is sent while parked, <b>(b)</b> the reported state is still the SOURCE state —
    /// §7.2a row 1, the optimistic advance had to move to the execute half or the cluster would claim to be
    /// in the target state while its files were in flight — and <b>(c)</b> a second transition arriving
    /// meanwhile is REJECTED rather than queued (§7.2c).</para>
    ///
    /// <para>⚠ The distribution here is completed by acknowledging <c>PrefetchFiles</c> as a node would,
    /// which is exactly the set the saga already tracked; nothing about the wait is synthesised for the
    /// test.</para>
    /// </summary>
    [Fact(Timeout = 20_000)]
    public void A_scenario_transition_parks_until_every_node_has_acknowledged_its_files()
    {
        var scenarioDir = Path.Combine(
            _nasDir, Fdp.Toolkit.Orchestration.OrchestrationConstants.ScenariosDirectoryName, _scenarioId);
        Directory.CreateDirectory(scenarioDir);
        File.WriteAllText(Path.Combine(scenarioDir, "Hrot.SimHost.json"), "{}");

        var stagingDir = Path.Combine(Path.GetTempPath(), "fdp_pf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        var saga = new AssetPrefetchProcessManager(bus, new StorageGatewayModule(), _nasDir, stagingDir);
        RegisterNode(bus, master);

        var sourceState = master.CurrentClusterState;
        var reqId       = Guid.NewGuid();
        RequestTransition(master, reqId, ClusterState.LoadingEdit, _scenarioId);

        // ── (a) admitted, parked, and NOTHING fanned out ─────────────────────────────
        saga.Tick();
        master.Tick();
        bus.SwapBuffers();

        Assert.Equal(reqId, master.ParkedRequestId);
        Assert.False(SawPrepare(bus), "A parked transition must fan out NOTHING.");

        // ── (b) the reported state is still the SOURCE, not the target (§7.2a row 1) ──
        Assert.Equal(sourceState, master.CurrentClusterState);

        // ── (c) a second transition is REJECTED while one is parked (§7.2c) ──────────
        var secondId = Guid.NewGuid();
        RequestTransition(master, secondId, ClusterState.OperatingLive, scenarioId: null);
        saga.Tick();   // ⚠ EVERY frame — the intent that starts the copy is readable for ONE frame only.
        master.Tick();
        bus.SwapBuffers();

        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(),
            e => e.RequestId == secondId && e.StatusCode == OrchestrationStatusCode.Rejected);
        Assert.Equal(reqId, master.ParkedRequestId);

        // ── complete the distribution the way a node does: acknowledge PrefetchFiles ──
        bool fannedOut  = false;
        int  acksSent   = 0;
        int  completions = 0;
        var  deadline   = DateTime.UtcNow.AddSeconds(15);
        while (!fannedOut && DateTime.UtcNow < deadline)
        {
            foreach (var op in bus.ReadManaged<ExecuteNodeOpIntent>())
            {
                if (op.Operation != FdpNodeOpType.PrefetchFiles) continue;
                acksSent++;
                bus.PublishManaged(new NodeOpCompletedEvent
                {
                    TransactionId = op.TransactionId,
                    Operation     = FdpNodeOpType.PrefetchFiles,
                    NodeId        = op.TargetNodeId,
                    StatusCode    = OrchestrationStatusCode.Success,
                });
            }
            completions += bus.ReadManaged<PrefetchDistributionCompletedEvent>().Count();

            saga.Tick();
            master.Tick();
            bus.SwapBuffers();

            fannedOut = SawPrepare(bus);
            Thread.Sleep(10);
        }

        try { Directory.Delete(stagingDir, recursive: true); } catch { }

        Assert.True(fannedOut,
            "Once every node has acknowledged its files the parked transition must resume and fan out. "
          + $"(PrefetchFiles acked {acksSent}x, distribution-completed seen {completions}x, "
          + $"parked={master.ParkedRequestId}, state={master.CurrentClusterState})");
        Assert.Null(master.ParkedRequestId);
        Assert.Equal(ClusterState.LoadingEdit, master.CurrentClusterState);
    }

    /// <summary>
    /// 🔴🔴 <b>A FAILED distribution fails the request and fans out NOTHING.</b> §7.4 / §7.5 row 4.
    ///
    /// <para>⛔ This is the second defect the same change closes, and it was live before it: the saga
    /// reported a timeout to the requester while the transition had <em>already</em> been fanned out, so the
    /// cluster went on to build a world from files that never arrived. ⭐ Now the parked entry is dropped
    /// and the load never starts.</para>
    /// </summary>
    [Fact(Timeout = 20_000)]
    public void A_failed_distribution_fails_the_request_and_fans_out_nothing()
    {
        var stagingDir = Path.Combine(Path.GetTempPath(), "fdp_pf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        var saga = new AssetPrefetchProcessManager(bus, new StorageGatewayModule(), _nasDir, stagingDir);
        RegisterNode(bus, master);

        var reqId = Guid.NewGuid();
        RequestTransition(master, reqId, ClusterState.LoadingEdit, "nonexistent_scenario_for_l8");

        bool observedFailure = false;
        bool anyPrepare      = false;
        var  deadline        = DateTime.UtcNow.AddSeconds(15);
        while (!observedFailure && DateTime.UtcNow < deadline)
        {
            saga.Tick();
            master.Tick();
            bus.SwapBuffers();

            anyPrepare |= SawPrepare(bus);
            observedFailure |= bus.ReadManaged<ClusterOpCompletedEvent>()
                .Any(e => e.RequestId == reqId && e.StatusCode.IsError());
            Thread.Sleep(10);
        }

        try { Directory.Delete(stagingDir, recursive: true); } catch { }

        Assert.True(observedFailure, "A failed distribution must fail the originating request.");

        // ⚠ The saga's failure status and the distribution-completed event are published in the SAME frame,
        //   and the master reads the latter one frame later — so the unpark is checked after pumping, not
        //   at the instant the requester learns of the failure.
        for (int i = 0; i < 4; i++) { saga.Tick(); master.Tick(); bus.SwapBuffers(); anyPrepare |= SawPrepare(bus); }

        Assert.False(anyPrepare, "A failed distribution must fan out NO load step.");
        Assert.Null(master.ParkedRequestId);
    }

    /// <summary>
    /// ⚠ <b>The one liveness bound that remains</b> (§7.3 ⑤) — and it FAILS rather than proceeding.
    ///
    /// <para>⛔ It is not the timeout this change removes. The difference is the outcome: the removed waits
    /// carried on with whatever files happened to be there, silently; this one abandons the transition
    /// loudly and sends nothing. ⭐ A master with no prefetch saga at all is the sharpest way to produce
    /// "the staging never completed" — and <b>no production master is shaped like that</b>: both the
    /// cluster (<c>OrchestratorSubsystem.cs:271</c>) and the editor (<c>EditorSubsystem.cs:2150</c>)
    /// construct and tick one, which §7.2b records after the original claim that the editor stages nothing
    /// turned out to be false.</para>
    /// </summary>
    [Fact(Timeout = 20_000)]
    public void A_distribution_that_never_completes_expires_and_fans_out_nothing()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig())
        {
            ParkedTransitionExpirySeconds = 0.25,
        };
        RegisterNode(bus, master);   // ⛔ no AssetPrefetchProcessManager: nothing will ever answer.

        var reqId = Guid.NewGuid();
        RequestTransition(master, reqId, ClusterState.LoadingEdit, _scenarioId);

        master.Tick();
        bus.SwapBuffers();
        Assert.Equal(reqId, master.ParkedRequestId);

        bool expired    = false;
        bool anyPrepare = false;
        var  deadline   = DateTime.UtcNow.AddSeconds(15);
        while (!expired && DateTime.UtcNow < deadline)
        {
            master.Tick();
            bus.SwapBuffers();
            anyPrepare |= SawPrepare(bus);
            expired = bus.ReadManaged<ClusterOpCompletedEvent>()
                .Any(e => e.RequestId == reqId && e.StatusCode == OrchestrationStatusCode.Timeout);
            Thread.Sleep(20);
        }

        Assert.True(expired, "A parked transition whose staging never completes must FAIL, not hang.");
        Assert.False(anyPrepare, "An expired transition must fan out NOTHING.");
        Assert.Null(master.ParkedRequestId);
    }
}
