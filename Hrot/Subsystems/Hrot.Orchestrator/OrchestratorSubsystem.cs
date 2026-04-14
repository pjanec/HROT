using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Map.Common;
using Hrot.Orchestrator;
using Hrot.Orchestrator.Translators;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Engine.Runner;
using FDP.Kernel.Logging;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;
using ImGuiNET;
using Fdp.ModuleHost.Core;
using Fdp.ModuleHost.Core.Time;
using Hrot.Orchestrator.Windows;
using Hrot.Orchestrator.Panels;
using Fdp.ModuleHost.Network.Cyclone.Services;

namespace Hrot.Orchestrator;

/// <summary>
/// Hosts <see cref="ClusterMaster"/> (DDS control plane + ID allocator server) under the Runner process.
/// Bypasses <c>WaitingRoomCoordinator</c> — boots instantly; UI renders immediately with a banner while
/// mandatory nodes are not yet ready (CGF1-S0105).
/// </summary>
public sealed class OrchestratorSubsystem : ISubsystem, IWindowRegistrar
{
    private DdsParticipant? _participant;
    private ClusterMaster? _clusterMaster;
    private ClusterConfiguration _config = ClusterConfiguration.Default;
    private ClusterUiCache?        _uiCache;
    private ClusterScenarioPanel?  _scenarioPanel;
#pragma warning disable CS0169 // dead field retained per TODO PACK-E001
    private DdsWriter<ClusterOpRequest>? _sysOpWriter;  // S0502 — TODO PACK-E001: dead field; remove in follow-up
#pragma warning restore CS0169

    private bool _isPaused;   // S0503: toggled by TimeControlRequested handler
    // ── Orchestration bus + translators (CMC-S016 / BATCH-06) ────────────────
    private FdpEventBus?                             _orchestrationBus;
    private Hrot.Orchestrator.Translators.ClusterOpMasterTranslator? _clusterOpTranslator;
    private Hrot.Orchestrator.Translators.NodeOpMasterTranslator?    _nodeOpTranslator;
    private DdsReader<ClusterOpRequest>?             _sysOpRequestReader;  // owned here in bus mode
    private DdsWriter<ClusterOpStatus>?              _sysOpStatusWriter;   // owned here in bus mode
    private DdsReader<NodeOpStatus>?                 _nodeOpStatusReader;
    private DdsReader<NodeHeartbeat>?                _heartbeatReader;     // DDS→bus heartbeat bridge
    // ── ID allocator server (bus-mode ClusterMaster doesn't create this) ──
    private DdsIdAllocatorServer?      _idAllocatorServer;
    private System.Threading.CancellationTokenSource? _idServerCts;
    private System.Threading.Thread?   _idServerThread;
    // ── Time controller (CGF1-A.1, BATCH-09) ─────────────────────────────
    // MasterSyncController unifies wall-clock advancement, barrier protocol, and stepping.
    private FdpEventBus? _eventBus;
    private FDP.Toolkit.Time.Controllers.MasterSyncController? _masterSync;
    private IDescriptorTranslator? _timeModeTranslator;
    private IDescriptorTranslator? _lockstepTranslator;
    private IDescriptorTranslator? _masterTimeSyncTranslator;
    private string? _lastProcessedTimeMode;

    /// <summary>Internal event bus exposed for test assertions on SwitchTimeModeEvent.</summary>
    internal FdpEventBus? TimeBusForTest => _eventBus;

    /// <summary>
    /// Internal test hook: exposes the <see cref="ClusterMaster"/> hosted by this subsystem so
    /// E2E test fixtures can inject <see cref="ClusterOpRequest"/> values via
    /// <see cref="ClusterMaster.HandleClusterOpRequest"/> and read cluster state.
    /// </summary>
    internal ClusterMaster? TestHook_ClusterMaster => _clusterMaster;

    /// <summary>Internal test hook: exposes current pause state for assertions.</summary>
    internal bool IsPausedForTest => _isPaused;

