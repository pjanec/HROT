using System;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Hrot.Orchestrator.Panels;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐ Q86 §4-E (R-215) — everything a host needs to run the cluster master, supplied by that host.
/// ⛔ No "is this the editor" flag: every difference between hosts is a VALUE here.
/// </summary>
public sealed class OrchestratorCoreOptions
{
    /// <summary>The host's orchestration bus. The host swaps it once per frame.</summary>
    public required FdpEventBus Bus { get; init; }

    /// <summary>The ONE configuration — every storage path is derived from it (Q86 §4-E).</summary>
    public required ClusterConfiguration Config { get; init; }

    /// <summary>How the core commands the clock — as requests on the bus (Q86 §4-C). The core never advances it.</summary>
    public required ITimeCommands TimeCommands { get; init; }

    /// <summary>
    /// Reads of the host's clock (time, scale). ⚠ The replay freeze/restore still sets the time scale through it
    /// directly, as it did before the extraction — it must land before the same frame's PrepareLive fan-out.
    /// </summary>
    public required ITimeController TimeReads { get; init; }

    /// <summary>The node id whose staging folder the master's own files use (orchestrator: 300; editor: its node).</summary>
    public required int StagingNodeId { get; init; }

    /// <summary>
    /// ⭐⭐ <c>CE-4087</c> — the roles the host plays IN-PROCESS: the editor composes all four simulation roles in the process that
    /// holds this core (<c>EditorCapabilities.DefaultRole</c>, <c>R-4005</c>); the orchestrator plays none (<c>None</c>). They count
    /// toward the master's load gate at once. ⛔ A host that plays roles MUST pass them, or it refuses its own loads.
    /// </summary>
    public Fdp.Core.NodeRole LocalRoles { get; init; } = Fdp.Core.NodeRole.None;

    /// <summary>The world's ONE id authority, reset at a scenario load (HN-037). Null ⇒ the host has none.</summary>
    public Fdp.Toolkit.NetworkSpawning.IWorldIdAuthority? IdAuthority { get; init; }

    /// <summary>⭐⭐ P6 part 2 (Q91 §4-E2) — the DDS file transport for nodes that advertised no share; null = share only (today).</summary>
    public Fdp.Toolkit.Orchestration.Transfer.IFileTransferProvider? DdsTransfer { get; init; }

    /// <summary>The file dialog the diagnostics panel opens.</summary>
    public required Fdp.Presentation.Abstractions.IFileDialogService FileDialogService { get; init; }
}

/// <summary>
/// ⭐⭐ Q86 (R-215) — the cluster master and every process manager around it, built ONCE and used by BOTH hosts:
/// the cluster's <see cref="OrchestratorSubsystem"/> and the editor (a one-node cluster). 📄
/// <c>docs/blueprints/Architect_Question_86_Editor_Runs_The_Orchestrator_Core.md</c> §3.1.
/// <para>⛔ What is NOT here, by design: the bus (the host owns and swaps it), the clock (the host creates and
/// advances it — the core only asks, through <see cref="ITimeCommands"/>), the network (the host's translators),
/// and the orchestrator's own presence as a cluster node.</para>
/// </summary>
public sealed class OrchestratorCore : IDisposable
{
    private readonly ClusterMaster                 _master;
    private readonly LiveBranchProcessManager      _liveBranch;
    private readonly ReplaySeekProcessManager      _seek;
    private readonly PreviewClockProcessManager    _previewClock;   // ⭐ CE-3299 B2 / D5
    private readonly GlobalContextProcessManager   _globalContext;
    private readonly AssetPrefetchProcessManager   _prefetch;
    private readonly ReplayProcessManager          _replay;
    private readonly ReplayClockProcessManager     _replayClock;   // ⭐ CE-3329 P1 — the sim axis jumps the paused stretches
    private readonly ReviewProcessManager          _review;        // ⭐ CE-3329 P3 — the review's clock: pause, L, the return
    private readonly ReplaySessionProcessManager   _session;       // ⭐ CE-3329 rows 32, 45 — the session layer, to every host
    private readonly StorageProcessManager         _storage;
    private readonly AssetInventoryProcessManager  _inventory;
    private readonly EpisodeProcessManager         _episodes;
    private readonly DiagnosticsDumpProcessManager _diagnosticsDump;
    private readonly DiagnosticLogMergeWorker      _mergeWorker;

