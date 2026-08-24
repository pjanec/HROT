using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Orchestrator;
using Hrot.Common;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Fdp.Core.Logging;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Messages;
using ImGuiNET;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Time;
using Fdp.ModuleHost.Diagnostics;
using Fdp.Toolkit.Diagnostics;
using Fdp.Core.Diagnostics;
using Hrot.Orchestrator.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Core.Network;
using Hrot.Core.Diagnostics;
using Hrot.Common.Diagnostics;

namespace Hrot.Orchestrator;

/// <summary>
/// Hosts <see cref="ClusterMaster"/> (DDS control plane + ID allocator server) under the Runner process.
/// Bypasses <c>WaitingRoomCoordinator</c> — boots instantly; UI renders immediately with a banner while
/// mandatory nodes are not yet ready (CGF1-S0105).
/// </summary>
public sealed class OrchestratorSubsystem : ISubsystem, IWindowRegistrar
{
    private ClusterMaster? _clusterMaster;
    private LiveBranchProcessManager?   _liveBranchProcessManager;
    private ReplaySeekProcessManager?   _seekProcessManager;
    private ReplayProcessManager? _replayProcessManager;
    private StorageProcessManager? _storageProcessManager;
    private EpisodeProcessManager? _episodeProcessManager;
    private GlobalContextProcessManager? _globalContextProcessManager;
    private AssetPrefetchProcessManager? _assetPrefetchProcessManager;
    private AssetInventoryProcessManager? _assetInventoryProcessManager;
    private DiagnosticsDumpProcessManager? _diagnosticsDumpProcessManager;
    private DiagnosticLogMergeWorker?      _mergeWorker;
    private ClusterDiagnosticsPanel?       _diagnosticsPanel;
    private Fdp.Presentation.Abstractions.IFileDialogService? _fileDialogService;
    private ClusterConfiguration _config = ClusterConfiguration.Default;
    private ClusterUiCache?        _uiCache;
    private ClusterScenarioPanel?  _scenarioPanel;
    private ClusterSlave? _clusterSlave;

    // ── Unified event bus (HEXAG2-S001) ─────────────────────────────────────
    private FdpEventBus?                   _bus;
    // ── Factory-managed infrastructure handles (HEXAG2-S008) ─────────────
    private INetworkFactory?               _networkFactory;
    private IOrchestrationTranslator?      _translator;
    private IDisposable?                   _idAllocatorServerHandle;
    private IMasterTimeTranslators?        _timeTranslators;
    // ── Time controller (CGF1-A.1, BATCH-09) ─────────────────────────────
    // MasterSyncController unifies wall-clock advancement, barrier protocol, and stepping.
    private MasterSyncController?          _masterSync;
    private string?                        _lastProcessedTimeMode;

    /// <summary>Internal event bus exposed for test assertions on SwitchTimeModeEvent.</summary>
    internal FdpEventBus? TimeBusForTest => _bus;

    /// <summary>Internal test hook: exposes the <see cref="ClusterUiCache"/> for bus-unification assertions.</summary>
    internal ClusterUiCache? UiCacheForTest => _uiCache;

    /// <summary>
    /// Internal test hook: exposes the <see cref="ClusterMaster"/> hosted by this subsystem so
    /// E2E test fixtures can inject <see cref="ClusterOpRequest"/> values via
    /// <see cref="ClusterMaster.HandleClusterOpRequest"/> and read cluster state.
    /// </summary>
    internal ClusterMaster? TestHook_ClusterMaster => _clusterMaster;

    /// <summary>Internal test hook: current master sim time in seconds.</summary>
    internal double TestHook_CurrentSimTime => _masterSync?.GetCurrentState().TotalTime ?? 0.0;

    /// <summary>
    /// TestHook: the master controller's current time scale. Exposed so an integration test can
    /// assert what the SetTimeScale cluster op actually delivered without inferring it from an
    /// observed sim-time slope.
    /// </summary>
    internal float TestHook_TimeScale => _masterSync?.GetTimeScale() ?? 0.0f;

