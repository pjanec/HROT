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

    /// <summary>The world's ONE id authority, reset at a scenario load (HN-037). Null ⇒ the host has none.</summary>
    public Fdp.Toolkit.NetworkSpawning.IWorldIdAuthority? IdAuthority { get; init; }

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
    private readonly GlobalContextProcessManager   _globalContext;
    private readonly AssetPrefetchProcessManager   _prefetch;
    private readonly ReplayProcessManager          _replay;
    private readonly StorageProcessManager         _storage;
    private readonly AssetInventoryProcessManager  _inventory;
    private readonly EpisodeProcessManager         _episodes;
    private readonly DiagnosticsDumpProcessManager _diagnosticsDump;
    private readonly DiagnosticLogMergeWorker      _mergeWorker;

    public ClusterMaster           Master           => _master;
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
        // ⭐⭐⭐ HN-037 — the world's ONE id authority, so a scenario load resets it to 1000.
        //    📄 docs/DESIGN_Deterministic_Network_Ids.md §11.
        _master.IdAuthority = o.IdAuthority;

        UiCache       = new ClusterUiCache(bus, o.TimeReads);
        ScenarioPanel = new ClusterScenarioPanel(bus, UiCache);

        // Aggregators — one per node-op whose ACKs carry a result.
        _master.RegisterAggregator(new ReplaySeekAggregator());                                   // TASK-T002
        _replay = new ReplayProcessManager(bus, o.TimeReads);
        _master.RegisterAggregator(_replay.CreateAggregator());
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
            time.SnapTo(new GlobalTime
            {
                TotalWallTicks    = startTicks,
                TotalTime         = simTimeSeconds,
                UnscaledTotalTime = simTimeSeconds,
            });
            FdpLog<OrchestratorCore>.Info(
                "[Orchestrator] Scenario loaded: clock reset to SimTime={1:F1}s (WallTicks={0})",
                startTicks, simTimeSeconds);
        };
        _globalContext = new GlobalContextProcessManager(bus, contextHandler);

        var storageGateway = new StorageGatewayModule();
        _storage   = new StorageProcessManager(bus, storageGateway, nas);                         // TASK-S002
        _inventory = new AssetInventoryProcessManager(                                            // CGF1-S0506
            bus, storageGateway, nas, OrchestrationConstants.ResolveStagingRoot(), o.StagingNodeId);
        _episodes  = new EpisodeProcessManager(bus);                                              // TASK-S003

        // TASK-T001 — the live branch. Must tick BEFORE ClusterMaster so FreezeTime runs before the PrepareLive fan-out.
        var timeReads = o.TimeReads;
        var replayMasterModule = new ReplayMasterModule(
            scale => timeReads.SetTimeScale(scale),
            () => timeReads.GetTimeScale());
        _liveBranch = new LiveBranchProcessManager(bus, replayMasterModule, time);

        // TASK-T002 — the seek. Must tick BEFORE ClusterMaster so its precondition events precede the fan-out.
        _seek = new ReplaySeekProcessManager(bus, time);

        // TASK-P002 — the prefetch. Must tick BEFORE ClusterMaster so PrefetchStagingCompletedEvent is ready.
        _prefetch = new AssetPrefetchProcessManager(bus, storageGateway, nas);

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
        _globalContext.Tick();
        _prefetch.Tick();
        _master.Tick();
        _replay.Tick();
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
}