    public ClusterMaster           Master           => _master;
    /// <summary>⭐ CE-3329 P3 — the review's clock (the live point L, whether a review is open).</summary>
    public ReviewProcessManager    Review           => _review;
    public ReplaySessionProcessManager ReplaySession => _session;
    public ClusterUiCache          UiCache          { get; }
    public ClusterScenarioPanel    ScenarioPanel    { get; }
    public ClusterDiagnosticsPanel DiagnosticsPanel { get; }

    public OrchestratorCore(OrchestratorCoreOptions o)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
        var bus    = o.Bus;
        var config = o.Config;
        string nas = config.NasBasePath;

        _master = new ClusterMaster(bus, config);
        _master.LocalNodeId = o.StagingNodeId;   // ⭐ P3 — the master never waits for the node it runs in
        _master.LocalRoles  = o.LocalRoles;      // ⭐ CE-4087 — the roles its own host plays count toward the load gate
        // ⭐ CE-3299 B3 (D3) — a checkpoint is named for ONE wall time on the clock every slave follows (silent-default rule:
        //   every host builds its master here, with its time reads, so none can forget it).
        _master.WallTicks = () => o.TimeReads.GetCurrentState().TotalWallTicks;
        // ⭐⭐⭐ HN-037 — the world's ONE id authority, so a scenario load resets it to 1000.
        //    📄 docs/DESIGN_Deterministic_Network_Ids.md §11.
        _master.IdAuthority = o.IdAuthority;

        UiCache       = new ClusterUiCache(bus, o.TimeReads);
        // ⭐ R-3055 — the master resolves a replay that names no exercise; the newest finished one is in the core's inventory read model
        _master.NewestExercise = () => UiCache.AvailableExercises.Length == 0
            ? null
            : UiCache.AvailableExercises.MaxBy(x => x.StartTimeUtc).ExerciseId;
        ScenarioPanel = new ClusterScenarioPanel(bus, UiCache);

        // Aggregators — one per node-op whose ACKs carry a result.
        _master.RegisterAggregator(new ReplaySeekAggregator());                                   // TASK-T002
        _master.RegisterAggregator(new CheckpointRestoreAggregator());                            // CE-3299 B4 (D5)
        _master.RegisterAggregator(new LiveBranchAggregator());                                   // CE-3312 — the branch's clock reaches the master
        _master.RegisterAggregator(new UnsavedCheckAggregator());                                 // ⭐ CE-4093 D20 — the unsaved-changes check round
        _replay = new ReplayProcessManager(bus, o.TimeReads);
        _master.RegisterAggregator(_replay.CreateAggregator());
        _master.RegisterAggregator(_replay.CreateReviewAggregator());                             // ⭐ CE-3329 P3
        _master.RegisterAggregator(new StorageConsensusAggregator());                             // TASK-S001
        _master.RegisterAggregator(new EpisodeConsensusAggregator(NodeOpType.StartEpisode));      // TASK-S003
        _master.RegisterAggregator(new EpisodeConsensusAggregator(NodeOpType.StopEpisode));
        var diagnosticsAggregator = new DiagnosticsConsensusAggregator();
        _master.RegisterAggregator(diagnosticsAggregator);

        // CGF1-S0307 — the scenario-load handler: restores the context and jumps the clock (Q86 §4-B/G).
        var contextHandler = new GlobalContextClusterOpHandler(bus, string.Empty)
        {
            LocalTempRoot = OrchestrationConstants.GetNodeStagingRoot(o.StagingNodeId),
        };
        var time = o.TimeCommands;
        contextHandler.OnContextLoaded += (startTicks, simTimeSeconds) =>
        {
            // ⭐ CE-122 / CE-3093 — a scenario load JUMPS the clock to the loaded time and holds it while the world
            //   is rebuilt. Whether it then runs is the load's property — GlobalContextProcessManager resumes at
            //   OperatingLive unless it asked to start paused (Q86 §4-G).
            time.SnapTo(LoadedClock(startTicks, simTimeSeconds, DateTime.UtcNow.Ticks));
            FdpLog<OrchestratorCore>.Info(
                "[Orchestrator] Scenario loaded: clock reset to SimTime={1:F1}s (WallTicks={0})",
                startTicks, simTimeSeconds);
        };
        _globalContext = new GlobalContextProcessManager(bus, contextHandler);

