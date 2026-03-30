using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Map.Common;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Framework.Runner;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Controllers;
using ImGuiNET;
using ModuleHost.Core;
using ModuleHost.Core.Time;

namespace Bagira.Runner.Services;

/// <summary>
/// Hosts <see cref="DrillMaster"/> (DDS control plane + ID allocator server) under the Runner process.
/// Bypasses <c>WaitingRoomCoordinator</c> — boots instantly; UI renders immediately with a banner while
/// mandatory nodes are not yet ready (CGF1-S0105).
/// </summary>
public sealed class OrchestratorSubsystem : ISubsystem
{
    private DdsParticipant? _participant;
    private DrillMaster? _drillMaster;
    private ClusterConfiguration _config = ClusterConfiguration.Default;
    private ClusterUiCache?        _uiCache;
    private ClusterScenarioPanel?  _scenarioPanel;
    private DdsWriter<SysOpRequest>? _sysOpWriter;  // S0502

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
    /// Internal test hook: exposes the <see cref="DrillMaster"/> hosted by this subsystem so
    /// E2E test fixtures can inject <see cref="SysOpRequest"/> values via
    /// <see cref="DrillMaster.HandleSysOpRequest"/> and read cluster state.
    /// </summary>
    internal DrillMaster? TestHook_DrillMaster => _drillMaster;

    /// <summary>Internal test hook: exposes current pause state for assertions.</summary>
    internal bool IsPausedForTest => _isPaused;

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);  // S0501: beige

    public void Initialize(SubsystemConfig config)
    {
        _config      = ClusterConfiguration.LoadFrom(
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));
        _participant = BagiraEnvironment.CreateParticipant(config.DomainId);
        _drillMaster = new DrillMaster(_participant, _config);
        _sysOpWriter   = new DdsWriter<SysOpRequest>(_participant);    // S0502
        _uiCache       = new ClusterUiCache(_participant);
        _scenarioPanel = new ClusterScenarioPanel(_sysOpWriter, _uiCache);

        // S0503: Subscribe to time-control events from DrillMaster.
        _drillMaster.TimeControlRequested += (op, payload) =>
        {
            switch (op)
            {
                case SysOpType.PauseTime:
                    var ids = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
                    _timeCoordinator?.SwitchToDeterministic(ids);
                    _isPaused = true;
                    break;
                case SysOpType.ResumeTime:
                    _timeCoordinator?.SwitchToContinuous();
                    _isPaused = false;
                    break;
                case SysOpType.StepTime:
                    try { _timeKernel?.StepFrame(1f / 60f); }
                    catch (InvalidOperationException) { /* MasterTimeController does not support stepping; step deferred */ }
                    break;
                case SysOpType.SetTimeScale:
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
    }

    public void Update(float deltaTime)
    {
        // Advance the orchestrator's wall clock so the coordinator has a monotonic
        // TotalWallTicks reference for future-barrier calculations.
        _timeKernel?.Update();
        _eventBus?.SwapBuffers();

        _drillMaster?.Tick();

        // CGF1-S0506: Update cache after DrillMaster tick so it reflects latest DDS state.
        _uiCache?.Update();

        // CGF1-A.1: Consume PendingTimeMode and drive DistributedTimeCoordinator.
        var pendingMode = _drillMaster?.PendingTimeMode;
        if (pendingMode != _lastProcessedTimeMode)
        {
            if (pendingMode == "Deterministic" && _timeCoordinator != null && _drillMaster != null)
            {
                // Collect current roster IDs as slave targets for the barrier broadcast.
                var slaveIds = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
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
        if (_uiCache == null) return;
        if (!ImGui.Begin("Orchestrator")) { ImGui.End(); return; }   // S0501

        bool disableAll = !_uiCache.IsBootstrapped || _uiCache.HasInFlightTransaction;

        _scenarioPanel?.Render(_uiCache, disableAll);

        ImGui.End();   // S0501
    }

    public void Shutdown()
    {
        _scenarioPanel = null;
        _uiCache?.Dispose();
        _uiCache = null;
        _sysOpWriter?.Dispose();     // S0502
        _sysOpWriter = null;
        _drillMaster?.Dispose();
        _drillMaster = null;
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
