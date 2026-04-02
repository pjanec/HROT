using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Map.Common;
using Hrot.Orchestrator;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Framework.Runner;
using FDP.Kernel.Logging;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Controllers;
using ImGuiNET;
using ModuleHost.Core;
using ModuleHost.Core.Time;
using Hrot.ClusterRunner.Windows;

namespace Hrot.ClusterRunner.Services;

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
    private DdsWriter<ClusterOpRequest>? _sysOpWriter;  // S0502

    private bool _isPaused;   // S0503: toggled by TimeControlRequested handler

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
        _clusterMaster = new ClusterMaster(_participant, _config);
        _sysOpWriter   = new DdsWriter<ClusterOpRequest>(_participant);    // S0502

        // ── Time controller setup (CGF1-A.1, BATCH-09) ─────────────────────
        // MasterSyncController replaces the minimal kernel + DistributedTimeCoordinator.
        // Must be created before _uiCache so it can be injected for smooth sim-time display.
        _eventBus          = new FdpEventBus();
        _masterSync        = new FDP.Toolkit.Time.Controllers.MasterSyncController(
            _eventBus, new HashSet<int>(), FDP.Toolkit.Time.Controllers.TimeConfig.Default);
        _timeModeTranslator  = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus);
        _lockstepTranslator  = TimeNetworkModule.CreateMasterLockstepTranslator(_participant, _eventBus);
        _masterTimeSyncTranslator = TimeNetworkModule.CreateMasterTimeSyncTranslator(_participant);

        _uiCache       = new ClusterUiCache(_participant, _masterSync);
        _scenarioPanel = new ClusterScenarioPanel(_sysOpWriter, _uiCache);

        // S0503: Subscribe to time-control events from ClusterMaster.
        _clusterMaster.TimeControlRequested += (op, payload) =>
        {
            switch (op)
            {
                case ClusterOpType.PauseTime:
                    var ids = new HashSet<int>(_clusterMaster.NodeRoster.ActiveNodes.Keys);
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
        _eventBus?.SwapBuffers();

        _clusterMaster?.Tick();

        // CGF1-S0506: Update cache after ClusterMaster tick so it reflects latest DDS state.
        _uiCache?.Update();

        // CGF1-A.1: Consume PendingTimeMode and drive MasterSyncController.
        var pendingMode = _clusterMaster?.PendingTimeMode;
        if (pendingMode != _lastProcessedTimeMode)
        {
            if (pendingMode == "Deterministic" && _masterSync != null && _clusterMaster != null)
            {
                var slaveIds = new HashSet<int>(_clusterMaster.NodeRoster.ActiveNodes.Keys);
                _masterSync.SwitchToDeterministic(slaveIds);
            }
            _lastProcessedTimeMode = pendingMode;
        }

        // Translate lockstep frames, time sync, and mode switches to/from DDS.
        _lockstepTranslator?.ScanAndPublish(null!);
        _lockstepTranslator?.PollIngress(null!, null!);
        _masterTimeSyncTranslator?.PollIngress(null!, null!);
        _timeModeTranslator?.ScanAndPublish(null!);
        _timeModeTranslator?.PollIngress(null!, null!);

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
        _sysOpWriter?.Dispose();     // S0502
        _sysOpWriter = null;
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