        // ⭐⭐ P6 part 2 — the transport per target: the share a node advertised, else DDS (when this host has one).
        var transfer = o.DdsTransfer == null
            ? StorageGatewayModule.DefaultProvider
            : new Fdp.Toolkit.Orchestration.Transfer.RoutingTransferProvider(
                StorageGatewayModule.DefaultProvider, o.DdsTransfer,
                hasShare: nodeId => Fdp.Toolkit.Orchestration.Transfer.TransferTokens.ParseShare(
                    _master.ActiveNodeCapabilitySnapshot().TryGetValue(nodeId, out var tokens) ? tokens : null) != null,
                nodeRootSeenByMaster: nodeId => OrchestrationConstants.GetNodeStagingRoot(OrchestrationConstants.ResolveStagingRoot(), nodeId),
                canReceiveDds: nodeId => Fdp.Toolkit.Orchestration.Transfer.TransferTokens.HasDdsReceiver(
                    _master.ActiveNodeCapabilitySnapshot().TryGetValue(nodeId, out var tokens) ? tokens : null));
        var storageGateway = new StorageGatewayModule(transfer);
        _storage   = new StorageProcessManager(bus, storageGateway, nas);                         // TASK-S002
        _inventory = new AssetInventoryProcessManager(                                            // CGF1-S0506
            bus, storageGateway, nas, OrchestrationConstants.ResolveStagingRoot(), o.StagingNodeId);
        _episodes  = new EpisodeProcessManager(bus);                                              // TASK-S003
        // ⭐ CE-4090 (AQ108 D15) — the master discards an unarchived recording through the inventory that holds its ledger (silent-default rule:
        //   every host builds its master here, so none can forget it).
        _master.ExerciseDeleter = _inventory.DeleteExercise;

        // TASK-T001 — the live branch. Must tick BEFORE ClusterMaster so FreezeTime runs before the PrepareLive fan-out.
        var timeReads = o.TimeReads;
        var replayMasterModule = new ReplayMasterModule(
            scale => timeReads.SetTimeScale(scale),
            () => timeReads.GetTimeScale());
        _liveBranch = new LiveBranchProcessManager(bus, replayMasterModule, time,
            map: () => _master.ReplayTimeMap, anchorWallTicks: () => _master.ReplayAnchorWallTicks,   // ⭐ CE-4065 — the branch resumes on sim time
            operatorRate: () => _replayClock!.OperatorRate);   // ⭐ CE-4067 — and at the operator's rate, not the replay segment's

        // TASK-T002 — the seek. Must tick BEFORE ClusterMaster so its precondition events precede the fan-out.
        _seek = new ReplaySeekProcessManager(bus, time, _master.StepRoster);   // ⭐ P3 — one roster rule

        // ⭐ CE-3329 P3 — a review's clock. Must tick BEFORE ClusterMaster so its barrier pause precedes the PrepareReview fan-out.
        _review = new ReviewProcessManager(bus, timeReads, time,
            () => (Fdp.Toolkit.Orchestration.ClusterState)(int)_master.CurrentClusterState,
            runNotRecorded: () => _master.RunNotRecorded);   // ⭐ CE-4090 D14 — a run with no recording is not paused for a review the master refuses

        // ⭐ CE-3329 rows 32, 45 — the session layer (§9f): republished on every change, to the DDS topic and this host's own cache
        _session = new ReplaySessionProcessManager(bus, _master, _review);