    /// <summary>Internal test hook: current master sim time in seconds.</summary>
    internal double TestHook_CurrentSimTime => _masterSync?.GetCurrentState().TotalTime ?? 0.0;

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);  // S0501: beige

    public void Initialize(SubsystemConfig config)
    {
        _config      = ClusterConfiguration.LoadFrom(
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));
        _participant = HrotEnvironment.CreateParticipant(config.DomainId);

        // ── Orchestration bus + translators (CMC-S016 / BATCH-06) ────────────────
        _orchestrationBus    = new FdpEventBus();
        _heartbeatReader     = new DdsReader<NodeHeartbeat>(_participant);
        _sysOpRequestReader  = new DdsReader<ClusterOpRequest>(_participant);
        _sysOpStatusWriter   = new DdsWriter<ClusterOpStatus>(_participant);
        _nodeOpStatusReader  = new DdsReader<NodeOpStatus>(_participant);
        _clusterMaster       = new ClusterMaster(_orchestrationBus, _config);
        _clusterOpTranslator = new Hrot.Orchestrator.Translators.ClusterOpMasterTranslator(
            _sysOpRequestReader, _sysOpStatusWriter, _orchestrationBus,
            unhandledRequestCallback: _clusterMaster.HandleClusterOpRequest);
        _nodeOpTranslator    = new Hrot.Orchestrator.Translators.NodeOpMasterTranslator(
            nodeId => new DdsWriter<NodeOpCommand>(_participant), _nodeOpStatusReader, _orchestrationBus);

        // DdsIdAllocatorServer: bus-mode ClusterMaster doesn't create this internally,
        // so OrchestratorSubsystem owns it (SimHost needs it to allocate entity IDs).
        _idAllocatorServer = new DdsIdAllocatorServer(_participant);
        _idServerCts       = new System.Threading.CancellationTokenSource();
        _idServerThread    = new System.Threading.Thread(() =>
        {
            while (!_idServerCts.IsCancellationRequested)
            {
                _idAllocatorServer?.ProcessRequests();
                System.Threading.Thread.Sleep(1);
            }
        }) { IsBackground = true, Name = "Orchestrator-IdAllocServer" };
        _idServerThread.Start();

        // ── Time controller setup (CGF1-A.1, BATCH-09) ─────────────────────
        // MasterSyncController replaces the minimal kernel + DistributedTimeCoordinator.
        // Must be created before _uiCache so it can be injected for smooth sim-time display.
        _eventBus          = new FdpEventBus();
        _masterSync        = new FDP.Toolkit.Time.Controllers.MasterSyncController(
            _eventBus, new HashSet<int>(), FDP.Toolkit.Time.Controllers.TimeConfig.Default);
        // MasterSyncController constructor publishes the initial SwitchTimeModeEvent{Continuous}
        // to _eventBus PENDING.  Swap it to CURRENT now so the first frame's ScanAndPublish
        // can read it and forward it to DDS before slaves (IG, ExCon) start their kernels.
        // Without this swap the event is destroyed by the two SwapBuffers calls in Update()
        // before ScanAndPublish ever gets a chance to read it, causing IG/ExCon to miss the
        // authoritative startup baseline and run ~180 ms behind Orch/SimHost.
        _eventBus.SwapBuffers();
        _timeModeTranslator  = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus);
        _lockstepTranslator  = TimeNetworkModule.CreateMasterLockstepTranslator(_participant, _eventBus);
        _masterTimeSyncTranslator = TimeNetworkModule.CreateMasterTimeSyncTranslator(_participant);

        _uiCache       = new ClusterUiCache(_orchestrationBus!, _masterSync);
        _scenarioPanel = new ClusterScenarioPanel(_clusterMaster, _uiCache);

        // S0503: Subscribe to time-control events from ClusterMaster.
        _clusterMaster.TimeControlRequested += (op, payload) =>
        {
            switch (op)
            {
                case ClusterOpType.PauseTime:
                    // Only simulation-kernel nodes (SimHost, IG, CGF) participate in
                    // lockstep ACK. ExCon has no kernel and never sends FrameAck, so it
                    // must be excluded to prevent Step() from blocking indefinitely.
                    var ids = _clusterMaster.NodeRoster.ActiveNodes
                        .Where(kv => kv.Value.SubsystemName is "SimHost" or "IG" or "CGF")
                        .Select(kv => kv.Key)
                        .ToHashSet();
                    _masterSync?.SwitchToDeterministic(ids);
                    _isPaused = true;
                    break;
                case ClusterOpType.ResumeTime:
                    _masterSync?.SwitchToContinuous();
                    _isPaused = false;
                    break;
                case ClusterOpType.StepTime:
                    _masterSync?.Step(ParseStepDelta(payload, 1f / 60f));
                    break;
                case ClusterOpType.SetTimeScale:
                    if (float.TryParse(payload,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out float s))
                        _masterSync?.SetTimeScale(s);
                    break;
            }
        };

        // CGF1-S0307: Create the global-context handler, subscribe to OnContextLoaded so the
        // MasterTimeController is seeded with the scenario's saved timeline on every load, and
        // register it with ClusterMaster so CommitState fan-outs trigger the local load path.
        var contextHandler = new GlobalContextClusterOpHandler(_participant, string.Empty);
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

    public void Update(float deltaTime)
    {
        // Advance the master sync controller's wall clock and state machine.
        _masterSync?.Update();

        // ── Time-mode and lockstep DDS egress/ingress first, reading events ──────
        // ── written at the end of the PREVIOUS frame (before clearing the read ───
        // ── buffer). _lockstepTranslator.ScanAndPublish MUST run here (before ────
        // ── the first swap) because AdvanceFrameIntent is a managed event: it is ──
        // ── published via MasterSyncController.Step() in _clusterMaster.Tick() ───
        // ── (which fires AFTER step 4's first swap), so it lands in the managed ──
        // ── write buffer, and the second swap exposes it in managed_read for the ──
        // ── NEXT frame. The FIRST swap of that next frame would CLEAR it before ───
        // ── ScanAndPublish could read it. Running ScanAndPublish here (before first
        // ── swap) reads managed_read that still contains AdvanceFrameIntent. ──────
        _timeModeTranslator?.ScanAndPublish(null!);
        _timeModeTranslator?.PollIngress(null!, null!);
        _lockstepTranslator?.ScanAndPublish(null!);
        _lockstepTranslator?.PollIngress(null!, null!);

        // ── Swap event bus (clear previous frame's read buffer). ─────────────────
        _eventBus?.SwapBuffers();

        // ── Orchestration bus pipeline (CMC-S016 / BATCH-06) ──────────────────
        // 1. Bridge DDS NodeHeartbeat → NodeHeartbeatEvent on orchestration bus so
        //    ClusterMaster (bus mode) can update its roster.
        if (_orchestrationBus != null && _heartbeatReader != null)
        {
            using var hbScope = _heartbeatReader.Take();
            foreach (var sample in hbScope)
            {
                if (!sample.IsValid) continue;
                _orchestrationBus.PublishManaged(new FDP.Toolkit.Orchestration.NodeHeartbeatEvent
                {
                    NodeId        = sample.Data.NodeId,
                    LocalStateId  = (int)sample.Data.LocalClusterState,
                    WallTicksUtc  = sample.Data.WallTicksUtc,
                    SubsystemName = sample.Data.SubsystemName ?? string.Empty,
                });
            }
        }

        // 2. Swap orchestration bus: publish → read buffer for this frame's consumers.
        _orchestrationBus?.SwapBuffers();

        // 3. Translators tick BEFORE ClusterMaster so ingress (DDS→bus) is processed first.
        _clusterOpTranslator?.Tick();  // DDS ClusterOpRequest → bus TransitionStateIntent etc.
        _nodeOpTranslator?.Tick();     // bus ExecuteNodeOpIntent → DDS NodeOpCommand;
                                       // DDS NodeOpStatus → bus NodeOpCompletedEvent

        _clusterMaster?.Tick();

        // CGF1-S0506: Update cache after ClusterMaster tick so it reflects latest DDS state.
        _uiCache?.Update();

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

        // Time sync master ingress (NTP responses from slaves).
        _masterTimeSyncTranslator?.PollIngress(null!, null!);

        // ── Final event bus swap: expose events written this frame (e.g. by ──────
        // ── SwitchToDeterministic, Step) to the read buffer so that the next ─────
        // ── frame's ScanAndPublish can read and forward them to DDS. ───────────
        _eventBus?.SwapBuffers();

        // S0503: Advance seek debounce.
        _scenarioPanel?.Update(deltaTime);
    }

    public void DrawWorld() { }

    public void DrawUI() { /* panels registered as ManagedWindows via IWindowRegistrar */ }

    /// <inheritdoc/>
    public void RegisterWindows(FDP.Toolkit.ImGui.WindowManager.WindowManager windowManager)
    {
        if (_scenarioPanel == null) return;
        windowManager.RegisterWindow(new OrchestratorWindow(_scenarioPanel));
    }

    public void Shutdown()
    {
        _scenarioPanel = null;
        _uiCache?.Dispose();
        _uiCache = null;
        _clusterOpTranslator = null;
        _nodeOpTranslator = null;
        _sysOpRequestReader?.Dispose();
        _sysOpRequestReader = null;
        _sysOpStatusWriter?.Dispose();
        _sysOpStatusWriter = null;
        _nodeOpStatusReader?.Dispose();
        _nodeOpStatusReader = null;
        _heartbeatReader?.Dispose();
        _heartbeatReader = null;
        _idServerCts?.Cancel();
        _idServerThread?.Join(System.TimeSpan.FromSeconds(2));
        _idServerCts?.Dispose();
        _idServerCts = null;
        _idServerThread = null;
        _idAllocatorServer?.Dispose();
        _idAllocatorServer = null;
        _orchestrationBus = null;
        _clusterMaster?.Dispose();
        _clusterMaster = null;
        _masterSync?.Dispose();
        _masterSync = null;
        _lockstepTranslator = null;
        _masterTimeSyncTranslator = null;
        _eventBus = null;
        _lastProcessedTimeMode = null;
        _participant?.Dispose();
        _participant = null;
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