    /// <summary>
    /// ⭐⭐ <b>The ONE fact the debug API's ack-gate needs: is the master still awaiting step ACKs?</b>
    /// <para><see langword="null"/> ⇒ <b>this node hosts no master</b> (parameterless/headless construction, or
    /// after <see cref="Shutdown"/> disposes it) ⇒ a step cannot be confirmed cluster-wide here.
    /// <see langword="true"/>/<see langword="false"/> ⇒ the master's own answer.</para>
    /// <para>⭐ Deliberately the NARROWEST surface rather than the controller itself:
    /// <see cref="MasterSyncController"/> also exposes <c>Step</c>/<c>SetTimeScale</c>, and handing those to the
    /// debug host would invite it to drive time directly — bypassing the perspective-scoped drive facade that
    /// <c>Architect_Question_54</c> Q54-2 established ("issue where the user is, confirm where the truth is").</para>
    /// <para>⚠ Read LIVE, never latched: <c>_masterSync</c> is created in <see cref="Initialize"/> and set back to
    /// <see langword="null"/> in <see cref="Shutdown"/>, so a captured reference would outlive the master and lie.</para>
    /// </summary>
    public bool? IsAwaitingStepAcks => _masterSync?.IsAwaitingStepAcks;

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);  // S0501: beige

    // used by tests
    public OrchestratorSubsystem()
    {
    }


    // used by ClusterMaster (HEXAG2-S008: factory-based constructor)
    public OrchestratorSubsystem(INetworkFactory networkFactory)
    {
        _networkFactory = networkFactory;
    }

    public void Initialize(SubsystemConfig config)
    {
        _config = ClusterConfiguration.LoadFrom(
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));

        // HEXAG2-S008: Use INetworkFactory to create the participant.
        // Parameterless constructor (headless/test mode) leaves _networkFactory null;
        // in that case factory calls return Null-object implementations via ?. / ?? operator.
        if (_networkFactory != null)
        {
            _networkFactory = _networkFactory.ConfigureForNode(_networkFactory.Participant, config.NodeId, NodeRole.None);
        }

        // ── Single unified event bus (HEXAG2-S001) ────────────────────────────────
        _bus          = new FdpEventBus();
        Fdp.Toolkit.Orchestration.OrchestrationEventRegistry.RegisterAll(_bus);
        OrchestratorEventRegistry.RegisterInternalEvents(_bus);
        _clusterMaster = new ClusterMaster(_bus, _config);
        int orchestratorNodeId = config.NodeId != 0 ? config.NodeId : 300;
        _clusterSlave = new ClusterSlave(orchestratorNodeId, "Orchestrator", _bus);
        string isolatedTempRoot = OrchestrationConstants.GetNodeStagingRoot(orchestratorNodeId);
        string resolvedLogDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs");
        var orchestratorLogService = new LogArchiveExtractionService(
            resolvedLogDir,
            "Orchestrator",
            orchestratorNodeId);
        _clusterSlave.RegisterHandler(new DiagnosticsDumpClusterOpHandler(
            new OrchestratorNullDiagnosticEventHistoryService(),
            new ArchitectureDiagnosticsService(() => null),
            new OrchestratorNullEntityStateExtractionService(),
            orchestratorLogService,
            new Hrot.Common.Infrastructure.HrotNodeConfig
            {
                NodeId = orchestratorNodeId,
                SubsystemName = "Orchestrator",
                LocalTempRoot = isolatedTempRoot,
                LogDirectory = resolvedLogDir,
            }));
        // FIX: Wire the storage gateway so the cluster master can scan local/NAS scenarios
        // and publish AssetInventoryUpdateEvent to populate the UI combo box.
        var storageGateway = new StorageGatewayModule();
        _translator    = _networkFactory?.CreateOrchestratorTranslators(_bus, config.NodeId)
                         ?? new NullOrchestrationTranslator();
        _idAllocatorServerHandle = _networkFactory?.CreateIdAllocatorServer()
                                   ?? new NullDisposable();

        // ── Time controller setup (CGF1-A.1, BATCH-09) ─────────────────────
        // Must be created before _timeTranslators so the initial SwitchTimeModeEvent{Continuous}
        // is published to _bus PENDING. Swap it immediately so the first ScanAndPublish can
        // read it and forward it to DDS before slaves start their kernels.
        _masterSync       = new MasterSyncController(
            _bus, new HashSet<int>(), TimeConfig.Default);
        
        
        _bus.SwapBuffers();
        _timeTranslators  = _networkFactory?.CreateMasterTimeTranslators(_bus, config.NodeId)
                            ?? new NullMasterTimeTranslators();

        _uiCache       = new ClusterUiCache(_bus, _masterSync);
        _scenarioPanel = new ClusterScenarioPanel(_bus!, _uiCache);

        // TASK-T002: Register ReplaySeekAggregator with ClusterMaster.
        _clusterMaster.RegisterAggregator(new ReplaySeekAggregator());

        // Wire the replay process manager and register its aggregator with the cluster master.
        _replayProcessManager = new ReplayProcessManager(_bus, _masterSync);
        _clusterMaster.RegisterAggregator(_replayProcessManager.CreateAggregator());

        // TASK-S001: Register the storage consensus aggregator.
        _clusterMaster.RegisterAggregator(new StorageConsensusAggregator());

        // TASK-S003: Register episode consensus aggregators for StartEpisode and StopEpisode.
        _clusterMaster.RegisterAggregator(new EpisodeConsensusAggregator(Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode));
        _clusterMaster.RegisterAggregator(new EpisodeConsensusAggregator(Fdp.Toolkit.Orchestration.NodeOpType.StopEpisode));

        // Register DiagnosticsConsensusAggregator for DumpDiagnostics cluster ops.
        var diagnosticsAggregator = new DiagnosticsConsensusAggregator();
        _clusterMaster.RegisterAggregator(diagnosticsAggregator);

        // CGF1-S0307: Create the global-context handler, subscribe to OnContextLoaded so the
        // MasterSyncController is seeded with the scenario's saved timeline on every load.
        // In headless mode (_networkFactory?.Participant == null) no DDS writer is available;
        // skip creation and leave _globalContextProcessManager null.
        var participant = _networkFactory?.Participant;
        GlobalContextClusterOpHandler? contextHandler = null;
        if (participant != null)
        {
            contextHandler = new GlobalContextClusterOpHandler(participant, string.Empty);
            contextHandler.LocalTempRoot = isolatedTempRoot;
            contextHandler.OnContextLoaded += (startTicks, simTimeSeconds) =>
            {
                if (_masterSync != null)
                {
                    _masterSync.SeedState(new GlobalTime
                    {
                        TotalWallTicks    = startTicks,
                        TotalTime         = simTimeSeconds,
                        UnscaledTotalTime = simTimeSeconds,
                        TimeScale         = _masterSync.GetTimeScale(),
                    });
                    FdpLog<OrchestratorSubsystem>.Info(
                        "[Orchestrator] Seeded MasterSyncController: WallTicks={0}, SimTime={1:F1}s",
                        startTicks, simTimeSeconds);
                }
            };
            _globalContextProcessManager = new GlobalContextProcessManager(_bus!, contextHandler);
        }

        // TASK-S002: Wire the storage process manager (TASK-P001: shim removed).
        _storageProcessManager = new StorageProcessManager(
            _bus!,
            storageGateway,
            _config.NasBasePath);

        // CGF1-S0506: Wire the asset inventory process manager.
        // Polls the storage gateway every 5 seconds and publishes AssetInventoryUpdateEvent.
        _assetInventoryProcessManager = new AssetInventoryProcessManager(
            _bus!,
            storageGateway,
            _config.NasBasePath,
            OrchestrationConstants.ResolveStagingRoot(),
            orchestratorNodeId);

        // TASK-S003: Wire the episode process manager.
        _episodeProcessManager = new EpisodeProcessManager(_bus);

        // TASK-T001: Wire the live-branch process manager (CGF1-S0305).
        // Must tick BEFORE ClusterMaster.Tick() so FreezeTime runs before the PrepareLive fan-out.
        var replayMasterModule = new ReplayMasterModule(
            scale => _masterSync!.SetTimeScale(scale),
            () => _masterSync!.GetTimeScale());
        _liveBranchProcessManager = new LiveBranchProcessManager(_bus, replayMasterModule, _masterSync);

        // TASK-T002: Wire the seek process manager (SnapAndPause + precondition events).
        // Must tick BEFORE ClusterMaster.Tick() so precondition events arrive before the seek fan-out.
        _seekProcessManager = new ReplaySeekProcessManager(_bus, _masterSync!);

        // TASK-P002: Wire the asset prefetch process manager.
        // Must tick BEFORE ClusterMaster.Tick() so ExecutePrefetchIntent is consumed and
        // PrefetchStagingCompletedEvent is published before ProcessPrefetchStagingCompleted runs.
        _assetPrefetchProcessManager = new AssetPrefetchProcessManager(
            _bus!,
            storageGateway,
            _config.NasBasePath);

        // Wire the diagnostics dump process manager for DumpDiagnostics cluster ops.
        _diagnosticsDumpProcessManager = new DiagnosticsDumpProcessManager(
            _bus!,
            storageGateway,
            _config.NasBasePath,
            diagnosticsAggregator);

        // Wire the diagnostic log merge worker (K-way merge on MergeLogsIntent).
        _mergeWorker = new DiagnosticLogMergeWorker(_bus!);

        // Wire the diagnostics panel (reads from _uiCache, publishes via _bus).
        _fileDialogService = Fdp.Presentation.Panels.FileDialogServiceFactory.Create();
        _diagnosticsPanel = new ClusterDiagnosticsPanel(
            _uiCache!,
            _bus!,
            _fileDialogService,
            _config.NasBasePath);

        // Drain the read buffer locally so the cache captures the bootstrapped state
        // before the first frame's Phase 2 SwapBuffers wipes it out.
        _uiCache.Update();
    }

    public void Update(float deltaTime)
    {
        // Phase 1: Network boundary — DDS ingress/egress (HEXAG2-S008).
        // ScanAndPublish reads from _bus CURRENT and sends to DDS (time-mode + lockstep).
        // PollIngress reads from DDS and writes to _bus WRITE buffer.
        // _translator.Tick() bridges DDS heartbeats, ClusterOpRequests, and NodeOpStatuses.
        _timeTranslators?.ScanAndPublish();
        _timeTranslators?.PollIngress();
        _translator?.Tick();

        // Phase 2: Single frame boundary swap — exactly one SwapBuffers per frame.
        _bus?.SwapBuffers();

        // Phase 3: Core logic — MasterSyncController drains bus intents (HEXAG2-S011),
        // then ClusterMaster ticks to process fan-out ops and 2PC tracking.
        // Then ReplayProcessManager auto-pauses the clock when replay ends.
        // Then StorageProcessManager handles NAS pulls for completed SerializeLocal ops.
        // Then EpisodeProcessManager updates active episode state and publishes EpisodeStateChangedEvent.
        _masterSync?.Update();
        _liveBranchProcessManager?.Tick();
        _seekProcessManager?.Tick();
        _globalContextProcessManager?.Tick();
        _assetPrefetchProcessManager?.Tick();
        _clusterMaster?.Tick();
        _replayProcessManager?.Tick();
        _storageProcessManager?.Tick();
        _assetInventoryProcessManager?.Tick();
        _episodeProcessManager?.Tick();
        _diagnosticsDumpProcessManager?.Tick();
        _mergeWorker?.Tick();
        _clusterSlave?.Tick();

        // CGF1-A.1: Consume PendingTimeMode and drive MasterSyncController.
        var pendingMode = _clusterMaster?.PendingTimeMode;
        if (pendingMode != _lastProcessedTimeMode)
        {
            if (pendingMode == "Deterministic" && _masterSync != null && _clusterMaster != null)
            {
                // Exclude ExCon: it has no simulation kernel and never sends FrameAck.
                var slaveIds = _clusterMaster.NodeRoster.ActiveNodes
                    .Where(kv => kv.Value.SubsystemName is "SimHost" or "IG" or "CGF")
                    .Select(kv => kv.Key)
                    .ToHashSet();
                _masterSync.SwitchToDeterministic(slaveIds);
            }
            _lastProcessedTimeMode = pendingMode;
        }

        // Phase 4: Local observation.
        // CGF1-S0506: Update cache after ClusterMaster tick so it reflects latest state.
        _uiCache?.Update();

        // S0503: Advance seek debounce.
        _scenarioPanel?.Update(deltaTime);

        // Phase 5: Time-sync NTP ingress (NTP responses from slaves).
        _timeTranslators?.PollNtpIngress();
    }

    public void DrawWorld() { }

    public void DrawUI() { /* panels registered as ManagedWindows via IWindowRegistrar */ }

    /// <inheritdoc/>
    public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
    {
        if (_scenarioPanel == null) return;
        windowManager.RegisterWindow(new OrchestratorWindow(_scenarioPanel));

        // Register diagnostics window.
        if (_diagnosticsPanel != null)
            windowManager.RegisterWindow(new DiagnosticsWindow(_diagnosticsPanel));

        // Wire the ImGui file dialog fallback so it renders on non-Windows hosts.
        // Harmless no-op for the Win32 backend: WindowManager only draws the service
        // when it is an ImGuiFileDialogService.
        if (_fileDialogService != null)
            windowManager.SetFileDialogService(_fileDialogService);
    }

    public void Shutdown()
    {
        _scenarioPanel = null;
        _uiCache?.Dispose();
        _uiCache = null;
        // Dispose ID allocator server first — joins its polling thread before any DDS teardown.
        _idAllocatorServerHandle?.Dispose();
        _idAllocatorServerHandle = null;
        // Dispose the orchestration translator — tears down DDS readers/writers.
        _translator?.Dispose();
        _translator = null;
        // Dispose time translators.
        _timeTranslators?.Dispose();
        _timeTranslators = null;
        _bus = null;
        _replayProcessManager = null;
        _clusterMaster?.Dispose();
        _clusterMaster = null;
        _clusterSlave?.Dispose();
        _clusterSlave = null;
        _assetInventoryProcessManager = null;
        _diagnosticsDumpProcessManager = null;
        _mergeWorker?.Dispose();
        _mergeWorker = null;
        _diagnosticsPanel = null;
        _fileDialogService = null;
        _masterSync?.Dispose();
        _masterSync = null;
        _lastProcessedTimeMode = null;
        _networkFactory = null;
    }

    /// <summary>
    /// Formats a JSON string with indentation for tooltip display.
    /// Returns the original string if parsing fails (CGF1-S0501).
    /// </summary>
    internal static string FormatPrettyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }

    /// <summary>
    /// Parses a FixedDelta seconds value from a <c>StepTime</c> payload JSON.
    /// Returns <paramref name="fallback"/> when the payload is absent, malformed,
    /// or contains a non-positive value.
    /// </summary>
    internal static float ParseStepDelta(string payload, float fallback)
    {
        if (string.IsNullOrWhiteSpace(payload)) return fallback;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("FixedDelta", out var el))
            {
                float v = el.GetSingle();
                return v > 0f ? v : fallback;
            }
        }
        catch { }
        return fallback;
    }
}

internal sealed class OrchestratorNullEntityStateExtractionService : IEntityStateExtractionService
{
    public IReadOnlyList<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? networkIds = null)
        => Array.Empty<EntityStateDumpDto>();
}

internal sealed class OrchestratorNullDiagnosticEventHistoryService : IDiagnosticEventHistoryService
{
    public void Capture(string providerName, FdpEventBus eventBus, uint currentFrame) { }

    public CapturedEventDto[] GetHistory(IReadOnlyList<string>? providerFilter = null)
        => Array.Empty<CapturedEventDto>();

    public void ClearHistory() { }

    public void RewindHistory(uint toFrame) { }
}