        // ⭐ CE-3329 P1 (R4) — on the Sim axis a running replay jumps every paused stretch (non-pausing: no epoch move)
        _replayClock = new ReplayClockProcessManager(timeReads, time,
            replaying:       () => Fdp.Toolkit.Orchestration.ClusterStates.IsReplaying((Fdp.Toolkit.Orchestration.ClusterState)(int)_master.CurrentClusterState),
            axis:            () => _master.ReplayAxis,
            map:             () => _master.ReplayTimeMap,
            anchorWallTicks: () => _master.ReplayAnchorWallTicks,
            loop:            () => _master.ReplayLoop,   // ⭐ CE-3329 §12n — the loop range wraps the playhead on either axis
            durationSeconds: () => _master.ReplayDurationSeconds,   // ⭐ row 9 — the end pause (absorbed from ReplayProcessManager)
            reviewing:       () => Fdp.Toolkit.Orchestration.ClusterStates.IsReview((Fdp.Toolkit.Orchestration.ClusterState)(int)_master.CurrentClusterState),
            bus:             bus);                                   // ⭐ row 9 — the operator's rate requests

        // ⭐ CE-3299 B2 / D5 — a cluster dry run puts the clock back with the world (the editor's own Play/Stop already does, CE-3156).
        _previewClock = new PreviewClockProcessManager(bus, timeReads, time);

        // TASK-P002 — the prefetch. Must tick BEFORE ClusterMaster so PrefetchStagingCompletedEvent is ready.
        _prefetch = new AssetPrefetchProcessManager(bus, storageGateway, nas, tkbName: config.TkbName);   // ⭐ R-4018

        // ⭐ CE-3021 — explicit publish / refresh over the SAME gateway and NAS (silent-default rule).
        _master.AssetSync = new AssetSyncService(storageGateway, nas, _master.ActiveNodeCapabilitySnapshot);

        // ⭐ Q86 §4-E — the dumps go under the ONE NAS root on every host (the editor used to put them in the
        //   scenarios folder).
        _diagnosticsDump = new DiagnosticsDumpProcessManager(bus, storageGateway, nas, diagnosticsAggregator);
        _mergeWorker     = new DiagnosticLogMergeWorker(bus);
        DiagnosticsPanel = new ClusterDiagnosticsPanel(UiCache, bus, o.FileDialogService, nas);
    }

    /// <summary>
    /// One frame of the master. Call AFTER the host's bus swap and the host's clock update. The order is the
    /// cluster orchestrator's, unchanged by the extraction.
    /// </summary>
    public void Tick()
    {
        _liveBranch.Tick();
        _seek.Tick();
        _review.Tick();   // CE-3329 — before the master (the entry pause precedes the fan-out)
        _previewClock.Tick();
        _globalContext.Tick();
        _prefetch.Tick();
        _master.Tick();
        _replayClock.Tick();   // CE-3329
        _session.Tick();       // CE-3329 — after the master: a round that completed this frame has updated the zero, map and marks
        _storage.Tick();
        _inventory.Tick();
        _episodes.Tick();
        _diagnosticsDump.Tick();
        _mergeWorker.Tick();
    }

    /// <summary>Local observation, after <see cref="Tick"/>: the UI cache, then the scenario panel's debounce.</summary>
    public void TickUi(float deltaTime)
    {
        UiCache.Update();
        ScenarioPanel.Update(deltaTime);
    }

    public void Dispose()
    {
        UiCache.Dispose();
        _master.Dispose();
        _mergeWorker.Dispose();
    }

    /// <summary>
    /// ⭐ backend `2026-10-26` (run C) — where a scenario load puts the clock: the loaded sim time, and the wall clock on the REAL
    /// clock's basis (<paramref name="nowTicks"/>) unless the context names its own start. ⛔ Never wall 0: every resume re-anchors the
    /// wall to the real clock (MasterSyncController), so a load at 0 gave a recording TWO bases — 📌 measured on the editor (its own
    /// clock is the master): frames from 1.7e8 to 6.39e17 ticks, a replay "63 928 598 528 s" long.
    /// </summary>
    public static GlobalTime LoadedClock(long startTicks, double simTimeSeconds, long nowTicks) => new()
    {
        TotalWallTicks    = startTicks != 0 ? startTicks : nowTicks,
        TotalTime         = simTimeSeconds,
        UnscaledTotalTime = simTimeSeconds,
    };
}
