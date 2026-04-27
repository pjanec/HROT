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
using Hrot.Orchestrator.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Core.Network;

namespace Hrot.Orchestrator;

/// <summary>
/// Hosts <see cref="ClusterMaster"/> (DDS control plane + ID allocator server) under the Runner process.
/// Bypasses <c>WaitingRoomCoordinator</c> — boots instantly; UI renders immediately with a banner while
/// mandatory nodes are not yet ready (CGF1-S0105).
/// </summary>
public sealed class OrchestratorSubsystem : ISubsystem, IWindowRegistrar
{
    private ClusterMaster? _clusterMaster;
    private ReplayProcessManager? _replayProcessManager;
    private ClusterConfiguration _config = ClusterConfiguration.Default;
    private ClusterUiCache?        _uiCache;
    private ClusterScenarioPanel?  _scenarioPanel;

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
        _clusterMaster = new ClusterMaster(_bus, _config);
        // FIX: Wire the storage gateway so the cluster master can scan local/NAS scenarios
        // and publish AssetInventoryUpdateEvent to populate the UI combo box.
        var storageGateway = new StorageGatewayModule();
        _clusterMaster.SetStorageGateway(storageGateway, OrchestrationConstants.DefaultStagingDirectory);
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
        
        
        _clusterMaster.SetMasterSync(_masterSync);
    
        _bus.SwapBuffers();
        _timeTranslators  = _networkFactory?.CreateMasterTimeTranslators(_bus, config.NodeId)
                            ?? new NullMasterTimeTranslators();

        _uiCache       = new ClusterUiCache(_bus, _masterSync);
        _scenarioPanel = new ClusterScenarioPanel(_bus!, _uiCache);

        // Wire the replay process manager and register its aggregator with the cluster master.
        _replayProcessManager = new ReplayProcessManager(_bus, _masterSync);
        _clusterMaster.RegisterAggregator(_replayProcessManager.CreateAggregator());

        // CGF1-S0307: Create the global-context handler, subscribe to OnContextLoaded so the
        // MasterSyncController is seeded with the scenario's saved timeline on every load, and
        // register it with ClusterMaster so CommitState fan-outs trigger the local load path.
        // In headless mode (_networkFactory?.Participant == null) no DDS writer is available;
        // skip creation and leave _globalContextHandler null in ClusterMaster.
        var participant = _networkFactory?.Participant;
        if (participant != null)
        {
            var contextHandler = new GlobalContextClusterOpHandler(participant, string.Empty);
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
            _clusterMaster.SetGlobalContextHandler(contextHandler);
        }


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
        _masterSync?.Update();
        _clusterMaster?.Tick();
        _replayProcessManager?.Tick();

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
