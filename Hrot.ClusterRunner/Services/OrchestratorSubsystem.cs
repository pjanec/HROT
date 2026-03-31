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

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Hosts <see cref="ClusterMaster"/> (DDS control plane + ID allocator server) under the Runner process.
/// Bypasses <c>WaitingRoomCoordinator</c> — boots instantly; UI renders immediately with a banner while
/// mandatory nodes are not yet ready (CGF1-S0105).
/// </summary>
public sealed class OrchestratorSubsystem : ISubsystem
{
    private DdsParticipant? _participant;
    private ClusterMaster? _clusterMaster;
    private ClusterConfiguration _config = ClusterConfiguration.Default;
    private ClusterUiCache?        _uiCache;
    private ClusterScenarioPanel?  _scenarioPanel;

    private bool _isPaused;   // S0503: toggled by TimeControlRequested handler

    // ── Time coordinator (CGF1-A.1, BATCH-09) ─────────────────────────────
    // Minimal kernel + coordinator so PendingTimeMode drives SwitchToDeterministic.
    private FdpEventBus? _eventBus;
    private EntityRepository? _timeWorld;
    private ModuleHostKernel? _timeKernel;
    private DistributedTimeCoordinator? _timeCoordinator;
    private IDescriptorTranslator? _timeModeTranslator;
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

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);  // S0501: beige

    public void Initialize(SubsystemConfig config)
    {
        _config      = ClusterConfiguration.LoadFrom(
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));
        _participant = HrotEnvironment.CreateParticipant(config.DomainId);
        _clusterMaster = new ClusterMaster(_participant, _config);
        _uiCache       = new ClusterUiCache(_participant);
        _scenarioPanel = new ClusterScenarioPanel(_clusterMaster, _uiCache);

        // S0503: Subscribe to time-control events from ClusterMaster.
        _clusterMaster.TimeControlRequested += (op, payload) =>
        {
            switch (op)
            {
                case ClusterOpType.PauseTime:
                    var ids = new HashSet<int>(_clusterMaster.NodeRoster.ActiveNodes.Keys);
                    _timeCoordinator?.SwitchToDeterministic(ids);
                    _isPaused = true;
                    break;
                case ClusterOpType.ResumeTime:
                    _timeCoordinator?.SwitchToContinuous();
                    _isPaused = false;
                    break;
                case ClusterOpType.StepTime:
                    try { _timeKernel?.StepFrame(1f / 60f); }
                    catch (InvalidOperationException) { /* MasterTimeController does not support stepping; step deferred */ }
                    break;
                case ClusterOpType.SetTimeScale:
                    if (float.TryParse(payload,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out float s))
                        _timeKernel?.GetTimeController()?.SetTimeScale(s);
                    break;
            }
        };

        // ── Time coordinator setup (CGF1-A.1, BATCH-09) ──────────────────────
        // A minimal ECS kernel gives the DistributedTimeCoordinator a wall-clock source
        // (MasterTimeController.GetCurrentState().TotalWallTicks) without needing a full
        // simulation world on the orchestrator process.
        _eventBus  = new FdpEventBus();
        _timeWorld = new EntityRepository();
        var accumulator = new EventAccumulator();
        _timeKernel = new ModuleHostKernel(_timeWorld, accumulator);
        var timeConfig = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master };
        var timeCtrl   = TimeControllerFactory.Create(_eventBus, timeConfig);
        _timeKernel.SetTimeController(timeCtrl);
        _timeKernel.Initialize();

        var coordConfig = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master,
            SyncConfig = TimeConfig.Default };
        _timeCoordinator   = new DistributedTimeCoordinator(_eventBus, _timeKernel, coordConfig,
            new HashSet<int>());
        _timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus);

        // CGF1-S0307: Create the global-context handler, subscribe to OnContextLoaded so the
        // MasterTimeController is seeded with the scenario's saved timeline on every load, and
        // register it with ClusterMaster so CommitState fan-outs trigger the local load path.
        var contextHandler = new GlobalContextClusterOpHandler(_participant, string.Empty);
        contextHandler.OnContextLoaded += (startTicks, simTimeSeconds) =>
        {
            if (_timeKernel != null)
            {
                var timeCtrl = _timeKernel.GetTimeController();
                timeCtrl.SeedState(new GlobalTime
                {
                    TotalWallTicks    = startTicks,
                    TotalTime         = simTimeSeconds,
                    UnscaledTotalTime = simTimeSeconds,
                    TimeScale         = timeCtrl.GetTimeScale(),
                });
                FdpLog<OrchestratorSubsystem>.Info(
                    "[Orchestrator] Seeded MasterTimeController: WallTicks={0}, SimTime={1:F1}s",
                    startTicks, simTimeSeconds);
            }
        };
        _clusterMaster.SetGlobalContextHandler(contextHandler);
    }

    public void Update(float deltaTime)
    {
        // Advance the orchestrator's wall clock so the coordinator has a monotonic
        // TotalWallTicks reference for future-barrier calculations.
        _timeKernel?.Update();
        _eventBus?.SwapBuffers();

        _clusterMaster?.Tick();

        // CGF1-S0506: Update cache after ClusterMaster tick so it reflects latest DDS state.
        _uiCache?.Update();

        // CGF1-A.1: Consume PendingTimeMode and drive DistributedTimeCoordinator.
        var pendingMode = _clusterMaster?.PendingTimeMode;
        if (pendingMode != _lastProcessedTimeMode)
        {
            if (pendingMode == "Deterministic" && _timeCoordinator != null && _clusterMaster != null)
            {
                // Collect current roster IDs as slave targets for the barrier broadcast.
                var slaveIds = new HashSet<int>(_clusterMaster.NodeRoster.ActiveNodes.Keys);
                _timeCoordinator.SwitchToDeterministic(slaveIds);
            }
            _lastProcessedTimeMode = pendingMode;
        }

        // Poll coordinator barrier and egress/ingress translate SwitchTimeModeEvent to DDS.
        _timeCoordinator?.Update();
        _timeModeTranslator?.ScanAndPublish(null!);
        _timeModeTranslator?.PollIngress(null!, null!);

        // S0503: Advance seek debounce.
        _scenarioPanel?.Update(deltaTime);
    }

    public void DrawWorld() { }

    public void DrawUI()
    {
        if (_clusterMaster == null || _scenarioPanel == null) return;
        if (!ImGui.Begin("Orchestrator")) { ImGui.End(); return; }   // S0501

        _scenarioPanel.Render();

        ImGui.End();   // S0501
    }

    public void Shutdown()
    {
        _scenarioPanel = null;
        _uiCache?.Dispose();
        _uiCache = null;
        _clusterMaster?.Dispose();
        _clusterMaster = null;
        _timeKernel?.Dispose();
        _timeKernel = null;
        _timeWorld = null;
        _eventBus = null;
        _timeCoordinator = null;
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
}
